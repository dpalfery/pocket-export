# Requirements Document

**Phase status:** Approved

## Introduction

The `pocket-sync` feature delivers an end-to-end cross-platform desktop synchronization, transcription, classification, routing, and management pipeline for voice recordings originating from the Pocket AI cloud service (`heypocketai.com`).

The system ingests raw audio files, cloud transcripts, and cloud metadata into a shared OneDrive vault (`VaultRoot`). It executes high-fidelity local re-transcription and forced alignment, diarizes speakers, extracts 512-dimensional biometric speaker embeddings, matches and enrolls speakers against a local voicebank, and classifies recordings against a user-defined taxonomy (`taxonomy.yaml`) using local LLMs (via LM Studio) or zero-shot classifiers. Structured Markdown summaries are generated using prescribed templates and filed into taxonomy-organized folder structures (`routed/`) or quarantined for inspection (`_needs_review/`).

Heavy local ML processing (automatic speech recognition, phoneme alignment, speaker diarization, and biometric embedding extraction) is explicitly designed and assumed to run while plugged into AC power, utilizing the dedicated NVIDIA GeForce RTX 5090 Laptop GPU (24GB VRAM, CUDA 12.8+) on Windows 11 and Apple Silicon unified memory with Metal / MLX acceleration on macOS.

A native cross-platform GUI built with Tauri v2, React, TypeScript, and a self-hosted C# (.NET 9/10) ASP.NET Core Web API allows the user to monitor pipelines, play isolated solo speaker audio snippets, enroll new speakers, preview retroactive speaker match proposals, manually re-tag recordings, and trigger re-transcription or re-summarization on demand.

## User Personas

### Persona 1: Primary User & Technical Architect (Dave — Windows Workstation)
- **Role:** Executive, engineering architect, and primary knowledge worker.
- **Hardware Profile:** High-performance Windows 11 workstation (2025 HP Omen Max 16 with NVIDIA RTX 5090 Laptop GPU 24GB VRAM [Blackwell sm_120, CUDA 12.8+], 64GB RAM, AC power).
- **Workflow & Needs:** Uses the Pocket AI hardware recorder continuously for architectural debates, client briefings, daily standups, and personal audio thoughts. Requires uncompromised local transcript accuracy, completely private processing, automatic taxonomy routing into OneDrive folders, speaker voicebank enrollment, and maximum throughput utilizing the RTX 5090 GPU while connected to AC power.

### Persona 2: Traveling & Mobile Multi-Device User (Dave — macOS Laptop)
- **Role:** Mobile professional operating away from the primary workstation.
- **Hardware Profile:** Apple Silicon MacBook (M-series, unified memory, Metal / MLX acceleration, AC power).
- **Workflow & Needs:** Needs seamless access to the same OneDrive vault from macOS to browse transcripts, audition speaker snippets, tag newly recognized speakers, and manually adjust taxonomy classifications. Requires multi-machine sync safety without binary database corruption, conflict-free distributed leases, and on-demand ML processing via Apple Silicon Metal/MLX while connected to AC power.

### Persona 3: Knowledge Collaborator & Downstream Reader
- **Role:** Client, colleague, or team stakeholder reviewing generated meeting notes.
- **Workflow & Needs:** Consumes clean, highly structured Markdown summaries filed in predictable directory structures (`routed/Work/{Customer}/{Project}/`), containing consistent meeting sections, action items, decisions, and speaker attributions adhering to prescribed templates.

## Scope Boundaries

