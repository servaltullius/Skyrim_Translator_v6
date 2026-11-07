# 툴리우스 번역기 폴더

지금 연 폴더가 이 폴더(`TulliusTranslator.exe`가 바로 들어 있는 폴더)가 아니면 이 파일은 건너뛰고 연 폴더의 `AGENTS.md`만 따른다. 이 파일은 번역기 폴더 자체를 열었을 때의 안내다.

이 폴더는 툴리우스 번역기(`TulliusTranslator.exe`)가 있는 폴더다. 에이전트 모드의 규칙과 안내는 `agent-kit` 폴더에 있다.

- 어떤 작업이든 시작하기 전에 `agent-kit\AGENTS.md`를 끝까지 읽고 그대로 따른다. 그 파일이 가리키는 안내(`guide-translate.md`, `guide-review.md`, `guide-lanes.md`)도 `agent-kit` 폴더에 있다. 셸로 파일을 읽을 수 없으면 `tullius` 도구의 `guide`로 읽는다.
- 그 파일의 "이 폴더"는 `agent-kit`이지만, 이 폴더를 열었어도 똑같이 하면 된다.
  - `tullius` 도구(MCP)는 이 폴더의 `.mcp.json`·`.claude\settings.json`(Claude)과 `.codex\config.toml`(Codex)도 등록한다.
  - `.\tullius.cmd`도 이 폴더에 있다. `agent-kit\tullius.cmd`와 같은 명령이다.
  - 프로젝트와 저장 결과는 어느 폴더를 열어도 `agent-kit\work`, `agent-kit\output`에 있다. 그 안내의 `work\…`는 `agent-kit\work\…`로 읽는다. 인자 파일(`args.txt`)이나 답 파일은 `agent-kit\work\` 아래에 쓰고, 인자 파일은 전체 경로로 넘긴다(`.\tullius.cmd "@<args.txt의 전체 경로>"`).
  - `안티그라비티 연결.cmd`와 `사용법.txt`도 `agent-kit` 폴더에 있다.
- 번역기 EXE를 직접 부르지 않는다. 언제나 도구나 `tullius.cmd`로 부른다.
