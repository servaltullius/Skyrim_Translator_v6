#!/usr/bin/env python3
from __future__ import annotations

import argparse
import re
import struct
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import BinaryIO, Iterable


HANGUL_RE = re.compile(r"[가-힣]")


def _normalize_tm_key(text: str) -> str:
    return (text or "").strip().replace("\r\n", "\n").replace("\r", "\n").lower()


@dataclass(frozen=True)
class BsaEntry:
    offset: int
    size: int
    is_compressed: bool


def _read_exact(f: BinaryIO, n: int) -> bytes:
    b = f.read(n)
    if len(b) != n:
        raise EOFError(f"expected {n} bytes, got {len(b)}")
    return b


def _read_u8(f: BinaryIO) -> int:
    return struct.unpack("<B", _read_exact(f, 1))[0]


def _read_u16(f: BinaryIO) -> int:
    return struct.unpack("<H", _read_exact(f, 2))[0]


def _read_u32(f: BinaryIO) -> int:
    return struct.unpack("<I", _read_exact(f, 4))[0]


def _read_u64(f: BinaryIO) -> int:
    return struct.unpack("<Q", _read_exact(f, 8))[0]


def _read_bzstring(f: BinaryIO) -> str:
    # byte-length prefixed string that includes a trailing NULL in the payload
    n = _read_u8(f)
    raw = _read_exact(f, n)
    if raw.endswith(b"\x00"):
        raw = raw[:-1]
    return raw.decode("utf-8", errors="replace")


def _parse_bsa_strings_index(bsa_path: Path) -> dict[str, BsaEntry]:
    """
    Parse a Skyrim SE/AE BSA (v104/v105) and return a map of
    `strings/<file>.<ext>` -> (offset,size,is_compressed).
    Only includes *.strings/*.dlstrings/*.ilstrings entries.
    """
    out: dict[str, BsaEntry] = {}
    with bsa_path.open("rb") as f:
        file_id = _read_exact(f, 4)
        if file_id != b"BSA\x00":
            raise ValueError(f"not a BSA file: {bsa_path}")

        version = _read_u32(f)
        _offset = _read_u32(f)
        archive_flags = _read_u32(f)
        folder_count = _read_u32(f)
        file_count = _read_u32(f)
        _total_folder_name_len = _read_u32(f)
        total_file_name_len = _read_u32(f)
        _file_flags = _read_u16(f)
        _padding = _read_u16(f)

        if version not in (104, 105):
            raise ValueError(f"unsupported BSA version: {version} ({bsa_path})")

        include_folder_names = (archive_flags & 0x1) != 0
        include_file_names = (archive_flags & 0x2) != 0
        include_names_in_data = (archive_flags & 0x100) != 0

        folder_rec_size = 24 if version == 105 else 16
        folder_records: list[tuple[int, int, int]] = []
        for _ in range(folder_count):
            _name_hash = _read_u64(f)
            count = _read_u32(f)
            if version == 105:
                _pad1 = _read_u32(f)
            folder_offset = _read_u32(f)
            if version == 105:
                _pad2 = _read_u32(f)
            folder_records.append((_name_hash, count, folder_offset))

        # File record blocks
        file_records: list[tuple[str, int, int, int]] = []  # folder, nameHash, size, offset
        folder_names: list[str] = []
        if not include_folder_names:
            raise ValueError(f"BSA missing folder names (archiveFlags=0x{archive_flags:x}): {bsa_path}")

        for _idx, (_name_hash, count, _folder_offset) in enumerate(folder_records):
            folder_name = _read_bzstring(f)
            folder_name = folder_name.replace("\\", "/").strip("/")
            folder_names.append(folder_name)

            for _ in range(count):
                name_hash = _read_u64(f)
                size = _read_u32(f)
                off = _read_u32(f)
                file_records.append((folder_name, name_hash, size, off))

        if not include_file_names:
            raise ValueError(f"BSA missing file names block (archiveFlags=0x{archive_flags:x}): {bsa_path}")

        file_names_block = _read_exact(f, total_file_name_len)
        file_names = [p.decode("utf-8", errors="replace") for p in file_names_block.split(b"\x00") if p]
        if len(file_names) != file_count or len(file_records) != file_count:
            raise ValueError(
                f"bad BSA index: file_count={file_count} names={len(file_names)} records={len(file_records)} ({bsa_path})"
            )

        for (folder_name, _name_hash, size_raw, off), fname in zip(file_records, file_names):
            rel = f"{folder_name}/{fname}".lower().replace("\\", "/")
            if not rel.startswith("strings/"):
                continue
            if not rel.endswith((".strings", ".dlstrings", ".ilstrings")):
                continue

            is_compressed = (size_raw & 0x40000000) != 0
            size = size_raw & 0x3FFFFFFF

            if include_names_in_data:
                # Not expected for Skyrim - Interface.bsa. Support would require skipping a bstring inside the payload.
                continue

            out[rel] = BsaEntry(offset=off, size=size, is_compressed=is_compressed)

    return out


