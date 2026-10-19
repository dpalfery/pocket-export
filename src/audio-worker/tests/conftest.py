"""Test configuration and fixtures for audio-worker tests."""

import sys
from pathlib import Path

# Ensure audio-worker directory is on sys.path so `src` package is importable
AUDIO_WORKER_DIR = Path(__file__).resolve().parent.parent
if str(AUDIO_WORKER_DIR) not in sys.path:
    sys.path.insert(0, str(AUDIO_WORKER_DIR))
