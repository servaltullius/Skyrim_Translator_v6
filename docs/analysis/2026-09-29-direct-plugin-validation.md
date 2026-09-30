# 직접 플러그인 지원 검증 기록

날짜: 2026-09-29. 수정 후보: `2026.09.29-esp-preview4`.

## 구현 결과

Skyrim SE/AE ESP/ESM/ESL을 직접 읽어 기존 프로젝트 DB·번역·비용 추정·문맥·검수 기능으로 연결한다. `Save ESP`는 새 폴더에 동일 파일명의 번역본과 필요한 loose 문자열을 확정한다. 기존 XML 경로는 유지한다. 사용법은 [직접 지원 안내](../direct-plugin-support.md)를 따른다.

전용 바이너리 패치 writer는 수정하지 않은 레코드의 압축 바이트도 복사하며, 수정한 레코드는 표시문구 외의 subrecord framing·payload·FormID·header를 검증한다. localized 테이블은 ID 및 미사용 문자열을 유지한다. EDID/MAST 인코딩은 번역문 인코딩과 분리했다. 원본 및 외부 문자열/BSA의 SHA256을 저장 전에 재검사하고 읽기 잠금을 유지한다.

## 이전 후보에서 발견한 문제와 수정

이전 `esp-preview`는 732개 자동 테스트와 xEdit `-check`를 통과했지만, 실제 USSEP 전체 dump 대조에서 PERK `[00106253] <BlockRunner>`의 EPFT=6/EPFD `bPerkShieldCharge`를 번역 대상으로 노출하는 오류가 발견됐다. 이 값은 Set Boolean Graph Variable의 기술 식별자다. 테스트 문자열을 덧붙이면 기능 변수명을 바꾸므로 이전 후보는 사용 중지했다. 해당 EXE는 `candidate/TulliusTranslator.superseded-perk-bug.exe.disabled`로 보존한다. 구조 검사 통과가 의미적 안전성까지 증명하지 않는 실제 사례다.

EPFT=6은 전부 번역 대상에서 제외하고 EPFT=4/EPF2 및 EPFT=7/EPFD 표시문구만 유지했다. localized·inline PERK 회귀와 독립 dump 대조에 기술 변수 보존 검사를 추가했다. 근거는 [필드 대조 보고서](2026-09-29-plugin-field-registry.md)에 있다.

추가 통합 수정:

- INFO/DIAL의 부모 topic FormID를 parser→DB→문맥/중복 판정에 전달한다. 부모 DIAL이 없는 override patch도 GRUP type 7로 처리하며, 근거가 없는 INFO는 record별로 격리한다.
- 직접 플러그인 대화에는 topic 출처가 없는 원문 전용 TM을 적용하지 않는다. 부분 재개와 Compare에도 적용한다. 기존 TM 및 Done/Edited 복원은 유지한다.
- 공유 StringID 충돌 시 정확한 `string:STRINGS/42` 검색어와 관련 행을 안내한다. 선택 행에 FormID/필드/StringID를 표시한다.
- LQA와 보호 요소 불일치 필터가 writer와 같은 최종 텍스트 검사를 사용한다. 원문 테이블·인코딩·원본 변경·빈 번역·출력 충돌은 원인별 고정 안내를 제공한다.

## preview3의 취소·응답성 수정

preview2는 `IsWorkspaceInteractive=false`일 때 창 전체를 비활성화하여 취소 버튼도 사용할 수 없었다. 취소 버튼을 항상 보이는 상태 표시줄 첫 항목으로 옮기고, 작업 중 잠금은 상단 설정·편집 탭에만 적용했다. 창 전체 잠금은 종료 처리에만 사용한다.

SQLite의 async API도 호출 스레드에서 동기 작업을 하므로 파싱·가져오기·내보내기 조회·검증 전체를 백그라운드 작업으로 옮겼다. 가져오기 커밋 직후에는 새 DB/행을 먼저 화면의 프로젝트로 채택한다. 이때 늦게 취소하면 새 프로젝트는 유지하고 보조 새로고침만 생략한다.

