# Elbwald Digital – Status, Roadmap und Chat-Handoff

Stand: Patch 0037, 09.10.2026

## 1. Kurzfazit

Elbwald Digital ist inzwischen deutlich mehr als eine lose Sammlung kleiner Desktop-Tools.

Die technische Basis hat sich zu einem gemeinsamen Medien-Core mit sicherer Dateioperations-Engine entwickelt. Darauf sitzen bereits ein echter Media Analyzer, ein Inspector-Ansatz und ein weit fortgeschrittener Photo-Sort-Workflow. Parallel hat sich die Produktidee strategisch von einer Tool-Sammlung zu einem lokalen Medien-Workspace über dem normalen Dateisystem erweitert.

Der wichtigste aktuelle Meilenstein ist nicht mehr „kann die App Dateien sortieren?“, sondern:

> Kann Elbwald reale Medienbestände sicher, nachvollziehbar und fehlertolerant analysieren und verändern?

Patch 0037 ist genau dieser Härtungsschritt:
- echtes Copy
- echtes Safe Move
- Recovery
- SHA-256-Verifikation
- Companion-Gruppen
- Source-I/O-Fehler isolieren
- Storage-Health-Hinweise
- zentrales Diagnoseprotokoll

0037 ist aber erst dann abgeschlossen, wenn Build, Tests und die realen Move-/Fehlerpfade lokal grün bestätigt sind.

---

# 2. Produktidentität

## 2.1 Sichtbare Produktidee

Die Navigation bleibt aufgabenorientiert:

- Start
- Analysieren
- Organisieren
- Bearbeiten
- Sichern
- Werkzeuge
- Einstellungen

Der Nutzer soll denken:

> Was möchte ich tun?

und nicht:

> Welcher Medientyp ist das?

Medientypen werden erst innerhalb eines Aufgabenbereiches relevant.

## 2.2 Strategische Erweiterung

Die langfristige Produktidentität ist:

> lokaler Medien-Workspace über dem normalen Dateisystem

Kernprinzip:

```text
Dateisystem = physische Ordnung
Elbwald     = logische Ordnung
```

Elbwald besitzt die Medien nicht. Dateien bleiben normale Dateien und Ordner.

Darüber legt Elbwald später:
- Media Index
- Projekte
- Tags
- Ratings
- Favoriten
- Bearbeitungsrezepte
- Backup-Status
- virtuelle Sammlungen
- Content-Studio-Funktionen

Ein Medium kann dadurch mehreren Projekten angehören, ohne physisch mehrfach gespeichert zu werden.

---

# 3. Aktueller technischer Stand

## 3.1 Stack

- C#
- .NET 10 LTS
- Avalonia 12.x
- MVVM
- CommunityToolkit.Mvvm
- Microsoft.Extensions.DependencyInjection
- System.Text.Json
- SQLite
- xUnit
- Rider
- Windows + Linux als Zielplattformen

## 3.2 Solution-Struktur

Aktuell sinngemäß:

```text
Elbwald.DesktopTools/
├── src/
│   ├── Elbwald.DesktopTools.App/
│   ├── Elbwald.DesktopTools.Contracts/
│   ├── Elbwald.DesktopTools.Core/
│   ├── Elbwald.DesktopTools.UI/
│   └── Modules/
│       ├── Elbwald.DesktopTools.PhotoSort/
│       └── Elbwald.DesktopTools.MediaAnalyzer/
├── tests/
├── docs/
├── scripts/
├── .github/workflows/
├── Directory.Build.props
├── .gitignore
└── Elbwald.DesktopTools.slnx
```

---

# 4. Was bereits weitgehend steht

## 4.1 Host / Shell / Module

Umgesetzt bzw. produktiv nutzbar:
- Host-Anwendung
- Sidebar-Navigation
- klassische Menüleiste
- dynamische Module
- Module Loader
- Registry
- API-/Versionsprüfung
- AssemblyLoadContext
- Fehlerisolierung für Module
- Startseite
- Settings-Seite
- grüne Fluent-artige Designsprache
- zentrale Navigation nach Aufgaben

## 4.2 File Operation Safety

Die sicherheitskritische Grundlage ist sehr weit:

