import tempfile
import unittest
from pathlib import Path

from scripts.manifest import asset_name, atomic_write_json, load_manifest, sha256_file, validate_manifest


class ManifestTests(unittest.TestCase):
    def test_hash_and_asset_name(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "addon.vpk"
            path.write_bytes(b"test")
            digest = sha256_file(path)
            self.assertEqual(digest, "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")
            self.assertEqual(asset_name("123", 1, path, digest), "123-1-9f86d081884c7d65.vpk")

    def test_atomic_round_trip(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "manifest.json"
            data = {"schemaVersion": 1, "latest": None, "game": 550, "generatedAt": None, "mods": []}
            atomic_write_json(path, data)
            self.assertEqual(load_manifest(path), data)

    def test_validates_complete_manifest(self) -> None:
        validate_manifest({
            "schemaVersion": 1,
            "latest": "1.0.0",
            "game": 550,
            "mods": [{
                "id": "123",
                "files": [{
                    "filename": "123-1-abcdef.vpk",
                    "size": 4,
                    "sha256": "a" * 64,
                    "url": "https://github.com/owner/repo/releases/download/v1.0.0/123-1-abcdef.vpk",
                }],
            }],
        })


if __name__ == "__main__":
    unittest.main()
