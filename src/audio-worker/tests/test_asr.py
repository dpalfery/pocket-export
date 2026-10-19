"""Contract tests for ASR transcription module."""

from pathlib import Path
import struct
import wave
import pytest

from src.asr import transcribe_audio


def _create_dummy_wav(path: Path, duration_seconds: float = 1.0) -> Path:
    """Generate a minimal valid 16kHz mono PCM WAV file."""
    sample_rate = 16000
    num_samples = int(sample_rate * duration_seconds)
    with wave.open(str(path), "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)
        wf.setframerate(sample_rate)
        wf.writeframes(struct.pack(f"<{num_samples}h", *([0] * num_samples)))
    return path


def test_transcribe_audio_missing_file_raises_file_not_found_error(tmp_path: Path) -> None:
    """transcribe_audio must raise FileNotFoundError when audio file does not exist."""
    missing_file = tmp_path / "nonexistent.wav"
    with pytest.raises(FileNotFoundError):
        transcribe_audio(str(missing_file), model_name="base", device="cpu")


def test_transcribe_audio_valid_audio_returns_expected_structure(tmp_path: Path) -> None:
    """transcribe_audio must return segments, language, and duration_seconds."""
    audio_path = _create_dummy_wav(tmp_path / "sample.wav")
    result = transcribe_audio(str(audio_path), model_name="base", device="cpu")

    assert isinstance(result, dict)
    assert "segments" in result
    assert "language" in result
    assert "duration_seconds" in result

    assert isinstance(result["language"], str)
    assert isinstance(result["duration_seconds"], (int, float))
    assert result["duration_seconds"] >= 0

    assert isinstance(result["segments"], list)
    for seg in result["segments"]:
        assert "start_ms" in seg
        assert "end_ms" in seg
        assert "text" in seg
        assert "confidence" in seg
        assert isinstance(seg["start_ms"], (int, float))
        assert isinstance(seg["end_ms"], (int, float))
        assert isinstance(seg["text"], str)
        assert isinstance(seg["confidence"], (int, float))
        assert seg["start_ms"] <= seg["end_ms"]
