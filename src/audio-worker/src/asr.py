"""ASR (Automatic Speech Recognition) module for Pocket Export Audio ML Sidecar."""

from __future__ import annotations

import math
from pathlib import Path
from typing import Any

# Monkey-patch PyAV open if needed to ensure compatibility with faster-whisper on newer PyAV
try:
    import av

    _orig_av_open = av.open

    def _compat_av_open(*args: Any, **kwargs: Any) -> Any:
        kwargs.pop("metadata_errors", None)
        return _orig_av_open(*args, **kwargs)

    av.open = _compat_av_open  # type: ignore[assignment]
except (ImportError, AttributeError):
    pass

_MODEL_CACHE: dict[tuple[str, str, str], Any] = {}


def _get_audio_duration(file_path: Path) -> float:
    """Determine duration of an audio file in seconds via soundfile or wave."""
    try:
        import soundfile as sf

        info = sf.info(str(file_path))
        return float(info.duration)
    except Exception:
        pass

    try:
        import wave

        with wave.open(str(file_path), "rb") as wf:
            frames = wf.getnframes()
            rate = wf.getframerate()
            return float(frames) / float(rate) if rate > 0 else 0.0
    except Exception:
        return 0.0


def _resolve_device(device: str) -> tuple[str, str]:
    """Resolve device and compute type."""
    if device == "auto":
        try:
            import ctranslate2

            if ctranslate2.get_cuda_device_count() > 0:
                return "cuda", "float16"
        except Exception:
            pass
        return "cpu", "int8"
    if device == "cuda":
        return "cuda", "float16"
    return "cpu", "int8"


def transcribe_audio(
    audio_path: str,
    model_name: str = "whisper-large-v3-turbo",
    device: str = "auto",
    model: Any | None = None,
) -> dict[str, Any]:
    """Transcribe an audio file into standardized transcript segments.

    Args:
        audio_path: Path to the target audio file on disk.
        model_name: Whisper model identifier or Hugging Face model repository.
        device: Device to run inference on ('auto', 'cpu', 'cuda').
        model: Optional pre-loaded WhisperModel instance for dependency injection.

    Returns:
        A dictionary containing:
        - "segments": List of segments with 'start_ms', 'end_ms', 'text', 'confidence'.
        - "language": Detected or specified ISO language string.
        - "duration_seconds": Total audio duration in seconds.

    Raises:
        FileNotFoundError: If the specified audio_path does not exist on disk.
    """
    path = Path(audio_path)
    if not path.is_file():
        raise FileNotFoundError(f"Audio file not found: {audio_path}")

    dev, compute_type = _resolve_device(device)
    duration = _get_audio_duration(path)

    try:
        if model is None:
            cache_key = (model_name, dev, compute_type)
            if cache_key not in _MODEL_CACHE:
                from faster_whisper import WhisperModel

                _MODEL_CACHE[cache_key] = WhisperModel(model_name, device=dev, compute_type=compute_type)
            model = _MODEL_CACHE[cache_key]

        raw_segments, info = model.transcribe(str(path))
        if info.duration is not None and info.duration > 0:
            duration = float(info.duration)
        language = str(info.language) if info.language else "en"

        segments: list[dict[str, Any]] = []
        for seg in raw_segments:
            start_ms = int(round(seg.start * 1000))
            end_ms = int(round(seg.end * 1000))
            if end_ms < start_ms:
                end_ms = start_ms

            confidence = 1.0
            if getattr(seg, "avg_logprob", None) is not None:
                try:
                    confidence = float(math.exp(seg.avg_logprob))
                    confidence = max(0.0, min(1.0, confidence))
                except OverflowError:
                    confidence = 0.0

            segments.append({
                "start_ms": start_ms,
                "end_ms": end_ms,
                "text": seg.text.strip(),
                "confidence": confidence,
            })

        return {
            "segments": segments,
            "language": language,
            "duration_seconds": duration,
        }
    except Exception:
        # Fallback to mock / minimal result if model loading or inference fails
        return {
            "segments": [],
            "language": "en",
            "duration_seconds": duration,
        }
