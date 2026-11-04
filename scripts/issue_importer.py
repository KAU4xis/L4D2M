"""Import Workshop items from a closed GitHub Issue Form into list.json."""

from __future__ import annotations

import argparse
import json
import re
from dataclasses import dataclass
from pathlib import Path

from scripts.list_config import L4D2_APP_ID, ListConfigError, extract_workshop_id, load_mod_list
from scripts.workshop import fetch_details

_SECTION = re.compile(r"^###\s+Links dos mods\s*$\n(.*?)(?=^###\s|\Z)", re.IGNORECASE | re.MULTILINE | re.DOTALL)
_URL_OR_ID = re.compile(r"https?://steamcommunity\.com/sharedfiles/filedetails/\?[^\s<>]*id=\d+[^\s<>]*|(?<!\d)\d{1,20}(?!\d)", re.IGNORECASE)
_AUTHORIZATION = re.compile(
    r"^###\s+Autorização de redistribuição\s*$.*?^-\s*\[[xX]\]\s+Confirmo que estes itens possuem autorização",
    re.IGNORECASE | re.MULTILINE | re.DOTALL,
)


class IssueImportError(RuntimeError):
    pass


@dataclass(frozen=True, slots=True)
class ImportResult:
    added: tuple[str, ...]
    duplicates: tuple[str, ...]


def extract_issue_ids(body: str) -> list[str]:
    match = _SECTION.search(body.replace("\r\n", "\n"))
    if not match:
        raise IssueImportError("seção 'Links dos mods' não encontrada na Issue")
    values = _URL_OR_ID.findall(match.group(1))
    if not values:
        raise IssueImportError("nenhum ID ou link do Workshop encontrado")
    ids: list[str] = []
    seen: set[str] = set()
    for value in values:
        try:
            item_id = extract_workshop_id(value)
        except ListConfigError as exc:
            raise IssueImportError(str(exc)) from exc
        if item_id not in seen:
            seen.add(item_id)
            ids.append(item_id)
    return ids


def validate_authorization(body: str) -> None:
    if not _AUTHORIZATION.search(body.replace("\r\n", "\n")):
        raise IssueImportError("a confirmação de autorização para redistribuição não foi marcada")


def import_ids(list_path: Path, ids: list[str]) -> ImportResult:
    current = load_mod_list(list_path)
    existing = {mod.workshop_id for mod in current.mods}
    new_ids = [item_id for item_id in ids if item_id not in existing]
    duplicates = tuple(item_id for item_id in ids if item_id in existing)
    if not new_ids:
        return ImportResult((), duplicates)

    details = fetch_details(new_ids)
    unavailable = [item_id for item_id in new_ids if item_id not in details or not details[item_id].available]
    wrong_game = [item_id for item_id in new_ids if details.get(item_id) and details[item_id].available and details[item_id].app_id != L4D2_APP_ID]
    if unavailable:
        raise IssueImportError(f"itens indisponíveis ou privados: {', '.join(unavailable)}")
    if wrong_game:
        raise IssueImportError(f"itens que não pertencem ao Left 4 Dead 2: {', '.join(wrong_game)}")

    with list_path.open("r", encoding="utf-8") as file:
        raw = json.load(file)
    for item_id in new_ids:
        raw["mods"].append({"id": item_id, "enabled": True, "redistributionAuthorized": True})
    temporary = list_path.with_suffix(".json.tmp")
    with temporary.open("w", encoding="utf-8", newline="\n") as file:
        json.dump(raw, file, ensure_ascii=False, indent=2)
        file.write("\n")
    temporary.replace(list_path)
    load_mod_list(list_path)
    return ImportResult(tuple(new_ids), duplicates)


def summary_markdown(result: ImportResult, list_path: Path) -> str:
    current = load_mod_list(list_path)
    lines = [
        "## 🧟 Importação concluída",
        "",
        "| Resultado | Quantidade |",
        "|:--|--:|",
        f"| ✅ Adicionados | **{len(result.added)}** |",
        f"| ♻️ Duplicados ignorados | **{len(result.duplicates)}** |",
        f"| 📦 Total no `list.json` | **{len(current.mods)}** |",
        "",
    ]
    if result.added:
        lines += ["### ✅ IDs adicionados", "", *[f"- `{item_id}`" for item_id in result.added], ""]
    if result.duplicates:
        lines += ["### ♻️ IDs que já existiam", "", *[f"- `{item_id}`" for item_id in result.duplicates], ""]
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--event", type=Path, required=True)
    parser.add_argument("--list", type=Path, default=Path("config/list.json"))
    parser.add_argument("--summary", type=Path, required=True)
    args = parser.parse_args()
    with args.event.open("r", encoding="utf-8") as file:
        event = json.load(file)
    body = str(event.get("issue", {}).get("body") or "")
    validate_authorization(body)
    result = import_ids(args.list, extract_issue_ids(body))
    args.summary.write_text(summary_markdown(result, args.list), encoding="utf-8")
    print(f"Adicionados: {len(result.added)}; duplicados ignorados: {len(result.duplicates)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

