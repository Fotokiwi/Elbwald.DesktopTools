# Elbwald Digital – Handoff: Modularität, Extension-System & Module API v2

**Stand:** Patch **0091**  
**Datum:** 11.10.2026  
**Projekt:** Elbwald Digital – Linux & Windows Apps  
**Stack:** C# · .NET 10 LTS · Avalonia · MVVM · CommunityToolkit.Mvvm · Microsoft.Extensions.DependencyInjection · System.Text.Json · xUnit · JetBrains Rider

---

# 1. Zweck dieses Dokuments

Dieses Dokument ist als **sauberer Handoff für einen neuen Chat** gedacht.

Es fasst zusammen:

- den aktuellen Produktstand,
- die heutige Modularchitektur,
- deren Stärken und Schwächen,
- die neue Zielarchitektur **Module API v2**,
- das geplante **Extension-/Capability-System**,
- die Rolle von Format-, Bearbeitungs- und Backup-Modulen,
- Architekturregeln,
- Sicherheitsprinzipien,
- Patch-/Arbeitsregeln,
- sowie die nächsten sinnvollen Umsetzungsschritte.

Der nächste Chat kann direkt auf diesem Dokument aufsetzen.

---

# 2. Produktverständnis

Elbwald Digital ist inzwischen kein einzelnes Foto-Sortierwerkzeug mehr.

Das Produkt entwickelt sich zu einem:

> **lokalen, sicheren, modularen und projektorientierten Medien-Workspace**

Grundprinzip:

> **Dateisystem = physische Ordnung**  
> **Elbwald = logische Ordnung**

Die Originaldateien bleiben normale Dateien und Ordner.  
Elbwald legt keine proprietäre Medienablage als Zwang darüber.

Zentrale Eigenschaften:

- offline-first
- kein Cloud-Zwang
- keine Accountpflicht
- Safety-first
- normale Dateien bleiben außerhalb der App nutzbar
- Medien werden intern über stabile Referenzen / Media-IDs verwaltet
- Projekte, Bibliothek und spätere Content-Studio-Funktionen arbeiten primär mit Referenzen statt Dateiduplikaten

---

# 3. Aktuelle Hauptnavigation seit Patch 0091

```text
Start
────────────────────────
Bibliothek
Projekte
────────────────────────
Analysieren
Organisieren
Bearbeiten
────────────────────────
Sichern
Werkzeuge
────────────────────────
Einstellungen
```

Bedeutung:

## Start
Zentrale Übersicht / Einstieg.

## Bibliothek
Lokaler Medienbestand und Media Index.

## Projekte
Eigene logische Welt über dem Dateisystem:
- Projektmetadaten
- Projektmedien
- Referenzen
- perspektivisch Tagebuch
- Reisebuch
- Fotobuch
- Content Studio

## Analysieren
Read-only Prüfung unbekannter Medien.

## Organisieren
Physische Dateiverarbeitung:
- Importieren
- Sortieren
- Umbenennen

## Bearbeiten
Perspektivisch:
- Metadaten
- Bildbearbeitung
- Audiobearbeitung
- Videobearbeitung

## Sichern
Perspektivisch:
- Backup Analyzer
- Backup Verify
- Backup-Ziele
- Cloud-/Remote-Provider

## Werkzeuge
Qualität, Diagnose, Library Health, Wartung.

---

# 4. Aktueller Feature-Stand

## Bereits weitgehend vorhanden

- App-Shell
- Avalonia UI
- MVVM
- Dependency Injection
- modulare Projektstruktur
- dynamischer Modul-Loader
- File Operation Safety Engine
- Copy / Move / Rename Planung
- SHA-256-Verifikation
- kein stilles Überschreiben
- persistente Operation Journals
- Recovery
- Cross-Process Lock
- Media Analyzer
- Import Workflow
- Sortieren
- Umbenennen
- Media Index / Bibliothek
- Thumbnail-/Preview-System
- Projektgrundlage
- stabile Media-IDs
- Analyse-/Import-History
- Startseite mit echter Bibliotheksintegration
- erste Storage-/Library-Health-Bausteine

## Noch offen / später

