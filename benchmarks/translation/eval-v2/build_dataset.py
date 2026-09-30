"""Build the fixed 720-row quality evaluation set (eval-v2).

Offline and read-only. The output contains game/mod text and community
translations, so write it under the gitignored artifacts/ folder.

python benchmarks/translation/eval-v2/build_dataset.py \
  --elden-dir artifacts/eldenrim-comparison-20260930 \
  --lotd-dataset benchmarks/translation/lotd-v1/dataset.json \
  --lotd-xml LegacyoftheDragonborn_english_korean.xml \
  --vanilla-fields VALIDATE-REPORT-OF-SKYRIM-ESM.json \
  --korean-strings Skyrim_Latest_Kor/01-main/strings   --elden-project-db "%LOCALAPPDATA%/XTranslatorAi/Projects/EldenSkyrim.english-korean.2e663c1d2e.sqlite" \
  --out artifacts/quality-eval-v2/dataset.json
"""
import argparse
import collections
import hashlib
import json
import random
import re
import sqlite3
import struct
import xml.etree.ElementTree as ET
from pathlib import Path

SEED = 20260930
HANGUL = re.compile('[가-힣]')

ELDEN_QUOTAS = {
    'MGEF:FULL': 50, 'SPEL:FULL': 45, 'PERK:FULL': 30, 'SPEL:DESC': 40, 'PERK:DESC': 35,
    'ARMO:DESC': 25, 'ENCH:FULL': 15, 'PROJ:FULL': 10, 'MESG:DESC': 15, 'MESG:FULL': 10,
    'EXPL:FULL': 8, 'BOOK:CNAM': 8, 'BOOK:DESC': 9,
}
VANILLA_NAME_RECS = ['WEAP:FULL', 'ARMO:FULL', 'NPC_:FULL', 'MISC:FULL', 'ALCH:FULL',
                     'ACTI:FULL', 'LCTN:FULL', 'CELL:FULL', 'SPEL:FULL', 'PERK:FULL']
VANILLA_QUOTAS = {
    'INFO:NAM1': 70, 'DIAL:FULL': 20, 'QUST:NNAM': 20, 'QUST:CNAM': 20, 'QUST:FULL': 10,
    'BOOK:DESC': 30, 'BOOK:FULL': 15, 'MGEF:DNAM': 25, 'MESG:DESC': 15, 'INFO:RNAM': 10,
    'GMST:DATA': 10, 'names': 55,
}
BOOK_MIN, BOOK_MAX = 200, 6000


