# Pocket Export Architecture

## Overview
`pocket-export` synchronizes audio recordings, cloud transcripts, and summaries from the Pocket AI API (`https://public.heypocketai.com/api/v1`) into a shared OneDrive VaultRoot, performs high-accuracy local re-transcription, speaker diarization, and biometric speaker identification, classifies recordings against a user-defined taxonomy (`Work/Personal`, `Customer`, `Project`, `Initiative`), routes transcripts into the taxonomy folder hierarchy, generates formatted summaries using prescribed templates via local LLMs (LM Studio), and provides a cross-platform Tauri v2 + React + C# GUI for managing recordings, playing speaker snippets, enrolling speakers, and re-tagging/re-transcribing.

## Components
- **C# (.NET 9/10) Backend Service & API (`PocketExport.Api` / `PocketExport.Core`):** Polls Pocket REST API, manages OneDrive `.ledger/` state & local SQLite projection, serves audio snippet byte-range streams, orchestrates Python ML sidecar and LM Studio LLM calls, and manages file routing.
- **Python Audio ML Worker (`audio-worker` via `uv`):** Runs local ASR (`WhisperX` / `faster-whisper` `large-v3-turbo` / `Parakeet-TDT` on RTX 5090 CUDA 12.8; `mlx-whisper` on macOS Apple Silicon), Wav2Vec2 forced word alignment, `pyannote/speaker-diarization-community-1` diarization, and `WeSpeaker CAM++` 512-d speaker embeddings.
- **Tauri v2 + React/TypeScript Desktop UI (`ui` / `src-tauri`):** Cross-platform GUI for browsing processed recordings, playing solo speaker audio snippets to tag/enroll unknown speakers, approving retroactive voicebank matches, moving/re-tagging recordings, and triggering re-transcription/re-summarization.
