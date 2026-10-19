"""Contract and integration tests for audio worker HTTP server."""

from pathlib import Path
import struct
from typing import Any
from unittest.mock import patch
import wave
from fastapi.testclient import TestClient

from src.server import app

client = TestClient(app)


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


def test_health_check_when_invoked_returns_ok() -> None:
    """GET /health must return status ok with 200 OK."""
    response = client.get("/health")
    assert response.status_code == 200
    assert response.json() == {"status": "ok"}


def test_transcribe_missing_audio_file_returns_not_found(tmp_path: Path) -> None:
    """POST /transcribe must return HTTP 404 when audio_path does not exist on disk."""
    missing_path = tmp_path / "nonexistent.wav"
    payload = {"audio_path": str(missing_path)}

    response = client.post("/transcribe", json=payload)
    assert response.status_code == 404
    body = response.json()
    assert "detail" in body
    assert "not found" in body["detail"].lower()


def test_transcribe_empty_path_returns_not_found() -> None:
    """POST /transcribe must return HTTP 404 when audio_path is empty."""
    response = client.post("/transcribe", json={"audio_path": ""})
    assert response.status_code == 404


def test_transcribe_valid_audio_file_returns_structured_response(tmp_path: Path) -> None:
    """POST /transcribe with valid audio file must return structured transcription result."""
    audio_path = _create_dummy_wav(tmp_path / "test_audio.wav", duration_seconds=1.0)
    payload = {
        "audio_path": str(audio_path),
        "model_name": "whisper-large-v3-turbo",
        "device": "cpu",
    }

    response = client.post("/transcribe", json=payload)
    assert response.status_code == 200

    data = response.json()
    assert "text" in data
    assert "segments" in data
    assert "speakers" in data
    assert "words" in data
    assert "language" in data
    assert "duration_seconds" in data

    assert isinstance(data["text"], str)
    assert isinstance(data["segments"], list)
    assert isinstance(data["speakers"], list)
    assert isinstance(data["words"], list)
    assert isinstance(data["language"], str)
    assert isinstance(data["duration_seconds"], (int, float))
    assert data["duration_seconds"] > 0

    # Fallback pseudo-segmentation generates at least one speaker turn for duration > 0
    assert len(data["speakers"]) >= 1
    speaker = data["speakers"][0]
    assert "cluster_id" in speaker
    assert "display_name" in speaker
    assert "embedding_vector" in speaker
    assert len(speaker["embedding_vector"]) == 512


def test_transcribe_with_mocked_transcript_attributes_speakers_correctly(tmp_path: Path) -> None:
    """POST /transcribe must attribute speaker clusters to transcript segments and combine full text."""
    audio_path = _create_dummy_wav(tmp_path / "multi_turn.wav", duration_seconds=6.0)

    mock_transcript: dict[str, Any] = {
        "segments": [
            {
                "start_ms": 500,
                "end_ms": 2500,
                "text": "Hello world from Pocket AI",
                "confidence": 0.96,
            },
            {
                "start_ms": 3500,
                "end_ms": 5500,
                "text": "Second speaker turn response",
                "confidence": 0.92,
            },
        ],
        "language": "en",
        "duration_seconds": 6.0,
    }

    with patch("src.server.transcribe_audio", return_value=mock_transcript):
        response = client.post(
            "/transcribe",
            json={"audio_path": str(audio_path)},
        )

    assert response.status_code == 200
    data = response.json()

    assert data["text"] == "Hello world from Pocket AI Second speaker turn response"
    assert len(data["segments"]) == 2

    seg1 = data["segments"][0]
    assert seg1["text"] == "Hello world from Pocket AI"
    assert seg1["speaker"] == "SPEAKER_00"
    assert seg1["start_ms"] == 500
    assert seg1["end_ms"] == 2500

    seg2 = data["segments"][1]
    assert seg2["text"] == "Second speaker turn response"
    assert seg2["speaker"] == "SPEAKER_01"
    assert seg2["start_ms"] == 3500
    assert seg2["end_ms"] == 5500

    # Ensure word alignments were populated from align_transcript
    assert len(data["words"]) > 0
    words = [w["word"] for w in data["words"]]
    assert "Hello" in words
    assert "Pocket" in words


def test_transcribe_passes_custom_model_and_device(tmp_path: Path) -> None:
    """POST /transcribe must forward custom model_name and device to transcribe_audio."""
    audio_path = _create_dummy_wav(tmp_path / "custom_opts.wav", duration_seconds=1.0)

    with patch("src.server.transcribe_audio") as mock_transcribe:
        mock_transcribe.return_value = {
            "segments": [],
            "language": "fr",
            "duration_seconds": 1.0,
        }

        response = client.post(
            "/transcribe",
            json={
                "audio_path": str(audio_path),
                "model_name": "custom-parakeet-model",
                "device": "cuda",
            },
        )

        assert response.status_code == 200
        mock_transcribe.assert_called_once_with(
            str(audio_path),
            model_name="custom-parakeet-model",
            device="cuda",
        )
        assert response.json()["language"] == "fr"
