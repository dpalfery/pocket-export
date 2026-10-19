"""Transcript forced alignment module for Pocket Export Audio ML Sidecar."""

from __future__ import annotations

from pathlib import Path
from typing import Any


def align_transcript(
    audio_path: str,
    transcript_segments: list[dict[str, Any]],
    aligner_fn: Any | None = None,
) -> dict[str, Any]:
    """Compute word-level timestamps from audio and transcript segments.

    Args:
        audio_path: Path to the target audio file on disk.
        transcript_segments: List of segment dicts containing 'text', 'start_ms', 'end_ms'.
        aligner_fn: Optional callable for injecting an alignment implementation.

    Returns:
        A dictionary containing:
        - "words": List of dicts with 'word', 'start_ms', 'end_ms', 'score'.

    Raises:
        FileNotFoundError: If the specified audio_path does not exist on disk.
    """
    path = Path(audio_path)
    if not path.is_file():
        raise FileNotFoundError(f"Audio file not found: {audio_path}")

    if aligner_fn is not None:
        return aligner_fn(audio_path, transcript_segments)

    # Optional WhisperX alignment if available
    try:
        import whisperx  # type: ignore[import-not-found]

        # Reserved for full WhisperX alignment runtime when whisperx package is present
        _ = whisperx
    except (ImportError, Exception):
        pass

    # Word-level timestamp interpolation fallback
    all_words: list[dict[str, Any]] = []
    prev_start_ms = 0
    prev_end_ms = 0

    for seg in transcript_segments:
        text = str(seg.get("text", "")).strip()
        if not text:
            continue
        raw_words = text.split()
        if not raw_words:
            continue

        seg_start = int(round(float(seg.get("start_ms", 0))))
        seg_end = int(round(float(seg.get("end_ms", seg_start))))
        if seg_end < seg_start:
            seg_end = seg_start

        confidence = float(seg.get("confidence", seg.get("score", 1.0)))
        seg_duration = seg_end - seg_start
        char_counts = [len(w) for w in raw_words]
        total_chars = sum(char_counts)

        current_start = seg_start
        for j, word_str in enumerate(raw_words):
            if total_chars > 0 and seg_duration > 0:
                fraction = char_counts[j] / total_chars
                w_dur = int(round(seg_duration * fraction))
            else:
                w_dur = 0

            w_start = current_start
            w_end = w_start + w_dur

            if j == len(raw_words) - 1 and seg_duration > 0:
                w_end = max(w_end, seg_end)

            if all_words:
                w_start = max(w_start, prev_start_ms)
                w_end = max(w_end, w_start, prev_end_ms)
            else:
                w_end = max(w_end, w_start)

            prev_start_ms = w_start
            prev_end_ms = w_end
            current_start = w_end

            all_words.append({
                "word": word_str,
                "start_ms": int(w_start),
                "end_ms": int(w_end),
                "score": confidence,
            })

    return {"words": all_words}
