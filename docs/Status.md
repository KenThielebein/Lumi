# Status – Lumi AI Assistant

## Für Claude Code: Lies diese Datei nach Foundations.md
Implementiere NUR was unter 🟢 AKTIV steht.

Nach jeder erledigten Aufgabe:
- Checkbox abhaken: `- [ ]` → `- [x]`
- Phase abgeschlossen: → `✅ ERLEDIGT`, nächste → `🟢 AKTIV`
- Neue Entscheidungen in Entscheidungslog eintragen
- "Letzte Änderung" und "Nächste Aufgabe" oben aktualisieren

---

## Aktueller Stand
**Aktive Phase:** Phase 6 – Update-System & Release
**Letzte Änderung:** 2026-07-24 – Version 4.5.4 auf GitHub veröffentlicht und GitHub-Pages-Website mit Download und Versionshinweisen aktualisiert
**Nächste Aufgabe:** Version 4.5.4 auf dem Ziel-Notebook praktisch mit Win→J, J→Win und einem langen realen Diktat testen; danach alternative Tastenkombination, NSIS-Installer, Win+H-Hinweis und Update-Check umsetzen

---

## Phase 1 – Projekt-Setup & Overlay-Skelett ✅ ERLEDIGT

### Ziel
Lauffähiges WPF-Fenster als transparente "Pille", zeigt/versteckt sich per Hotkey.

### Aufgaben
- [x] `.sln` und `.csproj` anlegen (WPF, .NET 8, Windows-Target), Projektname: `Lumi`
- [x] NuGet-Pakete: `NAudio`, `Hardcodet.NotifyIcon.Wpf`
- [x] Ordnerstruktur gemäß `Foundations.md` anlegen
- [x] `App.xaml.cs`: startet ohne Hauptfenster, nur Tray-Icon mit Tooltip "Lumi"
- [x] Tray-Icon Kontextmenü: "Einstellungen", "Beenden"
- [x] WPF Overlay ("Pille"): transparent, `Topmost`, nicht in Taskleiste, `ShowActivated = false`
- [x] Pille erscheint zentriert unten am Bildschirm
- [x] Pille zeigt vier Zustände (nur visuell):
  - `Idle` – kleines Lumi-Icon
  - `Listening` – animierte Wellenform
  - `Processing` – Spinner
  - `Result` – Text-Anzeige
- [x] `HotkeyManager`: Win+J registrieren via Low-Level Keyboard Hook (WH_KEYBOARD_LL)
- [x] Timing-Logik: kurz (<200ms) = Toggle, lang (>200ms) = Push-to-Talk
- [x] Doppel-Tap (<400ms) = Modus wechseln (loggt, noch kein Audio)
- [x] Dreifach-Tap (<600ms) = Provider-Switch (loggt, noch nicht implementiert)
- [x] Esc = Pille schließen
- [x] Pille zeigt Modus-Icon: 💬 / ✏️ / 🎤
- [x] Pille zeigt Provider-Indikator: ☁ (Cloud) / ⚡ (Lokal)

### Akzeptanzkriterien
- App startet ohne Fehler, kein Hauptfenster sichtbar ✅
- Tray-Icon "Lumi" erscheint ✅
- Win+J kurz → Pille erscheint/verschwindet (Toggle) ✅
- Win+J halten → Pille erscheint, Wellenform-Animation läuft, beim Loslassen verschwindet sie ✅
- Win+J doppelt → Modus wechselt (Debug-Log) ✅
- Esc schließt die Pille ✅

---

## Phase 2 – Audio-Pipeline ✅ ERLEDIGT

### Aufgaben
- [x] `IAudioService` implementieren via NAudio
- [x] Aufnahme startet beim Hotkey-Drücken, stoppt beim Loslassen
- [x] VAD: Aufnahme stoppt auch bei >2s Stille
- [x] Audio-Buffer als WAV in-memory (nicht auf Disk)
- [x] Groq STT-Client: WAV-Buffer → Transkriptions-Text
- [ ] Whisper.net als lokale STT-Alternative (opt-in, Phase 5)
- [x] API-Key sicher aus `%APPDATA%\Lumi\config.json` laden
- [x] Pille zeigt Wellenform-Animation während Aufnahme
- [x] Fehlerbehandlung: kein Mikrofon, API nicht erreichbar (NullSttClient-Fallback)

