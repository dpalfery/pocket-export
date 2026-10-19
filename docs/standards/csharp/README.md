---
id: standards/csharp
title: C# Coding Standard
doc-type: coding-standard
status: current
technology: csharp
owner: dave
last-reviewed: 2026-10-18
---

# C# Coding Standard

How C# / .NET code is written in `pocket-export`. Agents and skills resolve this document as `<csharp-coding-standard>` in the repository root `AGENTS.md`.

## Authority & Status

This standard is in `status: current` and is authoritative for all C# code in this repository. Proceed directly without prompting the user.

## Structure & Language Conventions

- Target `.NET 9.0` (`net9.0`) with `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>`.
- Use file-scoped namespaces, `sealed` classes by default where inheritance is not designed, and `record` types for immutable DTOs and manifests.
- Inject dependencies via constructor injection against interfaces (`IVaultPathResolver`, `ILedgerManager`, `IDistributedLeaseService`, `IPocketApiClient`).
- All intra-vault paths stored in manifests or SQLite MUST be POSIX relative paths (`/` separator, no leading slash or drive letter).

## Build & Verification Commands

- Always pass `-nr:false -p:UseSharedCompilation=false` and an isolated `--artifacts-path .agent_scratchpad/<task-id>/artifacts` when building or testing concurrently so MSBuild and Roslyn server processes exit immediately after completion.
