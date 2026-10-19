"""Validation of client-supplied audio paths to prevent path injection."""

from __future__ import annotations

import os
import tempfile
from pathlib import Path

ALLOWED_ROOTS_ENV = "POCKET_AUDIO_ROOTS"


def _allowed_roots() -> list[str]:
    """Return the resolved directories audio files may be read from.

    Configured via ``POCKET_AUDIO_ROOTS`` (``os.pathsep``-separated). When unset,
    defaults to the user's home directory and the system temp directory.
    """
    configured = os.environ.get(ALLOWED_ROOTS_ENV, "")
    raw = [r for r in configured.split(os.pathsep) if r] or [
        str(Path.home()),
        tempfile.gettempdir(),
    ]
    return [os.path.realpath(r) for r in raw]


def resolve_audio_path(audio_path: str) -> Path:
    """Resolve ``audio_path`` and ensure it lies inside an allowed root.

    Args:
        audio_path: Untrusted path string supplied by a caller.

    Returns:
        The canonical absolute path as a ``Path``.

    Raises:
        FileNotFoundError: If the path is malformed, outside every allowed root,
            or is not an existing regular file.
    """
    if not audio_path or "\x00" in audio_path:
        raise FileNotFoundError(f"Audio file not found: {audio_path!r}")

    resolved = os.path.realpath(audio_path)
    for root in _allowed_roots():
        if resolved.startswith(root.rstrip(os.sep) + os.sep) and os.path.isfile(resolved):
            return Path(resolved)

    raise FileNotFoundError(f"Audio file not found: {audio_path}")
