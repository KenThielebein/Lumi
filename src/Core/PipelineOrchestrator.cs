using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lumi.Services.AI;
using Lumi.Services.Audio;
using Lumi.Services.Diagnostics;
using Lumi.Services.Memory;
using Lumi.Services.Speech;
using Lumi.Services.TextManipulation;
using Lumi.UI;

namespace Lumi.Core
{
    public class PipelineOrchestrator : IDisposable
    {
        private const int HotkeyReleaseTimeoutMs = 5_000;
        private readonly AudioService             _audio;
        private readonly ISpeechToText            _stt;
        private readonly ISmoothingService        _smoother;
        private readonly IAIProvider              _llm;
        private readonly ITextManipulationService _text;
        private readonly MemoryService            _memory;
        private readonly OverlayWindow            _overlay;
        private readonly ModeController           _mode;
        private readonly Func<bool>                _areHotkeyKeysDown;
        private readonly SemaphoreSlim            _processingGate = new(1, 1);

        private AppMode? _forcedModeForCurrentRecording;
        private Func<Task>? _beforeTextInsertAsync;
        private IntPtr _sourceWindow;
        private string? _selectedText;
        private string? _suggestionCommand;
        private string? _suggestionResult;

        private const string SuggestionSystem =
            "Du bist ein Textbearbeitungs-Assistent. " +
            "Führe die Anweisung des Nutzers auf dem gegebenen Text aus. " +
            "Antworte NUR mit dem bearbeiteten Text, ohne Erklärung oder Kommentar.";

        public PipelineOrchestrator(
            AudioService audio, ISpeechToText stt, ISmoothingService smoother,
            IAIProvider llm, ITextToSpeech tts, ITextManipulationService text,
            MemoryService memory, OverlayWindow overlay, ModeController mode,
            Func<bool> areHotkeyKeysDown)
        {
            _audio   = audio;
            _stt     = stt;
            _smoother = smoother;
            _llm     = llm;
            _text    = text;
            _memory  = memory;
            _overlay = overlay;
            _mode    = mode;
            _areHotkeyKeysDown = areHotkeyKeysDown;

            _audio.SilenceDetected += OnSilenceDetected;
        }

        public void PrepareRecording(IntPtr sourceWindow)
        {
            _sourceWindow = sourceWindow;
            _selectedText = null;
            _suggestionCommand = null;
            _suggestionResult = null;
        }

        public async Task StartListeningAsync()
        {
            if (_audio.IsRecording || _processingGate.CurrentCount == 0) return;

            _forcedModeForCurrentRecording = null;
            _beforeTextInsertAsync = null;
            _overlay.SetState(OverlayState.Listening);
            await _audio.StartRecordingAsync();
        }

        public async Task StartHandsFreeDictationAsync(Func<Task>? beforeTextInsertAsync = null)
        {
            if (_audio.IsRecording || _processingGate.CurrentCount == 0) return;

            _forcedModeForCurrentRecording = AppMode.Dictation;
            _beforeTextInsertAsync = beforeTextInsertAsync;
            _overlay.SetState(OverlayState.Listening);
            await _audio.StartRecordingAsync();
        }

        public async Task StopAndProcessAsync()
        {
            if (!await _processingGate.WaitAsync(0)) return;

            try
            {
                await StopAndProcessCoreAsync();
            }
            catch (Exception ex)
            {
                LumiDiagnostics.Write(
                    "pipeline_error",
                    ("error_type", ex.GetType().Name),
                    ("hresult", ex.HResult));
                _overlay.ShowError(ex.Message);
            }
            finally
            {
                _forcedModeForCurrentRecording = null;
                _beforeTextInsertAsync = null;
                _processingGate.Release();
            }
        }

