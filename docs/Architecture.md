# Architecture – Lumi Windows AI Assistant

## Für Claude Code
Diese Datei beschreibt wie die Komponenten zusammenspielen. Halte dich an diese Struktur – andere Teile des Systems bauen darauf auf.

---

## Komponentenübersicht

```
┌─────────────────────────────────────────────────────────────┐
│                        App.xaml.cs                          │
│              (Einstiegspunkt, DI-Container, Tray)           │
└────────────────────────┬────────────────────────────────────┘
                         │
          ┌──────────────▼──────────────┐
          │         HotkeyManager        │
          │ (WH_KEYBOARD_LL Win32 Hook)  │
          │  Erkennt: Press / Release /  │
          │  DoubleClick / Hold-Duration │
          └──────────────┬──────────────┘
                         │ Events
          ┌──────────────▼──────────────┐
          │        ModeController        │
          │  Verwaltet aktiven Modus:    │
          │  Suggestion / Dictation      │
          └──┬───────────┬──────────────┘
             │           │
    ┌─────────▼──┐   ┌───▼──────────────────────────┐
    │  Overlay   │   │        PipelineOrchestrator   │
    │  (WPF)     │   │  Koordiniert den Datenfluss:  │
    │            │   │  Audio → STT → AI → TTS       │
    │  States:   │   └───┬──────────┬───────────┬───┘
    │  Idle      │       │          │           │
    │  Listening │  ┌────▼───┐ ┌───▼───┐ ┌────▼────┐
    │  Processing│  │ Audio  │ │  AI   │ │  Text   │
    │  Result    │  │Service │ │Provider│ │Manip.  │
    │  Error     │  └────────┘ └───────┘ └─────────┘
    └────────────┘
```

---

## Datenfluss pro Modus

### Modus 1: Vorschlag
```
[Hotkey Press]
    → AudioService.StartRecording()
    → Overlay: State = Listening

[Hotkey Release]
    → AudioService.StopRecording() → byte[] wavData
    → warten, bis Win und J physisch losgelassen sind
    → selectedText = TextManipulationService.GetSelectedTextAsync(sourceHwnd)
    → Overlay: State = Processing
    → GroqSTTClient.TranscribeAsync(wavData) → string rawCommand
    → Prompt: $"Bearbeite folgenden Text.\nAnweisung: {command}\nText: {selectedText}"
    → OpenRouterAIProvider.CompleteAsync(prompt) → string transformedText
    → Overlay: Vorschau mit Ersetzen / Kopieren / Neu / Abbrechen

[Ersetzen]
    → Ziel-HWND fokussieren
    → aktuelle Auswahl erneut lesen und mit selectedText vergleichen
    → nur bei Übereinstimmung einfügen
    → sonst Ergebnis sicher in die Zwischenablage kopieren
```

### Modus 2: Diktat
```
[Hotkey Press]
    → AudioService.StartRecording()
    → Overlay: State = Listening

[Hotkey Release]
    → AudioService.StopRecording() → byte[] wavData
    → warten, bis Win und J physisch losgelassen sind
    → Overlay: State = Processing
    → GroqSTTClient.TranscribeAsync(wavData) → string rawText
    → MemoryService.ApplyVocabulary(rawText) → string dictatedText
    → SendInput Unicode-Tastaturereignisse direkt am Cursor
      (Zwischenablage bleibt unverändert)
    → Overlay: kurz "✓" zeigen → State = Idle
```

Die VAD-Stille-Erkennung beendet ausschließlich das per Maus gestartete
Freihand-Diktat. Eine Push-to-talk-Aufnahme bleibt auch während natürlicher
Sprechpausen aktiv und stoppt sofort, sobald Win oder J losgelassen wird.

---

## Klassen-Übersicht

