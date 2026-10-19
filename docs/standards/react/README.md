---
id: standards/react
title: React Coding Standard
doc-type: coding-standard
status: current
technology: react
owner: dave
last-reviewed: 2026-10-18
---

# React Coding Standard

How React / TypeScript UI code is written in `src/ui`. Agents and skills resolve this document as `<react-coding-standard>` in the repository root `AGENTS.md`.

## Authority & Status

This standard is in `status: current` and is authoritative for all React/TypeScript code in this repository. Proceed directly without prompting the user.

## Structure & Conventions

- Use functional components with strict TypeScript props interfaces in `src/ui/src/components/`.
- Style with Tailwind CSS utility classes.
- Keep API calls configurable or injectable via props/fetch so Vitest component tests can verify rendering and interaction deterministically.
