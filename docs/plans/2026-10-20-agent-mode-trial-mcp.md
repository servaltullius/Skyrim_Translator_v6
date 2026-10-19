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

- Claude Code로 실제 번역은 하지 못했다. CLI 로그인(OAuth)이 만료돼서다. 연결 상태 확인까지만 했다.
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

## 남은 것

- 배포할 때 agent-kit zip에 `.mcp.json`과 `.codex\` 폴더를 넣는다. 점으로 시작해 탐색기 복사에서 빠지기 쉽다.
- Claude(Code 탭)로 실제 번역하는 시험. CLI 로그인을 새로 하면 할 수 있다.
- 갈래 번역을 MCP로 하는 시험. 하위 에이전트마다 서버가 따로 뜨는지, 같은 서버를 나눠 쓰는지 앱마다 다르다.
- 이름 표기 불일치 검사가 문장 첫머리의 동사(Summon)를 이름으로 보는 오탐.