- File Operation Planner
- Safety Preflight
- kein stilles Überschreiben
- freie Speicherprüfung
- `.partial`-Staging
- Flush to Disk
- SHA-256-Verifikation
- persistentes Journal
- persistente Recovery
- Recovery Coordinator
- Startup Recovery Gate
- Journal-Integritätsprüfung
- konservative Recovery
- Cross-Process Safety Lock
- Recovery-UI
- Journal-Reparatur

Grundregel bleibt:

> Bei Unsicherheit wird nichts gelöscht und nichts überschrieben.

## 4.3 Media Core

Vorhanden:
- Dateisystemscanner
- MediaFile-Modell
- Medientyperkennung
- Bild-Metadaten
- EXIF
- RAW-Erkennung
- ARW-Dimensions-Fallback
- Hashing
- Thumbnail-/Preview-Fundament
- RAW Embedded-JPEG Preview
- SQLite Analyse-Cache
- Progress
- Cancellation
- Fehlererfassung
- Zeitmessung

## 4.4 Media Analyzer

Praktisch getestet mit realen Archiven:
- große Verzeichnisse
- RAW-Dateien
- EXIF
- Größen
- Auflösungen
- Kamera-/Objektivinformationen
- Orientierung
- größte Dateien
- Parser-Hinweise
- Cache
- RAW-Vorschau

Analyzer bleibt read-only.

## 4.5 Media Date Resolver

Wichtige Architektur bereits vorhanden:
- mehrere Datumskandidaten
- Confidence
- Primary Source
- Conflicts
- Explanation
- EXIF
- GPS UTC
- Dateiname
- Dateisystemdaten
- konservative Konfliktbehandlung

Zusätzlich:
- Kontext-/Serienerkennung
- wiederkehrende Zeitabweichungen
- keine stille automatische Korrektur

## 4.6 Photo Sort

Photo Sort ist inzwischen deutlich über den ursprünglichen MVP hinaus.

Vorhanden:
- echte Ordnerauswahl
- rekursiver Scan
- Dry Run
- Jahr/Monat
- Jahr/Kamera/Monat
- Confidence-Regeln
- Datumskonflikte
- Preview
- Konfliktprüfung
- Copy-Plan
- Move-Plan
- Progress
- Cancel
- Laufzeit
- RAW/JPEG/XMP-Gruppen
- XMP-Projektion in Execution Plan
- sichere Gruppenlogik
- explizite Ausführungsfreigabe

### Live Copy

Praktisch getestet:
- gleiche HDD
- mehrere GB
- SSD → SSD
- mehrere tausend Operationen
- SHA-256-Verifikation
- Quellen bleiben erhalten

### Live Move

Patch 0037 führt Safe Move ein:

```text
Recovery erzeugen
→ Recovery verifizieren
→ Ziel als .partial schreiben
→ Flush
→ SHA-256
→ Ziel committen
→ Quelle / Recovery / Ziel nochmals verifizieren
→ erst dann Quelle löschen
```

Bei Companion-Gruppen muss die komplette Gruppe vor der ersten Quelllöschung sicher sein.

Move verlangt:
- Datumsfreigabe falls nötig
- separate Quelllöschungsfreigabe
- Text `VERSCHIEBEN`

## 4.7 I/O-Fehler / Storage Health

Durch einen realen defekten Datenträger wurde die Safety-Engine praktisch gehärtet.

Erkannt wurde:
- echter Linux EIO
- UNC / Medium Error
- nicht lesbare Sektoren
- kaputte Quelldatei

Neues Verhalten:
- lokaler Source-Read-Fehler stoppt nicht zwingend den ganzen Job
- betroffene Datei bzw. Companion-Gruppe wird übersprungen
- keine Quelle der Gruppe wird gelöscht
- unabhängige Gruppen dürfen weiterlaufen
- Ziel-/Recovery-/Journalfehler bleiben harte Stop-/Recovery-Fälle

Storage Health kann unter Linux nicht-invasiv ermitteln:
- Mount
- Filesystem
- Volume
- physisches Blockdevice
- Modell
- Seriennummer soweit verfügbar
- HDD/SSD
- Kapazität

Keine automatischen SMART-Selbsttests, kein hdparm, kein fsck, kein badblocks.

## 4.8 Zentrales Diagnoseprotokoll

In 0037 neu vorgesehen:
- SQLite-basierter Diagnostic Event Store
- lokale Datei:
  `~/.local/share/ElbwaldDigital/DesktopTools/Diagnostics/events.db`
