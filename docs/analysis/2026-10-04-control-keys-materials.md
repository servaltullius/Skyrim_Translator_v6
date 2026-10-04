# 조작 키와 재질명 (1.10-preview1)

작성일: 2026-10-04
대상: 1.9(6ea8a4d) 뒤의 수정. [1.9 릴리스 전 평가](2026-10-04-release-1.9-eval.md)의 "남은 차이" 두 가지를 고쳤다.

## 조작 키

스카이림 도움말(MESG)의 `[Sprint]`, `[Ready Weapon]`, `[Left Attack/Block]` 같은 대괄호 이름은 게임이 플레이어가 지정한 키로 바꿔 보여 준다. 공식 한국어판도 영어로 둔다("이동중에 [Sprint] 키를 누르면 질주 합니다"). 번역하면("[달리기]") 바뀌지 않고 그대로 보인다. 1.8은 평가 표본의 조작 키 2행을 모두 번역했고, 1.9는 1행을 번역했다.

- 보호 토큰으로 바꾼다. 스카이림 컨트롤 맵과 Skyrim.esm 도움말에 나오는 이름 64개를 대소문자까지 같을 때만 `__XT_PH_KEY_####__`로 가린다. 바닐라 Skyrim.esm에서는 원문 88개의 189곳이 가려진다.
- 엘든림의 `[Hand Strap]`, `[Weapon Switch]`처럼 모드가 대괄호로 쓴 스킬명은 목록에 없으므로 그대로 번역한다(참조 번역도 "[핸드 스트랩]"). 소문자 `[sprint]`도 가리지 않는다.
- 글 전체가 `[Back]` 하나뿐인 행은 버튼이나 선택지 이름으로 보고 가리지 않는다.
- 이 토큰이 있으면 "토큰 뒤에 '키'를 붙이고 조사를 단다"는 규칙과 예시 두 개를 프롬프트에 넣는다.
- 같은 보호 규칙을 품질 검사도 쓴다. 조작 키를 번역한 기존 행은 이제 "보호 요소 불일치"로 표시되고, 그런 번역을 담은 TM 항목은 적용되지 않는다.

## 재질명

공식 TM에 아이템 이름 전체가 있으면 1.9의 공식 이름 고정이 이미 처리한다(바닐라 표본의 Steel·Ebony 행은 1.9에서 모두 맞음). 남은 문제는 모드가 만든 이름이었다. 엘든림의 "Pure Ebony"가 흑단·흑연마석으로, "Ebony Beam"이 흑연마석 광선으로, "Dragonscale War Dance"가 용비늘 무답으로 나왔다.

- 공식 이름 색인에 재질 23개를 더했다. 공식 TM에서 그 단어로 시작하는 아이템 이름의 80% 이상(최소 2개)이 같은 표기를 쓸 때만 켠다. 예를 들어 Ebony는 403개 중 403개가 에보니, Dragonscale은 114개 중 114개가 드래곤 비늘이다. 스카이림 TM이 없는 프로젝트에서는 켜지지 않는다.
- 다른 뜻이 없는 재질(Ebony, Dragonscale, Dragonplate, Dragonbone, Stalhrim, Chitin, Bonemold, 광석 5종)은 대문자 이름 안에서 바꾼다. "Pure Ebony", "Ebony Beam"이 여기에 해당한다.
- 종족·일반 단어이기도 한 재질(Iron, Steel, Silver, Glass, Scaled, Orcish, Dwarven, Elven, Nordic, Ancient Nord, Daedric)은 장비 단어(armor, sword, Katana, Ingot, Plate 등) 앞에서만 바꾼다. 그래서 "Daedric Lord", "Glass Cannon", "Heavy Iron Cavalry"는 그대로 둔다. "Snow Elven staff"는 스노우 엘프의 것이라 바꾸지 않는다.
- 소문자 "ebony", 문장 앞 "Ebony is rare"처럼 이름이 아닌 곳은 바꾸지 않는다.

바닐라 Skyrim.esm(67,390행), LotD(17,374행), Druadach(20,635행), 세라나 SDA(11,689행)의 원문에서 바뀌는 곳을 모두 읽었다. 거의 모두 재질 뜻이었다(Ebony armor, Steel Plate, Cyrodiilic Iron Shortsword, Refined Moonstone 등). LotD의 "The Ebony need wins out", "fire of Fools Ebony"처럼 뜻이 애매한 곳이 두어 군데 있었다.

## 실제 번역 비교

조작 키가 있는 바닐라 도움말 40행과 재질명 21행(엘든림·바닐라 평가 행, Druadach·LotD 행)을 1.9와 새 빌드로 번역했다. 둘 다 시리즈 TM을 넘겼고, 비용은 $0.027과 $0.032였다. 실패한 행은 없었다.

| 항목 | 1.9 | 1.10-preview1 |
|---|---:|---:|
| 조작 키를 영어로 둔 곳(64곳) | 64 | 64 |
| 모호한 조사(`을(를)` 등)가 있는 행(40행) | 8 | 0 |
| 조작 키 뒤에 "키"를 붙인 곳 | 20 | 53 |
| 재질 오역(흑단, 강철 빠진 판금 부츠, Bonemold → 골형) | 4 | 0 |

- 이 표본에서는 1.9도 조작 키를 번역하지 않았다. 평가 표본의 긴 도움말(HelpAttackLongPC)에서는 1.9도 번역했다. 이제는 토큰이라 번역될 수 없다.
- "Dragonscale War Dance"는 엘든림 한글판의 "드래곤스케일 난무" 대신 공식 표기 "드래곤 비늘 군무"로 나온다. 모드 한글판 표기를 따르려면 프로젝트 용어집에 넣는다. 용어집이 우선한다.
- 증거: `artifacts/keys-materials-eval/`(표본, 두 실행의 행 결과·요청·응답·사용량).
