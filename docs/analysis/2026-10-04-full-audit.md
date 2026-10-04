# 번역기 전체 점검 (1.10-preview1)

작성일: 2026-10-04
대상: 525ffdb(1.10-preview1). 코드 약 4만 줄을 7개 영역으로 나눠 에이전트 7개가 읽기 전용으로 점검했다. 한국어 후처리 영역은 빌드된 DLL에 입력을 넣어 결과를 확인했다. 아래에서 "재확인"은 Claude가 코드나 실제 데이터로 다시 확인한 항목이다.

보고는 65건이고, 두 영역에서 겹친 2건을 합쳐 63건이다(1순위 15, 2순위 36, 3순위 12). 구조가 깨진 플러그인을 쓰는 결함은 없었다. ESP 읽기·쓰기, 압축 레코드, 문자열 테이블, XML 왕복은 이상이 없었다.

## 1순위: 사용자 데이터 손실이나 틀린 번역 저장

1.10-preview2에서 15건을 모두 고쳤다(아래 "1순위 처리" 참고).

| # | 영역 | 결함 | 위치 |
|---|---|---|---|
| 1 | 앱 서비스 | 용어집 TSV를 내보냈다 다시 가져오면 분류 없는 항목의 열이 한 칸씩 밀린다(원문 자리에 번역어, 번역어 자리에 "1"). 분류 있는 항목도 사용 여부·우선순위·강제 방식을 잃는다. 재확인 | `Services/GlossaryFileService.cs:34` |
| 2 | 화면 | 번역문 편집은 "번역문 저장"을 눌러야만 DB에 들어간다. 다른 행을 누르면 화면에는 바뀐 글이 보이지만 내보내기·다시 열기 때 사라진다. 번역 중에도 편집할 수 있고, 끝난 행이 덮어쓴다. 재확인 | `Views/StringsTabView.xaml:188`, `ViewModels/MainViewModel.Project.cs:180` |
| 3 | 화면 | 게임 시리즈를 바꾸면 전체 용어집·시리즈 TM 목록은 이전 게임 것인데 저장·삭제는 새 게임 DB로 간다(번역 중에는 목록을 다시 읽지 않음). 같은 Id의 다른 항목이 지워지거나 덮인다. 재확인 | `ViewModels/MainViewModel.Core.cs:275-295` |
| 4 | 번역 흐름 | Gemini는 잘못되거나 만료된 키에 HTTP 400(API_KEY_INVALID)을 준다. 401/403만 키 오류로 봐서 멈추지도, 다음 키로 넘어가지도 않고 남은 행이 모두 오류가 된다. 재확인 | `Translation/TranslationService.ErrorHandling.cs:82-147` |
| 5 | 번역 흐름·Gemini | 요청 제한을 오류 문장 속 "429", "rate"+"limit" 글자로 판단한다("GenerateContent"에 rate가 들어 있음). 행 번호 4291의 검증 오류가 요청 제한으로 처리돼 실행 전체가 멈추고 E202로 표시된다. 재확인 | `ErrorHandling.cs:51-80`, `Diagnostics/UserFacingErrorClassifier.cs:94` |
| 6 | 토큰·용어 | 기본 용어집의 한 단어 용어가 문장 첫머리나 호칭에서도 강제된다: "Fine. I'll do it." → "하급.", "Yes, Master." → "네, 달인.", "Reach the summit" → 리치. 재확인(사용자 전체 용어집 복사본으로 Master 확인. 사용자 PC에는 Fine 항목이 없지만 새로 설치하면 있음) | `Text/GlossaryApplier.cs:162-231` |
| 7 | 한국어 후처리 | 영어 뒤 조사를 마지막 글자로 고른다: "NPC는" → "NPC은", "HP가" → "HP이", "DLC를" → "DLC을". 프롬프트 규칙과 반대이고, TM 적용·후처리 재적용 때 사람이 고친 행도 바꾼다. | `Text/KoreanFix/Internal/Steps/AttachedSeparatedParticleStep.cs`, `KoreanParticleSelector.cs` |
| 8 | 한국어 후처리 | 지시어 "이"를 앞 단어에 붙인다: "내가 이 7년 동안" → "내가가 7년 동안", "그에게 이 100골드를" → "그에게가". | `Steps/StatAndSubjectParticleStep.cs:24-58` |
| 9 | 한국어 후처리 | 불규칙 활용을 조사로 고친다: "뒤이은" → "뒤이는", "끌어모은" → "끌어모는", "관련지을" → "관련지를". 품질 검사에는 같은 예외 목록이 있는데 후처리에는 없다. | `AttachedSeparatedParticleStep.cs:131-154` |
| 10 | 토큰 | 퍼센트 정리가 보호된 변수를 깬다: "%PLAYERNAME%" → "%PLAYERNAME", "%d%%" → "%d%". 검증에 걸려 그 행은 매번 오류가 된다. | `Text/PercentSignFixer.cs` |
| 11 | 앱 서비스 | 기본 용어집 보정(PromptOnly 전환, Smithing 대장, Block 막기 등)이 실행마다 다시 돌아 사용자가 바꾼 강제 방식·번역어·분류를 되돌린다. | `Services/BuiltInGlossaryService.cs:204-229` |
| 12 | 이번 변경 | 재질 Silver(은) 토큰 앞뒤의 같은 글자를 중복으로 보고 지운다: "네 검은 [은] 검이지" → "네 검은 검이지". 재확인 | `Translation/TranslationService.Helpers.cs:270-349` |
| 13 | 이번 변경 | 품질 검사가 영어로 지킨 조작 키를 "영문 남음: Sprint"로 경고한다. | `Text/LqaScanner.cs:39-42, 267-290` |
| 14 | 1.9 변경 | windows-1252 자동 전환이 CP949 한국어 ESP도 받아들여 깨진 글자로 읽고, 내보내면 기존 한국어가 영구히 깨진다(1.9 전에는 오류). | `Plugins/PluginReader.cs:94-141` |
| 15 | 1.9 변경 | 프로젝트 DB 경로가 실제로 읽은 인코딩에 따라 정해져, 모드 업데이트로 1252 문자가 하나 생기면 새 빈 프로젝트가 열린다(이전 번역이 안 보임). | `PluginReader.cs:100-119`, `Services/ProjectPaths.cs:15-17` |

