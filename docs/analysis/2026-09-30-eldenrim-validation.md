# 엘든림 ESP 및 실행 화면 검증

2026-09-30. 사용자는 LOTD 대신 현재 모드팩의 엘든림을 선택했으며 실제 게임 실행·화면 확인은 직접 담당한다고 명시했다. 게임 검증은 번역기의 파일 읽기·저장 검증과 구분한다.

## 실행 파일과 설정

`%LOCALAPPDATA%/TulliusTranslator/settings.json`에서 `apiKey`, `apiKeyProtected`, `apiKeys` 항목을 제거했다. 키를 복호화하거나 값으로 출력하지 않았으며, 키가 들어 있는 설정 백업을 만들거나 복원하지 않았다. 다른 설정은 유지했다. 삭제 후 키 항목 0개를 확인했고, 루트 EXE 교체와 실행은 이번에는 명령 거절 없이 완료했다. 따라서 이전 거절 원인을 API 키로 확정할 수는 없다.

기존 루트 EXE는 `artifacts/direct-plugin-20260929/deployment-20260930-072058/TulliusTranslator.previous.exe.disabled`로 보존했다. preview4 실제 화면 검사에서 수동 편집 후 완료·대기 숫자가 재열기 전까지 갱신되지 않는 오류를 발견했다. preview5는 DB 저장 성공 후 행 상태와 진행 숫자를 갱신한다. 반복 저장의 중복 집계와 DB 저장 실패의 완료 표시를 검증하는 회귀 2개를 추가했고 전체 **799/799**를 통과했다. 빌드 경고·오류 0개. 플러그인 reader/writer 자체의 변경은 없다.

## 실제 입력과 결과

MO2 설정의 선택 프로필은 `TKL - MUNG ADDON`이다. 활성 모드 목록 및 플러그인 목록에 아래 두 모드가 있으며, 같은 파일명의 다른 loose ESP는 조사 범위의 모드 폴더에서 발견되지 않았다. 설치 파일·프로필·세이브에는 쓰지 않았다.

| 입력 | 표시문구 | 읽기 진단 | 무편집 SHA 보존 |
| --- | ---: | ---: | --- |
| Elden Rim Base 3.7.2 / EldenSkyrim.esp | 985 | 0 | 일치 |
| Elden Rim Weapon Art 3.2.2 / EldenSkyrim_RimSkills.esp | 2,852 | 0 | 일치 |

본체 SHA256: `5953C4B4D3A43CEDBBF8BCDE3CE72C7581483CC2C13A977977C11A69888F7F40`.
전회 SHA256: `8CD7D536BC409DF22209E3ECE76B47AD43D6558BCF5999545B8C1F59C213AFC4`.

실제 WPF 창에서 `Open ESP`로 전회 ESP를 열었고 한글·필드 위치·2,852개 문자열을 확인했다. `Save ESP`의 별도 새 폴더 저장은 변경 0개로 완료됐으며 원본 SHA와 같았다. 출력 사본을 별도 프로젝트로 열어 `MGEF/04000801/0/FULL/0`의 문구를 `질주 단축키 - 활성화됨 [ESP 저장 검증]`으로 수동 편집했다. `Save Dest` 후 `Save ESP`는 변경 1개로 완료됐다. 원문 사본을 다시 열어 수동 편집이 복원되고 완료 1개·대기 2,851개가 표시됨을 확인했다. 시험 문구는 게임에 배포하지 않았다.

시험 출력 SHA256: `8860B63783044884CE96472FCB865440A303483C51D0DD97E699EF347469AF64`.

루트에 적용한 preview5 창에서 이 시험 출력 ESP를 직접 다시 읽었다. 별도 출력 경로 프로젝트의 수동 편집을 저장한 직후, 재열기 없이 완료 0→1개·대기 2,852→2,851개로 바뀌는 것을 실제 화면에서도 확인했다. 이 추가 편집은 시험 출력의 프로젝트 DB에만 저장했으며 기존 시험 ESP와 게임 파일은 변경하지 않았다.

## 독립 검증

xEdit 4.1.5f가 원본과 실제 UI 저장본을 각각 읽어 전체 dump를 생성했다. `verify_xedit_dump_fields.py`로 원문·기대 수정문을 대조한 결과 양쪽 **2,852/2,852** 일치, 누락·불일치·경계 해석 오류 0, 레코드/서브레코드 구조 동일이었다. 이 검사는 앱이 노출한 필드의 정확한 문자열 대조이며 모든 비문자열 바이트나 모든 표시문구의 선정 완전성을 독립적으로 보증하지 않는다.

Mutagen 0.53.1은 본체 1,279개·전회 3,230개 major record의 끝까지 순회하고 입력 해시를 다시 확인했다. 비어 있지 않은 translated getter는 본체 985개·전회 2,852개로 앱 노출 필드의 `(레코드 서명, 파일 내부 FormID, 문구)` 다중집합과 일치했다. 전회 수정본도 기대 문구 2,852개와 일치했다. 전회의 plain 문자열 13,720개는 원문과 같았다.

단, 원본 본체 PERK 1건 및 원본/수정 전회 PERK 3건의 Effects 열거에서 `PerkEntryPointModifyActorValue did not have expected parameter type flag: Float` 진단이 발생했다. 전회 원본/수정본의 진단은 동일하며 원본에 이미 존재한다. 이 부분의 독립 reader 해석 실패를 성공으로 바꾸거나 번역기 오류로 확정하지 않는다. raw 보고서의 `TraversalCompleted`는 false이며, 별도 문구 대조는 범위를 명시한 비교다.

## 증거와 사용자 확인

증거는 `artifacts/direct-plugin-20260929`에 있다: `elden-base-read.json`, `elden-base-unchanged-export.json`, `elden-skills-read.json`, `elden-ui-original`, `elden-ui-edited`, `elden-ui-edited-read.json`, `elden-skills-xedit-fields.json` 및 `.jsonl`, 원문/수정 dump, Mutagen raw 보고서 3개와 `elden-independent-comparison.json`, `tests-preview5-final/results.trx`.

유료 API 호출 없이 기존 한글 문구의 수동 편집으로 입출력을 검증했다. 이번 검증은 새 모델의 번역 품질 평가가 아니다. LOTD 자체, 모든 ESP, 원본의 미해석 PERK 효과, 게임 내 글꼴·최종 덮어쓰기·기능 동작까지 무오류로 보증하지 않는다. 특히 이전 Skyrim.esm 지역화 검증에서 발견한 SNDR 공유 ID의 간접 변경 영향은 계속 미확인이다. 게임 확인을 사용자가 맡았으므로 번역기 측 검증 작업을 위해 게임을 실행할 필요는 없다.
