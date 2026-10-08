# 🌲 Elbwald Digital – Desktop Tools

> **Einfach. Sicher. Einheitlich.**  
> Eine moderne, plattformübergreifende Desktop-Tool-Suite für **Windows** und **Linux** auf Basis von **C# / .NET 10 / Avalonia**.

**Aktueller Entwicklungsstand:** Patch **0019** · lokal erfolgreich gebaut und getestet · Stand **07.10.2026**

> **Status-Legende:**  
> `[x]` umgesetzt · `[ ]` offen · `teilweise` = technische Grundlage vorhanden, aber noch nicht vollständig als Endnutzer-Workflow umgesetzt.

---

## Inhaltsverzeichnis

1. [Projektziel](#1-projektziel)
2. [Technischer Stack](#2-technischer-stack)
3. [Architektur](#3-architektur)
4. [Solution-Struktur](#4-solution-struktur)
5. [Add-on-System](#5-add-on-system)
6. [Gemeinsame Dienste](#6-gemeinsame-dienste)
7. [File-Operation-Engine](#7-file-operation-engine)
8. [Undo-System](#8-undo-system)
9. [UX- und Designsystem](#9-ux--und-designsystem)
10. [Startseite](#10-startseite)
11. [Photo Sort](#11-photo-sort)
12. [Audio Tag](#12-audio-tag)
13. [Projektinitialisierung in Rider](#13-projektinitialisierung-in-rider)
14. [Git & GitHub](#14-git--github)
15. [Branch- und Commit-Strategie](#15-branch--und-commit-strategie)
16. [GitHub Actions](#16-github-actions)
17. [Roadmap & To-do](#17-roadmap--to-do)
18. [Bewusst nicht Teil des MVP](#18-bewusst-nicht-teil-des-mvp)
19. [GitHub Milestones](#19-github-milestones)
20. [Erstes konkretes Entwicklungsziel](#20-erstes-konkretes-entwicklungsziel)

---

# 1. Projektziel

**Elbwald Digital – Desktop Tools** soll eine moderne Sammlung kleiner, nützlicher Desktop-Werkzeuge werden.

Die Anwendung besteht aus einem schlanken Host und nachladbaren Modulen:

```text
Elbwald Digital – Desktop Tools
│
├── Host
│   ├── Startseite
│   ├── Navigation
│   ├── Einstellungen
│   ├── Add-on-Verwaltung
│   └── gemeinsame Dienste
│
├── Photo Sort
├── Audio Tag
└── zukünftige Add-ons
```

## Produktprinzipien

### Einfach
Ein unerfahrener Nutzer soll ohne technische Vorkenntnisse ans Ziel kommen.

### Sicher
Keine Datei wird überraschend gelöscht, überschrieben oder verändert.

### Einheitlich
Alle Werkzeuge fühlen sich wie Teile derselben Anwendung an.

---

# 2. Technischer Stack

| Bereich | Entscheidung |
|---|---|
| Sprache | C# |
| Runtime | .NET 10 LTS |
| UI | Avalonia 12.x |
| UI-Architektur | MVVM |
| MVVM | CommunityToolkit.Mvvm |
| Dependency Injection | Microsoft.Extensions.DependencyInjection |
| Logging | Serilog |
| Konfiguration | System.Text.Json |
| Datenbank | SQLite nur bei tatsächlichem Bedarf |
| Tests | xUnit |
| Versionsverwaltung | Git |
| Remote | GitHub |
| IDE | JetBrains Rider |
| Zielplattformen | Windows x64 + Linux x64 |

---

# 3. Architektur

## Grundidee

Host und Add-ons werden strikt getrennt.

```text
                 DesktopTools.App
                       │
          ┌────────────┼──────────────┐
          │            │              │
         Core      Contracts          UI
                       │
             ┌─────────┴────────┐
             │                  │
         PhotoSort           AudioTag
```

## `Contracts`

Enthält ausschließlich stabile Verträge zwischen Host und Add-ons:

- Interfaces
- DTOs
- Modul-Metadaten
- gemeinsame Capability-Definitionen

`Contracts` sollte möglichst wenig Abhängigkeiten besitzen.

## `Core`

Allgemeine Logik:

- Dateisystem
- Jobs
- Dateioperationspläne
- Undo-Journal
- Settings
- Logging-Abstraktionen
- Modulverwaltung
- App-Pfade
- allgemeine Services

## `UI`

Gemeinsame Oberflächenbestandteile:

- Theme
- Buttons
- Cards
- Dialoge
- Benachrichtigungen
- Progress-Anzeigen
- Empty States
- Error States
- Icons
- Converter

## `App`

Die ausführbare Host-Anwendung:

- Application Bootstrap
- Dependency Injection
- MainWindow
- Navigation
- Modulregistrierung
- Settings
- Shell

## Module

Fachliche Funktionalität:

- Photo Sort
- Audio Tag
- zukünftige Add-ons

---

# 4. Solution-Struktur

```text
Elbwald.DesktopTools/
│
├── src/
│   │
│   ├── Elbwald.DesktopTools.App/
│   ├── Elbwald.DesktopTools.Contracts/
│   ├── Elbwald.DesktopTools.Core/
│   ├── Elbwald.DesktopTools.UI/
│   │
│   └── Modules/
│       ├── Elbwald.DesktopTools.PhotoSort/
│       └── Elbwald.DesktopTools.AudioTag/
│
├── tests/
│   ├── Elbwald.DesktopTools.Core.Tests/
│   ├── Elbwald.DesktopTools.PhotoSort.Tests/
│   └── Elbwald.DesktopTools.AudioTag.Tests/
│
├── docs/
│   ├── architecture/
│   └── ux/
│
├── .github/
│   └── workflows/
│
├── .editorconfig
├── .gitignore
├── Directory.Build.props
├── Directory.Packages.props
├── README.md
└── Elbwald.DesktopTools.sln
```

## Repository-Strategie

Ein **Monorepository** für Host, gemeinsame Bibliotheken und alle offiziellen Add-ons.

Vorteile:

- gemeinsame Änderungen bleiben atomar
- zentrale Paketversionen
- einfachere CI
- weniger Versionschaos
- gemeinsame Tests
- gemeinsame Releases

---

# 5. Add-on-System

Intern werden Add-ons als **Modules** bezeichnet.

In der Benutzeroberfläche darf weiterhin **Add-ons** oder **Werkzeuge** stehen.

## Modul-Manifest

Beispiel `module.json`:

```json
{
  "id": "elbwald.photosort",
  "name": "Photo Sort",
  "description": "Fotos automatisch organisieren und sortieren",
  "version": "0.1.0",
  "apiVersion": 1,
  "minimumHostVersion": "0.1.0",
  "assembly": "Elbwald.DesktopTools.PhotoSort.dll"
}
```

## Modulschnittstelle

```csharp
public interface IToolModule
{
    string Id { get; }

    void RegisterServices(IServiceCollection services);

    ToolModuleDescriptor CreateDescriptor();
}
```

```csharp
public sealed record ToolModuleDescriptor(
    string Id,
    string Name,
    string Description,
    string Icon,
    Type ViewModelType,
    Type ViewType);
```

## Ladeprozess

```text
Programmstart
    ↓
Modules-Verzeichnis suchen
    ↓
module.json lesen
    ↓
Kompatibilität prüfen
    ↓
Assembly laden
    ↓
IToolModule suchen
    ↓
Services registrieren
    ↓
DI-Container erzeugen
    ↓
Navigation aufbauen
```

## Version 1: bewusst einfach

Noch nicht implementieren:

- Hot Reload von Add-ons
- Entladen während der Laufzeit
- automatische Add-on-Updates
- Live-Austausch von Abhängigkeiten

Stattdessen:

```text
Add-on installieren
→ Anwendung neu starten
→ Add-on verfügbar
```

## AssemblyLoadContext

Jedes Add-on kann später einen eigenen `AssemblyLoadContext` erhalten.

Wichtig:

```text
Default AssemblyLoadContext
├── Elbwald.DesktopTools.Contracts
├── Elbwald.DesktopTools.Core
└── Elbwald.DesktopTools.App

Plugin LoadContext
└── PhotoSort
    ├── Elbwald.DesktopTools.PhotoSort.dll
    └── eigene Dependencies
```

`Contracts` muss vom Host und allen Modulen gemeinsam verwendet werden.

---

# 6. Gemeinsame Dienste

Der Host stellt Add-ons zentrale Services zur Verfügung.

```csharp
IFilePickerService
IFolderPickerService

IDialogService
INotificationService

IJobService
IProgressService

ISettingsService

IFileOperationPlanner
IFileOperationExecutor
IUndoService

IAppPaths
ILogger
```

## Regel

Ein Add-on öffnet nicht selbstständig eigene Fenster oder Dialoge.

Statt:

```csharp
new SomeDialog().Show();
```

wird verwendet:

```csharp
await dialogService.ConfirmAsync(...);
```

Dadurch bleiben Design, Verhalten und UX überall konsistent.

---

# 7. File-Operation-Engine

Die zentrale Dateioperations-Engine ist ein Kernbestandteil der Suite.

**Status: technische Sicherheitsbasis weitgehend umgesetzt.**  
Der Endnutzer-Workflow in Photo Sort ist noch nicht angebunden.

## Aktueller Ablauf

```text
Add-on
   ↓
FileOperationRequest
   ↓
FileOperationPlanner
   ↓
Konfliktprüfung / Operationsplan
   ↓
Cross-Process-Lock
   ↓
Startup-/Recovery-Gate
   ↓
Safety Preflight
   ↓
persistentes Journal
   ↓
Recovery-Sicherung bei destruktiven Vorgängen
   ↓
staged Write (.partial)
   ↓
Flush + SHA-256-Verifikation
   ↓
Commit
```

Aktuell unterstützt die Engine:

```text
Move
Copy
```

`Rename` folgt später als eigener Operationstyp.

Mehrere Operationen ergeben einen:

```csharp
FileOperationPlan
```

## Bereits umgesetzte Sicherheitsregeln

- niemals blind überschreiben
- Konflikte bereits im Plan erkennen
- Quelle und Ziel unmittelbar vor der Ausführung erneut prüfen
- freien Speicher inklusive Sicherheitsreserve prüfen
- Kopien zunächst als `.partial` schreiben
- Daten vor Commit auf Disk flushen
- Kopien per SHA-256 verifizieren
- persistentes JSONL-Operationsjournal
- persistente Recovery-Sicherung vor kritischen Move-Schritten
- Recovery-Zustände beim Programmstart erkennen
- neue Dateioperationen bei ungeklärtem Recovery-Zustand fail-closed sperren
- beschädigte Journale nicht als „sauber“ behandeln
- nur eindeutig abgebrochene letzte Journalzeilen automatisch reparieren
- beschädigtes Originaljournal vor Reparatur vollständig sichern
- Cross-Process-Single-Writer-Lock gegen parallele schreibende Instanzen
- Cross-Volume-Moves derzeit bewusst blockieren, bis ein vollständig verifizierter Ablauf implementiert ist

## Grundregel

> **Bei Unsicherheit wird nichts gelöscht und nichts überschrieben.**

---

# 8. Undo-System

**Status: noch nicht als Undo-Funktion umgesetzt.**

Dateisystemoperationen sind keine echten Datenbanktransaktionen.

Die bereits vorhandene Journal- und Recovery-Infrastruktur bildet die technische Grundlage für ein späteres **Journal-basiertes Undo-System**. Recovery und Undo bleiben dabei bewusst getrennte Konzepte: Recovery schützt vor unterbrochenen oder unklaren Operationen, Undo macht regulär abgeschlossene Aktionen gezielt rückgängig.

## Beispiel Journal-Eintrag

```json
{
  "operation": "move",
  "source": "/Fotos/IMG001.jpg",
  "destination": "/Fotos/2026/IMG001.jpg",
  "timestamp": "..."
}
```

Daraus kann später erzeugt werden:

```text
Undo

/Fotos/2026/IMG001.jpg
→
/Fotos/IMG001.jpg
```

Nicht rückgängig machbare Vorgänge müssen in der UI eindeutig gekennzeichnet werden.

---

# 9. UX- und Designsystem

## Leitidee

> Technik wird intern präzise behandelt, aber dem normalen Benutzer nicht ungefiltert zugemutet.

## Designbasis

Avalonia FluentTheme als Grundlage.

Darüber entsteht ein eigenes:

> **Elbwald Design System**

## Design Tokens

### Spacing

```text
4
8
12
16
24
32
48
```

### Corner Radius

```text
Small     4
Medium    8
Large    12
```

### Gemeinsame Komponenten

- Primary Button
- Secondary Button
- Danger Button
- Cards
- Inputs
- Toggle
- Dialoge
- Sidebar
- NavigationItem
- Empty State
- Error State
- Progress
- Notifications
- Info Banner
- Warning Banner

## Theme

Von Anfang an:

```text
○ Systemeinstellung
○ Hell
○ Dunkel
```

## UX-Regeln

1. Nie ohne Vorschau Dateien verändern.
2. Nie kommentarlos überschreiben.
3. Löschen nur nach klarer Bestätigung.
4. Normale Benutzer sehen keine unnötigen Fachbegriffe.
5. Erweiterte Optionen werden eingeklappt.
6. Längere Vorgänge zeigen Fortschritt.
7. Vorgänge sind soweit möglich abbrechbar.
8. Fehlermeldungen erklären Problem und Lösung.
9. Drag & Drop wird unterstützt, wo es sinnvoll ist.
10. Leere Ansichten erklären den nächsten Schritt.
11. Tastaturbedienbarkeit wird berücksichtigt.
12. Light / Dark / System von Anfang an.
13. Einstellungen erhalten sinnvolle Standardwerte.
14. Destruktive Aktionen werden optisch klar getrennt.
15. Fachbegriffe erhalten auf Wunsch zusätzliche Details.

---

# 10. Startseite

```text
Elbwald Digital
Desktop Tools

Was möchtest du machen?

┌──────────────────────┐
│ 📷 Fotos sortieren   │
│                      │
│ Fotos automatisch    │
│ organisieren         │
└──────────────────────┘

┌──────────────────────┐
│ 🎵 Musik bearbeiten  │
│                      │
│ Tags und Cover       │
│ bearbeiten           │
└──────────────────────┘
```

Später:

```text
Zuletzt verwendet
```

Keine klassische Desktop-Menüstruktur wie:

```text
File Edit View Tools Window Help
```

wenn sie für die eigentliche Aufgabe keinen Mehrwert bietet.

---

# 11. Photo Sort

Photo Sort ist das erste echte Modul und das erste MVP.

## Grundablauf

```text
Quelle auswählen
        ↓
Verzeichnis analysieren
        ↓
Bilder finden
        ↓
Aufnahmedatum ermitteln
        ↓
Sortierstruktur auswählen
        ↓
Vorschau
        ↓
Ausführen
```

## Unterstützte Formate im MVP

- JPG
- JPEG
- PNG
- WebP

RAW folgt später.

## Datumsermittlung

Priorität:

```text
1. EXIF DateTimeOriginal
2. EXIF CreateDate
3. Dateiname, falls eindeutig erkannt
4. Dateisystemdatum
5. Unbekannt
```

Der Nutzer sieht dagegen nur:

```text
1.842 Fotos gefunden

✓ 1.791 mit Aufnahmedatum
⚠ 51 ohne eindeutiges Aufnahmedatum
```

## Sortierstrukturen

### Jahr / Monat

```text
2026/
├── 01 Januar/
├── 02 Februar/
└── 03 März/
```

### Jahr / Monat / Tag

```text
2026/
└── 08 August/
    └── 12/
```

### Erweiterter Modus

Später:

```text
{Year}/{Month:00} {MonthName}/{Day:00}
```

## Konflikte

Nie überschreiben.

Strategien:

```text
Beide behalten
Überspringen
Abbrechen
```

Optional:

```text
☑ Für alle weiteren Konflikte verwenden
```

Automatische Namensanpassung:

```text
IMG_1234.jpg
IMG_1234_2.jpg
```

## Spätere Ausbaustufen

- Duplikaterkennung
- GPS-Auswertung
- RAW
- Videos
- Event-Gruppierung
- Perceptual Hashing
- Zeitzonenkorrektur
- Metadatenkorrektur
- Dateinamenregeln
- Smartphone-Import

---

# 12. Audio Tag

Zweites Modul.

## MVP

Zunächst ausschließlich:

```text
MP3
```

Später:

- FLAC
- OGG
- M4A

## Bearbeitbare Felder

- Titel
- Interpret
- Album
- Album-Interpret
- Jahr
- Track
- Disc
- Genre
- Kommentar
- Cover

## Mehrfachbearbeitung

```text
15 Titel ausgewählt

Album:         Waldklänge
Album Artist:  Beispielband
Jahr:          2026
Genre:         Folk

[Auf alle anwenden]
```

## Später

### Dateiname → Tags

```text
01 - Interpret - Titel.mp3
```

### Tags → Dateiname

```text
{Track:00} - {Artist} - {Title}
```

### Cover

- hinzufügen
- entfernen
- ersetzen
- extrahieren

---

# 13. Projektinitialisierung in Rider

## Schritt 1 – Voraussetzungen prüfen

Terminal:

```bash
dotnet --info
```

Ziel:

```text
.NET 10
```

Avalonia Templates prüfen:

```bash
dotnet new list | grep -i avalonia
```

Falls nicht vorhanden:

```bash
dotnet new install Avalonia.Templates
```

---

## Schritt 2 – Solution erstellen

In Rider:

```text
File
→ New Solution
→ Empty Solution
```

Name:

```text
Elbwald.DesktopTools
```

Beispielpfad:

```text
~/RiderProjects/Elbwald.DesktopTools
```

Git direkt aktivieren:

```text
☑ Create Git repository
```

---

## Schritt 3 – Ordnerstruktur

```text
src/
tests/
docs/
```

---

## Schritt 4 – App-Projekt

```text
Solution
→ Add
→ New Project
```

Template:

```text
Avalonia .NET MVVM App
```

Name:

```text
Elbwald.DesktopTools.App
```

Pfad:

```text
src/Elbwald.DesktopTools.App
```

Target:

```text
.NET 10
```

Danach sofort einmal starten.

Ziel:

```text
Build erfolgreich
Fenster öffnet sich
```

Commit:

```text
chore: initialize Avalonia desktop application
```

---

## Schritt 5 – Contracts

Projekt:

```text
.NET Class Library
```

Name:

```text
Elbwald.DesktopTools.Contracts
```

Pfad:

```text
src/Elbwald.DesktopTools.Contracts
```

Target:

```text
net10.0
```

Keine unnötigen UI-Abhängigkeiten.

---

## Schritt 6 – Core

Projekt:

```text
Elbwald.DesktopTools.Core
```

Abhängigkeit:

```text
Contracts
```

Nicht auf:

```text
App
UI
PhotoSort
AudioTag
```

---

## Schritt 7 – UI

Projekt:

```text
Elbwald.DesktopTools.UI
```

Dort Avalonia-Pakete ergänzen.

Ordner:

```text
Controls/
Themes/
Dialogs/
Icons/
Converters/
```

---

## Schritt 8 – Photo Sort

Projekt:

```text
Elbwald.DesktopTools.PhotoSort
```

Pfad:

```text
src/Modules/Elbwald.DesktopTools.PhotoSort
```

Referenzen:

```text
Contracts
UI
```

Nicht:

```text
App
```

---

## Schritt 9 – Audio Tag

Projekt:

```text
Elbwald.DesktopTools.AudioTag
```

Zunächst nur als leeres Modulprojekt anlegen.

---

## Schritt 10 – Tests

Zunächst:

```text
Elbwald.DesktopTools.Core.Tests
Elbwald.DesktopTools.PhotoSort.Tests
```

Audio-Tests erst, sobald die Audio-Entwicklung beginnt.

---

## Schritt 11 – zentrale Paketverwaltung

Datei:

```text
Directory.Packages.props
```

Zentral verwalten:

- Avalonia
- CommunityToolkit.Mvvm
- Microsoft.Extensions.DependencyInjection
- Serilog
- xUnit

Zusätzlich:

```text
Directory.Build.props
```

für gemeinsame Compiler- und Build-Einstellungen.

---

# 14. Git & GitHub

## Rider mit GitHub verbinden

```text
Settings
→ Version Control
→ GitHub
→ Add
→ Log In via GitHub
```

Danach:

```text
Git
→ GitHub
→ Share Project on GitHub
```

Repository:

```text
Elbwald.DesktopTools
```

Beschreibung:

```text
A collection of intuitive, cross-platform tools for organizing and managing photos, audio, and other media files.
```

Zu Beginn:

```text
Private
```

## Alternative über GitHub CLI

```bash
git init -b main
git add .
git commit -m "chore: initialize project"
```

```bash
gh repo create Elbwald.DesktopTools \
  --private \
  --source=. \
  --remote=origin \
  --push
```

---

# 15. Branch- und Commit-Strategie

Für ein Einzelprojekt möglichst einfach halten.

## Main Branch

```text
main
```

## Feature Branches

```text
feature/module-loader
feature/photo-scan
feature/photo-preview
feature/audio-tags

fix/photo-date-detection
```

## Commit-Konvention

```text
feat: add module discovery
feat: add photo directory scanner
feat: add file operation preview

fix: prevent overwrite on conflicting filenames

refactor: extract metadata reader

test: add photo date detection tests

docs: document module contract

chore: update dependencies
```

---

# 16. GitHub Actions

Sobald das Grundgerüst stabil läuft:

```text
Push / Pull Request
    ↓
Linux Build
    ├── restore
    ├── build
    └── test

Windows Build
    ├── restore
    ├── build
    └── test
```

Release-Packaging folgt später.

---

# 17. Roadmap & To-do

## Phase 0 – Projektbasis

- [x] .NET 10 installieren / prüfen
- [x] Avalonia-Projektbasis eingerichtet
- [x] Rider-Entwicklungsumgebung in Benutzung
- [x] Solution `Elbwald.DesktopTools` erstellt
- [x] Git aktiviert
- [x] `src/`, `tests/`, `docs/` angelegt
- [x] Avalonia-App-Projekt erstellt
- [x] App unter Linux entwickelt und getestet
- [ ] App unter Windows testen
- [ ] GitHub-Account in Rider verbinden *(optional; Git funktioniert bereits über Terminal/SSH)*
- [x] GitHub-Repository erstellt
- [ ] Initialen Repository-Stand auf GitHub final verifizieren
- [ ] `.editorconfig` einrichten
- [x] `.gitignore` geprüft
- [x] `Directory.Build.props` eingeführt
- [ ] `Directory.Packages.props` einführen
- [x] README mit Projektziel erstellt

---

## Phase 1 – Grundarchitektur

- [x] `Contracts` erstellt
- [x] `Core` erstellt
- [x] `UI` erstellt
- [x] `PhotoSort` erstellt
- [ ] `AudioTag` erstellen
- [x] grundlegende Projektabhängigkeiten gesetzt
- [x] zyklische Host-/Core-/Contracts-Abhängigkeiten vermieden
- [x] Dependency Injection konfiguriert
- [x] CommunityToolkit.Mvvm integriert
- [ ] Logging / Serilog integrieren
- [ ] App-Pfade vollständig über `IAppPaths` abstrahieren
- [ ] Settings-Service erstellen

---

## Phase 2 – Host UI

- [x] MainWindow-Grundlayout
- [x] Sidebar
- [x] Startseite
- [x] NavigationService
- [x] dynamische NavigationItems
- [ ] Settings-Seite
- [ ] Light Theme
- [ ] Dark Theme
- [ ] System Theme
- [x] Elbwald-Farbpalette
- [ ] Typografie als vollständiges Designsystem definieren *(teilweise vorhanden)*
- [ ] Abstände als vollständige Design-Tokens definieren *(teilweise vorhanden)*
- [x] grundlegende Button-Styles
- [x] grundlegende Card-Styles
- [ ] Dialogsystem
- [ ] Notification-System
- [ ] Empty-State-Control
- [x] Error-State für Navigations-/Modulfehler
- [ ] allgemeines Progress-Control
- [x] Recovery-/Dateisicherheitsstatus auf der Startseite
- [x] Recovery-UI mit offenen Vorgängen und Ergebnissen
- [x] UI für sichere Journal-Reparatur

---

## Phase 3 – Modul-System

- [x] `IToolModule`
- [x] `ToolModuleDescriptor`
- [x] `module.json`
- [x] Manifest-Parser
- [x] Versionsprüfung
- [x] API-Versionierung
- [x] Module Registry
- [x] Module Loader
- [x] `AssemblyLoadContext`
- [x] fehlerhafte Module abfangen
- [ ] deaktivierte Module unterstützen
- [x] Modulnavigation dynamisch erzeugen
- [x] Photo Sort als erstes ladbares Modul / Demo-Modul
- [x] Modul aus `/Modules` laden
- [x] Modulstart unter Linux im Entwicklungsablauf
- [ ] Modulstart unter Windows testen

### Meilenstein 1

> **Erreicht:** Modul wird geladen, registriert und dynamisch in Navigation/Startseite eingebunden.

---

## Phase 4 – gemeinsame Datei-Infrastruktur

- [ ] FileScanner
- [x] CancellationToken-Unterstützung
- [x] Fortschrittsmeldungen im Executor
- [x] Dateioperationsmodell
- [x] Move-Operation
- [ ] Rename-Operation
- [x] Copy-Operation
- [x] Konflikterkennung im `FileOperationPlanner`
- [x] `FileOperationPlan`
- [ ] Endnutzer-Vorschau für Operationspläne
- [x] Executor
- [x] persistentes Operationsjournal
- [ ] Undo-Grundstruktur
- [x] Fehlermodell / Execution States / Safety Issues
- [x] verständliche Core-Fehlermeldungen
- [x] umfangreiche Tests für Dateioperationen

### Zusätzlich bereits umgesetzt – Safety & Recovery

- [x] Preflight-Sicherheitsprüfung vor Dateioperationen
- [x] kein stilles Überschreiben vorhandener Ziele
- [x] Speicherplatzprüfung mit konfigurierbarer Sicherheitsreserve
- [x] staged Copy über `.partial`
- [x] Flush auf Disk vor Commit
- [x] SHA-256-Verifikation kopierter Daten
- [x] Cross-Volume-Move bewusst fail-closed blockiert
- [x] Memory Recovery Store
- [x] persistenter File Recovery Store
- [x] Hybrid Recovery Store
- [x] persistente Recovery-Sicherung vor destruktiven Move-Schritten
- [x] sichere Recovery-Handle-/Pfadvalidierung
- [x] Recovery Inspector
- [x] Recovery Coordinator
- [x] konservative Wiederherstellung unter Beibehaltung sicherer Kopien
- [x] Startup-Recovery-Erkennung
- [x] zentraler Recovery-Status
- [x] harte Recovery-Sperre vor neuen Dateioperationen
- [x] Recovery-Status und offene Transaktionen in der UI
- [x] Journal-Integritätsprüfung
- [x] sichere Reparatur eines eindeutig abgebrochenen letzten JSONL-Datensatzes
- [x] vollständiges Backup des beschädigten Journals vor Reparatur
- [x] Schutz vor parallelen Writer-Instanzen durch Cross-Process-Lock
- [x] stale Lock-Dateien blockieren nach Crash nicht dauerhaft
- [x] Tests für Recovery, Journal, Safety und Process Lock

### Meilenstein 2

> **Technische Sicherheitsbasis erreicht.**  
> Move/Copy können geplant und sicher ausgeführt werden.  
> Der vollständige Endnutzer-Workflow „analysieren → Vorschau → bestätigen → ausführen“
> wird mit Photo Sort angebunden.

---

## Phase 5 – Photo Sort MVP

- [x] Photo Sort Landing Page / Modulansicht *(Grundgerüst)*
- [ ] Ordnerauswahl
- [ ] Drag & Drop
- [ ] rekursiver Scanner
- [ ] Bildtypen erkennen
- [ ] Metadatenreader
- [ ] EXIF-Aufnahmedatum lesen
- [ ] Fallback-Datum definieren
- [ ] Dateianalyse
- [ ] Statistik anzeigen
- [ ] Sortierregel `Jahr/Monat`
- [ ] Sortierregel `Jahr/Monat/Tag`
- [ ] Zielordner auswählen
- [ ] Operationsplan aus Photo Sort erzeugen
- [ ] Konflikte im Photo-Sort-Workflow anzeigen
- [ ] Vorschauseite
- [ ] Ausführen über gemeinsame File-Operation-Engine
- [ ] Fortschrittsanzeige
- [ ] Abbrechen
- [ ] Zusammenfassung
- [ ] Fehlerliste
- [ ] Journal/Recovery vollständig im Workflow nutzen
- [ ] grundlegendes Undo
- [ ] große Verzeichnisse testen
- [ ] Linux-End-to-End testen
- [ ] Windows-End-to-End testen

### Meilenstein 3

> Photo Sort kann reale Fotoverzeichnisse sicher analysieren und sortieren.

Erstes nutzbares Release:

```text
0.1.0
```

---

## Phase 6 – Photo Sort verbessern

- [ ] Umbenennungsregeln
- [ ] benutzerdefinierte Muster
- [ ] Duplikaterkennung
- [ ] Hashing / Duplikat-Hashing
- [ ] Videos
- [ ] RAW-Formate evaluieren
- [ ] fehlende Daten korrigieren
- [ ] Session-Historie
- [ ] Undo verbessern
- [ ] Performance optimieren

---

## Phase 7 – Audio Tag MVP

- [ ] Audio-Tag-Modulprojekt erstellen
- [ ] MP3-Bibliothek evaluieren
- [ ] Testdateien aufbauen
- [ ] ID3 lesen
- [ ] ID3 schreiben
- [ ] Titel
- [ ] Interpret
- [ ] Album
- [ ] Album-Interpret
- [ ] Jahr
- [ ] Track
- [ ] Disc
- [ ] Genre
- [ ] Kommentar
- [ ] Cover lesen
- [ ] Cover anzeigen
- [ ] Cover schreiben
- [ ] Cover entfernen
- [ ] Dateiliste
- [ ] Mehrfachauswahl
- [ ] Multi-Edit
- [ ] Änderungen markieren
- [ ] Speichern
- [ ] Fehlerbehandlung

### Meilenstein 4

> Audio Tag kann einen kompletten MP3-Ordner komfortabel bearbeiten.

---

## Phase 8 – Audio Tag Ausbau

- [ ] FLAC
- [ ] OGG
- [ ] M4A
- [ ] Dateiname → Tags
- [ ] Tags → Dateiname
- [ ] Nummerierung
- [ ] Cover aus Datei
- [ ] Cover extrahieren
- [ ] Metadaten aus Onlinediensten evaluieren
- [ ] ReplayGain nur anzeigen
- [ ] erweiterte Tags

---

## Phase 9 – Add-on-Verwaltung

- [ ] installierte Add-ons anzeigen
- [ ] Version anzeigen
- [ ] aktivieren / deaktivieren
- [x] inkompatible/fehlerhafte Module technisch erkennen und isolieren
- [ ] Kompatibilitätsfehler als vollständige Add-on-Verwaltungsansicht anzeigen
- [ ] Add-on-Verzeichnis öffnen
- [ ] Installation aus Paket
- [ ] Update-Konzept
- [ ] Signierung evaluieren

---

## Phase 10 – Distribution

- [ ] Windows `dotnet publish`
- [ ] Linux `dotnet publish`
- [ ] self-contained Builds
- [ ] App Icon
- [ ] Windows Installer evaluieren
- [ ] Linux AppImage / DEB evaluieren
- [ ] GitHub Releases
- [ ] automatische CI-Builds
- [ ] Release Notes
- [ ] Update-System später evaluieren

---

# 18. Bewusst nicht Teil des MVP

Damit das Projekt nicht unnötig wächst:

```text
kein Cloud-Konto
kein Login
keine Synchronisation
keine Telemetrie
kein eigener Store
kein automatischer Updater
keine Plugin-Hot-Reloads
kein macOS
kein Android
keine AI-Funktionen
```

Diese Dinge können später bewertet werden.

---

# 19. GitHub Milestones

```text
0.0.1 – Project Foundation

0.0.2 – Module Host

0.0.3 – File Operations

0.1.0 – Photo Sort MVP

0.2.0 – Photo Sort Advanced

0.3.0 – Audio Tag MVP
```

---

# 20. Erstes konkretes Entwicklungsziel – erreicht

Das ursprüngliche Fundament-Ziel ist erreicht:

> **Elbwald Desktop Tools startet, besitzt eine dynamische Modulstruktur und lädt Photo Sort als Modul.**

Zusätzlich ist inzwischen wesentlich mehr Sicherheitsinfrastruktur vorhanden als
im ursprünglichen ersten Meilenstein vorgesehen:

```text
Desktop Tools
    │
    ├── Host / Navigation / Startseite
    ├── dynamischer Module Loader
    ├── Photo Sort Modul
    │
    └── gemeinsame Datei-Infrastruktur
         ├── Planner
         ├── Safety Preflight
         ├── Executor
         ├── Journal
         ├── Recovery Stores
         ├── Recovery Coordinator
         ├── Startup Recovery Gate
         ├── Journal Maintenance
         └── Cross-Process Safety Lock
```

Der Schwerpunkt kann damit von der Infrastruktur auf den ersten echten
Endnutzer-Workflow wechseln.

---

# Nächster Arbeitsschritt

## Ziel

**Phase 4 sauber abschließen und danach den Photo-Sort-MVP wirklich nutzbar machen.**

Die sinnvollste Reihenfolge ab Patch 0020:

```text
FileScanner
    ↓
Photo-Verzeichnis auswählen
    ↓
rekursiv analysieren
    ↓
Bildtypen erkennen
    ↓
Metadaten / Aufnahmedatum lesen
    ↓
Sortierziel berechnen
    ↓
FileOperationPlan erzeugen
    ↓
Vorschau
    ↓
bestehende Safety-/Recovery-Engine ausführen
```

Priorität haben damit zunächst:

- gemeinsamer `FileScanner`
- sichere, abbrechbare Verzeichnisanalyse
- Photo-Sort-Ordnerauswahl
- Bildtyperkennung
- Metadatenreader
- EXIF-Datum mit klaren Fallback-Regeln
- Statistik und Vorschau
- erst danach reale Sortierausführung

`Undo`, Settings, Themes und Audio Tag bleiben wichtig, blockieren den ersten
nutzbaren Photo-Sort-Workflow aber nicht.

---

> 🌲 **Elbwald Digital – Desktop Tools**  
> Kleine Werkzeuge. Klare Bedienung. Sichere Dateioperationen.
