"""Build a blind human review page that checks the Claude judge.

python benchmarks/translation/eval-v2/make_human_review.py DATASET JUDGE_DIR OUT_DIR [--pairs 60]

Samples judged rows evenly across the judge's three outcomes (X won, Y won,
tie), keeping the packet's A/B order. The page shows neither the model names
nor the judge's verdict. Answers stay in the browser and are exported as
JSON; compare them with --compare.

python benchmarks/translation/eval-v2/make_human_review.py DATASET JUDGE_DIR OUT_DIR --compare answers.json
"""
import argparse
import collections
import html
import json
import random
from pathlib import Path

SEED = 20261002
MAX_SOURCE_CHARS = 400


def load_verdicts(judge):
    verdicts = {}
    for path in sorted((judge / 'verdicts').glob('packet-*.jsonl')):
        for line in path.read_text(encoding='utf-8').splitlines():
            verdict = json.loads(line)
            if not verdict['id'].endswith('#swap'):
                verdicts[verdict['id']] = verdict
    return verdicts


def load_candidates(judge):
    candidates = {}
    for path in sorted(judge.glob('packet-*.json')):
        for item in json.loads(path.read_text(encoding='utf-8')):
            candidates.setdefault(item['id'], item)
    return candidates


def outcome(key, row_id, winner):
    return 'tie' if winner == 'tie' else key['map'][row_id][winner]


def sample(dataset, judge, pairs):
    key = json.loads((judge / 'key.json').read_text(encoding='utf-8'))
    verdicts = load_verdicts(judge)
    candidates = load_candidates(judge)
    buckets = collections.defaultdict(list)
    for row_id, verdict in sorted(verdicts.items()):
        if len(dataset[row_id]['source']) <= MAX_SOURCE_CHARS and row_id in candidates:
            buckets[outcome(key, row_id, verdict['winner'])].append(row_id)
    rng = random.Random(SEED)
    per_bucket = pairs // 3
    chosen = []
    for name in ('x', 'y', 'tie'):
        rows = buckets[name]
        chosen += rng.sample(rows, min(per_bucket, len(rows)))
    rng.shuffle(chosen)
    return [candidates[row_id] for row_id in chosen]


def render(items):
    cards = []
    for number, item in enumerate(items, 1):
        reference = (f'<div class="ref"><b>참고 번역</b> {html.escape(item["reference"])}</div>'
                     if item.get('reference') else '')
        cards.append(f'''
<section class="card" data-id="{html.escape(item["id"])}">
  <h2>{number}. <span>{html.escape(item["group"])} · {html.escape(item["rec"])}</span></h2>
  <div class="src">{html.escape(item["source"])}</div>
  {reference}
  <div class="cand"><b>A</b><div>{html.escape(item["A"])}</div></div>
  <div class="cand"><b>B</b><div>{html.escape(item["B"])}</div></div>
  <div class="pick">
    <label><input type="radio" name="w{number}" value="A"> A가 낫다</label>
    <label><input type="radio" name="w{number}" value="tie"> 비슷하다</label>
    <label><input type="radio" name="w{number}" value="B"> B가 낫다</label>
    <input class="note" placeholder="메모 (선택)">
  </div>
</section>''')
    return PAGE.replace('{{CARDS}}', '\n'.join(cards)).replace('{{COUNT}}', str(len(items)))


def compare(judge, answers_path):
    key = json.loads((judge / 'key.json').read_text(encoding='utf-8'))
    verdicts = load_verdicts(judge)
    answers = json.loads(Path(answers_path).read_text(encoding='utf-8'))
    table = collections.Counter()
    agree = total = 0
    for row_id, answer in answers.items():
        human = answer['winner']
        judge_pick = verdicts[row_id]['winner']
        table[(judge_pick, human)] += 1
        total += 1
        agree += judge_pick == human
    names = {'x': Path(key['x']).name, 'y': Path(key['y']).name}
    preference = collections.Counter(outcome(key, row_id, a['winner']) for row_id, a in answers.items())
    result = {
        'answered': total,
        'agreement': round(agree / max(1, total), 3),
        'judge_vs_human': {f'{j}->{h}': n for (j, h), n in sorted(table.items())},
        'human_totals': {names.get(k, k): n for k, n in preference.items()},
    }
    print(json.dumps(result, ensure_ascii=False, indent=1))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('dataset')
    parser.add_argument('judge_dir')
    parser.add_argument('out')
    parser.add_argument('--pairs', type=int, default=60)
    parser.add_argument('--compare')
    args = parser.parse_args()
    judge = Path(args.judge_dir)
    if args.compare:
        compare(judge, args.compare)
        return
    dataset = {row['id']: row for row in json.loads(Path(args.dataset).read_text(encoding='utf-8'))['rows']}
    items = sample(dataset, judge, args.pairs)
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    (out / 'review.html').write_text(render(items), encoding='utf-8')
    print(f'{len(items)} pairs -> {out / "review.html"}')


