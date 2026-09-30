import copy
import json
import tempfile
import unittest
from pathlib import Path
import compare


def manifest(source="Source", dest=False):
    return {"Info": {"InputPath": "C:\\source\\EldenSkyrim.esp", "Masters": ["Skyrim.esm"], "Sha256": "test"},
            "Fields": [{"Key": "SPEL/01000801/0/FULL/0", "FormId": 0x01000801,
                        "Rec": "SPEL:FULL", "EditorId": "Skill", "SourceText": "기존 번역" if dest else source}]}


class ComparisonIdentityTests(unittest.TestCase):
    def test_source_local_master_slots_can_move(self):
        old = manifest()
        old["Info"]["Masters"] = ["Skyrim.esm", "Update.esm"]
        old["Fields"][0]["FormId"] = 0x01000801
        latest = copy.deepcopy(old)
        latest["Info"]["Masters"] = ["Update.esm", "Skyrim.esm"]
        latest["Fields"][0]["FormId"] = 0x00000801
        self.assertEqual(compare.identity(old, old["Fields"][0]), compare.identity(latest, latest["Fields"][0]))

    def test_different_record_owners_do_not_match(self):
        old, latest = manifest(), manifest()
        latest["Info"]["InputPath"] = "C:\\source\\Other.esp"
        self.assertNotEqual(compare.identity(old, old["Fields"][0]), compare.identity(latest, latest["Fields"][0]))

    def test_changed_english_is_excluded_from_quality_subset(self):
        result = self.run_comparison(manifest("Updated source"))
        self.assertEqual(result["Rows"][0]["Change"], "원문 변경")
        self.assertFalse(result["Rows"][0]["QualityComparable"])

    def test_unchanged_english_can_compare_different_translations(self):
        result = self.run_comparison(manifest())
        self.assertTrue(result["Rows"][0]["QualityComparable"])
        self.assertEqual(result["Summaries"][0]["ComparableDifferentTranslations"], 1)

    def test_reused_form_with_changed_edid_is_excluded(self):
        latest = manifest()
        latest["Fields"][0]["EditorId"] = "DifferentSkill"
        result = self.run_comparison(latest)
        self.assertEqual(result["Rows"][0]["Change"], "레코드 식별자 변경")
        self.assertFalse(result["Rows"][0]["QualityComparable"])

    def test_translation_from_another_source_is_rejected(self):
        with self.assertRaises(ValueError):
            self.run_comparison(manifest(), wrong_source=True)

    def run_comparison(self, latest, wrong_source=False):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            sources = {"old": manifest(), "baseline": manifest(dest=True), "latest": latest,
                       "rows": [{"Key": latest["Fields"][0]["Key"],
                                 "SourceText": "Wrong" if wrong_source else latest["Fields"][0]["SourceText"],
                                 "DestText": "새 번역", "Status": "Done"}]}
            for name, value in sources.items():
                (root / (name + ".json")).write_text(json.dumps(value), encoding="utf-8")
            return compare.compare([{"name": "EldenSkyrim.esp", "old_source": str(root / "old.json"),
                                     "baseline": str(root / "baseline.json"), "latest_source": str(root / "latest.json"),
                                     "translated_rows": str(root / "rows.json")}])


if __name__ == "__main__":
    unittest.main()
