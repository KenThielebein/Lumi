using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using Lumi.Services.Diagnostics;

namespace Lumi.Services.Speech
{
    public sealed class GroqSttClient : ISpeechToText, IDisposable
    {
        private const string TranscriptionUrl =
            "https://api.groq.com/openai/v1/audio/transcriptions";

        // Groq akzeptiert im kleinsten Tarif 25 MB. Mit 16 MB bleibt genug
        // Reserve für WAV-Header und Multipart-Overhead.
        private const int MaxChunkBytes = 16 * 1024 * 1024;
        private const int MaxAttempts = 3;
        private static readonly TimeSpan[] RequestTimeouts =
        {
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(120),
            TimeSpan.FromSeconds(180)
        };

        private readonly HttpClient _http;
        private readonly Func<string>? _promptProvider;

        public string ProviderName => "Groq";

        public GroqSttClient(string apiKey, Func<string>? promptProvider = null)
        {
            _promptProvider = promptProvider;
            _http = new HttpClient
            {
                // Das Zeitlimit wird pro Versuch gesetzt. Dadurch kann ein
                // kurzzeitiger Netzwerkfehler sauber wiederholt werden.
                Timeout = Timeout.InfiniteTimeSpan
            };
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);
        }

        public async Task<string> TranscribeAsync(byte[] wavData, string language = "de")
        {
            if (wavData.Length == 0)
                return "";

            IReadOnlyList<byte[]> chunks;
            try
            {
                chunks = SplitWavIfNeeded(wavData);
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
            {
                throw new InvalidOperationException(
                    "Die Mikrofonaufnahme war beschädigt. Bitte das Diktat erneut starten.", ex);
            }

            var prompt = _promptProvider?.Invoke();
            var transcripts = new List<string>(chunks.Count);

            for (var chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                string text;
                try
                {
                    text = await TranscribeChunkWithRetryAsync(
                        chunks[chunkIndex], language, prompt).ConfigureAwait(false);
                }
                catch (Exception ex) when (transcripts.Count > 0)
                {
                    throw new PartialTranscriptionException(
                        string.Join(" ", transcripts),
                        chunkIndex + 1,
                        chunks.Count,
                        ex);
                }

                if (!string.IsNullOrWhiteSpace(text))
                    transcripts.Add(text.Trim());
            }

            return string.Join(" ", transcripts);
        }

        private async Task<string> TranscribeChunkWithRetryAsync(
            byte[] wavData, string language, string? prompt)
        {
            Exception? lastException = null;

            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                var attemptTimer = Stopwatch.StartNew();
                try
                {
                    using var request = BuildRequest(wavData, language, prompt);
                    using var timeoutCts = new CancellationTokenSource(
                        RequestTimeouts[Math.Min(attempt - 1, RequestTimeouts.Length - 1)]);
                    using var response = await _http.SendAsync(
                        request,
                        HttpCompletionOption.ResponseContentRead,
                        timeoutCts.Token).ConfigureAwait(false);

                    var responseText = await response.Content
                        .ReadAsStringAsync(timeoutCts.Token)
                        .ConfigureAwait(false);

                    LumiDiagnostics.Write(
                        "stt_attempt",
                        ("audio_bytes", wavData.Length),
                        ("attempt", attempt),
                        ("status", (int)response.StatusCode),
                        ("elapsed_ms", attemptTimer.ElapsedMilliseconds));

                    if (response.IsSuccessStatusCode)
                        return responseText.Trim();

                    if (IsTransient(response.StatusCode) && attempt < MaxAttempts)
                    {
                        await Task.Delay(GetRetryDelay(response, attempt))
                            .ConfigureAwait(false);
                        continue;
                    }

                    throw CreateGroqException(response.StatusCode, responseText);
                }
                catch (OperationCanceledException ex)
                {
                    LumiDiagnostics.Write(
                        "stt_attempt_timeout",
                        ("audio_bytes", wavData.Length),
                        ("attempt", attempt),
                        ("elapsed_ms", attemptTimer.ElapsedMilliseconds));
                    lastException = ex;
                    if (attempt < MaxAttempts)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(attempt))
                            .ConfigureAwait(false);
                        continue;
                    }
                }
                catch (HttpRequestException ex)
                {
                    LumiDiagnostics.Write(
                        "stt_attempt_network_error",
                        ("audio_bytes", wavData.Length),
                        ("attempt", attempt),
                        ("elapsed_ms", attemptTimer.ElapsedMilliseconds),
                        ("error_type", ex.GetType().Name));
                    lastException = ex;
                    if (attempt < MaxAttempts)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(attempt))
                            .ConfigureAwait(false);
                        continue;
                    }
                }
            }

            throw new InvalidOperationException(
                "Die Transkription hat wegen einer instabilen Verbindung zu lange gedauert. " +
                "Bitte das Diktat erneut versuchen.",
                lastException);
        }

        private static HttpRequestMessage BuildRequest(
            byte[] wavData, string language, string? prompt)
        {
            var content = new MultipartFormDataContent();
            var audioContent = new ByteArrayContent(wavData);
            audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            content.Add(audioContent, "file", "audio.wav");
            content.Add(new StringContent("whisper-large-v3-turbo"), "model");
            content.Add(new StringContent(language), "language");
            content.Add(new StringContent("text"), "response_format");

            if (!string.IsNullOrWhiteSpace(prompt))
                content.Add(new StringContent(prompt), "prompt");

            return new HttpRequestMessage(HttpMethod.Post, TranscriptionUrl)
            {
                Content = content
            };
        }

        private static IReadOnlyList<byte[]> SplitWavIfNeeded(byte[] wavData)
        {
            if (wavData.Length <= MaxChunkBytes)
                return new[] { wavData };

            using var sourceStream = new MemoryStream(wavData, writable: false);
            using var reader = new WaveFileReader(sourceStream);

            var blockAlign = Math.Max(1, reader.WaveFormat.BlockAlign);
            var maxAudioBytes =
                ((MaxChunkBytes - 1024) / blockAlign) * blockAlign;
            var buffer = new byte[Math.Min(81_920, maxAudioBytes)];
            var chunks = new List<byte[]>();

            while (reader.Position < reader.Length)
            {
                using var chunkStream = new MemoryStream();
                using (var writer = new WaveFileWriter(chunkStream, reader.WaveFormat))
                {
                    var written = 0;
                    while (written < maxAudioBytes)
                    {
                        var bytesToRead = Math.Min(buffer.Length, maxAudioBytes - written);
                        var read = reader.Read(buffer, 0, bytesToRead);
                        if (read == 0)
                            break;

                        writer.Write(buffer, 0, read);
                        written += read;
                    }
                }

                var chunk = chunkStream.ToArray();
                if (chunk.Length <= 44)
                    break;
                chunks.Add(chunk);
            }

            return chunks.Count > 0 ? chunks : new[] { wavData };
        }

        private static bool IsTransient(HttpStatusCode statusCode) =>
            statusCode is
                HttpStatusCode.RequestTimeout or
                HttpStatusCode.TooManyRequests or
                HttpStatusCode.InternalServerError or
                HttpStatusCode.BadGateway or
                HttpStatusCode.ServiceUnavailable or
                HttpStatusCode.GatewayTimeout;

        private static TimeSpan GetRetryDelay(
            HttpResponseMessage response, int attempt)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta;
            if (retryAfter is { } delay && delay <= TimeSpan.FromSeconds(10))
                return delay;
            return TimeSpan.FromSeconds(attempt);
        }

        private static Exception CreateGroqException(
            HttpStatusCode statusCode, string responseText)
        {
            var detail = ExtractApiMessage(responseText);
            return statusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    new InvalidOperationException(
                        "Der Groq-API-Key wurde abgelehnt. Bitte den Key in den Lumi-Einstellungen prüfen."),
                HttpStatusCode.RequestEntityTooLarge =>
                    new InvalidOperationException(
                        "Groq hat die Audiodatei trotz Lumis Größenbegrenzung abgelehnt. " +
                        "Bitte die Aufnahme etwas kürzer wiederholen."),
                HttpStatusCode.TooManyRequests =>
                    new InvalidOperationException(
                        "Groq ist gerade ausgelastet oder das Nutzungslimit wurde erreicht. " +
                        "Bitte das Diktat in einem Moment erneut versuchen."),
                _ => new InvalidOperationException(
                    $"Groq konnte das Diktat nicht transkribieren " +
                    $"(Fehler {(int)statusCode}){FormatDetail(detail)}.")
            };
        }

        private static string ExtractApiMessage(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
                return "";

            try
            {
                using var document = JsonDocument.Parse(responseText);
                if (document.RootElement.TryGetProperty("error", out var error))
                {
                    if (error.ValueKind == JsonValueKind.String)
                        return error.GetString() ?? "";
                    if (error.TryGetProperty("message", out var message))
                        return message.GetString() ?? "";
                }
            }
            catch (JsonException)
            {
                // Nicht-JSON-Antworten werden gekürzt als Diagnose angezeigt.
            }

            return responseText.Length <= 180
                ? responseText.Trim()
                : responseText[..180].Trim() + "…";
        }

        private static string FormatDetail(string detail) =>
            string.IsNullOrWhiteSpace(detail) ? "" : $": {detail}";

        public void Dispose() => _http.Dispose();
    }

    public sealed class PartialTranscriptionException : InvalidOperationException
    {
        public string PartialTranscript { get; }
        public int FailedChunk { get; }
        public int ChunkCount { get; }

        public PartialTranscriptionException(
            string partialTranscript,
            int failedChunk,
            int chunkCount,
            Exception innerException)
            : base(
                $"Teil {failedChunk} von {chunkCount} konnte nicht transkribiert werden. " +
                "Der bereits erkannte Text wurde in der Diktat-Historie gesichert.",
                innerException)
        {
            PartialTranscript = partialTranscript;
            FailedChunk = failedChunk;
            ChunkCount = chunkCount;
        }
    }
}