        private async Task StopAndProcessCoreAsync()
        {
            if (!_audio.IsRecording) return;

            var isPushToTalkRecording = _forcedModeForCurrentRecording == null;
            var mode = _forcedModeForCurrentRecording ?? _mode.CurrentMode;
            _overlay.SetState(OverlayState.Processing);
            var stopTimer = Stopwatch.StartNew();
            var wavData = await _audio.StopRecordingAsync();
            LumiDiagnostics.Write(
                "audio_stopped",
                ("mode", mode),
                ("bytes", wavData.Length),
                ("elapsed_ms", stopTimer.ElapsedMilliseconds));

            // Das erste Loslassen von Strg oder # beendet aus Privacy-Gründen sofort
            // die Aufnahme. Tastatur- und Clipboard-Aktionen dürfen aber erst starten,
            // wenn beide Tasten physisch oben sind. Andernfalls interpretiert Windows
            // nachfolgende Eingaben möglicherweise noch als Strg-Kürzel.
            if (isPushToTalkRecording && !await WaitForHotkeyReleaseAsync())
            {
                _overlay.ShowError("Strg+# vollständig loslassen und erneut versuchen.");
                return;
            }

            if (mode == AppMode.Suggestion)
            {
                _selectedText = await _text.GetSelectedTextAsync(_sourceWindow);
                if (string.IsNullOrWhiteSpace(_selectedText))
                {
                    _overlay.ShowError("Keine Auswahl erkannt. Text markieren und Strg+# erneut halten.");
                    return;
                }
            }

            // 16 kHz · 16 bit · mono = 32.000 Byte/s
            if (wavData.Length < 16_000)
            {
                _overlay.SetState(OverlayState.Idle);
                return;
            }

            if (mode == AppMode.Dictation)
                await RunDictationAsync(wavData);
            else
                await RunSuggestionAsync(wavData);
        }

        private async Task<bool> WaitForHotkeyReleaseAsync()
        {
            var deadline = Environment.TickCount64 + HotkeyReleaseTimeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                // Der Low-Level-Hook unterdrückt #-Up absichtlich vor Windows.
                // Deshalb ist GetAsyncKeyState für # hier nicht verlässlich.
                // Der Hook selbst sieht aber jedes physische Up-Event und hält
                // den tatsächlichen Zustand in HotkeyManager aktuell.
                if (!_areHotkeyKeysDown())
                {
                    await Task.Delay(15);
                    return true;
                }
                await Task.Delay(20);
            }

            return false;
        }

        private async void OnSilenceDetected(object? sender, EventArgs e)
        {
            // Stille beendet ausschließlich eine bewusst gestartete Freihand-Aufnahme.
            // Push-to-talk läuft in beiden Modi bis Strg oder # losgelassen wird; natürliche
            // Sprechpausen dürfen längere Diktate nicht mitten im Satz abschneiden.
            if (_forcedModeForCurrentRecording != AppMode.Dictation)
                return;

            try
            {
                await StopAndProcessAsync();
            }
            catch
            {
                // StopAndProcessAsync zeigt wiederherstellbare Fehler in der Pille.
            }
        }

        private async Task RunDictationAsync(byte[] wavData)
        {
            string transcript;
            var sttTimer = Stopwatch.StartNew();
            try
            {
                transcript = await _stt.TranscribeAsync(wavData);
                LumiDiagnostics.Write(
                    "stt_completed",
                    ("audio_bytes", wavData.Length),
                    ("elapsed_ms", sttTimer.ElapsedMilliseconds),
                    ("characters", transcript.Length));
            }
            catch (PartialTranscriptionException ex)
            {
                LumiDiagnostics.Write(
                    "stt_partial_error",
                    ("audio_bytes", wavData.Length),
                    ("elapsed_ms", sttTimer.ElapsedMilliseconds),
                    ("failed_chunk", ex.FailedChunk),
                    ("chunk_count", ex.ChunkCount),
                    ("error_type", ex.InnerException?.GetType().Name));
                var partialText = _memory.ApplyVocabulary(ex.PartialTranscript);
                if (!string.IsNullOrWhiteSpace(partialText))
                    _overlay.ShowDictationResult(partialText, openEditor: false);
                _overlay.ShowError(ex.Message);
                return;
            }

            if (string.IsNullOrWhiteSpace(transcript))
            {
                _overlay.SetState(OverlayState.Idle);
                return;
            }

            var text = _memory.ApplyVocabulary(transcript);
            if (IsLikelySilenceHallucination(text) ||
                string.IsNullOrWhiteSpace(text.Trim().Trim('.', '…', ' ', '\n')))
            {
                _beforeTextInsertAsync = null;
                _overlay.SetState(OverlayState.Idle);
                return;
            }

            if (_overlay.InsertDictationImmediately)
            {
                var insertTimer = Stopwatch.StartNew();
                try
                {
                    if (_beforeTextInsertAsync != null)
                        await _beforeTextInsertAsync();

                    await _text.InsertTextAtCursorAsync(text.TrimEnd() + " ");
                    LumiDiagnostics.Write(
                        "insert_completed",
                        ("characters", text.Length),
                        ("elapsed_ms", insertTimer.ElapsedMilliseconds));
                }
                catch (Exception ex)
                {
                    LumiDiagnostics.Write(
                        "insert_error",
                        ("characters", text.Length),
                        ("elapsed_ms", insertTimer.ElapsedMilliseconds),
                        ("error_type", ex.GetType().Name),
                        ("hresult", ex.HResult));
                    // Die teure Transkription darf bei einem Fokus-/SendInput-
                    // Problem nicht verloren gehen. Verlauf 1 bleibt anklickbar.
                    _beforeTextInsertAsync = null;
                    _overlay.ShowDictationResult(text, openEditor: false);
                    _overlay.ShowError(
                        "Einfügen nicht möglich – das Diktat ist unter „1“ gesichert. " +
                        ex.Message);
                    return;
                }
            }

            _beforeTextInsertAsync = null;
            _overlay.ShowDictationResult(text, openEditor: !_overlay.InsertDictationImmediately);
        }

