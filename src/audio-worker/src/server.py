"""Pocket Export Audio ML Sidecar HTTP Server."""

from typing import Any
from fastapi import FastAPI

app = FastAPI(
    title="Pocket Export Audio ML Sidecar",
    description="Hermetic ML sidecar for transcription, alignment, and diarization.",
    version="0.1.0",
)


@app.get("/health")
def health_check() -> dict[str, Any]:
    """Minimal health check endpoint."""
    return {"status": "ok"}
