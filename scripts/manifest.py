"""Manifest construction and integrity helpers."""

from __future__ import annotations

import hashlib
import json
import os
import re
import tempfile
import unicodedata
from pathlib import Path

_SAFE_ASSET = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,199}$")


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as file:
        for chunk in iter(lambda: file.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _slug(value: str, maximum: int = 80) -> str:
    normalized = unicodedata.normalize("NFKD", value).encode("ascii", "ignore").decode("ascii")
    cleaned = re.sub(r"[^A-Za-z0-9]+", "-", normalized).strip("-")
    return (cleaned or "Workshop-Mod")[:maximum].rstrip("-")


def asset_name(workshop_id: str, index: int, source: Path, digest: str, mod_name: str = "Workshop Mod") -> str:
    suffix = source.suffix.lower()
    part = f"-part{index}" if index > 1 else ""
    name = f"{_slug(mod_name)}-{workshop_id}{part}-{digest[:12]}{suffix}"
    if suffix != ".vpk" or not _SAFE_ASSET.fullmatch(name):
        raise ValueError(f"nome de asset inseguro: {name!r}")
    return name


def load_manifest(path: Path) -> dict:
    if not path.exists():
        return {"schemaVersion": 1, "latest": None, "game": 550, "generatedAt": None, "mods": []}
    with path.open("r", encoding="utf-8") as file:
        data = json.load(file)
    if data.get("schemaVersion") != 1 or data.get("game") != 550 or not isinstance(data.get("mods"), list):
        raise ValueError("manifesto anterior inválido")
    return data


def atomic_write_json(path: Path, data: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary = tempfile.mkstemp(prefix=path.name, suffix=".tmp", dir=path.parent)
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8", newline="\n") as file:
            json.dump(data, file, ensure_ascii=False, indent=2)
            file.write("\n")
        os.replace(temporary, path)
    except BaseException:
        Path(temporary).unlink(missing_ok=True)
        raise


def validate_manifest(data: dict) -> None:
    if data.get("schemaVersion") != 1 or data.get("game") != 550:
        raise ValueError("cabeçalho do manifesto inválido")
    if not isinstance(data.get("latest"), str) or not data["latest"]:
        raise ValueError("versão ausente no manifesto")
    seen_files: set[str] = set()
    for mod in data.get("mods", []):
        if not str(mod.get("id", "")).isdigit() or not mod.get("files"):
            raise ValueError("mod inválido ou sem arquivos no manifesto")
        for item in mod["files"]:
            filename, digest, url = item.get("filename"), item.get("sha256"), item.get("url")
            if not isinstance(filename, str) or not _SAFE_ASSET.fullmatch(filename):
                raise ValueError("nome de arquivo inseguro no manifesto")
            if filename in seen_files or not isinstance(digest, str) or not re.fullmatch(r"[0-9a-f]{64}", digest):
                raise ValueError("arquivo duplicado ou hash inválido no manifesto")
            if not isinstance(url, str) or not url.startswith("https://github.com/"):
                raise ValueError("URL de asset inválida no manifesto")
            if int(item.get("size", 0)) <= 0:
                raise ValueError("tamanho de arquivo inválido no manifesto")
            seen_files.add(filename)