- Library Health zentral ausbauen
- Duplicate Finder
- Undo / History
- echtes Backup
- Metadata Editor
- Audio Tag
- Bildbearbeitung
- Videobearbeitung
- zusätzliche Formatprovider
- Cloud-Backup-Provider
- Add-on-Verwaltung
- Installer / Update-System
- Theme-System
- große E2E-/Windows-Härtung

---

# 5. Aktueller Modulstatus

Die heutige Modularchitektur ist bereits brauchbar, aber noch nicht vollständig generisch.

## Echte dynamische Module

Aktuell insbesondere:

- `MediaAnalyzer`
- `PhotoSort`

Diese werden über ein Modulsystem geladen.

Ein typisches Modul besteht heute grob aus:

```text
Module/
├── module.json
├── Module-Klasse
├── ViewModel
├── View
└── Assembly
```

Die Module hängen im Idealfall primär von:

- `Elbwald.DesktopTools.Contracts`
- `Elbwald.DesktopTools.UI`
- Avalonia
- CommunityToolkit

ab.

Sie sollen **nicht direkt vom App-Projekt** abhängen.

---

# 6. Was an der heutigen Modularität bereits gut ist

## 6.1 Trennung über Contracts

Ein Modul nutzt zentrale Dienste über Interfaces.

Beispielhaft:

```text
IMediaAnalyzer
IMediaDateResolver
IMediaSourcePicker
IMediaSortPlanner
IMediaSortExecutionPlanner
IMediaSortExecutionExecutor
IMediaIndexService
IOperationHistoryStore
IMediaImportHistoryStore
IStorageSettingsStore
```

Das Modul kennt also die konkreten Implementierungen nicht.

Das ist gut und soll beibehalten werden.

---

## 6.2 Dynamisches Laden

Der Loader kann bereits:

- Modulordner entdecken
- `module.json` lesen
- Modul-ID prüfen
- API-Version prüfen
- Mindest-Host-Version prüfen
- Assembly laden
- `IToolModule` erkennen
- Fehler isolieren
- inkompatible Module überspringen
- App weiter starten lassen

Das Fundament ist damit bereits **echte Plugin-Infrastruktur**.

---

## 6.3 Module sind voneinander relativ unabhängig

Beispiel:

PhotoSort hängt nicht vom MediaAnalyzer-UI-Modul ab.

Statt:

```text
PhotoSort → MediaAnalyzer.dll
```

gilt eher:

```text
PhotoSort
    ↓
Contracts
    ↑
Core
```

Das ist die richtige Richtung.

---

# 7. Schwächen der aktuellen Modularchitektur

## 7.1 Module können eigene Dienste nicht sauber registrieren

Das heutige Modulinterface ist im Kern zu UI-zentriert.

Sinngemäß:

```csharp
public interface IToolModule
{
    string Id { get; }
    ToolModuleDescriptor GetDescriptor();
}
```

Ein Modul kann also im Wesentlichen sagen:

> „Ich bin dieses Tool mit dieser View und diesem ViewModel.“

Aber nicht sauber:

> „Ich bringe zusätzlich eigene Services, Provider, Decoder, Exporter oder Backends mit.“

Das ist für zukünftige Module zu wenig.

---

## 7.2 DI wird zu früh gebaut

Aktueller Ablauf sinngemäß:

```text
DI-Container bauen
↓
Module laden
```

Ziel sollte werden:

```text
Module entdecken
↓
Module registrieren ihre Services / Provider
↓
DI-Container bauen
↓
Module aktivieren
↓
UI starten
```

Das ist ein zentraler Architekturpunkt für **Module API v2**.

---

## 7.3 Build / Publish kennt Module noch explizit

Der Runtime-Loader ist generisch.

Der Build-/Publish-Prozess ist es noch nicht vollständig.

Neue Module müssen teilweise noch im App-Projekt / Publish-Prozess explizit bekannt gemacht werden.

Ziel:

> Alles unter `src/Modules` automatisch bauen und ins Module-Verzeichnis übernehmen.

---

## 7.4 Navigation kennt einzelne Module teilweise noch explizit

Heute muss der Host einzelne Module teilweise kennen und gezielt einsortieren.

Beispielhaft:

```text
elbwald.mediaanalyzer
elbwald.photosort
```

Ziel:

Module deklarieren selbst:

```text
Bereich
Position
Titel
Icon
Sortierung
Capabilities
```

Der Host fragt dann nur noch:

