# Skyrim SE 플러그인 번역 필드 대조

검토일: 2026-09-29. 대상: `src/XTranslatorAi.Core/Plugins/PluginFieldRegistry.cs`.

이 문서는 레코드 서명과 번역 대상 필드 분류의 정적 대조 결과다. 바이너리 parser/writer, 실제 모드의 정상 저장, xEdit 재열기, 게임 동작 검증을 대신하지 않는다. 외부 개발 도구 구현을 근거로 했으며 Bethesda의 완전한 공식 파일 명세로 표현하지 않는다.

## 고정한 근거

| 프로젝트 | 조사한 버전/커밋 | 사용한 자료 |
| --- | --- | --- |
| xEdit | `xedit-4.1.5f`, `f5c00f3fa3ee39511185515802647246c807f759` | [wbDefinitionsTES5.pas](https://github.com/TES5Edit/TES5Edit/blob/f5c00f3fa3ee39511185515802647246c807f759/Core/wbDefinitionsTES5.pas) |
| xEdit | `9058a79437367c2cb1b757a9021ab443d743012d` | 최초 registry 작성 때 조사한 TES5 정의. 4.1.5f 태그로 별도 재대조했다. |
| Mutagen | `5d69a2d99d1ae1917e23db67f3e323b306168538` | [Skyrim Records의 XML schema 149개](https://github.com/Mutagen-Modding/Mutagen/tree/5d69a2d99d1ae1917e23db67f3e323b306168538/Mutagen.Bethesda.Skyrim/Records), [PERK 조건부 parser](https://github.com/Mutagen-Modding/Mutagen/blob/5d69a2d99d1ae1917e23db67f3e323b306168538/Mutagen.Bethesda.Skyrim/Records/Major%20Records/Perk.cs) |
| xTranslator | `9aa38d60860273401f8bb0dd1557c82a295c41b3` | [SkyrimSE 문자열 테이블 매핑](https://github.com/MGuffin/xTranslator/blob/9aa38d60860273401f8bb0dd1557c82a295c41b3/Data/SkyrimSE/_recorddefs.txt), [조건부 필드 판정](https://github.com/MGuffin/xTranslator/blob/9aa38d60860273401f8bb0dd1557c82a295c41b3/TESVT_espDefinition.pas) |

자료의 필드 의미와 형식 사실을 대조했으며 외부 parser/writer 구현을 복제하지 않았다.

## 대조 방법과 수치

1. xEdit 4.1.5f TES5 정의의 `wbRecord`, `wbRefRecord`, `ReferenceRecord`에 명시된 레코드 서명을 중복 제거했다. 매개변수로 받는 helper의 정의 자체는 서명으로 세지 않았다.
2. 추출한 서명 집합과 registry `KnownRecords` 집합을 양방향 비교했다. 양쪽 모두 **134개**, 차집합 **0개**다. 여기에는 미사용/레거시 레코드도 포함된다.
3. xEdit의 공통 `wbFULL`/`wbDESC` 사용처 및 `wbLString`/`cpTranslate` 선언을 확인했다. 공유된 중첩 정의는 실제 소유 레코드까지 추적했다.
4. Mutagen Skyrim Records 아래 XML schema **149개 전부**를 읽고 `translated`가 있는 String 선언을 추출했다. 같은 서브레코드명이 다른 레코드/중첩 문맥에서 쓰이는 경우를 별도로 대조했다.

| registry 범주 | 레코드·필드 조합 수 | 판정 |
| --- | ---: | --- |
| `FULL` | 46 | STRINGS. REFR의 중첩 MapMarker 이름 포함 |
| `DESC` | 16 | 15개는 DLSTRINGS, LSCR만 STRINGS |
| 기타 명시 필드 | 18 | 아래 테이블 및 문맥 조건 적용 |
| 합계 | **80** | PERK EPFD는 EPFT=7일 때만 포함. EPFT=6 식별자는 제외 |

**134는 인식하는 레코드 서명 수다. 134종 전체의 모든 문자열을 번역한다는 뜻이 아니다.** `IsKnownRecord`는 전체 필드 구조의 유효성 또는 모든 향후 확장 필드의 지원을 보장하지 않는다. 최초 정적 대조에서는 EPFT=6의 string 저장 형태를 표시문구로 잘못 분류했다. 이후 실제 USSEP에서 `PERK/00106253/0/EPFD/0`의 `bPerkShieldCharge`가 검출되어 아래 근거로 제외 규칙을 수정했다. 문자열 형식 및 `cpTranslate` 속성만으로 표시문구를 판정할 수 없다.

## 특수 필드와 저장 형태

| 레코드:필드 | localized일 때 형태 | 조건/의미 |
| --- | --- | --- |
| MGEF:DNAM | STRINGS | 마법 효과 설명 |
| INFO:NAM1 | ILSTRINGS | 대사 응답 |
| INFO:RNAM | STRINGS | 대화 선택/응답 prompt |
| NPC_:SHRT | STRINGS | 짧은 이름 |
| QUST:CNAM | DLSTRINGS | 단계 일지 |
| QUST:NNAM | STRINGS | QOBJ로 시작하는 objective 영역만 |
| BOOK:CNAM | DLSTRINGS | 책 설명 |
| WOOP:TNAM | STRINGS | 용언 해석 |
| MESG:ITXT | STRINGS | 버튼 문구 |
| REGN:RDMP | STRINGS | 지도 이름 |
| ACTI:RNAM, FLOR:RNAM | STRINGS | 활성화 문구 |
| BPTD:BPTN | STRINGS | 신체 부위 이름 |
| FACT:MNAM, FACT:FNAM | STRINGS | 남성/여성 계급명 |
| GMST:DATA | STRINGS | EDID 첫 글자가 소문자 `s`인 경우만 |
| PERK:EPF2 | STRINGS | entry point 효과의 EPFT=4 |
| PERK:EPFD | STRINGS | entry point 효과의 EPFT=7만. EPFT=6은 엔진 식별자이므로 제외 |

PERK의 나머지 EPFD 변형은 엔진 식별자, float, 값 쌍 또는 FormID 등을 담으므로 번역하지 않는다. PRKE(type=2)와 PRKF 사이에서 EPFT를 판정하며, 이전 효과의 EPFT가 다음 효과에 이어지지 않는다.

EPFT=6은 Mutagen의 `ParameterType.String`, 함수 11은 `SelectText`다. [Perk.cs](https://github.com/Mutagen-Modding/Mutagen/blob/5d69a2d99d1ae1917e23db67f3e323b306168538/Mutagen.Bethesda.Skyrim/Records/Major%20Records/Perk.cs)는 이 분기를 `Encodings.NonTranslated`로 읽고 쓴다. [Perk.xml](https://github.com/Mutagen-Modding/Mutagen/blob/5d69a2d99d1ae1917e23db67f3e323b306168538/Mutagen.Bethesda.Skyrim/Records/Major%20Records/Perk.xml)에서도 `PerkEntryPointSelectText.Text`는 일반 String이며, 함수 15/EPFT=7의 `PerkEntryPointSetText.Text`에만 `translated="Normal"`을 지정한다. [APerkEntryPointEffect.cs](https://github.com/Mutagen-Modding/Mutagen/blob/5d69a2d99d1ae1917e23db67f3e323b306168538/Mutagen.Bethesda.Skyrim/Records/Major%20Records/APerkEntryPointEffect.cs)는 EntryType 54를 `SetBooleanGraphVariable`로 정의한다. `bPerkShieldCharge` 같은 값을 번역하면 표시 언어가 아니라 기능 변수의 이름을 바꾼다. 게임 실행으로 효과를 재현한 검증은 아니며, 실제 입력 데이터와 공개 도구의 형식·의미 정의로 확인한 제외다.

## 의도적 제외와 정의 불일치

| 대상 | 제외 이유/남은 불확실성 |
| --- | --- |
| HDPT:NAM1 | xEdit에 cpTranslate가 붙어 있지만 Race Morph/Tri/Chargen Morph의 **자산 파일명**이다. 문구 번역 시 자산 참조가 깨지므로 제외한다. |
| PERK:EPFD, EPFT=6 | xEdit의 cpTranslate와 달리 Mutagen은 non-translated String으로 취급한다. Set Boolean Graph Variable의 `bPerkShieldCharge` 등 엔진 변수 식별자가 들어가는 분기이며, 표시 문자열인 EPFT=7과 분리하여 원본 바이트를 보존한다. |
| QUST:FLTR | CK Object Window의 개발자 필터다. 게임 표시문구로 취급하지 않는다. |
| TES4:CNAM/SNAM | 작성자/플러그인 설명 메타데이터다. 작성자 이름을 게임 문구와 함께 자동 번역하지 않는다. |
| EDID, MAST, 모델·텍스처 경로, 스크립트 식별자 | 기술 식별자 또는 자산 참조다. 표시 문자열과 분리하며 원본 바이트를 보존한다. |
| QUST의 후미 NNAM | xEdit 4.1.5f는 ANAM/aliases 뒤의 NNAM을 일반 개발자 Description으로 정의하지만, Mutagen Quest schema는 DL translated로 정의한다. objective NNAM으로 추측하지 않고 제외한다. |
| SNDR:FNAM | 폼 버전 35 미만의 비트 플래그다(비트 4 = Loop). 최신 xEdit 정의와 Skyrim.esm 데이터로 확인했다. 표시문구가 아니므로 제외하고 바이트를 유지한다. [SNDR:FNAM 확인](2026-09-30-sndr-fnam.md) |
| VMAD 안의 문자열, 별도 PEX/MCM 등 | 이 registry는 subrecord 단위의 검증된 표시문구만 분류한다. 중첩 스크립트 데이터와 별도 자산의 번역을 포함한다고 주장하지 않는다. |

QUST 후미 NNAM 같은 모호한 필드는 사용자에게 직접 번역하지 않는 사실과 필드 바이트 보존 범위를 알려야 한다. 지역화 파일에서 같은 문자열 ID를 공유하면 참조 내용까지 불변이라는 뜻은 아니다. 단순히 알려진 레코드 안에 있다는 이유로 전체 번역 완료로 표시해서는 안 된다. `FULL`/`DESC`도 모든 레코드에 적용하는 wildcard로 확장하지 않는다.

Skyrim.esm 전체 문구 편집 검증에서 Mutagen으로 해석한 SNDR 값 54개가 함께 바뀐 것으로 보였다. 후속 [SNDR:FNAM 확인](2026-09-30-sndr-fnam.md)에서 이 필드가 플래그임을 확인해 영향이 없다고 결론지었다. 앱의 SNDR:FNAM 경고도 제거했다. 상세 증거는 [지역화 검증 기록](2026-09-29-direct-plugin-validation.md)에 있다.

## 추가한 회귀 테스트

`tests/XTranslatorAi.Tests/PluginFieldRegistryTests.cs`에 다음 의미 경계를 검증하는 테스트를 추가했다.

- PERK EPFT=4/7의 표시문구 테이블, EPFT=6의 graph-variable 식별자 및 숫자·FormID 변형 제외.
- PRKF 종료, 미완료 새 PRKE, 잘못된 PRKE/EPFT 크기에서 앞선 문자열 타입을 재사용하지 않기.
- QUST QOBJ objective 영역과 ANAM/alias/stage 경계 구분.
- GMST의 문자열 EDID 접두사와 숫자/불명 설정 구분.
- LSCR DESC와 BOOK DESC, INFO NAM1 등 서로 다른 테이블 종류 구분.
- HDPT 자산 경로, TES4 메타데이터, 동명 숫자 필드와 unknown record의 FULL/DESC 제외.

이 검토 작업에서는 **테스트 실행·빌드를 하지 않았다.** 실행 결과는 통합 담당자의 실제 명령 출력으로 별도 확인해야 한다. 최초 정적 대조 뒤 실제 USSEP 데이터를 근거로 registry의 EPFT=6 노출을 제거하고 회귀 기대값을 정정했다.
