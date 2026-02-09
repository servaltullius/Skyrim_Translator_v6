# Windows xTranslator XML 번역기 (Gemini) — 상세 계획서

## 0) 목표/범위

### 목표
- xTranslator에서 추출한 XML(`SSTXMLRessources`)을 읽어 **`<Source>` → Gemini 번역 → `<Dest>` 채움**을 수행한다.
- xTranslator처럼 **원문/번역문을 테이블로 보고**, 번역이 진행될 때 **행이 실시간으로 채워지는 모습**을 제공한다.
- 대용량 XML(수만~수십만 행)을 **멈춤/재시작/크래시 복구** 가능한 구조로 만든다.
- **플레이스홀더/태그/포맷 문자열을 깨뜨리지 않는 것**을 최우선 품질 기준으로 둔다.
- **용어집(Glossary)은 항상 적용(강제)**하며, **커스텀 프롬프트는 옵션**으로 제공한다.
- 기본 번역 프롬프트는 `메타프롬프트.md`를 기반으로 한다.

### 비범위(당장 하지 않음)
- ESP/Strings/BSA 등 xTranslator의 다른 기능 대체 (XML만)
- XML 파일 여러 개 동시 작업(한 번에 1개만)
- 토큰 단위 스트리밍(부분 글자 단위로 한 문장이 “타이핑되듯” 출력) — 필요 시 V2에서 검토

## 1) 권장 기술 스택(Windows 전용)

- 런타임/언어: `.NET 8` + `C#`
- UI: `WPF` + `MVVM` (권장: `CommunityToolkit.Mvvm`)
- 로컬 저장(프로젝트/캐시/복구): `SQLite` (권장: `Microsoft.Data.Sqlite`)
- HTTP/JSON: `HttpClient` + `System.Text.Json`
- 로깅(권장): `Serilog`(파일 로그)
- 배포(권장): `MSIX`

## 2) Gemini 모델/엔드포인트 전략

- API 형태: **Gemini API Key(= Google AI Studio / Generative Language API)** 전제
- 기본 모델(제안): `gemini-2.5-flash`
  - UI에서 `gemini-3-flash`, `gemini-3-pro` 등으로 변경 가능
- 모델 목록/검증:
  - “모델 목록 새로고침” 버튼: `GET /v1beta/models?key=...` 호출로 실제 사용 가능 모델 표시(키 보유 시)
  - “연결 테스트” 버튼: 짧은 테스트 프롬프트 1회 호출로 권한/모델 유효성 검증
- 참고 문서(모델명 확인): https://ai.google.dev/gemini-api/docs/models

## 3) 데이터 흐름(Import → 작업 → Export)

1. **XML 열기**
   - XML 헤더/BOM/`<Params>`(Addon/Source/Dest/Version) 읽기
2. **DB 프로젝트 생성**
   - 기본 위치(권장): `%LOCALAPPDATA%\\XTranslatorAi\\Projects\\...`에 프로젝트 DB 생성
     - 이유: 입력 XML이 `\\wsl.localhost\\...`(WSL/UNC) 같은 네트워크 경로에 있을 때 SQLite(WAL/락) 문제가 발생할 수 있어, DB는 항상 Windows 로컬 경로에 두는 편이 안전
3. **XML Import(스트리밍)**
   - `XmlReader`로 `<Content>/<String>`를 순회하며 DB에 적재(메모리 폭발 방지)
4. **UI 표시**
   - DataGrid(가상화)로 원문/번역문/상태를 보여줌
5. **번역 작업 실행**
   - “미번역(기본 규칙)”만 큐에 넣고, 배치/재시도/캐시로 처리
   - 완료되는 즉시 해당 행의 번역이 UI에 반영(실시간)
6. **저장/내보내기**
   - Export 시 DB → XML 재생성
   - 임시파일에 쓰고 완료 후 원자적 rename + 백업 생성(안정성)

## 4) “미번역” 판정 규칙(기본)

- 기본 번역 대상:
  - `<Dest>`가 비어있음
  - 또는 `<Dest>`가 `<Source>`와 동일(공백 트림 후 비교)
