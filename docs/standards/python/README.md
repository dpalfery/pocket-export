---
id: standards/python
title: Python Coding Standard
doc-type: coding-standard
status: current
technology: python
owner: dave
last-reviewed: 2026-10-18
---

# Python Coding Standard

How Python code is written in `src/audio-worker`. Agents and skills resolve this document as `<python-coding-standard>` in the repository root `AGENTS.md`.

## Authority & Status

This standard is in `status: current` and is authoritative for all Python code in this repository. Proceed directly without prompting the user.

## Structure & Conventions

- Manage dependencies exclusively via `uv` (`pyproject.toml` and `uv.lock` in `src/audio-worker`).
- Use explicit type annotations on all public functions and Pydantic v2 models for FastAPI request/response payloads.
- Keep heavy ML model imports (`faster_whisper`, `whisperx`, `pyannote.audio`, `wespeaker`) lazy or injectable so unit tests run fast and deterministically without downloading multi-gigabyte model weights during `pytest`.