> „Welche Module gehören in Bereich Organisieren?“

---

## 7.5 Nicht alles ist ein echtes Modul

Aktuell direkt App-intern:

- Import
- Rename
- Projects
- Library
- Metadata
- Export
- Library Health
- Media Index
- Operation History

Das ist teilweise völlig sinnvoll.

Wichtig:

> Modularität bedeutet **nicht**, dass jede Seite zwingend eine eigene DLL sein muss.

Bestimmte Dinge sind bewusst Plattformkern.

---

# 8. Neue Zielarchitektur: Module API v2

Die künftige Modularität soll nicht nur beantworten:

> „Wie lade ich ein zusätzliches Tool?“

Sondern:

> **„Wie kann ein Paket Elbwald neue Fähigkeiten geben?“**

Das ist der Kern von **Module API v2**.

---

# 9. Drei Arten von Erweiterungen

## 9.1 Feature Extensions

Eigenständige Werkzeuge oder Module mit eigener UI.

Beispiele:

- Duplicate Finder
- Audio Tag
- Audio Analyzer
- Metadata Cleaner
- Image Converter
- Backup Analyzer
- Image Editor
- Video Inspector

Diese haben typischerweise eigene Views / ViewModels.

---

## 9.2 Capability Provider

Erweitern bestehende Funktionen, ohne zwingend eine eigene Seite zu besitzen.

Beispiele:

- PNG-Support
- TIFF-Support
- WebP-Support
- HEIF-Support
- RAW-Support
- FLAC-Support
- neue Decoder
- Thumbnail-Provider
- Metadatenreader
- Metadatenwriter
- Exportformate

Ein WebP-Modul sollte idealerweise **keinen eigenen Menüpunkt benötigen**.

Nach Installation kann Elbwald automatisch:

- WebP analysieren
- WebP importieren
- WebP thumbnails erzeugen
- WebP in der Bibliothek anzeigen
- WebP-Metadaten lesen
- ggf. schreiben
- WebP exportieren
- WebP im Bildeditor öffnen

---

## 9.3 Service Provider

Bieten externe Dienste oder Backends.

Beispiele:

- S3
- SFTP
- WebDAV
- OneDrive
- Google Drive
- Dropbox
- NAS-spezifische Backends

Diese benötigen meist:

- Capability
- Konfiguration
- Authentifizierung
- ggf. kleine eigene Settings-UI

---

# 10. Plattformkern vs. Erweiterungen

## Plattformkern

Soll fest in Elbwald bleiben:

- App-Shell
- Navigation
- Settings
- Bibliothek / Media Index
- Projekte
- Media IDs
- File Safety
- Recovery
- Operation History
- Storage-Abstraktion
- Thumbnail-/Cache-Grundsystem
- Import-Workflow
- zentrale Capability Registry
- zentrale Provider-Auflösung
- Berechtigungs-/Trust-System
- Logging / Diagnose
- Extension Loader

---

## First-Party Extensions

Sollten künftig möglichst über dieselbe Extension-Infrastruktur laufen:

- Analyzer
- Sortieren
- Duplicate Finder
- Metadata Editor
- Metadata Cleaner
- Audio Analyzer
- Audio Tag
- Image Tools
- Converter
- Backup Analyzer
- Backup Provider
- zusätzliche Formatpakete

---

## Third-Party Extensions

Langfristig möglich.

Dafür später zusätzlich:

- Signaturen
- Trust-Level
- Berechtigungen
- API-Kompatibilität
- eventuell Prozess-Isolation
- Marketplace / Add-on-Verwaltung

Noch nicht kurzfristig nötig.

---

# 11. Formatunterstützung als Capability-System

Das ist einer der wichtigsten Zielbereiche.

Der Importer, Analyzer, Bibliothek oder Exportdialog sollen möglichst **nicht selbst TIFF, PNG, WebP, RAW usw. kennen**.

Sie fragen stattdessen die Registry.

Beispiel:

```csharp
formatRegistry.FindReader(file);
formatRegistry.FindMetadataReader(file);
formatRegistry.FindThumbnailProvider(file);
formatRegistry.FindWriter("image/webp");
```

Entscheidend:

> Nicht fragen: „Ist TIFF installiert?“  
> Sondern: „Wer kann diese Operation mit dieser Datei ausführen?“

