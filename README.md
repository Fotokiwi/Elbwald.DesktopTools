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

### Media Analyzer

The analyzer is designed as a **read-only first step** for unknown or newly added media collections.

It can inspect a folder before any file operation is performed and provides information about the discovered media, dates and possible issues.

Analysis results can be persisted and reopened later.

### Library Health

The central Library Health view checks the state of configured storage endpoints, indexed locations and metadata quality without changing source files.

Current foundations include:

- endpoint reachability and library-state checks,
- index and metadata diagnostics,
- searchable and filterable findings,
- persistent health snapshots,
- stale-state detection after relevant library changes,
- responsive background processing with progress and cancellation.

Existing health reports are reopened from the cached snapshot instead of forcing a full rescan every time the view is opened.

### Duplicate Finder

The Duplicate Finder uses the media index and SHA-256 identities to locate **bit-identical** files.

Current capabilities include:

- duplicate groups and theoretical storage impact,
- filtering and sorting,
- explicit keep/remove planning,
- protected backup and archive endpoints,
- filesystem preflight validation,
- controlled cleanup through the operating-system trash / recycle bin,
- no fallback to permanent deletion,
- operation-history integration,
- confidence hints for obvious copy-style duplicates.

Duplicate detection is intentionally separated from semantic judgement: identical bytes do not automatically mean that one filesystem location is unnecessary.

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

### Media Consolidation

Media Consolidation is being built as a safer alternative to aggressively deleting duplicates from existing collections.

The current **read-only Dry-Run planner** can:

- combine multiple indexed source folders,
- plan a new target library structure,
- use indexed capture / media dates for destination paths,
- deduplicate identical source content by SHA-256,
- skip identical content that is already present in the target,
- keep filename conflicts with different content visible instead of overwriting,
- calculate planned copy counts and required copy volume,
- preserve source-origin information in the plan.

Current structure presets include:

- `Year / Month / Original filename`,
- `Year / Original filename`,
- preserving the source structure.

The planning stage does **not** create target folders, copy, move or delete files. Verified copy execution is the next development step.

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

## 🎛️ Image Editing

Image editing is being developed as a **separate optional first-party module**:

```text
Elbwald.DesktopTools.Image.Editor.Basic
```

The main application does not depend on the editor module. If the module is not installed or is disabled, the media library, projects, metadata, thumbnails, format support and other workflows continue to work normally.

The editor follows a **non-destructive workflow**:

```text
Original media file
        ↓
Decode through active image-format providers
        ↓
Stored edit document / operation stack
        ↓
Preview rendering
        ↓
Export rendering
        ↓
New output file
```

The original image is never modified by editor operations. Elbwald stores only the edit definition in its editor database and applies those operations when a preview or later export is rendered.

### Current editor foundation

The current optional image editor already includes:

- an editor workspace contributed dynamically under **Edit / Image Editing**,
- full-image decoding through the shared image decode registry,
- JPEG / PNG decoding in the application baseline,
- WebP / TIFF decoding when the optional Extended Image Formats module is active,
- normalized in-memory `BGRA32` image frames,
- EXIF orientation handling during decoding,
- a versioned non-destructive edit document and operation stack,
- persistence by stable `MediaItemId` instead of filesystem path,
- optimistic revision protection for concurrent edit sessions,
- capability-oriented operation providers rather than a hard-coded operation switch,
- preview rendering from the original source plus the current edit stack,
- in-memory working documents for unsaved changes,
- undo / redo,
- explicit saving of edit definitions without writing image pixels back to the source file,
- original / edited comparison,
- fit-to-window and zoom controls,
- interactive crop with draggable handles and normalized coordinates,
- 90° / 180° / 270° rotation,
- horizontal and vertical mirroring.

### Current light and color adjustments

The editor currently provides a live tone / color operation with:

- exposure,
- highlights,
- shadows,
- contrast,
- temperature,
- tint,
- saturation.

These controls are represented as one versioned tone operation instead of generating a separate edit-stack entry for every slider.

Slider changes are previewed live without writing to the database. The current preview path uses a two-stage strategy:

