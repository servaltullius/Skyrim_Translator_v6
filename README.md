# Tullius Translator

Tullius Translator는 **Bethesda/xTranslator XML 현지화 작업**에 특화된 Windows WPF 번역 앱입니다.  
핵심 목적은 단순 기계번역이 아니라, 모드 번역에서 자주 깨지는 **태그/플레이스홀더 보존**, **용어 통일**, **품질 검수**까지 한 번에 처리하는 것입니다.

이 레포는 다음 구성으로 이루어져 있습니다.
- **Windows WPF 앱(권장)**: `src/XTranslatorAi.App`
- **(선택) 보조 도구**
  - Validate CLI: `tools/XTranslatorAi.Validate` (XML ↔ DB 라운드트립 검증)
  - Python CLI: `translate_xtranslator_xml_gemini.py` (간단 자동화/실험용)

## 앱 소개

이 앱은 xTranslator에서 Export한 XML(`SSTXMLRessources`)을 입력으로 받아, 각 `<String>`의 `<Source>`를 Gemini로 번역하고 `<Dest>`를 채운 뒤 다시 XML로 내보냅니다.  
즉, ESP/ESM을 직접 파싱하는 툴이 아니라 **xTranslator XML 기반 번역 파이프라인**에 집중한 앱입니다.

실사용에서 중요한 지점은 다음입니다.
- 대량 문자열 번역 중에도 행 단위 진행 상태를 실시간으로 확인
- 번역 실패/오류 항목을 상태와 메시지로 추적
- 번역 후 수동 편집/저장과 후처리까지 UI 안에서 처리

## 핵심 기능

### 1) 번역 정확도/안정성
- 태그/플레이스홀더 보호: `<mag>`, `<dur>`, `<Alias=...>`, `%0f`, 줄바꿈 등 보존
- 자동 복구(Repair Pass): 토큰/형식 검증 실패 및 문맥 이상 항목 재요청 복구
- 템플릿 교정: 규칙 기반 보정으로 반복 오역 패턴 수정
- 품질 재번역(선별): 품질 규칙에 걸린 항목만 2차 모델로 재번역
- 위험 문장 다중 후보 재랭킹: 충돌 위험 구문에서 다중 후보 생성 후 최종 선택

### 2) 용어 통일
- Project Glossary / Global Glossary 분리 운영
- 매치 방식/강제 방식/우선순위 기반 용어 적용
- 문자열 편집 화면에서 즉시 용어 검색(프로젝트/글로벌 범위 선택)
- 세션 용어 메모리(번역 중 자동 학습)로 일관성 강화

### 3) 번역 자산 재사용
- Global TM(Translation Memory) 조회/가져오기
- 번역 결과 편집 시 메모리 축적 가능
- 반복 문장/유사 문장 작업 시 재사용성 향상

### 4) 검수/비교/운영
- Compare 탭: 여러 모델/설정을 나란히 실행해 결과 비교
- LQA 탭: 검수 스캔 및 이슈 필터링
- Project Context 탭: 프로젝트 문맥 관리
- API Logs 탭: 요청/토큰/오류 흐름 확인
- API Key 저장 및 자동 페일오버(오류 시 다른 키로 전환)

## 주요 화면 구성

- `Strings`: 검색/필터, 소스-타깃 편집, 상태 모니터링
- `Compare`: 다중 모델 결과 비교
- `LQA (Review)`: 번역 품질 검수
- `Project Glossary (This Addon)`: 프로젝트별 용어집
- `Global Glossary (All projects)`: 공용 용어집
- `Global TM`: 공용 번역 메모리
- `Prompt`: 기본/커스텀 프롬프트 및 린트 상태
- `Project Context`: 작품/모드 문맥 정보 관리
- `API Logs`: 호출 로그/토큰 사용량 확인

## 실행(개발용)
- 솔루션: `XTranslatorAi.sln`
- 앱 프로젝트: `src/XTranslatorAi.App`

Windows에서:
```bash
dotnet build XTranslatorAi.sln -c Release
dotnet run --project src/XTranslatorAi.App -c Release
```

앱에서:
1) `Open XML`로 xTranslator XML 열기  
2) `API Key` 입력 + 모델 선택(기본 `gemini-2.5-flash-lite`, 필요 시 `Refresh`)  
3) `Start`로 번역 시작  
4) `Export XML`로 저장  

## GitHub 릴리즈 자동 EXE 첨부

- 워크플로: `.github/workflows/release-win-x64-singlefile.yml`
- 트리거:
  - GitHub Release를 `Published`하면 자동 실행
  - `workflow_dispatch`로 특정 태그를 수동 재업로드 가능
- 업로드 자산:
  - `TulliusTranslator.exe` (win-x64 단일 파일)
  - `TulliusTranslator.exe.sha256`

수동 재실행 예시(기존 태그 자산 덮어쓰기):
```bash
gh workflow run release-win-x64-singlefile.yml -f tag=v0.1.0
```

## Python CLI

앱만 사용할 예정이면 이 섹션은 건너뛰어도 됩니다.

### 준비물

- Python 3
- Gemini API Key (Google AI Studio)

### 사용법

1) API 키 설정

```bash
export GEMINI_API_KEY="YOUR_KEY_HERE"
```

2) 번역 실행

```bash
python3 translate_xtranslator_xml_gemini.py \
  --input LegacyoftheDragonborn_english_korean.xml \
  --output LegacyoftheDragonborn_english_korean.translated.xml
```

### 동작 방식(요약)

- 기본값으로, `<Dest>`가 비어있거나 `<Source>`와 같은 경우에만 번역합니다. (이미 번역된 항목은 건너뜀)
- `<mag>`, `<Alias=...>`, `<font ...>` 같은 태그/플레이스홀더는 `__XT_PH_0000__` 같은 토큰으로 마스킹 후 번역하고 원복해서, 원문 토큰이 깨지지 않게 합니다.
- 진행 중단/재시작을 위해 `*.gemini_cache.jsonl` 캐시를 자동으로 사용합니다.

### 유용한 옵션

- `--limit 50` : 테스트로 50개만 번역
- `--overwrite` : 기존 `<Dest>`가 있어도 덮어쓰기
- `--batch-size 10` / `--max-chars 8000` : 한 번에 보내는 크기 조절
- `--cache path.jsonl` : 캐시 파일 위치 지정
