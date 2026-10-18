# 에이전트 모드 세 번째 시험: Fists of Fury - Skyrim.esp

- 날짜: 2026-10-18
- 대상: `G:\TAKEALOOK\mods\English Translations For Sorcerer\Fists of Fury - Skyrim.esp` (주먹 싸움 대회 퀘스트 모드, 대사·선택지·퀘스트·편지)
- 도구: `agent-kit\tullius.exe`(1.15-preview15, a9b15c4 기준), 새 대화가 `agent-kit\AGENTS.md`만 보고 진행
- 목적: 화자 이름 표시(6a53954), `tullius lookup`과 줄당 한 줄 요청(d27a437)이 실제로 도움이 되는지, 문서·도구에 막히는 곳이 없는지 확인
- 결과물: `agent-kit\output\Fists_of_Fury_-_Skyrim-28036bbf\Fists of Fury - Skyrim.esp` (바뀐 필드 337개). 원본 플러그인과 MO2 모드 폴더는 건드리지 않았다.

## 숫자

| 항목 | 값 |
|---|---|
| 전체 줄 | 340 |
| 모델이 답한 줄 | 292 |
| 모델이 답하지 않은 줄 | 48 (첫 `next`에서 TM·게임 사전이 채운 24줄, 같은 원문 줄 복사, 바닐라 대사 TM) |
| 요청 수 | 10 (모두 여러 줄 JSON, 묶음 크기 30) |
| 거부·다시 물은 줄 | 0 |
| `check` | 0건 (`--all`도 0건) |
| `edit`로 고친 줄 | 3 |
| export | 바뀐 필드 337 (원문과 번역이 같은 `???`, `VIP` 2줄 제외) |

## 진행

AGENTS.md 절차대로 `open` → `next` → (request.md 읽기 → answer.txt 쓰기 → `submit`) ×10 → `check` → `show --find`/`--id` 검토 → `edit` → `check` → `export`. 막혀서 멈춘 곳은 없었다. 번역기 앱은 꺼져 있었다.

### 고친 것 (edit)

| id | 원문 | 앞 | 뒤 | 이유 |
|---|---|---|---|---|
| 76 | Erdith The Bear | 곰 에르디스 | 곰의 에르디스 | TM이 채운 #65 `Gian the Fist → 주먹의 지안`과 같은 꼴(`X the Y → Y의 X`)로 맞춤. 퀘스트 저널·목표는 이미 `곰의 에르디스`로 썼다. |
| 125 | I'm right behind you. | 바로 뒤에 있을게. | 딱 붙어서 지켜주지. | 같은 원문 #124를 TM이 이렇게 채움. 같은 화자(동료 대사)라 TM에 맞춤. |
| 127 | You already appear to have someone with you. | 이미 일행이 있는 것 같은데. | 이미 동료가 있는 것처럼 보이는데. | 같은 원문 #126의 TM 표기에 맞춤. |

### lookup을 쓴 낱말

- 나온 것: `Pit`(The Pit → 투기장), `Ratway Vaults`(쥐소굴 저장고), `Brawl`(싸움, `(Brawl)` 표시 포함 28건), `Gian`(Gian the Fist → 주먹의 지안), `the College`(대학), `Undead`(언데드), `Bear`, `Camp`(야영지), `Fury`(분노), `Ghost`(유령), `Staff of the`(지팡이 - …), `Forgotten`(잊혀진), `Drunk`, `Invitation`, `Patrons`, `Observer`, `the Nine`(나인 디바인 기사단만 나와 `나인 디바인`으로 씀)
- 없던 것(새로 정함): Erdith, Kjer, Ulthyn, Maljorn, Reggr, Fighter, Brawler, Start Brawl, Ratway Arena, Organizer, Jannik, Lassiana, Spera, Nathaniel, Verel, Doran, Barin, Drackis, Alia, Darin, Racc, Tournament, spar, Summoning Stones, Lost Blade, Pure-Heart, Erel, Contender, Hand to Hand, summoning circle, milk-drinker, Back Door, Opportunity, Ashen
- 도움이 됐다. 특히 `Gian`이 `주먹의 지안`으로 나와 같은 꼴의 `Erdith The Bear`를 맞출 근거가 됐고, `Pit`·`Ratway Vaults`는 기억대로 썼으면 달라졌을 표기다. 열쇠 이름은 `Darkwater Pit Key → 열쇠 - 다크워터 구덩이`를 보고 `열쇠 - 쥐소굴 투기장 뒷문`으로 맞췄다.

### 화자 표시

