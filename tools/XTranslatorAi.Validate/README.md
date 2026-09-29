# XML 왕복 검증 CLI

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
