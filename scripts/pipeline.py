"""End-to-end incremental Workshop and GitHub Releases pipeline."""

from __future__ import annotations

import argparse
import os
import shutil
from datetime import datetime, timezone
from pathlib import Path

from scripts.comparator import compare, next_version
from scripts.downloader import download_item
from scripts.list_config import load_mod_list
from scripts.manifest import asset_name, atomic_write_json, load_manifest, sha256_file, validate_manifest
from scripts.publisher import create_draft, delete_draft, download_latest_manifest, publish, upload, verify_assets
from scripts.workshop import fetch_details


def release_url(repository: str, tag: str, filename: str) -> str:
    return f"https://github.com/{repository}/releases/download/{tag}/{filename}"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--list", type=Path, default=Path("config/list.json"))
    parser.add_argument("--manifest", type=Path, default=Path("manifest.json"))
    parser.add_argument("--work-dir", type=Path, default=Path("dist/work"))
    parser.add_argument("--dry-run", action="store_true", help="consulta e compara sem baixar/publicar")
    args = parser.parse_args()

    repository = os.environ.get("GITHUB_REPOSITORY", "")
    config = load_mod_list(args.list)
    selected = [mod for mod in config.mods if mod.enabled and mod.redistribution_authorized]
    if not args.dry_run and repository:
        download_latest_manifest(args.manifest)
    previous = load_manifest(args.manifest)
    if not selected and not previous.get("mods"):
        print("Nenhum mod habilitado e autorizado para redistribuição; nada a publicar.")
        return 0

    details = fetch_details([mod.workshop_id for mod in selected])
    unavailable = [mod.workshop_id for mod in selected if mod.workshop_id not in details or not details[mod.workshop_id].available]
    wrong_game = [item.workshop_id for item in details.values() if item.available and item.app_id != config.game]
    if unavailable:
        raise RuntimeError(f"itens indisponíveis: {', '.join(unavailable)}")
    if wrong_game:
        raise RuntimeError(f"itens não pertencem ao L4D2: {', '.join(wrong_game)}")

    timestamps = {mod.workshop_id: details[mod.workshop_id].time_updated for mod in selected}
    changes = compare(previous, timestamps)
    if not changes.changed:
        print("Nenhuma alteração detectada; nenhuma Release será criada.")
        return 0
    version = next_version(previous.get("latest"), changes)
    tag = f"v{version}"
    print(f"Alterações: +{len(changes.added)} ~{len(changes.updated)} -{len(changes.removed)}; próxima versão {version}")
    if args.dry_run:
        return 0
    if not repository or "/" not in repository:
        raise RuntimeError("GITHUB_REPOSITORY não definido no formato owner/repo")

    shutil.rmtree(args.work_dir, ignore_errors=True)
    download_root = args.work_dir / "steam"
    assets_dir = args.work_dir / "assets"
    assets_dir.mkdir(parents=True)
    old_by_id = {str(mod["id"]): mod for mod in previous.get("mods", [])}
    changed_ids = set(changes.added) | set(changes.updated)
    output_mods: list[dict] = []
    asset_paths: list[Path] = []

    for selected_mod in selected:
        item_id = selected_mod.workshop_id
        metadata = details[item_id]
        if item_id not in changed_ids:
            output_mods.append(old_by_id[item_id])
            continue
        files = download_item(config.game, item_id, download_root, os.environ.get("STEAMCMD_PATH"))
        entries = []
        for index, source in enumerate(files, start=1):
            digest = sha256_file(source)
            filename = asset_name(item_id, index, source, digest)
            destination = assets_dir / filename
            shutil.copy2(source, destination)
            asset_paths.append(destination)
            entries.append({
                "filename": filename,
                "size": destination.stat().st_size,
                "sha256": digest,
                "url": release_url(repository, tag, filename),
            })
        output_mods.append({
            "id": item_id,
            "name": metadata.name,
            "workshopUrl": metadata.url,
            "creator": metadata.creator,
            "timeUpdated": metadata.time_updated,
            "files": entries,
        })

    generated = datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")
    manifest = {"schemaVersion": 1, "latest": version, "game": config.game, "generatedAt": generated, "mods": output_mods}
    validate_manifest(manifest)
    historical = args.work_dir / f"manifest-{version}.json"
    latest_asset = args.work_dir / "manifest.json"
    atomic_write_json(historical, manifest)
    atomic_write_json(latest_asset, manifest)

    expected = {path.name for path in asset_paths} | {historical.name, latest_asset.name}
    draft_created = False
    try:
        create_draft(tag, f"L4D2 Modpack {version}", f"Adicionados: {len(changes.added)}\nAtualizados: {len(changes.updated)}\nRemovidos: {len(changes.removed)}")
        draft_created = True
        upload(tag, asset_paths)
        upload(tag, [historical, latest_asset])
        verify_assets(tag, expected)
        publish(tag)
    except BaseException:
        if draft_created:
            delete_draft(tag)
        raise

    atomic_write_json(args.manifest, manifest)
    print(f"Release {tag} publicada e manifesto atualizado.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

