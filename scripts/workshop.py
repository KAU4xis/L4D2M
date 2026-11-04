"""Steam Workshop metadata integration using Valve's public endpoint."""

from __future__ import annotations

import json
import ssl
from dataclasses import dataclass
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode
from urllib.request import Request, urlopen

import certifi

DETAILS_URL = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/"


class WorkshopError(RuntimeError):
    pass


@dataclass(frozen=True, slots=True)
class WorkshopItem:
    workshop_id: str
    name: str
    app_id: int
    creator: str | None
    time_updated: int
    file_size: int | None
    visibility: int | None
    available: bool
    result: int

    @property
    def url(self) -> str:
        return f"https://steamcommunity.com/sharedfiles/filedetails/?id={self.workshop_id}"


def fetch_details(ids: list[str], timeout: int = 30) -> dict[str, WorkshopItem]:
    if not ids:
        return {}
    form: list[tuple[str, str]] = [("itemcount", str(len(ids)))]
    form.extend((f"publishedfileids[{index}]", item_id) for index, item_id in enumerate(ids))
    request = Request(
        DETAILS_URL,
        data=urlencode(form).encode("ascii"),
        headers={"User-Agent": "l4d2-modpack-manager/1", "Content-Type": "application/x-www-form-urlencoded"},
        method="POST",
    )
    try:
        context = ssl.create_default_context(cafile=certifi.where())
        with urlopen(request, timeout=timeout, context=context) as response:
            payload = json.load(response)
    except (HTTPError, URLError, TimeoutError, json.JSONDecodeError) as exc:
        raise WorkshopError(f"falha ao consultar o Steam Workshop: {exc}") from exc

    raw_items = payload.get("response", {}).get("publishedfiledetails")
    if not isinstance(raw_items, list):
        raise WorkshopError("resposta inesperada da API do Steam Workshop")

    items: dict[str, WorkshopItem] = {}
    for raw in raw_items:
        item_id = str(raw.get("publishedfileid", ""))
        result = int(raw.get("result", 0))
        items[item_id] = WorkshopItem(
            workshop_id=item_id,
            name=str(raw.get("title") or f"Workshop {item_id}"),
            app_id=int(raw.get("consumer_app_id") or 0),
            creator=str(raw["creator"]) if raw.get("creator") else None,
            time_updated=int(raw.get("time_updated") or 0),
            file_size=int(raw["file_size"]) if str(raw.get("file_size", "")).isdigit() else None,
            visibility=int(raw["visibility"]) if str(raw.get("visibility", "")).isdigit() else None,
            available=result == 1,
            result=result,
        )
    return items

