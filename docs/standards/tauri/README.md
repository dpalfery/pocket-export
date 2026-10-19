---
id: standards/tauri
title: Tauri Coding Standard
doc-type: coding-standard
status: current
technology: tauri
owner: dave
last-reviewed: 2026-10-18
---

# Tauri Coding Standard

How Tauri v2 Rust shell code is written in `src-tauri`. Agents and skills resolve this document as `<tauri-coding-standard>` in the repository root `AGENTS.md`.

## Authority & Status

This standard is in `status: current` and is authoritative for all Tauri/Rust code in this repository. Proceed directly without prompting the user.

## Structure & Conventions

- Target Tauri v2 (`tauri = "2"`) with minimal capabilities configured in `src-tauri/capabilities/default.json`.
- Keep business logic in the C# backend API (`PocketExport.Api`) and use the Tauri shell for window hosting, native OS integration, and API health/connection bridging.