```text
Slider movement
        ↓
Fast ~1024 px in-memory preview
        ↓
Values stabilize
        ↓
Higher quality ~2048 px preview
```

The higher-resolution preview is rendered automatically after the interaction settles.

Applied values remain visible in the controls and continue to represent the current stored / working edit state. Re-adjusting an existing tone operation updates that operation instead of stacking repeated tone operations.

Exposure is expressed in photographic EV terms. Other slider scales are intentionally Elbwald-defined UI scales; equal numeric values are not expected to produce pixel-identical results to Lightroom, Photoshop, Capture One or other editors.

### Editor modularity

The editor itself does not directly depend on optional image-format packages.

Instead, it asks the shared decode and export registries which active provider can handle a media item.

That means an installed format module can automatically extend the editor:

```text
Base application
    ├── JPEG
    └── PNG

Extended Image Formats module
    ├── WebP
    └── TIFF

            ↓

Shared image decode registry

            ↓

Optional Image Editor
```

Disabling the Extended Image Formats module removes those format capabilities without requiring changes inside the editor.

The image-operation pipeline follows the same principle. Future editor packages can register additional operations without requiring the basic editor renderer to know every possible operation in advance.

### Planned image-editing development

Planned work includes:

- direct **Edit image** entry points from Library and Project views,
- export of the current non-destructive edit stack into a new rendered file,
- export format / quality / metadata options,
- stronger integration with the existing image-export providers,
- additional tone and color controls,
- white-balance refinement,
- curves and more advanced tonal control,
- HSL / selective color tools,
- sharpening,
- noise reduction,
- optional lens / optical corrections where technically appropriate,
- histogram and clipping assistance,
- configurable before / after comparison modes,
- keyboard shortcuts and workflow polish,
- editor presets,
- batch application of edit presets to multiple images,
- export presets such as web, archive and privacy-focused output,
- optional advanced editing packages on top of the basic editor,
- later RAW-aware editing through a dedicated RAW package.

Complex raster-painting features such as layers, freehand painting, cloning or Photoshop-style compositing are **not** the primary target of the basic editor. The focus is non-destructive photographic correction, media-workflow integration and safe export.

---


## 🎚️ Audio Studio

Audio editing is being developed as a **separate optional first-party module** that can be used independently of the media library:

```text
Elbwald.DesktopTools.Audio.Editor.Basic
```

The Audio Studio can open audio files directly from the filesystem and stores its own project files using the versioned `.ewaudio` format. Source files remain unchanged; edits, mixer settings, analysis data and restoration parameters are stored non-destructively in the project. The current implementation is module version **0.32.0** with `.ewaudio` project format **V6**.

### Current Audio Studio foundation

The current optional Audio Studio already includes:

- standalone projects with up to **16 mono / stereo tracks**,
- direct WAV import without requiring Library integration,
- clip-based tracks rather than a fixed `track = file` model,
- relative source references inside project folders and missing-source detection,
- versioned `.ewaudio` project save / load,
- WAV decoding to normalized 32-bit floating-point samples,
- waveform caching and timeline navigation,
- real-time playback, seeking and a cross-platform output path through miniaudio,
- live track volume, panorama, mute and solo,
- per-track and master peak metering,
- a virtual **Master Bus** that combines all audible tracks,
- compact track waveforms with absolute dB comparison mode,
- sample-peak warning zones and grouped master True-Peak warnings,
- direct seeking by clicking track, master or timeline waveforms.

### Current EQ, analysis and restoration tools

The detachable **EQ & Analysis** window currently provides:

- per-track and Master-Bus analysis,
- live FFT spectrum analysis before / after EQ,
- high-pass and low-pass filters,
- three-band EQ with adjustable mid frequency,
- large waveform view with time and level zoom,
- dBFS level-over-time diagrams,
- separate **pre-limiter**, **final-master** and overlaid comparison views,
- optional pre-limiter problem markers over the final-master history,
- spectrogram analysis,
- selectable waveform regions,
- learned noise profiles,
- non-destructive spectral noise reduction,
- removed-noise audition for checking whether useful audio is being damaged,
- live visualization of Notch and 50 / 60 Hz hum-removal filter curves,
- a marked De-Esser working range without pretending that its dynamic reduction is a fixed EQ curve,
- reusable analysis window state including position, size and pinning to a selected target.