- Best Effort
- niemals Teil der Recovery-Wahrheit
- Filter nach Severity / Category / Text
- zentrale Ansicht unter `Werkzeuge → Protokoll`

Strukturierte Ereignisse sollen u.a. enthalten:
- Zeitpunkt
- Schweregrad
- Kategorie
- Modul
- Datei
- Quelle
- Ziel
- Volume
- physisches Gerät
- Modell
- Seriennummer
- OS-Fehler
- Transaction / Group
- Aktion
- Ergebnis

Aktuell muss 0037 nach dem letzten Diagnostics-Hotfix noch lokal gebaut/getestet werden.

---

# 5. Bekannte offene Punkte im aktuellen 0037

0037 darf noch nicht als final grün gelten.

Vor nächstem Feature-Patch zwingend:

1. aktuellen 0037-Build ausführen
2. alle Tests ausführen
3. Diagnostics-Protokoll öffnen
4. künstlichen bzw. realen EIO-Pfad prüfen
5. sicherstellen:
   - App startet
   - Designer hält keinen Process Lock
   - App schließt sauber
   - Process Lock wird freigegeben
   - Copy weiterhin funktioniert
   - Move auf gesundem Testmedium funktioniert
   - Source EIO überspringt nur Datei/Gruppe
   - Job endet mit `CompletedWithIssues`
   - kein unnötiges `RecoveryRequired`
   - Diagnoseereignis wird geschrieben
6. bei Build-/Runtime-Fehlern:
   - weiterhin PATCH 0037
   - NICHT 0038 beginnen

---

# 6. UX-Stand

Die aktuelle Photo-Sort-Oberfläche ist funktional, aber zu voll.

Das ist bekannt und bewusst noch nicht final optimiert.

Später erforderlich:
- weniger gleichzeitig sichtbare technische Details
- Progressive Disclosure
- erweiterte Details einklappen
- Safety-Informationen strukturierter darstellen
- lange Warntexte in kompakte Cards / Details aufteilen
- technische Diagnose und normale Benutzerhinweise trennen

Wichtige UX-Regel:
- längere Verarbeitungsprozesse zeigen immer mindestens die bisherige Dauer
- finale Dauer bleibt nach Abschluss sichtbar

---

# 7. Strategische Roadmap – empfohlene Reihenfolge

## Phase A – 0037 vollständig stabilisieren

Keine neue Funktion beginnen, solange 0037 nicht grün ist.

Ziel:
> Live Copy + Live Move + Recovery + I/O Isolation + Diagnostics reproduzierbar stabil.

Danach 0037 dokumentarisch abschließen.

---

## Phase B – Photo Sort als belastbares 0.1-MVP abschließen

Noch vor neuen großen Modulen:

- Endergebnis / Execution Summary verbessern
- Fehler-/Skipped-Liste sauber aufbereiten
- Diagnostics-Links aus Fehlern
- Recovery-Übergänge prüfen
- Wiederanlauf nach Partial Success testen
- Same-Volume Move
- Cross-Volume Move
- Companion-Gruppen
- Ziel voll
- Recovery-Speicher voll
- Source verschwindet
- Source verändert sich
- Ziel entsteht zwischen Dry Run und Commit
- Cancel vor destruktiver Phase
- Fehler während destruktiver Phase
- große Verzeichnisse
- Windows-End-to-End

Danach kann Photo Sort als erstes ernsthaft nutzbares Modul gelten.

---

## Phase C – Shared Core konsolidieren

Bevor weitere Module dieselben Dienste duplizieren:

- Diagnostics/Event Log finalisieren
- Storage Health abstrahieren
- Windows Storage Provider
- IAppPaths konsequent verwenden
- Settings konsolidieren
- Logging-Abstraktion / optional Serilog
- gemeinsame Result-/Issue-Modelle
- gemeinsame Progress-/Duration-Komponenten
- gemeinsame Inspector-Komponenten

Ziel:
> Analyzer, Sort, Import, Backup und Library Health verwenden dieselben Core-Dienste.

---

## Phase D – Library Health + zentrale Analyse

Sehr sinnvoll als nächster sichtbarer Ausbau.