        private async Task RunSuggestionAsync(byte[] wavData)
        {
            var command = await _stt.TranscribeAsync(wavData);
            if (string.IsNullOrWhiteSpace(command))
            {
                _overlay.SetState(OverlayState.Idle);
                return;
            }

            _suggestionCommand = _memory.ApplyVocabulary(await _smoother.SmoothAsync(command));
            await GenerateSuggestionAsync();
        }

        private async Task GenerateSuggestionAsync()
        {
            if (string.IsNullOrWhiteSpace(_selectedText) ||
                string.IsNullOrWhiteSpace(_suggestionCommand))
                return;

            _overlay.SetState(OverlayState.Processing);
            var prompt = $"Anweisung: {_suggestionCommand}\n\nText:\n{_selectedText}";
            _suggestionResult = (await _llm.CompleteAsync(prompt, SuggestionSystem)).Trim();
            _overlay.ShowSuggestionPreview(_suggestionResult);
        }

        public async Task AcceptSuggestionAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_selectedText) ||
                    string.IsNullOrWhiteSpace(_suggestionResult))
                    return;

                var replaced = await _text.ReplaceSelectionIfMatchesAsync(
                    _sourceWindow, _selectedText, _suggestionResult);

                if (!replaced)
                {
                    await _text.CopyTextAsync(_suggestionResult);
                    _overlay.ShowError("Auswahl verloren – Vorschlag wurde kopiert.");
                    return;
                }

                CancelSuggestion();
                _overlay.ShowTransientMessage("✓ Text ersetzt");
            }
            catch (Exception ex)
            {
                _overlay.ShowError(ex.Message);
            }
        }

        public async Task CopySuggestionAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_suggestionResult)) return;
                await _text.CopyTextAsync(_suggestionResult);
                _overlay.ShowSuggestionCopied();
            }
            catch (Exception ex)
            {
                _overlay.ShowError(ex.Message);
            }
        }

        public async Task RetrySuggestionAsync()
        {
            try
            {
                await GenerateSuggestionAsync();
            }
            catch (Exception ex)
            {
                _overlay.ShowError(ex.Message);
            }
        }

        public void CancelSuggestion()
        {
            _selectedText = null;
            _suggestionCommand = null;
            _suggestionResult = null;
            _overlay.SetState(OverlayState.Idle);
        }

        public async Task LearnVocabularyAsync(string heardAs, string written)
        {
            try
            {
                if (await _memory.AddVocabularyAsync(heardAs, written))
                    _overlay.ShowTransientMessage($"✓ Gelernt: {heardAs} → {written}");
            }
            catch (Exception ex)
            {
                _overlay.ShowError(ex.Message);
            }
        }

        private static bool IsLikelySilenceHallucination(string text)
        {
            var normalized = string.Join(" ", new string(text
                    .ToLowerInvariant()
                    .Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
                    .ToArray())
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

            return normalized is
                "vielen dank" or
                "danke" or
                "danke fürs zuschauen" or
                "vielen dank fürs zuschauen" or
                "vielen dank für ihre aufmerksamkeit" or
                "untertitel im auftrag des zdf";
        }

        public void Dispose()
        {
            _audio.SilenceDetected -= OnSilenceDetected;
            if (_stt is IDisposable sttDisposable)
                sttDisposable.Dispose();
            if (_smoother is IDisposable smootherDisposable)
                smootherDisposable.Dispose();
            if (_llm is IDisposable llmDisposable)
                llmDisposable.Dispose();
        }
    }
}
