using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Lumi.Services.Speech
{
    public class GroqSttClient : ISpeechToText, IDisposable
    {
        private readonly HttpClient _http;
        private readonly Func<string>? _promptProvider;

        public string ProviderName => "Groq";

        public GroqSttClient(string apiKey, Func<string>? promptProvider = null)
        {
            _promptProvider = promptProvider;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);
        }

        public async Task<string> TranscribeAsync(byte[] wavData, string language = "de")
        {
            if (wavData.Length == 0) return "";

            using var content = new MultipartFormDataContent();
            var audioContent = new ByteArrayContent(wavData);
            audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            content.Add(audioContent, "file", "audio.wav");
            content.Add(new StringContent("whisper-large-v3-turbo"), "model");
            content.Add(new StringContent(language), "language");
            content.Add(new StringContent("text"), "response_format");
            var prompt = _promptProvider?.Invoke();
            if (!string.IsNullOrWhiteSpace(prompt))
                content.Add(new StringContent(prompt), "prompt");

            var response = await _http.PostAsync(
                "https://api.groq.com/openai/v1/audio/transcriptions", content);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                throw new Exception($"Groq STT Fehler {(int)response.StatusCode}: {err}");
            }

            return (await response.Content.ReadAsStringAsync()).Trim();
        }

        public void Dispose() => _http.Dispose();
    }
}