추가 회귀 4건은 XAML 취소 버튼의 접근 가능 구조, 읽기 준비 중 STA 취소 처리, 실제 SQLite 조회 중 STA 취소 처리, 커밋 직후 취소와 UI 컬렉션 이벤트를 검증한다. 임시 설정과 DB를 사용하며 실제 앱 창을 띄우지 않았다. 이 변경은 바이너리 reader/writer나 필드 레지스트리를 바꾸지 않아 아래 preview2 실제 파일·xEdit 검증 결과가 그대로 적용된다.

이번 분담에서는 App 담당이 취소·응답성 수정과 4개 회귀를 작성했고, 독립 검증 담당이 App/Core를 참조하지 않는 Mutagen 문자열 목록 도구를 작성했다. 주 에이전트가 전체 797개 테스트, 후보 publish, 실제 입력의 독립 도구 실행과 결과 판정을 담당했다.

## 최종 자동·독립 검증

| 검사 | 결과 |
| --- | --- |
| Release 전체 회귀 | **797/797 통과**, 실패·건너뜀 0 |
| App/test 및 Validate CLI 빌드 | 경고 0, 오류 0 |
| Windows x64 publish | self-contained single-file, 별도 `candidate-preview4` 폴더 |
| App 서비스/VM | 임시 DB·설정·가짜 HTTP로 직접 입력, 비용 추정, 문맥, Start, 완료 DB, export, 실패 복구 검증 |
| 대화 문맥·TM | 실제 요청 프롬프트 topic 격리, context OFF 중복 분리, 부분 재개, XML 기존 동작, Compare 회귀 |
| 보호 요소 | 태그·페이지·printf·중괄호·줄바꿈·서식 순서에 대한 LQA와 UI 필터 일치 |
| 입력 무결성 | XXXX 경계, zlib/LZ4 손상·후행 데이터, BSA 충돌, 중복 FormID, 잘못된 인코딩 거부 |
| 정의 대조 | xEdit 134개 레코드 서명·Mutagen schema 149개, 명시 번역 필드 80조합. 조건과 의도적 제외는 별도 보고서 참고 |
| 독립 도구 | SSEDump64 **4.1.5f**, 실제 원본/출력 구조 검사와 전체 문구 대조 |
| 독립 문자열 목록 | Mutagen 0.53.1, USSEP 57,205개 major record 완료. 비어 있지 않은 translated getter **18,870/18,870 일치**, 양방향 차이 0 |
| 지역화 전체 편집 | Skyrim.esm **67,388개** 원문/수정문 대조, ESM 바이트·전체 테이블 ID·미편집 26개 항목 보존. 외부 reader의 9개 진단을 명시한 범위 비교 |

최종 테스트: `artifacts/direct-plugin-20260929/tests-preview4/results.trx`. 첫 통합 테스트의 TM note 실패는 fixture가 정규화되지 않은 키 `Yes`를 사용한 것이 원인이었다. 실제 계약에 맞는 `yes`로 고치고 Done/Edited 및 TM 보존을 추가 확인했다. 생산코드의 우회 규칙을 약화하지 않았다. 네트워크 번역은 가짜 HTTP이며 유료 API 호출은 0회다.

분담 결과: 형식 담당은 PERK 의미 분류와 LQA/Compare 회귀를 보강했다. App/문맥 담당은 안전한 오류 안내 및 topic→DB→번역 흐름을 구현했다. 독립 검증 담당은 xEdit dump 대조 도구와 INFO의 가상 Topic 출력 경계 처리를 작성했다. 주 에이전트가 writer·필드 식별 UI를 통합하고 최종 테스트·실제 corpus·xEdit·publish를 실행했다.

## 실제 파일 결과

