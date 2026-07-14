# Lumi – Anweisungen für Claude Code

## Pflichtlektüre beim Start jeder Session
Lies diese drei Dateien in dieser Reihenfolge, bevor du irgendetwas implementierst:

1. `/docs/Foundations.md` – Vision, Tech-Stack, Hotkeys, Interfaces, verbotene Muster
2. `/docs/Status.md` – Aktuelle Phase, offene Aufgaben, Entscheidungslog
3. `/docs/Architecture.md` – Komponentenstruktur, Datenfluss, Klassen

---

## Verhaltensregeln

- App heißt **Lumi** – kein "TwoKey" im Code, UI oder Kommentaren
- Implementiere **nur**, was in `Status.md` unter 🟢 AKTIV steht
- Halte alle Interfaces aus `Foundations.md` stabil
- Keine API-Keys im Source-Code, in `.csproj` oder im Repository
- Alles async – keine Blocking-Calls auf dem UI-Thread
- Mikrofon **nur** aktiv wenn Hotkey gehalten wird
- Clipboard nach jeder Text-Manipulation wiederherstellen

---

## Status.md nach jeder Aufgabe aktualisieren

- Checkbox abhaken: `- [ ]` → `- [x]`
- Phase fertig: → `✅ ERLEDIGT`, nächste → `🟢 AKTIV`
- Neue Entscheidungen mit Datum in Entscheidungslog
- "Letzte Änderung" und "Nächste Aufgabe" oben aktuell halten
