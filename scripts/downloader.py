"""Authorized Workshop downloads through SteamCMD."""

from __future__ import annotations

import shutil
import subprocess
import time
from pathlib import Path


class DownloadError(RuntimeError):
    pass


def _steamcmd_path(explicit: str | None) -> str:
    candidates = [explicit] if explicit else []
    candidates.extend(["steamcmd", "/usr/games/steamcmd"])
    for candidate in candidates:
        if candidate and (shutil.which(candidate) or Path(candidate).is_file()):
            return candidate
    raise DownloadError("SteamCMD não encontrado; defina STEAMCMD_PATH ou instale o steamcmd")


def download_item(
    app_id: int,
    workshop_id: str,
    download_root: Path,
    steamcmd: str | None = None,
    attempts: int = 3,
) -> list[Path]:
    """Download one item and return every non-empty VPK it produced."""
    executable = _steamcmd_path(steamcmd)
    item_dir = download_root / "steamapps" / "workshop" / "content" / str(app_id) / workshop_id
    last_error = ""
    for attempt in range(1, attempts + 1):
        if item_dir.exists():
            shutil.rmtree(item_dir)
        command = [
            executable,
            "+force_install_dir", str(download_root.resolve()),
            "+login", "anonymous",
            "+workshop_download_item", str(app_id), workshop_id, "validate",
            "+quit",
        ]
        completed = subprocess.run(command, capture_output=True, text=True, timeout=1800, check=False)
        files = sorted(path for path in item_dir.rglob("*.vpk") if path.is_file() and path.stat().st_size > 0)
        if completed.returncode == 0 and files:
            return files
        output = (completed.stdout + "\n" + completed.stderr).strip()
        last_error = output[-2000:] or f"código de saída {completed.returncode}"
        if attempt < attempts:
            time.sleep(2 ** attempt)
    raise DownloadError(f"download do item {workshop_id} falhou ou não gerou VPK: {last_error}")

