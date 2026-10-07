# 🌲 Elbwald Digital – Desktop Tools

> **A collection of intuitive, cross-platform tools for organizing and managing photos, audio, and other media files.**

<p align="left">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white">
  <img alt="Avalonia UI" src="https://img.shields.io/badge/Avalonia-Desktop_UI-7B2CBF">
  <img alt="Platforms" src="https://img.shields.io/badge/Platforms-Linux%20%7C%20Windows-2E7D32">
  <img alt="Status" src="https://img.shields.io/badge/Status-Early_Development-F59E0B">
  <img alt="License" src="https://img.shields.io/badge/License-Apache--2.0-blue">
</p>

---

## What is Elbwald Desktop Tools?

**Elbwald Desktop Tools** is a modular desktop application for people who want to keep their media libraries organized without fighting complicated software.

The goal is simple:

- make common media-management tasks easy to understand,
- keep file operations safe and transparent,
- work consistently on **Linux and Windows**,
- and provide one coherent interface instead of a pile of unrelated utilities.

The project is being built as a collection of independent tools that share the same UI, safety mechanisms and application infrastructure.

---

## ✨ Planned tools

### 📷 Photo Sort

Organize large photo collections using metadata and configurable rules.

Planned capabilities include:

- analyze folders before changing anything,
- sort photos into a clean directory structure,
- use EXIF and other metadata,
- preview every planned operation,
- detect conflicts before execution,
- avoid silent overwrites,
- recover safely from interrupted operations.

---

### 🎵 Audio Tag

A modern, lightweight alternative for editing audio metadata on Linux and Windows.

Planned capabilities include:

- edit MP3 metadata,
- manage cover artwork,
- batch-edit tags,
- rename and organize audio files,
- later support formats such as **FLAC, OGG and M4A**.

---

### 🧩 More tools

Desktop Tools is designed around a modular architecture.

Additional tools can be added without turning the application into one monolithic codebase.

---

# 🛡️ Safety first

Media files are often irreplaceable.

For that reason, file safety is not an afterthought in this project — it is part of the architecture.

The core file-operation layer is being designed around principles such as:

- **never silently overwrite existing files,**
- validate operations before execution,
- check available storage before writing,
- keep a configurable free-space reserve,
- stage copies using temporary files,
- flush written data before commit where supported,
- verify copied data with **SHA-256**,
- keep persistent operation journals,
- detect interrupted transactions,
- retain recovery data when a filesystem state is ambiguous,
- prefer stopping over guessing.

For potentially destructive operations, the rule is:

> **If the application cannot prove that an operation is safe, it does not proceed.**

Cross-volume moves are intentionally treated differently from simple same-volume renames because a safe implementation requires a verified copy before the original can ever be removed.

---

# ♻️ Recovery architecture

Desktop Tools contains the foundation for a layered recovery system.

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
Recovery copy when required
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

Recovery storage is designed to support both:

- a small, fast **RAM cache** for suitable temporary operations,
- persistent recovery storage for crash-safe workflows.

A future settings page will allow the recovery location and limits to be configured. Fast SSD/NVMe storage is preferred for larger recovery workloads.

---

# 🏗️ Architecture

The application is split into a small host and reusable modules.

```text
Elbwald.DesktopTools/
├── src/
│   ├── Elbwald.DesktopTools.App/
│   ├── Elbwald.DesktopTools.Contracts/
│   ├── Elbwald.DesktopTools.Core/
│   ├── Elbwald.DesktopTools.UI/
│   └── Modules/
│       ├── Elbwald.DesktopTools.PhotoSort/
│       └── Elbwald.DesktopTools.AudioTag/
├── tests/
├── docs/
└── Elbwald.DesktopTools.sln
```

### Main projects

**Elbwald.DesktopTools.App**  
Application host, dependency injection and shell.

**Elbwald.DesktopTools.Contracts**  
Shared interfaces and contracts used across the host and modules.

**Elbwald.DesktopTools.Core**  
Core services, file-operation safety, recovery and infrastructure.

**Elbwald.DesktopTools.UI**  
Shared UI components and visual foundations.

**Modules**  
Independent tools loaded into the application.

---

# 🔌 Module system

Tools are designed to be loaded as modules instead of being hard-wired into the application.

The module loader supports:

- module manifests,
- API-version validation,
- minimum host-version checks,
- isolated assembly loading,
- graceful module-load failures,
- dynamic navigation registration.

A broken or incompatible add-on should not prevent the main application from starting.

---

# 🧰 Technology

The project currently uses:

| Area | Technology |
|---|---|
| Language | C# |
| Runtime | .NET 10 LTS |
| Desktop UI | Avalonia |
| Architecture | MVVM |
| MVVM toolkit | CommunityToolkit.Mvvm |
| Dependency injection | Microsoft.Extensions.DependencyInjection |
| Serialization | System.Text.Json |
| Testing | xUnit |
| IDE | JetBrains Rider |

Additional components such as structured logging and persistent application data will be added as needed.

---

# 🚧 Project status

**Desktop Tools is currently in early development.**

The architectural foundation already includes:

- cross-platform Avalonia application shell,
- navigation and module loading,
- Photo Sort module integration,
- module error isolation,
- safe file-operation planning,
- conflict detection,
- runtime safety checks,
- staged copy operations,
- SHA-256 verification,
- disk-space safety reserves,
- persistent operation journaling,
- persistent recovery storage,
- recovery inspection and coordination foundations.

The actual end-user Photo Sort and Audio Tag workflows are still under development.

Expect APIs, UI and internal structures to change while the project matures.

---

# 🧑‍💻 Building from source

## Requirements

- **.NET 10 SDK**
- Linux or Windows

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

---

# 🌿 Design philosophy

Desktop Tools aims to be usable without requiring users to understand the internals of filesystems, metadata formats or media libraries.

The UI is therefore designed around a few principles:

**Preview first.**  
Users should see what will happen before files are changed.

**Safe defaults.**  
Dangerous behavior must never be the easiest option.

**Clear language.**  
Technical details should be available when useful, but normal workflows should remain understandable.

**Progressive complexity.**  
Common tasks stay simple while advanced options remain accessible.

**Consistency.**  
All tools should feel like parts of one application.

---

# 🗺️ Roadmap

The current development direction is roughly:

```text
Application foundation
        ↓
Module system
        ↓
Safe file infrastructure
        ↓
Journal & recovery
        ↓
Photo Sort MVP
        ↓
Photo Sort advanced workflows
        ↓
Audio Tag MVP
        ↓
Additional media tools
```

Safety-related infrastructure is intentionally being implemented before large-scale file manipulation features.

---

# 🤝 Contributions

The repository is public primarily to make development transparent and to allow others to inspect, learn from and improve the project.

Bug reports and constructive suggestions are welcome.

Before larger contributions, opening an issue first is recommended so implementation details can be coordinated.

---

# 📜 License

Licensed under the **Apache License 2.0**.

Copyright © 2026 **Elbwald Digital**

The license permits use, modification and redistribution under its terms. Copyright and applicable license notices must be preserved.

See [`LICENSE`](LICENSE) for the full license text.

---

# 🌲 Elbwald Digital

Built with a focus on **useful software, understandable interfaces and careful handling of user data**.

**Linux first. Windows too. Files treated with respect.**