---

## Phase 3 – LLM-Integration & Gesprächsmodus ✅ ERLEDIGT

### Aufgaben
- [x] `OpenAICompatibleProvider` implementieren (Cloud + Lokal, gleicher Code)
- [x] `ISmoothingService`: Post-STT Glättung via Kimi K2.5
- [x] Gesprächsmodus: STT → Glättung → LLM → SAPI TTS
- [x] Gesprächskontext (History) in-memory
- [ ] OpenAI TTS als Premium-Option (opt-in, Phase 5)
- [x] Pille zeigt Antwort-Text
- [x] Gesprächsmodus: Text direkt in der Pille eintippen und per Enter absenden
- [x] Gesprächseingabe mehrzeilig, scrollbar und mitwachsend; Absenden per `Strg+Enter` oder Button
- [x] Ausgabe- und Eingabefenster in der Pille per Drag-Splitter in der Höhe veränderbar
- [x] Memory: Langzeit-Fakten in `memory.json` (opt-in, Phase 5)
- [x] Gesprächsbefehle zum gezielten Befüllen und Anzeigen des Langzeit-Gedächtnisses

---

## Phase 4 – Text bearbeiten & Diktier-Modus ✅ ERLEDIGT

### Aufgaben
- [x] `ITextManipulationService` implementieren
- [x] Clipboard sichern vor jedem Zugriff, danach wiederherstellen
- [x] **Variante A:** markierten Text via Strg+C lesen → LLM transformieren → ersetzen
- [x] **Variante B:** manuell kopierten Text aus Clipboard lesen → in Pille verarbeiten
- [x] Ergebnis via Strg+V einfügen (Normalweg)
- [ ] **Kompatibilitätsmodus** via SendKeys (Fallback für Gmail, opt-in, Phase 5)
- [ ] Kompatibilitätsmodus konfigurierbar pro App-Klasse (Phase 5)
- [x] Diktier-Modus: STT + Glättung → direkt am Cursor einfügen
- [x] Modus-Wechsel per Doppel-Tap implementiert

---

## Phase 5 – Einstellungen & Onboarding 🟢 AKTIV

### Aufgaben
- [x] Einstellungsfenster: API-Keys eingeben (mit 👁 Reveal-Toggle)
- [x] Erster-Start: Settings öffnet sich automatisch wenn kein API-Key konfiguriert
- [x] Autostart mit Windows (Registry; repariert veraltete EXE-Pfade beim Start)
- [x] Einstellungen verschlüsselt via DPAPI (DPAPIHelper, ConfigManager)
- [x] Farbthema wählbar: Dark / Light / Amber / Blue
- [x] Overlay frei verschiebbar (Win32 WM_NCLBUTTONDOWN/HTCAPTION)
- [x] Overlay größenveränderlich (Win32 HTBOTTOMRIGHT + ResizeMode=CanResize)
- [x] Position & Größe wird gespeichert (WM_EXITSIZEMOVE → config.json)
- [ ] Erster-Start-Wizard: Mikrofon testen, Hotkey testen
- [ ] Hinweis auf Win+H (Microsoft Voice Typing) Konflikt
- [ ] Hotkey konfigurierbar machen
- [ ] Kompatibilitätsmodus-Liste für problematische Apps (Gmail, Notion etc.)
- [x] Modell-Auswahl in Einstellungen: ComboBox beliebte Modelle + 🔍 Vollsuche mit Preisen (OpenRouter API)
- [x] HTTP-Timeout auf 120s erhöht (für lange Texte)
- [x] GitHub-Sync für `memory.json` konfigurierbar (Token/Owner/Repo/Branch/Pfad in Einstellungen)
- [x] GitHub-Sync Statusbutton: Verbinden/Trennen mit grün/rot Anzeige
- [x] Einstellungen direkt aus der Pille öffnen; X oben rechts beendet Lumi
- [x] Einstellbarer VAD-Nachlauf in den Einstellungen
- [x] Freihand-Diktat per Mausbutton in der Pille
- [x] Letzte fünf Diktate in der Pille ansehen, nachbearbeiten und kopieren
- [x] Diktat-Umschalter: sofort am Cursor einfügen oder zuerst nachbearbeiten
- [x] Push-to-talk-Diktat: natürliche Sprechpausen stoppen die Aufnahme nicht; Verarbeitung erst nach vollständigem Loslassen von Win+J
- [x] Win+J vollständig im Low-Level-Hook abfangen, damit Windows 11 nicht Recall oder ein anderes Systemfenster öffnet
- [x] Lange Diktate: STT-Timeout auf 180s pro Versuch, automatische Wiederholung und sichere WAV-Teilung ab 16 MB
- [x] Diktate und bestätigte Ersetzungen direkt per Unicode-Tastatureingabe einfügen, ohne die Zwischenablage zu verändern
- ~~Provider-Switcher (Ollama / LM Studio) – gestrichen~~