### In-Scope
1. **Pocket AI Synchronization:** Automated polling and pulling of recordings, pre-signed audio URLs, raw audio streams, cloud transcripts, speaker labels, and summaries from `https://public.heypocketai.com/api/v1`.
2. **OneDrive Multi-Machine Vault Storage:** Platform-agnostic relative POSIX path resolution inside a shared `VaultRoot` directory.
3. **Atomic Manifest Ledger & SQLite Projection:** Canonical state stored in atomic file manifests (`VaultRoot/.ledger/`) with isolated local SQLite query indexes on Windows (`%LOCALAPPDATA%`) and macOS (`~/Library/Application Support`).
4. **Distributed Lease Coordination:** Distributed role and work leases (`.ledger/leases/`) arbitrating between primary background poller/worker (Windows) and interactive client/worker (macOS).
5. **Local ASR & Forced Alignment:** Local transcription using `faster-whisper` (`whisper-large-v3-turbo` / `whisper-large-v3`) or `nvidia/parakeet-tdt-0.6b-v2` on Windows RTX 5090 CUDA 12.8+, `mlx-whisper` on macOS Metal, and `WhisperX` Wav2Vec2 phoneme forced word alignment while on AC power.
6. **Speaker Diarization & Biometric Embedding:** Speaker segmentation via `pyannote/speaker-diarization-community-1` (or 3.1), 512-d L2-normalized embedding extraction via `WeSpeaker CAM++` (or `TitaNet`), and cleanest 8-10s solo audio snippet window isolation.
7. **Speaker Voicebank & Disambiguation:** Cosine similarity voicebank matching, manual enrollment, and guided retroactive speaker matching workflow in the GUI.
8. **Taxonomy Classification:** Classification against `taxonomy.yaml` (`Domain`, `Customer`, `Project`, `Initiative`, confidence score, template ID) via LM Studio (`http://localhost:1234/v1`) or local zero-shot classifier.
9. **Container Tagging & Staging Preservation:** Audio stream preservation in `staging/` with non-destructive MP3/M4A metadata tagging via `TagLibSharp`.
10. **Folder Routing & Template Summarization:** Rule-based routing to `routed/` or `_needs_review/`, followed by LLM Markdown summary generation using prescribed templates.
11. **Desktop GUI:** Tauri v2 + React + C# management interface for recording dashboards, snippet playback, speaker enrollment, retroactive match approval, and manual re-tagging.
12. **Hardware Acceleration & Supervision:** Dedicated NVIDIA RTX 5090 CUDA 12.8 acceleration on Windows and Apple Silicon Metal/MLX on macOS under AC power, with robust child-process lifecycle supervision.

### Out-of-Scope
1. **Direct Audio Recording:** Capturing live audio inputs directly inside the desktop GUI or background service; all recordings originate from Pocket AI hardware/cloud.
2. **Real-Time Streaming Transcription:** Live streaming transcription during active Pocket recording sessions.
3. **Write-Back to Pocket Cloud:** Uploading local transcripts, modified tags, or audio edits back to the Pocket AI REST API.
4. **Multi-Tenant Server Hosting:** Centralized cloud multi-tenant deployment; the system is designed for single-user multi-workstation operation over shared personal/enterprise OneDrive storage.
5. **Silent Historical Auto-Move:** Silently moving historical transcripts or summaries without explicit user approval when a retroactive speaker match is found.
6. **Battery/NPU Offload Modes:** Execution on low-power NPU engines (e.g. OpenVINO) or low-battery throttling heuristics; all heavy ML processing is scoped for AC power.

## Requirements

### Requirement 1: Pocket AI Cloud Recording & Metadata Ingestion
**User Story:** As a user, I want the system to continuously poll and ingest my latest Pocket AI voice recordings, cloud transcripts, and cloud metadata, so that my audio and service records are preserved locally without manual exports.

#### Acceptance Criteria
1.1. WHEN the background sync scheduler triggers a sync cycle, the C# Orchestrator SHALL query `GET https://public.heypocketai.com/api/v1/public/recordings` using the configured API bearer token.
1.2. WHEN a recording item in the API response is not present in the local database or ledger, the C# Orchestrator SHALL request a pre-signed audio download URL via `GET https://public.heypocketai.com/api/v1/public/recordings/{id}/audio-url`.
1.3. WHEN fetching recording details, the C# Orchestrator SHALL issue `GET /public/recordings/{id}?include_transcript=true&include_summarizations=true` to capture cloud transcripts, speaker maps (`recording.speakers`), and cloud-generated summaries.
1.4. WHEN downloading raw audio via the pre-signed URL, the C# Orchestrator SHALL stream the payload to `VaultRoot/staging/{id}.{ext}` and verify byte integrity against the HTTP content-length header.
1.5. WHEN cloud metadata is fetched, the C# Orchestrator SHALL serialize the complete response verbatim to `VaultRoot/staging/{id}.pocket.json`.
1.6. IF the Pocket API returns an HTTP 429 rate limit or 5xx server error, THEN the C# Orchestrator SHALL apply exponential backoff with jitter and log a warning diagnostic without failing existing staged files.
1.7. IF a downloaded audio file is corrupted or truncated during download, THEN the C# Orchestrator SHALL discard the partial file, retain the error state in the local database, and retry on the subsequent sync cycle.

