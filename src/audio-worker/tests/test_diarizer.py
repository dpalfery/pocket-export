"""Contract tests for speaker diarization module."""

from pathlib import Path
import struct
import wave
import pytest

from src.diarizer import diarize_audio


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


def test_diarize_audio_missing_file_raises_file_not_found_error(tmp_path: Path) -> None:
    """diarize_audio must raise FileNotFoundError when audio file does not exist."""
    missing_file = tmp_path / "nonexistent.wav"
    with pytest.raises(FileNotFoundError):
        diarize_audio(str(missing_file))


def test_diarize_audio_valid_audio_returns_turn_dicts(tmp_path: Path) -> None:
    """diarize_audio must return a list of turn dicts with speaker, start_ms, and end_ms."""
    audio_path = _create_dummy_wav(tmp_path / "meeting.wav", duration_seconds=3.0)
    result = diarize_audio(str(audio_path))

    assert isinstance(result, list)
    for turn in result:
        assert isinstance(turn, dict)
        assert "speaker" in turn
        assert "start_ms" in turn
        assert "end_ms" in turn
        assert isinstance(turn["speaker"], str)
        assert isinstance(turn["start_ms"], (int, float))
        assert isinstance(turn["end_ms"], (int, float))
        assert turn["start_ms"] >= 0
        assert turn["start_ms"] <= turn["end_ms"]


def test_diarize_audio_returns_chronologically_ordered_turns(tmp_path: Path) -> None:
    """diarize_audio turns must be sorted monotonically by start_ms."""
    audio_path = _create_dummy_wav(tmp_path / "conversation.wav", duration_seconds=5.0)
    result = diarize_audio(str(audio_path))

    assert isinstance(result, list)
    for i in range(1, len(result)):
        assert result[i]["start_ms"] >= result[i - 1]["start_ms"]
