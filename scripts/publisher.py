"""Safe GitHub Release publishing through the authenticated gh CLI."""

from __future__ import annotations

import json
import shutil
import subprocess
from pathlib import Path


class PublishError(RuntimeError):
    pass


def _run(args: list[str]) -> str:
    completed = subprocess.run(args, capture_output=True, text=True, check=False)
    if completed.returncode:
        raise PublishError((completed.stderr or completed.stdout).strip())
    return completed.stdout.strip()


def create_draft(tag: str, title: str, notes: str) -> None:
    _run(["gh", "release", "create", tag, "--draft", "--title", title, "--notes", notes])


def upload(tag: str, paths: list[Path]) -> None:
    if paths:
        _run(["gh", "release", "upload", tag, *map(str, paths), "--clobber"])


def verify_assets(tag: str, expected_names: set[str]) -> None:
    raw = _run(["gh", "release", "view", tag, "--json", "assets"])
    assets = {asset["name"]: int(asset.get("size", 0)) for asset in json.loads(raw)["assets"]}
    missing = sorted(name for name in expected_names if assets.get(name, 0) <= 0)
    if missing:
        raise PublishError(f"assets ausentes ou vazios na Release: {', '.join(missing)}")


def publish(tag: str) -> None:
    _run(["gh", "release", "edit", tag, "--draft=false", "--latest"])


def download_latest_manifest(destination: Path) -> bool:
    """Recover the canonical manifest from the latest published Release."""
    temporary = destination.parent / ".latest-release-manifest"
    shutil.rmtree(temporary, ignore_errors=True)
    temporary.mkdir(parents=True)
    completed = subprocess.run(
        ["gh", "release", "download", "--pattern", "manifest.json", "--dir", str(temporary)],
        capture_output=True,
        text=True,
        check=False,
    )
    downloaded = temporary / "manifest.json"
    if completed.returncode != 0 or not downloaded.is_file():
        shutil.rmtree(temporary, ignore_errors=True)
        return False
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(downloaded, destination)
    shutil.rmtree(temporary, ignore_errors=True)
    return True


def delete_draft(tag: str) -> None:
    subprocess.run(["gh", "release", "delete", tag, "--yes", "--cleanup-tag"], capture_output=True, check=False)

