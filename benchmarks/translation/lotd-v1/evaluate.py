"""Validate saved benchmark responses offline. Never calls models or scores meaning."""
from collections import Counter
from pathlib import Path
import argparse
import hashlib
import json
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent
PROTECTED = re.compile(r'\r\n|\r|\n|<[^>]+>|\[page\s*break\]|\{\{[^{}]+\}\}|\{[^{}]+\}|%(?:\d+\$)?[-+0-9.]*[A-Za-z]|%%', re.I)
STRUCTURE = re.compile(r'\r\n|\r|\n|\[page\s*break\]|</?(?:b|i|u|font|p|br|img|page)\b[^>]*>', re.I)


def parse_response(raw):
    # Keep any envelope deviation visible; do not repair JSON or rewrite translations.
    text = raw.strip()
    fenced = bool(re.fullmatch(r'```(?:json)?\s*\n[\s\S]*\n```', text, re.I))
    if fenced:
        text = re.sub(r'^```(?:json)?\s*\n|\n```$', '', text, flags=re.I)
    result = json.loads(text)
    if not isinstance(result, dict) or set(result) != {'translations'}:
        raise ValueError('Expected one top-level translations key')
    rows = result['translations']
    if not isinstance(rows, list):
        raise ValueError('translations is not an array')
    for row in rows:
        if not isinstance(row, dict) or set(row) != {'id', 'text'} or not all(isinstance(row[k], str) for k in ('id', 'text')):
            raise ValueError('Each result must contain string id and text only')
    return rows, fenced


def check_text(case, text):
    errors, review = [], []
    expected = Counter(PROTECTED.findall(case['source']))
    actual = Counter(PROTECTED.findall(text))
    if expected != actual:
        errors.append({'kind': 'protected_tokens', 'missing': dict(expected-actual), 'extra': dict(actual-expected)})
    if STRUCTURE.findall(case['source']) != STRUCTURE.findall(text):
        errors.append({'kind': 'structure_order'})
    for en, ko in case['glossary'].items():
        if re.search(r'(?<![A-Za-z])'+re.escape(en)+r'(?![A-Za-z])', case['source']) and ko not in text:
            review.append({'kind': 'glossary_review', 'source': en, 'expected': ko})
    if text.strip() == case['source'].strip():
        review.append({'kind': 'unchanged_source'})
    if not re.search('[가-힣]', text):
        review.append({'kind': 'no_hangul'})
    visible_text = re.sub(r'<[^>]*>|\[page\s*break\]', '', text, flags=re.I)
    english = re.findall(r'(?<![A-Za-z])[A-Za-z]{3,}(?![A-Za-z])', visible_text)
    if english:
        review.append({'kind': 'remaining_english_review', 'words': sorted(set(english))})
    return errors, review


def self_test():
    case = {'source': '<b><mag></b>\n%s[pagebreak]', 'glossary': {}}
    assert not check_text(case, '<b><mag></b>\n%s[pagebreak]')[0]
    assert check_text(case, '</b><mag><b>\n%s[pagebreak]')[0]
    assert check_text(case, '<b><mag></b>\n%s')[0]
    assert check_text(case, '<b><mag><mag></b>\n%s[pagebreak]')[0]
    assert not check_text({'source': '<mag> for <dur>', 'glossary': {}}, '<dur> 동안 <mag>')[0]
    assert check_text({'source': '[page break]x<page break>y', 'glossary': {}}, '[pagebreak]x<page break>y')[0]
    assert any(r['kind'] == 'remaining_english_review' for r in check_text({'source':'riches', 'glossary':{}}, 'riches가 있다')[1])
    for bad in ['{"translations":[{"id":"a","text":null}]}', '{"translations":{}}', '{"translations":[],"extra":1}', 'prefix {"translations":[]}']:
        try:
            parse_response(bad)
        except (ValueError, TypeError):
            pass
        else:
            raise AssertionError('Invalid fixture passed')
    assert parse_response('{"translations":[{"id":"a","text":"한글"}]}')[0][0]['text'] == '한글'