def sha256(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def read_strings(path, length_prefixed):
    data = Path(path).read_bytes()
    count, _ = struct.unpack_from('<II', data, 0)
    base = 8 + 8 * count
    table = {}
    for index in range(count):
        string_id, offset = struct.unpack_from('<II', data, 8 + 8 * index)
        position = base + offset
        if length_prefixed:
            length = struct.unpack_from('<I', data, position)[0]
            raw = data[position + 4:position + 4 + length].rstrip(b'\0')
        else:
            raw = data[position:data.index(b'\0', position)]
        table[string_id] = raw.decode('utf-8', 'replace')
    return table


def take(rng, pool, count, seen):
    """Pick up to count rows with unique source text, deterministic for a seed."""
    candidates = [row for row in pool if row['source'] not in seen]
    rng.shuffle(candidates)
    picked = []
    for row in candidates:
        if len(picked) == count:
            break
        if row['source'] in seen:
            continue
        seen.add(row['source'])
        picked.append(row)
    return picked


def elden_rows(elden_dir, rng):
    load = lambda name: {f['Key']: f for f in json.loads((elden_dir / name).read_text(encoding='utf-8'))['Fields']}
    latest, old, korean = (load('EldenSkyrim.latest-base-3.7.5.json'), load('EldenSkyrim.old-original.json'),
                           load('EldenSkyrim.baseline.json'))
    by_rec = collections.defaultdict(list)
    for key, field in latest.items():
        # The installed Korean release translates 3.7.2; keep rows whose English is unchanged.
        if key not in old or old[key]['SourceText'] != field['SourceText'] or key not in korean:
            continue
        reference = korean[key]['SourceText']
        if not HANGUL.search(reference):
            continue
        by_rec[field['Rec']].append({'group': 'A-elden', 'rec': field['Rec'], 'edid': field['EditorId'],
                                     'source': field['SourceText'], 'reference': reference, 'origin': key})
    seen, rows = set(), []
    for rec, quota in ELDEN_QUOTAS.items():
        rows += take(rng, by_rec.get(rec, []), quota, seen)
    leftovers = [row for rec in sorted(by_rec) for row in by_rec[rec]]
    rows += take(rng, leftovers, 300 - len(rows), seen)
    return rows


def lotd_rows(dataset_path, xml_path):
    titles = {}
    for node in ET.parse(xml_path).getroot().iter('String'):
        if node.findtext('REC') == 'BOOK:FULL':
            titles.setdefault(node.findtext('EDID') or '', node.findtext('Source') or '')
    cases = json.loads(Path(dataset_path).read_text(encoding='utf-8'))['cases']
    return [{'group': 'B-lotd', 'rec': case['record'], 'edid': case['edid'], 'source': case['source'],
             'reference': None, 'origin': case['id'], 'category': case['category'],
             'book_title': titles.get(case['edid']) if case['record'] == 'BOOK:DESC' else None}
            for case in cases]


def vanilla_rows(fields_path, strings_dir, rng):
    tables = {0: read_strings(strings_dir / 'Skyrim_english.STRINGS', False),
              1: read_strings(strings_dir / 'Skyrim_english.DLSTRINGS', True),
              2: read_strings(strings_dir / 'Skyrim_english.ILSTRINGS', True)}
    fields = json.loads(Path(fields_path).read_text(encoding='utf-8'))['Fields']
    titles = {}
    for field in fields:
        if field['Rec'] == 'BOOK:FULL':
            titles.setdefault(field['EditorId'], field['SourceText'])
    by_rec = collections.defaultdict(list)
    for field in fields:
        reference = tables.get(field['TableKind'], {}).get(field['StringId'])
        source = field['SourceText']
        if not reference or not HANGUL.search(reference) or reference == source:
            continue
        rec = field['Rec']
        if rec == 'BOOK:DESC' and not BOOK_MIN <= len(source) <= BOOK_MAX:
            continue
        bucket = 'names' if rec in VANILLA_NAME_RECS else rec
        by_rec[bucket].append({'group': 'C-vanilla', 'rec': rec, 'edid': field['EditorId'], 'source': source,
                               'reference': reference, 'origin': field['Key'],
                               'book_title': titles.get(field['EditorId']) if rec == 'BOOK:DESC' else None})
    seen, rows = set(), []
    for bucket, quota in VANILLA_QUOTAS.items():
        rows += take(rng, by_rec.get(bucket, []), quota, seen)
    return rows


def main():
    parser = argparse.ArgumentParser()
    for name in ('--elden-dir', '--lotd-dataset', '--lotd-xml', '--vanilla-fields', '--korean-strings',
                 '--elden-project-db', '--out'):
        parser.add_argument(name, required=True)
    args = parser.parse_args()
    rng = random.Random(SEED)
    elden_dir, strings_dir = Path(args.elden_dir), Path(args.korean_strings)
    rows = elden_rows(elden_dir, rng) + lotd_rows(args.lotd_dataset, args.lotd_xml) \
        + vanilla_rows(args.vanilla_fields, strings_dir, rng)
    for index, row in enumerate(rows, 1):
        row['id'] = f'E{index:04d}'
        row.setdefault('book_title', None)
    inputs = {name: sha256(path) for name, path in [
        ('elden_latest', elden_dir / 'EldenSkyrim.latest-base-3.7.5.json'),
        ('elden_old', elden_dir / 'EldenSkyrim.old-original.json'),
        ('elden_korean', elden_dir / 'EldenSkyrim.baseline.json'),
        ('lotd_dataset', args.lotd_dataset), ('lotd_xml', args.lotd_xml),
        ('vanilla_fields', args.vanilla_fields),
        ('korean_strings', strings_dir / 'Skyrim_english.STRINGS')]}
    stats = {group: {'rows': len(items), 'source_chars': sum(len(r['source']) for r in items),
                     'recs': dict(collections.Counter(r['rec'] for r in items).most_common())}
             for group, items in sorted(collections.defaultdict(list, {
                 g: [r for r in rows if r['group'] == g] for g in {r['group'] for r in rows}}).items())}
    # The Elden Rim group uses the user's reviewed project glossary, as the app would.
    connection = sqlite3.connect(f'file:{args.elden_project_db}?mode=ro', uri=True)
    project_glossaries = {'A-elden': [
        {'source': source, 'target': target, 'match_mode': match, 'force_mode': force, 'priority': priority}
        for source, target, match, force, priority in connection.execute(
            'SELECT SrcTerm, DstTerm, MatchMode, ForceMode, Priority FROM Glossary WHERE Enabled = 1 ORDER BY Id')]}
    connection.close()
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps({'version': 'eval-v2', 'seed': SEED, 'inputs_sha256': inputs, 'stats': stats,
                               'project_glossaries': project_glossaries,
                               'rows': rows}, ensure_ascii=False, indent=1), encoding='utf-8')
    print(json.dumps({'rows': len(rows), 'stats': stats}, ensure_ascii=False, indent=1))


if __name__ == '__main__':
    main()
