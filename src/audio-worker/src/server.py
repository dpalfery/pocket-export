"""Pocket Export Audio ML Sidecar HTTP Server."""

from __future__ import annotations

from pathlib import Path
from typing import Any
from fastapi import FastAPI, HTTPException, Request, status
from fastapi.responses import JSONResponse
from pydantic import BaseModel, Field

try:
    from src.aligner import align_transcript
    from src.asr import transcribe_audio
    from src.biometrics import extract_embedding
    from src.diarizer import diarize_audio
    from src.snippet_selector import select_solo_snippet
except ImportError:
    from aligner import align_transcript
    from asr import transcribe_audio
    from biometrics import extract_embedding
    from diarizer import diarize_audio
    from snippet_selector import select_solo_snippet

app = FastAPI(
    title="Pocket Export Audio ML Sidecar",
    description="Hermetic ML sidecar for transcription, alignment, and diarization.",
    version="0.1.0",
)


@app.exception_handler(FileNotFoundError)
def file_not_found_exception_handler(_: Request, exc: FileNotFoundError) -> JSONResponse:
    """Translate FileNotFoundError to HTTP 404."""
    return JSONResponse(
        status_code=status.HTTP_404_NOT_FOUND,
        content={"detail": str(exc)},
    )


class TranscribeRequest(BaseModel):
    """Request payload for audio transcription and diarization."""

    audio_path: str
    model_name: str = "whisper-large-v3-turbo"
    device: str = "auto"


class SoloSnippet(BaseModel):
    """Non-overlapping solo speech window for speaker voiceprint enrollment."""

    start_ms: int
    end_ms: int


class SpeakerInfo(BaseModel):
    """Enrolled speaker profile with voiceprint embedding and snippet interval."""

    cluster_id: str
    display_name: str
    solo_snippet: SoloSnippet | None = None
    embedding_vector: list[float] = Field(default_factory=list)


class TranscriptSegment(BaseModel):
    """Timed transcription segment attributed to a speaker."""

    start_ms: int
    end_ms: int
    text: str
    confidence: float = 1.0
    speaker: str = "SPEAKER_00"


class WordAlignment(BaseModel):
    """Word-level alignment timestamp."""

    word: str
    start_ms: int
    end_ms: int
    score: float = 1.0


class TranscribeResponse(BaseModel):
    """Structured transcription response payload matching AudioWorkerClient expectations."""

    text: str = ""
    segments: list[TranscriptSegment] = Field(default_factory=list)
    speakers: list[SpeakerInfo] = Field(default_factory=list)
    words: list[WordAlignment] = Field(default_factory=list)
    language: str = "en"
    duration_seconds: float = 0.0


@app.get("/health")
def health_check() -> dict[str, Any]:
    """Minimal health check endpoint."""
    return {"status": "ok"}


@app.post("/transcribe", response_model=TranscribeResponse)
def transcribe(request: TranscribeRequest) -> TranscribeResponse:
    """Execute end-to-end transcription, alignment, diarization, and speaker embedding extraction.

    Args:
        request: TranscribeRequest containing audio_path, optional model_name, and optional device.

    Returns:
        TranscribeResponse containing full text, diarized segments, speaker embeddings, and metadata.

    Raises:
        HTTPException: Status 404 if the requested audio_path does not exist on disk.
    """
    path = Path(request.audio_path)
    if not path.is_file():
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Audio file not found: {request.audio_path}",
        )

    # 1. Transcribe audio
    transcript = transcribe_audio(
        str(path),
        model_name=request.model_name,
        device=request.device,
    )
    raw_segments: list[dict[str, Any]] = transcript.get("segments", [])

    # 2. Align transcript to obtain word-level timestamps
    alignment = align_transcript(str(path), raw_segments)
    words: list[dict[str, Any]] = alignment.get("words", [])

    # 3. Diarize audio into speaker turns
    all_turns = diarize_audio(str(path))

    # 4. Group turns by speaker cluster
    speaker_clusters: dict[str, list[dict[str, Any]]] = {}
    for turn in all_turns:
        speaker_id = str(turn.get("speaker", "SPEAKER_00"))
        if speaker_id not in speaker_clusters:
            speaker_clusters[speaker_id] = []
        speaker_clusters[speaker_id].append(turn)

    # 5. Extract speaker embeddings and solo snippets for each speaker cluster
    speakers: list[SpeakerInfo] = []
    for cluster_id, speaker_segments in speaker_clusters.items():
        embedding = extract_embedding(str(path), speaker_segments)
        solo_snippet_dict = select_solo_snippet(speaker_segments, all_turns)
        solo_snippet = (
            SoloSnippet(
                start_ms=int(solo_snippet_dict["start_ms"]),
                end_ms=int(solo_snippet_dict["end_ms"]),
            )
            if solo_snippet_dict is not None
            else None
        )
        embedding_list = (
            embedding.tolist()
            if hasattr(embedding, "tolist")
            else [float(x) for x in embedding]
        )
        speakers.append(
            SpeakerInfo(
                cluster_id=cluster_id,
                display_name=cluster_id,
                solo_snippet=solo_snippet,
                embedding_vector=embedding_list,
            )
        )

    # 6. Attribute speaker to transcript segments based on maximum temporal overlap
    attributed_segments: list[TranscriptSegment] = []
    for seg in raw_segments:
        s_start = int(seg.get("start_ms", 0))
        s_end = int(seg.get("end_ms", s_start))
        assigned_speaker = seg.get("speaker")

        if not assigned_speaker:
            best_speaker: str | None = None
            max_overlap: int = -1
            for turn in all_turns:
                t_start = int(turn.get("start_ms", 0))
                t_end = int(turn.get("end_ms", t_start))
                overlap = max(0, min(s_end, t_end) - max(s_start, t_start))
                if overlap > max_overlap:
                    max_overlap = overlap
                    best_speaker = turn.get("speaker")

            if best_speaker is not None and max_overlap > 0:
                assigned_speaker = best_speaker
            elif speaker_clusters:
                assigned_speaker = next(iter(speaker_clusters.keys()))
            else:
                assigned_speaker = "SPEAKER_00"

        attributed_segments.append(
            TranscriptSegment(
                start_ms=s_start,
                end_ms=s_end,
                text=str(seg.get("text", "")).strip(),
                confidence=float(seg.get("confidence", 1.0)),
                speaker=str(assigned_speaker),
            )
        )

    full_text = " ".join(seg.text for seg in attributed_segments if seg.text).strip()
    word_alignments = [
        WordAlignment(
            word=str(w.get("word", "")),
            start_ms=int(w.get("start_ms", 0)),
            end_ms=int(w.get("end_ms", 0)),
            score=float(w.get("score", 1.0)),
        )
        for w in words
    ]

    return TranscribeResponse(
        text=full_text,
        segments=attributed_segments,
        speakers=speakers,
        words=word_alignments,
        language=str(transcript.get("language", "en")),
        duration_seconds=float(transcript.get("duration_seconds", 0.0)),
    )
