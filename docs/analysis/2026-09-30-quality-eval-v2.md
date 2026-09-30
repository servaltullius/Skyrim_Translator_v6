# 번역 품질 평가 v2 결과

작성일: 2026-09-30
대상 빌드: `2026.09.30-sndr-preview9`
하네스: `tools/XTranslatorAi.TranslationBenchmark` `eval-run`, `benchmarks/translation/eval-v2/`

## 요약

1. **3.1 Flash-Lite는 40자 이하의 이름·라벨에서만 3.8 Flash와 대등했다.** 그보다 긴 문장, 대화, 책에서는 3.8 Flash가 분명히 나았다. Lite 분담은 짧은 이름 필드에 한정해야 한다.
2. **책 원문 문맥 옵션(bookctx)은 품질 차이를 만들지 못했다.** 14승 12패 31무로 무작위 변동 범위 안이다. 비용은 거의 같았다. 기본값으로 켤 근거가 없다.
3. **모델 선택보다 파이프라인 후처리 결함이 품질에 더 큰 영향을 준다.** 한국어 조사 교정기가 책의 약 40%에서 멀쩡한 문장을 망가뜨리고 있다. 용어집 적용과 토큰 복원에서도 뜻이 바뀌는 오류가 반복된다. 이 결함들은 모델과 설정에 관계없이 똑같이 나타났다.

## 표본과 실행

표본은 720행이다. 엘든림 300행, LOTD 120행, 바닐라 Skyrim.esm 300행이며 시드 20260930으로 고정했다. 표본 파일은 게임 문구가 들어 있어 git에서 제외된 `artifacts/quality-eval-v2/`에만 둔다.

| 실행 | 모델 | 범위 | 호출 | 시간 | 비용 | 실패 행 |
|---|---|---|---:|---:|---:|---:|
| pilot-baseline | 3.8 Flash (Low) | 53행 | 5 | 27초 | $0.038 | 2 |
| baseline-1 | 3.8 Flash (Low) | 720행 | 101 | 286초 | $0.613 | 4 |
| baseline-2 | 3.8 Flash (Low) | 720행 | 102 | 301초 | $0.595 | 3 |
| lite | 3.1 Flash-Lite (Minimal) | 720행 | 115 | 188초 | $0.238 | 3 |
| bookctx | 3.8 Flash + 책 문맥 | 책 59권 | 48 | 196초 | $0.391 | 1 |
| 합계 | | | | | **$1.875** | |

예산 상한은 $6였다. 판정은 API를 쓰지 않았다.

bookctx 비용 $0.39는 baseline 실행에서 책이 차지한 몫과 비슷하다. baseline에서는 책이 다른 필드와 같은 배치에 섞여 정확히 분리할 수 없다. 글자 수 기준 책 비중이 75%이므로 약 $0.46으로 추정한다.

## 판정 방법

Claude가 블라인드로 판정했다. 두 실행 결과를 행마다 무작위로 A/B에 배치했고, 정답표는 판정이 모두 끝난 뒤에만 열었다. 두 번역이 글자까지 같으면 자동 무승부로 처리했다. 판정 일관성을 보려고 약 10%의 행을 A/B 순서를 바꿔 한 번 더 판정했다. 기준은 `benchmarks/translation/eval-v2/README.md`에 있다. 문체 취향만 다른 경우는 무승부로 두었다.

## 결과 1: 3.8 Flash와 3.1 Flash-Lite

판정한 행은 575행이고, 자동 무승부가 145행이다. baseline이 133행, Lite가 92행을 이겼고 350행은 무승부였다. 순서를 바꾼 재판정 57건은 모두 첫 판정과 같았다.

### 원문 길이별

| 원문 길이 | baseline 승 | Lite 승 | 무승부 | 자동 무승부 |
|---|---:|---:|---:|---:|
| 40자 이하 | 48 | 46 | 154 | 137 |
| 41~120자 | 41 | 17 | 123 | 8 |
| 121~400자 | 24 | 18 | 51 | 0 |
| 400자 초과 | 20 | 11 | 22 | 0 |

40자 이하에서는 사실상 동률이다. 41자부터는 baseline이 2배 이상 자주 이긴다.

### 필드별로 차이가 큰 곳

| 필드 | baseline 승 | Lite 승 | 무승부 |
|---|---:|---:|---:|
| INFO:NAM1 (대사) | 25 | 8 | 82 |
| BOOK:DESC | 21 | 11 | 27 |
| MGEF:FULL | 11 | 2 | 22 |
| PERK:DESC | 10 | 1 | 23 |
| DIAL:FULL | 6 | 1 | 12 |
| SPEL:FULL | 6 | 9 | 21 |
| SPEL:DESC | 3 | 7 | 26 |
| MGEF:DNAM | 2 | 5 | 13 |
| ARMO:FULL | 1 | 6 | 6 |

SPEL·MGEF:DNAM·ARMO:FULL 같은 짧은 필드에서는 Lite가 오히려 조금 앞섰다. 다만 표본이 작아 "대등"으로 읽는 것이 맞다.