### 1순위 처리

| # | 커밋 | 고친 내용 |
|---|---|---|
| 1 | c379f4b | 칸을 나눈 뒤 칸마다 공백을 지운다. 앱 내보내기 머리글이 있으면 사용 여부·우선순위·일치·강제 방식·메모를 그대로 되살린다. |
| 2 | 124cd14 | 다른 행을 고르거나 내보내기·파일 열기·다시 번역·창 닫기 전에 고친 번역문을 자동 저장한다. 번역 중에는 편집기를 읽기 전용으로 둔다. |
| 3 | a509971 | 번역 중에는 게임 시리즈를 바꿀 수 없고, 바꾸면 전체 용어집·시리즈 TM 목록을 새 게임 것으로 다시 읽는다. |
| 4, 5 | 2ff35d6 | 요청 제한과 잘못된 키를 Gemini 응답의 상태 코드와 본문(RESOURCE_EXHAUSTED, API_KEY_INVALID)으로만 판단한다. 저장된 행 오류도 맨 "429"·" 401"과 맞추지 않는다. 다른 400은 키 오류로 표시하지 않는다. |
| 6 | 3513b9c | 일반 단어이기도 한 기본 용어(Fine, Master, Reach, Fortify 등 28개)가 문장 첫머리에서 문장부호·기능어(the, your…)·소문자 단어(형용사) 앞에 오거나 문장 끝 호칭이면 강제하지 않고 참고로만 준다. 이름·기술·능력치와 "Fine Iron Sword", "Destruction spells", "Rank: Master"는 그대로 강제한다. Skyrim.esm·LotD·세라나의 모든 해당 위치를 읽어 확인했다. |
| 7, 8, 9 | 39b84f5 | 영어 뒤에 모델이 쓴 조사는 바꾸지 않는다(약어는 글자 이름으로 읽어 L·M·N·R만 받침). "이" 붙이기는 능력치·기술 이름 뒤, 수치 앞에서만 한다. 뒤이은·끌어모은 등은 품질 검사와 같은 예외 목록을 쓴다. 품질 검사 particle_roman_mismatch도 약어만 본다. |
| 10 | 32aef33 | 원문의 %이름%·%d 같은 변수를 감싼 채 퍼센트 정리를 하고, 원문에 %%가 있으면 그대로 둔다. |
| 11 | 6a8bc07 | 기본 용어집 보정을 전체 용어집마다 한 번만(옆에 기록 파일) 하고, 프롬프트 전용 전환은 한 번도 전환되지 않은 항목에만 한다. |
| 12 | 52422ec | 한 글자 번역어는 토큰 앞뒤 중복으로 보지 않고, 빠진 토큰을 "번역어가 이미 있음"으로 인정하지도 않는다. 앞쪽 중복은 온전한 단어여야 한다. |
| 13 | 3f075d4 | 영문 남음 검사가 보호된 조작 키를 지우고 본다. |
| 14 | 2f8e1e5 | windows-1252로 다시 읽기 전에 흔한 한글 음절(KS X 1001 2,350자)이 낱말을 이루고 비ASCII 바이트의 절반을 넘으면 CP949 한글로 보고, 1.9 이전처럼 오류를 내며 ks_c_5601-1987을 고르라고 안내한다. |
| 15 | c7a511f | 실제로 읽은 인코딩, 설정 인코딩, 설정의 대체 인코딩 순으로 이미 있는 프로젝트 DB를 찾아 연다. 없을 때만 읽은 인코딩 기준으로 새로 만든다. |

