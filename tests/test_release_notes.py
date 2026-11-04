import unittest

from scripts.comparator import Changes
from scripts.release_notes import _steam_description, build_release_notes, human_size


class ReleaseNotesTests(unittest.TestCase):
    def test_human_size(self) -> None:
        self.assertEqual(human_size(19329610), "18.43 MiB")

    def test_builds_rich_markdown(self) -> None:
        manifest = {
            "mods": [{
                "id": "123",
                "name": "Pipe | Bomb",
                "description": "Uma descrição\nbonita.",
                "workshopUrl": "https://steamcommunity.com/sharedfiles/filedetails/?id=123",
                "files": [{"size": 1024}],
            }],
        }
        notes = build_release_notes("1.0.4", manifest, {"mods": []}, Changes(("123",), (), (), ()))
        self.assertIn("# 🧟 L4D2 Modpack — v1.0.4", notes)
        self.assertIn("**1.00 KiB**", notes)
        self.assertIn("Pipe \\| Bomb", notes)
        self.assertIn("🟢 Adicionado", notes)
        self.assertIn("<details>", notes)

    def test_converts_steam_bbcode(self) -> None:
        converted = _steam_description("[b]Arma[/b] [img]https://example.com/demo.gif[/img]")
        self.assertIn("**Arma**", converted)
        self.assertIn("![Imagem do mod](https://example.com/demo.gif)", converted)


if __name__ == "__main__":
    unittest.main()