### 오류 유형

| 유형 | baseline | Lite |
|---|---:|---:|
| accuracy:minor (뉘앙스·세부 의미) | 20 | 57 |
| omission:minor (일부 누락) | 3 | 15 |
| 영어 잔존 행 (자동 지표) | 2 | 9 |
| terminology:major | 9 | 3 |

Lite는 세부 의미를 흘리거나 문장 일부를 빼먹는 일이 많았다. baseline은 고유명사를 뜻풀이하는 실수가 더 많았다. 예를 들어 Rim을 "가장자리"나 "테두리"로 옮겼다.

### 비용과 권고

Lite 실행 비용은 baseline의 39%였다. 하지만 40자 이하 행은 표본의 53%를 차지해도 글자 수로는 5%에 그친다. 절감액은 배치마다 고정으로 드는 프롬프트 비용이 얼마나 되느냐에 달려 있다. 라우팅을 구현한 뒤 실제로 측정해야 한다.

권고는 다음과 같다. Lite 분담은 원문 40자 이하의 이름·라벨 필드(`*:FULL`, 짧은 `DNAM`)에만 적용한다. 대사, 설명문, 책은 3.8 Flash를 유지한다.

## 결과 2: 책 원문 문맥 (bookctx)

판정 대상은 책 59권이다. 순서를 바꾼 재판정 5건이 추가됐고, 자동 무승부는 2권이다.

| | baseline-1 | bookctx | 무승부 |
|---|---:|---:|---:|
| 합계 | 12 | 14 | 31 |
| 엘든림 | 4 | 0 | 4 |
| LOTD | 5 | 6 | 9 |
| 바닐라 | 3 | 8 | 18 |

재판정 5건은 모두 첫 판정과 같았다.

- **차이는 우연 변동 범위 안이다.** 12대 14는 부호 검정으로도 의미가 없다. 같은 설정을 두 번 돌렸을 때도 책 59권 가운데 57권의 결과가 달랐다. 실행 간 변동 자체가 크다.
- **기대했던 효과가 보이지 않았다.** 효과는 제목과 본문 용어의 일관성, 조각 경계의 연결로 기대했다. bookctx가 한 책 안에서 이름 표기를 통일한 사례가 1건 있었다(E0319 Herebane). 반대로 bookctx가 제목과 본문을 다르게 옮긴 사례도 있었다(E0577 순결/정조, E0319 "잃어버린 제국의 전당/방").
- **두 설정은 같은 파이프라인 결함을 공유한다.** 뜻이 바뀐 오류(accuracy:major)는 baseline 11건, bookctx 7건이었다. 대부분 용어집 토큰 뒤바뀜이었고, 양쪽에서 같은 문장이 같은 방식으로 틀린 경우가 많았다.

권고: 책 문맥 옵션은 기본값 OFF를 유지한다. 아래 결함들을 고친 뒤 다시 측정할 가치는 있다.

## 결과 3: 파이프라인 결함

판정 중 확인한 결함이다. 모델과 설정에 관계없이 똑같이 나타났으므로 번역 모델이 아니라 앱 후처리에서 생긴다.

### 1. 한국어 조사 교정기가 정상 문장을 손상 (가장 큼)

`src/XTranslatorAi.Core/Text/KoreanFix/Internal/Steps/AttachedSeparatedParticleStep.cs` 계열이 원인이다. 앞 단어 끝 글자나 지시어 "이"를 조사로 오인해 바꾼다.

| 모델 출력(추정) | 앱 결과 |
|---|---|
| 무언가, 언젠가, 어딘가, 누군가 | 무언이, 언젠이, 어딘이, 누군이 |
| 이 책을 / 그대가 이 책을 | 그대가가 책을 |
| 무엇인가? / 않은가 | 무엇인이? / 않은이 |
| 기꺼이 | 기꺼가 |
| 전문가 | 전문이 |
| 탐험가 길드 | 탐험이 길드 |
| 신화 시대 은 드래곤마크 | 신화 시대는 드래곤마크 |
| 살아남는 | 살아남은 |

의심 패턴으로 대략 세어 보면 책에서는 약 40%에서 나타났다(baseline-1 22/58, baseline-2 24/58, Lite 29/57, bookctx 27/58). 짧은 필드에서는 약 2%였다. 패턴에 오탐과 누락이 모두 있어 정확한 수치는 아니다. 그래도 문단이 긴 책일수록 거의 확실하게 한 번 이상 손상된다.

### 2. 용어집이 일반 단어에 적용됨

용어집 항목이 게임 용어가 아닌 일반 영어 단어에도 걸린다.

| 원문 | 앱 결과 |
|---|---|
| fine blond hair | 하급 금발 |
| that's just fine | 그것이 바로 하급다 |
| Superior officer | 중급 지휘관 |
| master (주인·나리) | 달인님 |
| Elder Scroll | 엘더 주문서 |
| destruction | 파괴마법 |
| sneak up | 은신 삽입 |
| loot | 루팅 |

