"""Render a review packet from immutable raw responses and explicit AI review notes."""
from pathlib import Path
from collections import Counter
import json
from evaluate import evaluate

ROOT = Path(__file__).resolve().parent
LABELS = {
    'gemini-3.8-flash-low':'Gemini 3.8 Flash / Low',
    'gemini-3.1-flash-lite-minimal':'Gemini 3.1 Flash Lite / Minimal',
    'solar-pro3-low':'Solar Pro 3 / Low',
    'solar-pro4-web-instant':'Solar Pro 4 / Solar Chat 즉시',
}


def main():
    result = evaluate()
    dataset = json.loads((ROOT/'dataset.json').read_text(encoding='utf-8'))
    stages = json.loads((ROOT/'batches.json').read_text(encoding='utf-8'))
    notes = json.loads((ROOT/'review-notes.json').read_text(encoding='utf-8'))
    assert set(notes['cases']) == set(dataset['pilot_ids'])
    assert all(set(n) == set(LABELS) for n in notes['cases'].values())
    tables, summaries = {}, []
    for stage in ('pilot','full'):
        lines = ['| 실행 경로 | 응답 파일/요청 | 응답 대상 행 | 엄격 JSON 통과 행 | 그중 형식 통과 행 | 스키마 실패 요청 |',
                 '| --- | ---: | ---: | ---: | ---: | ---: |']
        for model,label in LABELS.items():
            batches = [r for r in result['runs'] if r['run']==model and r['batch'].startswith(stage+'-')]
            if not batches:
                continue
            parsed = [row for b in batches if not b['schema_errors'] for row in b['rows']]
            summary = {'model':model, 'stage':stage, 'saved_batches':len(batches),
                       'total_planned_batches':len(stages[stage]),
                       'response_target_rows':sum(b['expected_rows'] for b in batches),
                       'strict_json_rows':len(parsed),
                       'format_pass_rows':sum(not r['format_errors'] for r in parsed),
                       'schema_failed_batches':sum(bool(b['schema_errors']) for b in batches)}
            summaries.append(summary)
            lines.append(f"| {label} | {summary['saved_batches']}/{len(stages[stage])} | {summary['response_target_rows']} | {summary['strict_json_rows']} | {summary['format_pass_rows']} | {summary['schema_failed_batches']} |")
        tables[stage] = '\n'.join(lines)
    (ROOT/'summary.json').write_text(json.dumps(summaries,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')

    report = f'''# LOTD 실제 원문 모델 비교 — 2026-09-29

현재 확보한 결과에서는 **Gemini 3.8 Flash를 품질 우선 후보로 유지하고, 3.1 Flash Lite를 검증·재처리를 전제로 한 저비용 후보로 두는 판단**이 타당하다. Solar 두 무료 경로에서는 미번역·직역·형식 문제가 더 뚜렷했다. 다만 Solar Pro 4는 웹 에이전트 경로이므로 그 결과를 Pro 4 API의 성능으로 일반화할 수 없다.

**6개 모델 종합 비교가 모두 끝난 것은 아니다.** GLM은 최종 응답을 확보하지 못했고 DeepSeek의 NVIDIA 경로는 서비스 재시도가 실패했다. 아래 표는 실제 확보한 결과만 나타낸다. 모델 전체의 통계적 순위나 사람 평가 점수는 아니다.

## 데이터와 조건

- 실제 `LegacyoftheDragonborn_english_korean.xml`에서 고정 추출한 **120개**: 대사 50, 책 20, 퀘스트 20, 아이템·UI 30. 중복 원문을 제거하고 동일 레코드 쏠림을 제한했다.
- 예비 비교 **24개**: 대사 10, 짧은 책 4, 퀘스트 4, 아이템·UI 6. 8개 요청으로 나누었다. 확장 120개는 37개 요청이다.
- 120개 원문은 총 **66,578자**이며 가장 긴 책은 **11,903자**이다. 원문 SHA-256: `{dataset['source_sha256']}`.
- 동일 원문·문맥·TES 용어집·출력 지시를 제공했다. 기존 Dest는 참조 정답으로 사용하지 않았다. 해당 XML에는 한국어 정답이 없다.
- 문맥은 같은 BOOK/QUST의 제목, 같은 INFO의 확인 가능한 응답 부분만 사용했다. 실제 NPC 화자·성별·상대 관계는 unknown이다. 인접 XML 행을 앞뒤 대화로 가정하지 않았다. Fallout/Starfield TM은 사용하지 않았다.
- 각 요청을 새 대화에서 실행하고 입력란이 로컬 프롬프트와 일치하는지 확인했다. JSON schema 강제·앱의 마스킹·후처리를 적용하지 않은 **raw 웹 비교**이다.
- Gemini는 AI Studio, API 키 미선택, 도구 끄기, 각각 Low/Minimal. Solar Pro 3는 Upstage Playground, Low, system prompt 빈 값, temperature 0.8, max tokens 65536. Pro 4는 Solar Chat의 즉시 모드이며 내부 작업 단계가 있는 경로이다. **입력은 같지만 제공자별 UI 설정과 시스템 동작이 완전히 같지는 않다.**
- 추가 결제, 크레딧 활성화, API 키 등록, 유료 API 직접 호출을 하지 않았다. Google은 로그인된 기존 Studio 이용 범위이다. 이 사실이 새 계정의 무료 API 조건을 보장하지는 않는다.
- 원본 응답은 편집창 또는 UI 복사로 저장했다. 렌더링된 Markdown은 태그·역슬래시를 숨길 수 있어 원본으로 사용하지 않았다. 입력이 중복 삽입됐던 Pro 4 사전 시도는 `runs/invalid`에 격리하고 제외했다.

## 24개 예비 비교

{tables['pilot']}

응답 대상 행은 해당 요청이 번역하도록 요구한 행 수이다. JSON 실패는 해당 요청 전체를 자동 파싱 실패로 처리한다. **형식 통과는 의미 정확도나 번역 완료를 뜻하지 않는다.** 예를 들어 Solar Pro 3의 계약서 원문 복사는 형식은 통과해도 번역은 실패했다.

- **3.8 Flash:** 대사와 일지 문체가 대체로 자연스럽고 지정 용어를 잘 지켰다. 다만 Scholar→학사, 제작 총액→계약금, 일부 첨가 표현은 검토가 필요하다.
- **3.1 Flash Lite:** 대사·목표·아이템명은 상당 부분 실용적이었다. `Sun's Dawn=2월` 지시를 무시해 ‘태양의 달’로 옮겼다. 계약서 한 요청은 두 시도 모두 internal error라 품질을 평가할 수 없다.
- **Solar Pro 3:** 계약서 한 건은 통째로 영어 원문을 반환했다. 다른 책의 제목과 부제도 영어로 남겼다. 6행 요청에서는 닫히지 않은 JSON 객체를 네 번 반복했고, 글꼴 태그의 `$` 앞에 역슬래시를 추가한 사례가 있었다.
- **Solar Pro 4 웹 경로:** `Red Year`를 ‘붉은 해가 뜨기 전’으로 옮겼고, `riches가`, `런estone`처럼 영어가 남았다. 책 본문 대부분을 영어로 남기면서 이미지 태그를 삭제한 사례도 있다. 이 문제의 원인이 기반 모델인지 웹 에이전트의 내부 처리인지는 구분할 수 없다.

24개 전부를 원문과 대조한 **비블라인드 AI 검토**는 [문장별 검토](comparison.md), [구조화한 검토 메모](review-notes.json)에 있다. 사람의 블라인드 평가 또는 공인 게임 번역가 평가로 표현하지 않는다. 출처 없는 10점 만점 점수는 부여하지 않았다.

## 120개 확장 검증

예비 비교에서 남은 Gemini 두 후보를 장문·추가 대사·퀘스트로 확대했다. Solar는 위 문제를 먼저 해결해야 하므로 이 단계로 확대하지 않았다. 예비 요청과 프롬프트 SHA-256이 같은 요청 중 이미 응답을 확보한 것은 재사용했고 metadata에 표시했다(3.8: 5개 요청, 3.1: 4개 요청). **재사용은 반복 실험이 아니다.**

{tables['full']}

- **3.8:** 120개 중 116개 응답을 확보했다. 마지막 4개 퀘스트 요청은 화면에 일일 무료 한도 안내가 나타났다. 확보한 36개 요청은 엄격한 JSON 스키마를 통과했지만 책 2개에서 줄바꿈 손실이 있었다.
- **3.1:** 계약서 1개를 제외한 119개 대상의 응답 파일을 확보했다. 장문 3개 요청이 스키마 검사를 통과하지 못했다. 2개는 최상위에 불필요한 `id` 키를 추가했고 1개는 닫는 중괄호가 잘못됐다. 파싱 가능한 116개 중 책 4개에서 줄바꿈 손실이 있었다.
- 일시적 internal error 후 같은 입력을 한 번 재시도해 3.8의 책 2개와 3.1의 대사 6개 요청을 회수했다. 3.1 계약서는 첫 요청과 한 번의 재시도 모두 실패했다. 실패 이력을 지우지 않았다.
- 자동 검토 표식은 오류 확정이 아니다. `loot`를 전리품으로, `Elder Scroll`을 엘더 스크롤로 옮긴 경우 단순 용어 탐지가 경고할 수 있다. 고유명사·다국어 인용의 영어 잔류도 문맥 확인이 필요하다.
- 확장 120개 전체의 의미를 사람 또는 별도 심사자가 채점하지 않았다. 이 단계의 수치는 **반환·JSON·서식 안정성**에 관한 것이다. 무료 한도에 도달해 두 최종 후보의 완전한 반복 실험도 완료하지 못했다.

## 응답을 확보하지 못한 후보

| 모델 | 실제 확인한 경로 | 이번 상태 |
| --- | --- | --- |
| GLM-5.3 Flash | NVIDIA NIM `z-ai/glm-5.3-flash`, Reasoning ON | 사용자 승인 후 Trial 약관에 동의하고 첫 표본을 보냈지만 최종 응답 없이 Thinking 상태가 지속됨. 품질 점수 없음. |
| DeepSeek V4.1 Flash | NVIDIA NIM `deepseek-ai/deepseek-v4.1-flash` | 정확한 모델의 무료 엔드포인트를 확인하고 첫 표본 전송. 서비스가 `Retries exhausted: 3/3` 반환. 품질 점수 없음. |
| DeepSeek 웹 | 공식 Chat 로그인 화면 | 사용자 로그인 대기. 로그인이 돼도 실제 제공 모델 버전을 다시 확인해야 함. |
| Gemma 4 | 선택적 로컬 후보 | 설치·다운로드·실행하지 않음. 이번 비교에 포함하지 않음. |

NVIDIA의 Free Endpoint 표시와 실제 응답 성공은 다른 사실이다. 실패한 서비스 경로를 모델 번역 능력의 실패로 집계하지 않았다. [DeepSeek 무료 모델 페이지](https://build.nvidia.com/deepseek-ai/deepseek-v4.1-flash/build), [GLM 무료 모델 페이지](https://build.nvidia.com/z-ai/glm-5-3-flash)

## 공개 API 가격 참고

2026-09-29 확인 기준, 텍스트 100만 토큰당 USD, 입력/출력 가격이다. 이 웹 실험의 청구액이 아니다. 추론·캐시·재시도 및 과세 조건에 따라 실비가 달라진다.

| 정확한 모델 ID | 입력 | 출력 | 조건 |
| --- | ---: | ---: | --- |
| `gemini-3.8-flash` | $0.75 | $3.75 | 2026-12-31까지 한시 가격. 이후 $1.50/$7.50 공지 |
| `gemini-3.1-flash-lite` | $0.25 | $1.50 | 표준 가격 |
| `solar-pro3` | $0.15 | $0.60 | 표준 가격, Upstage VAT 별도 |
| `solar-pro4` | $0.30 | $1.20 | 2026-10-10 00:00 UTC까지 $0.09/$0.36 행사, VAT 별도 |
| `deepseek-flash` (V4.1) | $0.30 | $1.20 | 피크 기준. 비피크 $0.15/$0.60, 캐시 별도 |
| `glm-5.3-flash` | $0.15 | $0.50 | 캐시 입력 $0.03, 추론 토큰 고려 필요 |

[Google 공식 가격](https://ai.google.dev/gemini-api/docs/pricing), [Upstage 공식 가격](https://www.upstage.ai/pricing/api), [DeepSeek 공식 가격](https://api-docs.deepseek.com/quick_start/pricing/), [Z.ai 공식 가격](https://docs.z.ai/guides/overview/pricing)

실제 입력·출력·추론 토큰 사용량은 웹 UI에서 모두 확인되지 않아 `usage: null`로 남겼다. UI의 일부 token 숫자를 API 사용량으로 간주하지 않았다. 따라서 이번 자료로 **완성 번역 1만 문장당 실제 비용**을 계산할 수 없다.

## 번역기에 적용할 판단

1. 이번 근거만으로 기본 품질 모델을 3.8에서 Solar로 교체하지 않는다.
2. 3.1 Lite는 대사·짧은 UI에 대한 비용 절감 후보로 남기되, 장문 JSON·개행 검사를 통과한 결과만 사용하고 실패 시 재처리 또는 상위 모델로 넘기는 정책이 필요하다.
3. 어떤 모델도 용어집과 태그·개행 검사 없이 원본 XML에 바로 적용하지 않는다. 이번 raw 비교는 앱의 마스킹·후처리·실제 추론 프리셋이 적용된 품질을 검증한 것이 아니다.
4. GLM/DeepSeek 실제 응답과 Pro 4의 직접 모델 경로를 확보하기 전에는 전체 모델 순위를 확정하지 않는다. 이번 작업은 벤치마크 자료만 추가했고 앱 설정·기본 모델·번역 DB는 바꾸지 않았다.

## 재현 자료

```powershell
python benchmarks/translation/lotd-v1/prepare.py
python benchmarks/translation/lotd-v1/evaluate.py
python benchmarks/translation/lotd-v1/make_report.py
```

세 명령은 로컬 파일만 사용하며 모델을 호출하지 않는다. `prepare.py`는 원본 XML 해시가 다르면 중단한다. 검증기는 원본 행·코퍼스 개수·입력 해시·JSON ID·태그·변수·줄바꿈·페이지 경계를 확인한다. 오류 원문을 고치거나 자동으로 JSON을 복구하지 않는다.

- [고정 코퍼스](dataset.json), [요청 목록·해시](batches.json), [실제 입력](prompts/)
- [원본 응답·실행 메타데이터](runs/), [서비스 오류](runs/service-errors/), [제외한 시도](runs/invalid/)
- [자동 검사](format-results.json), [집계](summary.json), [문장별 원문·응답·AI 검토](comparison.md)
- [Google 무료 한도 화면](evidence/gemini-free-quota.png), [DeepSeek 서비스 실패 화면](evidence/deepseek-nim-error.png), [GLM 대기 화면](evidence/glm-pending.png)

이전 [합성 문장 Gemini 비교](../README.md)는 다른 데이터·추론 설정을 썼으므로 이번 결과와 점수를 합치지 않는다. 공유 대화에 나온 일본어/중국어→한국어 AI 평가 역시 영어 Skyrim 대사에 대한 사람 평가를 대신할 수 없다.
'''
    (ROOT/'README.md').write_text(report,encoding='utf-8',newline='\n')

    by_case = {(r['run'],row['id']):row for r in result['runs'] if r['batch'].startswith('pilot-') for row in r['rows']}
    batch_for_id = {cid:b['batch'] for b in stages['pilot'] for cid in b['ids']}
    packet = ['# LOTD 24개 원문·번역·검토', '',
              '비블라인드 AI 검토이다. 평가 근거가 부족한 화자 신분·고유명사 표준·선행 대화는 확정하지 않는다. 엄격 JSON 실패 응답은 복구하지 않고 원본 링크를 제공한다.', '']
    for c in dataset['cases']:
        if c['id'] not in dataset['pilot_ids']:
            continue
        cid=c['id']
        packet += [f"## {cid} · {c['record']}", '', '```text', c['source'], '```', '']
        if c['glossary']:
            packet += ['제공 용어: '+json.dumps(c['glossary'],ensure_ascii=False), '']
        for model,label in LABELS.items():
            row=by_case.get((model,cid))
            packet += [f'### {label}', '', notes['cases'][cid][model], '']
            if row:
                packet += ['```text', row['text'], '```', '']
            raw=ROOT/'runs'/model/(batch_for_id[cid]+'.txt')
            if raw.exists():
                packet += [f'[원본 응답](runs/{model}/{raw.name})', '']
    (ROOT/'comparison.md').write_text('\n'.join(packet),encoding='utf-8',newline='\n')
    print('Report and 24-case review packet written; source/prompt provenance checks passed.')


if __name__ == '__main__':
    main()
