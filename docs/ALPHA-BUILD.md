# Elbwald Digital – Desktop Tools: Alpha Build

Aktueller Preview-Stand:

```text
0.1.0-alpha.1
```

## Ziel

0028 erzeugt erstmals reproduzierbare portable Test-Builds für:

- `linux-x64`
- `win-x64`

Die Builds sind **self-contained**. Auf dem Zielsystem muss daher nicht separat
.NET 10 installiert sein.

Sie sind bewusst **nicht**:

- single-file
- getrimmt
- installerbasiert

Die Modularchitektur lädt Assemblies zur Laufzeit. Single-file und Trimming
bleiben deshalb für diesen Zwischenstand deaktiviert.

## Build unter Linux

Im Repository-Root:

```bash
chmod +x scripts/publish-alpha.sh
./scripts/publish-alpha.sh
```

Das Skript führt vor dem Publish aus:

```text
clean
restore
test
publish linux-x64
publish win-x64
Modulprüfung
Archivierung
```

Ausgabe:

```text
artifacts/alpha/0.1.0-alpha.1/
```

Linux wird immer als `tar.gz` gepackt. Windows wird als ZIP gepackt, wenn das
Kommando `zip` installiert ist; andernfalls bleibt der Publish-Ordner erhalten
und es wird zusätzlich ein `tar.gz` erzeugt.

## Build unter PowerShell

```powershell
./scripts/publish-alpha.ps1
```

Das PowerShell-Skript erzeugt ZIP-Dateien für beide Runtime-Ziele.

## Erwartete Struktur

Beispiel Linux:

```text
publish/linux-x64/
├── Elbwald.DesktopTools.App
├── Elbwald.DesktopTools.App.dll
├── BUILD-INFO.txt
└── Modules/
    ├── MediaAnalyzer/
    │   ├── module.json
    │   └── Elbwald.DesktopTools.MediaAnalyzer.dll
    └── PhotoSort/
        ├── module.json
        └── Elbwald.DesktopTools.PhotoSort.dll
```

Der Publish schlägt absichtlich fehl, wenn eines der beiden aktuellen Module
nicht im Publish-Verzeichnis landet.

## Start

Linux:

```bash
chmod +x Elbwald.DesktopTools.App
./Elbwald.DesktopTools.App
```

Windows:

```text
Elbwald.DesktopTools.App.exe
```

## Standardfenster

Ab 0028 startet das Hauptfenster standardmäßig mit:

```text
1700 × 800
```

Die Mindestgröße bleibt:

```text
980 × 640
```

Damit nutzt die Anwendung einen 1920×1080-Desktop gut aus, ohne standardmäßig
den gesamten Bildschirm zu belegen.

## Daten und Sicherheit

Der portable Publish-Ordner enthält **keine** Originalmedien.

Cache-, Journal- und Recovery-Daten liegen weiterhin im lokalen
Anwendungsdatenbereich des jeweiligen Betriebssystems. Das bedeutet: Der Build
ist ohne Installer startbar, aber die Laufzeitdaten werden bewusst nicht neben
die EXE bzw. das Linux-Binary geschrieben.

Vor dem Publish laufen die bestehenden Tests. Ein fehlgeschlagener Test beendet
das Skript.

## Erster Alpha-Smoke-Test

Für beide Betriebssysteme sollte mindestens geprüft werden:

1. App startet.
2. Menüleiste und Sidebar funktionieren.
3. Media Analyzer wird als Modul geladen.
4. Photo Sort ist unter `Organisieren → Sortieren` erreichbar.
5. Ein kleiner Bildordner lässt sich read-only analysieren.
6. Thumbnail und RAW-Vorschau funktionieren.
7. Zweiter Scan verwendet den SQLite-Cache.
8. Cache-Löschen betrifft nur App-Cache.
9. Recovery-Status ist auf der Startseite erreichbar.
10. Beenden über `Datei → Beenden` funktioniert.