### 3. 용어 토큰이 서로 뒤바뀌거나 문단 끝에 붙음

용어를 자리표시 토큰으로 바꿨다가 되돌리는 과정에서 위치가 섞인다.

| 원문 | 앱 결과 |
|---|---|
| loot the place … before the Jarl | 루팅이 대처할 틈도 없이 이곳을 야를하고 |
| Jarl of Whiterun | 야를의 화이트런 |
| The sleeping bear of Skyrim, who would not come to aid us in Hammerfell | 스카이림에서 … 잠자는 해머펠의 곰 |
| like the kiai of an Akaviri swordsman | 함성 검사의 기합처럼 아카비르로 |
| for the Emperor's visit to Skyrim | 황제 폐하께서 페니투스 오큘라투스를 방문하시는 동안 스카이림이 |

빠진 토큰은 문단 끝에 덧붙는다.

- "…막아냈다.던머노드"
- "…커지고 있다.레드 마운틴"
- "하급 지휘관(이자 사랑하는 아버지),중급"

책 1권(E0580)에서는 이런 누출이 대량으로 나왔다.

### 4. 기타

- **오류 행 4건 중 3건은 "N%" 보호 토큰 오탐이다.** 나머지 1건은 책의 줄 수 불일치다.
- **프롬프트 예시가 결과에 새어 나온다.** 예: Frostmere Crypt→란베이그의 요새.
- **강제 용어가 적용되지 않은 사례가 있다.** Weapon Arts→전기, The Sallow Regent, Golden Dragon.
- **Alias 태그 순서가 바뀐 사례가 있다(E0519).** 키 자리표시자(예: `[Sprint]`)가 번역되기도 한다.
- **줄 단위로 끊긴 일지에서 문장이 다른 줄로 옮겨진다.** 원문이 문장 중간에서 줄이 바뀌는 경우다(E0308, E0302). 반대로 줄을 지키려다 뜻이 끊기기도 한다.

### 수정 우선순위 제안

1. **조사 교정기.** 영향 범위가 가장 넓다. 명확한 오조사(을(를) 같은 표기)만 고치도록 범위를 줄이고, 원래 단어의 일부인 "가/이"는 건드리지 않게 한다.
2. **용어집 일반 단어 매칭.** Fine, Master, Superior, Destruction, Scroll, Sneak, loot 같은 항목은 대소문자와 문맥 조건을 붙이거나 기본 용어집에서 뺀다.
3. **토큰 복원 검증.** 복원 뒤 토큰 순서가 원문 순서와 다르거나 문단 끝에 남는 경우를 검출해 재시도로 넘긴다.
4. **"N%" 보호 토큰 오탐.**

## 한계

- **판정자가 Claude 한 명이다.** 사람 판정과의 일치도는 아직 확인하지 않았다. 사용자 블라인드 검수 60쌍을 별도로 준비한다.
- **재판정 순서가 편향됐을 수 있다.** 순서를 바꾼 재판정 행이 묶음 끝에 모여 있어서, 판정자가 첫 판정을 기억했을 수 있다. 일치율 100%는 과대평가일 수 있다.
- **엘든림 참조 번역이 이 도구의 이전 출력일 가능성이 높다.** 엘든림 chrF는 "이전 결과와 얼마나 비슷한가"에 가깝다.
- **실행 간 변동이 크다.** 같은 설정을 두 번 돌려도 글자까지 같은 행은 36%였고, 책은 59권 중 1권뿐이었다. 그래서 chrF 평균 차이(48.5 / 47.3 / 46.5)로는 모델을 가를 수 없다.
- **bookctx 표본은 책 59권이다.** 작은 효과를 검출하기에는 부족하다.

## 재현

API 키는 `GEMINI_API_KEY` 환경 변수로만 넘긴다. 실행 폴더는 비어 있어야 하며, 중단된 실행은 `--resume`으로 이어 간다.

```bash
dotnet run --project tools/XTranslatorAi.TranslationBenchmark -- eval-run artifacts/quality-eval-v2/dataset.json baseline artifacts/quality-eval-v2/runs/baseline-1 --budget-usd 6
python benchmarks/translation/eval-v2/metrics.py artifacts/quality-eval-v2/dataset.json artifacts/quality-eval-v2/runs/baseline-1 artifacts/quality-eval-v2/runs/baseline-2 artifacts/quality-eval-v2/runs/lite artifacts/quality-eval-v2/runs/bookctx --out artifacts/quality-eval-v2/metrics-main.json
python benchmarks/translation/eval-v2/make_judge_packets.py artifacts/quality-eval-v2/dataset.json artifacts/quality-eval-v2/runs/baseline-1 artifacts/quality-eval-v2/runs/lite artifacts/quality-eval-v2/judge/base-vs-lite
python benchmarks/translation/eval-v2/aggregate_verdicts.py artifacts/quality-eval-v2/dataset.json artifacts/quality-eval-v2/judge/base-vs-lite --out artifacts/quality-eval-v2/judge/base-vs-lite/summary.json
```
