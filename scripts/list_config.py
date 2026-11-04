"""Load, normalize, and validate the administrator's mod list."""

from __future__ import annotations

import json
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Any
from urllib.parse import parse_qs, urlparse

L4D2_APP_ID = 550
_WORKSHOP_ID = re.compile(r"^[1-9][0-9]{0,19}$")
_ALLOWED_HOSTS = {"steamcommunity.com", "www.steamcommunity.com"}


class ListConfigError(ValueError):
    """Raised when config/list.json is invalid."""


@dataclass(frozen=True, slots=True)
class ModEntry:
    workshop_id: str
    enabled: bool
    redistribution_authorized: bool = False


@dataclass(frozen=True, slots=True)
class ModList:
    game: int
    mods: tuple[ModEntry, ...]


def extract_workshop_id(value: object) -> str:
    """Return a numeric Workshop ID from an ID or supported Workshop URL."""
    if isinstance(value, bool) or not isinstance(value, (str, int)):
        raise ListConfigError("o campo 'id' deve ser um ID numérico ou uma URL do Workshop")

    text = str(value).strip()
    if _WORKSHOP_ID.fullmatch(text):
        return text

    parsed = urlparse(text)
    if parsed.scheme not in {"http", "https"} or parsed.hostname not in _ALLOWED_HOSTS:
        raise ListConfigError(f"ID/URL do Workshop inválido: {text!r}")
    if parsed.path.rstrip("/") != "/sharedfiles/filedetails":
        raise ListConfigError(f"URL do Workshop não suportada: {text!r}")

    ids = parse_qs(parsed.query).get("id", [])
    if len(ids) != 1 or not _WORKSHOP_ID.fullmatch(ids[0]):
        raise ListConfigError(f"URL sem um ID válido: {text!r}")
    return ids[0]


def _require_bool(item: dict[str, Any], field: str, index: int, default: bool | None = None) -> bool:
    value = item.get(field, default)
    if not isinstance(value, bool):
        raise ListConfigError(f"mods[{index}].{field} deve ser true ou false")
    return value


def parse_mod_list(data: object) -> ModList:
    """Validate data, normalize IDs, remove duplicates, and preserve order."""
    if not isinstance(data, dict):
        raise ListConfigError("a raiz do arquivo deve ser um objeto JSON")
    unknown_root = set(data) - {"game", "mods"}
    if unknown_root:
        raise ListConfigError(f"campos desconhecidos na raiz: {', '.join(sorted(unknown_root))}")
    if data.get("game") != L4D2_APP_ID:
        raise ListConfigError(f"'game' deve ser {L4D2_APP_ID} (Left 4 Dead 2)")

    raw_mods = data.get("mods")
    if not isinstance(raw_mods, list):
        raise ListConfigError("'mods' deve ser uma lista")

    mods: list[ModEntry] = []
    seen: set[str] = set()
    for index, item in enumerate(raw_mods):
        if not isinstance(item, dict):
            raise ListConfigError(f"mods[{index}] deve ser um objeto")
        unknown = set(item) - {"id", "enabled", "redistributionAuthorized"}
        if unknown:
            raise ListConfigError(f"campos desconhecidos em mods[{index}]: {', '.join(sorted(unknown))}")
        if "id" not in item or "enabled" not in item:
            raise ListConfigError(f"mods[{index}] precisa de 'id' e 'enabled'")

        workshop_id = extract_workshop_id(item["id"])
        enabled = _require_bool(item, "enabled", index)
        authorized = _require_bool(item, "redistributionAuthorized", index, False)
        if workshop_id in seen:
            continue
        seen.add(workshop_id)
        mods.append(ModEntry(workshop_id, enabled, authorized))

    return ModList(game=L4D2_APP_ID, mods=tuple(mods))


def load_mod_list(path: Path) -> ModList:
    try:
        with path.open("r", encoding="utf-8") as file:
            data = json.load(file)
    except FileNotFoundError as exc:
        raise ListConfigError(f"arquivo não encontrado: {path}") from exc
    except json.JSONDecodeError as exc:
        raise ListConfigError(f"JSON inválido em {path}: linha {exc.lineno}, coluna {exc.colno}") from exc
    return parse_mod_list(data)

