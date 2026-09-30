"""Create blind A/B judging packets from two eval-run outputs.

python benchmarks/translation/eval-v2/make_judge_packets.py DATASET RUN_X RUN_Y OUT_DIR [--rows-per-packet 25]

Each row's candidates are assigned to A/B at random (fixed seed). The mapping
goes to OUT_DIR/key.json, which the judge must not open before all verdicts
are written. Identical outputs are recorded as automatic ties and not judged.
A 10% sample is repeated with A/B swapped to measure judge consistency.
"""
import argparse
import json
import random
from pathlib import Path

SEED = 20261001


def load_rows(run_dir):
    return {row['Id']: row for row in json.loads((Path(run_dir) / 'rows.json').read_text(encoding='utf-8'))}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('dataset')
    parser.add_argument('run_x')
    parser.add_argument('run_y')
    parser.add_argument('out')
    parser.add_argument('--rows-per-packet', type=int, default=25)
    args = parser.parse_args()
    rng = random.Random(SEED)
    dataset = {row['id']: row for row in json.loads(Path(args.dataset).read_text(encoding='utf-8'))['rows']}
    x_rows, y_rows = load_rows(args.run_x), load_rows(args.run_y)
    out = Path(args.out)
    if out.exists() and any(out.iterdir()):
        raise SystemExit('Use a new, empty output folder.')
    out.mkdir(parents=True, exist_ok=True)

    items, key, ties = [], {}, []
    for row_id in sorted(set(x_rows) & set(y_rows)):
        x, y, source = x_rows[row_id], y_rows[row_id], dataset[row_id]
        # A failed row keeps its source text; judge it as an output the user would see.
        x_text = x['Dest'] if x['Status'] == 'Done' else f"[번역 실패: 원문 유지]\n{x['Dest']}"
        y_text = y['Dest'] if y['Status'] == 'Done' else f"[번역 실패: 원문 유지]\n{y['Dest']}"
        if x_text == y_text:
            ties.append(row_id)
            continue
        swap = rng.random() < 0.5
        key[row_id] = {'A': 'y' if swap else 'x', 'B': 'x' if swap else 'y'}
        items.append({'id': row_id, 'rec': source['rec'], 'group': source['group'], 'source': source['source'],
                      'reference': source['reference'], 'A': y_text if swap else x_text, 'B': x_text if swap else y_text})

    rng.shuffle(items)
    repeat = rng.sample(items, max(1, len(items) // 10)) if items else []
    for item in repeat:
        key[item['id'] + '#swap'] = {'A': key[item['id']]['B'], 'B': key[item['id']]['A']}
    items += [dict(item, id=item['id'] + '#swap', A=item['B'], B=item['A']) for item in repeat]

    packets = [items[i:i + args.rows_per_packet] for i in range(0, len(items), args.rows_per_packet)]
    for number, packet in enumerate(packets, 1):
        (out / f'packet-{number:03d}.json').write_text(json.dumps(packet, ensure_ascii=False, indent=1), encoding='utf-8')
    (out / 'key.json').write_text(json.dumps({'x': args.run_x, 'y': args.run_y, 'map': key}, indent=1), encoding='utf-8')
    (out / 'auto-ties.json').write_text(json.dumps(ties, indent=1), encoding='utf-8')
    print(json.dumps({'judged_rows': len(items) - len(repeat), 'swap_repeats': len(repeat),
                      'auto_ties': len(ties), 'packets': len(packets)}))


if __name__ == '__main__':
    main()
