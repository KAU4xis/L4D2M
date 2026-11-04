import struct
import tempfile
import unittest
from pathlib import Path

from scripts.downloader import VPK_SIGNATURE, is_vpk


class DownloaderTests(unittest.TestCase):
    def test_recognizes_vpk_with_bin_extension(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "legacy.bin"
            path.write_bytes(struct.pack("<I", VPK_SIGNATURE) + b"payload")
            self.assertTrue(is_vpk(path))

    def test_rejects_arbitrary_binary(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "legacy.bin"
            path.write_bytes(b"not a vpk")
            self.assertFalse(is_vpk(path))


if __name__ == "__main__":
    unittest.main()