| 입력 | 표시문구 | 최종 코드 검증 결과 |
| --- | ---: | --- |
| 프로젝트의 USSEP ESP | **18,870** | 모든 표시문구에 ` 검증`을 붙여 저장. 내부 비번역 바이트 보존 및 xEdit 구조 검사 통과. 독립 문구 대조 18,870/18,870, 기술 변수 변경 0 |
| Stendarr's Hammer ESL | 12 | BSA 105/LZ4의 3종 테이블 읽기. WEAP 이름 한글 변경, ESL SHA 불변. xEdit 한글 decode 확인 출력과 동일한 파일 세트 재생성 |
| Skyrim.esm | **67,388** | 기술 변수 3개 제외. localized 대형 파일 읽기·무편집 저장, ESM과 문자열 테이블 3종 SHA 동일 |

USSEP 무편집 출력도 앞선 검증에서 원본 SHA256과 동일했다. 수정 스트레스 파일은 AI 번역 품질 평가나 게임에 설치할 번역본이 아니다.

USSEP 원본과 수정본의 xEdit `-check` stdout은 모두 0바이트이며 stderr는 `All Done.`으로 종료했다. 기존 unresolved-reference note 1개는 양쪽 동일하고 새 오류는 없었다. Stendarr는 전체 dump에서 `WEAP [03000D62]` 이름 `스텐다르의 망치`를 확인했다. 종료 코드 0만으로 성공 판정하지 않았다.

독립 대조는 `tools/verify_xedit_dump_fields.py`로 수행했다. manifest의 키·기대 문구를 xEdit가 출력한 record signature/FormID/subrecord occurrence 및 문자열과 비교한다. CRLF/CR→LF 외에는 정규화하지 않는다. 여러 줄 경계가 모호하면 실패한다. INFO 앞에 xEdit가 넣는 저장되지 않는 가상 Topic은 DIAL FormID·들여쓰기·Record Header·Signature INFO까지 연속 확인한 형태만 허용한다.

| 독립 대조 | 원본/출력 일치 문구 | 누락·불일치·경계 오류 | 기술 변수 변경 | 판정 |
| --- | ---: | ---: | ---: | --- |
| 이전 잘못된 후보 | 18,871/18,871 | 각 0 | **1** (`bPerkShieldCharge`) | **실패** |
| 수정 후보 | 18,870/18,870 | 각 0 | **0** | **통과** |

수정 후보는 record/subrecord sequence hash도 원본과 같다. 이 대조는 요청된 필드의 저장 결과 및 특정 기술 식별자 보존을 독립 확인한다. 레지스트리에 없는 모든 문자열의 완전한 번역 여부나 비문자열 데이터 전체를 독립적으로 증명하는 도구는 아니다.

증거: `ussep-preview2-xedit-fields.json` 및 `.jsonl`, 실패 대조 `ussep-xedit-field-validation-superseded-v2.json`, 원본/이전/수정 전체 dump. 모두 `artifacts/direct-plugin-20260929`에 있다.

검증용 master·문자열을 artifacts의 `xedit-data`에 복사했다. 필요한 재귀 MAST 누락 0, source/복사본 SHA 동일, source 변경 없음. 실제 설치/MO2 프로필에는 쓰지 않았다. `-d:`만으로 master 경로를 바꿀 수 없으므로 입력 복사본과 masters를 같은 폴더에 배치했다. UTF-8 `_english` 테이블은 xEdit용 `.cpoverride`를 검증 폴더에만 추가했다.

### Mutagen 독립 문자열 목록

`tools/XTranslatorAi.PluginInventory`는 App/Core/번역 필드 레지스트리를 참조하지 않고 Mutagen.Bethesda.Skyrim 0.53.1의 읽기 전용 getter를 순회한다. 실제 USSEP 원본에서 **57,205개 major record**를 끝까지 읽었다(TES4 헤더 제외). translated getter 20,857개 중 비어 있지 않은 값은 **18,870개**, 빈 값은 1,987개였으며 삭제 레코드의 translated 값은 0개였다. 앱 manifest의 18,870개와 `(record signature, 파일 내부 FormID, 정확한 문자열)` 다중집합이 모두 같았다. 앱에만 있는 값 0개, 독립 목록에만 있는 값 0개, 진단 오류 0개다.

