# Lumi Kurzanleitung

## Was ist Lumi?

Lumi ist ein kleiner Windows-Assistent, der im System-Tray laeuft und per Hotkey eingeblendet wird. Die App ist fuer Spracheingabe, Diktat und Textbearbeitung gedacht.

Die fertige EXE liegt hier:

`C:\Users\thiel\Documents\Codex\260525_Lumi\dist\Lumi-win-x64-single\Lumi.exe`

## Start

1. `Lumi.exe` doppelklicken.
2. Lumi erscheint als Tray-Icon unten rechts in Windows.
3. Beim ersten Start oeffnet Lumi die Einstellungen, falls noch keine API-Keys hinterlegt sind.
4. Trage dort mindestens die benoetigten Keys fuer OpenRouter und Groq ein.

Die Keys werden lokal unter `%APPDATA%\Lumi\` gespeichert und per Windows DPAPI verschluesselt.

## API-Keys erstellen

Lumi braucht aktuell zwei API-Keys:

- **Groq** fuer die Spracherkennung
- **OpenRouter** fuer Textglaettung und KI-Antworten

### Groq-Key

1. Oeffne [console.groq.com](https://console.groq.com/).
2. Melde dich an oder erstelle ein Konto.
3. Oeffne den Bereich fuer API-Keys: [console.groq.com/keys](https://console.groq.com/keys).
4. Erstelle einen neuen API-Key.
5. Kopiere den Key direkt nach dem Erstellen.
6. Oeffne in Lumi die Einstellungen und fuege den Key in das Feld fuer Groq ein.

Offizielle Hilfe: [Groq Quickstart](https://console.groq.com/docs/quickstart)

### OpenRouter-Key

1. Oeffne [openrouter.ai](https://openrouter.ai/).
2. Melde dich an oder erstelle ein Konto.
3. Oeffne den API-Key-Bereich: [openrouter.ai/keys](https://openrouter.ai/keys).
4. Erstelle einen neuen Key.
5. Optional: Setze direkt ein Credit-Limit, damit keine unerwarteten Kosten entstehen.
6. Kopiere den Key direkt nach dem Erstellen.
7. Oeffne in Lumi die Einstellungen und fuege den Key in das Feld fuer OpenRouter ein.

Offizielle Hilfe: [OpenRouter API Keys](https://openrouter.ai/docs/api-keys)

Gib API-Keys nie weiter und speichere sie nicht in Screenshots, Chats oder Git-Repositories. Wenn ein Key versehentlich geteilt wurde, loesche ihn im jeweiligen Anbieter-Dashboard und erstelle einen neuen.

## Wichtigster Hotkey

Der zentrale Hotkey ist:

`Win + J`

Lumi unterscheidet dabei nach Dauer und Wiederholung:

| Aktion | Ergebnis |
|---|---|
| `Win + J` kurz antippen | Pille ein-/ausblenden |
| `Win + J` halten | Push-to-Talk: aufnehmen solange gehalten |
| `Win + J` doppelt antippen | Modus wechseln |
| `Esc` | Overlay schliessen / abbrechen |

Hinweis: `Win + H` ist die Windows-eigene Spracheingabe. Lumi nutzt deshalb standardmaessig `Win + J`.

## Die zwei Modi

Lumi merkt sich den zuletzt gewaehlten Modus dauerhaft.

### 1. Vorschlag

Nutze Lumi, um markierten oder kopierten Text umzuschreiben.

Beispiel mit markiertem Text:

1. Text in Word, Browser, Mailprogramm oder einer anderen App markieren.
2. `Win + J` halten.
3. Sage zum Beispiel: "Mach das freundlicher" oder "Fasse das kuerzer zusammen".
4. `Win + J` loslassen.
5. Lumi zeigt den Vorschlag zuerst als Vorschau.
6. Mit `Enter` oder `Ersetzen` uebernimmst du ihn; alternativ kannst du kopieren, neu versuchen oder abbrechen.

Vor dem Ersetzen prueft Lumi, ob dieselbe Auswahl noch aktiv ist. Andernfalls wird der Vorschlag sicher kopiert.

### 2. Diktiermodus

Nutze Lumi als Sprache-zu-Text-Werkzeug.

Ablauf:

1. Cursor an die gewuenschte Stelle setzen.
2. Zum Diktiermodus wechseln, falls er nicht aktiv ist.
3. `Win + J` halten und sprechen.
4. Beim Loslassen fuegt Lumi den Text direkt am Cursor ein.

Die Pille kann auf eine kompakte Ansicht reduziert werden. Diese Einstellung bleibt ebenso erhalten.

## Einstellungen

Ueber das Tray-Icon kannst du die Einstellungen oeffnen. Dort kannst du unter anderem:

- API-Keys hinterlegen
- Modell auswaehlen
- Langzeit-Gedaechtnis aktivieren
- Gedaechtnis ueber GitHub synchronisieren
- persoenliche Schreibweisen im Woerterbuch pflegen (`falsch => richtig`)
- Farbthema waehlen
- Autostart aktivieren
- Overlay-Position und Groesse anpassen

Das Overlay kann verschoben und in der Groesse veraendert werden. Lumi merkt sich die Position.

Die Einstellungen sind auch direkt in der Pille erreichbar: oben rechts auf das Zahnrad klicken. Das `X` daneben beendet Lumi.

Wenn `Mit Windows starten` aktiv ist, traegt Lumi sich unter `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` ein. Beim Start prueft Lumi den Eintrag und repariert ihn automatisch, falls er auf eine alte EXE zeigt.

## Gedaechtnis

Lumi kann sich langfristige Fakten merken, zum Beispiel wer du bist, wo du arbeitest, welche Projekte wichtig sind oder welche Vorlieben du hast.

1. Oeffne die Einstellungen.
2. Aktiviere `Langzeit-Gedaechtnis aktivieren`.
3. Speichere die Einstellungen.

Lumi speichert das lokale Gedaechtnis unter:

`%APPDATA%\Lumi\memory.json`

Wenn du das Gedaechtnis auf mehreren Rechnern verwenden willst:

1. Erstelle ein privates GitHub-Repository.
2. Erstelle bei GitHub einen Fine-grained Personal Access Token.
3. Gib dem Token fuer dieses Repository `Contents: Read and write`.
4. Aktiviere in Lumi `Gedaechtnis ueber GitHub synchronisieren`.
5. Trage Token, Owner, Repository, Branch und JSON-Pfad ein.
6. Klicke auf den Verbindungsbutton. Gruen bedeutet verbunden, rot bedeutet nicht verbunden. Wenn verbunden, trennt ein erneuter Klick den GitHub-Sync.

Empfohlener JSON-Pfad:

`lumi-memory.json`

Hinweis: Das Gedaechtnis kann persoenliche Informationen enthalten. Verwende dafuer ein privates Repository und teile den Token nicht.

### Gedaechtnis gezielt befuellen

Im Gespraechsmodus kannst du Lumi direkt etwas merken lassen:

```text
Merke: Ich arbeite bei Hiram Haus.
```

oder:

```text
Merke dir: Ich bevorzuge kurze Antworten auf Deutsch.
```

Lumi antwortet dann kurz mit `Gemerkt` und speichert den Fakt in `memory.json` sowie, falls verbunden, bei GitHub.

### Gedaechtnis anzeigen

Nutze im Gespraechsmodus zum Beispiel:

```text
Zeig Gedaechtnis
```

oder:

```text
Was weisst du ueber mich?
```

Dann zeigt Lumi die gespeicherten Fakten direkt in der Pille an.

## Typische Probleme

| Problem | Loesung |
|---|---|
| Keine Reaktion auf Sprache | Pruefen, ob ein Mikrofon angeschlossen und in Windows erlaubt ist |
| API-Fehler | OpenRouter- und Groq-Key in den Einstellungen pruefen |
| Text landet an falscher Stelle | Vor dem Diktieren einmal in das Zieltextfeld klicken |
| `Win + J` reagiert nicht | Pruefen, ob eine Vollbild-App oder ein Spiel den Hotkey blockiert |
| Gmail / Notion ersetzt Text nicht sauber | Diese Apps koennen bei Rich-Text-Feldern zicken; Kompatibilitaetsmodus ist noch als offene Aufgabe geplant |

## Beenden

Rechtsklick auf das Lumi-Tray-Icon und dann `Beenden` waehlen.
