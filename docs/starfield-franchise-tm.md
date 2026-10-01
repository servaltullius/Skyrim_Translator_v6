# Starfield Franchise TM Workflow

2026-09-29 정리 후 기존 로컬 TM 보존본의 기준 위치는 루트 `tm/`입니다. 아래 `artifacts/tm/`는 새로 생성할 임시 출력 위치이며, 기존 보존본과 동일했던 중복 파일은 제거했습니다. 생성 결과는 검토한 뒤 필요한 보존본만 유지하세요.

## 목적
- 로컬에 보유한 Starfield 영문 `.strings`와 한국어 패치 `.strings`를 비교해 `Starfield` 전용 TM TSV를 생성한다.
- 생성된 TSV는 앱의 `시리즈 TM 가져오기`로만 적재한다.

## 데이터 정책
- 제3자 번역 데이터는 저장소, 릴리즈, 임베디드 리소스에 포함하지 않는다.
- Starfield TM은 TES/Fallout TM으로 승격하지 않는다.
- 생성된 TSV는 검토 가능한 상태로 유지한다.

## 준비물
- Starfield 영문 `Data/Strings` 디렉터리
- 로컬에 보유한 한국어 패치 `Data/Strings` 디렉터리
- Python 3

## 생성 명령
```bash
python3 scripts/seed_tm_from_bethesda_strings_dirs.py \
  --source-root "/path/to/Starfield-English-Strings" \
  --target-root "/path/to/Starfield-KoreanPatch-Strings" \
  --source-locale en \
  --out artifacts/tm/starfield-franchise-tm.tsv
```

## 실행 예시
- 로컬 한국어 패치는 현재 `/mnt/g/스타필드 모드/Starfield_KoreanPatch_0619.7z` 아카이브 상태이므로 먼저 압축을 풀어야 한다.
- 이 세션에서는 `/mnt/g` 아래에서 공식 Starfield 영문 `Data/Strings` 루트를 찾지 못했다. 따라서 엔드투엔드 실행 검증은 사용자가 해당 소스 경로를 제공해야만 가능하다.
- 아래 명령은 실제로 TSV/JSON 출력 파일을 생성한다.

```bash
python3 scripts/seed_tm_from_bethesda_strings_dirs.py \
  --source-root "/path/to/official/Starfield/Data/Strings" \
  --target-root "/path/to/extracted/Starfield_KoreanPatch/Data/Strings" \
  --source-locale en \
  --out artifacts/tm/starfield-franchise-tm.tsv \
  --report-json artifacts/tm/starfield-franchise-tm.report.json
```

성공적으로 실행되면 다음 파일이 생성된다.
- `artifacts/tm/starfield-franchise-tm.tsv`
- `artifacts/tm/starfield-franchise-tm.report.json`

## 검토 체크리스트
- TSV 첫 줄이 `Source<TAB>Target` 인지 확인한다.
- 고유명사, UI, 시스템 메시지가 의도대로 들어갔는지 샘플 확인한다.
- 장문이 많고 문맥 의존성이 높은 항목이 너무 많으면 `--include-long` 없이 다시 생성한다.

## 앱 import
- 가져오기 TSV는 `Source<TAB>Target` 형식이어야 한다.
- 앱의 `게임 시리즈` 선택기를 `Starfield`로 설정한 뒤 고급 설정의 `시리즈 TM 가져오기`에서 `artifacts/tm/starfield-franchise-tm.tsv`를 선택한다.