def verify_dataset(dataset, stages):
    """Fail closed if the fixed input, selection, or source provenance drifted."""
    source = ROOT.parents[2] / dataset['source_file']
    assert hashlib.sha256(source.read_bytes()).hexdigest() == dataset['source_sha256']
    glossary = ROOT.parents[2] / 'src/XTranslatorAi.App/Assets/기본용어집.md'
    assert hashlib.sha256(glossary.read_bytes()).hexdigest() == dataset['glossary_sha256']
    original = ET.fromstring(source.read_bytes()).findall('./Content/String')
    cases = dataset['cases']
    ids = [c['id'] for c in cases]
    by_id = {c['id']:c for c in cases}
    assert len(ids) == len(set(ids)) == 120
    assert Counter(c['category'] for c in cases) == {'dialogue':50, 'book':20, 'quest':20, 'item_ui':30}
    assert len(dataset['pilot_ids']) == len(set(dataset['pilot_ids'])) == 24
    assert set(dataset['pilot_ids']) <= set(ids)
    for case in cases:
        row = original[case['source_row']-1]
        assert row.findtext('Source') == case['source']
        assert row.findtext('EDID') == case['edid']
        assert row.findtext('REC') == case['record']
        assert case['context']['speaker'] == 'unknown'
    for stage, batches in stages.items():
        stage_ids = []
        for batch in batches:
            raw = (ROOT/'prompts'/(batch['batch']+'.txt')).read_bytes()
            assert hashlib.sha256(raw).hexdigest() == batch['prompt_sha256']
            assert len(raw) == batch['utf8_bytes']
            payload = json.loads(raw.decode().split('\n\n', 1)[1])
            assert [x['id'] for x in payload['items']] == batch['ids']
            assert all(set(x) == {'id','source','context','glossary'} for x in payload['items'])
            for item in payload['items']:
                assert item == {key:by_id[item['id']][key] for key in item}
            stage_ids.extend(batch['ids'])
        assert Counter(stage_ids) == Counter(dataset['pilot_ids'] if stage == 'pilot' else ids)


def evaluate():
    self_test()
    dataset = json.loads((ROOT/'dataset.json').read_text(encoding='utf-8'))
    cases = {c['id']: c for c in dataset['cases']}
    stages = json.loads((ROOT/'batches.json').read_text(encoding='utf-8'))
    verify_dataset(dataset, stages)
    manifests = {b['batch']: b for batches in stages.values() for b in batches}
    results = []
    for folder in sorted((ROOT/'runs').glob('*')):
        if not folder.is_dir() or folder.name == 'invalid':
            continue
        for path in sorted(folder.glob('*.txt')):
            if path.stem not in manifests:
                continue
            batch = manifests[path.stem]
            raw = path.read_text(encoding='utf-8')
            metadata_path = path.with_suffix('.meta.json')
            metadata = json.loads(metadata_path.read_text(encoding='utf-8')) if metadata_path.exists() else {}
            report = {'run': folder.name, 'batch': path.stem, 'expected_rows': len(batch['ids']),
                      'raw_sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                      'input_verified': metadata.get('inputVerified', False),
                      'schema_errors': [], 'rows': [], 'meaning_assessed': False}
            try:
                outputs, fenced = parse_response(raw)
                report['markdown_fence'] = fenced
                counts = Counter(row['id'] for row in outputs)
                if counts != Counter(batch['ids']):
                    report['schema_errors'].append({'kind': 'ids', 'missing': sorted(set(batch['ids'])-set(counts)),
                                                    'extra': sorted(set(counts)-set(batch['ids'])),
                                                    'duplicate': [k for k,v in counts.items() if v>1]})
                for output in outputs:
                    if output['id'] not in batch['ids']:
                        continue
                    errors, review = check_text(cases[output['id']], output['text'])
                    report['rows'].append({'id': output['id'], 'category': cases[output['id']]['category'],
                                           'format_errors': errors, 'review_flags': review, 'text': output['text']})
            except (ValueError, TypeError) as ex:
                report['schema_errors'].append({'kind': 'invalid_response', 'detail': str(ex)})
            results.append(report)
    summary = {'dataset_version': dataset['version'], 'meaning_assessed': False, 'runs': results}
    (ROOT/'format-results.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2)+'\n',encoding='utf-8', newline='\n')
    for model in sorted(set(r['run'] for r in results)):
        for stage in stages:
            batches = [r for r in results if r['run']==model and r['batch'].startswith(stage+'-')]
            if not batches:
                continue
            ids = {r['id'] for b in batches for r in b['rows']}
            print(json.dumps({'run':model, 'stage':stage, 'saved_batches':len(batches), 'unique_rows':len(ids),
                              'schema_failed_batches':sum(bool(b['schema_errors']) for b in batches),
                              'format_failed_rows':sum(bool(r['format_errors']) for b in batches for r in b['rows']),
                              'review_flag_rows':sum(bool(r['review_flags']) for b in batches for r in b['rows'])},ensure_ascii=False))
    return summary


if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--self-test',action='store_true')
    args=parser.parse_args()
    if args.self_test:
        self_test()
        print('Offline validation fixtures passed')
    else:
        evaluate()