---

## Phase 6 – Update-System & Release 🟢 AKTIV

### Aufgaben
- [ ] NSIS Installer (Setup-EXE mit Shortcuts + Deinstallation)
- [x] `build/build.ps1` für lokale Release-Builds
- [x] Versionsnummer in der Pille anzeigen
- [ ] GitHub Releases API: Versionscheck beim Start
- [ ] Update-Banner in Pille / Tray
- [x] GitHub Actions: Git-Tag `vX.Y.Z` → versionierter Build → GitHub Release mit automatisch erzeugtem Begleittext
- [x] Version 4.5.1 über Tag `v4.5.1` veröffentlicht (EXE + Portable-ZIP + automatisch erzeugte Release Notes)
- [x] Version 4.5.2 über Tag `v4.5.2` veröffentlicht (EXE + Portable-ZIP + ausführlicher deutscher Release-Text)
- [x] Version 4.5.3 lokal gebaut: korrekte physische Loslassprüfung und Strg- statt F15-Maskierung gegen Notebook-Bildschirmflackern
- [x] Version 4.5.3 auf dem Ziel-Notebook gestartet, Autostart repariert und per Win+J praktisch bestätigt
- [x] Version 4.5.4 lokal gebaut: 55-ms-Chord-Puffer gegen sichtbares J, ereignisbasierter Audio-Stopp, kürzerer Einfüge-Nachlauf und verlustsichere Langdiktat-Fehlerbehandlung
- [x] Version 4.5.4 über Tag `v4.5.4` auf GitHub veröffentlicht (EXE + Portable-ZIP)
- [x] Release-EXE (self-contained, win-x64, 156 MB) unter `src/bin/Release/.../publish/Lumi.exe`
- [x] Statische Landingpage unter `website/index.html` mit Downloadlink, Nutzungserklärung und API-Key-Anleitung
- [x] Landingpage für GitHub Pages vorbereitet: lokales Icon, `.nojekyll`, GitHub-Release-Downloadlink als Platzhalter
- [x] GitHub-Pages-Landingpage auf die zwei Modi und Version 4.5.1 aktualisiert; Versionsnummer, Download und Dateigröße folgen künftig automatisch dem Latest-Release
- [x] GitHub-Pages-Landingpage auf Version 4.5.2, lange Diktate und die Abschirmung gegen Windows Recall aktualisiert und live geprüft
- [x] GitHub-Pages-Landingpage auf Version 4.5.4, Hotkey-Reparatur, kürzeren Nachlauf und gesicherte Langdiktate aktualisiert

---

## Version 4.5.0 – Diktat & Vorschlag ✅ ERLEDIGT

### Ziel
Lumi auf die beiden alltagstauglichen Kernfunktionen Diktat und Vorschlag konzentrieren, die Pille deutlich verkleinern und persönliche Schreibweisen zuverlässig lernen.

