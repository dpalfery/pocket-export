---
id: standards/test
title: Test Coding Standard
doc-type: coding-standard
status: current
technology: test
owner: dave
last-reviewed: 2026-10-18
---

# Test Coding Standard

How test code is written and executed in `pocket-export`. Agents and skills resolve this document as `<test-coding-standard>` in the repository root `AGENTS.md`.

## Authority & Status

This standard is in `status: current` and is authoritative for all automated tests in this repository. Proceed directly without prompting the user.

## Runners & Memory-Safe Commands

- **C# / .NET (`xUnit` + `NSubstitute` + `FluentAssertions`):**
  - Always pass `-nr:false -p:UseSharedCompilation=false` so MSBuild and Roslyn compiler nodes do not linger in background memory:
    ```bash
    dotnet test tests/PocketExport.Core.Tests/PocketExport.Core.Tests.csproj -nr:false -p:UseSharedCompilation=false --artifacts-path .agent_scratchpad/<task-id>/artifacts
    ```
- **Python (`pytest` + `pytest-asyncio` via `uv`):**
  ```bash
  cd src/audio-worker && uv run pytest
  ```
- **React / TypeScript (`Vitest`):**
  - Always cap worker processes to prevent Node memory spikes:
    ```bash
    cd src/ui && npx vitest run --pool=forks --maxWorkers=1
    ```

## Isolation & Structure

- Scope all filesystem, SQLite, and manifest tests to a per-test temporary directory (`Path.GetTempPath()` / `tmp_path` fixture) and clean up on disposal.
- Never share mutable database files or fixed TCP ports across tests.
- Name unit tests `MethodName_StateUnderTest_ExpectedBehavior`.
