"""Contract tests for speaker solo snippet selection module."""

from typing import Any
import pytest

from src.snippet_selector import select_solo_snippet


def test_select_solo_snippet_long_continuous_speech_returns_window_between_8_and_10_seconds() -> None:
    """select_solo_snippet must isolate an 8000ms-10000ms window from long continuous solo speech."""
    speaker_turns: list[dict[str, Any]] = [
        {"speaker": "SPEAKER_00", "start_ms": 0, "end_ms": 15000}
    ]
    all_speaker_turns = list(speaker_turns)

    result = select_solo_snippet(speaker_turns, all_speaker_turns)

    assert isinstance(result, dict)
    assert "start_ms" in result
    assert "end_ms" in result
    assert isinstance(result["start_ms"], (int, float))
    assert isinstance(result["end_ms"], (int, float))
    assert result["start_ms"] >= 0
    assert result["end_ms"] <= 15000

    duration = result["end_ms"] - result["start_ms"]
    assert 8000 <= duration <= 10000


def test_select_solo_snippet_avoids_overlapping_cross_talk_speech() -> None:
    """select_solo_snippet must avoid overlapping cross-talk regions when selecting solo speech."""
    speaker_turns: list[dict[str, Any]] = [
        {"speaker": "SPEAKER_00", "start_ms": 0, "end_ms": 20000}
    ]
    all_speaker_turns: list[dict[str, Any]] = [
        {"speaker": "SPEAKER_00", "start_ms": 0, "end_ms": 20000},
        {"speaker": "SPEAKER_01", "start_ms": 1000, "end_ms": 6000},
    ]

    result = select_solo_snippet(speaker_turns, all_speaker_turns)

    assert isinstance(result, dict)
    assert "start_ms" in result
    assert "end_ms" in result

    # Must be chosen from the clean solo window [6000, 20000]
    assert result["start_ms"] >= 6000
    assert result["end_ms"] <= 20000

    # Ensure no overlap with SPEAKER_01 turn [1000, 6000]
    overlap_start = max(result["start_ms"], 1000)
    overlap_end = min(result["end_ms"], 6000)
    assert overlap_start >= overlap_end

    duration = result["end_ms"] - result["start_ms"]
    assert 8000 <= duration <= 10000


def test_select_solo_snippet_shorter_than_8_seconds_returns_longest_continuous_speech() -> None:
    """When speech is < 8s, select_solo_snippet must return the longest available continuous speech."""
    speaker_turns: list[dict[str, Any]] = [
        {"speaker": "SPEAKER_00", "start_ms": 0, "end_ms": 2500},
        {"speaker": "SPEAKER_00", "start_ms": 5000, "end_ms": 11000},
    ]
    all_speaker_turns = list(speaker_turns)

    result = select_solo_snippet(speaker_turns, all_speaker_turns)

    assert isinstance(result, dict)
    assert result["start_ms"] == 5000
    assert result["end_ms"] == 11000
    assert (result["end_ms"] - result["start_ms"]) == 6000


def test_select_solo_snippet_with_cross_talk_in_short_speech_selects_longest_clean_segment() -> None:
    """select_solo_snippet must select longest non-overlapping solo segment when all segments < 8s."""
    speaker_turns: list[dict[str, Any]] = [
        {"speaker": "SPEAKER_00", "start_ms": 0, "end_ms": 7000}
    ]
    all_speaker_turns: list[dict[str, Any]] = [
        {"speaker": "SPEAKER_00", "start_ms": 0, "end_ms": 7000},
        {"speaker": "SPEAKER_01", "start_ms": 2000, "end_ms": 3000},
    ]

    result = select_solo_snippet(speaker_turns, all_speaker_turns)

    assert isinstance(result, dict)
    # Segments are [0, 2000] (2000ms) and [3000, 7000] (4000ms). Longest is [3000, 7000].
    assert result["start_ms"] == 3000
    assert result["end_ms"] == 7000


def test_select_solo_snippet_empty_turns_returns_none() -> None:
    """select_solo_snippet must return None when no speaker turns are provided."""
    result = select_solo_snippet([], [])
    assert result is None