---

# 12. Beispiel: TIFF-Erweiterung

Ein TIFF-Paket könnte Provider liefern wie:

```text
IMediaFormatProvider
IImageDecoder
IThumbnailProvider
IMetadataReader
IMetadataWriter
IImageExportProvider
```

Nicht jeder Formatprovider muss alles unterstützen.

Beispiel HEIF:

```text
✓ erkennen
✓ lesen
✓ Thumbnail
✓ Metadaten lesen
– Metadaten schreiben
– Exportieren
```

Elbwald erkennt diese Fähigkeiten dynamisch.

---

# 13. Zielverhalten bei Formatinstallation

Beispiel:

```text
WebP-Paket installieren
        ↓
Elbwald startet
        ↓
Registry entdeckt WebP
        ↓
Analyzer erkennt WebP
        ↓
Import akzeptiert WebP
        ↓
Bibliothek erzeugt WebP-Thumbnails
        ↓
Metadateneditor zeigt WebP
        ↓
Bildeditor öffnet WebP
        ↓
Exportdialog bietet WebP
```

Dabei sollte **keiner dieser bestehenden Bereiche** speziell für WebP geändert werden müssen.

Das ist der gewünschte Reifegrad.

---

# 14. Bearbeiten als modularer Host

Der Bereich **Bearbeiten** sollte langfristig nicht aus fest eingebauten Einzeltools bestehen.

Stattdessen:

```text
Bearbeiten
├── Metadaten
├── Bilder
├── Audio
└── Video
```

Die tatsächlichen Fähigkeiten kommen über Extensions.

---

# 15. Bildbearbeitung modular

Beispiel:

## `Elbwald.Image.Editor.Basic`

liefert:

```text
Crop
Rotate
Exposure
Contrast
White Balance
```

## `Elbwald.Image.Editor.Advanced`

liefert:

```text
Curves
HSL
Denoise
Sharpen
Lens Correction
```

Der Image Editor Host fragt:

```text
Welche IImageEditToolProvider sind installiert?
```

und baut seine Werkzeugliste dynamisch.

---

# 16. Audio modular

Beispiel:

## `Elbwald.Audio.Core`

liefert:

- Audio-Analyse
- Tags
- Cover
- Basis-Metadaten

## `Elbwald.Audio.Editor`

liefert:

- Schneiden
- Normalisieren
- Fade
- Lautstärke
- einfache Bearbeitung

## `Elbwald.Audio.Restoration`

liefert später:

- Rauschreduktion
- Restaurationsfunktionen

Wenn der Nutzer Audio nicht benötigt, bleibt es deinstalliert.

---

# 17. Video modular

Analog:

- Video Decoder Provider
- Metadata Provider
- Thumbnail / Keyframe Provider
- Video Editor Tool Provider
- Export / Transcode Provider

Auch hier soll der Kern nicht jedes Codec-/Formatdetail kennen.

---

# 18. Backup als Provider-System

Der Backup-Kern soll verantwortlich sein für:

- Planung
- Vergleich
- Integrität
- Hashing
- Verify
- Versionen
- Backupstatus
- Jobausführung
- Fehler-/Recovery-Handling

Er soll **nicht wissen**, wie S3, OneDrive oder SFTP technisch funktionieren.

---

## Backup Provider

Beispiele:

```text
Elbwald.Backup.Local
Elbwald.Backup.Sftp
Elbwald.Backup.S3
Elbwald.Backup.OneDrive
Elbwald.Backup.GoogleDrive
Elbwald.Backup.WebDav
```

Provider könnten z. B. implementieren:

```text
IBackupTargetProvider
```

und Fähigkeiten deklarieren:

```text
Read
Write
Delete
Rename
Directories
AtomicReplace
Resume
RemoteHash
Versioning
```

Damit wird ein lokales Laufwerk architektonisch genauso behandelt wie ein Cloudziel.

---

# 19. Capability Registry

Zentrales Herzstück von Module API v2.

Geplant:

```csharp
IExtensionRegistry
```

Beispielhafte Registrierung:

```csharp
registry.AddMediaFormat<TiffProvider>();
registry.AddMetadataProvider<TiffMetadataProvider>();
registry.AddThumbnailProvider<TiffThumbnailProvider>();
registry.AddExportProvider<TiffExportProvider>();

registry.AddImageEditorTool<NoiseReductionTool>();

registry.AddBackupProvider<S3BackupProvider>();
```

