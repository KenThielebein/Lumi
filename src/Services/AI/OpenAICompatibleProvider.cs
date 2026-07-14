using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Lumi.Services.AI
{
    public class OpenAICompatibleProvider : IAIProvider, IDisposable
    {
        private readonly HttpClient _http;
        private readonly string     _model;
        private readonly string     _providerName;

        public string ProviderName => _providerName;
        public string Model        => _model;

        public OpenAICompatibleProvider(string baseUrl, string apiKey, string model, string providerName)
        {
            _model        = model;
            _providerName = providerName;

            _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(120) };
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        public async Task<string> CompleteAsync(string userMessage, string? systemPrompt = null)
        {
            var messages = new List<ChatMessage>();
            if (!string.IsNullOrWhiteSpace(systemPrompt))
                messages.Add(new ChatMessage { Role = "system", Content = systemPrompt });
            messages.Add(new ChatMessage { Role = "user", Content = userMessage });
            return await CompleteWithContextAsync(messages, null);
        }

        public async Task<string> CompleteWithContextAsync(List<ChatMessage> history, string? systemPrompt = null)
        {
            var messages = new List<object>();
            if (!string.IsNullOrWhiteSpace(systemPrompt))
                messages.Add(new { role = "system", content = systemPrompt });
            foreach (var m in history)
                messages.Add(new { role = m.Role, content = m.Content });

            var body = new { model = _model, messages };
            var response = await _http.PostAsJsonAsync("chat/completions", body);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                throw new Exception($"{_providerName} Fehler {(int)response.StatusCode}: {err}");
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";
        }

        public void Dispose() => _http.Dispose();
    }
}