### Current track repair and Basic FX tools

Per-track cleanup is now part of the real playback and offline-analysis DSP path. The current Basic FX set includes:

- soft **Expander / Noise Gate** with threshold and maximum attenuation,
- narrow **Notch filtering** for isolated whistles, chirps and other tonal interference,
- **50 / 60 Hz hum removal** with configurable harmonic count and reduction strength,
- dynamic **De-Esser** with adjustable working frequency and strength,
- existing spectral noise reduction, EQ / filters, compressor and track limiter.

The current track processing order is intentionally understandable and conservative:

```text
Clip / source audio
        ↓
Spectral noise reduction
        ↓
Expander / Noise Gate
        ↓
50 / 60 Hz hum removal
        ↓
Notch
        ↓
High-Pass / EQ / Low-Pass
        ↓
De-Esser
        ↓
Compressor
        ↓
Track limiter
        ↓
Track volume / panorama
        ↓
Master Bus
```

### Stem Repair Analyzer

Each track now exposes a compact **Repair** action that opens a dedicated detachable **Stem Repair & Analysis** window. The analyzer is deliberately rule-based and explainable instead of silently applying opaque corrections.

The first analyzer generation can currently detect and rank:

- elevated background / noise floor in quiet passages,
- likely **50 or 60 Hz mains hum** and matching harmonics,
- unusually narrow spectral spikes that may indicate whistles, chirps or other interference,
- suspicious high-frequency energy that may indicate **sibilance**.

Each finding contains a severity, confidence, explanation and concrete filter recommendation. Recommendations map directly to the existing live DSP parameters and are only applied when the user explicitly chooses **Apply recommendation**. The same window also exposes the live Expander, Notch, Hum Removal and De-Esser controls for immediate listening and fine adjustment, with a direct link into the larger EQ & Analysis window.

For normal song-length stems the analyzer scans the complete source in bounded blocks. Longer material is sampled across distributed windows so analysis remains predictable in memory and processing cost. Narrowband findings are intentionally treated as suggestions because a spectral peak can also be intentional musical content.

### Current dynamics and mastering foundation

The Audio Studio also includes:

- live track compressor and sample-peak limiter,
- master EQ / filtering,
- ITU-style loudness analysis with Integrated, Short-Term and Momentary LUFS,
- True-Peak analysis with 4× inter-sample evaluation,
- a real Master True-Peak limiter,
- complete offline project analysis through the same DSP path used for playback,
- mastering targets such as **Streaming / Dynamic**, **Universal**, **Loud / Auto** and **Event / Powerful**,
- separate project-wide and live loudness values,
- target feasibility analysis based on required gain and peak reduction,
- conservative A/B mastering preview and automatic application for suitable targets,
- limiter gain-reduction statistics including maximum, average and time above thresholds,
- per-track peak contribution analysis,
- grouped critical peak events with timeline jump and short audition,
- detection of cases where several individually safe tracks create an excessive summed Master peak.

The current default **Universal** mastering target is `-12 LUFS / -1 dBTP`. Presets are guidance rather than hard rules; Elbwald keeps loudness normalization, peak control and stronger dynamics processing conceptually separate.

### Audio Studio architecture

The Audio Studio follows the same modular and non-destructive principles as the image editor:

```text
Source audio files
        ↓
Decode to floating-point samples
        ↓
Clips / timeline
        ↓
Spectral restoration
        ↓
Expander / Hum / Notch
        ↓
Track EQ / De-Esser / Compressor / Limiter
        ↓
Track mixer
        ↓
Master Bus + Master EQ
        ↓
Loudness / True-Peak analysis
        ↓
Master limiter
        ↓
Playback / future rendered export
```

The media library may later offer convenience entry points into the Audio Studio, but the editor is intentionally able to remain a standalone workflow with its own projects.

### Planned Audio Studio development

Planned work includes:

