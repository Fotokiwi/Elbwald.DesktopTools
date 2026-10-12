# 🌲 Elbwald Digital – Desktop Tools

> **A local-first, cross-platform media workspace for organizing, understanding and safely managing real files.**

<p align="left">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white">
  <img alt="Avalonia UI" src="https://img.shields.io/badge/Avalonia-Desktop_UI-7B2CBF">
  <img alt="Platforms" src="https://img.shields.io/badge/Platforms-Linux%20%7C%20Windows-2E7D32">
  <img alt="Status" src="https://img.shields.io/badge/Status-Alpha_Development-F59E0B">
  <img alt="License" src="https://img.shields.io/badge/License-Apache--2.0-blue">
</p>

---

## What is Elbwald Desktop Tools?

**Elbwald Desktop Tools** is a local media workspace built around a simple idea:

> **The filesystem provides physical organization. Elbwald provides logical organization.**

Your photos and other media remain normal files and folders. Elbwald does not require a proprietary media vault, cloud account or online service in order to organize them.

The application combines:

- a local media library and index,
- safe file organization workflows,
- project-based collections that reference existing files instead of duplicating them,
- media analysis and metadata-aware tools,
- a modular extension system for additional formats and future features,
- and a safety layer designed for operations on irreplaceable user data.

The primary targets are **Linux x64** and **Windows x64**.

---

# ✨ Current feature set

## 📚 Library

Elbwald maintains a local media index above the normal filesystem.

Current foundations include:

- stable internal media IDs,
- indexed files and metadata,
- thumbnail and preview caching,
- folder and timeline-oriented library data,
- search/filter foundations,
- project references without duplicating the source media,
- persistent application state across restarts.

The library is intended to remain useful even when optional format or feature extensions are not installed.

---

## 🔎 Analyze

The analyzer is designed as a **read-only first step** for unknown or newly added media collections.

It can inspect a folder before any file operation is performed and provides information about the discovered media, dates and possible issues.

Analysis results can be persisted and reopened later.

---

## 🗂️ Organize

### Import

Media can be imported through a safety-aware workflow instead of being copied blindly.

### Sort

Files can be organized using metadata and configurable path rules.

The sorting workflow includes:

- planning before execution,
- date resolution,
- handling of uncertain dates,
- configurable path patterns,
- conflict detection,
- companion-file handling,
- previews before file operations are committed.

### Rename

The rename workflow includes:

- presets,
- live filename examples,
- real source previews,
- collision detection,
- controlled execution through the file-operation layer.

---

## 🧭 Projects

Projects form a logical layer above the filesystem.

A media file can be referenced by projects without being copied into a project-specific storage format.

Current project foundations include:

- project overview,
- project workspace,
- project metadata,
- media assignments,
- overview, media, journal and information areas,
- stable references to indexed media.

Planned project development includes galleries, highlights, travel/holiday workflows, journal features and later content-studio / photo-book workflows.

---

## 🖼️ Image formats

### Built-in base formats

The base application includes support for:

| Format | Detection | Metadata | Thumbnail | Export |
|---|---:|---:|---:|---:|
| JPEG / JPG | ✅ | ✅ | ✅ | ✅ |
| PNG | ✅ | ✅ | ✅ | ✅ |

These formats are part of the normal application baseline.

### Extended Image Formats

Additional image formats are provided through an optional extension backed by **Magick.NET / ImageMagick**.

Current vertically integrated formats are:

| Format | Detection | Metadata | Thumbnail | Export |
|---|---:|---:|---:|---:|
| WebP | ✅ | ✅ | ✅ | ✅ |
| TIFF / TIF | ✅ | ✅ | ✅ | ✅ |

Additional formats may be added later, but Elbwald only advertises capabilities that have been explicitly integrated and tested.

RAW formats are planned as a separate package because camera RAW workflows require different handling from normal raster formats.

JPEG XL is also being considered as a separate specialized package, particularly for possible future archival / backup workflows.

---

# 🛡️ Safety first

Media files are often irreplaceable.

For that reason, file safety is not an afterthought in Elbwald — it is part of the architecture.

The core file-operation layer follows principles such as:

- **never silently overwrite existing files,**
- validate operations before execution,
- detect conflicts before committing changes,
- check available storage before writing,
- keep a configurable free-space reserve,
- stage copies using temporary files,
- flush written data before commit where supported,
- verify copied data with **SHA-256**,
- keep persistent operation journals,
- detect interrupted transactions,
- retain recovery information when filesystem state is ambiguous,
- prefer stopping over guessing.

For potentially destructive operations, the rule is:

> **If the application cannot prove that an operation is safe, it does not proceed.**

Cross-volume moves are intentionally treated differently from same-volume renames because a safe cross-volume move requires a verified copy before the original may be removed.

---