검수가 끝난 번역 15,582행(세라나, 엘든림 두 프로젝트)에 바뀐 후처리를 다시 적용하자 바뀐 행은 1행("되어라" → "돼라", 의도된 문체 규칙)뿐이었다.

문장 첫머리 용어(6)는 바닐라·세라나 대사 22행("Fine.", "Master, look.", "Reach the Oculory" 등)을 1.9와 새 빌드로 번역해 비교했다(비용 $0.022). 1.9는 22행 모두에 "하급", "달인님", "리치하기"가 들어갔고("하급, 날 죽게 내버려 둬.", "오큘로리에 리치하기"), 새 빌드는 0행이었다("좋아, 그냥 죽게 내버려 둬.", "스승님, 보세요.", "오큘로리에 도달하기"). 증거: `artifacts/position-terms-eval/`.

화면(2, 3)의 동작 변화: 프로젝트가 열려 있지 않을 때 게임 시리즈를 바꾸면 그 게임의 전체 용어집 DB를 연다(없으면 만든다). 필터를 켠 채 번역문을 고치다 행이 목록에서 빠지면 그 순간 저장된다. 고친 내용 저장에 실패하면 내보내기·열기·번역 시작을 멈추고, 창을 닫을 때는 저장하지 않고 닫을지 묻는다.

## 2순위: 잘못된 동작이지만 덜 흔하거나 피해가 작음

**번역 흐름·Gemini**
- 안전 필터로 막힌 응답(`promptFeedback.blockReason`)을 읽지 않아 "예상치 못한 오류(E999)"로 표시된다. `GeminiClient.Types.cs:114`
- 400 응답이 대부분 "API 키가 유효하지 않음(E201)"으로 분류되고, 행 오류 쪽에는 4xx 분기가 없다. `UserFacingErrorClassifier.cs:249`
- 재시도 한도를 다 쓴 행 하나 때문에 같은 배치의 새 행까지 모두 오류가 된다(키 전환 뒤). `TranslationGenerationBudget.cs:49-59`
- 키 전환이 꺼져 있으면 일일 할당량 소진 뒤에도 남은 배치를 몇 시간 동안 재시도한다. `Workers.Process.cs:60`
- 한 행 경로가 원문 끝 공백을 지운다("Gold: " → "골드:"). `TextRequests.cs:473,483`
- 따옴표로 시작·끝나는 번역을 JSON 문자열로 보고 따옴표를 벗긴다. `TextRequests.cs:516-531`
- 프롬프트 캐시 생성이 한 번 실패하면 그 실행 내내 캐시를 끈다. `PromptCache.cs:139`

**토큰·용어·프롬프트**
- 한 단어로 번역한 행동 지문("<헛기침>")이 태그로 오인돼 지워진다. `TokenSanitizer.cs:142-150`
- 배치 프롬프트의 "숫자 토큰 외에는 순서를 바꾸지 말라"가 시스템 프롬프트·검증 규칙(용어·값 토큰은 이동 가능)과 모순된다. `TranslationPrompt.cs:133`
- LotD의 `<page break>`가 값 토큰으로 취급돼 순서 검사를 받지 않고, `[page break]`는 아예 보호되지 않는다. `PlaceholderMasker.cs`, `ProtectedTextKinds.cs`
- 긴 글을 나눌 때 문장 중간(용어 토큰 앞)에서 자른다. `TokenAwareTextSplitter.cs:206-229`
- "Fortify X is active, and enemies…"에 Fortify를 끼워 넣는다. `FortifyListExpander.cs`
- 꺼진 자동 학습 후보(Auto(Session))가 같은 원문의 전체 용어집 항목을 가린다. `GlossaryMerger.cs:23-43`
- 기본 용어집 Fine은 하급인데 공식 TM은 초급이다(Superior 중급, Exquisite 상급과 짝). `기본용어집.md:444`
- 사용자 프롬프트 충돌 검사가 "미번역 문장을 남기지 말 것"처럼 부정문도 충돌로 막는다. `PromptConflictLint.cs`
- "A/B/C per X/Y/Z" 확장의 마지막 항목이 줄바꿈을 넘어간다. `PairedSlashListExpander.Helpers.cs`

