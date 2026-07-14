using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Lumi.Config;

namespace Lumi.Services.Memory
{
    public class GitHubMemoryClient
    {
        private readonly AppConfig _config;

        public GitHubMemoryClient(AppConfig config)
        {
            _config = config;
        }

        public bool IsConfigured =>
            _config.GitHubMemorySyncEnabled &&
            !string.IsNullOrWhiteSpace(_config.GitHubMemoryToken) &&
            !string.IsNullOrWhiteSpace(_config.GitHubMemoryOwner) &&
            !string.IsNullOrWhiteSpace(_config.GitHubMemoryRepo) &&
            !string.IsNullOrWhiteSpace(_config.GitHubMemoryPath);

        public async Task<string?> DownloadAsync()
        {
            if (!IsConfigured) return null;

            using var http = CreateClient();
            var url = BuildContentsUrl(includeRef: true);
            var response = await http.GetAsync(url);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();

            var file = await response.Content.ReadFromJsonAsync<GitHubFileResponse>();
            if (file?.Content == null) return null;

            var base64 = file.Content.Replace("\n", "").Replace("\r", "");
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }

        public async Task<GitHubMemoryConnectionResult> TestConnectionAsync()
        {
            if (!IsConfigured)
                return GitHubMemoryConnectionResult.Fail("Bitte GitHub-Sync, Token, Owner, Repo und JSON-Pfad ausfuellen.");

            try
            {
                using var http = CreateClient();
                var repoResult = await EnsureRepositoryAccessibleAsync(http);
                if (!repoResult.IsConnected) return repoResult;

                var branchResult = await EnsureBranchAccessibleAsync(http);
                if (!branchResult.IsConnected) return branchResult;

                var url = BuildContentsUrl(includeRef: true);
                var response = await http.GetAsync(url);

                if (response.IsSuccessStatusCode)
                    return GitHubMemoryConnectionResult.Ok("Verbunden");

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    var uploadResult = await TryUploadAsync("{\n  \"version\": 2,\n  \"updatedAt\": \"" +
                                      DateTimeOffset.UtcNow.ToString("O") +
                                      "\",\n  \"facts\": [],\n  \"vocabulary\": []\n}\n");
                    return uploadResult.IsConnected
                        ? GitHubMemoryConnectionResult.Ok("Verbunden, Datei erstellt")
                        : uploadResult;
                }

                var detail = await response.Content.ReadAsStringAsync();
                return GitHubMemoryConnectionResult.Fail($"GitHub {(int)response.StatusCode}: {detail}");
            }
            catch (Exception ex)
            {
                return GitHubMemoryConnectionResult.Fail(ex.Message);
            }
        }

        private async Task<GitHubMemoryConnectionResult> EnsureRepositoryAccessibleAsync(HttpClient http)
        {
            var response = await http.GetAsync(BuildRepositoryUrl());
            if (response.IsSuccessStatusCode)
                return GitHubMemoryConnectionResult.Ok("Repository erreichbar");

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return GitHubMemoryConnectionResult.Fail(
                    "Repository nicht gefunden oder Token hat keinen Zugriff. Pruefe Owner, Repository-Name und ob der Fine-grained Token genau dieses Repo auswaehlt.");
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return GitHubMemoryConnectionResult.Fail(
                    "GitHub-Token wurde abgelehnt. Pruefe, ob der Token gueltig ist und Contents: Read and write besitzt.");
            }