- rendered project export to WAV and additional audio formats,
- export readiness checks and presets for streaming, archive, car / general playback and event use,
- broader codec support through optional audio-format providers,
- editable clip trimming, splitting, moving and fades directly on the timeline,
- crossfades and richer multi-clip editing,
- project packaging / collecting referenced source files for transfer and backup,
- adaptive high-frequency / AI-artifact reduction for metallic hiss and chirping,
- improved spectrogram interaction and automatic localization of repair candidates,
- further Stem Repair diagnostics such as low-frequency rumble, phase / channel anomalies and clipping patterns,
- smarter recommendation scoring and optional before / after audition workflows,
- channel tools such as L/R swap, mono conversion and phase inversion,
- stereo-width control with mono-compatibility guidance,
- basic musical effects such as Reverb and Delay after the repair pipeline is stable,
- automated peak / transient correction with local automation where appropriate,
- smarter mastering assistance that can suggest or apply the least invasive correction,
- optional advanced dynamics / mastering packages,
- later integration with Audio Analyzer, metadata / tag editing and Cover Manager workflows.

The target is **not** to recreate a full DAW. MIDI sequencing, virtual instruments, unrestricted bus routing and large studio-session features are intentionally outside the initial scope. The focus is understandable multi-track editing, restoration, mixing, loudness control and reliable export.

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
├── Elbwald.DesktopTools.Image.Formats.Extended/
├── Elbwald.DesktopTools.Image.Editor.Basic/
└── Elbwald.DesktopTools.Audio.Editor.Basic/
```

### Media Analyzer

Provides the main analysis workflow through a navigation contribution.

### Photo Sort

Provides sorting functionality and contributes itself to the appropriate navigation area.

### Extended Image Formats

A capability-oriented extension rather than a standalone tool page.

It currently contributes WebP and TIFF capabilities through Magick.NET, including format recognition, metadata, thumbnails, full-image decoding and export support.

### Basic Image Editor

An optional first-party editing module that contributes its own editor workspace and non-destructive image-editing capabilities.

It currently provides:

- persistent edit documents,
- preview rendering,
- crop / rotate / mirror operations,
- undo / redo,
- live exposure / light / color adjustments,
- adaptive in-memory preview rendering,
- and capability-oriented editor operations that can later be extended by additional modules.

The module does not own the media library and does not modify original source images.

### Basic Audio Studio

An optional standalone multi-track audio-editing and mastering module with its own `.ewaudio` projects.

It currently provides:

- up to 16 mono / stereo tracks,
- WAV import, floating-point decoding and real-time playback,
- non-destructive clip / project persistence,
- track mixer controls and a virtual Master Bus,
- live EQ / filters, compressor and limiting,
- Expander / Noise Gate, Notch, 50 / 60 Hz hum removal and De-Esser,
- detachable FFT / waveform / dBFS / spectrogram analysis,
- learned-noise spectral restoration,
- per-track Stem Repair analysis with explainable filter recommendations,
- dedicated detachable repair window with direct live filter control,
- LUFS / True-Peak project analysis,
- mastering targets and A/B assistance,
- limiter gain-reduction statistics,
- peak-cause and critical-event analysis.

The module can work without the media library; future Library integration is intended as an optional convenience layer.

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
│       ├── Elbwald.DesktopTools.Image.Formats.Extended/
│       ├── Elbwald.DesktopTools.Image.Editor.Basic/
│       └── Elbwald.DesktopTools.Audio.Editor.Basic/
├── tests/
├── docs/
└── Elbwald.DesktopTools.slnx
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
├── Elbwald.DesktopTools.Image.Formats.Extended/
├── Elbwald.DesktopTools.Image.Editor.Basic/
└── Elbwald.DesktopTools.Audio.Editor.Basic/
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
| Audio output backend | MiniAudioEx / miniaudio |
| Testing | xUnit |
| IDE | JetBrains Rider |

---

# 🚧 Project status

**Elbwald Desktop Tools is under active alpha development.**

The project already has a substantial functional foundation, including:

- cross-platform Avalonia application shell,
- local media index and library,
- stable media IDs,
- responsive large-library loading paths,
- analyzer workflow,
- Library Health with persistent snapshots and stale-state detection,
- bit-identical Duplicate Finder with safe cleanup planning and preflight,
- trash / recycle-bin based controlled duplicate cleanup,
- import workflow,
- sorting workflow,
- rename workflow,
- Media Consolidation Dry-Run planning with SHA-256 deduplication,
- project foundations,
- thumbnail / preview caching,
- metadata reading,
- image export infrastructure,
- optional non-destructive image-editor module,
- image decode registry for base and extension-provided formats,
- versioned edit-stack persistence,
- interactive crop / rotate / mirror editing,
- undo / redo and live adaptive tone / color previews,
- optional standalone 16-track Audio Studio module,
- `.ewaudio` project persistence and WAV-based non-destructive audio workflow,
- real-time audio playback, mixing, EQ, dynamics and Master Bus,
- FFT / waveform / spectrogram / dBFS audio analysis,
- spectral noise-profile restoration plus Expander, Notch, Hum Removal and De-Esser,
- explainable per-stem repair analysis with direct filter recommendations,
- LUFS / True-Peak project analysis and assisted mastering foundations,
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
dotnet restore Elbwald.DesktopTools.slnx
```

