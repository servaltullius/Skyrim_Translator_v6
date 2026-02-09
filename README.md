# Tullius Translator

이 레포는 **Windows WPF 앱** 사용을 기준으로 구성되어 있습니다. (대부분의 사용자는 앱만 쓰면 됩니다)

- **Windows WPF 앱(권장)**: `src/XTranslatorAi.App`
- **(선택) 보조 도구**
  - Validate CLI: `tools/XTranslatorAi.Validate` (XML ↔ DB 라운드트립 검증용)
  - Python CLI: `translate_xtranslator_xml_gemini.py` (간단 자동화/실험용)

xTranslator에서 추출(Export)한 XML(`SSTXMLRessources`)을 읽어서, 각 `<String>`의 `<Source>`를 Gemini로 번역해 `<Dest>`에 채운 뒤 다시 XML로 저장하는 **번역 전용 프로그램**입니다.

## vibe-kit (개발/에이전트 도우미)

- 한 방 진단(권장): `python3 scripts/vibe.py doctor --full` (요약: `.vibe/context/LATEST_CONTEXT.md`, Windows: `scripts\\vibe.cmd doctor --full`)
- 검색: `python3 scripts/vibe.py search <query>`
- 영향도: `python3 scripts/vibe.py impact <path>`
- XML 플레이스홀더 QA: `python3 scripts/vibe.py qa <file.xml>`
- LLM 컨텍스트 팩: `python3 scripts/vibe.py pack --scope staged` (git 없으면 `--scope recent`)
- 자세한 사용법: `.vibe/README.md`

## Windows 앱 (WPF)

### 기능
- xTranslator XML을 열어 `<Source>/<Dest>`를 테이블로 표시
- Gemini로 번역 진행 시 **행이 실시간으로 갱신**
- 용어집(Glossary) **항상 적용(강제)**
- 기본 프롬프트는 `메타프롬프트.md` 기반(앱에 내장), 커스텀 프롬프트는 선택
- 태그/플레이스홀더 보호(`<mag>`, `<Alias=...>`, `%0f`, 줄바꿈 등)

### 실행(개발용)
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
