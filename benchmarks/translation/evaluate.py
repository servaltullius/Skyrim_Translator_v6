"""Offline format checks only. Does not call any API or score translation meaning."""
from collections import Counter
import argparse
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parent
PROTECTED = re.compile(
    r"\r\n|\r|\n|<[^>]+>|\[pagebreak\]|\{\{[^{}]+\}\}|\{[^{}]+\}|"
    r"%(?:\d+\$)?[-+0-9.]*[A-Za-z]|%%|[+-]?\d+(?:\.\d+)?%",
    re.IGNORECASE,
)
STRUCTURE = re.compile(r"\r\n|\r|\n|\[pagebreak\]|</?(?:b|i|u|font|p|br)\b[^>]*>", re.IGNORECASE)


def check_case(case, text):
    errors = []
    expected = Counter(PROTECTED.findall(case["source"]))
    actual = Counter(PROTECTED.findall(text))
    if expected != actual:
        errors.append({"kind": "protected_tokens", "missing": dict(expected - actual), "extra": dict(actual - expected)})
    if STRUCTURE.findall(case["source"]) != STRUCTURE.findall(text):
        errors.append({"kind": "structural_token_order"})
    for literal, count in Counter(case["checks"]).items():
        if text.count(literal) < count:
            errors.append({"kind": "required_literal", "literal": literal, "expected_at_least": count})
    if case["id"] == "13" and not re.fullmatch(r"\s*하\s+하[.!]?\s*", text):
        errors.append({"kind": "repeated_laugh_review_required"})
    return errors


def evaluate(dataset, result):
    cases = {case["id"]: case for case in dataset["cases"]}
    rows = result["outputs"]
    ids = Counter(row["id"] for row in rows)
    schema_errors = []
    if ids != Counter(cases.keys()):
        schema_errors.append({"kind": "ids", "missing": sorted(set(cases) - set(ids)),
                              "extra": sorted(set(ids) - set(cases)),
                              "duplicate": [key for key, value in ids.items() if value > 1]})
    failures = []
    passed = 0
    for row in rows:
        if row["id"] not in cases:
            continue
        if not isinstance(row.get("text"), str):
            failures.append({"id": row["id"], "errors": [{"kind": "text_not_string"}]})
            continue
        errors = check_case(cases[row["id"]], row["text"])
        if errors:
            failures.append({"id": row["id"], "errors": errors})
        else:
            passed += 1
    return {"model": result["model"], "thinkingLevel": result["thinkingLevel"],
            "rows": len(rows), "format_passed": passed, "schema_errors": schema_errors,
            "failures": failures, "meaning_assessed": False}


def self_test():
    case = {"id": "test", "source": "<b>{player}</b>\n%s [pagebreak]", "checks": []}
    assert not check_case(case, "<b>{player}</b>\n%s [pagebreak]")
    assert check_case(case, "<b>{player}</b>\n%s")
    assert check_case(case, "</b>{player}<b>\n%s [pagebreak]")
    assert check_case(case, "<b>{player}{player}</b>\n%s [pagebreak]")
    assert check_case(case, "<b>{player}</b>\n%s [pagebreak]<i>")
    assert not check_case({"id": "test", "source": "<mag> for <dur>", "checks": []}, "<dur> 동안 <mag>")
    result = {"model": "fixture", "thinkingLevel": "none", "outputs": [{"id": "test", "text": case["source"]}]}
    assert not evaluate({"cases": [case]}, result)["schema_errors"]
    result["outputs"] *= 2
    assert evaluate({"cases": [case]}, result)["schema_errors"]


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--results", type=Path, default=ROOT / "results" / "2026-09-28")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    self_test()
    dataset = json.loads((ROOT / "ko-game-v1.json").read_text(encoding="utf-8-sig"))
    reports = [evaluate(dataset, json.loads(path.read_text(encoding="utf-8-sig")))
               for path in sorted(args.results.glob("*-run*.json"))]
    if not reports:
        parser.error("No benchmark runs found")
    output = json.dumps(reports, ensure_ascii=False, indent=2)
    if args.output:
        args.output.write_text(output + "\n", encoding="utf-8")
    print(output)
    raise SystemExit(1 if any(r["schema_errors"] or r["failures"] for r in reports) else 0)
