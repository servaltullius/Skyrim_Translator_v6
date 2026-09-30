"""Automatic, API-free metrics for eval-run outputs.

python benchmarks/translation/eval-v2/metrics.py DATASET RUN_DIR [RUN_DIR ...] --out metrics.json

chrF is a relative indicator against a community/older translation, not a
quality score. Vanilla references may also be in the model's training data.
"""
import argparse
import collections
import json
import re
from pathlib import Path

HANGUL = re.compile('[가-힣]')
TAG = re.compile(r'<[^>]*>|\[[^\]]*\]|\$\w+|%\w|\{[^}]*\}')
LATIN_WORD = re.compile(r'\b[A-Za-z]{3,}\b')
ALLOWED_LATIN = {'NPC', 'NPCs', 'MCO', 'DLC', 'HUD', 'MCM', 'SKSE', 'FPS', 'UI'}
AMBIGUOUS_PARTICLE = re.compile(r'\((?:을|를|이|가|은|는|과|와|으로|로|아|야)\)')
ELDEN_TERMS = [(re.compile(r'\bWeapon Arts?\b', re.I), '전기'), (re.compile(r'\bAsh(?:es)? of War\b', re.I), '전회')]


def chrf(hypothesis, reference, n=6, beta=2.0):
    hypothesis, reference = hypothesis.replace(' ', ''), reference.replace(' ', '')
    precisions, recalls = [], []
    for order in range(1, n + 1):
        hyp = collections.Counter(hypothesis[i:i + order] for i in range(len(hypothesis) - order + 1))
        ref = collections.Counter(reference[i:i + order] for i in range(len(reference) - order + 1))
        if not hyp or not ref:
            continue
        overlap = sum((hyp & ref).values())
        precisions.append(overlap / sum(hyp.values()))
        recalls.append(overlap / sum(ref.values()))
    if not precisions:
        return 0.0
    p, r = sum(precisions) / len(precisions), sum(recalls) / len(recalls)
    return 0.0 if p + r == 0 else (1 + beta ** 2) * p * r / (beta ** 2 * p + r) * 100


def row_metrics(row, source_row):
    dest, source = row['Dest'] or '', source_row['source']
    visible = TAG.sub(' ', dest)
    # Failed rows keep the source text; they are counted as errors, not as residue.
    residue = [w for w in LATIN_WORD.findall(visible) if w not in ALLOWED_LATIN] if row['Status'] == 'Done' else []
    result = {
        'error': row['Status'] != 'Done',
        'untranslated': row['Status'] == 'Done' and not HANGUL.search(dest) and bool(LATIN_WORD.search(source)),
        'latin_residue_words': len(residue),
        'ambiguous_particle': bool(AMBIGUOUS_PARTICLE.search(dest)),
        'length_ratio': len(dest) / max(1, len(source)),
    }
    if source_row['reference'] and row['Status'] == 'Done':
        result['chrf'] = chrf(dest, source_row['reference'])
    checks = [(pattern.search(source) is not None, target in dest) for pattern, target in ELDEN_TERMS]
    applicable = [ok for present, ok in checks if present]
    if applicable and row['Status'] == 'Done':
        result['elden_terms_ok'] = all(applicable)
    return result


def summarize(items):
    count = len(items)
    chrfs = [m['chrf'] for m in items if 'chrf' in m]
    terms = [m['elden_terms_ok'] for m in items if 'elden_terms_ok' in m]
    return {
        'rows': count,
        'error_rows': sum(m['error'] for m in items),
        'untranslated_rows': sum(m['untranslated'] for m in items),
        'rows_with_latin_residue': sum(m['latin_residue_words'] > 0 for m in items),
        'ambiguous_particle_rows': sum(m['ambiguous_particle'] for m in items),
        'length_ratio_outliers': sum(not 0.3 <= m['length_ratio'] <= 2.5 for m in items),
        'chrf_mean': round(sum(chrfs) / len(chrfs), 2) if chrfs else None,
        'elden_terms_ok': f'{sum(terms)}/{len(terms)}' if terms else None,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('dataset')
    parser.add_argument('runs', nargs='+')
    parser.add_argument('--out', required=True)
    args = parser.parse_args()
    dataset = {row['id']: row for row in json.loads(Path(args.dataset).read_text(encoding='utf-8'))['rows']}
    report, dests = {}, {}
    for run in args.runs:
        rows = json.loads((Path(run) / 'rows.json').read_text(encoding='utf-8'))
        usage = json.loads((Path(run) / 'run.json').read_text(encoding='utf-8'))['Usage']
        per_row = {row['Id']: row_metrics(row, dataset[row['Id']]) for row in rows}
        groups = collections.defaultdict(list)
        for row in rows:
            groups[row['Group']].append(per_row[row['Id']])
        report[Path(run).name] = {'usage': usage, 'all': summarize(list(per_row.values())),
                                  'groups': {g: summarize(items) for g, items in sorted(groups.items())}}
        dests[Path(run).name] = {row['Id']: row['Dest'] for row in rows if row['Status'] == 'Done'}
    names = list(dests)
    # Same-setting repeats show how much single runs vary.
    for i, first in enumerate(names):
        for second in names[i + 1:]:
            shared = set(dests[first]) & set(dests[second])
            if shared:
                identical = sum(dests[first][k] == dests[second][k] for k in shared)
                report.setdefault('agreement', {})[f'{first} vs {second}'] = {
                    'shared_done_rows': len(shared), 'identical_rows': identical,
                    'identical_rate': round(identical / len(shared), 3)}
    Path(args.out).write_text(json.dumps(report, ensure_ascii=False, indent=1), encoding='utf-8')
    print(json.dumps(report, ensure_ascii=False, indent=1))


if __name__ == '__main__':
    main()