# ♻️ Recovery architecture

Elbwald contains a layered recovery and operation-journal foundation.

```text
Source file
    │
    ▼
Preflight checks
    │
    ▼
Persistent journal
    │
    ▼
Recovery data when required
    │
    ▼
Temporary / staged write
    │
    ▼
Flush + verification
    │
    ▼
Commit
    │
    ▼
Journal completion
```

The central principle is that incomplete or ambiguous operations should remain diagnosable instead of being silently treated as successful.

---

# 🔌 Module API v2

Elbwald uses a capability-oriented extension system.

Extensions are not limited to adding standalone screens. They can also add capabilities to existing workflows.

Examples include:

- media-format recognition,
- metadata readers,
- thumbnail providers,
- image exporters,
- navigation entries,
- settings contributions,
- future editor tools,
- future backup providers and remote services.

The host is designed to ask:

> **Which active capability can perform this operation?**

rather than:

> **Is a specific hard-coded module installed?**

This allows an installed format extension to become available automatically to existing parts of the application.

## Extension lifecycle

The current startup model is broadly:

```text
Discover extensions
        ↓
Read and validate manifests
        ↓
Resolve dependencies
        ↓
Load extensions
        ↓
Register services and providers
        ↓
Build dependency-injection container
        ↓
Resolve capability registries
        ↓
Register navigation / settings contributions
        ↓
Start UI
```

## Current extension infrastructure

Module API v2 currently provides:

- manifest API versioning,
- dependency declarations,
- capability declarations,
- permission/trust fields prepared in manifests,
- extension service registration before DI is built,
- graceful isolation of incompatible or broken extensions,
- central extension registry,
- media-format registry,
- metadata registry,
- thumbnail registry,
- export registry,
- dynamic navigation contributions,
- settings contributions,
- live enable/disable state in Settings,
- generic module build and publish collection.

Capability-only extensions do not need to provide their own page or navigation entry.

Disabling an extension removes its active contributions immediately. Assemblies currently remain loaded until the application exits; full hot-unloading is intentionally not required at this stage.

---

# 🧩 Current modules

The repository currently contains first-party modules such as:

```text
src/Modules/
├── Elbwald.DesktopTools.MediaAnalyzer/
├── Elbwald.DesktopTools.PhotoSort/
└── Elbwald.DesktopTools.Image.Formats.Extended/
```

### Media Analyzer

Provides the main analysis workflow through a navigation contribution.

### Photo Sort

Provides sorting functionality and contributes itself to the appropriate navigation area.

### Extended Image Formats

A capability-oriented extension rather than a standalone tool page.

It currently contributes WebP and TIFF capabilities through Magick.NET, including format recognition, metadata, thumbnails and export support.

---

# 🏗️ Architecture

The solution is split into a small host, shared contracts, reusable core services and independent modules.

```text
Elbwald.DesktopTools/
├── src/
│   ├── Elbwald.DesktopTools.App/
│   ├── Elbwald.DesktopTools.Contracts/
│   ├── Elbwald.DesktopTools.Core/
│   ├── Elbwald.DesktopTools.UI/
│   └── Modules/
│       ├── Elbwald.DesktopTools.MediaAnalyzer/
│       ├── Elbwald.DesktopTools.PhotoSort/
│       └── Elbwald.DesktopTools.Image.Formats.Extended/
├── tests/
├── docs/
└── Elbwald.DesktopTools.sln
```

### Main projects

**Elbwald.DesktopTools.App**  
Application host, startup, dependency-injection composition and shell.

**Elbwald.DesktopTools.Contracts**  
Shared interfaces, extension contracts and capability abstractions.

**Elbwald.DesktopTools.Core**  
Core services, media index, file-operation safety, recovery and application infrastructure.

**Elbwald.DesktopTools.UI**  
Shared UI components and visual foundations.

**Modules**  
First-party and future optional extensions loaded through Module API v2.

## Platform core vs. extensions

Not everything is intended to become a plugin.

The platform core intentionally owns areas such as:

- application shell,
- library / media index,
- projects,
- settings host,
- file safety,
- recovery,
- stable media IDs,
- storage abstractions,
- operation history infrastructure,
- central cache / thumbnail infrastructure,
- extension and capability registries.

Extensions provide optional tools and capabilities without owning the underlying media library.

---

# ⚙️ Generic module build and publish

Projects under:

```text
src/Modules/*/*.csproj
```

are discovered by the application build infrastructure and collected into the module output automatically.

The goal is that adding a new module does **not** require hard-coded copy logic or application-project changes for every new extension.

Typical output:

```text
Modules/
├── Elbwald.DesktopTools.MediaAnalyzer/
├── Elbwald.DesktopTools.PhotoSort/
└── Elbwald.DesktopTools.Image.Formats.Extended/
```

---