- 기본 덮어쓰기 금지:
  - `<Dest>`가 `<Source>`와 다르면 “기존 번역”으로 보고 유지
  - 사용자 선택으로 “전체 덮어쓰기/선택 덮어쓰기” 지원

## 5) DB 스키마(초안)

### Project(1개)
- `Id`
- `InputXmlPath`
- `AddonName`, `SourceLang`, `DestLang`, `XmlVersion`
- `XmlHasBom`, `XmlPrologLine`(원문 보존용)
- `ModelName`
- `BasePromptText`(기본=메타프롬프트 기반), `CustomPromptText`(옵션), `UseCustomPrompt`
- 생성/수정 시각

### StringEntry(N개)
- `Id`(PK), `OrderIndex`(원본 순서)
- `ListAttr`, `PartialAttr`, 기타 `<String>` attribute JSON
- `EDID`, `REC`
- `SourceText`, `DestText`
- `Status`(Pending/InProgress/Done/Error/Skipped/Edited)
- `ErrorMessage`
- `RawStringXml`(원본 `<String>...</String>` 전체 XML 문자열; Export 시 보존/복원용)
- `UpdatedAt`

### Glossary(항상 적용)
- `Id`, `SrcTerm`, `DstTerm`, `Enabled`
- `MatchMode`(WordBoundary/Substring/Regex)
- `ForceMode`(ForceToken/PromptOnly) — 기본 ForceToken
- `Note`, `Priority`

### TranslationMemory(캐시/중복 제거)
- `KeyHash`(언어+모델+프롬프트버전+용어집버전+원문+마스킹전략)
- `TranslatedText`
- `CreatedAt`

## 6) 플레이스홀더/태그 보호(핵심 품질)

### 보호 대상(최소 세트)
- `<mag>`, `<Alias=...>`, `<font ...>` 등 **`<...>` 형태 태그**
- `%0f`, `%d`, `%s`, `%02d` 등 **printf 스타일 포맷 토큰**
- 줄바꿈(`\n`) — 개수/위치 유지

### 전략
1) 번역 전 `SourceText`에서 보호 토큰을 `__XT_PH_0001__` 형태로 마스킹  
2) Gemini에는 “토큰을 절대 변경/삭제/재정렬하지 말라” 규칙을 강하게 부여  
3) 번역 후 검증:
   - 모든 `__XT_PH_####__`가 존재하는지
   - 줄바꿈 개수/위치가 동일한지
4) 실패 시:
   - 해당 행을 `Error`로 표기, 원인 로그 저장
   - 자동 재시도(배치 반으로 쪼개기 포함) 후에도 실패하면 사용자 검수로 남김

## 7) 용어집(항상 적용) 구현 방식

### 기본(강제 토큰 방식, 항상 적용)
- 원문에서 용어 매칭되는 구간을 `__XT_TERM_0001__`로 치환하고, 모델에 “TERM 토큰은 그대로 두라”고 지시
- 번역 결과에서 TERM 토큰을 **반드시 `DstTerm`으로 치환**
- 장점: 모델이 용어집을 무시해도 **100% 강제**
- 단점: 한국어 조사/활용이 자연스럽지 않을 수 있어, 추후 “조사 보정” 옵션(V2) 고려

### 보조(프롬프트 방식, 옵션)
- 배치에 포함된 용어만 추려 프롬프트에 주입(토큰 절약)
- ForceToken이 어색한 용어는 PromptOnly로 운영 가능

## 8) 프롬프트 시스템

### 기본 프롬프트
- `메타프롬프트.md`를 “기본 템플릿”으로 사용
- 앱 배포 시:
  - 기본 프롬프트를 리소스로 내장 + “외부 파일로 내보내기/불러오기” 제공

### 커스텀 프롬프트(선택)
- 체크박스로 On/Off
- 커스텀 프롬프트는 기본 프롬프트의 “추가 지시문”으로 합성(기본을 덮어쓰지 않게)

### 모델 입력/출력 형태
- `responseMimeType: application/json` 사용
- 출력 스키마 고정(배치 번역):
  - `{"translations":[{"id":123,"text":"..."}]}`

## 9) 번역 엔진(실시간/안정성/효율)

