#!/usr/bin/env python3
from __future__ import annotations

import argparse
import re
import struct
import sys
from pathlib import Path
from typing import BinaryIO, Iterable


HANGUL_RE = re.compile(r"[가-힣]")


def _normalize_tm_key(text: str) -> str:
    return (text or "").strip().replace("\r\n", "\n").replace("\r", "\n").lower()


def _read_exact(f: BinaryIO, n: int) -> bytes:
    b = f.read(n)
    if len(b) != n:
        raise EOFError(f"expected {n} bytes, got {len(b)}")
    return b


def _decode_bethesda_string(raw: bytes) -> str:
    # These files are typically UTF-8, but some official assets may contain legacy encodings.
    try:
        return raw.decode("utf-8")
    except UnicodeDecodeError:
        return raw.decode("cp1252", errors="replace")


def _parse_strings_file_bytes(file_name: str, data: bytes) -> dict[int, str]:
    lower = file_name.lower()
    is_sized = lower.endswith(".dlstrings") or lower.endswith(".ilstrings")
    if len(data) < 8:
        return {}

    count, _data_size = struct.unpack_from("<II", data, 0)
    dir_off = 8
    data_off = 8 + count * 8
    out: dict[int, str] = {}

    for i in range(count):
        sid, off = struct.unpack_from("<II", data, dir_off + i * 8)
        start = data_off + off
        if start < 0 or start >= len(data):
            continue

        if is_sized:
            if start + 4 > len(data):
                continue
            length = struct.unpack_from("<I", data, start)[0]
            start += 4
            end = min(len(data), start + max(0, length - 1))  # exclude NULL
            raw = data[start:end]
            out[sid] = _decode_bethesda_string(raw)
            continue

        # .strings: NULL-terminated
        try:
            end = data.index(b"\x00", start)
        except ValueError:
            continue
        raw = data[start:end]
        out[sid] = _decode_bethesda_string(raw)

    return out


def _iter_strings_files(root: Path, source_locale: str | None) -> Iterable[Path]:
    for p in sorted(root.rglob("*")):
        if not p.is_file():
            continue
        low = p.name.lower()
        if not low.endswith((".strings", ".dlstrings", ".ilstrings")):
            continue
        if source_locale:
            suffix = f"_{source_locale.lower()}."
            if suffix not in low:
                continue
        yield p


def _should_keep_pair(source: str, target: str, include_long: bool) -> bool:
    if not source or not target:
        return False
    if not HANGUL_RE.search(target):
        return False
    if source == target:
        return False
    if not include_long:
        if "\n" in source or "\n" in target:
            return False
        if len(source) > 120 or len(target) > 200:
            return False
    return True


def _write_tsv(path: Path, pairs: list[tuple[str, str]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="\n") as f:
        f.write("Source\tTarget\n")
        for src, dst in pairs:
            # Basic TSV escaping: replace tabs/newlines.
            src = src.replace("\t", " ").replace("\r", " ").replace("\n", " ").strip()
            dst = dst.replace("\t", " ").replace("\r", " ").replace("\n", " ").strip()
            if not src or not dst:
                continue
            f.write(src)
            f.write("\t")
            f.write(dst)
            f.write("\n")


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(
        description="Build Source->Target translation-memory TSV by matching Bethesda *.STRINGS files (by filename and string ID)."
    )
    ap.add_argument("--source-root", required=True, help="Root directory containing the SOURCE language STRINGS files.")
    ap.add_argument("--target-root", required=True, help="Root directory containing the TARGET language STRINGS files.")
    ap.add_argument("--source-locale", default="en", help="Only include files whose name contains _<locale> (default: en).")
    ap.add_argument("--out", required=True, help="Output TSV path.")
    ap.add_argument(
        "--include-long",
        action="store_true",
        help="Include long/multiline entries (default filters them out).",
    )
    args = ap.parse_args(argv)

    source_root = Path(args.source_root).expanduser()
    target_root = Path(args.target_root).expanduser()
    out_path = Path(args.out).expanduser()
    source_locale = (args.source_locale or "").strip()

    if not source_root.exists():
        raise SystemExit(f"missing --source-root: {source_root}")
    if not target_root.exists():
        raise SystemExit(f"missing --target-root: {target_root}")

    target_by_name: dict[str, Path] = {}
    for p in _iter_strings_files(target_root, source_locale=None):
        key = p.name.lower()
        target_by_name.setdefault(key, p)

    pairs_by_key: dict[str, tuple[str, str]] = {}
    matched_files = 0
    missing_target_files = 0

    for src_path in _iter_strings_files(source_root, source_locale=source_locale):
        tgt_path = target_by_name.get(src_path.name.lower())
        if tgt_path is None:
            missing_target_files += 1
            continue

        try:
            src_bytes = src_path.read_bytes()
            tgt_bytes = tgt_path.read_bytes()
        except Exception as ex:
            print(f"[seed-tm] failed reading: {src_path} / {tgt_path}: {ex}", file=sys.stderr)
            continue

        src_map = _parse_strings_file_bytes(src_path.name, src_bytes)
        tgt_map = _parse_strings_file_bytes(tgt_path.name, tgt_bytes)
        if not src_map or not tgt_map:
            continue

        matched_files += 1
        for sid, src_text in src_map.items():
            tgt_text = tgt_map.get(sid)
            if tgt_text is None:
                continue

            if not _should_keep_pair(src_text, tgt_text, include_long=args.include_long):
                continue

            key = _normalize_tm_key(src_text)
            if not key or key in pairs_by_key:
                continue
            pairs_by_key[key] = (src_text, tgt_text)

    pairs = sorted(pairs_by_key.values(), key=lambda p: _normalize_tm_key(p[0]))
    _write_tsv(out_path, pairs)
    print(
        f"[seed-tm] matched_files={matched_files} missing_target_files={missing_target_files} "
        f"pairs={len(pairs)} out={out_path}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))