            var detail = await response.Content.ReadAsStringAsync();
            return GitHubMemoryConnectionResult.Fail($"GitHub Repo-Test {(int)response.StatusCode}: {detail}");
        }

        private async Task<GitHubMemoryConnectionResult> EnsureBranchAccessibleAsync(HttpClient http)
        {
            var response = await http.GetAsync(BuildBranchUrl());
            if (response.IsSuccessStatusCode)
                return GitHubMemoryConnectionResult.Ok("Branch erreichbar");

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return GitHubMemoryConnectionResult.Fail(
                    "Branch nicht gefunden. Pruefe, ob der Branch wirklich 'main' heisst und das Repository nicht leer ist.");
            }

            var detail = await response.Content.ReadAsStringAsync();
            return GitHubMemoryConnectionResult.Fail($"GitHub Branch-Test {(int)response.StatusCode}: {detail}");
        }

        private async Task<GitHubMemoryConnectionResult> TryUploadAsync(string json)
        {
            try
            {
                await UploadAsync(json);
                return GitHubMemoryConnectionResult.Ok("Upload erfolgreich");
            }
            catch (HttpRequestException ex)
            {
                return GitHubMemoryConnectionResult.Fail(
                    "Datei konnte nicht erstellt werden. Pruefe, ob der Token 'Contents: Read and write' hat. " + ex.Message);
            }
            catch (Exception ex)
            {
                return GitHubMemoryConnectionResult.Fail("Datei konnte nicht erstellt werden: " + ex.Message);
            }
        }

        public async Task UploadAsync(string json)
        {
            if (!IsConfigured) return;

            using var http = CreateClient();
            var readUrl = BuildContentsUrl(includeRef: true);
            var writeUrl = BuildContentsUrl(includeRef: false);
            var existingSha = await TryGetShaAsync(http, readUrl);

            var body = new Dictionary<string, string>
            {
                ["message"] = "Update Lumi memory",
                ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(json)),
                ["branch"] = string.IsNullOrWhiteSpace(_config.GitHubMemoryBranch) ? "main" : _config.GitHubMemoryBranch
            };

            if (!string.IsNullOrWhiteSpace(existingSha))
                body["sha"] = existingSha;

            var response = await http.PutAsJsonAsync(writeUrl, body);
            response.EnsureSuccessStatusCode();
        }

        private async Task<string?> TryGetShaAsync(HttpClient http, string url)
        {
            var response = await http.GetAsync(url);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();

            var file = await response.Content.ReadFromJsonAsync<GitHubFileResponse>();
            return file?.Sha;
        }

        private HttpClient CreateClient()
        {
            var http = new HttpClient { BaseAddress = new Uri("https://api.github.com/"), Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.GitHubMemoryToken);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Lumi");
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            return http;
        }

        private string BuildContentsUrl(bool includeRef)
        {
            var path = string.Join("/", _config.GitHubMemoryPath
                .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));

            var branch = string.IsNullOrWhiteSpace(_config.GitHubMemoryBranch)
                ? "main"
                : _config.GitHubMemoryBranch;

            var url = $"repos/{Uri.EscapeDataString(_config.GitHubMemoryOwner)}/{Uri.EscapeDataString(_config.GitHubMemoryRepo)}/contents/{path}";
            return includeRef ? url + $"?ref={Uri.EscapeDataString(branch)}" : url;
        }

        private string BuildRepositoryUrl()
        {
            return $"repos/{Uri.EscapeDataString(_config.GitHubMemoryOwner)}/{Uri.EscapeDataString(_config.GitHubMemoryRepo)}";
        }

        private string BuildBranchUrl()
        {
            var branch = string.IsNullOrWhiteSpace(_config.GitHubMemoryBranch)
                ? "main"
                : _config.GitHubMemoryBranch;

            return BuildRepositoryUrl() + $"/branches/{Uri.EscapeDataString(branch)}";
        }

        private class GitHubFileResponse
        {
            [JsonPropertyName("content")]
            public string? Content { get; set; }

            [JsonPropertyName("sha")]
            public string? Sha { get; set; }
        }
    }

    public class GitHubMemoryConnectionResult
    {
        public bool IsConnected { get; init; }
        public string Message { get; init; } = "";

        public static GitHubMemoryConnectionResult Ok(string message) => new()
        {
            IsConnected = true,
            Message = message
        };

        public static GitHubMemoryConnectionResult Fail(string message) => new()
        {
            IsConnected = false,
            Message = message
        };
    }
}
