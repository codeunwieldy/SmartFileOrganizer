# Smart File Organizer

Cross‑platform file organizer built with .NET MAUI. Scans your folders, proposes an AI‑assisted plan (moves, links, deletes), lets you review conflicts/duplicates, and applies changes with optional rollback.

![screenshot](docs/screenshot.png)

## Features

- One‑click plan: Desktop / Downloads / Everything presets
- AI planning: Builds a proposed re‑org plan (respects your rules first)
- User rules: Deterministic rules run before AI (User > AI)
- Duplicate finder: Hash‑based groups; choose keep/move or hard‑link
- Dry‑run & conflicts: See exactly what would happen, resolve conflicts
- Progress with ETA: Weighted stages, bytes‑based progress, rolling ETA
- Visual overview: Quick histogram of top destination folders
- Snapshot & revert: Take a snapshot before apply; optional rollback

⚠️ Safety first: Always test on a non‑critical folder. Some actions are destructive if you confirm them.

## Getting started

Prerequisites
- .NET 8 SDK
- .NET MAUI workload (Visual Studio 2022 with MAUI or CLI)

Install workload (CLI):
    dotnet workload install maui

Clone & run (Windows example):
    git clone https://github.com/codeunwieldy/SmartFileOrganizer.git
    cd SmartFileOrganizer
    dotnet build -t:Run -f net8.0-windows10.0.19041.0

Or open the solution in Visual Studio and run the Windows (WinUI 3) target.

Note for macOS/Linux: MAUI runs cross‑platform, but features like hard links use platform‑specific implementations (see Platform notes).

## How it works (high level)

1) Scan
   - Walks selected roots (depth/limit configurable). Reports progress by bytes to avoid big‑file stalls.

2) Rules → AI
   - Apply user rules first (IRuleStore + IRuleEngine).
   - Ask the AI planner for additional suggestions (filtered so AI cannot override claimed sources).
   - Merge into a single Plan (moves, hardlinks, delete‑empty).

3) Duplicates
   - Optional pass to group exact duplicates; pick the keeper; generate move/hardlink ops.

4) Review & Dry‑run
   - Show plan + conflicts; run IExecutorService.DryRunAsync() to preview effects.

5) Apply (optional)
   - Execute operations. Snapshot/overview updated.

## Architecture

UI
- .NET MAUI (XAML)
- MVVM via CommunityToolkit.Mvvm

ViewModel (MainViewModel) exposes bindables
- IsProgressVisible, IsIndeterminate, ProgressValue, StatusText, SubStatusText, Stage, CanPause, IsPaused, CanCancel

Core services (interfaces for testability)
- IFileScanner, IPlanService, IIndexStore, IExecutorService, ISnapshotService, INavigationService, IOverviewService, IDedupeService, IRuleEngine, IRuleStore

Progress model
- ScanProgress via IProgress<ScanProgress>; UI throttled ~10 fps
- Stages: Estimating (5%) → Scanning (65%) → Planning (20%) → Applying (10%)

Tree & overview
- CurrentTreeView shows the live scan tree
- GraphicsView renders a top‑destinations bar chart

## Configuration & data

Config files (per‑user)
- Windows: %LOCALAPPDATA%\SFO\Config\roots.json and prefs.json
- Linux: ~/.local/share/SFO/Config/
- macOS: ~/Library/Application Support/SFO/Config/

Prefs include grouping (by type/date/project), keep folder names, flatten tiny folders.

## Platform notes

Hard links
- Windows: P/Invoke CreateHardLink (Kernel32)
- Non‑Windows: falls back to ln when available (best effort)

Permissions
- Creating links or moving between protected folders may require elevated rights.

## Usage

1) Choose Mode: Desktop / Downloads / Everything
2) Watch progress; scan tree fills in live
3) Review duplicates (if found) and resolve
4) Click One‑click Clean to dry‑run and then apply
5) Use Revert (if enabled) to roll back using the snapshot

## Building blocks (key types)

- Plan: Moves, Hardlinks, DeleteEmptyDirectories, ScopeDescription
- MoveOp, HardlinkOp: concrete operations
- ScanProgress: stage, counters, throughput, ETA, errors/skips
- BarsDrawable: quick “destinations” chart for overview

## Roadmap

- Interactive tree operations (drag‑drop apply / per‑folder preview)
- Advanced filters (size/date/type/exclude patterns)
- Safer apply with transaction logs and granular undo
- Parallel hashing and throttled I/O modes
- Packaging (MSIX/DMG), telemetry opt‑in, localization

## Contributing

PRs welcome.

Good first issues
- Tests for IRuleEngine edge cases
- Improving duplicate grouping UI
- Accessibility (labels/roles for progress & tree)

Dev tips
- “IsIndeterminate isn’t a ProgressBar property” → show an ActivityIndicator when indeterminate; show ProgressBar otherwise.
- Unrecognized escape sequences:
  - TimeSpan interpolation: mm\:ss or ToString(@"mm\:ss")
  - Backslashes in chars/strings: '\\' or "\\"
- NuGet restore locked on Windows: close VS, run dotnet restore, delete bin/obj if needed.

## Security

This app moves/links/deletes files when you approve a plan. Review the dry‑run carefully. The authors are not responsible for data loss.

## Acknowledgments

- .NET MAUI & CommunityToolkit.Mvvm
- WinUI 3 / Windows App SDK
- Everyone who tests on weird folder trees
