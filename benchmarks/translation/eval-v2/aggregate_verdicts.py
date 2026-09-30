"""Aggregate blind verdicts after judging is finished.

python benchmarks/translation/eval-v2/aggregate_verdicts.py DATASET JUDGE_DIR --out summary.json

Opens key.json only here. Rows judged twice (id#swap) measure consistency;
the first verdict is used for the totals.
"""
import argparse
import collections
import json
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('dataset')
    parser.add_argument('judge_dir')
    parser.add_argument('--out', required=True)
    args = parser.parse_args()
    judge = Path(args.judge_dir)
    dataset = {row['id']: row for row in json.loads(Path(args.dataset).read_text(encoding='utf-8'))['rows']}
    key = json.loads((judge / 'key.json').read_text(encoding='utf-8'))
    names = {'x': Path(key['x']).name, 'y': Path(key['y']).name}
    ties = json.loads((judge / 'auto-ties.json').read_text(encoding='utf-8'))
    verdicts = {}
    for path in sorted((judge / 'verdicts').glob('packet-*.jsonl')):
        for line in path.read_text(encoding='utf-8').splitlines():
            verdict = json.loads(line)
            verdicts[verdict['id']] = verdict

    def side(row_id, label):
        return key['map'][row_id][label] if label in ('A', 'B') else 'tie'

    totals = collections.Counter()
    by_group = collections.defaultdict(collections.Counter)
    by_rec = collections.defaultdict(collections.Counter)
    errors = {name: collections.Counter() for name in names.values()}
    for row_id in ties:
        totals['auto_tie'] += 1
        by_group[dataset[row_id]['group']]['auto_tie'] += 1
    for row_id, verdict in verdicts.items():
        if row_id.endswith('#swap'):
            continue
        winner = side(row_id, verdict['winner'])
        label = names[winner] if winner in names else 'tie'
        totals[label] += 1
        by_group[dataset[row_id]['group']][label] += 1
        by_rec[dataset[row_id]['rec']][label] += 1
        for candidate in ('A', 'B'):
            model = names[key['map'][row_id][candidate]]
            for error in verdict.get(candidate, []):
                errors[model][f"{error['type']}:{error['severity']}"] += 1

    agree = disagree = 0
    flips = []
    for row_id, verdict in verdicts.items():
        if not row_id.endswith('#swap'):
            continue
        base_id = row_id[:-5]
        first = side(base_id, verdicts[base_id]['winner'])
        second = key['map'][row_id][verdict['winner']] if verdict['winner'] in ('A', 'B') else 'tie'
        if first == second:
            agree += 1
        else:
            disagree += 1
            flips.append({'id': base_id, 'first': first, 'second': second})

    judged = sum(totals[name] for name in names.values()) + totals['tie']
    summary = {
        'pair': names, 'judged_rows': judged, 'auto_ties': totals['auto_tie'],
        'totals': {k: totals[k] for k in (*names.values(), 'tie')},
        'by_group': {g: dict(c) for g, c in sorted(by_group.items())},
        'by_rec': {r: dict(c) for r, c in sorted(by_rec.items(), key=lambda kv: -sum(kv[1].values()))},
        'errors': {m: dict(c.most_common()) for m, c in errors.items()},
        'swap_consistency': {'agree': agree, 'disagree': disagree, 'rate': round(agree / max(1, agree + disagree), 3),
                             'flips': flips},
    }
    Path(args.out).write_text(json.dumps(summary, ensure_ascii=False, indent=1), encoding='utf-8')
    print(json.dumps(summary, ensure_ascii=False, indent=1))


if __name__ == '__main__':
    main()
