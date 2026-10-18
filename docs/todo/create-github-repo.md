---
id: todo/create-github-repo
title: Create Public GitHub Repository (pocket-export)
doc-type: todo
status: draft
owner: dave
last-reviewed: 2026-10-18
---

# Create Public GitHub Repository (`pocket-export`)

**Status:** Ready
**Date:** 2026-10-18
**Goal:** Create a new public GitHub repository named `pocket-export` under the user's GitHub account, link `origin`, and push the initial commit and specification artifacts.

## Tasks
1. Verify GitHub CLI authentication (`gh auth status`).
2. Create initial commit on `main` branch (`git add . && git commit -m "chore: bootstrap repo, docs architecture, and pocket-sync spec"`).
3. Create public repository on GitHub and push:
   ```bash
   gh repo create pocket-export --public --source=. --remote=origin --push
   ```
4. Verify remote tracking and repository visibility via `gh repo view`.
