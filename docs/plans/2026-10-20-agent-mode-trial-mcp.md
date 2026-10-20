# 에이전트 모드 시험 9: MCP 도구로 번역하기 (2026-10-20)

[시험 8](2026-10-20-agent-mode-trial-codex-teldryn.md)과 Codex 조사 보고서(`artifacts/codex-research-20261020/report.md`)에서 나온 문제가 있었다.
- 셸이 한국어를 따옴표로 감싸는 방식이 앱마다 달랐다.
- PowerShell 5.1은 답 파일을 UTF-16·CP949로 썼다.
- cmd가 `&`에서 인자를 쪼갰다.

이 문제는 모두 `tullius.cmd`를 셸로 부르기 때문에 생긴다. 그래서 같은 명령을 MCP 도구로 내놓는 서버(`TulliusTranslator.exe agent mcp`)를 만들고 실제 앱에 연결했다.

- 시험 폴더: `artifacts/mcp-trial-20261020/` (Codex 출력·로그, 내보낸 플러그인, 시험 스크립트)
- 빌드: 1.16.3(d4018e8) + 이번 변경(Debug)

## 서버

- stdio, 줄마다 JSON-RPC 하나. stdout에는 프로토콜만 쓰고, 다른 출력은 stderr로 보낸다.
- 지원하는 프로토콜 판: 2025-11-25, 2025-06-18, 2025-03-26, 2024-11-05.
- 도구는 CLI 명령과 이름·인자가 같다: open, status, next, request, instructions, submit, lookup, voice, style, show, check, edit, review, korean, export, version, guide.
  - 각 도구는 그 CLI 명령을 출력을 받아 실행하므로 결과가 `tullius.cmd`와 같다.
  - 앱이 켜져 있을 때 쓰기를 거부하는 것도 같다.
