# MemSweep

> Canonical project rules. `AGENTS_cht.md` is the Traditional Chinese reference version.
> If the two versions conflict, follow this file and report the inconsistency. Update both in the same change.

## CodeLab Governance Bootstrap

CodeLab-Governance: optional

This project supports CodeLab-managed and standalone operation.

When `../../AGENTS.md` contains the exact standalone line `CodeLab-Workspace-Root: v1` and the required anchors are available, use CodeLab-managed mode. Before project work, read `../../AGENTS.md` and `../../governance_eng.md`. Claude Code / Cowork also reads `../../CLAUDE.md`. Read `../../governance_cht.md` only for a request for the Chinese rule text, bilingual maintenance, or an inconsistency check.

Otherwise, do not search elsewhere for substitutes. Use standalone mode and project-local rules. Without user authorization, do not read or write outside the project directory. Report the mode and reason in interactive work and non-interactive output.

Change the marker to `CodeLab-Governance: required` when failed managed-mode verification must block project work.

## Project

MemSweep is a read-only Windows memory monitor. A tray app shows physical RAM usage and the applications that use it, along with commit usage and commit peaks. A command-line tool lets an agent check commit headroom before memory-heavy work. `docs/spec.md` defines what to build, and `docs/design.md` defines how.

- Remote: `https://github.com/AxeenWang/MemSweep.git`
- Default branch: `main`

### Technology

- C# on .NET 10. Windows use WPF with software rendering for the whole process; the tray icon uses the built-in WinForms `NotifyIcon`.
- Win32 access goes through `LibraryImport` and is limited to read-only queries.
- No third-party runtime dependencies. MSTest (`MSTest.Sdk`) is the only development-time package.
- Publish framework-dependent for win-x64, with both exes in one folder.
- Run every `dotnet` command inside `app/`, because `app/global.json` pins the SDK, the MSTest.Sdk version, and the test runner. For example: `(cd app && dotnet build)`.
- Test: `(cd app && dotnet test)`. Unit tests only: `(cd app && dotnet test --filter "TestCategory!=Live")`. Live tests read the real system and are read-only.
- Publish (framework-dependent, win-x64, both exes in `app/artifacts/publish/MemSweep/`): `(cd app && dotnet publish MemSweep.Desktop -c Release -r win-x64 --self-contained false -o artifacts/publish/MemSweep)` and `(cd app && dotnet publish MemSweep.Cli -c Release -r win-x64 --self-contained false -o artifacts/publish/MemSweep)`.

## Language

- Documents, code comments, and commit messages are written in Traditional Chinese (Taiwan).
- Identifiers are English: code symbols, file and folder names, and branch names.
- Commit subjects are Traditional Chinese only, with no type prefix such as `feat:` or `fix:`.

## Documents

Work follows three document layers, stored under `docs/`:

1. **SPEC**: what to build and why, including scope and non-goals.
2. **Design**: how to build it, including the technology stack, repository layout, and the ordered Phase list.
3. **Development**: one implementation plan per Phase, written when that Phase starts, not in advance.

The SPEC is approved before the Design is written, and the Design is approved before any Development plan. A Development plan quotes the SPEC and Design clauses it implements instead of paraphrasing them.

## Repository Layout

- The repository root holds only rule files, `README.md`, repository configuration, and `docs/`.
- Application code (project files, source, tests, and build output) lives in `app/`. Never place it in the repository root.
  - `app/` holds `MemSweep.slnx`, `global.json`, `Directory.Build.props`, and the projects `MemSweep.Core/`, `MemSweep.Desktop/` (builds `MemSweep.exe`), `MemSweep.Cli/` (builds `memsweep-cli.exe`), and `MemSweep.Core.Tests/`.
  - Build and publish output goes to `app/artifacts/`, which is not versioned.
- Development plans live in `docs/development/`, one file per Phase named `phase-NN-<topic>.md`.
- Before proposing folder names, check that no name repeats the meaning of its parent or child, for example `src/src` or `app/src/app`.

## Branches and Pull Requests

- Documents (rule files, SPEC, Design, and Development plans) are committed directly to `main`.
- Code changes go through a Phase branch and a pull request.
- Create a Phase branch only when that Phase starts. Never create branches for later Phases in advance. Phases run strictly in order.
- Push after each completed Task. Open the pull request only after every Task in the Phase is done.
- The user decides when to merge. Merge with a merge commit.
- After a merge, clean up every branch: delete the merged branch locally and on the remote, prune stale remote-tracking references, and confirm that only `main` remains locally, in the remote-tracking references, and on the remote, with no leftover worktree.
- A small fix that changes neither the SPEC nor the Design may use a `fix/` branch when the user agrees.
- Before every push, fetch and confirm that the remote branch still exists and that its pull request is not merged or closed.

## Releases

Design 3.12 defines the version rules and the release process. Release a new version only when the user asks.

- The version is set once, in `Version` in `app/Directory.Build.props`, as `MAJOR.MINOR.PATCH`. A fix that leaves the SPEC unchanged raises PATCH. A feature that changes the SPEC raises MINOR. An incompatible change raises MAJOR, for example changed CLI arguments, exit codes, or JSON fields, or CSV or `settings.json` files from an older version that can no longer be read.
- A version change goes through a `release/vX.Y.Z` branch and a pull request.
- After the user merges it, create an annotated tag on that merge commit in `main` and push only that tag: `git tag -a vX.Y.Z -m "MemSweep X.Y.Z"` and `git push origin vX.Y.Z`. Do not create GitHub Releases.
- Publish that commit to a new, empty `app/artifacts/release/vX.Y.Z/`: `(cd app && dotnet publish MemSweep.Desktop -c Release -r win-x64 --self-contained false -o artifacts/release/vX.Y.Z)` and `(cd app && dotnet publish MemSweep.Cli -c Release -r win-x64 --self-contained false -o artifacts/release/vX.Y.Z)`.
- The installed copy is at `D:\Utility\MemSweep\`, outside the repository. Writing there needs the user's approval for each release. The user exits the running MemSweep first; then copy the release files over the installed copy and keep its `data\`. `app/artifacts/publish/MemSweep/` is for acceptance builds only.

## System Safety

MemSweep acts on the memory state of the whole machine, and the development machine is the user's daily computer.

- Running anything that changes system memory state requires the user's explicit approval for that run. This includes trimming the working sets of other processes, purging the standby or modified page lists, flushing the file cache, and enabling token privileges.
- Do not start elevated (administrator) processes or change system settings such as the page file without the user's explicit approval.
- Read-only queries of memory status and process lists are allowed.
- Never terminate the user's processes to free memory.
