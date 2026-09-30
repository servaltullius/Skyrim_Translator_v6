# XML / 플러그인 검증 CLI

## 플러그인 직접 검증

```powershell
dotnet run --project tools/XTranslatorAi.Validate -c Release -- --plugin input.esp new-output-folder --report report.json --edits edits.json
```

출력 폴더를 생략하면 읽기만 합니다. `--edits`는 보고서 `Fields[].Key`를 키, 저장할 문구를 값으로 쓰는 JSON 객체입니다. 생략하면 문구를 편집하지 않고 내보냅니다. API는 호출하지 않습니다.

- `--source-language english`: 원본 문자열 파일명 슬롯입니다.
- `--source-encoding utf-8`, `--target-encoding utf-8`: 표시문구와 문자열 테이블 인코딩입니다. target 생략 시 source를 유지합니다.
- `--metadata-encoding windows-1252`: EDID/MAST 해석용입니다. 저장 시 해당 필드의 바이트는 유지합니다.
- `--strings-directory path`: 필요한 경우 loose 문자열 폴더를 지정합니다. 기본은 플러그인 옆 `Strings`, 그다음 같은 이름의 BSA입니다.
- 인코딩은 UTF-8, windows-1252, cp949를 지원합니다. 새 출력 폴더와 새 보고서 경로만 허용합니다.

검증은 구조 재해석·번역문·비번역 데이터·ID·원본 변경 여부 및 디스크 결과를 확인합니다. 내부 reader/writer가 공유하는 오류까지 검출하지는 못하므로 xEdit와 게임 확인은 별도입니다. 상세 범위는 [ESP 사용·검증 안내](../../docs/direct-plugin-support.md)를 참고하세요.

### xEdit dump 독립 대조

원본과 출력의 전체 SSEDump64 4.1.5f dump를 만든 뒤 다음 도구로 문구를 전수 대조할 수 있습니다. 검사할 플러그인과 필요한 재귀 masters는 같은 검증용 폴더에 준비합니다. 게임 원본을 그 폴더로 이동하지 마세요.

```powershell
python -B tools/verify_xedit_dump_fields.py --manifest input-report.json --edits edits.json --original-dump original.dump.txt --edited-dump edited.dump.txt --report field-check.json --evidence field-check.jsonl
```

보고서의 필드 식별자별 원문·기대 번역을 xEdit의 레코드/서브레코드 출력과 비교하고, EPFT=6 기술 문자열의 원본 보존을 별도로 검사합니다. CRLF/CR만 LF로 정규화하며 문자열 경계가 모호하면 실패합니다. xEdit 전체 dump의 여러 줄 문법은 일반 교환 포맷이 아니므로 이 도구는 요청한 필드의 대조용입니다. 번역 대상 선정의 완전성이나 비문자열 데이터의 전체 무결성을 단독으로 증명하지 않습니다. 구조 검사 `-check`만 통과한 이전 PERK 변수 오번역을 이 대조가 실패로 검출했습니다.

### 지역화 테이블 독립 대조

```powershell
python -B tools/verify_localized_tables.py --manifest input-report.json --edits edits.json --original-plugin original/Skyrim.esm --edited-plugin output/Skyrim.esm --original-strings original/Strings --edited-strings output/Strings --report new-table-check.json
```

UTF-8 `STRINGS`/`DLSTRINGS`/`ILSTRINGS`의 디렉터리·길이·종료 문자를 별도 Python 코드로 읽어 모든 ID와 요청한 원문/수정문을 대조합니다. 수정 대상이 아닌 항목의 바이트 보존과 플러그인 전체 SHA 동일성도 확인합니다. 보고서 경로는 새 파일이어야 합니다. 이 도구는 지역화 플러그인의 문자열 테이블 수정 검증용이며, inline 파일이나 다른 인코딩에는 적용하지 않습니다. 전체 표시문구 선정의 완전성은 별도의 reader 목록과 비교해야 합니다.

Skyrim.esm의 67,388개 문구 전체 편집에서 통과했고, 대상 밖의 빈 `STRINGS/26239`를 `NEGATIVE`로 바꾼 대조 파일은 `Unedited entry bytes changed`로 실패했습니다. 증거는 `artifacts/direct-plugin-20260929/skyrim-localized-tables-check.json`과 `skyrim-localized-tables-negative-entry-check.json`입니다.

## XML 왕복 검증

번역 API 호출 없이 xTranslator XML → 프로젝트 DB → XML 경로를 검증합니다.
원본 XML은 읽기만 하며, 검증 DB와 출력 XML은 별도 경로에 생성합니다.

```powershell
dotnet run --project tools/XTranslatorAi.Validate -c Release -- input.xml [validation.sqlite] [output.xml] [--existing-db]
```

- `input.xml`: 검증할 원본. 생략하면 현재 폴더의 `LegacyoftheDragonborn_english_korean.xml`을 사용합니다.
- `validation.sqlite`: 기본값은 임시 폴더의 고유 파일명입니다. 일반 모드에서 기존 DB를 지정하면 그 DB의 문자열을 다시 가져오므로 검증용 DB를 사용하세요.
- `output.xml`: 기본값은 입력과 같은 폴더의 `<입력명>.validate.out.xml`입니다. 기존 출력은 교체되며 `.bak` 파일을 남깁니다.
- `--existing-db`: 반드시 기존 DB 경로를 지정합니다. XML을 다시 가져오지 않고 저장된 번역을 내보내며, 의도적으로 편집한 `Dest`는 DB 값과 비교합니다. DB 열기 과정의 스키마 초기화는 수행되므로 별도 복사본을 사용하는 것이 좋습니다.

입력 XML, DB, 출력 XML 경로는 서로 달라야 합니다. 생성한 DB와 출력은 검토를 위해 남겨 둡니다.

## 검증 범위

입력은 importer와 별개의 XML 파서로 읽어 누락을 독립적으로 확인합니다.

- 입력·DB·출력의 행 수와 순서
- `String` 속성, `EDID`·`REC` 식별자와 속성, `Source`, 저장된 DB 필드
- `Dest` 속성과 출력 번역문: 일반 모드에서는 입력 번역도 비교하고, `--existing-db`에서는 저장된 DB 번역을 기준으로 비교
- `Dest`를 제외한 행의 추가 요소·구조, `Params`의 Addon/Source/Dest/Version
- UTF-8 출력 선언과 UTF-8 BOM 유무

XML 들여쓰기와 속성 순서는 무시합니다. 입력이 UTF-16이면 UTF-8로 내보내므로 바이트 단위 동일성 검사가 아닙니다.
번역 의미의 정확성이나 ESP/ESM/ESL 바이너리, 실제 게임 동작은 검증하지 않습니다.

종료 코드는 성공 `0`, 불일치 또는 검증 중 오류 `1`, 잘못된 옵션·필수 파일·경로 `2`입니다.
불일치는 최대 50건을 표시하고 나머지는 개수를 출력합니다.