Der Core kennt nur Interfaces und Registries.

---

# 20. Mögliche Provider-Contracts

Noch nicht final, aber als Zielrichtung:

## Medienformate

```text
IMediaFormatProvider
IMediaDecoder
IImageDecoder
IAudioDecoder
IVideoDecoder
```

## Metadaten

```text
IMetadataReader
IMetadataWriter
IMetadataCapabilityProvider
```

## Vorschau / Thumbnail

```text
IThumbnailProvider
IPreviewProvider
```

## Export

```text
IExportProvider
IImageExportProvider
IAudioExportProvider
IVideoExportProvider
```

## Bearbeitung

```text
IImageEditToolProvider
IAudioEditToolProvider
IVideoEditToolProvider
IMetadataEditProvider
```

## Backup

```text
IBackupTargetProvider
IBackupTransport
IRemoteStorageProvider
```

## UI / Feature

```text
IToolModule
INavigationContribution
ISettingsContribution
```

---

# 21. Manifest / Capability-Beschreibung

Beispiel:

```json
{
  "id": "elbwald.image.webp",
  "name": "WebP-Unterstützung",
  "version": "1.0.0",
  "apiVersion": 2,
  "capabilities": [
    "media.image.webp.read",
    "media.image.webp.write",
    "media.image.webp.thumbnail",
    "media.image.webp.metadata.read"
  ]
}
```

Zweck:

- Fähigkeiten vor Assembly-Laden erkennen
- Add-on-Verwaltung
- Abhängigkeiten
- Kompatibilität
- Pro-/Lizenzmodelle
- Updates
- Diagnose

---

# 22. Abhängigkeiten zwischen Extensions

Künftig sinnvoll.

Beispiel:

```text
Elbwald.Image.Editor.Raw
        ↓
Elbwald.ImageFormats.Raw
        ↓
Elbwald.Image.Core
```

Ein Paketmanifest soll Abhängigkeiten deklarieren können.

Elbwald kann dann verständlich anzeigen:

> RAW Editor benötigt RAW-Unterstützung.

Statt erst beim Laden an fehlenden DLLs zu scheitern.

---

# 23. Graceful Degradation

Sehr wichtig.

Wenn ein Nutzer z. B. die TIFF-Unterstützung deinstalliert, darf die Bibliothek nicht kaputtgehen.

Der Index kennt weiterhin:

```text
Datei
Media-ID
Pfad
Format
Projektzuordnung
Metadaten
```

Die UI kann anzeigen:

> TIFF-Unterstützung ist derzeit nicht installiert.

Mögliche Folgen:

- Thumbnail eventuell nicht verfügbar
- Vollansicht nicht verfügbar
- Bearbeitung nicht verfügbar

Aber:

- Media-ID bleibt
- Projektzuordnung bleibt
- Indexeintrag bleibt
- Projekt bleibt konsistent

Nach erneuter Installation funktioniert alles wieder.

Grundsatz:

> Extensions liefern Fähigkeiten, aber besitzen nicht die Mediendaten.

---

# 24. Capability ≠ Permission

Künftig sollten zwei Dinge getrennt werden:

## Capability
Was kann das Modul?

Beispiel:

```text
TIFF lesen
WebP exportieren
S3 schreiben
RAW entwickeln
```

## Permission / Trust
Was darf das Modul?

Beispiel TIFF-Decoder:

```text
ReadMedia
```

Beispiel S3-Backup:

```text
ReadMedia
Network
RemoteWrite
```

Beispiel Bildfilter:

```text
kein direkter Dateizugriff nötig
```

Das ist später wichtig für Third-Party-Plugins.

---

# 25. Sicherheitsmodell für Extensions

Für First-Party-Extensions reicht zunächst In-Process.

Langfristig bei Drittanbietern evaluieren:

- Signierung
- Trust-Level
- Permission-Manifest
- API-Version
- Sandboxing
- separater Prozess
- IPC
- Crash-Isolation

Noch kein kurzfristiger Blocker.

---

# 26. Neue Module API v2 – Zielablauf

Heute ungefähr:

