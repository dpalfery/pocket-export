# Implementation Tasks

**Phase status:** Approved
**Development mode:** test-first

## Overview

This document specifies the complete, ordered implementation task graph for the `pocket-sync` feature, tracing directly to approved requirements `REQ-1` through `REQ-15` and the approved technical design.

Under `development-mode: test-first`, every code task begins with the authoring of failing automated tests (RED), followed by the minimal implementation required to satisfy the contract (GREEN), and subsequent refactoring (REFACTOR) prior to task audit. Tasks marked `[no-test]` represent purely declarative or administrative milestones verified through deterministic replacement commands.

### 6-Wave Parallel Execution Strategy

To maximize throughput and prevent file contention or merge conflicts across parallel subagents:
- **Wave 1 (Bootstrap & Scaffolding):** Task 2 scaffolds the .NET solution, pre-installing all NuGet packages (`Microsoft.Data.Sqlite`, `Dapper`, `TagLibSharp`, `YamlDotNet`, `Polly`, `Microsoft.Extensions.Http.Resilience`, `Microsoft.AspNetCore.Mvc.Testing`, `Moq` / `NSubstitute`, `FluentAssertions`) and defining shared base DTO records. Task 1 then initializes git and pushes the initial scaffolded solution to GitHub.
- **Wave 2 (Foundations & Independent ML / UI Scaffolding):** Tasks 3 (`VaultPathResolver`), 7 (Python ASR / forced alignment), 8 (Python diarization & biometrics), 9 (C# worker supervisor & IPC client), and 14 (Tauri v2 shell & React dashboard) execute concurrently, all depending solely on Task 2.
- **Wave 3 (Ledger Persistence & GUI Modals):** Task 4 (`LedgerManager` & SQLite projection) executes following Task 3, while Tasks 15 (speaker auditioning), 16 (retroactive matches modal), and 17 (taxonomy editor & re-transcribe dialog) execute concurrently following Task 14.
- **Wave 4 (Domain Services):** Tasks 5 (distributed leases), 10 (voicebank service & cosine math), 11 (taxonomy classifier & TagLibSharp tagger), and 12 (vault folder router & summarizer) execute concurrently, all depending solely on Task 4.
- **Wave 5 (Client Ingestion & API Layer):** Task 6 (Pocket API sync engine) executes after Tasks 4 & 5; Task 13 (ASP.NET Core Web API controllers & HTTP 206 byte-range audio streamer) executes after Tasks 4, 10, 11, & 12.
- **Wave 6 (End-to-End Verification & Closeout):** Task 18 executes full end-to-end integration and multi-machine failover tests, followed by Task 19 (`docs-dev` closeout and system documentation migration).

---

## Tasks

### Wave 1: Bootstrap & Solution Scaffolding

- [x] 2. Solution & Project Scaffolding `[no-test]`
  - **Objective:** Scaffold the cross-platform solution structure: .NET 9/10 solution with core, API, and test projects; Tauri v2 desktop shell with React 18+ and TypeScript frontend; and hermetic Python 3.12 ML sidecar environment managed via `uv`. Pre-install all required NuGet packages (`Microsoft.Data.Sqlite`, `Dapper`, `TagLibSharp`, `YamlDotNet`, `Polly`, `Microsoft.Extensions.Http.Resilience`, `Microsoft.AspNetCore.Mvc.Testing`, `Moq`, `NSubstitute`, `FluentAssertions`) into `.csproj` files and declare shared base DTO records (`RecordingManifest`, `SpeakerProfile`, `TaxonomyConfig`, `AudioSnippetBounds`) so parallel C# workers in Waves 2–5 only add files in their own namespaces and never contend on project manifests.
  - **Exact files / components:**
    - `PocketExport.sln`
    - `src/PocketExport.Core/PocketExport.Core.csproj`
    - `src/PocketExport.Core/Models/CommonModels.cs`
    - `src/PocketExport.Api/PocketExport.Api.csproj`
    - `tests/PocketExport.Core.Tests/PocketExport.Core.Tests.csproj`
    - `src-tauri/Cargo.toml`, `src-tauri/tauri.conf.json`, `src-tauri/src/main.rs`
    - `src/ui/package.json`, `src/ui/tsconfig.json`, `src/ui/vite.config.ts`, `src/ui/src/App.tsx`
    - `src/audio-worker/pyproject.toml`, `src/audio-worker/uv.lock`, `src/audio-worker/src/__init__.py`
  - **Acceptance criteria:** Solution builds cleanly without warnings; Python environment locks and installs all base dependencies via `uv`; React/Vite development server builds without errors; NuGet dependencies and shared models are available across all downstream projects.
  - **Dependencies:** None.
  - **Required skills:** `csharp-dev`, `tauri-dev`, `react-dev`, `python-dev`, `github-devops`.
  - **Traced requirement IDs:** REQ-1, REQ-2, REQ-5, REQ-12.
  - **Test contract:** `[no-test]`
    - *Replacement validation:*
      1. `dotnet build PocketExport.sln` (exit code 0, 0 errors).
      2. `cd src/audio-worker && uv sync` (exit code 0).
      3. `cd src/ui && npm install && npm run build` (exit code 0).

- [x] 1. GitHub Repository Bootstrap & Initial Push `[no-test]`
  - **Objective:** Initialize the public GitHub repository `pocket-export`, configure git remote `origin`, create the initial commit on `main` containing the scaffolded solution, and push the initial project tree and specification artifacts (fulfilling `docs/todo/create-github-repo.md`).
  - **Exact files / components:** `.git/config`, `docs/todo/create-github-repo.md`.
  - **Acceptance criteria:** Git repository has clean status on branch `main`; remote `origin` points to `pocket-export`; initial commit contains scaffolded code, bootstrap documentation, and specifications; GitHub repository is public and accessible via GitHub CLI.
  - **Dependencies:** Task 2.
  - **Required skills:** `github-devops`.
  - **Traced requirement IDs:** None (Repository Setup).
  - **Test contract:** `[no-test]`
    - *Replacement validation:* Run `gh auth status` (exit code 0), `git status --porcelain` (empty), and `gh repo view pocket-export --json name,visibility -q '.name + " " + .visibility'` (returns `pocket-export PUBLIC`).

---

### Wave 2: Core Foundations, Independent ML Sidecar & GUI Scaffolding

- [x] 3. Vault Storage & POSIX Relative Path Normalizer
  - **Objective:** Implement `VaultPathResolver` and vault directory initialization logic in `PocketExport.Core`, enforcing platform-agnostic POSIX relative paths, cross-platform filename sanitization, and strict prohibition of absolute host filesystem paths.
  - **Exact files / components:**
    - `src/PocketExport.Core/Storage/IVaultPathResolver.cs`
    - `src/PocketExport.Core/Storage/VaultPathResolver.cs`
    - `tests/PocketExport.Core.Tests/Storage/VaultPathResolverTests.cs`
  - **Acceptance criteria:** REQ-2.1, 2.2, 2.3, 2.4, 2.5. Correctly converts Windows backslash and macOS UNIX paths to POSIX relative paths originating from `VaultRoot`; rejects absolute paths with `ArgumentException`; sanitizes illegal characters (`: * ? " < > |`); creates standard vault directories (`staging`, `.ledger/recordings`, `.ledger/speakers`, `.ledger/leases`, `.ledger/locks`, `.ledger/templates`, `routed`, `_needs_review`).
  - **Dependencies:** Task 2.
  - **Required skills:** `test-dev`, `csharp-dev`.
  - **Traced requirement IDs:** REQ-2, REQ-3.
  - **Test contract (test-first):**
    - *Test project / file:* `tests/PocketExport.Core.Tests/Storage/VaultPathResolverTests.cs`
    - *Test runner:* `dotnet test tests/PocketExport.Core.Tests/PocketExport.Core.Tests.csproj --filter FullyQualifiedName~VaultPathResolverTests`
    - *Observable behavior:* Converts `C:\Vault\staging\rec1.m4a` to `staging/rec1.m4a`; resolves `staging/rec1.m4a` to full platform path; rejects `C:\Windows\...` or `/etc/...` as intra-vault references; creates intermediate directories.
    - *Expected RED reason:* Class `VaultPathResolver` does not exist; compilation fails.
    - *Expected GREEN acceptance:* All path normalization, conversion, sanitization, and folder creation assertions pass with 100% test success.
    - *Sequence:* RED → GREEN → REFACTOR.

- [x] 7. Hermetic Python ML Worker (ASR & Forced Alignment)
  - **Objective:** Implement ASR and phoneme forced alignment in `src/audio-worker`, utilizing `faster-whisper` (`large-v3-turbo` / `large-v3` / `parakeet-tdt-0.6b-v2` via CUDA 12.8 on Windows RTX 5090) and `mlx-whisper` on macOS Metal, followed by `WhisperX` Wav2Vec2 alignment to generate word-level millisecond timestamps.
  - **Exact files / components:**
    - `src/audio-worker/src/asr.py`
    - `src/audio-worker/src/aligner.py`
    - `src/audio-worker/src/server.py`
    - `src/audio-worker/tests/test_asr.py`
    - `src/audio-worker/tests/test_aligner.py`
  - **Acceptance criteria:** REQ-5.1, 5.2, 5.3, 5.4, 5.5, 5.6. Model executes in FP16 on CUDA 12.8 / Metal; forced alignment computes word-level millisecond intervals; standardized JSON transcript returned.
  - **Dependencies:** Task 2.
  - **Required skills:** `test-dev`, `python-dev`.
  - **Traced requirement IDs:** REQ-5.
  - **Test contract (test-first):**
    - *Test project / file:* `src/audio-worker/tests/test_asr.py`, `src/audio-worker/tests/test_aligner.py`
    - *Test runner:* `cd src/audio-worker && uv run pytest tests/test_asr.py tests/test_aligner.py`
    - *Observable behavior:* Mock or synthetic WAV input processed through ASR and Wav2Vec2 aligner; returns JSON containing `segments` and `words` with positive, monotonic `start_ms` and `end_ms`.
    - *Expected RED reason:* Python modules `asr.py` and `aligner.py` missing or uncallable.
    - *Expected GREEN acceptance:* Pytest suite passes; output matches standardized JSON schema.
    - *Sequence:* RED → GREEN → REFACTOR.

- [x] 8. Python Diarization, WeSpeaker CAM++ 512-d Biometrics & Solo Snippet Extractor
  - **Objective:** Implement speaker diarization via `pyannote/speaker-diarization-community-1`, 512-d L2-normalized speaker embedding extraction via `WeSpeaker CAM++` (or `TitaNet`), and the cleanest 8-10 second non-overlapping solo audio snippet extractor algorithm.
  - **Exact files / components:**
    - `src/audio-worker/src/diarizer.py`
    - `src/audio-worker/src/biometrics.py`
    - `src/audio-worker/src/snippet_selector.py`
    - `src/audio-worker/tests/test_diarizer.py`
    - `src/audio-worker/tests/test_biometrics.py`
    - `src/audio-worker/tests/test_snippet_selector.py`
  - **Acceptance criteria:** REQ-6.1, 6.2, 6.3, 6.4, 6.5, 6.6. Audio segmented into `SPEAKER_XX` turns; word tokens mapped to speakers; 512-d embeddings L2-normalized ($\sum e_i^2 = 1.0 \pm 1e^{-6}$); cleanest 8-10s solo speech interval isolated (or longest if < 8s).
  - **Dependencies:** Task 2.
  - **Required skills:** `test-dev`, `python-dev`.
  - **Traced requirement IDs:** REQ-6.
  - **Test contract (test-first):**
    - *Test project / file:* `src/audio-worker/tests/test_diarizer.py`, `src/audio-worker/tests/test_biometrics.py`, `src/audio-worker/tests/test_snippet_selector.py`
    - *Test runner:* `cd src/audio-worker && uv run pytest tests/`
    - *Observable behavior:* Diarization outputs turn segments; biometrics outputs 512 float vector with L2 norm of 1.0; snippet selector picks non-overlapping solo window between 8000ms and 10000ms.
    - *Expected RED reason:* Modules missing or snippet selector boundary math failing edge cases.
    - *Expected GREEN acceptance:* All Python unit tests pass with clean exit code 0.
    - *Sequence:* RED → GREEN → REFACTOR.

- [x] 9. C# Audio Worker Supervisor & IPC Client
  - **Objective:** Implement `AudioWorkerSupervisor` and `AudioWorkerClient` in `PocketExport.Core` to launch the hermetic Python sidecar, supervise child process lifecycle, enforce a 16GB memory cap, monitor loopback health, and dispatch audio transcription/diarization jobs with error recovery.
  - **Exact files / components:**
    - `src/PocketExport.Core/Ml/IAudioWorkerSupervisor.cs`, `src/PocketExport.Core/Ml/AudioWorkerSupervisor.cs`
    - `src/PocketExport.Core/Ml/IAudioWorkerClient.cs`, `src/PocketExport.Core/Ml/AudioWorkerClient.cs`
    - `tests/PocketExport.Core.Tests/Ml/AudioWorkerSupervisorTests.cs`
  - **Acceptance criteria:** REQ-5.1, 5.6, REQ-6.6. Worker started on loopback port; health check succeeds; process killed and restarted if memory exceeds 16GB or crashes; task status updated to `TranscriptionFailed` upon terminal error.
  - **Dependencies:** Task 2.
  - **Required skills:** `test-dev`, `csharp-dev`.
  - **Traced requirement IDs:** REQ-5, REQ-6.
  - **Test contract (test-first):**
    - *Test project / file:* `tests/PocketExport.Core.Tests/Ml/AudioWorkerSupervisorTests.cs`
    - *Test runner:* `dotnet test tests/PocketExport.Core.Tests/PocketExport.Core.Tests.csproj --filter FullyQualifiedName~AudioWorkerSupervisorTests`
    - *Observable behavior:* Supervisor starts worker mock process, queries health endpoint, detects simulated OOM/crash, logs stderr, and restarts cleanly.
    - *Expected RED reason:* Supervisor classes missing or process management logic unimplemented.
    - *Expected GREEN acceptance:* Process launch, health polling, OOM recovery, and IPC communication tests pass.
    - *Sequence:* RED → GREEN → REFACTOR.

- [x] 14. Tauri v2 Core & React Frontend Recordings Dashboard
  - **Objective:** Implement desktop shell in Tauri v2 (`src-tauri`) and React dashboard (`src/ui`) displaying recordings from the local SQLite index with columns for date, title, duration, domain, customer, project, speakers, status, and routed path. Support search, multi-facet filtering, detail pane inspection, and live background pipeline progress badges.
  - **Exact files / components:**
    - `src-tauri/src/main.rs`
    - `src/ui/src/App.tsx`, `src/ui/src/components/RecordingsDashboard.tsx`
    - `src/ui/src/components/FilterBar.tsx`, `src/ui/src/components/PipelineProgressBar.tsx`, `src/ui/src/components/RecordingDetailPane.tsx`
    - `src/ui/src/tests/RecordingsDashboard.test.tsx`
  - **Acceptance criteria:** REQ-12.1, 12.2, 12.3, 12.4, 12.5. Dashboard displays recordings; search and taxonomy filters update table instantly; row selection reveals transcript, summary, and metadata; live status indicators reflect active ML pipeline stages.
  - **Dependencies:** Task 2.
  - **Required skills:** `test-dev`, `tauri-dev`, `react-dev`.
  - **Traced requirement IDs:** REQ-12.
  - **Test contract (test-first):**
    - *Test project / file:* `src/ui/src/tests/RecordingsDashboard.test.tsx`
    - *Test runner:* `cd src/ui && npm run test -- RecordingsDashboard.test.tsx`
    - *Observable behavior:* Dashboard renders rows; typing in search filters list; selecting "Work" facet filters out "Personal"; detail pane opens on row click.
    - *Expected RED reason:* Components missing or React test fails on missing DOM elements.
    - *Expected GREEN acceptance:* Vitest component tests pass with 100% assertions satisfied.
    - *Sequence:* RED → GREEN → REFACTOR.

---

### Wave 3: Ledger Persistence & GUI Modals

- [x] 4. Atomic Ledger Manager & Local SQLite Projection
  - **Objective:** Implement `LedgerManager` in `PocketExport.Core` to write atomic manifests (`.tmp` write followed by `File.Move` with linear retry on OneDrive file locks) for recordings, speakers, leases, and taxonomy; implement `LedgerProjectionService` with `FileSystemWatcher` to incrementally hydrate and update the local machine SQLite index (`index.db` in `%LOCALAPPDATA%` on Windows, `~/Library/Application Support` on macOS).
  - **Exact files / components:**
    - `src/PocketExport.Core/Ledger/ILedgerManager.cs`, `src/PocketExport.Core/Ledger/LedgerManager.cs`
    - `src/PocketExport.Core/Database/SqliteIndexContext.cs`
    - `src/PocketExport.Core/Database/LedgerProjectionService.cs`
    - `tests/PocketExport.Core.Tests/Ledger/LedgerManagerTests.cs`
    - `tests/PocketExport.Core.Tests/Ledger/LedgerProjectionServiceTests.cs`
  - **Acceptance criteria:** REQ-3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9. Atomic replaces preserve data integrity under concurrent reads; locks retry up to 5s with linear backoff; SQLite schema matches design; `FileSystemWatcher` debounces and upserts to SQLite within 500ms; full rebuild succeeds on startup.
  - **Dependencies:** Task 3.
  - **Required skills:** `test-dev`, `csharp-dev`.
  - **Traced requirement IDs:** REQ-3, REQ-7.
  - **Test contract (test-first):**
    - *Test project / file:* `tests/PocketExport.Core.Tests/Ledger/LedgerManagerTests.cs`, `tests/PocketExport.Core.Tests/Ledger/LedgerProjectionServiceTests.cs`
    - *Test runner:* `dotnet test tests/PocketExport.Core.Tests/PocketExport.Core.Tests.csproj --filter FullyQualifiedName~Ledger`
    - *Observable behavior:* Manifests written atomically without corrupting existing files; concurrent write simulation verifies linear backoff retry; FSW event updates SQLite record within 500ms; deleting SQLite file and calling `HydrateAll()` restores all rows from disk manifests.
    - *Expected RED reason:* Types and methods not implemented; tests fail to compile or throw `NotImplementedException`.
    - *Expected GREEN acceptance:* All atomic write, retry, FSW sync, and SQLite projection tests pass.
    - *Sequence:* RED → GREEN → REFACTOR.

- [x] 15. GUI Solo Snippet Auditioning & One-Click Speaker Enrollment
  - **Objective:** Implement `SpeakerAudition` component in React allowing playback of 8-10s solo audio snippets via HTML5 audio from the C# byte-range endpoint, and inline enrollment form with Name, Organization/Customer, and "Enroll" action updating the voicebank and recording manifest.
  - **Exact files / components:**
    - `src/ui/src/components/SpeakerAudition.tsx`
    - `src/ui/src/components/SpeakerEnrollModal.tsx`
    - `src/ui/src/tests/SpeakerAudition.test.tsx`
  - **Acceptance criteria:** REQ-13.1, 13.2, 13.3, 13.4, 13.5, 13.6. Play button streams audio slice; enrollment form posts to `/api/speakers/enroll`; UI updates speaker label from `SPEAKER_XX` to enrolled name.
  - **Dependencies:** Task 14.
  - **Required skills:** `test-dev`, `react-dev`.
  - **Traced requirement IDs:** REQ-13.
  - **Test contract (test-first):**
    - *Test project / file:* `src/ui/src/tests/SpeakerAudition.test.tsx`
    - *Test runner:* `cd src/ui && npm run test -- SpeakerAudition.test.tsx`
    - *Observable behavior:* Play button triggers audio play on snippet URL; clicking enroll submits display name and org to API; optimistic UI update replaces speaker badge.
    - *Expected RED reason:* `SpeakerAudition` component missing or audio playback event handlers unmocked.
    - *Expected GREEN acceptance:* Vitest component tests pass.
    - *Sequence:* RED → GREEN → REFACTOR.

- [x] 16. GUI Retroactive Speaker Match Modal Dialog & Guided Batch Re-Routing
  - **Objective:** Implement `RetroactiveMatchesModal` and notification banner in React to present historical recordings matching a newly enrolled speaker's voiceprint ($\text{Sim} \ge 0.75$), with snippet playback, confidence scores, current vs projected folder paths, and single/batch approval buttons that trigger atomic folder moves and re-summarization without unprompted background file movements.
  - **Exact files / components:**
    - `src/ui/src/components/RetroactiveMatchesModal.tsx`
    - `src/ui/src/components/MatchNotificationBanner.tsx`
    - `src/ui/src/tests/RetroactiveMatchesModal.test.tsx`
  - **Acceptance criteria:** REQ-14.1, 14.2, 14.3, 14.4, 14.5, 14.6, 14.7. Banner alerts user when proposals exist; modal lists recordings with current and proposed destination folders; approving items invokes API; rejecting records dismissal in SQLite.
  - **Dependencies:** Task 14.
  - **Required skills:** `test-dev`, `react-dev`.
  - **Traced requirement IDs:** REQ-14.
  - **Test contract (test-first):**
    - *Test project / file:* `src/ui/src/tests/RetroactiveMatchesModal.test.tsx`
    - *Test runner:* `cd src/ui && npm run test -- RetroactiveMatchesModal.test.tsx`
    - *Observable behavior:* Modal displays suggested recordings; clicking approve sends POST to `/api/speakers/retroactive-matches/approve`; rejected item removes row and updates database.
    - *Expected RED reason:* Modal component missing or batch approval handler unimplemented.
    - *Expected GREEN acceptance:* Vitest component tests pass.
    - *Sequence:* RED → GREEN → REFACTOR.

- [x] 17. GUI Manual Re-Tagging, Re-Transcription, & Re-Summarization Workflows
  - **Objective:** Implement action workflows in the React Recording Detail view allowing manual editing of taxonomy tags (`Domain`, `Customer`, `Project`, `Initiative`) with atomic file move and audio tag updates, an on-demand "Re-Transcribe" dialog (with model and beam selection), and an on-demand "Re-Summarize" button.
  - **Exact files / components:**
    - `src/ui/src/components/TaxonomyEditor.tsx`
    - `src/ui/src/components/ReTranscribeDialog.tsx`
    - `src/ui/src/tests/TaxonomyEditor.test.tsx`
  - **Acceptance criteria:** REQ-15.1, 15.2, 15.3, 15.4, 15.5, 15.6. Taxonomy edits atomically move files and trigger re-summarization if template changes; re-transcribe dispatches job to worker with chosen parameters; re-summarize regenerates summary via LM Studio.
  - **Dependencies:** Task 14.
  - **Required skills:** `test-dev`, `react-dev`.
  - **Traced requirement IDs:** REQ-15.
  - **Test contract (test-first):**
    - *Test project / file:* `src/ui/src/tests/TaxonomyEditor.test.tsx`
    - *Test runner:* `cd src/ui && npm run test -- TaxonomyEditor.test.tsx`
    - *Observable behavior:* Changing customer and clicking "Save & Move" invokes re-tag API; selecting model in Re-Transcribe dialog triggers worker job; UI indicates in-flight task progress.
    - *Expected RED reason:* Editor component missing or form submission logic unwritten.
    - *Expected GREEN acceptance:* Vitest component tests pass.
    - *Sequence:* RED → GREEN → REFACTOR.

---

### Wave 4: Domain Services (Dependent on Ledger)

- [x] 5. Distributed Lease & Work Lock Coordination Service
  - **Objective:** Implement `DistributedLeaseService` in `PocketExport.Core` managing worker roles (`PrimaryPollerWorker` on Windows, `SecondaryClient` on macOS), 60-second lease manifests in `.ledger/leases/primary_poller.json` with 20-second heartbeat renewals, automatic expiration failover, and item-specific on-demand execution locks in `.ledger/locks/{recording_id}.lock`.
  - **Exact files / components:**
    - `src/PocketExport.Core/Coordination/IDistributedLeaseService.cs`
    - `src/PocketExport.Core/Coordination/DistributedLeaseService.cs`
    - `tests/PocketExport.Core.Tests/Coordination/DistributedLeaseServiceTests.cs`
  - **Acceptance criteria:** REQ-4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 4.8. Primary node acquires and renews lease; secondary node detects active lease and suppresses automated polling; item lock prevents concurrent execution on the same recording.
  - **Dependencies:** Task 4.
  - **Required skills:** `test-dev`, `csharp-dev`.
  - **Traced requirement IDs:** REQ-4.
  - **Test contract (test-first):**
    - *Test project / file:* `tests/PocketExport.Core.Tests/Coordination/DistributedLeaseServiceTests.cs`
    - *Test runner:* `dotnet test tests/PocketExport.Core.Tests/PocketExport.Core.Tests.csproj --filter FullyQualifiedName~DistributedLeaseServiceTests`
    - *Observable behavior:* Lease written with 60s TTL; heartbeat renews TTL; secondary node detects valid lease; simulated node crash allows standby to claim after 60s; item lock acquisition rejects simultaneous claim.
    - *Expected RED reason:* `DistributedLeaseService` does not exist or lease acquisition logic unimplemented.
    - *Expected GREEN acceptance:* All lease acquisition, heartbeat, expiration, and lock conflict tests pass.
    - *Sequence:* RED → GREEN → REFACTOR.

- [x] 10. Speaker Voicebank Repository & Cosine Similarity Matching Engine
  - **Objective:** Implement `SpeakerVoicebankService` in `PocketExport.Core` to store enrolled speaker profiles (`.ledger/speakers/{id}.json`), calculate vector cosine similarity against 512-d embeddings, match speakers against thresholds (match $\ge 0.75$, disambiguate $0.70-0.74$, unidentified $< 0.70$), correlate Pocket cloud speaker hints, and record proposed retroactive matches in SQLite.
  - **Exact files / components:**
    - `src/PocketExport.Core/Speakers/ISpeakerVoicebankService.cs`, `src/PocketExport.Core/Speakers/SpeakerVoicebankService.cs`
    - `src/PocketExport.Core/Speakers/VectorMath.cs`
    - `tests/PocketExport.Core.Tests/Speakers/SpeakerVoicebankServiceTests.cs`
  - **Acceptance criteria:** REQ-7.1, 7.2, 7.3, 7.4, 7.5, 7.6, REQ-14.1, 14.2. Cosine dot product correct; threshold boundaries strictly honored; Pocket cloud speaker name hints elevate candidate ranking; historical recordings scanned upon enrollment and populated in `proposed_retroactive_matches` table.
  - **Dependencies:** Task 4.
  - **Required skills:** `test-dev`, `csharp-dev`.
  - **Traced requirement IDs:** REQ-7, REQ-14.
  - **Test contract (test-first):**
    - *Test project / file:* `tests/PocketExport.Core.Tests/Speakers/SpeakerVoicebankServiceTests.cs`
    - *Test runner:* `dotnet test tests/PocketExport.Core.Tests/PocketExport.Core.Tests.csproj --filter FullyQualifiedName~SpeakerVoicebankServiceTests`
    - *Observable behavior:* `CosineSimilarity(v1, v2)` validates orthogonality and identity; matching resolves enrolled speaker for score $\ge 0.75$; flags disambiguation for $0.72$; scans past recordings on new speaker enrollment and populates proposal rows.
    - *Expected RED reason:* Vector math or voicebank service missing.
    - *Expected GREEN acceptance:* All vector math, threshold matching, and retroactive scan unit tests pass.
    - *Sequence:* RED → GREEN → REFACTOR.

- [x] 11. Taxonomy Classification Engine & TagLibSharp Audio Container Tagger
  - **Objective:** Implement `TaxonomyClassifier` in `PocketExport.Core` to parse `taxonomy.yaml`, construct structured JSON schema prompts, and query LM Studio (`http://localhost:1234/v1/chat/completions`) to determine `Domain`, `Customer`, `Project`, `Initiative`, `confidence`, and `template_id`; implement `AudioContainerTagger` using `TagLibSharp` to write container metadata tags (Title, Artist, Album, Year, Comment) to `staging/{id}.{ext}` without modifying audio data streams.
  - **Exact files / components:**
    - `src/PocketExport.Core/Taxonomy/ITaxonomyClassifier.cs`, `src/PocketExport.Core/Taxonomy/TaxonomyClassifier.cs`
    - `src/PocketExport.Core/Metadata/IAudioContainerTagger.cs`, `src/PocketExport.Core/Metadata/AudioContainerTagger.cs`
    - `tests/PocketExport.Core.Tests/Taxonomy/TaxonomyClassifierTests.cs`
    - `tests/PocketExport.Core.Tests/Metadata/AudioContainerTaggerTests.cs`
  - **Acceptance criteria:** REQ-8.1, 8.2, 8.3, 8.4, 8.5, 8.6, REQ-9.1, 9.2, 9.3, 9.4, 9.5. Validates YAML taxonomy; LM Studio JSON schema response parsed into strongly-typed model; TagLibSharp writes tags to MP3/M4A; audio payload checksum unmodified.
  - **Dependencies:** Task 4.
  - **Required skills:** `test-dev`, `csharp-dev`.
  - **Traced requirement IDs:** REQ-8, REQ-9.
  - **Test contract (test-first):**
    - *Test project / file:* `tests/PocketExport.Core.Tests/Taxonomy/TaxonomyClassifierTests.cs`, `tests/PocketExport.Core.Tests/Metadata/AudioContainerTaggerTests.cs`
    - *Test runner:* `dotnet test tests/PocketExport.Core.Tests/PocketExport.Core.Tests.csproj --filter FullyQualifiedName~Taxonomy|FullyQualifiedName~AudioContainerTagger`
    - *Observable behavior:* Mock LM Studio validates JSON schema response; audio file tagged with metadata; audio stream MD5 checksum identical before and after tagging.
    - *Expected RED reason:* Taxonomy classifier or TagLibSharp integration classes missing.
    - *Expected GREEN acceptance:* All taxonomy parsing, LLM response validation, and audio tagging tests pass.
    - *Sequence:* RED → GREEN → REFACTOR.

- [x] 12. Vault Folder Router & LM Studio Template Markdown Summarizer
  - **Objective:** Implement `VaultFolderRouter` in `PocketExport.Core` to route formatted transcripts to `routed/{Domain}/{Customer}/{Project}/{YYYY-MM-DD}_{id}_{slug}.md` (if confidence $\ge 0.75$) or `_needs_review/` (if confidence $< 0.75$), with collision suffixing; implement `TemplateSummarizer` to load templates from `.ledger/templates/{template_id}.md` (falling back to `default.md`), prompt LM Studio, and write formatted Markdown summaries.
  - **Exact files / components:**
    - `src/PocketExport.Core/Routing/IVaultFolderRouter.cs`, `src/PocketExport.Core/Routing/VaultFolderRouter.cs`
    - `src/PocketExport.Core/Summarization/ITemplateSummarizer.cs`, `src/PocketExport.Core/Summarization/TemplateSummarizer.cs`
    - `tests/PocketExport.Core.Tests/Routing/VaultFolderRouterTests.cs`
    - `tests/PocketExport.Core.Tests/Summarization/TemplateSummarizerTests.cs`
  - **Acceptance criteria:** REQ-10.1, 10.2, 10.3, 10.4, 10.5, 10.6, REQ-11.1, 11.2, 11.3, 11.4, 11.5, 11.6. Transcripts routed to expected folders; intermediate dirs created; filename collisions handled monotonically (`_1`, `_2`); template loaded and populated via LM Studio; fallback to `default.md` if template missing.
  - **Dependencies:** Task 4.
  - **Required skills:** `test-dev`, `csharp-dev`.
  - **Traced requirement IDs:** REQ-10, REQ-11.
  - **Test contract (test-first):**
    - *Test project / file:* `tests/PocketExport.Core.Tests/Routing/VaultFolderRouterTests.cs`, `tests/PocketExport.Core.Tests/Summarization/TemplateSummarizerTests.cs`
    - *Test runner:* `dotnet test tests/PocketExport.Core.Tests/PocketExport.Core.Tests.csproj --filter FullyQualifiedName~VaultFolderRouter|FullyQualifiedName~TemplateSummarizer`
    - *Observable behavior:* High-confidence transcript placed in `routed/Work/Customer/Project/`; low-confidence placed in `_needs_review/`; duplicate path appends `_1`; template prompt rendered and summary written.
    - *Expected RED reason:* Router or summarizer missing.
    - *Expected GREEN acceptance:* All routing logic, collision handling, and template summarization tests pass.
    - *Sequence:* RED → GREEN → REFACTOR.

---

### Wave 5: Client Ingestion & API Layer

- [x] 6. Pocket AI Cloud Ingestion & Staging Client
  - **Objective:** Implement `PocketApiClient` and `PocketSyncEngine` in `PocketExport.Core` to authenticate via bearer token, poll `GET /api/v1/public/recordings`, fetch pre-signed S3 download URLs (`GET /public/recordings/{id}/audio-url`), fetch cloud metadata (`GET /public/recordings/{id}`), stream audio to `staging/{id}.{ext}` with HTTP content-length integrity checks, serialize `staging/{id}.pocket.json`, and apply Polly exponential backoff on HTTP 429/5xx errors.
  - **Exact files / components:**
    - `src/PocketExport.Core/Pocket/IPocketApiClient.cs`, `src/PocketExport.Core/Pocket/PocketApiClient.cs`
    - `src/PocketExport.Core/Pocket/PocketSyncEngine.cs`
    - `tests/PocketExport.Core.Tests/Pocket/PocketSyncEngineTests.cs`
  - **Acceptance criteria:** REQ-1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7. Raw audio streamed to staging; `.pocket.json` saved verbatim; atomic recording manifest created with status `Staged`; Polly backoff triggered on rate limits.
  - **Dependencies:** Task 4, Task 5.
  - **Required skills:** `test-dev`, `csharp-dev`.
  - **Traced requirement IDs:** REQ-1.
  - **Test contract (test-first):**
    - *Test project / file:* `tests/PocketExport.Core.Tests/Pocket/PocketSyncEngineTests.cs`
    - *Test runner:* `dotnet test tests/PocketExport.Core.Tests/PocketExport.Core.Tests.csproj --filter FullyQualifiedName~PocketSyncEngineTests`
    - *Observable behavior:* Mock HTTP server verifies recordings fetch, audio download streaming, content-length verification, 429 backoff retry, and creation of `staging/{id}.m4a`, `staging/{id}.pocket.json`, and `.ledger/recordings/{id}.json`.
    - *Expected RED reason:* Unimplemented client methods; missing Polly policy handlers.
    - *Expected GREEN acceptance:* Mock sync cycle passes, downloads audio, verifies hashes, and handles transient HTTP errors gracefully.
    - *Sequence:* RED → GREEN → REFACTOR.

- [x] 13. ASP.NET Core Kestrel Web API Endpoints & Byte-Range Audio Slicing Endpoint
  - **Objective:** Implement ASP.NET Core controllers in `src/PocketExport.Api` providing REST endpoints for querying recordings (with pagination, search, taxonomy filters), retrieving recording details, initiating mutations (re-tag, re-transcribe, re-summarize, enroll speaker), approving retroactive matches, and serving HTTP 206 byte-range audio slices for speaker solo snippets `[start_ms, end_ms]`.
  - **Exact files / components:**
    - `src/PocketExport.Api/Controllers/RecordingsController.cs`
    - `src/PocketExport.Api/Controllers/SpeakersController.cs`
    - `src/PocketExport.Api/Services/AudioSnippetStreamer.cs`
    - `tests/PocketExport.Core.Tests/Api/RecordingsControllerTests.cs`
    - `tests/PocketExport.Core.Tests/Api/AudioSnippetStreamerTests.cs`
  - **Acceptance criteria:** REQ-12.1, 12.2, 12.3, REQ-13.2, 13.3, 13.4, 13.5, REQ-14.6, REQ-15.1, 15.4, 15.6. Endpoints return valid JSON; snippet endpoint returns HTTP 206 Partial Content with correct audio duration and headers; mutation endpoints update ledger manifests atomically.
  - **Dependencies:** Task 4, Task 10, Task 11, Task 12.
  - **Required skills:** `test-dev`, `csharp-dev`.
  - **Traced requirement IDs:** REQ-12, REQ-13, REQ-14, REQ-15.
  - **Test contract (test-first):**
    - *Test project / file:* `tests/PocketExport.Core.Tests/Api/RecordingsControllerTests.cs`, `tests/PocketExport.Core.Tests/Api/AudioSnippetStreamerTests.cs`
    - *Test runner:* `dotnet test tests/PocketExport.Core.Tests/PocketExport.Core.Tests.csproj --filter FullyQualifiedName~Api`
    - *Observable behavior:* `WebApplicationFactory` tests verify `GET /api/recordings` filtering, `POST /api/speakers/enroll` atomic write, and `GET /api/recordings/{id}/speakers/{idx}/snippet` returns 206 Partial Content with `Content-Range`.
    - *Expected RED reason:* API controllers or snippet streaming service not implemented.
    - *Expected GREEN acceptance:* All Web API integration tests pass with 200 OK and 206 Partial Content responses.
    - *Sequence:* RED → GREEN → REFACTOR.

---

### Wave 6: Integration Verification & Documentation Closeout

- [x] 18. End-to-End Integration & Multi-Machine Scenario Verification
  - **Objective:** Implement comprehensive end-to-end integration tests simulating a full pipeline run: Pocket API poll -> staging download -> Python ASR & diarization -> WeSpeaker biometric extraction -> taxonomy classification -> TagLibSharp metadata tagging -> folder routing -> template summarization -> GUI state sync -> multi-machine lease expiration and failover.
  - **Exact files / components:**
    - `tests/PocketExport.Core.Tests/E2E/FullPipelineE2ETests.cs`
    - `tests/PocketExport.Core.Tests/E2E/MultiMachineCoordinationE2ETests.cs`
  - **Acceptance criteria:** REQ-1 through REQ-15 verified end-to-end; dual-machine lease handover verified without file conflicts; audio container tags verified.
  - **Dependencies:** Task 6, Task 7, Task 8, Task 9, Task 13, Task 15, Task 16, Task 17.
  - **Required skills:** `test-dev`, `csharp-dev`.
  - **Traced requirement IDs:** REQ-1, REQ-2, REQ-3, REQ-4, REQ-5, REQ-6, REQ-7, REQ-8, REQ-9, REQ-10, REQ-11, REQ-12, REQ-13, REQ-14, REQ-15.
  - **Test contract (test-first):**
    - *Test project / file:* `tests/PocketExport.Core.Tests/E2E/FullPipelineE2ETests.cs`, `tests/PocketExport.Core.Tests/E2E/MultiMachineCoordinationE2ETests.cs`
    - *Test runner:* `dotnet test tests/PocketExport.Core.Tests/PocketExport.Core.Tests.csproj --filter FullyQualifiedName~E2E`
    - *Observable behavior:* Entire pipeline runs from synthetic Pocket response to routed Markdown file; second simulated node observes lease and locks; SQLite projection verified.
    - *Expected RED reason:* Missing end-to-end glue services or test fixtures.
    - *Expected GREEN acceptance:* All multi-stage E2E pipeline and multi-machine tests pass cleanly.
    - *Sequence:* RED → GREEN → REFACTOR.

- [x] 19. Closeout & System Documentation Migration `[no-test]`
  - **Objective:** Verify all requirements `REQ-1` through `REQ-15` against implementation and test evidence, migrate durable architectural facts into canonical docs (`docs/system/architecture.md`, `<component-catalog>`, `<documentation-index>`), update specification index to Delivered, and archive the specification.
  - **Exact files / components:**
    - `docs/specs/pocket-sync/requirements.md`
    - `docs/specs/pocket-sync/design.md`
    - `docs/specs/pocket-sync/tasks.md`
    - `docs/specs/README.md`
    - `docs/catalog.md`
    - `docs/system/architecture.md`
  - **Acceptance criteria:** All 15 requirements verified with passing test evidence; component catalog updated; spec marked Delivered in `docs/specs/README.md`.
  - **Dependencies:** Task 18.
  - **Required skills:** `docs-dev`.
  - **Traced requirement IDs:** None (Closeout task).
  - **Test contract:** `[no-test]`
    - *Replacement validation:* Documentation review and `git diff --check` to verify no lingering drafts or broken markdown links.