일반 문자열 131,884개도 별도로 보존했다. PERK `[00106253]`의 `Effects[0].Text = bPerkShieldCharge`는 plain 문자열로 분류되어 translated 목록에 들어가지 않는다. 원본 입력 해시는 읽기 잠금을 유지한 채 전후 재확인했으며, 비교 manifest의 원본 SHA도 같은 입력에 결속했다. manifest는 한 번 읽은 바이트로 파싱과 해시 계산을 수행한다.

순회가 사용하는 수치형 제외 내역은 결과에 남긴다. P3Float의 계산 속성 재귀와 숫자 Array2d를 제외하며, 해당 버전에서 읽기 getter가 미구현인 `IWeatherAmbientColorSetGetter` 8개는 공식 정의가 색·숫자만 포함함을 확인한 **선언 스키마 제외**다. 이 색 값을 성공적으로 읽었다는 주장은 하지 않는다. WeatherImageSpaces/VolumetricLighting의 유한 enum 인덱서는 네 값을 각각 순회하고 이름별 별칭의 중복을 제거했다. 속성이 없는 leaf는 모두 BinaryWriteTranslation 도우미였다.

최초 도구 실행은 계산 속성 재귀로 메모리가 과도하게 늘어 중단했다. 첫 제한 적용 실행 `ussep-mutagen-inventory.json`도 42,541개 레코드에서 방문 상한에 걸린 **실패한 부분 보고서**다. 일반 문자열을 숫자 시퀀스로 제외하던 오류와 weather 인덱서 처리를 고친 뒤 새 `ussep-mutagen-inventory-v2.json`에서 완료를 확인했다. 100개 레코드 진단 실행들은 의도한 `record_limit` 실패이며 전체 비교를 하지 않았다. 최종 실행은 약 95.5초, 관찰 최대 working set 약 262MiB, 2,682,112개 노드였다. 이 도구의 빌드도 경고·오류 0이다.

이 결과는 **USSEP의 Mutagen translated getter 목록에 대한 선택 누락 대조**다. plain 문자열 전체의 표시 가능 여부, 다른 모든 모드의 커버리지, 정확한 subrecord occurrence를 증명하지 않는다. 필드별 저장 결과는 앞선 xEdit 대조가 담당한다. 전체 JSON과 로그, 소스/입력 해시 및 요약 `ussep-mutagen-inventory-summary.json`을 artifacts에 보관했다.

이후 working set 조회를 100ms 간격으로 제한하고 관리 힙·방문·문자열 개수 상한은 유지했다. USSEP 재실행 v3은 v2의 문자열 행 152,741개와 대조 결과가 모두 같았고, 비교 완료 시간이 95.5초에서 4.9초로 줄었다. 이는 검사 도구의 관찰값이며 앱 번역 속도 수치가 아니다. 지역화 빈값 처리를 추가한 최종 v4에서도 18,870개 대조가 일치했다. 최종 v4는 선택 getter 부재 15,548개도 명시적으로 기록하므로 translated 슬롯은 36,405개(실제 값 18,870, 빈값 1,987, 부재 15,548)다. 최종 증거는 `ussep-mutagen-inventory-v4.json`과 `ussep-mutagen-inventory-final-summary.json`이다.

### Skyrim.esm 지역화 전체 편집과 reader 한계

원본 Skyrim.esm의 **869,687개 major record**를 독립 파서로 순회했다. 앱이 노출한 67,388개 모든 원문에 ` 검증`을 붙인 별도 출력도 같은 방식으로 읽었다. 기대 manifest는 원문 manifest와 편집 지시에서 생성한 **기대값**으로 표시하며, 관찰한 출력으로 위장하지 않는다. 실제 결과는 별도 reader 출력과 대조한다.

별도 Python 테이블 파서 `tools/verify_localized_tables.py`는 모든 ID·원문·수정문과 대상 밖 항목의 바이트를 확인했다. ESM 전체 SHA는 원본과 동일하다.