### Aufgaben
- [x] Gesprächsmodus aus Oberfläche und Laufzeit entfernen; bestehende Konfigurationen auf Diktat migrieren
- [x] Nur noch zwischen Vorschlag und Diktat umschalten; Auswahl dauerhaft speichern
- [x] Ein-/ausklappbaren Kompaktmodus mit dauerhaft gespeicherter Einstellung umsetzen
- [x] Auswahl für Vorschläge erst nach Loslassen des Hotkeys erfassen
- [x] Vorschlag vor dem Ersetzen anzeigen; Ersetzen, Kopieren, Neu versuchen und Abbrechen anbieten
- [x] Zielanwendung und Auswahl vor dem Ersetzen prüfen; bei verlorener Auswahl sicher kopieren
- [x] Persönliches Wörterbuch mit bestätigten Korrekturen ergänzen
- [x] Wörterbuch als Groq-Prompt und lokale Nachkorrektur im Diktat verwenden
- [x] Bestehende GitHub-Memory-JSON abwärtskompatibel um synchronisiertes Vokabular erweitern
- [x] Version auf 4.5.0 setzen und Dokumentation aktualisieren

---

## Bekannte Probleme & Offene Fragen
| # | Problem | Status |
|---|---|---|
| 1 | Win+H (Microsoft Voice Typing) ist direkter Konkurrent – Hinweis in Onboarding | Offen |
| 2 | Gmail / Notion: Rich-Text-Editor reagiert nicht immer auf Strg+V | → Kompatibilitätsmodus |
| 3 | Vollbild-Apps / Spiele blockieren globale Hotkeys | Bekannte Einschränkung, dokumentieren |
| 4 | UAC-Dialoge: keine Hotkeys möglich | Bekannte Einschränkung |
| 5 | Diktat-Modus: Text landete in Windows-Suchleiste statt am Cursor | ✅ Behoben: KeyUp(LWIN/RWIN) + 100ms vor Ctrl+V in TextManipulationService |
| 6 | Längere Push-to-talk-Diktate stoppten bei Sprechpausen; Verarbeitung bei noch gehaltener Win-Taste löste Windows-Kürzel aus | ✅ Behoben: VAD nur noch für Freihand-Diktat, vollständige Tastenfreigabe vor Verarbeitung |
| 7 | Aktuelle Windows-11-Versionen verwenden Win+J für Recall und konnten Lumi die Tastenkombination entziehen | ✅ Behoben: Win+J wird vollständig im WH_KEYBOARD_LL-Hook erkannt und unterdrückt |
| 8 | Diktate wurden über Strg+V eingefügt; verzögertes Lesen konnte den vorherigen Inhalt der Zwischenablage unbrauchbar machen | ✅ Behoben: Direkte Unicode-Eingabe ohne Clipboard-Zugriff |
| 9 | Version 4.5.2 meldete nach jedem Push-to-talk fälschlich, Win+J sei noch gedrückt | ✅ Behoben: Die Loslassprüfung verwendet den physischen Zustand aus dem Low-Level-Hook; Mikrofon separat mit NAudio verifiziert |
| 10 | Beim Drücken von Win+J flackerte der Notebook-Bildschirm kurz | ✅ Behoben und praktisch bestätigt: F15-Maskierung in 4.5.3 durch kurzen Strg-Impuls ersetzt |
| 11 | Wenn J knapp vor Win eintraf, erschien erst „j“ und Lumi startete verzögert beim Auto-Repeat | ✅ Behoben in 4.5.4: J wird 55 ms gepuffert und bei ausbleibendem Win unverändert nachgereicht |
| 12 | Ein Einfüge- oder später Chunkfehler konnte ein bereits transkribiertes Langdiktat unzugänglich machen | ✅ Behoben in 4.5.4: erkannte Texte bleiben in der Diktat-Historie; technische Metadaten können opt-in geloggt werden |

---