### Core
```csharp
public class HotkeyManager : IDisposable
// Fängt Win+J vollständig via WH_KEYBOARD_LL ab, bevor Windows Recall öffnet
// Feuert Events basierend auf Tipp-Länge und -Frequenz:
//   ShortPressed   → < 200ms  (Toggle)
//   LongPressed    → > 200ms  (Push-to-Talk Beginn)
//   Released       → Loslassen nach LongPress (Push-to-Talk Ende)
//   DoubleTapped   → 2x < 400ms (Modus wechseln)
//   TripleTapped   → 3x < 600ms (Provider-Switch)

public class ModeController
{
    public AppMode CurrentMode { get; }  // Suggestion | Dictation
    public void CycleMode();
}

public class PipelineOrchestrator
{
    public Task ExecuteAsync(AppMode mode, CancellationToken ct);
}
```

### UI
```csharp
public class OverlayWindow : Window
// Transparentes WPF-Fenster, immer im Vordergrund
// ShowActivated = false → stiehlt keinen Fokus
{
    public void SetState(OverlayState state);
    public void ShowMessage(string text);
}

public enum OverlayState { Idle, Listening, Processing, Result, Error }
```

### Services
```csharp
// OpenRouter als einheitlicher Endpunkt für alle LLM-Calls
// Modell ist konfigurierbar – Standard: "moonshotai/kimi-k2.5"
public interface IAIProvider
{
    string ProviderName { get; }
    string Model { get; }
    Task<string> CompleteAsync(string userMessage, string? systemPrompt = null);
    Task<string> CompleteWithContextAsync(List<ChatMessage> history, string? systemPrompt = null);
}

// Einheitliche Implementierung für Cloud UND Lokal
// Kein Code-Unterschied – nur BaseUrl + Model kommen aus AppConfig
public class OpenAICompatibleProvider : IAIProvider
{
    // Cloud:  BaseUrl = "https://openrouter.ai/api/v1",  ApiKey = OpenRouterKey
    // Ollama: BaseUrl = "http://localhost:11434/v1",     ApiKey = "ollama" (dummy)
    // LMStudio: BaseUrl = "http://localhost:1234/v1",    ApiKey = "lm-studio" (dummy)
    public string ProviderName { get; }  // z.B. "OpenRouter", "Ollama", "LM Studio"
    public string Model { get; }         // z.B. "moonshotai/kimi-k2.5" oder "llama3.1:8b"
}

// STT – Standard: Groq Whisper large-v3 Turbo
public interface ISpeechToText
{
    Task<string> TranscribeAsync(byte[] wavData, string language = "de");
    string ProviderName { get; }
}

// Text-Glättung nach STT (eigener Service, nutzt IAIProvider intern)
public interface ISmoothingService
{
    Task<string> SmoothAsync(string rawTranscript);
    // Prompt: Versprecher entfernen, Satzzeichen setzen, Inhalt nicht ändern
}

public interface IAudioService
{
    Task StartRecordingAsync();
    Task<byte[]> StopRecordingAsync();
    event EventHandler<float> VolumeChanged;
    bool IsRecording { get; }
}

public interface ITextToSpeech
{
    Task SpeakAsync(string text);
    // Standard: Windows SAPI (kostenlos)
    // Premium: OpenAI TTS (opt-in via AppConfig.TTSProvider)
}

public interface ITextManipulationService
{
    Task<string?> GetSelectedTextAsync(IntPtr sourceHwnd = default);
    Task InsertTextAtCursorAsync(string text);
    Task<bool> ReplaceSelectionIfMatchesAsync(
        IntPtr sourceHwnd, string expectedText, string replacement);
    Task CopyTextAsync(string text);
}
```