def _read_bsa_file(bsa_path: Path, entry: BsaEntry) -> bytes:
    if entry.is_compressed:
        raise ValueError(f"compressed BSA entries are not supported yet: {bsa_path}")
    with bsa_path.open("rb") as f:
        f.seek(entry.offset)
        return _read_exact(f, entry.size)


def _decode_skyrim_string(raw: bytes) -> str:
    # Skyrim prefers UTF-8 and may fall back for invalid sequences. For our use, UTF-8 is the expected encoding.
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
            out[sid] = _decode_skyrim_string(raw)
            continue

        # .strings: NULL-terminated
        try:
            end = data.index(b"\x00", start)
        except ValueError:
            continue
        raw = data[start:end]
        out[sid] = _decode_skyrim_string(raw)

    return out


def _iter_community_strings_files(root: Path) -> Iterable[Path]:
    for p in sorted(root.rglob("*")):
        if not p.is_file():
            continue
        low = p.name.lower()
        if low.endswith((".strings", ".dlstrings", ".ilstrings")):
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
        description="Build English->Korean translation-memory TSV by matching community STRINGS files against official STRINGS inside Skyrim - Interface.bsa."
    )
    ap.add_argument("--interface-bsa", required=True, help="Path to Skyrim - Interface.bsa (official archive).")
    ap.add_argument("--community-root", required=True, help="Root directory containing the community STRINGS files.")
    ap.add_argument("--out", required=True, help="Output TSV path.")
    ap.add_argument(
        "--include-long",
        action="store_true",
        help="Include long/multiline entries (DL/IL strings can be huge). Default filters them out.",
    )
    args = ap.parse_args(argv)

    interface_bsa = Path(args.interface_bsa).expanduser()
    community_root = Path(args.community_root).expanduser()
    out_path = Path(args.out).expanduser()

    if not interface_bsa.exists():
        raise SystemExit(f"missing --interface-bsa: {interface_bsa}")
    if not community_root.exists():
        raise SystemExit(f"missing --community-root: {community_root}")

    bsa_index = _parse_bsa_strings_index(interface_bsa)
    bsa_by_filename = {Path(k).name: (k, v) for k, v in bsa_index.items()}

    pairs_by_key: dict[str, tuple[str, str]] = {}
    matched_files = 0
    for fpath in _iter_community_strings_files(community_root):
        name = fpath.name.lower()
        if name not in bsa_by_filename:
            continue

        bsa_rel, bsa_entry = bsa_by_filename[name]
        if bsa_entry.is_compressed:
            print(f"[seed-tm] skip (compressed): {bsa_rel}", file=sys.stderr)
            continue

        # Default: focus on .strings to avoid huge IL/DL tables unless explicitly requested.
        if not args.include_long and not name.endswith(".strings"):
            continue

        try:
            eng_bytes = _read_bsa_file(interface_bsa, bsa_entry)
        except Exception as ex:
            print(f"[seed-tm] failed reading BSA file {bsa_rel}: {ex}", file=sys.stderr)
            continue

        try:
            kor_bytes = fpath.read_bytes()
        except Exception as ex:
            print(f"[seed-tm] failed reading community file {fpath}: {ex}", file=sys.stderr)
            continue

        eng = _parse_strings_file_bytes(name, eng_bytes)
        kor = _parse_strings_file_bytes(name, kor_bytes)
        if not eng or not kor:
            continue

        matched_files += 1
        for sid, src in eng.items():
            dst = kor.get(sid)
            if dst is None:
                continue
            if not _should_keep_pair(src, dst, include_long=args.include_long):
                continue
            key = _normalize_tm_key(src)
            if not key or key in pairs_by_key:
                continue
            pairs_by_key[key] = (src, dst)

    pairs = sorted(pairs_by_key.values(), key=lambda p: _normalize_tm_key(p[0]))
    _write_tsv(out_path, pairs)
    print(f"[seed-tm] matched_files={matched_files} pairs={len(pairs)} out={out_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))