## Entscheidungslog
| Datum | Entscheidung | Begründung |
|---|---|---|
| Kickoff | WPF statt WinUI 3 | Stabilere Overlay-Unterstützung |
| Kickoff | Kein lokales LLM als Zwang | Hardware zu schwach; lokal optional |
| Kickoff | Claude Code statt Codex | Bessere Agentic-Fähigkeiten |
| Kickoff | Groq statt OpenAI Whisper | 9x günstiger, gleiche Qualität |
| Kickoff | OpenRouter als LLM-Endpunkt | Einheitliche API, Modellwechsel per Config |
| Kickoff | Kimi K2.5 als Standard-LLM | Günstigster sinnvoller Anbieter |
| Kickoff | SAPI als Standard-TTS | Kostenlos, offline |
| Kickoff | Post-STT Glättung via Kimi | ~$0.0005/Call, sauberer Output wie ChatGPT |
| Kickoff | OpenAICompatibleProvider | Ollama/LM Studio/OpenRouter = gleicher Code |
| Kickoff | Provider-Switch zur Laufzeit | Kein Neustart nötig |
| Kickoff | Win+J als primärer Hotkey | Einziger freier Win+-Buchstabe in Win11 |
| Kickoff | Halten+Toggle+Multi-Tap Kombination | Intuitiv, eine Hand, kein zweiter Hotkey nötig |
| Kickoff | Zwei Text-Editing-Varianten | A: markiert+ersetzen, B: Clipboard manuell |
| Kickoff | Kompatibilitätsmodus SendKeys | Fallback für Gmail, Notion, Web-Apps |
| Kickoff | App-Name: Lumi | Leuchtet kurz auf, verschwindet wieder |
| 2026-05-25 | WH_KEYBOARD_LL statt RegisterHotKey | Nur Hook gibt uns Key-Up-Event für Druckdauer-Messung |
| 2026-05-25 | UseWindowsForms=true im csproj | Nötig für System.Drawing (Icon-Erzeugung) |
| 2026-05-25 | Win32 WM_NCLBUTTONDOWN für Drag/Resize | DragMove() unsicher mit AllowsTransparency; Win32 zuverlässiger |
| 2026-05-25 | WM_EXITSIZEMOVE zum Speichern der Position | Feuert einmalig nach Drag/Resize-Ende, nicht pro Frame |
| 2026-05-25 | Offline-Anbindung (Ollama / LM Studio) gestrichen | Nicht benötigt; vereinfacht den Code erheblich |
| 2026-05-25 | VK_F15-Injektion statt Win-UP-Unterdrückung | Win-UP-Essen ließ Win-Taste im System als gedrückt stehen → alle nachfolgenden Hotkeys wirkten wie Win+Taste; F15-Trick bricht Win-Sequenz ohne Win-UP zu fressen |
| 2026-05-25 | WM_COPY + F15+Ctrl+C-Fallback für Texterkennung | WM_COPY ans fokussierte Kind-Fenster (funktioniert für Win32-Apps); F15-Trick + Ctrl+C als Fallback für LibreOffice/Browser (kein Win-Key-Konflikt, Hook filtert injizierte Win-Events) |
| 2026-05-25 | _suppressJ-Flag im Hook | RegisterHotKey unterdrückt nur ersten J-KeyDown; Auto-Repeat-J-Events können Selektion löschen → Hook supprimiert physische J-Events während aktiver Win+J-Session |
| 2026-05-25 | OpenRouter-Modellauswahl mit Vollsuche | PopularModels-ComboBox (16 Modelle, gruppiert) + ModelPickerWindow mit Live-Suche und Preisanzeige (↑/↓ pro 1M Token) |
| 2026-05-27 | Autostart beim App-Start reconciliieren | Config und Registry konnten auseinanderlaufen; Lumi setzt bei `AutoStart=true` den Run-Eintrag jetzt auf die aktuell laufende EXE |
| 2026-05-27 | Gesprächsmodus bekommt manuelle Texteingabe | Nutzer können Lumi ohne Mikrofon im gleichen Conversation-Verlauf fragen; Enter nutzt dieselbe History wie Sprache |
| 2026-05-27 | Langzeit-Gedächtnis als Opt-in | Stabile Nutzerfakten werden lokal in `%APPDATA%\Lumi\memory.json` gespeichert und in den Gesprächs-Systemprompt eingebettet |
| 2026-05-27 | GitHub-Sync für Gedächtnis | Mehrere Rechner können dieselbe JSON-Datei über GitHub Contents API nutzen; Token wird lokal per DPAPI gespeichert |
| 2026-05-28 | GitHub-Verbindungsbutton | Einstellungen zeigen grün/rot, testen die GitHub-Verbindung und können den Sync direkt trennen |
| 2026-05-28 | App-Aktionen in der Pille | Zahnrad öffnet Einstellungen, X beendet Lumi ohne Umweg über das Tray |
| 2026-05-28 | GitHub-Verbindungstest differenziert 404 | Lumi prüft jetzt zuerst Repo-Zugriff, dann Branch, dann Memory-Datei; Fehlermeldungen nennen die konkrete Ursache |
| 2026-05-28 | Einstellungen verstecken Overlay | Die Topmost-Pille lag über dem Einstellungsfenster; beim Öffnen der Einstellungen wird sie jetzt ausgeblendet |
| 2026-05-28 | Explizite Gedächtnis-Befehle | `Merke:` speichert Fakten direkt ohne Chat-Antwort; `Zeig Gedächtnis` zeigt gespeicherte Fakten in der Pille |
| 2026-05-28 | Pille-Drag robuster gemacht | Interaktive Controls fingen Mausereignisse ab; Drag nutzt jetzt PreviewMouse und ReleaseCapture |
| 2026-05-28 | Mehrzeilige Gesprächseingabe | Lange Markdown-Inhalte können eingefügt werden; Enter erzeugt Zeilenumbrüche, Strg+Enter sendet |
| 2026-05-28 | Splitter zwischen Ausgabe und Eingabe | Nutzer kann die Höhe von Antwortbereich und Eingabefeld direkt per Drag-and-drop anpassen |
| 2026-05-28 | Sichtbare Custom-Modellauswahl | Per OpenRouter-Vollsuche oder manuell gesetzte Modell-IDs bleiben in den Modell-Dropdowns sichtbar, auch wenn sie nicht in `PopularModels` stehen |
| 2026-05-28 | Automatische EXE-Versionen | `build/build.ps1` erhöht `build/version.json` und legt genau eine versionierte EXE als `Lumi-<Version>.exe` ab |
| 2026-05-28 | Bestehendes Versionsschema übernommen | Version steht auf `3.3.1`; der nächste automatische Build erhöht die Patch-Version auf `3.3.2` |
| 2026-05-28 | Ein Release-Artefakt pro Build | Es werden keine parallelen `Lumi.exe`-/`Lumi_ohne_Api.exe`-Kopien mehr erzeugt; der Release-Name reicht als Versionsnachweis |
| 2026-05-28 | Aktives Modell sichtbar | Die editierbaren ComboBox-Felder in den Einstellungen haben nun einen dunklen inneren Textbereich, damit Custom-Modelle wie `moonshotai/kimi-k2.6:free` lesbar sind |
| 2026-05-28 | ComboBox-Anzeige robust überblendet | Die weißen Windows-ComboBox-Flächen zeigen die aktive Modell-ID nun per eigener Textüberblendung, unabhängig vom nativen ComboBox-Template |
| 2026-05-28 | Pille-X robuster gemacht | X- und Zahnrad-Buttons liegen per Z-Index über der Pille und lösen ihre Aktion schon im Preview-Mausereignis aus |
| 2026-05-28 | Pille-Mausbedienung per HitTest | Drag, Resize und Aktionsbuttons werden nun über `WM_NCHITTEST` getrennt, damit transparente WPF-Flächen keine Klicks verschlucken |
| 2026-05-28 | Nur eine Lumi-Instanz | Ein benannter Mutex verhindert, dass mehrere Lumi-Prozesse parallel geöffnet werden |
| 2026-05-28 | Modell-Historie | Über Suche gewählte OpenRouter-Modelle werden in der Konfiguration gemerkt; Modellzeilen bekommen ein X zum Entfernen aus der Liste |
| 2026-05-29 | VAD-Nachlauf konfigurierbar | Nutzer können einstellen, wie lange Lumi nach der letzten Sprache wartet, bevor die Transkription startet |
| 2026-05-30 | Freihand-Diktatbutton | Diktat kann per Maus gestartet werden und endet automatisch über den konfigurierten VAD-Nachlauf |
| 2026-05-30 | Version 3.4.0 | Freihand-Diktat und Versionsanzeige werden als Minor-Release 3.4.0 ausgeliefert |
| 2026-05-30 | Version 3.4.1 | Overlay nimmt beim Freihand-Diktat keinen Fokus mehr weg; Smoother-Fehler fallen auf Rohtext zurück |
| 2026-05-31 | Diktat-Historie im Overlay | Die letzten fünf Diktate bleiben sitzungsintern abrufbar, editierbar und einzeln in die Zwischenablage kopierbar |
| 2026-05-31 | Kompakte Pille | Overlay kann bis 320 x 150 verkleinert werden; gespeicherte kleine Größen werden beim Start respektiert |
| 2026-05-31 | Version 4.0.0 | Diktat-Historie und kompakte Overlay-Skalierung werden als Major-Release 4.0.0 ausgeliefert |
| 2026-05-31 | Diktat-Einfügemodus | Kompakter Toggle in der Pille schaltet persistent zwischen sofortigem Einfügen und Nachbearbeitung im Historienfeld um |
| 2026-05-31 | Version 4.0.1 | Umschaltbarer Diktat-Einfügemodus wird als Patch-Release 4.0.1 ausgeliefert |
| 2026-05-31 | Diktat-Einfügemodi getrennt | Sofortmodus fügt am Cursor ein und öffnet den Editor nicht automatisch; Stiftmodus öffnet ausschließlich das editierbare Historienfeld |
| 2026-05-31 | Stille-Halluzinationsfilter | Typische Whisper-Phantomtexte wie `Vielen Dank` werden bei leeren Aufnahmen verworfen |
| 2026-05-31 | Version 4.0.2 | Diktatmodus-Fix und aufgeräumte Overlay-Leiste werden als Patch-Release 4.0.2 ausgeliefert |
| 2026-06-02 | Diktat-Verarbeitung serialisiert | VAD-Stille und Hotkey-Loslassen konnten dieselbe Aufnahme parallel stoppen; Audio-Lifecycle und Pipeline sind nun gegen Doppel-Stop abgesichert |
| 2026-06-02 | Async Event-Fehler abgefangen | Hotkey-, Freihand- und VAD-Einstiegspunkte lassen Hardware- oder Fokusfehler nicht mehr ungefangen bis zur WPF-Nachrichtenschleife durch |
| 2026-06-02 | Version 4.0.3 | Stabilitätsfix für sporadische Abstürze nach dem Diktat wird als Patch-Release 4.0.3 ausgeliefert |
| 2026-06-18 | Statische Landingpage | Download, Schnellstart, Hotkeys und API-Key-Beschaffung werden als direkt öffnbare HTML-Seite im Repo dokumentiert |
| 2026-06-18 | GitHub-Pages-Vorbereitung | Landingpage enthält eigene Assets im `website`-Ordner und einen vorbereiteten GitHub-Release-Downloadlink |
| 2026-07-01 | Version 4.5.0 | Lumi konzentriert sich auf die persistenten Modi Diktat und Vorschlag; der Gesprächsmodus entfällt |
| 2026-07-01 | Kompakte Pille | Das Overlay kann dauerhaft auf 300 × 84 Pixel eingeklappt werden und öffnet sich für Vorschauen temporär |
| 2026-07-01 | Sicherer Vorschlagsablauf | Auswahl wird erst nach vollständigem Loslassen von Win+J gelesen und vor dem Ersetzen erneut geprüft |
| 2026-07-01 | Persönliches Wörterbuch | Bestätigte Korrekturen steuern Groq per Prompt und werden lokal deterministisch nachkorrigiert |
| 2026-07-01 | GitHub-Memory Version 2 | Die bestehende JSON behält Fakten und ergänzt zusammenführbares, synchronisiertes Vokabular |
| 2026-07-14 | Push-to-talk endet nur per Tastenfreigabe | VAD bleibt dem Freihand-Diktat vorbehalten; nach dem ersten Key-Up stoppt das Mikrofon sofort, Tastaturaktionen warten jedoch auf die vollständige Freigabe von Win und J |
| 2026-07-14 | Git-Tags sind die Release-Version | Tags im Format `vX.Y.Z` überschreiben die Assembly-Version beim Build und erzeugen über GitHub Actions automatisch EXE, Portable-ZIP und Release Notes |
| 2026-07-14 | Version 4.5.1 | Stabilitätsfix für längere Push-to-talk-Diktate und tagbasierte Release-Automatik werden als Patch-Release 4.5.1 ausgeliefert |
| 2026-07-14 | Landingpage folgt dem Latest-Release | Die GitHub-Pages-Seite liest Version, EXE-Link und Dateigröße aus der öffentlichen GitHub-Releases-API; statische 4.5.1-Werte bleiben als Offline-Fallback |
| 2026-07-16 | Win+J vollständig im Low-Level-Hook | Windows 11 belegt Win+J inzwischen für Recall; Lumi unterdrückt die physische J-Sequenz und neutralisiert die Win-Taste mit F15, bevor Windows sie auswertet |
| 2026-07-16 | Lange Groq-Transkriptionen werden fehlertolerant | Das frühere 30s-Limit war für längere Aufnahmen zu knapp; jetzt gelten 180s pro Versuch, bis zu drei Versuche und eine 16-MB-WAV-Teilung |
| 2026-07-16 | Version 4.5.2 | Recall-Hotkey-Abschirmung und robuste Langdiktat-Transkription wurden mit EXE, Portable-ZIP, deutschem Release-Text und aktualisierter Website veröffentlicht |
| 2026-07-16 | Direkte Texteingabe ohne Clipboard | Diktate und bestätigte Ersetzungen werden per SendInput als Unicode-Tastaturereignisse eingefügt; vorhandene Zwischenablageinhalte bleiben verfügbar |
| 2026-07-17 | Version 4.5.3 | Der vollständig unterdrückte J-KeyUp erreicht den Windows-Tastenzustand nicht zuverlässig; die Pipeline fragt deshalb den bereits im Low-Level-Hook gepflegten physischen Zustand von Win und J ab |
| 2026-07-17 | Strg-Maskierung statt F15 | Die künstliche F15-Taste kann auf Notebook-Hilfssoftware sichtbare Reaktionen auslösen; ein kurzer injizierter Strg-Impuls neutralisiert die Win-Sequenz ohne Funktionstastenereignis |
| 2026-07-20 | 55-ms-Chord-Puffer für J-vor-Win | Nahezu gleichzeitige Tasten können in umgekehrter Reihenfolge eintreffen; kurzes Puffern verhindert sichtbares J, ohne fremden Text per Backspace zu verändern |
| 2026-07-20 | Ereignisbasierter Diktat-Nachlauf | NAudio `RecordingStopped` ersetzt das blinde 100-ms-Warten; nach bestätigter Hotkey-Freigabe genügen 15 ms Stabilisierung und 30 ms vor Unicode-Eingabe |
| 2026-07-20 | Langdiktate bleiben wiederherstellbar | Teiltranskripte und fertige Texte werden bei späterem Chunk- oder SendInput-Fehler in der Historie gesichert; opt-in Logs enthalten nur technische Metadaten |
| 2026-07-20 | Version 4.5.4 | Hotkey-Reihenfolge, Diktat-Latenz und Fehlerrobustheit werden als lokaler Patch-Release gebündelt |
| 2026-07-24 | Version 4.5.4 veröffentlicht | GitHub-Release, Download-Fallback, README und GitHub-Pages-Website zeigen denselben freigegebenen Stand |