Warum:
- Analyzer existiert
- Metadata existiert
- Hashing existiert
- Storage Health existiert
- Diagnostics existiert
- Fehlerklassifikation existiert

Library Health kann daraus eine verständliche Gesamtansicht machen:

- nicht lesbare Dateien
- problematische Datenträger
- Metadatenprobleme
- Datumskonflikte
- ungewöhnliche Formate
- mögliche Dubletten
- nicht gesicherte Medien
- wiederkehrende I/O-Probleme

Damit wird die App nicht nur reaktiv, sondern kann Probleme aktiv sichtbar machen.

---

## Phase E – Lokaler Media Index / Medien-Layer

Nach dem stabilen Dateisystem-Fundament sollte die logische Ebene beginnen.

Ziel:
```text
Dateisystem = physische Ordnung
Elbwald     = logische Ordnung
```

Erster kleiner Scope:
- `MediaItems`
- stabile interne ID
- Pfad
- Größe
- Hash
- Medientyp
- bekannte frühere Pfade
- Analysezustand
- Storage Location

Noch keine riesige Bibliotheks-UI bauen.

Zuerst:
- Dateien indexieren
- Pfadänderungen erkennen
- anhand Hash wiederfinden
- Referenzen reparieren

Damit entsteht das Fundament für:
- Projekte
- virtuelle Sammlungen
- Ratings
- Favoriten
- Content Studio
- Backup-Status
- non-destructive Edits

---

## Phase F – Projekte / Content Studio

Erst wenn Media Index stabil ist.

Ein Projekt:
- Name
- Typ
- Zeitraum
- Ort
- Tags
- Medienreferenzen

Automatische Vorschläge:
- EXIF-Datum
- Dateidatum
- GPS
- Kamera
- Pfad
- Tags

Beispiele:
- Urlaub
- Hochzeit
- Geburtstag
- Fotobuch
- Kalender
- freie Sammlung

Wichtig:
- Projektzuordnung ändert Originaldatei zunächst nicht
- Metadaten nur optional zurückschreiben

---

## Phase G – Duplicate Finder / Renamer / Importer

Diese drei profitieren stark vom gemeinsamen Core.

### Duplicate Finder
Zuerst:
- SHA-256 bitidentisch

Später:
- perceptual hash
- ähnliche Bilder

### Media Renamer
Bausteine:
- `{Date}`
- `{Time}`
- `{Year}`
- `{Month}`
- `{Camera}`
- `{OriginalName}`
- `{Counter}`

Immer:
- Dry Run
- Konfliktprüfung
- sichere Ausführung

### Media Importer
Workflow:
```text
Quelle analysieren
→ Dubletten
→ Metadaten
→ Umbenennen
→ Sortieren
→ Kopieren
→ Hash prüfen
```

---

## Phase H – Backup / Sync

Bewusst spät.

Erst wenn:
- File Operations
- Recovery
- Hashing
- Media Index
- Storage Health
- Diagnostics
- Library Health

wirklich belastbar sind.

Zuerst:
- Backup Analyzer
- Backup Verify

Erst danach:
- inkrementelles Backup
- Sync
- Versionshistorie
- mehrere Speicherorte

Backup soll redundant und überprüfbar sein, nicht nur „Dateien kopieren“.

---

## Phase I – Bild / Audio / Video Bearbeitung

Nicht vorziehen, solange die Organisations-/Safety-Schicht nicht stabil ist.

### Bild
Langfristig:
- non-destructive
- Floating Point / 16 Bit
- Belichtung
- HSL
- Tonkurven
- Schwarzweiß
- Presets
- Histogramm
- RAW via LibRaw
- Collagen
- Export

### Audio
Langfristig:
- Tags
- Cover
- CD Import
- Analyzer
- ReplayGain
- einfacher Editor
- EQ
- Restauration
- Spektrogramm
- Rauschreduzierung
- A/B
- Konvertierung
- FLAC-Optimierung

Leitgedanke:
> leistungsfähig, aber verständlich; keine DAW und kein Photoshop-Nachbau.

---

# 8. Was bewusst nicht als Nächstes gemacht werden sollte

Noch nicht priorisieren:

- kompletten Bildeditor
- komplette Audio-Suite
- Fotobuch-Editor
- Cloud
- Login
- KI
- Add-on-Store
- komplexe Sync-Engine
- automatische Online-Metadaten
- aufwendige UI-Politur vor stabiler Funktion

