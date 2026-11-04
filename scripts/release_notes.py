"""Rich, maintainable Markdown notes for GitHub Releases."""

from __future__ import annotations

from scripts.comparator import Changes


def human_size(size: int) -> str:
    value = float(size)
    for unit in ("B", "KiB", "MiB", "GiB"):
        if value < 1024 or unit == "GiB":
            return f"{value:.0f} {unit}" if unit == "B" else f"{value:.2f} {unit}"
        value /= 1024
    raise AssertionError("unreachable")


def _cell(value: object) -> str:
    return str(value or "—").replace("|", "\\|").replace("\r", " ").replace("\n", " ").strip()


def _status(item_id: str, changes: Changes) -> str:
    if item_id in changes.added:
        return "🟢 Adicionado"
    if item_id in changes.updated:
        return "🟡 Atualizado"
    return "⚪ Mantido"


def _mod_table(mods: list[dict], changes: Changes) -> list[str]:
    lines = [
        "| Estado | Mod | Workshop ID | VPKs | Tamanho |",
        "|:--|:--|--:|--:|--:|",
    ]
    for mod in mods:
        files = mod.get("files", [])
        size = sum(int(file.get("size", 0)) for file in files)
        name = _cell(mod.get("name"))
        url = mod.get("workshopUrl", "")
        link = f"[{name}]({url})" if url else name
        lines.append(f"| {_status(str(mod['id']), changes)} | {link} | `{mod['id']}` | {len(files)} | {human_size(size)} |")
    return lines


def _change_table(title: str, emoji: str, ids: tuple[str, ...], by_id: dict[str, dict]) -> list[str]:
    lines = [f"### {emoji} {title} ({len(ids)})", ""]
    if not ids:
        return lines + ["_Nenhum nesta versão._", ""]
    lines += ["| Mod | Workshop ID |", "|:--|--:|"]
    for item_id in ids:
        mod = by_id.get(item_id, {"id": item_id, "name": "Item do Workshop", "workshopUrl": ""})
        name = _cell(mod.get("name"))
        url = mod.get("workshopUrl", "")
        link = f"[{name}]({url})" if url else name
        lines.append(f"| {link} | `{item_id}` |")
    return lines + [""]


def build_release_notes(version: str, manifest: dict, previous: dict, changes: Changes) -> str:
    mods = manifest.get("mods", [])
    previous_mods = previous.get("mods", [])
    current_by_id = {str(mod["id"]): mod for mod in mods}
    previous_by_id = {str(mod["id"]): mod for mod in previous_mods}
    all_for_changes = previous_by_id | current_by_id
    file_count = sum(len(mod.get("files", [])) for mod in mods)
    total_size = sum(int(file.get("size", 0)) for mod in mods for file in mod.get("files", []))

    lines = [
        f"# 🧟 L4D2 Modpack — v{version}",
        "",
        "> Pacote sincronizado automaticamente com a Oficina Steam e validado por SHA-256. "
        "Esta Release representa o estado completo do modpack, mesmo quando reutiliza VPKs de versões anteriores.",
        "",
        "## 📊 Resumo",
        "",
        "| 🎮 Mods ativos | 📦 Arquivos VPK | 💾 Tamanho total | ➕ Adicionados | 🔄 Atualizados | ➖ Removidos |",
        "|--:|--:|--:|--:|--:|--:|",
        f"| **{len(mods)}** | **{file_count}** | **{human_size(total_size)}** | **{len(changes.added)}** | **{len(changes.updated)}** | **{len(changes.removed)}** |",
        "",
        "## 🧩 Mods incluídos",
        "",
        *_mod_table(mods, changes),
        "",
        "## 📝 Alterações desta versão",
        "",
        *_change_table("Adicionados", "➕", changes.added, all_for_changes),
        *_change_table("Atualizados", "🔄", changes.updated, all_for_changes),
        *_change_table("Removidos", "➖", changes.removed, all_for_changes),
        "## 📖 Descrições dos mods",
        "",
    ]
    if not mods:
        lines += ["_O modpack está vazio nesta versão._", ""]
    for mod in mods:
        description = _cell(mod.get("description"))
        lines += [
            "<details>",
            f"<summary><strong>🎯 {_cell(mod.get('name'))}</strong> — <code>{mod['id']}</code></summary>",
            "",
            description if description != "—" else "_O autor não forneceu uma descrição no Workshop._",
            "",
            f"[🔗 Abrir na Oficina Steam]({mod.get('workshopUrl', '')})",
            "",
            "</details>",
            "",
        ]
    lines += [
        "---",
        "",
        "### ℹ️ Sobre os downloads",
        "",
        "O `manifest.json` aponta para os VPKs exatos desta versão. Arquivos inalterados podem permanecer hospedados em Releases anteriores para evitar uploads e downloads duplicados.",
        "",
        "_Gerado automaticamente pelo L4D2 Modpack Manager._",
    ]
    return "\n".join(lines) + "\n"

