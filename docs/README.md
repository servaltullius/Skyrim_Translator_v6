# 문서 안내

현재 기능과 실행 절차는 [루트 README](../README.md)를 기준으로 합니다.

- [파이프라인·설정 개선과 후보 빌드 검증](analysis/2026-09-29-pipeline-defaults-implementation.md)
- [3.8 Flash 번역 프롬프트 개선과 실제 표본 비교](analysis/2026-09-30-prompt-improvement.md)
- [EDID 문맥·자동 용어 기억 개선과 실제 표본 비교](analysis/2026-09-30-edid-session-memory.md)
- [Skyrim SE/AE 플러그인 직접 번역 사용법과 검증 범위](direct-plugin-support.md)
- [직접 지원 자동 테스트·실제 파일·xEdit 검증 기록](analysis/2026-09-29-direct-plugin-validation.md)
- [플러그인 번역 필드 전수 대조](analysis/2026-09-29-plugin-field-registry.md)
- [일반 API 최적화 구현과 검증 결과](analysis/2026-09-29-standard-api-implementation.md)
- [현재 유지보수 작업과 검증 상태](plans/2026-09-29-maintenance.md)
- [이전 개선 작업과 검증 상태](plans/2026-09-28-translator-improvement.md)
- [LOTD 무료 번역 모델 비교](../benchmarks/translation/lotd-v1/README.md)
- [번역 품질·비용 최적화 조사와 검증 설계](analysis/2026-09-29-quality-cost-optimization.md)
- [현재 코드 검토 기준](review-checklist.md)
- [보관한 루트 문서·CLI·VibeKit](archive/2026-09-29/README.md)
- [직접 ESP·ESM·ESL 지원 검토](direct-plugin-feasibility.md)
- [Starfield 프랜차이즈 TM 준비](starfield-franchise-tm.md)
- [XML 왕복 검증 CLI](../tools/XTranslatorAi.Validate/README.md)

`plans/`의 2026년 1~2월 문서와 `superpowers/plans/`의 4월 문서는 과거 설계 기록입니다. 당시 모델명·가격·미구현 기능·삭제된 VibeKit 명령을 현재 사용법으로 따르지 마세요. 경로를 유지하여 기존 링크와 작업 이력을 보존했습니다. 현재 상태를 판단할 때 코드, 테스트, 위 문서의 검증 날짜를 확인하세요.

2026-09-30에 `artifacts/`의 5MB 초과 산출물(대체된 후보 EXE, 복사한 게임 ESM, xEdit 덤프, Mutagen 원본 출력, 왕복 검증 DB)을 정리했습니다. 분석 문서가 이 파일들을 증거로 언급하더라도 로컬에 남아 있지 않을 수 있습니다. 옮긴 파일의 크기와 SHA-256은 로컬 `artifacts/CLEANUP-20260930.md`에 있습니다. 요약 JSON·보고서·테스트 결과와 롤백 EXE는 유지했습니다.
