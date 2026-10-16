# 한글패치 베타 배포 준비 (2026-10-16)

## 게임 1.7.104 대응

- Steam 최신판은 1.7.104, 떼껄룩 모드팩은 1.6.1170이다.
  - `Skyrim.esm` 필드 67,390개는 두 판에서 문자열 번호와 영어가 모두 같다.
  - `Update.esm`은 1.7에 지역 이름 2개가 더 있다: LCTN Riften(`000D27`), Riften Mistveil Keep(`0096C6`).
- 한패의 Update 문자열 표에 이 두 번호가 없어, 1.7에서는 이름이 비어 보일 수 있었다.
  - "리프튼"과 "미스트베일 요새"를 추가했다. 미스트베일 요새는 Skyrim.esm의 같은 영어를 한패가 적는 표기를 따랐다.
  - 기존 1,130개는 그대로다.
- fomod의 게임 버전 조건은 "1.6.1130 이상"이라 1.7.104도 이미 권장으로 표시된다.

## 메뉴 글자 검수 (`Interface/translate_english.txt`)

- 1회차 검수 범위 밖이던 메뉴·화면 글자다. 1.7 영어 원본은 `Skyrim - Interface.bsa`(houseCARL로 꺼냄)에서 가져왔다.
- 영어 1.7은 861항목, 한패는 791항목이었다.
  - 빠진 70항목: 1.6의 `$CreationsEnableFailDependency` 1개와 1.7 새 항목 69개(마우스 조작, 스위치 이식판, 화질 설정, 도전과제 등).
  - 이 파일은 영어 파일을 통째로 대신하므로, 빠진 항목은 게임에서 `$Mouse_Jump` 같은 내부 이름으로 보인다.
- Claude 하위 에이전트 3개가 나눠 검수했다(`artifacts/patch-review-interface-20261016/work`, PROMPT.md·check.py).
  - 빠진 70개 번역
  - 영어 그대로이던 줄과 1.7에서 영어가 바뀐 줄
  - 오역: 영어와 다른 문장이 들어 있던 LoginPage_Header, 인식 중 → 초기화 중, 발자국 → 발소리, 보고 → 신고 등
  - 영어가 Creation인 줄의 "모드"는 "크리에이션"으로
- 묶음끼리 어긋난 용어는 한패 본문 기준으로 통일했다.
  - Shout 함성(본문 332곳, 포효 21곳)
  - Power 특수능력
  - Survival Mode 생존 모드
  - 지명 하핑가르, 히얄마치
- `$ TO PLACE`, `$ TO STEAL`, `$lvl`의 공백은 원래대로 두었다. 키 이름의 공백이 화면에서는 사라져, 번역자가 단추 그림과 띄우려고 넣은 것으로 본다.
- 결과: 기존 791개 중 104개 수정, 70개 추가, 모두 861항목. 형식(UTF-16, CRLF, 키[탭]값)은 그대로다.
- 모드팩 파일도 교체했다. 원래 파일은 `artifacts/patch-review-interface-20261016/translate_english.modpack-before.txt`에 있다.

## 배포 패키지

- **fomod**
  - info.xml에 버전 `26.10.16 베타`를 넣었다. 작성자는 갤러리와 툴리우스 번역기 검수, 설명에 바탕(25.3.14)과 베타를 적었다.
  - USSEP 단계에 "원본도 4.3.9c여야 함" 경고를 넣었다.
  - USSEP 파일 이름 대소문자를 실제 파일과 맞췄다.
  - USCCCP "설치 안 함"의 쓰이지 않는 조건을 지웠다.
- `읽어보기.txt`(UTF-8 BOM, CRLF): 지원 버전, 설치, 25.3.14와 달라진 점, 주의, 만든 곳.
- `artifacts/kopatch-26.10.16-beta/Skyrim-Korean-Patch-26.10.16-beta.zip`: 파일 250개, 39.9MB.
  - 압축을 풀어 fomod 경로 4개가 대소문자까지 있는지, 파일이 저장소와 같은지 확인했다.
  - USSEP esp는 gitignore라 로컬 파일에서 묶었다.

## 게임 1.5.97 대응 (베타 2)

- 사용자 승인으로 Steam 콘솔에서 1.5.97 데이터 묶음을 받았다. 명령은 사용자가 직접 넣었다.
  - `download_depot 489830 489831 7848722008564294070`(4.9GB)
  - `download_depot 489830 489832 8702665189575304780`(7.9GB, 기본 게임 esm 5개와 `Skyrim - Interface.bsa`)
  - 받은 곳은 `C:\Program Files (x86)\Steam\steamapps\content\app_489830\`이고, 설치된 게임은 바뀌지 않았다.
- 1.5.97과 1.7.104의 영어 문자열 표를 번호별로 비교했다.
  - 거의 같다. 영어 오타 수정 12개(Dragonborn 책 11, 레벨업 안내 1)는 지금 한국어가 두 판에 모두 맞는다.
  - **1.5.97에만 있는 번호 2개**를 추가했다.
    - Update `0x22` "Skyrim" → 스카이림
    - Dawnguard 대사 `0xE4` "Death to the Dawnguard!" → 던가드에게 죽음을!(1.7에서 옮겨 간 번호의 번역)
  - **1.6부터 "Creation Club Cell"(보이지 않는 CC 셀 이름)로 재활용된 번호 3개**를 1.5.97에서 보이는 이름으로 바꿨다.
    - Dawnguard `0x92` 퀘스트 Touching the Sky → 하늘에 닿아(1.7에서 옮겨 간 0x28E2와 같은 번역)
    - Dawnguard `0x9F` 퀘스트 Dawnguard Vampire Hunters → 던가드 흡혈귀 사냥꾼
    - Dragonborn `0xDF` 마법 효과 `DLC2ExpSpiderAlbinoScrollFF` Exploding Spider → 폭발형 거미
      - 이 효과는 1.7에서도 이 번호를 가리켜 이름이 "Creation Club Cell"로 보였다. 1.7의 원래 버그도 함께 고친 셈이다.
- 결과:
  - 1.5.97·1.7 모두 영어가 있는 번호 중 한패에 없는 것 0개
  - "Creation Club Cell"이 잘못 보이는 곳 0개
  - 1.5.97 메뉴 항목 388개 모두 한패 메뉴 파일에 있다. 1.5.97 영어에는 " TO PLACE"처럼 앞 공백이 실제로 있어, 앞 공백을 지킨 판단이 맞았다.
  - 한패 글꼴 설정이 1.5.97 메뉴가 쓰는 글꼴 이름 16개를 모두 연결한다.
- **게임 번역 사전**
  - 1.7 영어 기준으로 만들기 때문에, "Creation Club Cell" 글자가 0xDF의 "폭발형 거미"로 글자 기준 번역이 되어 버렸다.
  - 이 자리표시 이름은 글자 기준 번역에서 빼고 레코드 기준으로만 찾게 했다(`GameStringDictionaryTests.CreationClubCellPlaceholder_IsMatchedByRecordOnly`).
  - 검수 도구는 솔루션에 없어서 따로 빌드해야 사전에 반영된다.
- fomod 권장 버전에 1.5.97을 넣었다. info.xml과 읽어보기는 `26.10.16 베타 2`로 올렸다.