| 테이블 | 전체 항목 | 수정 ID | 미편집 바이트 보존 | ID 집합·순서 |
| --- | ---: | ---: | ---: | --- |
| STRINGS | 30,301 | 30,292 | 9 | 동일 |
| DLSTRINGS | 2,686 | 2,669 | 17 | 동일 |
| ILSTRINGS | 34,427 | 34,427 | 0 | 동일 |

대상 밖의 빈 `STRINGS/26239`를 `NEGATIVE`로 바꾼 대조는 정확히 `Unedited entry bytes changed: STRINGS/26239`로 실패했다. 성공/실패 증거는 `skyrim-localized-tables-check.json`과 `skyrim-localized-tables-negative-entry-check.json`이다. 모든 출력은 스트레스 검증용이며 게임에 설치할 번역본이 아니다.

최초 Mutagen 실행은 ID 0의 빈 translated sentinel을 실제 언어 누락으로 오인해 진단 상한에서 중단했다. pinned 구현의 `StringsKey`와 빈 sentinel을 구분하도록 도구를 수정했다. 작은 실제 바이너리 fixture에서 ID 0은 허용하고, ID 42가 테이블에 없으면 `missing_language`로 실패함을 확인했다(`localized-inventory-probe/probe-check.json`). 실제 nonzero 누락을 빈값으로 덮지 않는다.

수정 도구는 원본·편집본 모두 마지막 레코드까지 도달하고 입력 해시를 재확인했지만, **각 9개 진단 때문에 `TraversalCompleted=false`를 유지**한다. 원본/편집 보고서는 `skyrim-mutagen-original-v2.json`, `skyrim-mutagen-edited-v2.json`이며 전체 reader 성공으로 처리하지 않는다.