```text
DI bauen
↓
Module laden
↓
UI
```

Ziel:

```text
1. Extension-Verzeichnisse finden
2. Manifeste lesen
3. Kompatibilität prüfen
4. Abhängigkeiten auflösen
5. Extensions laden
6. Services / Provider registrieren
7. DI-Container bauen
8. Capability Registry finalisieren
9. Navigation / Settings / UI Contributions registrieren
10. App starten
```

---

# 27. Navigation künftig dynamisch

Module sollen nicht mehr vom Host namentlich einsortiert werden müssen.

Beispiel Manifest / Descriptor:

```text
Category = Organize
Placement = MainSection
Order = 20
Title = Sortieren
Icon = Folder
```

Der Host fragt:

```text
Welche Navigation Contributions existieren?
```

und baut die Navigation daraus.

---

# 28. Build & Publish künftig generisch

Ziel:

Neue Module unter:

```text
src/Modules/
```

werden automatisch:

- gebaut
- gepackt
- ins Modulverzeichnis kopiert
- bei Publish berücksichtigt

Keine manuellen Einträge pro Modul mehr in:

- App `.csproj`
- Publish-Skripten
- Copy-Targets

---

# 29. Wunschziel für neue Module

Idealer Workflow später:

```text
dotnet new elbwald-module -n DuplicateFinder
```

Ergebnis:

```text
DuplicateFinder/
├── module.json
├── DuplicateFinderModule.cs
├── ViewModels/
│   └── DuplicateFinderViewModel.cs
├── Views/
│   └── DuplicateFinderView.axaml
└── DuplicateFinder.csproj
```

Danach:

> Feature implementieren – ohne Änderungen an MainWindow, App.csproj oder zentraler Navigation.

---

# 30. Was bewusst Plattformkern bleiben darf

Nicht alles muss Extension sein.

Bewusst fest:

- Bibliothek
- Projekte
- Settings
- Media Index
- File Safety
- Recovery
- Extension Registry
- Storage
- Startseite
- Navigation
- Operation History
- zentrale Cache-/Thumbnail-Infrastruktur

Projekte sind bewusst **keine kleine Tool-DLL**, sondern Teil der Produktplattform.

---

# 31. Perspektive für optionale Installation

Ziel:

Ein Nutzer kann Elbwald schlank halten.

Beispiel 1:

```text
Elbwald Basis
JPEG
PNG
WebP
Image Editor Basic
```

Beispiel 2:

```text
Elbwald Basis
JPEG
RAW
MP3
FLAC
Audio Tag
SFTP Backup
```

Nicht benötigte Bereiche bleiben deinstalliert.

UI zeigt möglichst nur verfügbare Fähigkeiten.

---

# 32. Monetarisierung passt gut zu dieser Architektur

Mögliche spätere Struktur:

## Kostenloser Kern

- Basisbibliothek
- Projekte
- grundlegende Formate
- sichere Dateifunktionen
- Grundanalyse

## Optionale Add-ons

- zusätzliche Formate
- Advanced Image Editor
- spezielle Audiofunktionen
- Cloud Backup
- Spezial-Exporter
- Pro-Workflows

Wichtig:

> Keine künstliche Einschränkung von Safety-, Integritäts- oder Datenhoheitsfunktionen.

---

# 33. Aktuelle Modulbewertung

Grobe Einschätzung Stand 0091:

| Bereich | Bewertung |
|---|---:|
| Trennung Modul ↔ Host | 8/10 |
| Dynamisches Laden | 8/10 |
| Fehlerisolation | 8/10 |
| Nutzung gemeinsamer Core-Dienste | 9/10 |
| Eigene Modul-Dienste / DI | 4/10 |
| Automatische Navigation | 4/10 |
| Neues Modul ohne Host-Anpassung | 5/10 |
| Fundament insgesamt | 7/10 |

Das Fundament ist also gut genug, um weiterzubauen.

Der nächste Schritt sollte aber bewusst die **Module API v2** sein, bevor:

- Duplicate Finder
- Audio
- Video
- Bildbearbeitung
- Backup
- Cloud Provider
- neue Formate

in größerem Umfang entstehen.

---

# 34. Empfohlener nächster Architekturblock

## Phase A – Module API v2 Contracts

