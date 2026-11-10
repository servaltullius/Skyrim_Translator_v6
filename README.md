# Tullius Translator

Skyrim SE/AE 플러그인(ESP·ESM·ESL)과 Bethesda/xTranslator XML(`SSTXMLRessources`)을 한국어로 번역하고 검수하는 Windows 앱입니다. .NET 10 WPF와 SQLite로 만들었습니다.

번역하는 방법은 두 가지입니다.

- **번역기 창:** Gemini API 키로 번역기가 직접 번역합니다.
- **에이전트 모드:** API 키 없이, 쓰고 있는 구독 앱(Claude·ChatGPT Codex·안티그라비티)이 번역기를 도구로 써서 번역합니다.

스카이림 모드라면 어느 쪽이든 바닐라 문장과 이름·용어를 스카이림 한국어 패치(유저 번역)의 글과 표기로 맞추고, 번역한 뒤 품질 검사로 확인할 수 있습니다.

## 받기

최신 릴리스는 **1.16.60**입니다. [GitHub 릴리스](https://github.com/servaltullius/Skyrim_Translator_v6/releases/tag/1.16.60)에서 받으세요. 판마다 바뀐 것은 [릴리스 기록](docs/release-history.md)에 있습니다.

- **처음이라면** `TulliusTranslator-<버전>.zip` 하나를 받아 아무 폴더에 풉니다. 번역기 EXE와 에이전트 모드 키트(`agent-kit` 폴더)가 함께 들어 있어, 풀면 키트가 EXE 바로 옆에 놓입니다.
- **이미 쓰고 있다면** `TulliusTranslator.exe`만 바꾸면 됩니다. 키트가 바뀐 판은 릴리스 글에 적으니, 그때는 zip을 덮어 풉니다(`agent-kit`의 `work`·`output`에 있는 번역은 그대로 남습니다).
- `TulliusTranslator-agent-kit.zip`(키트만)도 당분간 함께 올립니다.
- 스카이림 한국어 패치는 따로 냅니다([아래](#스카이림-한국어-패치)).

## 에이전트 모드

1. `agent-kit` 폴더나 번역기 폴더(zip을 푼 폴더)를 구독 앱에서 작업 폴더로 엽니다(Claude 앱은 Code 탭, Codex는 작업 폴더, 안티그라비티는 워크스페이스). 어느 쪽을 열어도 같은 안내·도구·프로젝트를 씁니다.
2. "G:\모드\○○\○○.esp 번역해 줘"처럼 말합니다.
3. 앱이 번역기 도구(tullius)로 요청을 받아 번역하고, 품질 검사 뒤 `agent-kit/output`에 번역한 플러그인을 저장합니다.

이미 한국어판이 있는 모드를 검수하거나, 줄이 많은 모드를 하위 에이전트 여럿으로 나눠 번역할 수도 있습니다. 자세한 사용법은 [agent-kit/사용법.txt](agent-kit/사용법.txt)에 있습니다. 번역기 도구는 AI를 부르지 않고, 번역은 구독 앱이 합니다.

## 번역기 창 사용 흐름

플러그인은 `ESP 열기 → 번역·검수 → ESP 저장`으로 진행하고, 번역한 플러그인은 새 폴더에 저장합니다. 설정·출력·검증 범위는 [플러그인 직접 번역 안내](docs/direct-plugin-support.md)와 [엘든림 검증 기록](docs/analysis/2026-09-30-eldenrim-validation.md)을 보세요. Fallout·Starfield는 XML 경로를 씁니다.

XML 경로는 다음과 같습니다.

1. xTranslator에서 원본 플러그인의 문자열을 XML로 내보냅니다.
2. 앱의 **게임 시리즈**(TES / Fallout / Starfield)를 선택하고 `XML 열기`로 엽니다.
3. API 키와 모델을 선택합니다. 기본 모델은 `gemini-3.8-flash`입니다. `새로고침`은 계정에서 사용 가능한 모델 목록을 갱신합니다.
4. 용어집과 프로젝트 문맥을 확인한 뒤 `번역 시작`으로 번역합니다. 번역 결과는 프로젝트 DB에 바로 저장되므로, 같은 파일을 다시 열면 이전 번역이 그대로 나옵니다. `번역 시작`은 대기·오류 행만 처리하고 완료·수동 편집 결과는 보존합니다. 완료된 행을 다시 번역하려면 문자열 탭에서 `선택 행 다시 번역`이나 `보이는 행 모두 다시 번역`으로 대기 상태로 되돌린 뒤 `번역 시작`을 누릅니다.
5. `품질 검사`와 `모델 비교` 탭에서 검토하고 필요한 행을 수정합니다. 모델 비교와 비용 추정의 API 호출도 선택한 계정의 요금·할당량을 따릅니다.
6. `XML 내보내기`로 결과를 저장하고 xTranslator에 가져와 확인합니다. 기존 출력 파일이 있으면 `.bak` 백업을 남깁니다.

### API 호출과 비용

앱의 API 호출이 항상 무료인 것은 아닙니다. 무료 웹 비교의 실행 조건·실제 출력·한계는 [LOTD 모델 비교](benchmarks/translation/lotd-v1/README.md)에 따로 적었습니다.

- `gemini-3.8-flash`와 `gemini-3.1-flash-lite`의 추론 설정은 각각 low·minimal입니다(다른 모델은 모델마다 다릅니다). 모델 비교와 비용 샘플도 같은 설정을 씁니다. 웹 비교와 추론 수준을 맞춘 것이며, 앱의 마스킹·문맥·후처리까지 포함한 품질 우위를 실측한 것은 아닙니다.
- 비용 추정에서 샘플을 실행하면 추론을 포함한 API 사용량으로 출력 토큰을 추정합니다. 샘플 사용량이 없으면 **추론 미포함 휴리스틱**으로 표시합니다. 캐시 비용은 요청별 읽기 비용과 2시간 저장을 가정하며, 재시도·TM 적중·후보 재평가 등을 완전히 모사하지 않으므로 최종 비용은 실제 호출 누계로 확인하세요.
- 일반 `generateContent` API를 쓰며 Batch API·Flex 선택 모드는 없습니다. 화면의 BatchSize는 여러 문자열을 한 요청에 묶는 크기입니다. 프롬프트 캐시는 실제 생성 요청이 생길 때 만들고, TM만 재사용하면 만들지 않습니다. 캐시가 지원되지 않으면 일반 요청으로 진행합니다.
- 번역 옵션은 저장되며, 원본 행별 추가 생성 호출은 기본 8회로 제한합니다. 전체 생성 호출 상한은 0이면 자동이고 양수이면 작업 전체의 고정 상한입니다. 정상 결과를 먼저 저장하고 누락·실패 행만 다시 요청합니다. 이 제한은 금액 상한이 아닙니다. 출력 예산 자동 조절과 책 원문 문맥은 기본으로 꺼져 있는 실험 옵션입니다.
- `책 제목 모델 분리`는 BOOK:FULL, `책 본문 모델 분리`는 BOOK:DESC에 적용됩니다. 선택한 종류는 오른쪽의 공통 책 모델을 쓰고, 선택하지 않은 종류는 기본 모델을 씁니다. 예전 책 모델 설정은 제목에만 남고 본문 선택은 기본으로 꺼져 있습니다. 기본 모델과 책 모델이 같으면 따로 실행하지 않습니다.
- API 로그에는 호출 목적, 종료 사유, 추론·캐시 토큰을 표시합니다. `Out+Think`는 추론을 포함하므로 `Think`를 다시 더하지 마세요. 잘리거나 거부된 생성 응답도 서버가 사용량을 주면 비용 누계에 넣습니다. 사용량 없는 호출은 미확인으로 남고, 이 누계는 서비스 청구 확정액이 아닙니다. 캐시 보관료 등은 따로 생각해야 합니다.

번역 파이프라인과 기본값을 정한 과정은 [파이프라인 개선 기록](docs/analysis/2026-09-29-pipeline-defaults-implementation.md)에 있습니다.

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

- Fallout TM은 지금 Fallout 4 계열 데이터만 다룹니다.
- 내장 Fallout TM은 Fallout 프로젝트를 처음 열 때 자동으로 넣습니다.
- 내장 Skyrim/TES TM은 Elder Scrolls 프로젝트를 처음 열 때 자동으로 넣습니다.
- 내장 Starfield TM은 Starfield 프로젝트를 처음 열 때 자동으로 넣습니다.
- 직접 만든 TSV는 고급 설정의 `시리즈 TM 가져오기`로 계속 가져올 수 있습니다.

1.6 릴리스부터 TES와 Starfield 번역 TM 시드를 내장합니다. 프로젝트를 처음 열 때 각 프랜차이즈의 `tm-import` 폴더에 복사되고, 기존 자동 가져오기로 프랜차이즈 TM에 들어갑니다. 파일 크기와 SHA-256이 내장본과 다르면 다시 복사합니다.

| 프랜차이즈 | 내장 시드 | 규모 |
|---|---|---|
| TES | `bundled-skyrim-tes-franchise-tm.tsv` | 약 24,000쌍 |
| Starfield | `bundled-starfield-franchise-tm.tsv` | 약 153,000쌍 |
| Fallout | `bundled-fallout4-franchise-tm.tsv` | `Pip-Boy`, `Vault-Tec`, `Commonwealth`의 **영문 유지 3쌍** |

TES 시드는 스카이림 한국어 패치(유저 번역)의 글로 만들었고, 패치를 고치면 다음 판에서 함께 바뀝니다(지금 v27). 이미 가져온 사용자는 바뀐 행만 받으므로, 이번 판에서 바뀌지 않은 행은 시리즈 TM에서 직접 고친 대로 남습니다. Fallout 시드는 완성된 한글 번역 데이터가 아닙니다. 추가 번역 자산은 해당 프랜차이즈로 가져오세요.

- 일반 TSV: 첫 줄 `Source<TAB>Target`, 이후 원문과 번역문 두 열을 지원합니다.
- 앱의 새 내보내기 형식: 헤더에 `XTranslatorAi-JSON-v1`이 있으며 각 열을 JSON 문자열로 기록합니다. 앱으로 다시 가져올 때 탭·개행·따옴표·역슬래시를 복원합니다. 일반 2열 TSV만 받는 외부 도구와는 형식이 다릅니다.
- 용어집·TM 가져오기 파일은 UTF-8(BOM 유무 무관)·UTF-16·CP949(한국어 Windows 엑셀에서 저장한 TSV)로 읽습니다. 어느 쪽으로도 읽을 수 없거나 이미 깨진 글자(�)가 든 파일은 가져오지 않고 이유를 표시합니다.
- [Starfield 로컬 자산에서 TM 만들기](docs/starfield-franchise-tm.md)

## 스카이림 한국어 패치

저장소의 `Skyrim_Latest_Kor`는 엘더스크롤 시리즈 갤러리 한글패치 25.3.14를 바탕으로, 이 번역기로 검수해 고친 스카이림 AE(SE) 한국어 패치(26.10.16 베타)입니다. 릴리스 zip에는 본편·DLC·크리에이션 클럽과 비공식 패치(USSEP·USCCCP) 한국어판이 들어 있습니다(USSEP 플러그인은 저장소에 올리지 않습니다).

- 베타는 GitHub 릴리스의 `kopatch-` 태그로 냅니다. 최신은 [베타 28](https://github.com/servaltullius/Skyrim_Translator_v6/releases/tag/kopatch-26.10.16-beta28)입니다.
- 번역기의 게임 번역 사전(바닐라 문장을 바로 채움)과 내장 TES TM은 이 패치의 글을 따릅니다.
- 검수와 고침 기록은 [베타 기록](docs/analysis/2026-10-16-kopatch-beta.md)과 그 안의 링크에 있습니다.

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

테스트는 Release 빌드 후 `dotnet test --no-build`를 실행합니다. Publish 출력은 `artifacts/current`이며, 실행 중인 루트 EXE를 자동 교체하지 않습니다. 릴리스 워크플로는 지정 태그를 checkout하고 해당 커밋인지 확인한 뒤 .NET 10 Windows 단일 실행 파일과 SHA-256을 만듭니다. 로컬 빌드와 공개 릴리스는 별도 작업입니다. 개발 PC에서는 프로젝트 루트의 `TulliusTranslator.exe`를 최신 릴리스로 바꿔 두고, 바로 앞 판은 `artifacts/root-deploy-20261019/backup-<버전>/`에 둡니다(둘 다 저장소에는 올리지 않습니다).

## 라이선스

이 저장소의 코드와 문서(아래 데이터는 빼고)는 [GPL-3.0-or-later](LICENSE)로 배포합니다(Copyright © 2026 servaltullius). GNU 일반 공중 사용 허가서 3판, 또는 받는 사람이 고르는 그 뒤의 어느 판이 정한 조건에 따라 쓰고 고치고 다시 배포할 수 있습니다.

- EXE에 함께 들어가는 .NET 런타임·WPF와 라이브러리(CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, SQLitePCLRaw, SharpZipLib, K4os LZ4·xxHash, System.IO.Pipelines)는 각자의 라이선스(MIT, SQLitePCLRaw는 Apache-2.0)를 따릅니다. 라이선스 전문과 고지는 [Licenses/](Licenses/README.md)에 있고, 릴리스 zip에도 같이 들어갑니다.
- 다음 데이터에는 이 라이선스(GPL)를 적용하지 않고 코드와 따로 다룹니다. 게임 원문과 공개된 한국어 번역을 바탕으로 만든 자료이며, 바탕이 된 작업의 배포 조건을 함께 따릅니다.
  - `Skyrim_Latest_Kor`(스카이림 한국어 패치): 엘더스크롤 시리즈 갤러리 한글패치 25.3.14를 고친 판입니다. 원본 게시글의 "수정배포자유"에 따라 배포합니다. 그 안의 글꼴 파일(`Interface/fonts_en.swf`)과 비공식 패치(USSEP·USCCCP) 한국어판은 각 원권리자의 것입니다.
  - 내장 스카이림 TM과 게임 번역 사전(`src/XTranslatorAi.App/Assets/TmSeeds`, `src/XTranslatorAi.App/Assets/GameStrings`): 게임의 영어 원문과 위 한국어 패치의 글을 짝지어 만들었고, 한국어 패치를 고칠 때 함께 고칩니다.
  - 내장 Starfield TM(`starfield-franchise-tm.tsv`): 게임의 영어 원문과 스타필드 한국어 패치의 글을 짝지어 만들었습니다.
  - `fo4_strings_원본/`(폴아웃 4 게임 문자열), `FO4_Strings 230706/`(폴아웃 4 한국어 패치 문자열), `LegacyoftheDragonborn_english_korean.xml`과 `benchmarks/translation/lotd-v1/`(LOTD 모드의 영어 원문과 한국어 번역, 그 모델 비교 결과): 시험과 비교에 쓴 자료입니다.
  - 게임의 원문과 이름에 대한 권리는 Bethesda Softworks에 있습니다.

## 문서 구분

- [릴리스 기록](docs/release-history.md)
- [스카이림 한국어 패치 베타 기록](docs/analysis/2026-10-16-kopatch-beta.md)
- [파이프라인·설정 개선과 검증](docs/analysis/2026-09-29-pipeline-defaults-implementation.md)
- [일반 API 최적화 구현과 검증 결과](docs/analysis/2026-09-29-standard-api-implementation.md)
- [현재 유지보수 계획과 검증 상태](docs/plans/2026-09-29-maintenance.md)
- [이전 개선 작업](docs/plans/2026-09-28-translator-improvement.md)
- [현재 코드 검토 기준](docs/review-checklist.md)
- [문서 안내](docs/README.md)
- [이전 설계 기록](docs/plans/README.md): 과거 계획은 현재 구현이나 지원 모델을 뜻하지 않습니다.
- [이전 CLI·설계·VibeKit 자료](docs/archive/2026-09-29/README.md)는 이력 보관용이며 현재 실행 절차가 아닙니다.
