#!/usr/bin/env python3
"""Compare xDump 4.1.5f display values with an explicitly supplied manifest.

This script never imports the application's parser or field registry and never
opens a plugin. Record identity and all signature occurrences come from xEdit's
dump. The manifest supplies requested keys and expected strings, not type rules.

xDump writes unescaped multiline strings, so its text format is not a general
serialization format. For a requested value we consume the expected number of
physical lines, compare every character, and require a following structural
boundary. CRLF/CR are normalized to LF; no other whitespace is normalized.
Ambiguous boundaries, missing fields, mismatches and changed EPFT=6 technical
strings fail the comparison. This is evidence for the requested fields, not a
claim that the application's selection of every translatable field is complete.

Source of the dump grammar (WriteElement/WriteContainer):
https://github.com/TES5Edit/TES5Edit/blob/xedit-4.1.5f/xDump.dpr
EPFD's Text child and EPFT discriminant:
https://github.com/TES5Edit/TES5Edit/blob/xedit-4.1.5f/Core/wbDefinitionsTES5.pas
"""

from __future__ import annotations

import argparse
from collections import Counter, deque
from dataclasses import dataclass
import hashlib
import json
from pathlib import Path
import re
import sys


HEADER = re.compile(r"^( *)Record Header(?: \[S\])?(?:: .*)?$")
SUBRECORD = re.compile(r"^( *)([A-Z0-9_]{4}) - (.*)$")
FORM_ID = re.compile(r"\[([0-9A-F]{8})\]")
KEY = re.compile(r"^([A-Z0-9_]{4})/([0-9A-F]{8})/(\d+)/([A-Z0-9_]{4})/(\d+)$")
TOPIC = re.compile(r"^( *)Topic: DIAL - Dialog Topic \[[0-9A-F]{8}\](?: .*)?$")
LIMIT = 30


def normalized(value: str) -> str:
    return value.replace("\r\n", "\n").replace("\r", "\n")


