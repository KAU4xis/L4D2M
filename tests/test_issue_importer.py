import unittest

from scripts.issue_importer import IssueImportError, extract_issue_ids, validate_authorization


class IssueImporterTests(unittest.TestCase):
    def test_extracts_and_deduplicates_batch(self) -> None:
        body = """### Links dos mods

https://steamcommunity.com/sharedfiles/filedetails/?id=3547441271
1234567890
https://steamcommunity.com/sharedfiles/filedetails/?id=3547441271

### Autorização de redistribuição

- [x] Confirmo
"""
        self.assertEqual(extract_issue_ids(body), ["3547441271", "1234567890"])

    def test_rejects_missing_section(self) -> None:
        with self.assertRaises(IssueImportError):
            extract_issue_ids("### Outra coisa\n123")

    def test_requires_checked_authorization(self) -> None:
        checked = "### Autorização de redistribuição\n\n- [x] Confirmo que estes itens possuem autorização para redistribuição neste modpack."
        validate_authorization(checked)
        with self.assertRaises(IssueImportError):
            validate_authorization(checked.replace("[x]", "[ ]"))


if __name__ == "__main__":
    unittest.main()
