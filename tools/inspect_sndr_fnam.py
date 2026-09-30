"""Summarize SNDR:FNAM values in Skyrim master files.

Usage: python tools/inspect_sndr_fnam.py Skyrim.esm [Update.esm ...]

Read-only. Reports each FNAM value with its record form version, loop bit
(0x10) and whether the EditorID marks a looping descriptor (LPSD).
"""
import collections
import struct
import sys
import zlib


def sound_descriptors(path):
    data = open(path, 'rb').read()
    found = []

    def walk(offset, end):
        while offset < end:
            signature = data[offset:offset + 4]
            size = struct.unpack_from('<I', data, offset + 4)[0]
            if signature == b'GRUP':
                label = data[offset + 8:offset + 12]
                group_type = struct.unpack_from('<i', data, offset + 12)[0]
                if group_type != 0 or label == b'SNDR':
                    walk(offset + 24, offset + size)
                offset += size
                continue
            if signature == b'SNDR':
                flags = struct.unpack_from('<I', data, offset + 8)[0]
                form_version = struct.unpack_from('<H', data, offset + 20)[0]
                body = data[offset + 24:offset + 24 + size]
                if flags & 0x40000:
                    body = zlib.decompress(body[4:])
                found.append((form_version, dict(subrecords(body))))
            offset += 24 + size

    walk(24 + struct.unpack_from('<I', data, 4)[0], len(data))
    return found


def subrecords(body):
    index, extended = 0, None
    while index < len(body):
        signature = body[index:index + 4].decode('ascii', 'replace')
        size = struct.unpack_from('<H', body, index + 4)[0]
        if signature == 'XXXX':
            extended = struct.unpack_from('<I', body, index + 6)[0]
            index += 6 + size
            continue
        if extended is not None:
            size, extended = extended, None
        yield signature, body[index + 6:index + 6 + size]
        index += 6 + size


def main(paths):
    for path in paths:
        records = sound_descriptors(path)
        values = collections.Counter()
        for form_version, fields in records:
            if len(fields.get('FNAM', b'')) != 4:
                continue
            value = struct.unpack('<I', fields['FNAM'])[0]
            edid = fields.get('EDID', b'').rstrip(b'\0').decode('ascii', 'replace')
            values[(value, form_version < 35, edid.upper().endswith('LPSD'))] += 1
        print(f'{path}: SNDR={len(records)} with FNAM={sum(values.values())}')
        for (value, below_35, looping), count in sorted(values.items()):
            print(f'  FNAM={value:<3} 0x{value:02x} loop-bit={bool(value & 0x10)!s:<5} '
                  f'form<35={below_35!s:<5} LPSD={looping!s:<5} x{count}')


if __name__ == '__main__':
    main(sys.argv[1:])