- CPTH 4건: `[00034CF0]` 조건 1과 `[00000016]` 조건 0의 `Parameter1/2` getter 미구현이다. 원시 CTDA의 함수 407은 `GetVATSValue`이며 분기 4/값 0과 분기 6/값 11은 숫자 조건이다. 표시 문자열이 빠진 오류로 해석하지 않는다. [xEdit 정의](https://github.com/TES5Edit/TES5Edit/blob/f5c00f3fa3ee39511185515802647246c807f759/Core/wbDefinitionsTES5.pas#L5731-L5751), [Mutagen 부모 getter](https://github.com/Mutagen-Modding/Mutagen/blob/bdbb6ff6faad3b4c3b0689e98e889ecd205068c8/Mutagen.Bethesda.Skyrim/Records/Common%20Subrecords/ConditionData.cs#L55-L60).
- SNDR 5건: `String`이 참조하는 nonzero ID 17·18을 찾지 못했다. 정상 빈값이나 확정된 reader 버그로 바꾸지 않는다. 이 외에도 앱에 노출하지 않는 SNDR 값 54개가 해석됐다. [Mutagen은 FNAM을 Normal translated로 정의](https://github.com/Mutagen-Modding/Mutagen/blob/bdbb6ff6faad3b4c3b0689e98e889ecd205068c8/Mutagen.Bethesda.Skyrim/Records/Major%20Records/SoundDescriptor.xml#L11)하고 [xEdit는 localized/cpIgnore로 정의](https://github.com/TES5Edit/TES5Edit/blob/f5c00f3fa3ee39511185515802647246c807f759/Core/wbDefinitionsTES5.pas#L9645)한다. 어느 쪽도 실제 게임에서 표시되거나 무시된다는 증명은 아니다.

별도 `skyrim-mutagen-scoped-comparison.json`은 이 한계를 그대로 기록하고 SNDR.String을 명시적으로 범위에서 제외한다. 나머지 비어 있지 않은 translated 값과 앱 manifest를 **(record signature, 파일 내부 FormID, StringsKey, 정확한 문자열)** 다중집합으로 비교했다. 원문과 수정문 각각 **67,388개 전부 일치**, 누락/예상 밖 값 0이다. 각 실행의 실제 문자열 테이블 SHA를 Python 보존 검사의 원본/출력 SHA와 결속했다. 전체 표시 문자열의 완전한 누락 검사가 아니라 **앱이 노출한 값의 독립 대조**다.

> 후속 결론(2026-09-30): [SNDR:FNAM 확인](2026-09-30-sndr-fnam.md)에서 SNDR:FNAM이 문자열 ID가 아니라 비트 플래그임을 확인했다. 아래의 54개 간접 변경과 ID 17·18 조회 실패 5건은 Mutagen 해석에서만 생긴 결과이며 게임 영향은 없다.

SNDR:FNAM의 필드 바이트와 참조 ID는 유지되지만, ID 1·2·20을 번역 항목과 공유해 Mutagen으로 해석한 값 54개도 간접적으로 변경됐다. 실제 게임 용도와 영향은 확인하지 않았다. 이를 원문·의미의 완전한 보존으로 표현하면 과장이다. preview4는 앱의 `ambiguous_field` 안내를 필드 바이트 보존과 공유 ID 내용 변경 가능성으로 구분했다. 근거 없이 ID를 복제하거나 reader 오류를 무시하는 동작은 추가하지 않았다. 최종 전체 회귀 **797/797**과 Windows x64 publish를 완료했다. 첫 clean은 RID 자산 부재로 중단됐고, win-x64 restore 후 clean/publish가 정상 완료됐다.

### SHA256

| 파일 | SHA256 |
| --- | --- |
| USSEP 원본/무편집 | `6AF502105E79BFC9D73331A486D947FE9D4F79E2B37FCB2FE5C81FDD6A8A3709` |
| USSEP 수정 후보 전체 문구 편집 | `3AF3D76B5AE9B7F8A42F885CEDAF44CA1E6D5B0B573314340C380DCA741E5EAF` |
| 이전 잘못된 USSEP 편집본 (사용 금지) | `D58432B571FFFE8ECBB27BCE41A58DD8213378DA919B23603840DCFCA3A1AC3C` |
| Stendarr ESL 원본/출력 | `B04F468ADB59ABF6BD9C40A8B18A947377AA3EF519FB2E8667975EE45541917D` |
| Skyrim.esm 원본/무편집 | `2BBC77FDEC35A70EF96B710F8C525E50A1DB9E63E11A391A0EB9EE8F56D36107` |
| SSEDump64.exe | `30C085B8A20DC02BF5ABAE2CB6610870C9BB9EEA50330E0FE5ADE98E3F89EFE6` |
| 수정 후보 TulliusTranslator.exe | `A12A9EB2F90084B616040B3EBBCC9963A1B05888D2037688929D3673A16E85A7` |

## 후보와 남은 조건

실행 파일: `artifacts/direct-plugin-20260929/candidate-preview4/TulliusTranslator.exe`. 크기 66,748,554바이트, ProductVersion `2026.09.29-esp-preview4+df381508703319f93684ccacbcd21945356b0e70`. 커밋 suffix는 기반 커밋이며 이 수정은 아직 커밋하지 않았다. 의존성 고지 파일을 `Licenses`에 포함했다. 기존 루트 EXE는 변경하지 않았다.

실제 LOTD ESM 경로는 아직 확보하지 못했다. LOTD XML은 직접 바이너리 검증을 대신할 수 없다. 후보 앱의 실제 화면 조작, 게임 내 한글 표시·대사·퀘스트 확인도 미실시다. 이전 루트 EXE 교체·실행·사용자 설정 접근을 함께 수행하던 작업은 자동 승인 검토에서 `blocked by policy`로 거절됐으며 자세한 이유는 제공되지 않았다. 그 실행 절차를 재시도하거나 우회하지 않았고, 이번 검증은 임시 환경의 테스트 및 독립 CLI로 수행했다.

현재 판정은 **수정 및 자동/독립 도구 검증을 마친 직접 지원 후보**다. 전체 사용자 목표의 완료 또는 모든 ESP의 무오류 보장으로 판정하지 않는다.