### Config
```csharp
public class AppConfig
{
    // Cloud API Keys (DPAPI-verschlüsselt)
    public string OpenRouterApiKey { get; set; }
    public string GroqApiKey { get; set; }
    public string OpenAiApiKey { get; set; }        // Optional: TTS Premium

    // Aktiver Provider – wird per Tray-Switch zur Laufzeit geändert
    public string ActiveLLMProfile { get; set; } = "Cloud";  // "Cloud" | "Ollama" | "LMStudio"

    // Cloud-Profil
    public string CloudLLMBaseUrl { get; set; } = "https://openrouter.ai/api/v1";
    public string CloudLLMModel { get; set; } = "moonshotai/kimi-k2.5";
    public string CloudSmoothingModel { get; set; } = "moonshotai/kimi-k2.5";

    // Lokal-Profil Ollama
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434/v1";
    public string OllamaModel { get; set; } = "llama3.1:8b";

    // Lokal-Profil LM Studio
    public string LMStudioBaseUrl { get; set; } = "http://localhost:1234/v1";
    public string LMStudioModel { get; set; } = "local-model";

    // STT
    public string STTProvider { get; set; } = "Groq";   // "Groq" | "WhisperNet" (lokal)
    public string TTSProvider { get; set; } = "SAPI";   // "SAPI" | "OpenAITTS"

    // Allgemein
    public string Language { get; set; } = "de";
    public bool EnableSmoothing { get; set; } = true;
    public bool AutoStart { get; set; } = false;
    public bool EnableMemory { get; set; } = false;
    public bool EnableLogging { get; set; } = false;
}
```

---

## NuGet-Pakete

| Paket | Verwendung |
|---|---|
| `NAudio` | Mikrofon-Aufnahme, Audio-Playback |
| `Hardcodet.NotifyIcon.Wpf` | Tray-Icon |
| `InputSimulator` | Tastatureingabe simulieren |
| `Microsoft.Extensions.DependencyInjection` | DI-Container |
| `System.Net.Http.Json` | HTTP-Clients für APIs |

---

## Win32 API Aufrufe

```csharp
[DllImport("user32.dll")]
private static extern IntPtr SetWindowsHookEx(
    int idHook, LowLevelKeyboardProc callback, IntPtr module, uint threadId);

// Der Hook unterdrückt physische J-Down/Repeat/Up-Ereignisse der aktiven
// Win+J-Sequenz. Ein kurzer injizierter Strg-Impuls verhindert, dass Windows
// beim Loslassen der Win-Taste das Startmenü öffnet, ohne eine Funktionstaste
// auszulösen. Trifft J wenige Millisekunden vor Win ein, hält ein 55-ms-
// Chord-Puffer das erste J zurück und reicht es nur dann nach, wenn Win ausbleibt.
```

> ℹ️ Win+J öffnet in aktuellen Windows-11-Versionen Recall. Lumi verarbeitet
> die Kombination daher vor Windows und reicht sie nicht an das System weiter.
> Hotkey ist in den Einstellungen konfigurierbar falls gewünscht.

---

## Fehlerbehandlung-Strategie

| Fehler | Verhalten |
|---|---|
| Kein Mikrofon | Overlay zeigt Fehler-Icon + Tooltip |
| API Key fehlt | Overlay → Einstellungen öffnen |
| STT Timeout / temporärer Netzwerkfehler | Drei adaptive Versuche mit 60s, 120s und 180s; danach verständliche Fehlermeldung |
| Sehr lange Aufnahme (>16 MB WAV) | In sichere WAV-Teile zerlegen und nacheinander transkribieren |
| Später WAV-Teil schlägt fehl | Bereits transkribierte Teile in der Diktat-Historie sichern |
| Direkte Texteingabe scheitert | Transkript in der Diktat-Historie sichern; Teilübertragung bis zu dreimal fortsetzen |
| Rate Limit (429) | Exponentielles Retry, max. 3 Versuche |
| Netzwerk offline | SAPI-Fallback für TTS, kein STT möglich |

---

## Deployment

```
Release-Build:
  dotnet publish -c Release -r win-x64 --self-contained true

Installer:
  NSIS-Script: /build/installer.nsi
  → Installiert nach %LOCALAPPDATA%\Lumi\

GitHub Release:
  Tag: v1.0.0
  Assets: Lumi-Setup-1.0.0.exe, Lumi-Portable-1.0.0.zip
```