- 답은 `submit`의 `answer`(문자열), 수정은 `edit`의 `edits`(배열) 인자로 받는다. 서버가 `work\.mcp\`에 UTF-8로 써서 넘긴다.
- `next`·`submit` 결과 끝에 다음 request.md 내용이 `----- 다음 요청 -----` 아래로 함께 온다.
- `lookup`은 `words` 배열로 여러 낱말을 한 번에 찾는다.
- `--work`가 없으면 번역기 옆 `agent-kit\work`를 쓴다. `tullius.cmd`와 같은 곳이다.

## 연결

| 앱 | 방법 | 결과 |
|---|---|---|
| Codex CLI 0.162.1 | agent-kit의 `.codex\config.toml`(명령 `..\TulliusTranslator.exe`, 상대 경로) | 폴더를 열면 tullius 도구가 잡힘 |
| Codex CLI | `codex mcp add tullius -- "<EXE>" agent mcp` (임시 CODEX_HOME에서 확인) | 전역 설정에 같은 항목 |
| Claude Code 2.1.220 | agent-kit의 `.mcp.json`(상대 경로) | `claude mcp get tullius`: Connected. 처음 한 번 허용이 필요 |
| Claude Code | `claude mcp add --scope project` | 같은 `.mcp.json` 형식 |
| Gemini CLI 0.3.0-preview.1 | 시험 폴더 `.gemini\settings.json` | MCP와 상관없이 시작부터 실패(`wmic` 없음) |

- Claude Code는 처음에 CLI 로그인(OAuth)이 만료돼 연결 확인만 했다. 다시 로그인한 뒤 번역까지 시험했다(아래 "Claude 시험").
- 안티그라비티·Gemini CLI는 폴더별 설정이 없어 전체 경로가 필요하다. `tullius.cmd mcp-config`가 이 번역기의 경로를 넣은 설정을 보여 준다.

## Codex 시험

1. **지시한 절차(Teldryn 첫 요청)**
   - 프롬프트는 "MCP 도구만 써서 open → next → instructions → submit"이었다.
   - 36초 만에 32줄을 저장했고, 다시 물은 줄은 없었다. 31,527토큰을 썼다.
   - 한국어·따옴표·줄바꿈이 든 답도 문제없었다(앞서 파이썬 클라이언트로 `&`·따옴표·줄바꿈 답도 확인).
2. **사용자처럼 "○○.esp 번역해 줘"(EldenWarAshPack1, 150줄)**
   - 첫 번째 실행
     - Codex의 셸(`exec_command`)이 이 PC의 샌드박스 오류(`setup refresh had errors`)로 시작되지 않았다.
     - 그래서 `guide-translate.md`를 읽지 못했다. tullius 서버의 리소스 목록까지 찾아본 뒤 "안내 파일을 붙여 달라"며 멈췄다.
   - 고친 것
     - `guide` 도구와 MCP 리소스(`tullius://guide/translate` 등)를 더해, 셸 없이도 AGENTS.md와 안내 파일을 읽게 했다.
     - AGENTS.md에 "셸로 못 읽으면 `guide` 도구로"를 적었다.
   - 다시 실행: 2분 10초 만에 150줄을 번역하고 품질검사·저장까지 끝냈다. 74,577토큰을 썼다.
     - 셸은 한 번도 쓰지 않았다(2번 시도했다가 실패한 뒤 도구로 넘어감).
     - 부른 도구: guide 1, open 1, style 1, next 1, instructions 1, lookup 5, submit 5, show 3, check 1, export 1.
     - 남은 경고 4건(#34·36·38·40)은 Codex가 오탐으로 판단했다. 확인해 보니 맞다. 동사 Summon을 이름으로 보고 '소환한/소환해'의 어미 차이를 표기 불일치로 짚었다. 품질검사 쪽 문제로 따로 고칠 거리다.

## Claude 시험

Codex의 두 번째 시험과 같은 플러그인(EldenWarAshPack1, 150줄)을 썼다. "○○.esp 번역해 줘"만 주었다.
- 실행: Claude Code 2.1.220, `claude -p`, 시험용 agent-kit 사본에서 그 폴더의 `.mcp.json`으로 연결.
- 허용 도구: tullius 도구와 Read·Glob·Grep. 셸과 파일 쓰기는 막았다.

| | Codex | Claude |
|---|---|---|
| 걸린 시간 | 2분 10초 | 5분 49초 |
| 제출 | 5회 | 6회(#81 Dragon's Maw가 토큰 오류로 한 번 되돌아옴) |
| 남은 품질검사 | 4건(Summon 오탐, 91b7025로 고침) | 0건 |
| 토큰 | 74,577 | 출력 23,470, 캐시 읽기 1,273,531 |
| 안내 읽기 | 셸 실패 뒤 `guide` 도구 | 처음부터 `guide` 도구(Read를 허용했는데도) |

- Claude Code는 MCP 도구를 미뤄 두었다가 `ToolSearch`로 불러온다(2번). 그 밖의 오류는 없었다.
- Claude가 새로 정한 이름 가운데 "Starscourge of Arrows → 별 바수는 화살"은 어색하다. 번역 품질 문제이고 도구와는 상관없다.
- 출력: `artifacts/mcp-trial-20261020/mcp-claude/`(stream-json 기록과 내보낸 플러그인).

## 안티그라비티 시험

1.16.4 루트 EXE로 같은 플러그인(EldenWarAshPack1, 150줄)을 안티그라비티 2.24.0에 맡겼다. 화면은 컴퓨터 유즈로 조작했다.

- **등록**
  - 안티그라비티는 전역 설정 `~/.gemini/config/mcp_config.json` 하나만 읽는다. 폴더별 설정이 없어 agent-kit에 넣어 둘 수 없다. `tullius.cmd mcp-config`가 내놓는 형식 그대로 넣었다(시험에서는 `--work`, `--projects`를 시험 폴더로 지정).
  - 켜져 있는 동안 바꾼 설정은 다시 읽지 않는다. 앱을 닫았다 다시 열자 Customizations → Installed → MCP Servers에 "tullius · 17 tools enabled"로 보였다.
  - 시험이 끝난 뒤 설정 파일은 원래대로(빈 파일) 되돌렸다.
- **작업 폴더**: 안티그라비티에 이미 있던 "agent-kit" 프로젝트(루트 `agent-kit`)에서 새 대화를 열고 "○○.esp 번역해 줘"만 입력했다. 모델은 기본값 Gemini 3.8 Flash(Medium)였다.
- **허락 창**: 도구마다 처음 부를 때 "Allow using this MCP tool?"을 묻는다. "이 대화에서는 항상 허용"을 골랐고, 7번 물었다(open, style, next, lookup, check, show, export). 프로젝트 단위나 전체 허용도 고를 수 있다.
- **결과**: 4분 만에 150줄을 저장했고, 품질검사(`--all`)는 0건이었다.
  - 안내 파일과 instructions.md는 파일로 직접 읽었다.
  - 긴 도구 결과는 안티그라비티가 `output.txt`로 받아 나눠 읽었다.
- **번역**: 뜻은 맞지만 이름 몇 개는 어색하다. Floating Passage를 "유주"로 옮겼고, "Learning - " 접두어는 "배우기"로 썼다(Claude·Codex는 "습득").

| | Codex | Claude | 안티그라비티 |
|---|---|---|---|
| 걸린 시간 | 2분 10초 | 5분 49초 | 약 4분(허락 창 대기 포함) |
| 남은 품질검사 | 4건(오탐, 고침) | 0건 | 0건 |
| 등록 | 폴더의 `.codex\config.toml` | 폴더의 `.mcp.json` | 전역 설정에 직접(`mcp-config`) |

## 세 앱의 번역 비교

같은 150줄을 비교했다. 기술 이름 50개가 효과(MGEF)와 주문서(SCRL)에 한 번씩 나오고, 효과 설명이 50줄이다. 줄별 표는 `artifacts/mcp-trial-20261020/compare-3apps.tsv`에 있다.
- 세 앱이 글자까지 같은 줄은 19줄이다. 주로 TM에 있는 검은 책 주문 이름(백일몽, 통찰의 서간 등)과 볼렌드렁·오그마 인피니움이다.
- 기술 이름 50개 가운데 셋이 같은 것은 20개다.
- 세 앱 모두 같은 이름을 효과와 주문서에 똑같이 썼다. 이름 기억과 품질검사가 잡아 준다.

| | Codex (GPT) | Claude | 안티그라비티 (Gemini 3.8 Flash) |
|---|---|---|---|
| "Learning - " | 습득 - | 습득 - | 배우기 - (이름으로 어색함) |
| 설명 첫 문장 | 대부분의 이들이 범접하기 어려운 기술입니다. | 보통 사람은 닿을 수 없는 경지의 기술입니다. | 평범한 자는 다다를 수 없는 기술입니다. |
| 설명의 뜻 | 정확함 | 정확함 | 몇 줄 틀림(아래) |
| 이름 | 대체로 무난, 몇 개는 틀림 | 가장 자연스러움, 몇 개는 어색 | 직역이 많고 몇 개는 틀림 |
| 걸린 시간 | 2분 10초 | 5분 49초 | 약 4분 |

**틀리거나 어색한 이름**
- Floating Passage
  - Codex "쪽배 건너기": passage를 배로 건넌다는 뜻으로 읽었다. 틀렸다.
  - Claude "부유 보법": 맞다.
  - Gemini "유주": 뜻이 통하지 않는다.
- Stamp (Upward Cut / Sweep)
  - Codex "파고들기 (올려 베기 / 회전 베기)": Stamp(발 구르기)를 다르게 읽었다.
  - Claude "짓밟기 (올려베기 / 쓸어베기)": 뜻에 가장 가깝다.
  - Gemini "참격 (차올리기 / 회전)": 둘 다 틀렸다.
- Evocation ○○: Codex·Claude는 "소환 ○○", Gemini는 "환영 ○○"이다. 설명이 "소환해"이니 Gemini 쪽이 틀렸다.
- Starscourge of Arrows: Codex "별 부수는 화살", Claude "별 바수는 화살"(어색함), Gemini "별 부수는 자의 화살"(칭호로 읽음).
- Dragon's Maw: "아가리"(Codex), "아귀"(Claude, 어색함), "턱"(Gemini).
- 그 밖에 갈린 것
  - Kaiten: 회천 / 카이텐
  - Perfect Rush: 퍼펙트 러시 / 완벽한 연격
  - Farron's Stride: 팔란 / 파론
  - First Flame: 최초의 불 / 최초의 불꽃 / 태초의 불

**설명에서 Gemini가 틀린 줄**
- #78 "Drag your weapon to the ground to charge": 힘을 모은다는 뜻의 charge를 "돌진한 뒤"로 옮겼다.
- #28 "carrying the momentum": "반동을 살려"로 옮겼다(기세가 맞다).
- #10 "daedric warhammer": "데이드릭 전투 망치"로 썼다. 다른 둘은 한글패치식 "데이드라제"를 썼다.
- #36: 주어가 꼬였다("화살을 쏘아 곡선을 그리며 떨어져 … 타격하게 합니다").

**그 밖의 차이**
- 띄어쓰기: Codex는 "한손 무기", Claude·Gemini는 "한손무기"로 썼다.
- 원문에 공백이 빠진 "Rim Scroll- ○○"(#129~131): Codex만 다른 줄처럼 "림 주문서 - "로 맞췄다.
- 조작 키: Gemini는 "[direction button]"을 "[방향키]"로, 다른 둘은 "[방향 버튼]"으로 옮겼다.

**정리**
- 이 모드에서는 Claude와 Codex가 비슷하게 좋았다.
  - Claude는 이름이 가장 자연스러웠다.
  - Codex는 가장 빨랐고 설명이 정확했지만, 이름 두셋은 틀렸다.
- Gemini 3.8 Flash(안티그라비티 기본 모델)는 문장은 매끄러웠지만, 뜻이 틀린 줄과 직역한 이름이 가장 많았다. 안티그라비티에서 Pro 모델을 고르면 다를 수 있다(시험하지 않음).
- 세 앱 모두 이름 일관성·태그·품질검사에는 문제가 없었다. 도구가 맡는 부분은 앱과 상관없이 같게 돌았다.

## 남은 것

- 배포할 때 agent-kit zip에 `.mcp.json`과 `.codex\` 폴더를 넣는다. 점으로 시작해 탐색기 복사에서 빠지기 쉽다.
- ~~Claude로 실제 번역하는 시험.~~ 했다(위). Code 탭에서 `.mcp.json`을 허용하는 첫 확인 창은 따로 보지 못했다.
- 다음 키트 zip: `artifacts/release-1.16-20261020/kit-1.16.4/`(점 파일 2개 포함, git의 agent-kit 10개와 같음). 1.16.4로 배포했다.
- 갈래 번역을 MCP로 하는 시험. 하위 에이전트마다 서버가 따로 뜨는지, 같은 서버를 나눠 쓰는지 앱마다 다르다.
- ~~이름 표기 불일치 검사가 문장 첫머리의 동사(Summon)를 이름으로 보는 오탐.~~ 고침: 문장 중간이나 한 낱말 줄에서 대문자로 나온 낱말만 이름으로 본다. 실제 프로젝트 사본 7개는 품질검사 건수가 그대로였다.
