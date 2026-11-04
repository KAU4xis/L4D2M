import unittest

from scripts.comparator import compare, next_version


class ComparatorTests(unittest.TestCase):
    def test_detects_all_change_types(self) -> None:
        previous = {"mods": [{"id": "1", "timeUpdated": 10}, {"id": "2", "timeUpdated": 20}, {"id": "3", "timeUpdated": 30}]}
        changes = compare(previous, {"1": 10, "2": 21, "4": 40})
        self.assertEqual(changes.unchanged, ("1",))
        self.assertEqual(changes.updated, ("2",))
        self.assertEqual(changes.removed, ("3",))
        self.assertEqual(changes.added, ("4",))

    def test_version_rules(self) -> None:
        initial = compare({"mods": []}, {"1": 1})
        self.assertEqual(next_version(None, initial), "1.0.0")
        self.assertEqual(next_version("1.2.3", initial), "1.3.0")
        update = compare({"mods": [{"id": "1", "timeUpdated": 1}]}, {"1": 2})
        self.assertEqual(next_version("1.2.3", update), "1.2.4")

