"""Offline xTranslator corpus composition; no model/API calls or input changes."""

import argparse
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET


def summarize(rows):
    sources = [row[2] for row in rows]
    unique = set(sources)
    chars = sum(map(len, sources))
    unique_chars = sum(map(len, unique))
    return {
        "rows": len(rows),
        "source_chars": chars,
        "unique_sources": len(unique),
        "unique_source_chars": unique_chars,
        "duplicate_extra_rows": len(rows) - len(unique),
        "duplicate_extra_chars": chars - unique_chars,
        "max_source_chars": max(map(len, sources), default=0),
    }


def analyze(path):
    payload = path.read_bytes()
    root = ET.fromstring(payload)
    rows = [(s.findtext("REC", ""), s.findtext("EDID", ""),
             s.findtext("Source", ""), s.findtext("Dest", ""))
            for s in root.findall(".//String")]
    total = summarize(rows)
    recs = sorted({r[0] for r in rows})
    groups = {rec: summarize([r for r in rows if r[0] == rec]) for rec in recs}
    for group in groups.values():
        group["source_char_share_pct"] = round(100 * group["source_chars"] / total["source_chars"], 4) if total["source_chars"] else 0
    book = [r for r in rows if r[0] == "BOOK:DESC"]
    contextual_keys = {(r[0], r[1], r[2]) for r in rows}
    dialogue = [r for r in rows if r[0].split(":")[0] in ("INFO", "DIAL")]
    return {
        "input_file": path.name,
        "sha256": hashlib.sha256(payload).hexdigest(),
        "method": "ElementTree XML parsing; Unicode code points after XML newline normalization; tags included. Exact-source duplicates ignore context and are an optimistic bound, not app dedup/TM hits or token savings.",
        "total": total,
        "all_dest_equal_source": all(r[2] == r[3] for r in rows),
        "by_rec": groups,
        "books_source_over_8000_chars": sum(len(r[2]) > 8000 for r in book),
        "books_source_over_15000_chars": sum(len(r[2]) > 15000 for r in book),
        "rec_edid_source_duplicate_extra_rows": len(rows) - len(contextual_keys),
        "rec_edid_source_duplicate_extra_chars": total["source_chars"] - sum(len(k[2]) for k in contextual_keys),
        "dialogue_rows": len(dialogue),
        "dialogue_rows_without_edid": sum(not r[1].strip() for r in dialogue),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("xml", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    report = json.dumps(analyze(args.xml), ensure_ascii=False, indent=2) + "\n"
    if args.output:
        if args.output.resolve() == args.xml.resolve():
            parser.error("Output must differ from the input XML.")
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(report, encoding="utf-8")
    else:
        print(report, end="")


if __name__ == "__main__":
    main()