**한국어 후처리·품질 검사**
- "않되"를 "않돼"로 바꾼다("해치지 않되" → "않돼"). `SpellingFixStep.cs:165-191`
- "탈모르의 공격으로부터 드래곤본을 보호하라"의 역할을 뒤집는다(드래곤본 안에 "드래곤"이 들어 있어서). `KoreanProtectFromFixer.cs`
- "젊은이?", "그 노인이?", "음...알겠어"를 이전 버전 손상으로 경고한다. `LegacyPostEditDamageRule.cs:181,187`
- 품질 검사가 UI 스레드에서 돌아 큰 프로젝트에서 창이 멈춘다(합성 데이터 11~17초). `MainViewModel.Lqa.Scan.cs`, `NameConsistencyRule.cs`
- "초 초과"를 중복 표현으로 잡아 유료 재번역까지 일으킨다. `LqaHeuristics.cs:48-51`
- 지속시간 교정이 맞는 문장의 숫자를 뒤바꾼다("모든 적은 3초 동안 <25>포인트"). `DurationProbabilityStep.cs`
- 괄호 검사가 원문을 보지 않는다("1) … 2) …"). `BracketMismatchRule.cs:73`
- 프롬프트 누출 정리가 토큰이 있는 행에서는 아예 돌지 않는다. `PromptLeakCleaner.cs:15-18`

**앱 서비스·화면**
- XML 프로젝트 DB 폴더가 그때의 게임 시리즈 선택으로 정해지고 선택은 저장되지 않아, 다음 날 같은 XML을 열면 빈 프로젝트가 된다. `ProjectWorkspaceService.cs:54`
- Addon 이름이 같은 다른 XML(부분 내보내기 등)을 열면 그 파일에 없는 행의 번역이 확인 없이 지워진다. `ProjectWorkspaceService.cs:71`
- 이미 있는 원문으로 용어를 추가하면 중복 행이 생기고, 우선순위가 같으면 새 항목이 적용되지 않는다. `ProjectGlossaryService.cs:23`
- 설정 파일 쓰기가 원자적이지 않아, 손상되면 다음 저장 때 API 키가 지워질 수 있다. `AppSettingsStore.cs:58-74`
- 전체 DB 열기 실패를 조용히 "전체 DB 없음"으로 처리해 용어집·TM 없이 번역한다. `GlobalProjectDbService.cs:49-68`
- 필터를 켠 채 번역문을 고치면 그 행이 목록에서 사라진다. `MainViewModel.Core.cs:150`
- ESP 열기를 늦게 취소하면 이전 프로젝트의 용어집 목록이 새 DB에 붙은 채 남는다. `MainViewModel.Project.Plugins.cs:82-117`
- 프로젝트를 바꿔도 품질 검사 결과가 남아, 누르면 새 프로젝트의 같은 Id 행이 선택된다. `MainViewModel.Lqa.cs:81`
- 용어 추가·가져오기·파일 열기가 용어집·TM 표의 저장하지 않은 수정을 버린다. `MainViewModel.GlobalGlossary.cs` 외
- 번역 중 파일을 열거나 끌어다 놓으면 묻지 않고 번역을 멈춘다. `MainViewModel.FileDrop.cs:23`
- 이전 번역판과 짝을 지을 때 마스터 목록·EditorID를 확인하지 않아 다른 레코드와 짝지어질 수 있다. `Plugins/PreviousTranslationMatcher.cs`
- 용어집·TM 가져오기가 UTF-8만 가정한다(엑셀 CP949 파일이 깨진 채 들어감). `GlossaryFileService.cs:17`

## 3순위: 작은 문제

- API 키가 URL 쿼리에 실린다(앱 로그에는 남지 않음, 프록시는 볼 수 있음). 헤더로 옮길 것. `GeminiClient.*.cs`
- 비용 추정이 countTokens 실패를 모두 "글자 수 ÷ 4"로 조용히 대신한다.
- 경고를 오류로 처리하는 설정 때문에 의존 패키지 보안 공지만으로 CI·릴리스가 깨질 수 있다. `Directory.Build.props`
- 닫은 DB의 SQLite 연결이 풀에 남아 비교용 임시 DB가 지워지지 않는다(이 PC에 2,105개, 약 130MB). `ProjectDb.Core.cs:142`
- 저장한 API 키를 지워도 현재 키 칸에 남아 다시 저장된다. `MainViewModel.Core.ApiKeysAndLogging.cs:84`
- AppLog 크기 제한이 없다. `AppLog.cs`
- 비교 도구가 공식 이름 색인과 프로젝트 TM을 쓰지 않는다. `CompareTranslationService.cs`
- 책 문맥 제목을 대기 중인 행에서만 모은다. `TranslationRunnerService.cs:76`
- 용어집 "추가" 버튼이 입력칸에서 포커스가 빠져야 켜진다. `*GlossaryTabView.xaml`
- 상태 표시줄의 "대기" 수가 경우마다 다르게 센다.
- 번역 중 비교·새로고침 버튼이 말없이 아무것도 안 한다.
- 끌어다 놓아 연 파일은 창을 닫을 때 기다리지 않는다.