### 큐/동시성
- `Channel<int>`(StringEntryId) 기반 작업 큐
- 동시 요청 개수 제한(기본 2~3) + 레이트리밋 대응
- 취소/일시정지:
  - CancellationToken으로 즉시 중단
  - Pause는 “새 요청 발행 중단 + 진행 중 요청은 완료 후 멈춤”

### 배치 규칙(예시)
- 배치 크기: 10~30개(설정 가능)
- 배치 최대 문자 수: 8k~12k(설정 가능)
- 실패 시:
  - 429/5xx: 지수 백오프 재시도
  - 파싱 실패/검증 실패: 배치 반으로 쪼개기(문제 행 격리)

### 캐시/중복 제거
- 동일 원문(마스킹/용어집/프롬프트/모델 동일)은 TranslationMemory에서 즉시 재사용
- 같은 Source가 여러 행에 있으면 “한 번만 번역 후 복제”도 옵션 제공(추후)

## 10) UI 설계(MVP)

### 메인 화면
- 상단: 열기/내보내기, 시작/일시정지/중지, 진행률(%)·남은 개수·속도·실패 수
- 중앙 DataGrid(가상화):
  - 컬럼: `Status`, `EDID`, `REC`, `Source`, `Dest`, `Error`
  - 인라인 편집: Dest 더블클릭 편집, 수정 시 Status=Edited
- 좌측/우측 패널(탭):
  - Gemini 설정(모델/키/동시성/배치/재시도)
  - 용어집 편집(추가/수정/CSV import/export)
  - 프롬프트(기본 보기, 커스텀 On/Off)

### 필터/검색
- 미번역만, 오류만, 수정됨만, 전체
- 텍스트 검색(원문/번역문)

## 11) 내보내기(Export) 안정성

- Export는 항상:
  1) `output.tmp`에 XmlWriter로 작성
  2) 완료/검증 후 기존 파일 백업(`.bak`)
  3) `tmp → output` 원자적 rename
- Export 시 `<String>` 보존:
  - `RawStringXml`을 로드 → `<Dest>`만 현재 `DestText`로 치환 → 기록
  - 알 수 없는 노드/속성도 유지(보존 전략)

## 12) 보안/키 관리

- Gemini API Key는 평문 저장 금지
- 선택지:
  - Windows Credential Manager 저장(권장)
  - 또는 DPAPI로 암호화하여 사용자 설정에 저장

## 13) 테스트/검증 계획

### 단위 테스트
- 플레이스홀더 마스킹/복원(태그/포맷/줄바꿈)
- 용어집 강제 토큰 적용/복원(영문 word boundary, CJK substring)
- XML `<String>` round-trip: RawStringXml → Dest 치환 → 다시 파싱 가능

### 통합 테스트(샘플 파일 기반)
- `LegacyoftheDragonborn_english_korean.xml` Import → 일부 행 Dest 수정 → Export → xTranslator import 가능 여부(사용자 확인)
- 중단/재시작: 번역 100개 진행 후 앱 종료 → 재실행 → 이어서 진행

## 14) 마일스톤(권장 진행 순서)

### M1: “파일 열기/표시/저장” MVP
- WPF 스캐폴딩(MVVM)
- SQLite 프로젝트/스키마
- XML Import/Export(번역 없이 round-trip)
- DataGrid 가상화 + 검색/필터 + 인라인 편집

### M2: Gemini 번역 엔진 연결
- 키/모델 설정 + 연결 테스트
- 배치 번역 + 실시간 UI 업데이트
- 기본 재시도/백오프 + 로그

### M3: 품질/안정성 강화
- 플레이스홀더 마스킹/검증
- 용어집 강제 적용(항상)
- TranslationMemory 캐시
- Export 안정화(백업/원자적 저장)

### M4: 배포/운영 편의
- MSIX 패키징
- CSV 용어집 import/export
- 오류 리포트/진단 UI

## 15) 결정해야 할 남은 사항(체크리스트)
- Export 시 `Partial` 속성 처리:
  - 원본 유지(기본) vs 번역 완료 항목은 Partial 제거(=translated로 import) 옵션 제공 여부
- 기본 동시성/배치 파라미터(비용/속도/안정성 균형)
- 용어집 MatchMode 기본값(영문: WordBoundary, CJK: Substring)
