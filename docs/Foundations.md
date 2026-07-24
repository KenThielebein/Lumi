# Foundations – Lumi AI Assistant

## Pflichtlektüre für die KI
Lies diese Datei vollständig, bevor du irgendetwas implementierst. Sie definiert alle verbindlichen Entscheidungen für dieses Projekt.

---

## Projektname
**Lumi** – leuchtet kurz auf wenn du es brauchst, verschwindet danach wieder.
- Repo: `lumi`
- Binary: `Lumi.exe`
- AppData: `%APPDATA%\Lumi\`
- Tray-Icon Tooltip: "Lumi"

---

## Vision
Wir bauen einen nativen Windows-KI-Assistenten, der sich unsichtbar in den Arbeitsalltag integriert. Kein separates Fenster, keine Ablenkung – nur ein globaler Hotkey und ein minimalistisches Overlay ("Pille"), das bei Bedarf aufleuchtet und danach wieder verschwindet.

Inspiriert von: [TwoKey (YouTube)](https://youtu.be/iTRZat7urvs)

---

## Das Light-Prinzip
> "So wenig UI wie möglich, so viel KI wie nötig."

- Die App läuft still im System-Tray
- Sichtbar wird sie NUR wenn der Nutzer sie aktiv aufruft
- Keine eigenen Fenster, die den Fokus stehlen
- KI-Provider ist frei wählbar: Cloud (OpenRouter) oder lokal (Ollama / LM Studio) – umschaltbar im laufenden Betrieb
- Jede Funktion muss mit einer Hand bedienbar sein

---

## Technologie-Stack (verbindlich)

| Entscheidung | Wahl | Begründung |
|---|---|---|
| Sprache | C# / .NET 8 | Voller Windows-API-Zugriff (Hotkeys, Clipboard, Prozesse) |
| UI-Framework | WPF | Native Windows-Transparenz & Overlay-Fenster |
| STT | **Groq Whisper large-v3 Turbo** | 9x günstiger als OpenAI ($0.04/h vs $0.36/h), gleiche Qualität, schneller |
| LLM | **OpenAICompatibleProvider** | Einheitliche Implementierung; BaseUrl + Model konfigurierbar |
| LLM Cloud (Standard) | OpenRouter → Kimi K2.5 | ~$0.40/1M Input, günstigste sinnvolle Option |
| LLM Cloud Fallback | OpenRouter → Claude / GPT-4o | Für komplexere Aufgaben, gleicher Code |
| Text-Glättung | OpenAICompatibleProvider → Kimi K2.5 | STT-Rohtext glätten: Versprecher entfernen, Satzzeichen setzen |
| TTS | **Windows SAPI** (primär) | Kostenlos, offline, keine API nötig |
| TTS Premium | OpenAI TTS (opt-in) | ~$15/1M Zeichen, nur wenn Qualität gewünscht |
| Audio | NAudio (NuGet) | Mikrofon-Aufnahme & Audio-Playback unter Windows |
| Config/Secrets | Windows AppData + DPAPI | API-Keys niemals im Source-Code oder Repo |
| Updates | GitHub Releases API | Öffentliches Repo, automatische Update-Prüfung |
| Build | GitHub Actions + NSIS/WiX | Automatisierter Build & Installer |

### Geschätzte laufende Kosten
| Konfiguration | STT | LLM | TTS | Gesamt |
|---|---|---|---|---|
| Cloud Low-Cost | Groq $0.04/h | Kimi K2.5 ~$0.40/1M | SAPI gratis | **~$1–4/Mo** |

---

## Projekt-Struktur (verbindlich)

```
/src
  /Core               → Hotkey-Manager, App-Lifecycle, Modus-Verwaltung
  /UI                 → WPF Overlay (Pille), Tray-Icon, Einstellungsfenster
  /Services
    /Audio            → Mikrofon-Aufnahme, Playback (NAudio)
    /AI               → IAIProvider Interface + OpenAICompatibleProvider
                        (funktioniert für OpenRouter UND Ollama/LM Studio)
    /Speech           → ISpeechToText (Groq Cloud + Whisper.net lokal), TTS (SAPI / OpenAI)
    /TextManipulation → Clipboard-Zugriff, Tastatur-Simulation, Cursor-Ersetzung
  /Config             → AppData-Verwaltung, Einstellungen, DPAPI-Verschlüsselung
  /Update             → GitHub Release Checker, Auto-Updater
