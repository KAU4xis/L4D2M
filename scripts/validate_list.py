"""Command-line validation for config/list.json."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from scripts.list_config import ListConfigError, load_mod_list


def main() -> int:
    parser = argparse.ArgumentParser(description="Valida a lista de mods do L4D2")
    parser.add_argument("path", nargs="?", type=Path, default=Path("config/list.json"))
    args = parser.parse_args()
    try:
        mod_list = load_mod_list(args.path)
    except ListConfigError as exc:
        print(f"ERRO: {exc}", file=sys.stderr)
        return 1

    enabled = sum(mod.enabled for mod in mod_list.mods)
    authorized = sum(mod.enabled and mod.redistribution_authorized for mod in mod_list.mods)
    print(f"Lista válida: {len(mod_list.mods)} mod(s), {enabled} habilitado(s), {authorized} autorizado(s) para redistribuição.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

