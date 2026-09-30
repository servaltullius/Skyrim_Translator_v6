# ESP/ESM/ESL 직접 지원 가능성 및 검증 조건

이 문서는 구현 전 검토 기록이다. 2026-09-29 이후 구현·후보 빌드의 현재 상태는 [직접 지원 안내](direct-plugin-support.md)와 [구현 계획의 진행 상태](plans/2026-09-29-direct-plugin-support.md)를 따른다.

조사일: 2026-09-28. 이 문서는 Mutagen 및 xTranslator 개발자가 공개한 문서·소스에 근거한 설계 검토다. Bethesda의 완전한 공식 파일 명세나 이 프로젝트의 직접 플러그인 지원 완료 증거는 아니다. 이번 조사에서는 플러그인 라이브러리를 설치하거나 게임 파일을 수정하지 않았다.

## 판정

**직접 읽기와 제한된 번역 필드 쓰기는 구현 가능하지만, 현재 증거로 ESP/ESM/ESL 전체에 대한 완벽한 보존·번역·재저장을 보장할 수 없다.** Mutagen은 이 파일군의 읽기·쓰기와 문자열 처리를 제공한다. 그러나 라이브러리 지원 여부와 이 번역기의 안전한 통합 검증은 별개다. [Mutagen 개요](https://mutagen-modding.github.io/Mutagen/)

사용자의 조건인 “문제없이 완벽하게 지원할 수 있으면 허용”을 충족했다고 볼 실증 자료가 아직 없다. 따라서 이번 개선판에 검증되지 않은 바이너리 writer를 켜지 않는다. 지원하지 않는 입력을 조용히 일부만 처리하는 기능도 제공하면 안 된다.

## 지금 유지할 작업 방식과 다음 구현 단위

| 작업 | 현재 판단 | 다음 구현의 합격 기준 |
| --- | --- | --- |
| xTranslator XML 번역 후 원본 플러그인에 적용 | 기존 제공 경로 | XML 원본/DB/출력 행과 구조 대조, xTranslator에서 식별자와 번역 적용 확인 |
| ESP/ESM/ESL 읽기 전용 목록·번역 미리보기 | 첫 독립 구현 후보 | 게임/릴리스·마스터·언어·필드 식별, 미지원 항목과 누락 문자열 목록 제공 |
| localized 플러그인의 기존 ID를 유지하는 loose 문자열 테이블 번역 | 별도 후보 | 플러그인 해시 불변, 타입별 ID 집합 불변, 지정 언어 텍스트만 변경 |
| non-localized 플러그인의 허용된 표시 문자열 수정 | 높은 검증 비용이 필요한 후보 | 수정 대상 필드 외 데이터와 참조 불변, 독립 도구와 게임 검증 |
| 전체 레코드 일반 재직렬화, 자동 localize/delocalize, ESL 변환·FormID 압축 | 초기 범위 제외 | 별도 요구사항 및 훨씬 넓은 검증 없이는 활성화하지 않음 |
| PEX·MCM·음성·BSA/BA2 재패킹 | 플러그인 번역과 별도 기능 | 전용 파서·writer와 자산별 검증 필요 |

xTranslator 자체도 ESP 모드와 localized 파일의 레코드 구조를 이용하는 Hybrid 모드를 구분하고, MCM·PEX를 별도 편집 대상으로 취급한다. 또한 localize/delocalize 도구가 아니라고 명시한다. 파일 선택창에 세 확장자를 추가하는 것으로 이 범위를 대체할 수 없다. [xTranslator 기능 설명](https://github.com/MGuffin/xTranslator#readme)

## 반드시 분리해야 하는 기술적 문제

### 1. 확장자, 게임 릴리스, 마스터 형식

ESP/ESM/ESL 확장자만으로 실제 헤더 플래그와 해석 규칙을 결정하면 안 된다. 예를 들어 Small 플래그를 가진 ESP는 게임에서 light master로 처리된다. 초기 구현은 Skyrim LE/SE/VR 등을 한 범주로 암묵 처리하지 말고 검증한 GameRelease 목록을 고정해야 한다. [Mutagen Compaction](https://mutagen-modding.github.io/Mutagen/plugins/Compaction/)

Starfield는 light/medium master의 분리된 인덱스 체계를 다룬다. 공식 문서는 올바른 master style 정보가 없으면 해당 마스터 참조의 FormID 해석이 손상될 수 있다고 명시한다. 파일 하나만 받은 상태에서 필요한 master 정보를 추측해 쓰기를 계속하면 안 된다. [Mutagen Importing](https://mutagen-modding.github.io/Mutagen/plugins/Importing/#starfield-and-separated-master-games)

### 2. localized STRINGS / DLSTRINGS / ILSTRINGS

localized 플래그가 켜지면 플러그인 내 문자열 자리에 테이블 조회용 인덱스가 들어간다. 언어별 loose 파일과 아카이브 내부 파일의 검색·우선순위도 결과에 영향을 준다. 프로그램이 현재 읽은 게임·언어·테이블·실제 파일 경로를 저장해야 한다. [Mutagen Strings](https://mutagen-modding.github.io/Mutagen/Strings/)

세 테이블을 하나의 ID 사전으로 합치면 안 된다. Mutagen 구현은 Normal/IL/DL을 구분하며, Normal은 null-terminated 텍스트, IL/DL은 바이트 길이와 null-terminated 텍스트를 기록한다. 글자 수를 바이트 수로 잘못 쓰거나 길이·offset을 갱신하지 않으면 한글 변환에서 손상된다. [Mutagen StringsWriter 소스](https://github.com/Mutagen-Modding/Mutagen/blob/dev/Mutagen.Bethesda.Core/Strings/StringsWriter.cs)

설계상 키는 최소한 `게임 릴리스 + ModKey + 언어 + 테이블 종류 + StringID`여야 한다. 레코드 문맥까지 붙여 어떤 필드가 그 ID를 참조하는지 추적한다. 같은 StringID를 여러 필드가 공유하면 서로 다른 번역을 넣을 수 없으므로 충돌을 보여 주고 처리 방향을 명시해야 한다.

**중요한 제한:** Mutagen의 일반 문자열 writer는 기존 StringID를 보존하는 편집기로 가정하면 안 된다. 공식 correctness 문서는 원본 문자열 키를 보존하지 않고 다시 인덱싱한다고 설명한다. 플러그인을 그대로 두고 문자열 파일만 교체하는 모드에는 기존 ID를 보존하는 별도 경로와 검증이 필요하다. [Mutagen Correctness](https://mutagen-modding.github.io/Mutagen/Correctness/#strings-file-key-reindexing)

localized 출력은 플러그인과 여러 테이블이 하나의 배포 단위다. 일부 파일만 새 버전으로 저장한 상태를 성공으로 표시하면 안 된다. 임시 디렉터리에 전체를 기록하고 검증한 뒤 별도 출력 폴더로 확정해야 한다.

### 3. FormID·FormKey·EDID·필드 위치

FormID의 master index 부분은 파일/로드 순서 문맥에 의존한다. Mutagen의 FormKey는 원본 ModKey와 인덱스를 제외한 ID를 묶는다. 번역문을 원래 레코드로 되돌리는 키를 EDID 또는 원문 문자열 하나로 만들면 중복·누락 EDID와 반복 필드에서 잘못된 대상을 덮어쓸 수 있다. [Mutagen 식별자 문서](https://mutagen-modding.github.io/Mutagen/plugins/ModKey,%20FormKey,%20FormLink/)

제안 키는 `GameRelease + 입력 플러그인 SHA-256 + FormKey + record type + field path + 반복 필드 ordinal`이다. localized라면 테이블 종류와 원래 StringID도 포함한다. 입력 해시가 바뀌면 수입 때 저장한 위치를 그대로 사용하지 말고 다시 대응 검증해야 한다.

번역 때문에 master 목록, ESM/ESL 플래그, FormID, 로드 순서, 레코드 추가·삭제를 바꾸지 않는다. Mutagen exporter의 기본값은 master 목록/정렬과 next FormID 등을 처리하므로, 무변경 저장 실험 때부터 출력 정책을 명시적으로 점검해야 한다. 검증을 우회하는 옵션을 켜서 오류를 숨기는 것은 해결책이 아니다. [Mutagen Exporting](https://mutagen-modding.github.io/Mutagen/plugins/Exporting/)

### 4. 압축과 비번역 데이터 보존

Mutagen의 passthrough 검증은 압축 해제, float 표현, subrecord 순서, 문자열 키 등을 정규화한 비교를 사용한다. 따라서 “Mutagen으로 읽고 썼으니 원본 바이트가 유지됐다”는 주장은 성립하지 않는다. 정규화 후의 의미 보존과 원시 바이트 보존을 서로 다른 증거로 보고해야 한다. [Mutagen Correctness](https://mutagen-modding.github.io/Mutagen/Correctness/)

향후 writer는 수정하지 않은 레코드의 원본 바이트를 그대로 복사하는 설계를 우선 검토한다. 수정한 압축 레코드에는 압축 해제 payload 비교, 원래 플래그, 압축/비압축 길이, 상위 GRUP 길이, 큰 subrecord 길이와 반복 구조 검증이 필요하다. 이는 이번에 확인된 구현 완료 사항이 아니라 요구되는 테스트 항목이다. Mutagen은 게임별 header와 frame을 읽는 저수준 API도 제공하지만, 그것만으로 이 보존 계약이 자동 충족되지는 않는다. [Mutagen Header Structs](https://mutagen-modding.github.io/Mutagen/lowlevel/Header-Structs/)

### 5. unknown record/subrecord

미지원 레코드를 버리고 나머지만 저장하는 정책은 허용하지 않는다. Mutagen은 `ThrowIfUnknownSubrecord(true)`를 제공한다. 같은 프로젝트의 Spriggit도 모르는 레코드를 만났을 때 데이터 손실 방지를 위해 중단한다고 설명한다. [Mutagen Importing](https://mutagen-modding.github.io/Mutagen/plugins/Importing/#throwifunknownsubrecord), [Spriggit Unexpected Records](https://mutagen-modding.github.io/Spriggit/unexpected-records/)

읽기 전용 목록에서 미지원 항목을 보여 줄 수는 있지만 전체를 처리했다고 표시하지 않는다. 쓰기는 전체 레코드를 읽고 검사해야 한다. 일부 group만 읽는 import mask를 그대로 전체 플러그인 저장에 사용하면 안 된다. unknown payload 원본 복사를 지원하더라도 수정 레코드 내부의 구조를 이해하지 못하면 쓰기를 거부한다.

### 6. 문자열처럼 보이는 기술 필드와 인코딩

자산 경로, EditorID, script/property 식별자, 조건문 데이터는 일반 표시 문구와 구분해야 한다. 문자열 타입이라는 이유로 모두 번역하는 반사(reflection) 기반 루프는 부적절하다. 게임·레코드별 허용 필드 목록과 반복 인덱스를 명시하고, VMAD/PEX는 별도 범위로 취급한다.

xTranslator 문서는 게임별 인코딩과 codepage 설정, VMAD 편집 제한을 다룬다. 이 프로젝트도 원본 decode→encode 무변경 왕복을 먼저 확인하고, 대상 문자를 표현할 수 없는 인코딩에서는 대체문자 `?`로 조용히 저장하지 않아야 한다. 번역 필드의 byte length, terminator, markup·플레이스홀더·개행 보존을 별도로 검사한다. [xTranslator README](https://github.com/MGuffin/xTranslator#readme)

## 권장 통합 구조

다음은 아직 구현하지 않은 제안이다.

1. `PluginReader`: 파일과 의존 문자열 테이블을 읽기 전용으로 검사하고 원본 해시·릴리스·마스터·지원 범위를 기록한다.
2. `TranslationDocument`: 기존 XML 행 모델과 분리된 출처 키를 유지하면서 Source/Dest/문맥/검증 결과를 번역 엔진에 제공한다.
3. `PluginEditPlan`: 변경할 정확한 필드와 원문, 이전 값, 번역값, byte/ID 영향만 담는다. 번역 엔진은 바이너리 구조에 접근하지 않는다.
4. `PluginWriter`: 검증된 게임·필드 조합만 별도 출력 폴더로 쓴다. 원본 변경 감지, 취소·실패 시 미완성 출력 정리, 전체 파일 집합의 확정을 담당한다.
5. `PluginVerifier`: 같은 writer의 재읽기 외에 독립 도구와 원본 비교를 수행한다. 미지원 요소 또는 예상하지 않은 차이가 있으면 배포 후보를 만들지 않는다.

처음부터 XML과 플러그인 저장 방식을 하나의 `RawStringXml` 필드에 섞지 않는다. 기존 XML 경로는 독립적으로 유지한다.

## 직접 쓰기 활성화 전 검증 표

| 축 | 최소 검증 |
| --- | --- |
| 입력 집합 | 허가된 복사본, 합성 fixture, 실제 모드 표본; 원본 SHA-256과 기대 레코드/문자열 목록 기록 |
| 게임/버전 | 지원할 릴리스별 fixture와 parser 버전 고정; 새 게임 업데이트는 회귀 검증 후 승격 |
| 파일 형식 | ESP·ESM·ESL 및 ESL-flagged ESP; full/light/medium을 실제 해당 게임 규칙으로 시험 |
| 구조 | 빈 그룹·중첩 그룹·삭제/override·반복 필드·무EDID·중복 원문·알 수 없는 레코드 |
| 문자열 | inline/localized, STRINGS/DLSTRINGS/ILSTRINGS, ID 공유·누락·충돌, 빈값·한글·긴 텍스트·태그·CR/LF·대상 인코딩 오류 |
| 저장 | 무편집 왕복, 한 필드 편집, 다중 필드 편집; 수정하지 않은 바이트·FormKey·masters·flags·비번역필드 대조 |
| 압축 | 압축/비압축 레코드, 압축 해제 payload, 큰 subrecord와 길이 경계, 실패·취소 중 원본 불변 |
| localized 묶음 | 플러그인↔테이블 모든 참조 유효, 지정하지 않은 언어 보존, 일부 파일만 성공하는 상태 방지 |
| 독립 검증 | xEdit/xTranslator로 재열기와 오류 검사, 원본과 허용된 번역 필드만 다른지 비교 |
| 실제 게임 | 원본과 분리된 MO2 테스트 프로필에서 UI·책·대화·퀘스트·저장/재로드 확인; static pass와 구분 |

몇 개의 합성 fixture 통과를 모든 플러그인의 완벽 지원으로 표현하지 않는다. 유한한 검증을 마친 조합은 명확한 지원표와 미지원 입력 중단 정책으로 출시할 수 있다. 모든 미래 게임 버전과 임의의 unknown 레코드까지 지원한다는 약속은 하지 않는다.

## 이번 조사 후 남은 결정

- 원본 바이트 보존이 필수인지, 허용된 정규화 후 의미 보존까지 인정할지 제품 계약을 정해야 한다.
- 첫 game/release와 필드 목록, localized ID 보존 방식, 실제 fixture corpus가 필요하다.
- Mutagen을 채택하면 고정 package 버전과 현재 net8 앱의 호환성을 별도 빌드로 확인해야 한다. 이번 조사에서는 패키지 설치나 호환 빌드를 하지 않았다.
- xTranslator 소스/실행 파일을 직접 통합하는 경우 해당 프로젝트의 배포·라이선스 조건을 별도로 검토해야 한다. 현재 XML 교환을 통한 사용은 직접 writer 구현과 구분한다.

현재의 안전한 결론은 XML 경로 개선을 배포 가능한 수준으로 검증하고, 직접 플러그인 기능은 위 계약과 표본 검증이 끝난 범위만 단계적으로 활성화하는 것이다.
