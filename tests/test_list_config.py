import json
import tempfile
import unittest
from pathlib import Path

from scripts.list_config import ListConfigError, extract_workshop_id, load_mod_list, parse_mod_list


class WorkshopIdTests(unittest.TestCase):
    def test_accepts_numeric_id(self) -> None:
        self.assertEqual(extract_workshop_id("3566800973"), "3566800973")

    def test_extracts_id_from_url(self) -> None:
        url = "https://steamcommunity.com/sharedfiles/filedetails/?id=2396847377"
        self.assertEqual(extract_workshop_id(url), "2396847377")

    def test_rejects_untrusted_host(self) -> None:
        with self.assertRaises(ListConfigError):
            extract_workshop_id("https://example.com/sharedfiles/filedetails/?id=123")


class ModListTests(unittest.TestCase):
    def test_normalizes_and_deduplicates_preserving_first_entry(self) -> None:
        data = {
            "game": 550,
            "mods": [
                {"id": "123", "enabled": True, "redistributionAuthorized": True},
                {"id": "https://steamcommunity.com/sharedfiles/filedetails/?id=456", "enabled": False},
                {"id": 123, "enabled": False},
            ],
        }
        result = parse_mod_list(data)
        self.assertEqual([mod.workshop_id for mod in result.mods], ["123", "456"])
        self.assertTrue(result.mods[0].redistribution_authorized)

    def test_rejects_wrong_game(self) -> None:
        with self.assertRaises(ListConfigError):
            parse_mod_list({"game": 4000, "mods": []})

    def test_reports_invalid_json(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "list.json"
            path.write_text("{invalid", encoding="utf-8")
            with self.assertRaisesRegex(ListConfigError, "JSON inválido"):
                load_mod_list(path)


if __name__ == "__main__":
    unittest.main()