# 🧰 Technology

| Area | Technology |
|---|---|
| Language | C# |
| Runtime | .NET 10 LTS |
| Desktop UI | Avalonia |
| Architecture | MVVM |
| MVVM toolkit | CommunityToolkit.Mvvm |
| Dependency injection | Microsoft.Extensions.DependencyInjection |
| Serialization | System.Text.Json |
| Image extension backend | Magick.NET / ImageMagick |
| Testing | xUnit |
| IDE | JetBrains Rider |

---

# 🚧 Project status

**Elbwald Desktop Tools is under active alpha development.**

The project already has a substantial functional foundation, including:

- cross-platform Avalonia application shell,
- local media index and library,
- stable media IDs,
- analyzer workflow,
- import workflow,
- sorting workflow,
- rename workflow,
- project foundations,
- thumbnail / preview caching,
- metadata reading,
- image export infrastructure,
- file-operation safety engine,
- SHA-256 verification,
- persistent operation journals,
- recovery infrastructure,
- cross-process protection,
- Module API v2 and capability registries,
- live extension activation,
- navigation and settings contributions,
- generic module build / publish integration.

The application is **not yet considered a finished stable release**. APIs, UI details and internal structures may still change.

---

# 🧑‍💻 Building from source

## Requirements

- **.NET 10 SDK**
- Linux x64 or Windows x64

Clone the repository:

```bash
git clone git@github.com:Fotokiwi/Elbwald.DesktopTools.git
cd Elbwald.DesktopTools
```

Restore dependencies:

```bash
dotnet restore Elbwald.DesktopTools.sln
```

Build:

```bash
dotnet build Elbwald.DesktopTools.sln
```

Run tests:

```bash
dotnet test Elbwald.DesktopTools.sln
```

The application build automatically collects projects below `src/Modules/` into the module output.

---

# 🌿 Design philosophy

Elbwald is intended to be usable without requiring users to understand filesystem internals, metadata standards or media-database implementation details.

The UI and workflows therefore follow a few principles:

**Local first.**  
The normal filesystem remains the source of truth for physical files.

**Preview first.**  
Users should see what will happen before files are changed.

**Safe defaults.**  
Dangerous behavior must never be the easiest option.

**Clear language.**  
Technical details should be available when useful, but normal workflows should remain understandable.

**Progressive complexity.**  
Common tasks stay simple while advanced options remain accessible.

**Graceful degradation.**  
Removing an optional extension should remove a capability, not break the media library or project data.

**Consistency.**  
All tools should feel like parts of one application rather than unrelated utilities.

---

# 🗺️ Roadmap

The current direction is intentionally focused on consolidating the existing product before adding too many new areas.

```text
Module API v2 foundation
        ✓
        ↓
Workflow consolidation / E2E hardening
        ↓
Projects MVP refinement
        ↓
Library Health
        ↓
Duplicate Finder
        ↓
Undo / History
        ↓
Backup Analyzer + Verify
        ↓
Backup providers
        ↓
Metadata / Audio tools
        ↓
Image / Video / Content Studio
```

Near-term priorities include:

- Linux end-to-end testing of the main workflow,
- Windows smoke / end-to-end testing,
- large-library and failure-path testing,
- further project UX and gallery work,
- a central Library Health view,
- bit-identical duplicate detection,
- operation history and undo foundations.

Backup is intentionally planned **after** health and verification foundations are strong enough to prove that a backup is actually valid.

---

# 🔭 Planned extension areas

Future work may include:

- RAW image format package,
- JPEG XL support and possible archival workflows,
- Duplicate Finder,
- metadata editor and cleaner,
- Audio Analyzer / Audio Tag / Cover Manager,
- image editing tools,
- video inspection and organization,
- local and remote backup providers,
- SFTP / S3 / WebDAV-style integrations,
- travel / holiday project profiles,
- journal and content-studio workflows,
- photo-book / PDF export workflows.

These features are intentionally expected to build on the same capability-oriented extension infrastructure instead of introducing separate plugin mechanisms.

---

# 🤝 Contributions

The repository is public primarily to make development transparent and to allow others to inspect, learn from and improve the project.

Bug reports and constructive suggestions are welcome.

Before larger contributions, opening an issue first is recommended so implementation details can be coordinated.

The extension APIs are still evolving during alpha development, so third-party compatibility should not yet be considered stable.

---

# 📜 License

Licensed under the **Apache License 2.0**.

Copyright © 2026 **Elbwald Digital**

The license permits use, modification and redistribution under its terms. Copyright and applicable license notices must be preserved.

See [`LICENSE`](LICENSE) for the full license text.

---

# 🌲 Elbwald Digital

Built with a focus on **useful software, understandable interfaces, local data ownership and careful handling of user files**.

**Linux first. Windows too. Files treated with respect.**
