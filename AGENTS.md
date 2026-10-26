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

MemSweep is a Windows memory (RAM) utility. Its scope, features, and technology stack are not decided yet. They will be defined in the SPEC and Design documents, and this file will gain the stack, commands, and layout once the Design is approved.

- Remote: `https://github.com/AxeenWang/memsweep.git`
- Default branch: `main`

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
- Application code (project files, source, tests, and build output) lives in one dedicated subfolder named in the Design. Never place it in the repository root.
- Before proposing folder names, check that no name repeats the meaning of its parent or child, for example `src/src` or `app/src/app`.

## Branches and Pull Requests

- Documents (rule files, SPEC, Design, and Development plans) are committed directly to `main`.
- Code changes go through a Phase branch and a pull request.
- Create a Phase branch only when that Phase starts. Never create branches for later Phases in advance. Phases run strictly in order.
- Push after each completed Task. Open the pull request only after every Task in the Phase is done.
- The user decides when to merge. Merge with a merge commit.
- A small fix that changes neither the SPEC nor the Design may use a `fix/` branch when the user agrees.
- Before every push, fetch and confirm that the remote branch still exists and that its pull request is not merged or closed.

## System Safety

MemSweep acts on the memory state of the whole machine, and the development machine is the user's daily computer.

- Running anything that changes system memory state requires the user's explicit approval for that run. This includes trimming the working sets of other processes, purging the standby or modified page lists, flushing the file cache, and enabling token privileges.
- Do not start elevated (administrator) processes or change system settings such as the page file without the user's explicit approval.
- Read-only queries of memory status and process lists are allowed.
- Never terminate the user's processes to free memory.
