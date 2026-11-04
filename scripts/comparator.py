"""Manifest comparison and semantic version selection."""

from __future__ import annotations

import re
from dataclasses import dataclass

_VERSION = re.compile(r"^(\d+)\.(\d+)\.(\d+)$")


@dataclass(frozen=True, slots=True)
class Changes:
    added: tuple[str, ...]
    updated: tuple[str, ...]
    removed: tuple[str, ...]
    unchanged: tuple[str, ...]

    @property
    def changed(self) -> bool:
        return bool(self.added or self.updated or self.removed)


def compare(previous: dict, current_updated_at: dict[str, int]) -> Changes:
    old = {str(mod["id"]): int(mod.get("timeUpdated", 0)) for mod in previous.get("mods", [])}
    new_ids, old_ids = set(current_updated_at), set(old)
    common = new_ids & old_ids
    return Changes(
        added=tuple(sorted(new_ids - old_ids)),
        updated=tuple(sorted(item_id for item_id in common if current_updated_at[item_id] != old[item_id])),
        removed=tuple(sorted(old_ids - new_ids)),
        unchanged=tuple(sorted(item_id for item_id in common if current_updated_at[item_id] == old[item_id])),
    )


def next_version(previous: str | None, changes: Changes) -> str:
    if not previous:
        return "1.0.0"
    match = _VERSION.fullmatch(previous)
    if not match:
        raise ValueError(f"versão anterior inválida: {previous}")
    major, minor, patch = map(int, match.groups())
    if changes.added or changes.removed:
        return f"{major}.{minor + 1}.0"
    if changes.updated:
        return f"{major}.{minor}.{patch + 1}"
    return previous