- [ ] Extension Manifest v2
- [ ] Capability IDs
- [ ] Dependency Deklaration
- [ ] Navigation Contribution
- [ ] Settings Contribution
- [ ] Service Registration
- [ ] API-Versionierung
- [ ] Trust-/Permission-Felder vorbereiten

---

## Phase B – Loader umbauen

- [ ] Module vor DI entdecken
- [ ] Manifeste validieren
- [ ] Abhängigkeiten auflösen
- [ ] Services registrieren lassen
- [ ] danach ServiceProvider bauen
- [ ] Fehler einzelner Extensions isolieren
- [ ] Diagnosen für inkompatible Module

---

## Phase C – Capability Registry

- [ ] `IExtensionRegistry`
- [ ] Media Format Registry
- [ ] Metadata Registry
- [ ] Thumbnail Registry
- [ ] Export Registry
- [ ] Editor Tool Registry
- [ ] Backup Provider Registry

---

## Phase D – Navigation dynamisieren

- [ ] Host kennt keine konkreten Tool-IDs mehr
- [ ] Contributions nach Kategorie einsortieren
- [ ] Reihenfolge
- [ ] Icons
- [ ] geplante / optionale Bereiche dynamisch

---

## Phase E – Build / Publish

- [ ] alle Module automatisch bauen
- [ ] automatisch einsammeln
- [ ] Publish automatisch
- [ ] Modulversionen sichtbar
- [ ] Diagnoseansicht installierter Module

---

## Phase F – erster echter Capability-Test

Empfohlener Proof of Concept:

> **WebP- oder TIFF-Support als reine Capability Extension**

Warum?

Weil damit getestet werden kann, ob ein Formatprovider automatisch:

- Analyzer
- Import
- Bibliothek
- Thumbnail
- Metadaten
- Export

erweitern kann, ohne diese Screens anzupassen.

Erst wenn das funktioniert, ist die neue Architektur wirklich bewiesen.

---

# 35. Danach geeignete Module

In sinnvoller Reihenfolge:

1. WebP / TIFF Format Provider
2. Duplicate Finder
3. Metadata Editor
4. Audio Core / Audio Tag
5. Backup Local Provider
6. SFTP / S3 Provider
7. Image Editor Basic
8. Advanced Editor
9. Video Provider
10. Cloud Provider

---

# 36. Architekturprinzipien für alle neuen Extensions

## 1.
Keine direkte Abhängigkeit von `Elbwald.DesktopTools.App`.

## 2.
Möglichst nur gegen `Contracts`, UI-Basis und klar definierte Extension-APIs entwickeln.

## 3.
Core-Dienste werden über Interfaces konsumiert.

## 4.
Keine Extension darf Eigentümer zentraler Media-IDs oder Projektzuordnungen sein.

## 5.
Deinstallation einer Extension darf keine Bibliothek beschädigen.

## 6.
Fehlende Capability muss sauber abgefangen werden.

## 7.
Jede schreibende Medienoperation weiterhin über Safety Engine / definierte Writer laufen.

## 8.
Keine Extension darf still Dateien überschreiben.

## 9.
Capabilities statt konkrete Modulnamen abfragen.

## 10.
UI soll sich aus verfügbaren Fähigkeiten ergeben, wo sinnvoll.

---

# 37. Safety bei Format- und Editor-Modulen

Neue Decoder / Writer / Editoren dürfen Safety nicht umgehen.

Beispiel Export:

```text
Editor
↓
IImageExportProvider
↓
File Operation / Safe Write Pipeline
↓
temp file
↓
flush
↓
verify
↓
atomic replace / final write
```

Keine direkte „einfach Datei überschreiben“-Logik in einem Extension-Modul.

---

# 38. Safety bei Backup-Modulen

Auch Backup Provider sollen nicht frei „irgendwas kopieren“.

Der Backup Core steuert:

- Planung
- Quelle
- Hash
- Vergleich
- Retry
- Verify
- Journal
- Fehlerzustand

Der Provider implementiert nur den Transport / das Ziel.

---

# 39. Wichtiger Produktgrundsatz

Neue Formate, neue Backends und neue Bearbeitungswerkzeuge sollen sich möglichst so verhalten:

> Installieren → Fähigkeit erscheint automatisch.

Nicht:

> Installieren → fünf andere Bereiche müssen manuell angepasst werden.