PAGE = '''<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>번역 블라인드 검수</title>
<style>
:root { --bg:#f7f7f5; --card:#fff; --ink:#1d1d1f; --muted:#6b6b70; --line:#e2e2de; --accent:#2f6fde; --src:#f0f0ec; }
@media (prefers-color-scheme: dark) { :root { --bg:#17171a; --card:#212125; --ink:#ececef; --muted:#9a9aa2; --line:#34343a; --accent:#7aa7ff; --src:#2a2a2f; } }
* { box-sizing:border-box; }
body { margin:0; background:var(--bg); color:var(--ink); font:15px/1.6 system-ui, "Malgun Gothic", sans-serif; }
main { max-width:860px; margin:0 auto; padding:16px; }
header { position:sticky; top:0; background:var(--bg); padding:12px 0; border-bottom:1px solid var(--line); z-index:1; }
header p { margin:4px 0; color:var(--muted); }
button { font:inherit; padding:6px 14px; border:1px solid var(--accent); background:var(--accent); color:#fff; border-radius:6px; cursor:pointer; }
.card { background:var(--card); border:1px solid var(--line); border-radius:8px; padding:14px; margin:14px 0; }
h2 { font-size:15px; margin:0 0 8px; } h2 span { color:var(--muted); font-weight:normal; }
.src { background:var(--src); padding:8px 10px; border-radius:6px; white-space:pre-wrap; overflow-wrap:anywhere; }
.ref { color:var(--muted); margin:6px 0; white-space:pre-wrap; overflow-wrap:anywhere; }
.cand { display:flex; gap:10px; margin:8px 0; } .cand b { color:var(--accent); } .cand div { white-space:pre-wrap; overflow-wrap:anywhere; }
.pick { display:flex; flex-wrap:wrap; gap:14px; align-items:center; margin-top:8px; }
.note { flex:1; min-width:180px; font:inherit; padding:4px 8px; border:1px solid var(--line); border-radius:6px; background:var(--card); color:var(--ink); }
</style>
</head>
<body>
<main>
<header>
  <b>번역 블라인드 검수 ({{COUNT}}쌍)</b>
  <p>원문 뜻을 더 정확하고 자연스럽게 옮긴 쪽을 고르세요. 문체 취향 차이만 있으면 "비슷하다"를 고르세요. 참고 번역은 정답이 아닙니다.</p>
  <p><span id="progress"></span> · 답은 이 브라우저에 자동 저장됩니다. <button id="export">결과 저장 (JSON)</button></p>
</header>
{{CARDS}}
</main>
<script>
const STORE = 'eval-v2-human-review';
let saved = {};
try { saved = JSON.parse(localStorage.getItem(STORE) || '{}'); } catch (e) {}
const cards = [...document.querySelectorAll('.card')];
function collect() {
  const out = {};
  for (const card of cards) {
    const pick = card.querySelector('input[type=radio]:checked');
    const note = card.querySelector('.note').value.trim();
    if (pick) out[card.dataset.id] = { winner: pick.value, note };
  }
  return out;
}
function update() {
  const answers = collect();
  document.getElementById('progress').textContent = `${Object.keys(answers).length} / ${cards.length} 완료`;
  try { localStorage.setItem(STORE, JSON.stringify(answers)); } catch (e) {}
}
for (const card of cards) {
  const prior = saved[card.dataset.id];
  if (prior) {
    const radio = card.querySelector(`input[value="${prior.winner}"]`);
    if (radio) radio.checked = true;
    card.querySelector('.note').value = prior.note || '';
  }
  card.addEventListener('change', update);
  card.querySelector('.note').addEventListener('input', update);
}
document.getElementById('export').addEventListener('click', () => {
  const blob = new Blob([JSON.stringify(collect(), null, 1)], { type: 'application/json' });
  const link = document.createElement('a');
  link.href = URL.createObjectURL(blob);
  link.download = 'human-review-answers.json';
  link.click();
});
update();
</script>
</body>
</html>
'''

if __name__ == '__main__':
    main()
