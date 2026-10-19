"""Contract tests for transcript alignment module."""

from pathlib import Path
import struct
import wave
import pytest

from src.aligner import align_transcript


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


def test_align_transcript_missing_file_raises_file_not_found_error(tmp_path: Path) -> None:
    """align_transcript must raise FileNotFoundError when audio file does not exist."""
    missing_file = tmp_path / "nonexistent.wav"
    segments = [{"start_ms": 0, "end_ms": 500, "text": "hello"}]
    with pytest.raises(FileNotFoundError):
        align_transcript(str(missing_file), transcript_segments=segments)


def test_align_transcript_valid_segments_returns_monotonic_word_alignments(tmp_path: Path) -> None:
    """align_transcript must return word alignments with monotonic start_ms/end_ms."""
    audio_path = _create_dummy_wav(tmp_path / "sample.wav")
    segments = [
        {"start_ms": 0, "end_ms": 500, "text": "hello world", "confidence": 0.95}
    ]
    result = align_transcript(str(audio_path), transcript_segments=segments)

    assert isinstance(result, dict)
    assert "words" in result
    assert isinstance(result["words"], list)

    for i, word_entry in enumerate(result["words"]):
        assert "start_ms" in word_entry
        assert "end_ms" in word_entry
        assert "word" in word_entry
        assert "score" in word_entry
        assert isinstance(word_entry["word"], str)
        assert isinstance(word_entry["score"], (int, float))
        assert isinstance(word_entry["start_ms"], (int, float))
        assert isinstance(word_entry["end_ms"], (int, float))
        assert word_entry["start_ms"] <= word_entry["end_ms"]
        if i > 0:
            assert word_entry["start_ms"] >= result["words"][i - 1]["start_ms"]
            assert word_entry["end_ms"] >= result["words"][i - 1]["end_ms"]