Das ist der Maßstab für Module API v2.

---

# 40. Aktueller Arbeitsstand / letzte Patches

## Patch 0087
Sortieren:
- Datumsprüfung interaktiv
- alternatives Datum
- manuelles Datum
- „Datum unklar“
- Datei überspringen
- Massenentscheidung
- Copy ohne unnötige Textbestätigung

## Patch 0088 / 0089
Umbenennen:
- UX-Neuaufbau
- Presets
- Live-Beispiel
- echte Quellbeispiele
- Kollisionen
- bessere Primäraktionen

## Patch 0090
Projekte:
- deutlich bessere Projekt-UX
- verständlichere Metadaten
- Medienzuordnung übersichtlicher

## Patch 0091
Projekte:
- eigener Hauptmenüpunkt
- Navigations-Trenner
- Projektübersicht
- Projektarbeitsbereich
- Bereiche:
  - Übersicht
  - Medien
  - Tagebuch
  - Informationen
- Projektkachel aus Organisieren entfernt

---

# 41. Aktuelle strategische Priorität

Die große Roadmap wurde auf Stand 0091 neu geschrieben.

Die aktuelle Priorität war zuletzt:

```text
Konsolidierung
    ↓
Projekte-MVP abrunden
    ↓
Library Health
    ↓
Duplicate Finder
    ↓
Undo / History
    ↓
Backup
```

Mit der neuen Modularitätsentscheidung sollte nun eingeschoben werden:

```text
Konsolidierung
    ↓
Module API v2 / Capability System
    ↓
erster Format-Provider als Proof of Concept
    ↓
Projekte / Library Health
    ↓
Duplicate Finder
    ↓
Undo / History
    ↓
Backup Provider Architektur
```

---

# 42. Patch-Regeln

Verbindlich:

- jeder Patch als ZIP
- Originalordnerstruktur beibehalten
- nur neue/geänderte Dateien
- `PATCH.md` beilegen
- keine Dateien still löschen
- Löschanweisungen immer separat im Chat
- Safety-relevante Änderungen explizit dokumentieren
- möglichst kleine nachvollziehbare Patches
- Patchnummern fortlaufend

Nächster sinnvoller Patchblock:

> **0092 ff. – Module API v2**

---

# 43. Technische Randbedingungen der bisherigen Arbeit

- `dotnet` war in der Tool-Umgebung nicht installiert
- daher keine lokalen Builds durch ChatGPT möglich
- XAML/XML wurde strukturell geprüft
- Tests wurden ergänzt, aber müssen lokal ausgeführt werden
- lokale Build-/Runtime-Ergebnisse des Nutzers sind weiterhin maßgeblich

---

# 44. Nächster Chat – empfohlener Startprompt

Beispiel:

> Wir machen bei Elbwald Digital auf Stand 0091 weiter.  
> Lies das Handoff-Dokument zur Module API v2 vollständig.  
> Ziel ist jetzt, die vorhandene Modularchitektur zu einem echten Extension-/Capability-System auszubauen.  
> Neue Formate, Bearbeitungsfunktionen und Backup-Provider sollen künftig möglichst nur als DLL/Extension installiert werden und automatisch von Analyzer, Import, Bibliothek, Metadaten, Export und Editoren genutzt werden.  
> Beginne mit einer Bestandsaufnahme der aktuellen Modul-Contracts und plane Patch 0092 als ersten sauberen Schritt der Module API v2.

---

# 45. Kurzfassung für den neuen Chat

> **Elbwald hat bereits eine funktionierende Plugin-Basis, aber sie ist aktuell hauptsächlich Tool-/View-basiert.**
>
> Die nächste Architekturphase soll daraus ein **Capability-/Provider-System** machen.
>
> Künftig sollen Extensions neue Fähigkeiten liefern:
>
> - Dateiformate
> - Decoder
> - Metadaten
> - Thumbnails
> - Export
> - Bild-/Audio-/Videobearbeitung
> - Backupziele
> - externe Dienste
>
> Der Plattformkern bleibt stabil und fragt nur:
>
> **„Welche installierte Capability kann diese Aufgabe erledigen?“**
>
> Ziel:
>
> **Extension installieren → bestehende Elbwald-Workflows können die neue Fähigkeit automatisch nutzen.**

