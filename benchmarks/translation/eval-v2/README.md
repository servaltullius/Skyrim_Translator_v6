# 번역 품질 평가 v2

앱의 실제 번역 파이프라인으로 고정 표본 720행을 번역하고, 설정별 결과를 비교한다. 목적은 다음 세 가지 판단이다.

1. 짧은 필드를 3.1 Flash-Lite로 분담할지
2. 책 원문 문맥 옵션을 기본값으로 켤지
3. 이후 프롬프트 변경 때 다시 돌릴 기준 세트 마련

## 표본

`build_dataset.py`가 고정 시드(20260930)로 만든다. 결과 파일에 게임·모드 문구와 커뮤니티 번역이 들어 있으므로 git에서 제외된 `artifacts/quality-eval-v2/`에만 둔다. 입력 파일의 SHA-256은 dataset에 기록된다.

| 묶음 | 행 | 참조 번역 | 비고 |
|---|---:|---|---|
| A. 엘든림 Base 3.7.5 | 300 | 설치된 3.7.2 한국어판 | 영어 원문이 두 버전에서 같은 행만 사용. 사용자의 엘든림 프로젝트 용어집 적용 |
| B. LOTD v6.7.2 | 120 | 없음 | 기존 `lotd-v1` 고정 세트 |
| C. 바닐라 Skyrim.esm | 300 | 커뮤니티 한국어 번역 | 모델이 학습했을 수 있어 유사도 점수는 참고용 |

## 번역 실행

`TranslationBenchmark eval-run`이 앱과 같은 `TranslationService`와 기본 옵션을 쓴다. 적용 옵션은 배치 12행·15,000자·병렬 2, REC 힌트, 의미 수리(Soft), 대화 문맥, 세션 용어 기억, 위험 후보 재선택 3개, 내장 TES 용어집이다. 프롬프트 캐시·TM·프로젝트 문맥 생성·품질 승격은 끈다. 평가 폴더 전체의 누적 금액이 `--budget-usd`에 닿으면 즉시 중단한다.

| 설정 | 모델 | 범위 | 반복 |
|---|---|---|---|
| baseline | gemini-3.8-flash (Low) | 720행 | 2회 |
| lite | gemini-3.1-flash-lite (Minimal) | 720행 | 1회 |
| bookctx | gemini-3.8-flash + 책 문맥 | BOOK:DESC 행 | 1회 |

실패한 행은 앱 화면과 같이 원문이 유지된 결과로 평가한다. 파이프라인 자체의 오류율도 지표로 기록한다.

## 판정

`make_judge_packets.py`가 두 실행 결과를 행마다 무작위로 A/B에 배치한다. 정답표 `key.json`은 모든 판정이 끝날 때까지 열지 않는다. 두 번역이 완전히 같으면 자동 무승부로 처리한다. 판정 일관성을 보기 위해 10% 표본을 A/B 순서를 바꿔 한 번 더 판정한다.

판정자는 Claude(대화 세션)다. 번역 모델과 다른 모델이므로 자기 선호 편향을 피한다. 다만 사람의 평가를 대신하지 않으므로, 사용자가 60쌍을 블라인드로 검수해 판정과의 일치도를 확인한다.

행마다 다음을 기록한다.

- `winner`: `A`, `B`, `tie`
- 각 후보의 오류 목록. 유형과 심각도를 함께 적는다.
  - 유형: `accuracy`(의미 오류·수치·조건·주체 뒤바뀜), `omission`(누락), `addition`(원문에 없는 내용), `terminology`(용어집·통용 번역 위반, 같은 대상의 표기 불일치), `fluency`(어색한 한국어·조사 오류·번역투), `format`(태그·변수·줄바꿈 훼손, 미번역)
  - 심각도: `major`(뜻이 달라지거나 게임 이해를 해침), `minor`(뜻은 통하지만 다듬어야 함)
- `note`: 판단 근거 한 줄

판단 기준은 다음 순서로 적용한다. 원문 의미의 정확성, 누락·추가 여부, 용어, 형식, 자연스러움이다. 참조 번역은 정답이 아니며 해석이 모호할 때만 참고한다. 문체 취향만 다른 경우는 `tie`로 둔다.

## 자동 지표

`metrics.py`는 API 없이 다음을 계산한다. 실패 행, 미번역, 영어 잔존, `을(를)` 같은 모호한 조사 표기, 비정상 길이 비율, 엘든림 용어(전기·전회) 준수율, 참조 번역과의 chrF(A·C), 같은 설정 두 번 실행의 일치율이다. chrF는 상대 비교용이며 품질 점수로 해석하지 않는다.

## 사람 검수

Claude 판정이 사람 판단과 맞는지 확인하려고 판정 결과별(X 승, Y 승, 무승부)로 20쌍씩, 모두 60쌍을 뽑는다. 원문은 400자 이하만 쓴다. 검수 페이지에는 모델 이름과 Claude 판정이 나오지 않는다.

```bash
python benchmarks/translation/eval-v2/make_human_review.py artifacts/quality-eval-v2/dataset.json artifacts/quality-eval-v2/judge/base-vs-lite artifacts/quality-eval-v2/human-review
```

`review.html`을 브라우저로 열어 답하고 "결과 저장"으로 JSON을 내려받는다. 일치도는 다음으로 계산한다.

```bash
python benchmarks/translation/eval-v2/make_human_review.py artifacts/quality-eval-v2/dataset.json artifacts/quality-eval-v2/judge/base-vs-lite artifacts/quality-eval-v2/human-review --compare human-review-answers.json
```

## 결과

2026-09-30 실행 결과와 발견한 파이프라인 결함은 `docs/analysis/2026-09-30-quality-eval-v2.md`에 정리했다.