### Requirement 2: Multi-Machine Shared OneDrive Vault Storage & POSIX Path Portability
**User Story:** As a multi-device user, I want all recording assets, state manifests, and routed notes stored within a shared OneDrive directory using platform-agnostic relative paths, so that both my Windows workstation and macOS laptop access identical files without path mismatches or sync corruption.

#### Acceptance Criteria
2.1. The System SHALL persist all recordings, manifests, and routed outputs within a configurable root directory designated as `VaultRoot`.
2.2. The System SHALL format and store all intra-vault file references as POSIX relative paths originating from `VaultRoot` (e.g., `staging/{id}.m4a`, `routed/{Domain}/{Customer}/{filename}.md`).
2.3. The System SHALL prohibit the persistence of absolute host filesystem paths (including Windows drive letters `C:\` and macOS UNIX paths `/Users/...`) in all shared ledger files and database tables.
2.4. WHEN resolving a POSIX relative path for local I/O operations, the System SHALL combine the host's configured `VaultRoot` with the relative path using the host OS path separator.
2.5. WHILE operating on macOS or Windows, the System SHALL preserve filename case-sensitivity compatibility and sanitize illegal characters (`:`, `*`, `?`, `"`, `<`, `>`, `|`) across both filesystems.

### Requirement 3: Cross-Machine Atomic State Ledger & Conflict-Free Projection
**User Story:** As a multi-machine user, I want canonical system state recorded as atomic JSON and YAML ledger files in OneDrive rather than a shared binary database, so that OneDrive never creates sync conflict files and each machine maintains a fast local SQLite query index.

#### Acceptance Criteria
3.1. The System SHALL store canonical recording state records as individual JSON documents at `VaultRoot/.ledger/recordings/{id}.json`.
3.2. The System SHALL store enrolled speaker biometric profiles and metadata as discrete records at `VaultRoot/.ledger/speakers/{id}.json`.
3.3. The System SHALL store the active taxonomy rules and hierarchy at `VaultRoot/.ledger/taxonomy.yaml`.
3.4. The System SHALL store summarization templates as individual Markdown documents at `VaultRoot/.ledger/templates/{template_id}.md`.
3.5. WHEN mutating any state record in `.ledger/`, the System SHALL execute an atomic write pattern (writing to a temporary file in the same directory and replacing the target file atomically).
3.6. The System SHALL maintain an isolated, machine-local SQLite database outside `VaultRoot` (`%LOCALAPPDATA%/PocketExport/index.db` on Windows, `~/Library/Application Support/PocketExport/index.db` on macOS) as a query projection.
3.7. WHEN the desktop application or background service initializes, the System SHALL scan `VaultRoot/.ledger/` and synchronize the local SQLite projection with all disk manifests.
3.8. WHILE the service is running, WHEN `FileSystemWatcher` detects external file creations, modifications, or deletions in `VaultRoot/.ledger/`, the System SHALL incrementally update the local SQLite index within 500 milliseconds.
3.9. IF a ledger JSON file is locked by OneDrive synchronization during a write attempt, THEN the System SHALL retry the atomic replace using linear backoff over 5 seconds before reporting a persistence error.

### Requirement 4: Distributed Sync Coordination & Role-Based Work Leases
**User Story:** As a multi-machine user, I want my Windows workstation and Mac laptop to negotiate distinct operational roles via a distributed lease in the ledger, so that the powerful Windows GPU machine handles heavy background processing while the Mac functions as an interactive client without processing collisions.

#### Acceptance Criteria
4.1. The System SHALL manage distributed worker roles using ephemeral lease manifests written to `VaultRoot/.ledger/leases/{role}.json`.
4.2. The System SHALL support two primary node roles: `PrimaryPollerWorker` (responsible for Pocket API polling, audio download, and GPU ML processing) and `SecondaryClient` (responsible for UI browsing, snippet playback, manual tagging, and on-demand jobs).
4.3. WHEN a node boots with the `PrimaryPollerWorker` role enabled, the node SHALL attempt to acquire the primary lease by atomically writing `VaultRoot/.ledger/leases/primary_poller.json` with its unique machine identifier and a 60-second expiration timestamp.
4.4. WHILE holding an active lease, the primary node SHALL renew its lease manifest every 20 seconds by updating the expiration timestamp.
4.5. IF a primary node encounters an unhandled crash or network partition, THEN competing nodes SHALL consider the primary lease expired after 60 seconds and allow an authorized standby node to claim the lease.
4.6. WHILE a node holds the `SecondaryClient` role, the node SHALL NOT initiate automated periodic polling of the Pocket API or automated batch ML transcription pipelines.
4.7. WHERE a user on a `SecondaryClient` node explicitly triggers an on-demand re-transcription or re-summarization, the secondary node SHALL claim an item-specific work lock at `VaultRoot/.ledger/locks/{recording_id}.lock` prior to execution and release it upon completion.
4.8. IF an item-specific work lock is currently held by another machine, THEN the System SHALL reject concurrent execution on that recording and display the active lock holder in the GUI.

### Requirement 5: Local High-Fidelity Audio Re-Transcription & Word-Level Alignment
**User Story:** As a user, I want the system to locally re-transcribe raw audio using state-of-the-art ASR models and phoneme alignment, so that transcripts achieve near-perfect accuracy with exact word timestamps without relying on cloud privacy compromises.

#### Acceptance Criteria
5.1. WHEN a new recording is staged in `staging/{id}.{ext}` and claimed for processing, the C# Orchestrator SHALL dispatch an ASR task to the Python `audio-worker` sidecar.
5.2. WHILE running on Windows with an NVIDIA GPU on AC power, the Python worker SHALL execute ASR using `faster-whisper` (`whisper-large-v3-turbo` or `whisper-large-v3`) or `nvidia/parakeet-tdt-0.6b-v2` utilizing CUDA 12.8+ acceleration with FP16 precision.
5.3. WHILE running on macOS Apple Silicon on AC power, the Python worker SHALL execute ASR using `mlx-whisper` utilizing Metal GPU/ANE acceleration with FP16/BF16 precision.
5.4. WHEN ASR text generation completes, the Python worker SHALL execute `WhisperX` Wav2Vec2 phoneme forced alignment against the audio stream to compute exact start and end timestamps for every recognized word.
5.5. The Python worker SHALL output a standardized JSON transcript payload containing utterance segments, word tokens, word-level start/end millisecond timestamps, and model confidence scores.
5.6. IF the Python audio worker process crashes or encounters an Out-Of-Memory (OOM) exception during transcription, THEN the C# Orchestrator SHALL capture the stderr diagnostics, restart the worker process, mark the recording as `TranscriptionFailed`, and notify the orchestrator pipeline.

### Requirement 6: Speaker Diarization, Biometric Embedding, & Solo Snippet Extraction
**User Story:** As a user, I want audio diarized into distinct speaker turns with 512-dimensional biometric voiceprints and clean solo audio snippets extracted for each speaker, so that speakers are precisely identified and auditionable in the UI.

#### Acceptance Criteria
6.1. WHEN word-aligned transcription completes, the Python worker SHALL execute speaker diarization using `pyannote/speaker-diarization-community-1` (or `pyannote 3.1`) to segment the audio into discrete speaker turns (`SPEAKER_00`, `SPEAKER_01`, ...).
6.2. For each identified speaker turn cluster, the Python worker SHALL extract a 512-dimensional L2-normalized biometric speaker embedding using `WeSpeaker CAM++` (or `TitaNet`).
6.3. For each identified speaker cluster, the Python worker SHALL evaluate all single-speaker intervals and identify the cleanest continuous 8-to-10-second solo speech window (`[start_ms, end_ms]`) exhibiting minimal background noise and zero speaker overlap.
6.4. IF a speaker speaks for less than 8 total seconds across the entire recording, THEN the Python worker SHALL select the longest available continuous solo speech segment for that speaker.
6.5. The Python worker SHALL map word-level transcript tokens to their corresponding diarized speaker labels based on temporal timestamp intersection.
6.6. The Python worker SHALL package speaker embeddings (512 float values), snippet bounds (`start_ms`, `end_ms`), and diarized utterance segments into the processing result returned to C#.

### Requirement 7: Biometric Speaker Voicebank Matching, Enrollment, & Disambiguation
**User Story:** As a user, I want extracted speaker embeddings matched against my enrolled voicebank and supplemented with Pocket cloud speaker hints, so that recurring participants are automatically recognized by name.

#### Acceptance Criteria
7.1. The C# Orchestrator SHALL calculate cosine similarity between each extracted 512-d speaker embedding and all enrolled speaker profiles in the local SQLite voicebank table.
7.2. WHEN the highest cosine similarity score for a speaker cluster is greater than or equal to the configurable match threshold (default: `0.75`), the C# Orchestrator SHALL associate the speaker cluster with the matched enrolled speaker identity.
7.3. IF the highest cosine similarity score is below the match threshold, THEN the C# Orchestrator SHALL designate the speaker as `Unidentified` (`SPEAKER_XX`) and preserve its embedding and solo snippet bounds for manual enrollment.
7.4. WHERE Pocket cloud metadata (`staging/{id}.pocket.json`) contains speaker name labels (`recording.speakers`), the C# Orchestrator SHALL utilize these names as auxiliary routing hints and suggested candidate labels in the voicebank matching pipeline.
7.5. IF a recording contains multiple candidate speakers with borderline cosine similarity (between `0.70` and `0.75`), THEN the System SHALL flag the speaker attribution as `NeedsDisambiguation` in the recording manifest.
7.6. WHEN a user enrolls a speaker in the GUI, the System SHALL persist an atomic profile to `VaultRoot/.ledger/speakers/{speaker_id}.json` containing speaker display name, affiliated organization/customer, enrollment timestamp, sample snippet reference, and the 512-d biometric embedding vector.

### Requirement 8: Taxonomy Classification & Metadata Confidence Scoring
**User Story:** As a user, I want the system to classify transcripts against my structured taxonomy rules using a local decision model or LLM, so that recordings are automatically categorized into Domain, Customer, Project, and Initiative with an explicit confidence score.

#### Acceptance Criteria
8.1. The C# Orchestrator SHALL load taxonomy definitions from `VaultRoot/.ledger/taxonomy.yaml`, comprising valid `Domain` values (`Work`, `Personal`), and under `Work`: hierarchies of `Customer`, `Project`, and `Initiative`.
8.2. WHEN a recording's speaker-attributed transcript is ready, the C# Orchestrator SHALL invoke the local classification engine (LM Studio structured JSON completion via `http://localhost:1234/v1` or local zero-shot classifier `GLiNER`).
8.3. The classification engine SHALL accept the transcript text, enrolled speaker names, speaker organizations, and Pocket cloud summary as input context.
8.4. The classification engine SHALL generate a strictly validated JSON response containing: `domain` ("Work" | "Personal"), `customer` (string or null), `project` (string or null), `initiative` (string or null), `confidence` (float between 0.0 and 1.0), `rationale` (string), and `template_id` (string).
8.5. IF the local LM Studio service is unreachable or returns a malformed response, THEN the C# Orchestrator SHALL retry with backoff, and if persistent, assign a classification status of `ClassificationPending` without terminating the sync pipeline.
8.6. WHEN classification succeeds, the C# Orchestrator SHALL persist the classification results into the recording manifest `VaultRoot/.ledger/recordings/{id}.json` and update the local SQLite index.

### Requirement 9: Audio Container Metadata Tagging & Staging Preservation
**User Story:** As a user, I want basic taxonomy tags written directly into the audio file container headers without altering the raw audio streams or moving the file from staging, so that Windows Explorer and macOS Finder display accurate track metadata.

#### Acceptance Criteria
9.1. The System SHALL retain all downloaded raw audio files in `VaultRoot/staging/{id}.{ext}` throughout the lifecycle of the recording.
9.2. WHEN taxonomy classification is determined or manually updated, the C# Orchestrator SHALL write container metadata tags to the audio file in `staging/` using `TagLibSharp`.
9.3. The C# Orchestrator SHALL populate standard container tags: `Title` (Pocket title or generated summary title), `Artist` (identified speaker names), `Album` (Customer / Project hierarchy), `Year` (recording year), and `Comment` (POSIX path to routed transcript).
9.4. IF writing metadata tags via `TagLibSharp` fails due to file locks or unsupported audio container codecs, THEN the System SHALL log a non-fatal warning and proceed with transcript routing and summarization without corrupting the audio file.
9.5. The System SHALL verify that modifying container tags preserves audio stream checksums and does not alter audio playability.

### Requirement 10: Vault Folder Routing & Needs-Review Quarantining
**User Story:** As a user, I want speaker-attributed transcripts routed into an organized folder tree based on classification confidence, so that high-confidence files are neatly filed while ambiguous files are quarantined for review.

#### Acceptance Criteria
10.1. WHEN classification confidence is greater than or equal to the configurable threshold (default: `0.75`), the C# Orchestrator SHALL route the formatted transcript to `VaultRoot/routed/{Domain}/{Customer}/{Project}/{YYYY-MM-DD}_{id}_{slug}.md`.
10.2. IF classification confidence is below the threshold, THEN the C# Orchestrator SHALL route the formatted transcript to `VaultRoot/_needs_review/{YYYY-MM-DD}_{id}_{slug}.md`.
10.3. IF a domain is classified as `Personal`, THEN the C# Orchestrator SHALL route the formatted transcript to `VaultRoot/routed/Personal/{Category}/{YYYY-MM-DD}_{id}_{slug}.md`.
10.4. WHEN constructing the destination folder path, the C# Orchestrator SHALL create intermediate directories if they do not already exist.
10.5. IF a destination file with the target filename already exists, THEN the C# Orchestrator SHALL append a monotonic collision suffix (`_{n}`) to prevent accidental overwrites.
10.6. The C# Orchestrator SHALL record the final routed relative POSIX path in `VaultRoot/.ledger/recordings/{id}.json` and the local SQLite database.

### Requirement 11: Local LLM Prescribed Markdown Summarization
**User Story:** As a user, I want structured Markdown summaries generated via local LLM using templates prescribed by the taxonomy, so that my meeting notes and voice memos have consistent, actionable formatting.

#### Acceptance Criteria
11.1. WHEN a recording transcript is placed in its routed destination, the C# Orchestrator SHALL load the template Markdown file specified by `template_id` from `VaultRoot/.ledger/templates/{template_id}.md`.
11.2. IF the specified `template_id` does not exist in `.ledger/templates/`, THEN the C# Orchestrator SHALL fall back to `VaultRoot/.ledger/templates/default.md`.
11.3. The C# Orchestrator SHALL construct an LLM prompt incorporating the template instructions, speaker-attributed transcript, speaker biographies/affiliations, and recording metadata.
11.4. The C# Orchestrator SHALL submit the prompt to the local LM Studio instance (`http://localhost:1234/v1/chat/completions`) using the configured model (e.g., `Qwen 2.5 32B-Instruct`).
11.5. The C# Orchestrator SHALL write the generated Markdown summary to the routed folder alongside the transcript, either embedded as frontmatter/sections within `{YYYY-MM-DD}_{id}_{slug}.md` or as a companion `{YYYY-MM-DD}_{id}_{slug}.summary.md` as configured in system preferences.
11.6. IF LM Studio is unavailable or encounters an error during summarization, THEN the C# Orchestrator SHALL mark summarization status as `SummarizationPending` and schedule a retry without invalidating the routed transcript.

### Requirement 12: Cross-Platform Desktop GUI Dashboard & Recording Inspection
**User Story:** As a desktop user, I want an intuitive Tauri v2 + React dashboard to inspect all recordings, filter by taxonomy, view speaker attributions, and monitor processing pipelines across Windows and Mac.

#### Acceptance Criteria
12.1. The Tauri v2 application SHALL present a responsive React dashboard displaying recordings from the local SQLite index with columns for date, title, duration, domain, customer, project, speakers, status, and routed location.
12.2. The GUI SHALL support real-time text search and multi-facet filtering by Domain (`Work`/`Personal`), Customer, Project, Speaker, and Status (`Completed`, `NeedsReview`, `Processing`, `Failed`).
12.3. WHEN a user selects a recording row, the GUI SHALL display a detail pane containing the full speaker-attributed transcript, extracted summary, confidence scores, and raw Pocket metadata.
12.4. WHILE background sync or ML transcription is active, the GUI SHALL display live pipeline progress indicators reflecting current stages (Downloading, Transcribing, Diarizing, Classifying, Summarizing).
12.5. The GUI SHALL render identically on Windows 11 and macOS using native platform styling cues while maintaining consistent layout and shortcut bindings.

### Requirement 13: Desktop GUI Speaker Management, Snippet Playback, & Enrollment
**User Story:** As a user, I want to listen to isolated 8-to-10-second solo audio snippets of unidentified speakers in the GUI and enroll them into the voicebank with a single click, so that future recordings recognize them automatically.

#### Acceptance Criteria
13.1. WHEN viewing a recording with unidentified or recognized speakers in the GUI, the GUI SHALL display an audition button beside each speaker cluster.
13.2. WHEN the user clicks the audition button, the Tauri frontend SHALL request an audio stream slice for `[start_ms, end_ms]` from the C# Web API.
13.3. The C# Web API SHALL serve the requested audio slice via an HTTP byte-range audio endpoint (`GET /api/recordings/{id}/speakers/{speaker_index}/snippet`) without re-encoding the entire source file.
13.4. WHEN the user enters a display name and optional Organization/Customer for an unidentified speaker and clicks "Enroll", the GUI SHALL dispatch an enrollment command to the C# API.
13.5. The C# API SHALL create a new speaker record in `VaultRoot/.ledger/speakers/{new_id}.json` storing the name, organization, and the 512-d embedding vector extracted from that speaker cluster.
13.6. The C# API SHALL update the current recording's transcript and ledger manifest to reflect the newly enrolled speaker name.

### Requirement 14: Desktop GUI Retroactive Speaker Matching & Guided Batch Re-Routing
**User Story:** As a user, I want the system to detect past recordings matching a newly enrolled speaker and propose them in the GUI for one-click re-routing, so that my historical vault stays organized without dangerous unprompted folder moves.

#### Acceptance Criteria
14.1. WHEN a new speaker is enrolled or an existing speaker's voiceprint is updated, the C# Orchestrator SHALL scan all historical recordings containing unresolved speakers in the local SQLite index.
14.2. For each historical unresolved speaker cluster with cosine similarity greater than or equal to the match threshold (`0.75`), the C# Orchestrator SHALL generate a proposed match record in the database.
14.3. The System SHALL NOT automatically move files or modify historical transcripts in the background without explicit user confirmation.
14.4. WHEN proposed matches exist, the GUI SHALL display a prominent notification and a dedicated "Review Retroactive Matches" modal dialog.
14.5. The modal dialog SHALL list each matching historical recording, match confidence percentage, snippet playback button, current folder location, and projected new destination folder path.
14.6. WHEN the user clicks "Approve Selected Matches", the C# Orchestrator SHALL update speaker identities in the respective recording manifests, atomically move routed transcripts to their new taxonomy folders, and trigger template re-summarization.
14.7. IF the user rejects a proposed match, THEN the System SHALL record the rejection in the local database to suppress future prompts for that specific recording-speaker pairing.

### Requirement 15: Desktop GUI Manual Re-Tagging, Re-Transcription, & Re-Summarization
**User Story:** As a user, I want the ability to manually adjust taxonomy tags, move folders, re-run transcription with alternative models, or re-generate summaries, so that I maintain complete control over my vault organization.

#### Acceptance Criteria
15.1. WHEN a user edits taxonomy fields (`Domain`, `Customer`, `Project`, `Initiative`) in the recording detail view and clicks "Save & Move", the C# Orchestrator SHALL atomically move the transcript and summary files to the corresponding target directory in `VaultRoot/routed/`.
15.2. WHEN files are moved due to manual re-tagging, the C# Orchestrator SHALL update the recording manifest in `.ledger/recordings/{id}.json`, refresh the local SQLite index, and update audio container tags in `staging/{id}.{ext}` via `TagLibSharp`.
15.3. WHERE a user changes taxonomy tags that prescribe a different `template_id`, the C# Orchestrator SHALL automatically trigger re-summarization using the new template.
15.4. WHEN a user clicks "Re-Transcribe" on a recording, the GUI SHALL present an options dialog permitting selection of ASR model (`whisper-large-v3-turbo`, `whisper-large-v3`, `parakeet-tdt-0.6b-v2`) and beam size.
15.5. WHEN the user confirms re-transcription, the C# Orchestrator SHALL dispatch a fresh ASR job to the Python worker using the original raw audio in `staging/{id}.{ext}` and overwrite the transcript upon successful completion.
15.6. WHEN a user clicks "Re-Summarize", the C# Orchestrator SHALL prompt LM Studio to regenerate the summary using the currently assigned template without re-running transcription or diarization.
