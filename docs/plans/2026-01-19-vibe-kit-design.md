# vibe-kit 설계 (Skyrim_Translator_v6)

## 목표
- 레포 구조/핵심 파일/위험 신호를 빠르게 파악하는 “지도의 자동 생성”.
- 플레이스홀더/토큰 관련 번역 리스크를 QA로 빠르게 찾는 “진단기”.
- 가능한 경우 git pre-commit으로 “오류 증가/순환 참조”를 차단(빠른 staged-only).

## 원칙
- diff 폭탄 금지: 코드를 자동으로 대량 수정하지 않는다.
- RAG 임베딩 금지: SQLite FTS로 시작한다.
- Windows 우선: Python 엔트리 + wrapper 제공.

## 구성
- `scripts/vibekit.py`: 단일 진입점(필요 시 자동 초기화)
- `.vibe/config.json`: 스캔 범위/게이트 설정
- `.vibe/db/context.sqlite`: 파일/심볼/간이 의존성 + FTS 인덱스
- `.vibe/context/LATEST_CONTEXT.md`: 최근 변경/경고/핫스팟/다음 행동 요약

## 커맨드(예정)
- `doctor`: 전체 스캔 + 리포트 생성
- `watch`: 변경 감시 + LATEST_CONTEXT 자동 갱신
- `impact <file>`: 영향도 분석(텍스트/FTS 기반 휴리스틱)
- `qa <xml>`: xTranslator XML에서 플레이스홀더/단위 오염 패턴 탐지
- `precommit`: (git 존재 시) staged-only 게이트 체인

