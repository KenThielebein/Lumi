using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Lumi.Config;

namespace Lumi.Services.Memory
{
    public class MemoryService
    {
        private readonly AppConfig _config;
        private readonly GitHubMemoryClient _github;
        private MemoryDocument _document = new();

        public static readonly string MemoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Lumi", "memory.json");

        public MemoryService(AppConfig config)
        {
            _config = config;
            _github = new GitHubMemoryClient(config);
            LoadLocal();
        }

        public bool Enabled => _config.EnableMemory;

        public async Task InitializeAsync()
        {
            if (!_github.IsConfigured) return;

            try
            {
                var remoteJson = await _github.DownloadAsync();
                if (string.IsNullOrWhiteSpace(remoteJson)) return;

                var remote = JsonSerializer.Deserialize<MemoryDocument>(remoteJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (remote == null) return;

                Normalize(remote);
                var localWasNewer = _document.UpdatedAt > remote.UpdatedAt;
                var changed = MergeFrom(remote);
                if (changed || remote.UpdatedAt > _document.UpdatedAt)
                    SaveLocal();
                if (changed || localWasNewer)
                    await SaveRemoteAsync();
            }
            catch
            {
                // GitHub-Sync ist optional; lokales Gedächtnis bleibt nutzbar.
            }
        }

        public string BuildPromptContext()
        {
            if (!Enabled || _document.Facts.Count == 0) return "";

            var facts = string.Join("\n", _document.Facts.Select(f => "- " + f.Text));
            return "Langzeitgedaechtnis ueber den Nutzer:\n" + facts;
        }

        public string BuildVocabularyPrompt()
        {
            var words = _document.Vocabulary
                .Where(v => !string.IsNullOrWhiteSpace(v.Written))
                .OrderByDescending(v => v.UpdatedAt)
                .Select(v => v.Written.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase);

            const int maxCharacters = 760; // sicher unter Groqs 224-Token-Limit
            var prompt = "Bevorzugte Schreibweisen und Eigennamen: ";
            foreach (var word in words)
            {
                var addition = (prompt.EndsWith(": ", StringComparison.Ordinal) ? "" : ", ") + word;
                if (prompt.Length + addition.Length > maxCharacters) break;
                prompt += addition;
            }

            return prompt.EndsWith(": ", StringComparison.Ordinal) ? "" : prompt + ".";
        }

        public string ApplyVocabulary(string transcript)
        {
            if (string.IsNullOrWhiteSpace(transcript)) return transcript;

            var result = transcript;
            var replacements = _document.Vocabulary
                .SelectMany(entry => entry.HeardAs.Select(heard => new
                {
                    Heard = heard?.Trim() ?? "",
                    Written = entry.Written.Trim()
                }))
                .Where(item => item.Heard.Length > 0 && item.Written.Length > 0)
                .OrderByDescending(item => item.Heard.Length);

            foreach (var item in replacements)
            {
                var pattern = $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(item.Heard)}(?![\p{{L}}\p{{N}}])";
                result = Regex.Replace(result, pattern, item.Written,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            return result;
        }

        public IReadOnlyList<VocabularyEntry> GetVocabulary() =>
            _document.Vocabulary
                .OrderBy(v => v.Written, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        public async Task<bool> AddVocabularyAsync(string heardAs, string written)
        {
            heardAs = heardAs.Trim();
            written = written.Trim();
            if (heardAs.Length == 0 || written.Length == 0 ||
                string.Equals(heardAs, written, StringComparison.Ordinal))
                return false;

            var entry = _document.Vocabulary.FirstOrDefault(v =>
                string.Equals(v.Written, written, StringComparison.OrdinalIgnoreCase));

            if (entry == null)
            {
                entry = new VocabularyEntry { Written = written };
                _document.Vocabulary.Add(entry);
            }

            if (!entry.HeardAs.Any(value =>
                    string.Equals(value, heardAs, StringComparison.OrdinalIgnoreCase)))
                entry.HeardAs.Add(heardAs);

            entry.UpdatedAt = DateTimeOffset.UtcNow;
            _document.Version = 2;
            _document.UpdatedAt = entry.UpdatedAt;
            SaveLocal();
            await SaveRemoteAsync();
            return true;
        }

        public async Task ReplaceVocabularyAsync(IEnumerable<VocabularyEntry> entries)
        {
            _document.Vocabulary = entries
                .Where(v => !string.IsNullOrWhiteSpace(v.Written))
                .Select(v => new VocabularyEntry
                {
                    Written = v.Written.Trim(),
                    HeardAs = v.HeardAs
                        .Where(h => !string.IsNullOrWhiteSpace(h))
                        .Select(h => h.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    UpdatedAt = DateTimeOffset.UtcNow
                })
                .ToList();
            _document.Version = 2;
            _document.UpdatedAt = DateTimeOffset.UtcNow;
            SaveLocal();
            await SaveRemoteAsync();
        }

        public string BuildDisplayText()
        {
            if (!Enabled)
                return "Das Langzeit-Gedaechtnis ist in den Einstellungen deaktiviert.";

            if (_document.Facts.Count == 0)
                return "Im Langzeit-Gedaechtnis stehen noch keine Fakten.";

            return "Langzeit-Gedaechtnis:\n" +
                   string.Join("\n", _document.Facts.Select((fact, index) => $"{index + 1}. {fact.Text}"));
        }

        public async Task<bool> AddFactAsync(string factText)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(factText)) return false;

            var normalized = factText.Trim();
            var exists = _document.Facts.Any(f =>
                string.Equals(f.Text.Trim(), normalized, StringComparison.OrdinalIgnoreCase));

            if (!exists)
            {
                _document.Facts.Add(new MemoryFact
                {
                    Text = normalized,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }

            _document.UpdatedAt = DateTimeOffset.UtcNow;
            SaveLocal();
            await SaveRemoteAsync();
            return true;
        }

        private void LoadLocal()
        {
            if (!File.Exists(MemoryPath)) return;

            try
            {
                var json = File.ReadAllText(MemoryPath);
                _document = JsonSerializer.Deserialize<MemoryDocument>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new MemoryDocument();
                Normalize(_document);
            }
            catch
            {
                _document = new MemoryDocument();
            }
        }

        private bool MergeFrom(MemoryDocument remote)
        {
            var changed = false;

            foreach (var remoteFact in remote.Facts)
            {
                if (_document.Facts.Any(f =>
                        string.Equals(f.Text.Trim(), remoteFact.Text.Trim(), StringComparison.OrdinalIgnoreCase)))
                    continue;
                _document.Facts.Add(remoteFact);
                changed = true;
            }

            foreach (var remoteEntry in remote.Vocabulary)
            {
                var local = _document.Vocabulary.FirstOrDefault(v =>
                    string.Equals(v.Written, remoteEntry.Written, StringComparison.OrdinalIgnoreCase));
                if (local == null)
                {
                    _document.Vocabulary.Add(remoteEntry);
                    changed = true;
                    continue;
                }

                foreach (var heard in remoteEntry.HeardAs)
                {
                    if (local.HeardAs.Any(h =>
                            string.Equals(h, heard, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    local.HeardAs.Add(heard);
                    changed = true;
                }
                if (remoteEntry.UpdatedAt > local.UpdatedAt)
                    local.UpdatedAt = remoteEntry.UpdatedAt;
            }

            _document.Version = 2;
            _document.UpdatedAt = new[] { _document.UpdatedAt, remote.UpdatedAt }.Max();
            return changed;
        }

        private static void Normalize(MemoryDocument document)
        {
            document.Facts ??= new List<MemoryFact>();
            document.Vocabulary ??= new List<VocabularyEntry>();
            foreach (var entry in document.Vocabulary)
                entry.HeardAs ??= new List<string>();
            document.Version = Math.Max(2, document.Version);
        }

        private void SaveLocal()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MemoryPath)!);
            var json = JsonSerializer.Serialize(_document, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(MemoryPath, json);
        }

        private async Task SaveRemoteAsync()
        {
            if (!_github.IsConfigured) return;

            try
            {
                var json = JsonSerializer.Serialize(_document, new JsonSerializerOptions { WriteIndented = true });
                await _github.UploadAsync(json);
            }
            catch
            {
                // Das lokale Wörterbuch bleibt auch bei fehlender GitHub-Verbindung nutzbar.
            }
        }

    }
}
