"""Contract tests for speaker biometrics module."""

from pathlib import Path
import struct
import wave
import numpy as np
import pytest

from src.biometrics import extract_embedding


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


def test_extract_embedding_missing_file_raises_file_not_found_error(tmp_path: Path) -> None:
    """extract_embedding must raise FileNotFoundError when audio file does not exist."""
    missing_file = tmp_path / "nonexistent.wav"
    segments = [{"start_ms": 0, "end_ms": 2000}]
    with pytest.raises(FileNotFoundError):
        extract_embedding(str(missing_file), speaker_segments=segments)


def test_extract_embedding_empty_segments_raises_value_error(tmp_path: Path) -> None:
    """extract_embedding must raise ValueError when speaker_segments is empty."""
    audio_path = _create_dummy_wav(tmp_path / "speech.wav", duration_seconds=2.0)
    with pytest.raises(ValueError):
        extract_embedding(str(audio_path), speaker_segments=[])


def test_extract_embedding_valid_segments_returns_normalized_512d_array(tmp_path: Path) -> None:
    """extract_embedding must return a 512-dimensional float numpy array with L2 norm 1.0 +- 1e-5."""
    audio_path = _create_dummy_wav(tmp_path / "speech.wav", duration_seconds=5.0)
    segments = [
        {"start_ms": 0, "end_ms": 2500},
        {"start_ms": 3000, "end_ms": 4800},
    ]
    embedding = extract_embedding(str(audio_path), speaker_segments=segments)

    assert isinstance(embedding, np.ndarray)
    assert embedding.shape == (512,)
    assert np.issubdtype(embedding.dtype, np.floating)

    l2_norm = float(np.linalg.norm(embedding))
    assert abs(l2_norm - 1.0) <= 1e-5