/docs                 → Projektdokumentation (diese Dateien)
/build                → Build-Scripts, Installer-Konfiguration
/tests                → Unit- und Integrationstests
```

---

## Interfaces (nicht brechen!)

```csharp
// Alle KI-Provider müssen dieses Interface implementieren
public interface IAIProvider
{
    Task<string> CompleteAsync(string prompt, string? systemPrompt = null);
    Task<string> CompleteWithContextAsync(List<Message> history, string? systemPrompt = null);
    string ProviderName { get; }
    string Model { get; }  // z.B. "moonshotai/kimi-k2.5"
}

// STT – Default-Implementierung: Groq Whisper large-v3 Turbo
public interface ISpeechToText
{
    Task<string> TranscribeAsync(byte[] wavData, string language = "de");
    string ProviderName { get; }  // z.B. "Groq"
}

// Alle Audio-Services müssen dieses Interface implementieren
public interface IAudioService
{
    Task StartRecordingAsync();
    Task<byte[]> StopRecordingAsync();
    event EventHandler<float> VolumeChanged;
    bool IsRecording { get; }
}
```

---

## Hotkey-System

### Primärer Hotkey: Strg + #
Einziger globaler Hotkey. Alles ergibt sich aus Tipp-Länge und -Frequenz:

```
┌─────────────────────────────────────────────────────────────┐
│  Strg + #  (< 200ms)     → Pille ein-/ausblenden            │
│  Strg + #  (> 200ms)     → Push-to-Talk: halten = aufnehmen │
│  Strg + #  (2x < 400ms)  → Modus wechseln                   │
│  Esc                    → Abbrechen, Pille schließen        │
│  Pfeil ↑  (Pille aktiv) → Letzte Antwort wiederholen       │
└─────────────────────────────────────────────────────────────┘
```

### Timing-Konstanten (konfigurierbar)
```csharp
const int ShortPressMs    = 200;   // unter 200ms = kurz (Toggle)
const int DoubleTapMs     = 400;   // zwei Klicks innerhalb 400ms = Modus
const int ChordGraceMs     = 55;   // # knapp vor Strg puffern, sonst # nachreichen
```

Die physische Rautentaste einer deutschen Tastatur (Scan-Code `0x2B`), die
höchstens 55 ms vor einer Strg-Taste eintrifft, wird kurz gepuffert. Folgt Strg,
gehört sie vollständig zur Lumi-Sequenz; folgt Strg nicht, wird die normale
Rauteneingabe unverändert nachgereicht. Eine bereits sichtbare
Eingabe darf niemals nachträglich per Backspace entfernt werden.

Der Low-Level-Hook unterscheidet Lumi-eigene synthetische Eingaben über einen
eindeutigen Marker von Eingaben anderer Tastaturtreiber, OEM-Hotkeydienste oder
Remoting-Lösungen. Nur Lumi-eigene Ereignisse werden übersprungen. Fremd als
`injected` markierte Strg-/Raute-Ereignisse gehören weiterhin zur
Hotkey-Erkennung. Die `SendInput`-Strukturen enthalten die vollständige
64-Bit-Union, damit Replay-Ereignisse auf allen x64-Rechnern gültig sind.

### Warum Strg + #?
- keine Windows-Taste und damit kein Konflikt mit Recall oder Windows Hello
- auf deutschen Tastaturen eindeutig und mit der rechten Hand bedienbar
- linke und rechte Strg-Taste werden akzeptiert
- die Kombination wird vollständig im Low-Level-Keyboard-Hook verarbeitet
- ⚠️ Win + H (Microsoft Voice Typing) ist unser direkter Konkurrent – in Lumi-Einstellungen Hinweis einblenden

---

## Die zwei Modi

Der aktive Modus wird dauerhaft gespeichert. Bestehende Konfigurationen mit `Conversation` werden beim Laden auf `Dictation`, `TextEdit` auf `Suggestion` migriert.

### Modus 1: Diktat 🎤 (Standard)
- Strg+# halten → Groq STT → persönliches Wörterbuch → Text direkt am Cursor
- Kein LLM-Call im Normalweg; minimale Latenz
- Wahlweise sofort einfügen oder zuerst in der Diktat-Historie nachbearbeiten

### Modus 2: Vorschlag ✏️
```
1. Text in beliebiger App markieren
2. Strg+# halten
3. Sprachbefehl sprechen: "Mach das formeller"
4. Strg+# loslassen
5. Erst jetzt liest Lumi den markierten Text
6. Das LLM erzeugt einen Vorschlag
7. Vorschau: Ersetzen, Kopieren, Neu versuchen oder Abbrechen
8. Vor dem Ersetzen prüft Lumi Zielanwendung und Auswahl; bei verlorener Auswahl wird sicher kopiert
```

---

## Persönliches Wörterbuch

Bestätigte Schreibkorrekturen werden lokal in `memory.json` und optional in derselben GitHub-JSON gespeichert:

- bevorzugte Schreibweisen gehen als kurzer Prompt an Groq Whisper
- bekannte Fehlformen werden danach lokal und deterministisch ersetzt
- Lumi lernt nur nach ausdrücklicher Bestätigung
- Beispiel: `Hier am Haus => Hiram Haus`

---

## App-Kompatibilität

Lumi arbeitet auf Windows-Ebene und funktioniert in nahezu jeder App:

| App-Typ | Diktieren | Text ersetzen | Anmerkung |
|---|---|---|---|
| Word, Outlook, LibreOffice | ✅ | ✅ | Perfekt |
| Browser (Chrome, Edge, Firefox) | ✅ | ✅ | Perfekt |
| Gmail (im Browser) | ✅ | ⚠️ | Meistens ja; Rich-Text-Editor kann zicken → Kompatibilitätsmodus |
| Google Docs (im Browser) | ✅ | ⚠️ | Wie Gmail |
| VS Code, Notepad++ | ✅ | ✅ | Perfekt |
| Teams, Slack, WhatsApp Desktop | ✅ | ⚠️ | Meistens; gelegentlich Fokus-Probleme |
| Windows Notepad | ✅ | ✅ | Perfekt |
| PDF-Viewer (lesend) | ✅ | ❌ | Lesen ja, ersetzen nein |
| Terminal / CMD | ✅ | ⚠️ | Einfügen funktioniert anders |
| Spiele / Vollbild-Apps | ❌ | ❌ | Hotkeys oft blockiert |
| UAC-Dialoge | ❌ | ❌ | Windows sperrt dort alles |

**Direkte Diktateingabe:** Diktate und bestätigte Ersetzungen werden als
Unicode-Tastatureingabe direkt am Cursor eingefügt. Die Zwischenablage bleibt
dabei vollständig unberührt. Nur das Lesen einer markierten Auswahl und
ausdrückliche Kopieraktionen verwenden das Clipboard; ein voriger Inhalt wird
beim Lesen anschließend wiederhergestellt.

**Kompatibilitätsmodus** (für Gmail, Notion etc.): Statt Strg+V wird der Text Zeichen für Zeichen via SendKeys eingefügt – langsamer aber universell. Konfigurierbar pro App.

---

---

## Datenspeicherung
```
%APPDATA%\Lumi\
  config.json    → Einstellungen (verschlüsselt via DPAPI)
  memory.json    → Nutzer-Fakten und persönliches Wörterbuch (optional GitHub-Sync)
  logs\          → Debug-Logs (opt-in)
```

---

## Verbotene Muster
- ❌ API-Keys im Source-Code oder in `.csproj`-Dateien
- ❌ Mikrofon aktiv ohne aktiven Hotkey (Privacy!)
- ❌ Fenster, die den Nutzer-Fokus stehlen (`Topmost = true` nur für die Pille)
- ❌ Blocking-Calls auf dem UI-Thread (alles async)
- ❌ Provider-Switch ohne sofortige Wirkung – Wechsel muss im laufenden Betrieb funktionieren
- ❌ Clipboard nicht wiederherstellen nach Text-Manipulation

---

## Wichtige Hinweise für Claude Code
1. Prüfe immer zuerst `Status.md` für den aktuellen Stand
2. Implementiere nur, was in `Status.md` als 🟢 AKTIV markiert ist
3. Halte alle Interfaces stabil – andere Komponenten bauen darauf auf
4. Jede neue technische Entscheidung in dieser Datei dokumentieren
5. App heißt überall "Lumi" – kein "TwoKey" mehr im Code oder UI