Diese Ideen bleiben gültig, sind aber nachgelagert.

---

# 9. Empfohlene nächste konkrete Patches

## Aktuell
### 0037
Nur Stabilisierung / Hotfixes bis grün.

## Danach
### 0038 – Photo Sort Stabilization / Execution Report
Empfohlen:
- finaler Execution Report
- Completed / CompletedWithIssues
- skipped files/groups
- Diagnostics-Verknüpfung
- Recovery-Zustand
- klare Endzusammenfassung
- keine neue große Funktion

### 0039 – Shared Diagnostics / Storage Health Cleanup
Falls 0037-Integration noch provisorisch:
- Windows Provider
- Event Categories
- Retention
- Event Details
- Export
- Diagnostics Navigation polieren

### 0040 – Library Health Foundation
- zentraler Read-only Health Scan
- nicht lesbare Dateien
- Storage Events
- Metadata Quality
- Date Conflicts
- Hash-basierte Problemhinweise

Danach neu entscheiden:
- Media Index
- Duplicate Finder
- Renamer
- Importer

---

# 10. Patch-Workflow – unverändert verbindlich

Jede Änderung:
```text
Elbwald.DesktopTools-Patch-XXXX.zip
```

Regeln:
- fortlaufende Nummer
- bei Fehlern im aktuellen Patch dieselbe Nummer
- ZIP nur neue/geänderte Dateien
- vollständige Dateien, keine Diffs
- Repo-Struktur erhalten
- PATCH.md
- UTF-8
- LF
- XAML/JSON prüfen
- ZIP-Integrität prüfen
- Safety-Invarianten prüfen

Wenn lokal kein .NET SDK verfügbar ist:
- niemals behaupten, gebaut/getestet zu haben
- nur mechanische Prüfung nennen

User baut/testet lokal mit:

```bash
dotnet clean Elbwald.DesktopTools.slnx
dotnet restore Elbwald.DesktopTools.slnx
dotnet build Elbwald.DesktopTools.slnx
dotnet test Elbwald.DesktopTools.slnx
```

---

# 11. Verbindliche Safety-Regeln

Diese Regeln haben Vorrang vor Komfort und Geschwindigkeit.

- niemals überschreiben
- Source niemals vor verifiziertem Ziel + Recovery löschen
- SHA-256
- Flush to Disk
- Recovery vor destruktivem Schritt
- Journal
- Cross-Process Lock
- Ziel-/Recovery-/Journalfehler => fail closed
- Hardware-I/O-Verdacht deutlich anzeigen
- Source-Read-Fehler dürfen nur lokal isoliert werden, wenn eindeutig
- Companion-Gruppen zusammen behandeln
- keine automatischen riskanten Reparaturversuche
- keine SMART-Selbsttests ohne explizite Benutzeraktion
- bei Ambiguität nichts löschen

---

# 12. Anweisung für einen neuen Chat

Den folgenden Block am Anfang eines neuen Chats verwenden.

---

## CHAT-HANDOFF

Wir entwickeln **Elbwald Digital – Desktop Tools**.

### Stack
- C# / .NET 10
- Avalonia 12.x
- MVVM
- CommunityToolkit.Mvvm
- Microsoft.Extensions.DependencyInjection
- SQLite
- xUnit
- Rider
- Windows/Linux

### Produktidee
Elbwald ist langfristig ein **lokaler Medien-Workspace über dem normalen Dateisystem**.

Grundprinzip:
```text
Dateisystem = physische Ordnung
Elbwald     = logische Ordnung
```

Keine proprietäre Medienablage, kein Importzwang, offline first.

### Navigation
- Start
- Analysieren
- Organisieren
- Bearbeiten
- Sichern
- Werkzeuge
- Einstellungen

Aufgabenorientiert, nicht medientyporientiert.

### Aktueller Patch
**0037**

WICHTIG:
- Solange 0037 nicht vollständig grün gebaut/getestet ist, KEIN 0038.
- Fehler in 0037 werden als SAME-NUMBER-Hotfix 0037 geliefert.

