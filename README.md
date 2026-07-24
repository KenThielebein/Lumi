# Lumi

Lumi ist ein nativer Windows-Assistent für Diktate und sichere Textvorschläge.
Die App wartet unauffällig im System-Tray und wird über `Win + J` bedient.

## Aktuelle Version

**Lumi 4.5.5**

- blockiert Windows Recall und die nachfolgende Windows-Hello-Abfrage auch bei Tastaturen mit OEM-Treiber oder Remoting-Eingabe
- erkennt `Win + J` auch dann zuverlässig, wenn `J` wenige Millisekunden zuerst eintrifft
- verarbeitet Diktate mit kürzerem festem Nachlauf
- sichert bereits erkannten Text bei späteren Transkriptions- oder Einfügefehlern
- fügt Diktate direkt per Unicode-Eingabe ein, ohne die Zwischenablage zu verändern

[Lumi 4.5.5 herunterladen](https://github.com/KenThielebein/Lumi/releases/latest)

[Lumi-Website öffnen](https://kenthielebein.github.io/Lumi/)

## Bedienung

- `Win + J` kurz: Lumi ein- oder ausblenden
- `Win + J` halten: Push-to-Talk-Diktat oder Textanweisung aufnehmen
- `Win + J` doppelt: zwischen Diktat und Vorschlag wechseln
- `Esc`: aktuelle Aktion abbrechen

API-Schlüssel werden lokal unter `%APPDATA%\Lumi\` gespeichert und mit Windows DPAPI verschlüsselt. Das Mikrofon ist nur während einer bewusst gestarteten Aufnahme aktiv.