def digest(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def indentation(line: str) -> int:
    return len(line) - len(line.lstrip(" "))


class Lines:
    """At most four lookahead lines; hashes the exact input bytes while streaming."""

    def __init__(self, path: Path):
        self.file = path.open("rb")
        self.pending: deque[tuple[int, str]] = deque()
        self.number = 0
        self.sha256 = hashlib.sha256()
        self.byte_count = 0

    def _read(self) -> tuple[int, str] | None:
        raw = self.file.readline()
        if not raw:
            return None
        self.sha256.update(raw)
        self.byte_count += len(raw)
        self.number += 1
        text = raw.decode("utf-8-sig" if self.number == 1 else "utf-8")
        return self.number, text.removesuffix("\n").removesuffix("\r")

    def next(self) -> tuple[int, str] | None:
        return self.pending.popleft() if self.pending else self._read()

    def peek(self, count: int = 1) -> tuple[int, str] | None:
        while len(self.pending) < count:
            item = self._read()
            if item is None:
                return None
            self.pending.append(item)
        return self.pending[count - 1]


def structural_boundary(lines: Lines) -> bool:
    first = lines.peek()
    if first is None:
        return True
    text = first[1]
    if not text:
        return False
    if SUBRECORD.match(text) or HEADER.match(text) or text.lstrip().startswith("GRUP "):
        return True
    if " [S]: " in text:
        return True
    # xEdit inserts a synthetic containing Topic before an INFO Record Header.
    # It has no on-disk subrecord signature (dcfDontSave, SetSortOrder(-2)).
    # See wbImplementation.pas, TwbContainedIn.Create and wbContainedInDef[7]:
    # https://github.com/TES5Edit/TES5Edit/blob/xedit-4.1.5f/Core/wbImplementation.pas
    # Depending on whether DisplayName is empty, the preceding record-name line
    # is absent or exactly two spaces shallower. Require the complete prelude,
    # including the actual INFO signature, rather than accepting arbitrary text.
    for topic_position in (1, 2):
        topic_item = lines.peek(topic_position)
        if topic_item is None:
            continue
        topic = TOPIC.match(topic_item[1])
        if topic is None:
            continue
        topic_indent = len(topic[1])
        if topic_position == 2 and topic_indent != indentation(text) + 2:
            continue
        header_item = lines.peek(topic_position + 1)
        signature_item = lines.peek(topic_position + 2)
        if (
            header_item is not None
            and signature_item is not None
            and HEADER.match(header_item[1])
            and indentation(header_item[1]) == topic_indent
            and signature_item[1] == " " * (topic_indent + 2) + "Signature: INFO"
        ):
            return True
    # A displayed record name or a container with no summary is followed by a
    # child exactly two spaces deeper. Require a recognized structural child.
    second = lines.peek(2)
    return bool(
        second
        and indentation(second[1]) == indentation(text) + 2
        and (
            HEADER.match(second[1])
            or SUBRECORD.match(second[1])
            or " [S]: " in second[1]
        )
    )


@dataclass
class Observed:
    line: int
    value: str
    boundary_ok: bool


@dataclass
class Parsed:
    path: str
    sha256: str
    bytes: int
    records: dict[str, int]
    structure_sha256: str
    values: dict[str, Observed]
    technical: dict[str, dict]
    problems: list[dict]


def parse_dump(path: Path, expected: dict[str, str]) -> Parsed:
    lines = Lines(path)
    values: dict[str, Observed] = {}
    technical: dict[str, dict] = {}
    problems: list[dict] = []
    record_counts: Counter[str] = Counter()
    record_occurrences: Counter[tuple[str, str]] = Counter()
    sub_counts: Counter[str] = Counter()
    structure = hashlib.sha256()
    current: tuple[str, str, int] | None = None
    header_indent: int | None = None
    record_type: str | None = None
    epft: str | None = None
    entry_point: str | None = None
    pending_epfd: tuple[str, int, int, str | None, str | None] | None = None

    def capture(key: str, first: str, line_number: int) -> None:
        if key in values:
            problems.append({"kind": "duplicate_field", "key": key, "line": line_number})
            return
        text_lines = [first]
        for _ in range(expected[key].count("\n")):
            extra = lines.next()
            if extra is None:
                problems.append({"kind": "truncated_value", "key": key, "line": line_number})
                break
            text_lines.append(extra[1])
        values[key] = Observed(line_number, normalized("\n".join(text_lines)), structural_boundary(lines))

    try:
        while (item := lines.next()) is not None:
            line_number, text = item
            match = HEADER.match(text)
            if match:
                current = None
                header_indent = len(match[1])
                record_type = None
                epft = entry_point = None
                pending_epfd = None
                continue
            if current is None and header_indent is not None:
                prefix = " " * (header_indent + 2)
                if text.startswith(prefix + "Signature: "):
                    record_type = text[len(prefix + "Signature: ") :]
                elif text.startswith(prefix + "FormID: "):
                    form = FORM_ID.search(text)
                    if form and record_type and re.fullmatch(r"[A-Z0-9_]{4}", record_type):
                        identity = record_type, form[1]
                        occurrence = record_occurrences[identity]
                        record_occurrences[identity] += 1
                        current = (*identity, occurrence)
                        record_counts[record_type] += 1
                        sub_counts.clear()
                        structure.update(f"R|{record_type}|{form[1]}|{occurrence}\n".encode())
                continue
            if current is None:
                continue

            if current[0] == "PERK" and text.lstrip().startswith("Entry Point: "):
                entry_point = text.lstrip()[len("Entry Point: ") :]
            if pending_epfd is not None:
                key, parent_indent, parent_line, technical_type, technical_entry = pending_epfd
                if indentation(text) <= parent_indent:
                    pending_epfd = None
                elif text == " " * (parent_indent + 2) + "Text" or text.startswith(
                    " " * (parent_indent + 2) + "Text: "
                ):
                    value = text.partition(": ")[2]
                    if technical_type == "string":
                        technical[key] = {
                            "line": line_number,
                            "value": value,
                            "entry_point": technical_entry,
                            "epft": 6,
                        }
                    if key in expected:
                        capture(key, value, line_number)
                    pending_epfd = None
                    continue

            match = SUBRECORD.match(text)
            if not match:
                continue
            subtype, suffix = match[2], match[3]
            ordinal = sub_counts[subtype]
            sub_counts[subtype] += 1
            key = "/".join((*map(str, current), subtype, str(ordinal)))
            structure.update(f"S|{subtype}|{ordinal}\n".encode())
            title, separator, value = suffix.partition(": ")
            if current[0] == "PERK" and subtype == "EPFT":
                epft = value
            if current[0] == "PERK" and subtype == "EPFD":
                pending_epfd = key, len(match[1]), line_number, epft, entry_point
            if key not in expected:
                continue
            if title.endswith(" [S]"):
                if not (current[0] == "PERK" and subtype == "EPFD"):
                    problems.append({"kind": "unexpected_container", "key": key, "line": line_number})
                continue
            if separator:
                capture(key, value, line_number)
            elif expected[key] == "":
                capture(key, "", line_number)
            elif not (current[0] == "PERK" and subtype == "EPFD"):
                problems.append({"kind": "missing_scalar_value", "key": key, "line": line_number})
    finally:
        lines.file.close()
    return Parsed(
        str(path.resolve()), lines.sha256.hexdigest(), lines.byte_count,
        dict(sorted(record_counts.items())), structure.hexdigest(), values, technical, problems,
    )


def describe_mismatch(key: str, expected: str, observed: Observed) -> dict:
    first_difference = next(
        (i for i, (left, right) in enumerate(zip(expected, observed.value)) if left != right),
        min(len(expected), len(observed.value)),
    )
    start = max(0, first_difference - 35)
    return {
        "key": key, "line": observed.line, "first_difference": first_difference,
        "expected_length": len(expected), "actual_length": len(observed.value),
        "expected_context": expected[start:first_difference + 85],
        "actual_context": observed.value[start:first_difference + 85],
    }


def compare_expected(parsed: Parsed, expected: dict[str, str]) -> dict:
    missing = sorted(expected.keys() - parsed.values.keys())
    mismatches, boundaries = [], []
    matches: Counter[str] = Counter()
    for key, observed in parsed.values.items():
        if observed.value != expected[key]:
            mismatches.append(describe_mismatch(key, expected[key], observed))
        else:
            parts = key.split("/")
            matches[f"{parts[0]}:{parts[3]}"] += 1
        if not observed.boundary_ok:
            boundaries.append({"key": key, "line": observed.line})
    return {
        "path": parsed.path, "sha256": parsed.sha256, "bytes": parsed.bytes,
        "records": parsed.records, "structure_sha256": parsed.structure_sha256,
        "expected_count": len(expected), "observed_count": len(parsed.values),
        "matched_count": sum(matches.values()), "matched_by_type": dict(sorted(matches.items())),
        "missing_count": len(missing), "missing_examples": missing[:LIMIT],
        "mismatch_count": len(mismatches), "mismatch_examples": mismatches[:LIMIT],
        "ambiguous_boundary_count": len(boundaries), "ambiguous_boundary_examples": boundaries[:LIMIT],
        "parse_problem_count": len(parsed.problems), "parse_problem_examples": parsed.problems[:LIMIT],
    }


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--manifest", type=Path, required=True, help="PluginReader JSON containing Fields")
    ap.add_argument("--edits", type=Path, required=True, help="Key -> translated text JSON dictionary")
    ap.add_argument("--original-dump", type=Path, required=True)
    ap.add_argument("--edited-dump", type=Path, required=True)
    ap.add_argument("--report", type=Path, help="Optional JSON report path; no plugin is modified")
    ap.add_argument("--evidence", type=Path, help="Optional per-field line/hash evidence JSONL path")
    args = ap.parse_args()
    inputs = {str(path.resolve()).casefold() for path in (
        args.manifest, args.edits, args.original_dump, args.edited_dump
    )}
    outputs = [path for path in (args.report, args.evidence) if path is not None]
    if any(str(path.resolve()).casefold() in inputs for path in outputs):
        raise ValueError("An output path must not overwrite an input")
    if len({str(path.resolve()).casefold() for path in outputs}) != len(outputs):
        raise ValueError("Report and evidence paths must be different")
    if args.report and args.report.suffix.lower() != ".json":
        raise ValueError("Report output must use a .json extension")
    if args.evidence and args.evidence.suffix.lower() != ".jsonl":
        raise ValueError("Evidence output must use a .jsonl extension")
    manifest = json.loads(args.manifest.read_text(encoding="utf-8-sig"))
    edits = json.loads(args.edits.read_text(encoding="utf-8-sig"))
    fields = manifest["Fields"]
    expected_original = {}
    for field in fields:
        key = field["Key"]
        match = KEY.fullmatch(key)
        if not match or key in expected_original:
            raise ValueError(f"Invalid or duplicate field key: {key}")
        if (match[1], match[2], match[4]) != (
            field["RecordType"], f"{field['FormId']:08X}", field["SubrecordType"]
        ):
            raise ValueError(f"Inconsistent field identity: {key}")
        expected_original[key] = normalized(field["SourceText"])
    if not isinstance(edits, dict) or any(not isinstance(value, str) for value in edits.values()):
        raise ValueError("Edits must be a dictionary of string values")
    if edits.keys() - expected_original.keys():
        raise ValueError(f"Edits outside the manifest: {sorted(edits.keys() - expected_original.keys())[:LIMIT]}")
    expected_edited = {key: normalized(edits.get(key, text)) for key, text in expected_original.items()}
    original = parse_dump(args.original_dump, expected_original)
    edited = parse_dump(args.edited_dump, expected_edited)
    first, second = compare_expected(original, expected_original), compare_expected(edited, expected_edited)
    technical_changes = []
    for key in sorted(original.technical.keys() | edited.technical.keys()):
        before, after = original.technical.get(key), edited.technical.get(key)
        if before is None or after is None or before["value"] != after["value"] or before["entry_point"] != after["entry_point"]:
            technical_changes.append({"key": key, "original": before, "edited": after})
    structure_matches = original.structure_sha256 == edited.structure_sha256
    report = {
        "tool": "verify_xedit_dump_fields", "schema_version": 1,
        "method": "xDump record identity and all subtype occurrences; manifest-guided exact text assertions",
        "normalization": "CRLF and CR to LF only",
        "scope": "Requested manifest fields; does not certify complete translatable-field selection or non-text byte preservation",
        "manifest_sha256": hashlib.sha256(args.manifest.read_bytes()).hexdigest(),
        "edits_sha256": hashlib.sha256(args.edits.read_bytes()).hexdigest(),
        "requested_edits": len(edits), "original": first, "edited": second,
        "record_and_subrecord_structure_matches": structure_matches,
        "epft6_technical_strings": {
            "original_count": len(original.technical), "edited_count": len(edited.technical),
            "changed_count": len(technical_changes), "changed_examples": technical_changes[:LIMIT],
            "unchanged_keys": sorted(key for key in original.technical if key in edited.technical and original.technical[key]["value"] == edited.technical[key]["value"]),
        },
    }
    problem_counts = ("missing_count", "mismatch_count", "ambiguous_boundary_count", "parse_problem_count")
    report["passed"] = (
        bool(expected_original) and structure_matches and not technical_changes
        and all(part[name] == 0 for part in (first, second) for name in problem_counts)
    )
    if args.evidence:
        with args.evidence.open("w", encoding="utf-8", newline="\n") as output:
            for key in sorted(expected_original):
                row = {"key": key}
                for name, parsed, expected in (("original", original, expected_original), ("edited", edited, expected_edited)):
                    observed = parsed.values.get(key)
                    row[name] = None if observed is None else {
                        "line": observed.line, "value_sha256": digest(observed.value),
                        "expected_sha256": digest(expected[key]), "matches": observed.value == expected[key],
                        "boundary_ok": observed.boundary_ok,
                    }
                output.write(json.dumps(row, ensure_ascii=False) + "\n")
    formatted = json.dumps(report, ensure_ascii=False, indent=2)
    if args.report:
        args.report.write_text(formatted + "\n", encoding="utf-8")
    print(formatted)
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
