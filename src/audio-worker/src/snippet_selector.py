"""Speaker solo snippet selection module for Pocket Export Audio ML Sidecar."""

from __future__ import annotations

from typing import Any


def _merge_intervals(intervals: list[tuple[int, int]]) -> list[tuple[int, int]]:
    """Merge overlapping or adjacent intervals."""
    if not intervals:
        return []
    sorted_intervals = sorted(intervals, key=lambda x: (x[0], x[1]))
    merged = [sorted_intervals[0]]
    for start, end in sorted_intervals[1:]:
        prev_start, prev_end = merged[-1]
        if start <= prev_end:
            merged[-1] = (prev_start, max(prev_end, end))
        else:
            merged.append((start, end))
    return merged


def _extract_crosstalk_intervals(
    speaker_turns: list[dict[str, Any]],
    all_speaker_turns: list[dict[str, Any]],
) -> list[tuple[int, int]]:
    """Extract non-overlapping intervals belonging to other speakers."""
    target_speakers = {t["speaker"] for t in speaker_turns if "speaker" in t}
    target_turn_tuples = {
        (
            t.get("speaker"),
            int(round(float(t.get("start_ms", 0)))),
            int(round(float(t.get("end_ms", 0)))),
        )
        for t in speaker_turns
    }

    crosstalk: list[tuple[int, int]] = []
    for turn in all_speaker_turns:
        start = int(round(float(turn.get("start_ms", 0))))
        end = int(round(float(turn.get("end_ms", start))))
        if end <= start:
            continue

        turn_tuple = (turn.get("speaker"), start, end)
        if turn_tuple in target_turn_tuples:
            continue

        if target_speakers and "speaker" in turn:
            if turn["speaker"] not in target_speakers:
                crosstalk.append((start, end))
        else:
            crosstalk.append((start, end))

    return _merge_intervals(crosstalk)


def select_solo_snippet(
    speaker_turns: list[dict[str, Any]],
    all_speaker_turns: list[dict[str, Any]],
) -> dict[str, Any] | None:
    """Select the cleanest non-overlapping single-speaker window for voiceprint enrollment.

    Args:
        speaker_turns: List of turn dicts for the target speaker with 'start_ms', 'end_ms'.
        all_speaker_turns: List of all turn dicts from all speakers.

    Returns:
        A dictionary {'start_ms': int, 'end_ms': int} representing the selected snippet,
        or None if no speaker turns are provided.
    """
    if not speaker_turns:
        return None

    # Merge target speaker intervals
    target_intervals: list[tuple[int, int]] = []
    for turn in speaker_turns:
        start = int(round(float(turn.get("start_ms", 0))))
        end = int(round(float(turn.get("end_ms", start))))
        if end > start:
            target_intervals.append((start, end))

    target_merged = _merge_intervals(target_intervals)
    if not target_merged:
        return None

    crosstalk = _extract_crosstalk_intervals(speaker_turns, all_speaker_turns)

    # Subtract crosstalk from target speech intervals
    clean_segments: list[tuple[int, int]] = []
    for t_start, t_end in target_merged:
        current = [(t_start, t_end)]
        for o_start, o_end in crosstalk:
            next_current: list[tuple[int, int]] = []
            for s, e in current:
                if o_end <= s or o_start >= e:
                    next_current.append((s, e))
                else:
                    if o_start > s:
                        next_current.append((s, o_start))
                    if o_end < e:
                        next_current.append((o_end, e))
            current = next_current
        clean_segments.extend(current)

    clean_segments = [(s, e) for s, e in clean_segments if e > s]
    if not clean_segments:
        return None

    # Pick the longest continuous clean segment
    sorted_segments = sorted(
        clean_segments,
        key=lambda seg: (seg[1] - seg[0], -seg[0]),
        reverse=True,
    )
    best_start, best_end = sorted_segments[0]
    best_duration = best_end - best_start

    if best_duration >= 8000:
        win_len = min(best_duration, 10000)
        return {"start_ms": best_start, "end_ms": best_start + win_len}

    return {"start_ms": best_start, "end_ms": best_end}
