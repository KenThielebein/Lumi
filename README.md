# Lumi

Lumi ist ein nativer Windows-Assistent für Diktate und sichere Textvorschläge.
Die App wartet unauffällig im System-Tray und wird über `Strg + #` bedient.

## Aktuelle Version

**Lumi 4.5.6**

- verwendet `Strg + #` statt einer von Windows reservierten Tastenkombination
- akzeptiert die linke und rechte Strg-Taste sowie eine knapp zuerst gedrückte Rautentaste
- lässt Windows Recall und Windows Hello vollständig unberührt
- verarbeitet Diktate mit kürzerem festem Nachlauf
- sichert bereits erkannten Text bei späteren Transkriptions- oder Einfügefehlern
- fügt Diktate direkt per Unicode-Eingabe ein, ohne die Zwischenablage zu verändern

[Lumi 4.5.6 herunterladen](https://github.com/KenThielebein/Lumi/releases/latest)

[Lumi-Website öffnen](https://kenthielebein.github.io/Lumi/)

## Bedienung

- `Strg + #` kurz: Lumi ein- oder ausblenden
- `Strg + #` halten: Push-to-Talk-Diktat oder Textanweisung aufnehmen
- `Strg + #` doppelt: zwischen Diktat und Vorschlag wechseln
- `Esc`: aktuelle Aktion abbrechen

API-Schlüssel werden lokal unter `%APPDATA%\Lumi\` gespeichert und mit Windows DPAPI verschlüsselt. Das Mikrofon ist nur während einer bewusst gestarteten Aufnahme aktiv.
