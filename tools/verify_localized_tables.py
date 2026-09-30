"""Independently compare localized Skyrim string-table bytes and intended edits.

Reads snapshots only; imports no application code and never writes input files.
The manifest identifies which IDs should change, but both tables are decoded here.
This does not prove that the manifest includes every display field in a plugin.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct


KINDS = ("STRINGS", "DLSTRINGS", "ILSTRINGS")


def require(condition, detail):
    if not condition:
        raise ValueError(detail)


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


def read_table(path, kind):
    data = path.read_bytes()
    require(len(data) >= 8, f"Short table header: {path}")
    count, size = struct.unpack_from("<II", data)
    start = 8 + count * 8
    require(start <= len(data) and start + size == len(data), f"Table size mismatch: {path}")
    entries = {}
    order = []
    for i in range(count):
        string_id, offset = struct.unpack_from("<II", data, 8 + 8 * i)
        require(string_id not in entries, f"Duplicate ID {string_id}: {path}")
        pos = start + offset
        require(start <= pos < len(data), f"Out-of-bounds ID {string_id}: {path}")
        if kind == "STRINGS":
            end = data.find(b"\0", pos)
            require(end >= pos, f"Unterminated ID {string_id}: {path}")
            raw = data[pos:end + 1]
            text_bytes = raw[:-1]
        else:
            require(pos + 4 <= len(data), f"Missing length for ID {string_id}: {path}")
            length = struct.unpack_from("<I", data, pos)[0]
            end = pos + 4 + length
            require(length >= 1 and end <= len(data), f"Invalid length for ID {string_id}: {path}")
            raw = data[pos:end]
            require(raw[-1] == 0, f"Missing terminator for ID {string_id}: {path}")
            text_bytes = raw[4:-1]
        require(b"\0" not in text_bytes, f"Embedded NUL for ID {string_id}: {path}")
        entries[string_id] = (text_bytes.decode("utf-8", errors="strict"), raw)
        order.append(string_id)
    return entries, order, hashlib.sha256(data).hexdigest().upper()


def verify(args):
    manifest_bytes = args.manifest.read_bytes()
    edits_bytes = args.edits.read_bytes()
    manifest = json.loads(manifest_bytes)
    edits = json.loads(edits_bytes)
    fields = manifest["Fields"]
    require(len({field["Key"] for field in fields}) == len(fields), "Duplicate manifest keys")
    require(set(edits) <= {field["Key"] for field in fields}, "Edits contain unknown field keys")
    expected_by_kind = {kind: {} for kind in KINDS}
    for field in fields:
        require(field["StringId"] is not None, "Nonlocalized field in localized manifest")
        kind = KINDS[field["TableKind"]]
        string_id = field["StringId"]
        value = (field["SourceText"], edits.get(field["Key"], field["SourceText"]))
        prior = expected_by_kind[kind].setdefault(string_id, value)
        require(prior == value, f"Conflicting expectation for {kind}/{string_id}")
    input_hash = sha(args.original_plugin)
    require(input_hash == manifest["Info"]["Sha256"], "Manifest/plugin input hash mismatch")
    output_hash = sha(args.edited_plugin)
    require(input_hash == output_hash, "Localized plugin binary changed")
    tables = []
    for kind in KINDS:
        name = f"{args.original_plugin.stem}_{args.language}.{kind}"
        source_path = args.original_strings / name
        target_path = args.edited_strings / name
        source, source_order, source_hash = read_table(source_path, kind)
        target, target_order, target_hash = read_table(target_path, kind)
        require(source.keys() == target.keys(), f"ID set changed: {kind}")
        expected = expected_by_kind[kind]
        require(expected.keys() <= source.keys(), f"Manifest references missing ID: {kind}")
        changed = 0
        untouched = 0
        for string_id, (source_text, source_raw) in source.items():
            target_text, target_raw = target[string_id]
            if string_id in expected:
                before, after = expected[string_id]
                require(source_text == before, f"Source mismatch: {kind}/{string_id}")
                require(target_text == after, f"Edited text mismatch: {kind}/{string_id}")
                if before != after:
                    changed += 1
                    continue
            require(source_raw == target_raw, f"Unedited entry bytes changed: {kind}/{string_id}")
            untouched += 1
        require(sha(source_path) == source_hash, f"Input table changed during validation: {kind}")
        require(sha(target_path) == target_hash, f"Output table changed during validation: {kind}")
        tables.append({"Kind": kind, "Entries": len(source), "ReferencedIds": len(expected),
                       "ChangedIds": changed, "UneditedEntryBytesPreserved": untouched,
                       "IdSetPreserved": True, "IdOrderPreserved": source_order == target_order,
                       "SourceSha256": source_hash, "OutputSha256": target_hash})
    require(sha(args.original_plugin) == input_hash, "Input plugin changed during validation")
    require(sha(args.edited_plugin) == output_hash, "Output plugin changed during validation")
    return {"Passed": True, "Scope": "UTF-8 table values, ID sets, unedited entry bytes, and unchanged plugin binary; not display-field coverage or game behavior",
            "ManifestSha256": hashlib.sha256(manifest_bytes).hexdigest().upper(),
            "EditsSha256": hashlib.sha256(edits_bytes).hexdigest().upper(),
            "PluginSha256": input_hash, "PluginByteIdentical": True,
            "FieldCount": len(fields), "RequestedEdits": len(edits), "Tables": tables}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for option in ("manifest", "edits", "original-plugin", "edited-plugin", "original-strings", "edited-strings", "report"):
        parser.add_argument("--" + option, required=True, type=Path)
    parser.add_argument("--language", default="english")
    args = parser.parse_args()
    # Reserve a new report before work, preventing accidental output overwrite.
    with args.report.open("x", encoding="utf-8") as output:
        try:
            result = verify(args)
        except Exception as error:
            result = {"Passed": False, "Error": str(error)}
        json.dump(result, output, ensure_ascii=False, indent=2)
        output.write("\n")
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result["Passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
