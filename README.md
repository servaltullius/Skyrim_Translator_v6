# Tullius Translator

Skyrim SE/AE 플러그인과 Bethesda/xTranslator XML을 한국어로 번역하고 검수하는 Windows 앱입니다. .NET 10 WPF와 SQLite를 사용하며 Gemini API를 지원합니다.

최신 릴리스는 **1.11**입니다. [GitHub 릴리스](https://github.com/servaltullius/Skyrim_Translator_v6/releases/tag/1.11)에서 받을 수 있습니다. 프로젝트 루트의 `TulliusTranslator.exe`는 그 다음 미리보기 1.12-preview6로, 게임 한글화 TM의 문장 안에만 나오는 이름(Gray Quarter → 잿빛 지구, Lake Honrich → 혼리크 호수 등)도 고정합니다. 1.11은 [밤샘 개선](docs/analysis/2026-10-05-overnight.md)과 실제 모드 검수(엘든림, MEI)에서 나온 문제를 고친 버전입니다. 게임 한글화 TM의 이름을 문장 속에서 더 넓게 지키고(이름·성 단독, 소리로 옮긴 한 단어 이름, 권수가 붙은 책 제목, 소문자로 쓴 이름), 짧은 용어집 용어가 긴 공식 이름을 깨뜨리지 않으며, "< 동료로 영입한다 >" 같은 꺾쇠 선택지를 번역합니다. 품질 검사에는 "같은 이름 다른 번역"이 생겼고, 고칠 것 없는 "TM 대신 번역" 안내는 나오지 않습니다. 1.10은 [전체 점검](docs/analysis/2026-10-04-full-audit.md)에서 찾은 결함 63건을 모두 고친 버전입니다. 문장 첫머리 "Fine.", "Yes, Master."가 "하급.", "달인님"이 되던 문제, "NPC는"을 "NPC은"으로 바꾸던 후처리, 저장 버튼을 누르지 않은 번역문 수정이 사라지던 문제, 잘못된 API 키·요청 제한 오판, 용어집 TSV 다시 가져오기 열 밀림, CP949 한글 ESP를 깨진 글자로 읽던 문제, 안전 필터 차단 표시, LotD 쪽 나눔 보호, 품질 검사 중 창 멈춤 등입니다. API 키는 요청 헤더로 보냅니다. 도움말의 조작 키(`[Sprint]` 등)를 영어로 지키고, 공식 TM에 없는 아이템 이름의 재질명(에보니, 드래곤 비늘 등)을 공식 표기로 고정합니다([조작 키와 재질명](docs/analysis/2026-10-04-control-keys-materials.md)). 모드 폴더를 옮긴 뒤 플러그인을 열어도 기존 프로젝트(검수한 번역·용어집·문맥)를 이어서 열고, 여러 ESP로 나뉜 모드의 새 플러그인 프로젝트는 같은 모드의 다른 프로젝트 용어집을 가져와 파일마다 정형 문구가 다르게 번역되지 않게 합니다. 1.9는 대사 모드 검수에서 찾은 문제를 번역기에서 고친 버전입니다. 문장 속 바닐라 이름(인물·지명·주문 등)을 공식 한국어판 표기로 고정하고, 공식 표기가 없는 모드 고유 이름은 번역 중에 기억해 같은 표기로 쓰며, 대사의 말투를 화자별로 맞춥니다. 품질 검사는 이름 표기 불일치(인사말 속 이름 포함)와 용어집 대신 쓴 음역어(흡혈귀 대신 뱀파이어)를 찾습니다. ESP·ESM·ESL·XML 파일은 창에 끌어다 놓아도 열리고, windows-1252로 저장된 영문 ESP는 원문 인코딩이 UTF-8로 되어 있어도 자동으로 다시 읽습니다([대사 모드 검수](docs/analysis/2026-10-03-dialogue-mod-review.md), [1.8과 비교한 평가](docs/analysis/2026-10-04-release-1.9-eval.md)). 1.8은 같은 플러그인의 이전 번역판(예: 이전 버전 한글판)을 참고해 기존 이름·용어를 따르게 하는 기능을 더했습니다([이전 번역 참고](docs/analysis/2026-10-02-previous-translation-reference.md)). 창 제목에서 버전을 확인할 수 있습니다. .NET 런타임을 포함한 Windows x64 단일 파일입니다. 1.6 이후 Skyrim SE/AE 플러그인 직접 번역([안내](docs/direct-plugin-support.md)), 번역 후처리 결함 수정([품질 평가 v2](docs/analysis/2026-09-30-quality-eval-v2.md)), 한국어 화면([2026-10-01 전반 개선](docs/analysis/2026-10-01-upgrade.md)), 품질 검사 오탐 수정([2026-10-02](docs/analysis/2026-10-02-lqa-false-positives.md)), 완료된 행 다시 번역이 추가됐습니다. 이전 루트 빌드는 1.10-preview6이 `artifacts/release-1.10-20261004/deployment/`, 1.9가 `artifacts/keys-materials-1.10-preview1-20261004/deployment/`, 1.9-preview14가 `artifacts/release-1.9-20261004/deployment/`, 1.8이 `artifacts/context-1.9-preview1-20261002/deployment/`에 있고, 그 사이 미리보기 빌드는 각 작업 폴더의 `deployment/`에 있습니다. [EDID·용어 기억 개선과 실제 표본 비교](docs/analysis/2026-09-30-edid-session-memory.md), [앞선 프롬프트 개선](docs/analysis/2026-09-30-prompt-improvement.md)에 변경·비용·미해결 범위를 기록했습니다.

현재 빌드는 앞서 적용한 번역 파이프라인·기본값 개선을 포함합니다. 그 변경 내용은 [파이프라인 개선 기록](docs/analysis/2026-09-29-pipeline-defaults-implementation.md)에 보존합니다.

**루트 EXE는 Skyrim SE/AE ESP·ESM·ESL 직접 입력과 기존 xTranslator XML(`SSTXMLRessources`) 입력을 지원합니다.** 번역한 플러그인은 새 폴더에 저장합니다. 이전 `esp-preview`는 PERK 기술 변수 오분류로 사용 중지했습니다. 설정·출력 사용법·검증 범위는 [플러그인 직접 번역 안내](docs/direct-plugin-support.md)와 [엘든림 검증 기록](docs/analysis/2026-09-30-eldenrim-validation.md)을 참고하세요. Fallout/Starfield는 기존 XML 경로를 사용합니다.

## 사용 흐름

아래는 XML 경로입니다. ESP 후보에서는 `ESP 열기 → 번역·검수 → ESP 저장`로 진행합니다.

1. xTranslator에서 원본 플러그인의 문자열을 XML로 내보냅니다.
2. 앱의 **게임 시리즈**(TES / Fallout / Starfield)를 선택하고 `XML 열기`로 엽니다.
3. API 키와 모델을 선택합니다. 기본 모델은 `gemini-3.8-flash`입니다. `새로고침`은 계정에서 사용 가능한 모델 목록을 갱신합니다.
4. 용어집과 프로젝트 문맥을 확인한 뒤 `번역 시작`으로 번역합니다. 번역 결과는 프로젝트 DB에 바로 저장되므로, 같은 파일을 다시 열면 이전 번역이 그대로 나옵니다. `번역 시작`은 대기·오류 행만 처리하고 완료·수동 편집 결과는 보존합니다. 완료된 행을 다시 번역하려면 문자열 탭에서 `선택 행 다시 번역`이나 `보이는 행 모두 다시 번역`으로 대기 상태로 되돌린 뒤 `번역 시작`을 누릅니다.
5. `품질 검사`와 `모델 비교` 탭에서 검토하고 필요한 행을 수정합니다. 모델 비교와 비용 추정의 API 호출도 선택한 계정의 요금·할당량을 따릅니다.
6. `XML 내보내기`로 결과를 저장하고 xTranslator에 가져와 확인합니다. 기존 출력 파일이 있으면 `.bak` 백업을 남깁니다.

앱의 API 호출이 항상 무료인 것은 아닙니다. 무료 웹 비교의 실행 조건·실제 출력·한계는 [LOTD 모델 비교](benchmarks/translation/lotd-v1/README.md)에 별도로 기록합니다.

현재 정확한 stable 모델 ID에 적용되는 추론 설정은 `gemini-3.8-flash=low`, `gemini-3.1-flash-lite=minimal`입니다. Compare와 비용 샘플도 같은 공통 정책을 사용합니다. 웹 비교와 추론 수준을 맞춘 것이며, 앱의 마스킹·문맥·후처리까지 포함한 품질 우위를 실측한 것은 아닙니다.

비용 추정에서 샘플을 실행하면 추론 포함 API 사용량으로 출력 토큰을 추정합니다. 샘플 사용량이 없으면 **추론 미포함 휴리스틱**으로 표시합니다. 캐시 사용 비용은 요청별 읽기 비용과 2시간 저장을 가정하며, 재시도·TM 적중·후보 재평가 등을 완전히 모사하지 않으므로 최종 비용은 실제 호출 누계로 확인하세요.

일반 `generateContent` API를 사용하며 Batch API·Flex 선택 모드는 제공하지 않습니다. 화면의 BatchSize는 여러 문자열을 한 일반 요청에 묶는 크기입니다. 프롬프트 캐시는 실제 생성 요청이 생길 때 만들고, TM만 재사용하면 생성하지 않습니다. 캐시가 지원되지 않으면 일반 요청으로 진행합니다.

후보 빌드는 번역 옵션을 저장하며, 원본 행별 추가 생성 호출은 기본 8회로 제한합니다. 전체 생성 호출 상한은 0이면 자동이며 양수이면 작업 전체의 고정 상한입니다. 정상 결과를 먼저 저장하고 누락·실패 행만 다시 요청합니다. 이 제한은 금액 상한이 아닙니다. 출력 예산 자동 조절과 책 원문 문맥은 기본 꺼짐 실험 옵션입니다.

`책 제목 모델 분리`는 BOOK:FULL, `책 본문 모델 분리`는 BOOK:DESC에 적용됩니다. 선택한 종류는 오른쪽의 공통 책 모델을 사용하고, 선택하지 않은 종류는 기본 모델을 사용합니다. 예전 책 모델 설정은 제목에만 유지되며 본문 선택은 기본적으로 꺼져 있습니다. 기본 모델과 책 모델이 같으면 별도 실행으로 나누지 않습니다.

API 로그에는 호출 목적, 종료 사유, 추론·캐시 토큰을 표시합니다. `Out+Think`는 추론을 포함하므로 `Think`를 다시 더하지 마세요. 잘리거나 거부된 생성 응답도 서버가 사용량을 제공하면 비용 누계에 포함합니다. 사용량 없는 호출은 미확인으로 남으며 이 누계는 서비스 청구 확정액이 아닙니다. 캐시 보관료 등은 별도로 고려해야 합니다.

## 번역과 데이터 보호

- 태그, 변수, printf 형식, 줄바꿈, 페이지 구분자를 보호하고 최종 결과에서도 검사합니다. TM에서 재사용한 문장에도 같은 검사를 적용합니다.
- 반복 용어를 임의로 줄이지 않으며 긴 텍스트 분할 경계의 공백을 보존합니다.
- 문맥과 레코드가 다른 행은 단순히 원문이 같다는 이유로 같은 API 결과를 공유하지 않습니다.
- XML 가져오기는 전체를 하나의 트랜잭션으로 처리합니다. 읽기 실패나 취소가 발생하면 이전 문자열과 프로젝트 정보를 유지합니다.
- 같은 XML을 다시 열 때 식별 정보가 일치하는 기존 번역을 복원합니다. 수동 편집은 의도적으로 비운 번역까지 보존합니다.
- XML 프로젝트는 Addon 이름으로 구분하므로 같은 Addon의 다른 XML(부분 내보내기 등)도 같은 프로젝트로 열립니다. 그 파일에 없는 행의 번역은 지우지 않고 보관했다가, 그 행이 든 XML을 다시 열면 복원합니다.
- 전체 용어집·시리즈 TM DB를 열 수 없으면(다른 프로그램이 잠금, 파일 손상 등) 앱 로그에 원인을 남기고, 번역을 시작할 때 다시 열어 본 뒤 그래도 안 되면 그 데이터 없이 번역할지 묻습니다.
- 파일 전환·작업 종료에서 진행 중 요청의 취소와 정리를 기다립니다.

형식 검증은 의미 정확성이나 게임 내 표시를 보증하지 않습니다. 고유명사·문맥·말투·퀘스트 조건은 사람이 확인해야 합니다.

## 용어집과 번역 메모리(TM)

프로젝트 용어집은 현재 애드온에 속합니다. `전체 용어집`과 `시리즈 TM`은 **선택한 게임 시리즈 안에서 공유**한다는 뜻이며 TES·Fallout·Starfield 데이터를 섞지 않습니다. TM은 원문 중심 조회이므로 모호한 짧은 단어는 문맥을 함께 검토하세요.

세션 용어 메모리는 이름·제목에서 학습한 번역을 실행 중 참고 힌트로 사용합니다. 새 항목은 프로젝트 용어집의 `Auto(Session)`에 **비활성 검수 후보**로 저장됩니다. 검토 후 직접 활성화하세요. 대사는 용어 학습에서 제외하며, 기존 수동 용어와 이미 저장된 용어의 설정은 바꾸지 않습니다.

### 내장 TM의 실제 범위

Fallout TM is currently scoped to Fallout 4 family data.
Bundled Fallout TM is auto-seeded on first Fallout project load.
Bundled Skyrim/TES TM is auto-seeded on first Elder Scrolls project load.
Bundled Starfield TM is auto-seeded on first Starfield project load.
직접 만든 TSV는 고급 설정의 `시리즈 TM 가져오기`로 계속 가져올 수 있습니다.

1.6 릴리스부터 TES와 Starfield 번역 TM 시드를 내장합니다. 프로젝트를 처음 열 때 각 프랜차이즈의 `tm-import` 폴더에 복사되고, 기존 자동 가져오기로 프랜차이즈 TM에 들어갑니다. 파일 크기와 SHA-256이 내장본과 다르면 다시 복사합니다.

| 프랜차이즈 | 내장 시드 | 규모 |
|---|---|---|
| TES | `bundled-skyrim-tes-franchise-tm.tsv` | 약 24,000쌍 |
| Starfield | `bundled-starfield-franchise-tm.tsv` | 약 153,000쌍 |
| Fallout | `bundled-fallout4-franchise-tm.tsv` | `Pip-Boy`, `Vault-Tec`, `Commonwealth`의 **영문 유지 3쌍** |

Fallout 시드는 완성된 한글 번역 데이터가 아닙니다. TM은 원문 중심 조회이므로, 내장 TM도 짧은 문장은 문맥을 함께 검토하세요. 추가 번역 자산은 해당 프랜차이즈로 가져오세요.

- 일반 TSV: 첫 줄 `Source<TAB>Target`, 이후 원문과 번역문 두 열을 지원합니다.
- 앱의 새 내보내기 형식: 헤더에 `XTranslatorAi-JSON-v1`이 있으며 각 열을 JSON 문자열로 기록합니다. 앱으로 다시 가져올 때 탭·개행·따옴표·역슬래시를 복원합니다. 일반 2열 TSV만 받는 외부 도구와는 형식이 다릅니다.
- 용어집·TM 가져오기 파일은 UTF-8(BOM 유무 무관)·UTF-16·CP949(한국어 Windows 엑셀에서 저장한 TSV)로 읽습니다. 어느 쪽으로도 읽을 수 없거나 이미 깨진 글자(�)가 든 파일은 가져오지 않고 이유를 표시합니다.
- [Starfield 로컬 자산에서 TM 만들기](docs/starfield-franchise-tm.md)

## 저장 위치와 이전

- 프로젝트 DB: `%LOCALAPPDATA%\XTranslatorAi\Projects\{elder-scrolls,fallout,starfield}`
- 플러그인을 다른 폴더로 옮긴 뒤 열면, 같은 파일(SHA-256 일치)이거나 원래 위치에 파일이 없을 때 기존 프로젝트를 새 경로로 복사해 이어서 엽니다([안내](docs/direct-plugin-support.md#프로젝트-위치)).
- XML을 다시 열면 지금 고른 게임 시리즈와 상관없이 세 폴더에서 그 XML의 프로젝트를 찾아 열고, 게임 시리즈도 그 프로젝트에 저장된 것으로 바꿉니다. 프로젝트가 없을 때만 고른 게임 시리즈로 새로 만듭니다.
- 프랜차이즈 공용 데이터: `%LOCALAPPDATA%\XTranslatorAi\Global` 아래 프랜차이즈별 경로. TES는 호환성을 위해 기존 `global-glossary.sqlite` 위치를 유지합니다.
- 이전 프로젝트 DB는 프랜차이즈·언어·애드온이 일치할 때 새 위치로 복사합니다. 레거시 원본은 남깁니다. 프랜차이즈 정보가 없으면 입력 XML 경로까지 일치해야 이전합니다.
- 이전 후 되돌리려면 앱을 종료하고 보존된 기존 DB와 실행 파일을 사용하세요. 새 DB에서 편집한 내용은 XML/TM으로 먼저 내보내세요.

## 개발과 검증

Windows, .NET 10 SDK가 필요합니다. `global.json`은 안정판 .NET 10 SDK를 선택합니다. 아래 스크립트는 일반 SDK 또는 이 PC의 사용자 전용 SDK를 찾아 실행합니다.

```powershell
./scripts/build.ps1 -Task Test
./scripts/build.ps1 -Task Publish
```

XML 왕복 검증 도구는 [Validate CLI](tools/XTranslatorAi.Validate/README.md)에 설명되어 있습니다. 앱과 테스트의 주 구현은 `src/XTranslatorAi.App`, `src/XTranslatorAi.Core`, `tests/XTranslatorAi.Tests`입니다.

테스트는 Release 빌드 후 `dotnet test --no-build`를 실행합니다. Publish 출력은 `artifacts/current`이며, 실행 중인 루트 EXE를 자동 교체하지 않습니다. 릴리스 워크플로는 지정 태그를 checkout하고 해당 커밋인지 확인한 뒤 .NET 10 Windows 단일 실행 파일과 SHA-256을 만듭니다. 로컬 빌드와 공개 릴리스는 별도 작업입니다.

## 문서 구분

- [파이프라인·설정 개선과 후보 빌드 검증](docs/analysis/2026-09-29-pipeline-defaults-implementation.md)
- [일반 API 최적화 구현과 검증 결과](docs/analysis/2026-09-29-standard-api-implementation.md)
- [현재 유지보수 계획과 검증 상태](docs/plans/2026-09-29-maintenance.md)
- [이전 개선 작업](docs/plans/2026-09-28-translator-improvement.md)
- [현재 코드 검토 기준](docs/review-checklist.md)
- [문서 안내](docs/README.md)
- [이전 설계 기록](docs/plans/README.md): 과거 계획은 현재 구현이나 지원 모델을 뜻하지 않습니다.
- [이전 CLI·설계·VibeKit 자료](docs/archive/2026-09-29/README.md)는 이력 보관용이며 현재 실행 절차가 아닙니다.