Build:

```bash
dotnet build Elbwald.DesktopTools.slnx
```

Run tests:

```bash
dotnet test Elbwald.DesktopTools.slnx
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

The current direction is intentionally focused on turning the existing foundations into dependable end-to-end workflows before adding too many unrelated areas.

```text
Module API v2 foundation
        ✓
        ↓
Library / index foundation
        ✓
        ↓
Library Health
        ✓
        ↓
Bit-identical Duplicate Finder
        ✓
        ↓
Media Consolidation Dry-Run
        ✓
        ↓
Verified consolidation copy engine
        ↓
Pause / resume / interrupted-run recovery
        ↓
Conflict handling + richer structure rules
        ↓
Operation history / undo refinement
        ↓
Backup Analyzer + Verify
        ↓
Backup providers
        ↓
Audio Studio foundation
        ✓
        ↓
Basic Track FX + Stem Repair Analyzer
        ✓
        ↓
Audio export / advanced restoration / mastering refinement
        ↓
Metadata / Audio Tag / Cover tools
        ↓
Image Editor foundation
        ✓
        ↓
Image export / advanced editing integration
        ↓
Video / Content Studio
```

Near-term priorities include:

- validate Media Consolidation Dry-Runs against large real-world libraries,
- implement verified `copy + SHA-256 verify` execution for consolidation,
- add pause, resume, cancellation and restart-safe continuation,
- define explicit handling for same-target-name / different-content conflicts,
- expand consolidation history and execution reports,
- add richer destination structure rules,
- integrate successfully consolidated targets efficiently into the media index,
- continue Linux end-to-end and failure-path testing,
- continue Windows smoke / end-to-end testing,
- refine project UX and gallery workflows,
- complete rendered Audio Studio export, export-readiness checks and export presets,
- validate Stem Repair recommendations against real vocal, instrument and AI-generated stems,
- expand repair analysis with rumble, clipping, phase / channel and adaptive high-frequency artifact detection,
- refine master / peak-assistant UX and least-invasive automatic corrections,
- continue performance and correctness testing of multi-track audio playback / analysis,
- connect the optional image editor directly to Library and Project media actions,
- complete rendered export of non-destructive image edits,
- refine live image-editing performance and tonal behavior,
- expand editor controls only after the current non-destructive pipeline is stable.

The Duplicate Finder remains useful as a diagnostic and controlled cleanup tool, but the stronger long-term workflow is **deduplication while building or consolidating a clean target library**.

Backup is intentionally planned **after** health, verification and consolidation foundations are strong enough to prove that copied data is actually valid.

---

# 🔭 Planned extension areas

Future work may include:

- RAW image format package,
- JPEG XL support and possible archival workflows,
- richer duplicate / similarity analysis where it adds real value,
- metadata editor and cleaner,
- Audio Analyzer / Audio Tag / Cover Manager,
- optional advanced audio restoration, codec and mastering packages,
- advanced image-editing packages building on the optional Basic Image Editor,
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