- 도움이 됐다. 대사 줄(INFO:NAM1)이 짧은 감탄사가 많아(`No.`, `Mercy!`, `Come on!`) 화자와 앞뒤 `[이름] 대사`가 없으면 누가 누구에게 하는 말인지 알 수 없었다. 싸움에서 진 NPC들이 차례로 한 마디씩 하는 장면(#103~#108, #110~#113)도 화자 표시로 흐름을 잡았다.
- 바닐라 NPC(마울, 팔리온, 니라녜)는 TM 이름으로, 모드 NPC는 영어로 나왔다. 모드 NPC의 NPC_:FULL을 앞 요청에서 번역한 뒤에도 뒤 요청의 화자는 영어(`Erdith The Bear`)였다. 참고용이라 문제는 없었다.
- 화자마다 반말 한 가지로 맞췄다(대회 싸움꾼·주최자·팔리온·마울 모두 반말, 저널은 해라체, 편지는 해라체 문어).

## 헷갈리거나 막힌 점

1. **INFO:RNAM 줄의 `Speaker:`가 틀린 사람을 가리킨다 (버그 후보).** RNAM은 플레이어가 고르는 선택지 글인데, ctx가 그 INFO의 NPC를 `Speaker:`로 적는다. 예: #130·#132·#134·#136 `Can you train me to improve my Hand to Hand combat?`에 `Speaker: Gian the Fist`/`Erdith The Bear`, #206 `Am I supposed to fight you? (Brawl Start)`에 `Speaker: Erel Pure-Heart`, #237·#239·#241에 Kjer·Maljorn·Ulthyn. 요청 규칙은 "Speaker는 그 줄을 말하는 사람이고 같은 화자는 같은 말투"라고 하니, 그대로 따르면 플레이어 선택지를 NPC 말투로 쓰게 된다. 앞뒤 줄의 `[Erdith The Bear] Can you train me…`처럼 이웃 줄 표시도 같은 식으로 틀린다. 이번에는 원문 뜻으로 플레이어 말로 옮겼다. 고치지 않고 기록만 한다.
2. **요청 규칙 절이 거의 같은 네 벌로 나온다.** `R-5c0c7149`, `R-a0773040`, `R-cbf61b54`, `R-fd9a54e0`가 edid 줄·화자 줄이 있느냐 없느냐만 다르다. 새 절이라 매번 읽어야 하는데(약 2,700자), 다른 곳이 한두 줄이라 직접 비교해야 했다. 바뀌는 줄만 따로 작은 절로 떼거나 "R-x = R-y + 이 줄"처럼 적으면 사용량을 아낄 수 있다.
3. **세션 이름 토큰이 일부 이름에만 걸린다.** 앞 요청에서 번역한 Ulthyn·Barin·Drackis·Grimm은 뒤 요청에서 `__XT_TERM_NAME_…__⟦XT_TERM=울틴⟧`처럼 왔지만, Kjer·Maljorn·Reggr·Erdith·Fights-With-Fists·The Organizer는 원문 그대로 왔다. 어느 쪽이든 답할 수는 있지만 기준을 알 수 없어, 이름을 직접 다시 써야 하는 쪽은 표기가 흔들릴 위험이 있다. 이번에는 직접 맞췄다.
4. **TM이 채운 줄만 골라 보는 방법이 없다.** AGENTS.md 4-2는 "네가 답하지 않은 줄"을 TM 표기와 맞추라고 하지만, 그 줄을 고르는 명령이 없어 답한 id를 모아 1~340에서 빼고 `show --id`로 봤다. `show --filled tm`(또는 `--not-answered`) 같은 필터가 있으면 좋다.
5. **같은 원문의 다른 번역을 `check`가 잡지 않는다.** #124/#125, #126/#127은 원문이 같은데 TM과 내 번역이 달랐다. 일부러 다르게 둘 수도 있으니 `참고`로라도 보여 주면 4-2 검토가 쉬워진다.
6. `lookup`은 한 번에 낱말 하나라 이름이 많은 요청에서는 PowerShell 반복문으로 여러 번 불렀다. 여러 낱말을 한 번에 받으면 왕복이 줄어든다.
7. `tullius show --help`가 `'--help' 프로젝트가 없습니다`로 실패한다. 옵션은 AGENTS.md에 있어 막히지는 않았다.
8. 원문의 `BLANK`(퀘스트 목표 #270·#275·#276), 오타(`Champoin`, `Deafeat`, `seperate`)는 모드 원문 그대로라 `BLANK`는 두고 오타는 뜻대로 옮겼다. 특별히 막히지 않았다.

## 개선 후보

- RNAM 줄은 `Speaker:` 대신 "플레이어 선택지, 상대: NAME"으로 적고, 이웃 줄 표시도 RNAM이면 플레이어로 적기 (1)
- 요청 규칙 절을 고정 부분과 바뀌는 줄로 나누기 (2)
- 세션 이름 토큰이 걸리는 기준을 맞추거나 문서에 적기 (3)
- `show`에 TM이 채운 줄 필터 (4), `check` 참고에 같은 원문 다른 번역 (5)
- `lookup`에 여러 낱말 (6), `--help` (7)