### Stand 0037
Bereits vorhanden:
- Module Host
- Media Core
- Scanner
- EXIF/Metadata
- RAW Preview
- SQLite Analysis Cache
- Media Analyzer
- Date Resolver
- Serien-/Kontextanalyse
- RAW/JPEG/XMP Companion Groups
- Photo Sort Dry Run
- Safe Execution Plan
- Live Copy
- Live Safe Move
- Recovery / Journal
- Process Lock
- Startup Recovery
- SHA-256
- Flush-to-disk
- elapsed duration
- Source-I/O isolation
- Storage Health
- zentrales Diagnostics/Event Log

### Safe Move
Ablauf:
```text
Recovery
→ Recovery Verify
→ Target .partial
→ Flush
→ SHA-256
→ Final Target
→ Source/Recovery/Target Verify
→ erst dann Source löschen
```

RAW/JPEG/XMP-Gruppen bleiben zusammen.

### Hardware-I/O
Ein echter defekter Samsung-HDD-Test hat Linux EIO/UNC produziert.

Regel:
- eindeutiger Source-Read-Fehler => Datei/Companion-Gruppe überspringen
- keine Quelle der Gruppe löschen
- unabhängige Gruppen dürfen weiter
- Ziel-/Recovery-/Journalfehler bleiben harte Stop-/Recovery-Fälle

### Diagnostics
Strukturiertes SQLite-Log:
```text
~/.local/share/ElbwaldDigital/DesktopTools/Diagnostics/events.db
```

Best effort, niemals Recovery-Wahrheit.

Geplante Felder:
- Timestamp
- Severity
- Category
- Module
- Source/Destination
- File
- Volume
- Physical Device
- Model
- Serial
- Error
- Transaction/Group
- Action
- Result

UI: `Werkzeuge → Protokoll`

### Aktueller unmittelbarer Auftrag
ZUERST:
1. aktuellen 0037 lokal build/testen lassen
2. alle Compile-/Runtime-Fehler als Hotfix 0037 beheben
3. Diagnostics-UI prüfen
4. gesunden Safe-Move-Test
5. kontrollierten I/O-Fehlerpfad
6. CompletedWithIssues + Log prüfen
7. Shutdown / Process Lock prüfen

Erst wenn alles grün:
0038 beginnen.

### Vorschlag 0038
Photo Sort Stabilization / Execution Report:
- finaler Job-Report
- CompletedWithIssues
- skipped files/groups
- Diagnose-Verknüpfung
- klare Recovery-/Safety-Zusammenfassung
- keine große neue Funktion

### Danach
- Shared Diagnostics / Storage Health festigen
- Library Health
- Media Index / lokaler Medien-Layer
- Projekte / Content Studio
- Duplicate Finder
- Renamer
- Importer
- Backup Analyzer / Verify
- Backup/Sync erst spät
- Bild/Audio/Video-Bearbeitung später

### UX
Photo Sort ist aktuell funktional, aber überladen.
UI-Optimierung später:
- Progressive Disclosure
- weniger technische Details gleichzeitig
- kompakte Warnungen + Details
- klare Endreports

Längere Vorgänge müssen IMMER Dauer anzeigen.

### Patch-Regeln
- `Elbwald.DesktopTools-Patch-XXXX.zip`
- nur neue/geänderte Dateien
- vollständige Dateien
- Repo-Struktur erhalten
- PATCH.md
- UTF-8/LF
- XAML/JSON/ZIP prüfen
- wenn kein .NET SDK vorhanden: nicht behaupten, gebaut zu haben
- User testet lokal
- Fehler => gleiche Patchnummer

### Build
```bash
dotnet clean Elbwald.DesktopTools.slnx
dotnet restore Elbwald.DesktopTools.slnx
dotnet build Elbwald.DesktopTools.slnx
dotnet test Elbwald.DesktopTools.slnx
```

### Wichtigste Safety-Regel
> Bei Unsicherheit wird nichts gelöscht und nichts überschrieben.

---

# 13. Quellenbasis dieses Statusdokuments

Berücksichtigt wurden:
- `elbwald-digital-app-struktur(2).md`
- `Elbwald-Digital-Desktop-Tools-Projektplan-Stand-0019(1).md`
- `elbwald-digital-feature-absprachen-bild-audio(1).md`
- `elbwald-digital-lokaler-medien-layer(1).md`
- `elbwald-digital-medien-roadmap(2).md`
- aktueller Entwicklungsverlauf bis Patch 0037

