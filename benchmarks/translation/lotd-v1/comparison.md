# LOTD 24개 원문·번역·검토

비블라인드 AI 검토이다. 평가 근거가 부족한 화자 신분·고유명사 표준·선행 대화는 확정하지 않는다. 엄격 JSON 실패 응답은 복구하지 않고 원본 링크를 제공한다.

## L10885 · BOOK:DESC

```text
<font face="$HandwrittenFont">

7th of Sun's Dawn
     
              Fort Frostmoth was once the jewel of the empire on Solstheim, a rich center for the East Empire Trading Company, but after the Red Year disaster the fort was all but destroyed, leaving behind much death and ruin. The chaos of everyone's departure from Morrowind and parts of Solstheim would ensure that much was left behind. I expect that some store room or another must have been buried and left filled with riches. When I reach Solstheim, I should try and see what is left there.

</font>
```

제공 용어: {"Morrowind": "모로윈드", "Solstheim": "솔스트하임", "Sun's Dawn": "2월"}

### Gemini 3.8 Flash / Low

제공 용어와 사건 관계를 대체로 지켰으며 일지 문체가 자연스럽다.

```text
<font face="$HandwrittenFont">

2월 7일
     
              프로스트모스 요새는 한때 동제국 무역 회사의 부유한 거점이자 솔스트하임에서 제국의 보석과도 같았던 곳이었으나, 붉은 해의 재앙 이후 요새는 거의 파괴되어 수많은 죽음과 폐허만을 남겼다. 모두가 모로윈드와 솔스트하임 일부 지역을 급히 떠나느라 벌어진 혼란 속에서 필시 많은 것들이 남겨졌을 것이다. 어느 저장고 같은 곳이 파묻혀 온갖 보물로 가득 찬 채 남아있을 것으로 기대된다. 솔스트하임에 도착하면, 그곳에 무엇이 남아있는지 확인해 보아야겠다.

</font>
```

[원본 응답](runs/gemini-3.8-flash-low/pilot-01.txt)

### Gemini 3.1 Flash Lite / Minimal

Sun's Dawn=2월 지시를 무시하고 '태양의 달'로 옮겼다. 본문은 대체로 자연스럽다.

```text
<font face="$HandwrittenFont">

태양의 달 7일
     
              프로스트모스 요새는 한때 솔스트하임에서 제국의 보석이자 동제국 회사의 부유한 중심지였으나, 붉은 해의 재앙 이후 요새는 거의 파괴되었고 수많은 죽음과 폐허만이 남았다. 모로윈드와 솔스트하임의 여러 지역에서 사람들이 떠나며 벌어진 혼란으로 인해 많은 것이 방치되었을 것이다. 어딘가 창고 하나쯤은 묻힌 채 보물로 가득 차 있을 것이라 예상한다. 솔스트하임에 도착하면 무엇이 남아있는지 확인해 봐야겠다.

</font>
```

[원본 응답](runs/gemini-3.1-flash-lite-minimal/pilot-01.txt)

### Solar Pro 3 / Low

원문에 없는 '솔스톰 제국'을 만들고 Morrowind 지정 용어를 어겼다. 고유명사의 과도한 음역과 어색한 창고 문장도 수정이 필요하다.

```text
<font face="$HandwrittenFont">

2월 7일

              솔스트하임의 포트 프로스트모스는 한때 솔스톰 제국에게 보석 같은 곳이었고, 이스트 엠파이어 트레이딩 컴퍼니의 부유한 중심지였습니다. 그러나 레드 이어 참사 이후 요새는 거의 파괴되어 많은 죽음과 폐허를 남겼습니다. 모로우윈드와 솔스트하임의 일부 지역에서 모두가 떠나는 혼란으로 인해 많은 것들이 남겨지게 되었습니다. 어느 창고나 다른 곳에 묻히고 보물로 가득 차 있을 것으로 예상합니다. 솔스트하임에 도착하면 남은 것이 무엇인지 확인해야 합니다.

</font>
```

[원본 응답](runs/solar-pro3-low/pilot-01.txt)

### Solar Pro 4 / Solar Chat 즉시

Sun's Dawn 지정 용어 위반, 'riches가' 미번역, '솔스트하임 제국'으로 관계가 모호해졌다. 웹 에이전트 경로 결과이다.

```text
<font face="$HandwrittenFont">

선성의 여명 7일
     
              포트 프로스트모스는 한때 솔스트하임 제국의 보석과도 같은 곳으로, 동제국 교역 회사의 풍요로운 중심지였으나, 붉은 해 재앙 이후 요새는 거의 파괴되어 많은 죽음과 폐허만을 남겼다. 모로윈드와 솔스트하임의 일부에서 모두가 떠나는 혼란 속에 많은 것이 그대로 남겨졌을 것이다. 어딘가의 저장실이 묻혀 riches가 가득한 채 잊혔을 것이라 예상한다. 솔스트하임에 도착하면 그곳에 무엇이 남아있는지 살펴봐야겠다.

</font>
```

[원본 응답](runs/solar-pro4-web-instant/pilot-01.txt)

## L10074 · BOOK:DESC

```text
[pagebreak]
<p align="center">


<b>Dwemer Sexuality Vol III</b>

'Marital Aides'

by
Prudencen I. Hazhedake, Scholar
</p>

[pagebreak]
<p align="left">
<font face='$SkyrimBooks'>
<img src='img://Textures/Interface/Books/Illuminated_Letters/N_letter.png'> ow, with the third and final volume of our series, gentle reader, we may begin discussion of the practical application of what we have learned thus far. I strongly recommend that serious students of this topic conduct their own personal field research...

[pagebreak]
[You quickly surmise that there will not be anything useful in this book.] 




```

제공 용어: {"Dwemer": "드웨머"}

### Gemini 3.8 Flash / Low

본문·제목·표식을 보존했다. Scholar를 학위인 '학사'로 좁힌 표현과 마지막 문장의 번역투는 손볼 수 있다.

```text
[pagebreak]
<p align="center">


<b>드웨머의 성생활 제3권</b>

'부부 생활 보조 기구'

저자
프루덴슨 I. 헤즈헤데이크 학사
</p>

[pagebreak]
<p align="left">
<font face='$SkyrimBooks'>
<img src='img://Textures/Interface/Books/Illuminated_Letters/N_letter.png'> 자, 이제 이 시리즈의 마지막이자 제3권에 이르러, 친애하는 독자 여러분, 지금까지 우리가 배운 내용의 실전 적용에 관한 논의를 시작해 볼 수 있겠습니다. 이 주제를 진지하게 공부하는 학도라면 각자 몸소 현장 연구를 수행해 보기를 강력히 권장하는 바입니다...

[pagebreak]
[이 책에는 아무런 쓸모 있는 내용도 없을 것 같다는 판단이 재빨리 들었습니다.] 




```

[원본 응답](runs/gemini-3.8-flash-low/pilot-02.txt)

### Gemini 3.1 Flash Lite / Minimal

제목·본문을 번역하고 표식을 보존했다. 일부 설명체 표현은 다소 딱딱하지만 핵심 의미는 유지된다.

```text
[pagebreak]
<p align="center">


<b>드웨머의 성생활 제3권</b>

'부부 관계 보조 기구'

저자
학자, 프루덴센 I. 해즈헤데이크
</p>

[pagebreak]
<p align="left">
<font face='$SkyrimBooks'>
<img src='img://Textures/Interface/Books/Illuminated_Letters/N_letter.png'> 자, 친애하는 독자 여러분, 본 시리즈의 세 번째이자 마지막 권을 통해 우리는 지금까지 배운 내용을 실제로 적용하는 방법에 대해 논의해 볼까 합니다. 이 주제를 진지하게 연구하는 학생들에게 직접 개인적인 현장 조사를 수행할 것을 강력히 권하는 바입니다...

[pagebreak]
[이 책에는 유용한 정보가 전혀 없으리라는 것을 빠르게 짐작할 수 있습니다.] 




```

[원본 응답](runs/gemini-3.1-flash-lite-minimal/pilot-02.txt)

### Solar Pro 3 / Low

제목·부제·저자 행을 영어로 남겼다. font의 $ 앞에 역슬래시를 추가했고 gentle reader 호칭도 빠졌다.

```text
[pagebreak]
<p align="center">


<b>Dwemer Sexuality Vol III</b>

'Marital Aides'

by
Prudencen I. Hazhedake, Scholar
</p>

[pagebreak]
<p align="left">
<font face='\$SkyrimBooks'>
<img src='img://Textures/Interface/Books/Illuminated_Letters/N_letter.png'> 오, 세 번째이자 마지막 권을 통해 우리는 지금까지 배운 것을 실제 적용하는 방법을 논할 수 있게 되었습니다. 진지한 이 분야 연구자는 본인만의 현장 연구를 수행할 것을 강력히 권장합니다...

[pagebreak]
[이 책에는 유용한 정보가 없을 것으로 보입니다.] 




```

[원본 응답](runs/solar-pro3-low/pilot-02.txt)

### Solar Pro 4 / Solar Chat 즉시

본문 대부분과 저자 행을 영어로 남기고 장식 이미지 태그를 삭제했다. 그대로 사용할 수 없다.

```text
[pagebreak]
<p align="center">


<b>드웨머의 성 Vol III</b>

'결혼 보조 도구'

by
Prudencen I. Hazhedake, Scholar
</p>

[pagebreak]
<p align="left">
<font face='$SkyrimBooks'>
 ow, with the third and final volume of our series, gentle reader, we may begin discussion of the practical application of what we have learned thus far. I strongly recommend that serious students of this topic conduct their own personal field research...

[pagebreak]
[You quickly surmise that there will not be anything useful in this book.] 




```

[원본 응답](runs/solar-pro4-web-instant/pilot-02.txt)

## L10087 · BOOK:DESC

```text
<font face="$HandwrittenBold" size="24">
Commission Contract
</font>
<font face="$HandwrittenBold">
Burgess Olfstead of Haafingar agrees to sculpt one life size statue in the likeness of lady Orellia Macnarian using a provided oil painting as reference and hewn from natural marble provided by the patron, Niles Macnarian, for the sum of 8000 septims. The amount of 4000 septims has been paid in advance with the rest payable prior to delivery of the work.</font>

```

### Gemini 3.8 Flash / Low

8000 총액·4000 선지급·인도 전 잔금 조건을 유지했다. 총액을 '계약금'이라고 부른 것은 착수금으로 읽힐 수 있어 '제작비 총액'이 낫다.

```text
<font face="$HandwrittenBold" size="24">
의뢰 계약서
</font>
<font face="$HandwrittenBold">
하핑가르의 버기스 올프스테드는 의뢰인 나일스 맥나리안이 제공한 유화를 참고하고 역시 그가 제공한 천연 대리석을 깎아내어, 오렐리아 맥나리안 부인의 모습을 본뜬 실물 크기의 조각상 1점을 제작하는 데 동의하며 계약금은 8,000세프팀으로 한다. 계약금 중 4,000세프팀은 선불로 지급되었으며, 잔금은 작품을 인도하기 전에 지급하기로 한다.</font>

```

[원본 응답](runs/gemini-3.8-flash-low/pilot-03.txt)

### Gemini 3.1 Flash Lite / Minimal

동일 입력 두 시도 모두 화면에 internal error. 번역 품질 평가 불가.

### Solar Pro 3 / Low

번역문이 원문과 완전히 같고 한글이 없다.

```text
<font face="$HandwrittenBold" size="24">
Commission Contract
</font>
<font face="$HandwrittenBold">
Burgess Olfstead of Haafingar agrees to sculpt one life size statue in the likeness of lady Orellia Macnarian using a provided oil painting as reference and hewn from natural marble provided by the patron, Niles Macnarian, for the sum of 8000 septims. The amount of 4000 septims has been paid in advance with the rest payable prior to delivery of the work.</font>

```

[원본 응답](runs/solar-pro3-low/pilot-03.txt)

### Solar Pro 4 / Solar Chat 즉시

septims를 '시길드'로 바꿨다. font 경계와 줄바꿈 위치도 달라졌다. 금액과 지급 시점은 유지했다.

```text
<font face="$HandwrittenBold" size="24">
의뢰 계약서
</font>
<font face="$HandwrittenBold">
하핑거 지역의 버제스 올프스테드는 오렐리아 맥나리안 부인의 모습을 본뜬 실물 크기 조각상 하나를, 후원자인 나일스 맥나리안이 제공한 천연 대리석으로, 참고 자료로 제공된 유화 한 점을 이용해, 8000 시길드에 제작하기로 합의한다. 4000 시길드는 선지급되었으며, 나머지는 작품 인도 전에 지급해야 한다.
</font>
```

[원본 응답](runs/solar-pro4-web-instant/pilot-03.txt)

## L10801 · BOOK:DESC

```text
<font face='$Handwrittenbold'>

In all my studies of magical legend and lore concerning Skyrim, none have fascinated me more than the exploits of the great Shalidor. I was fortunate enough in fact to have come across some of his writings and after some effort of deciphering them I discovered quite an interesting account of the construction of a magical stone which Shalidor referred to as an Arcane battery which could focus and concentrate soul energy and instill it into the spell caster who invoked it in the form of Magicka. Quite an interesting artifact if in fact it does exist. I would suspect that the depths of Labyrinthian would hold the secrets of the stone as Shalidor sealed himself away for years at a time within its depths. Oh the secrets which must reside within. I should very much like to explore it one day.
```

제공 용어: {"Skyrim": "스카이림", "Magicka": "매지카"}

### Gemini 3.8 Flash / Low

문장 연결과 판타지 문체가 자연스럽고 마법석 작동 설명을 유지했다.

```text
<font face='$Handwrittenbold'>

스카이림에 얽힌 마법 전설과 전승을 연구하면서, 위대한 샬리도어의 위업만큼 나를 매료시킨 것은 없었다. 운 좋게도 그의 저술 몇 편을 접할 수 있었는데, 고심 끝에 해독해 낸 결과 꽤 흥미로운 기록을 발견했다. 샬리도어가 '비전 배터리'라 칭한 마법석의 제작에 관한 내용이었다. 이 돌은 영혼 에너지를 한데 모아 집중시킨 뒤, 이를 발동한 시전자에게 매지카의 형태로 불어넣어 준다고 한다. 실제로 존재한다면 대단히 흥미로운 유물일 것이다. 샬리도어가 수년간 그 깊은 곳에 은둔했던 만큼, 래비린시안 심층부에 이 돌의 비밀이 숨겨져 있을 것으로 짐작된다. 아, 그 안에 잠들어 있을 비밀들이란. 언젠가 꼭 그곳을 탐험해 보고 싶다.
```

[원본 응답](runs/gemini-3.8-flash-low/pilot-04.txt)

### Gemini 3.1 Flash Lite / Minimal

construction을 '구조', invoked를 '호출'로 옮긴 부분은 문맥상 '제작/발동'보다 덜 자연스럽다. 큰 줄기는 유지했다.

```text
<font face='$Handwrittenbold'>

스카이림의 마법 전설과 지식에 관한 나의 모든 연구 중, 위대한 샬리도어의 업적만큼 나를 매료시킨 것은 없었다. 운 좋게도 그의 저술 중 일부를 손에 넣을 수 있었고, 해독을 위해 노력한 끝에 샬리도어가 '비전 배터리'라고 불렀던 마법 돌의 구조에 대한 매우 흥미로운 기록을 발견했다. 이 돌은 영혼의 에너지를 모아 집중시키고, 이를 호출한 주문 사용자에게 매지카의 형태로 주입할 수 있다고 한다. 실제로 존재한다면 정말 흥미로운 유물일 것이다. 샬리도어가 수년간 그 깊은 곳에 스스로를 봉인했던 만큼, 라비린시안의 심연에 그 돌의 비밀이 잠들어 있을 것으로 추측된다. 오, 그곳에 얼마나 많은 비밀이 숨겨져 있을까. 언젠가 꼭 탐험해 보고 싶다.
```

[원본 응답](runs/gemini-3.1-flash-lite-minimal/pilot-04.txt)

### Solar Pro 3 / Low

'전설과 전설', '집중하고 집중', '깊이', '비밀이 거주' 등 번역투와 arcane battery 미번역이 있다.

```text
<font face='$Handwrittenbold'>

내 연구와 스카이림의 마법 전설과 전설에 대해, 위대한 살리도르의 모험보다 더 흥미로운 것은 없습니다. 사실 나는 그의 기록을 몇 가지 접할 수 있었고, 그것을 해독하는 데 어느 정도 노력을 기울인 후, 살리도르가 'arcane battery'라고 언급한 마법 돌의 건설에 대한 흥미로운 기록을 발견했습니다. 이는 영혼 에너지를 집중하고 집중시키며, 이를 매지카의 형태로 소환하는 마법사에게 주입할 수 있었습니다. 실제로 존재한다면 상당히 흥미로운 유물입니다. 나는 라비린티안의 깊이가 그 돌의 비밀을 간직하고 있을 것이라고 추측합니다. 살리도르가 그 안에서 수년 동안 스스로를 봉인했기 때문입니다. 얼마나 많은 비밀이 그곳에 거주할지. 언젠가 꼭 탐험하고 싶습니다.
```

[원본 응답](runs/solar-pro3-low/pilot-04.txt)

### Solar Pro 4 / Solar Chat 즉시

대체로 의미를 유지했지만 '조집' 같은 어색한 단어와 construction 정보 축약을 손볼 필요가 있다.

```text
<font face='$Handwrittenbold'>

스카이림에 관한 마법의 전설과 비전(祕傳)을 모두 연구해 왔지만, 위대한 샤일리더의 업적만큼 매혹적인 것은 없었습니다. 운 좋게도 실제로 그의 저작물 일부를 접하게 되었고, 일부 해독을 거친 끝에 샤일리더가 '비전 배터리'라고 칭한, 영혼 에너지를 집중·조집하고 그것을 발동하는 마법사에게 매지카 형태로 주입할 수 있는 마법의 돌에 대한 꽤 흥미로운 기록을 발견했습니다. 만약 실제로 존재한다면 꽤 흥미로운 유물일 것입니다. 샤일리더가 그 깊은 곳 안에서 수년 동안 스스로를 봉인해 지냈으니, 라비린시안 깊숙한 곳에 그 돌의 비밀이 담겨 있으리라 짐작합니다. 그 안에 머무르고 있을 비밀이라니. 언젠가 꼭 탐험해 보고 싶습니다.
```

[원본 응답](runs/solar-pro4-web-instant/pilot-04.txt)

## L14705 · INFO:NAM1

```text
Or better still, how I stole the greatest of all jewels; the heart of a beautiful lady after avenging the death of her dear father.
```

### Gemini 3.8 Flash / Low

마음을 훔친다는 비유를 살렸다. '그녀의 아버지' 소유 관계를 더 명시하면 좋다.

```text
아니면 더 기가 막힌 이야기, 사랑하는 아버지의 원수를 갚아주고 아름다운 귀부인의 마음이라는 세상에서 가장 위대한 보석을 훔쳐낸 이야기는 어떤가.
```

[원본 응답](runs/gemini-3.8-flash-low/pilot-05.txt)

### Gemini 3.1 Flash Lite / Minimal

마음의 비유와 그녀의 아버지라는 관계를 유지했다. 문장이 길지만 이해 가능하다.

```text
아니면 더 나은 이야기로, 제가 어떻게 세상에서 가장 위대한 보석을 훔쳤는지 말해드릴까요? 바로 그녀의 소중한 아버지를 죽인 원수를 갚고 나서 얻은 아름다운 숙녀의 마음 말입니다.
```

[원본 응답](runs/gemini-3.1-flash-lite-minimal/pilot-05.txt)

### Solar Pro 3 / Low

연애적 heart 비유를 '심장'으로 직역해 이 판타지 문맥에서는 실제 장기로 오해할 수 있다. '죽음을 복수'도 부자연스럽다.

```text
아니면 차라리, 내가 가장 위대한 보석을 훔친 이야기를 들려줄까? 아름다운 아가씨의 심장을, 그녀의 사랑하는 아버지의 죽음을 복수한 뒤에.
```

[원본 응답](runs/solar-pro3-low/pilot-05.txt)

### Solar Pro 4 / Solar Chat 즉시

heart를 '심장'으로 직역했고 Or better still을 '더구나'로 바꿨다. 비유와 화제 선택의 말맛이 약하다.

```text
더구나 가장 위대한 보석, 즉 아름다운 여인의 심장을 훔친 내 이야기를 들려줄까. 그 여인의 사랑하는 아버지의 원수를 갚은 후에 말이야.
```

[원본 응답](runs/solar-pro4-web-instant/pilot-05.txt)

## L13793 · INFO:NAM1

```text
What about before the Red Year?
```

### Gemini 3.8 Flash / Low

사건 이전 시점에 관한 질문을 유지했다.

```text
붉은 해 이전에는 어땠나요?
```

[원본 응답](runs/gemini-3.8-flash-low/pilot-05.txt)

### Gemini 3.1 Flash Lite / Minimal

사건 이전 시점에 관한 질문을 대체로 유지했다.

```text
붉은 해가 오기 전은 어땠나요?
```

[원본 응답](runs/gemini-3.1-flash-lite-minimal/pilot-05.txt)

### Solar Pro 3 / Low

의미를 유지했다. 화자 관계가 없어 반말 적합성은 판단하지 않았다.

```text
붉은 해 이전은 어땠어?
```

[원본 응답](runs/solar-pro3-low/pilot-05.txt)

### Solar Pro 4 / Solar Chat 즉시

Red Year를 '붉은 해가 뜨기 전'으로 옮겨 연도/사건을 일출로 바꿨다.

```text
붉은 해가 뜨기 전에는 어땠어요?
```

[원본 응답](runs/solar-pro4-web-instant/pilot-05.txt)

## L13869 · INFO:NAM1

```text
I've got to say, Madras, Dwemer architecture is amongst the finest I've ever seen.
```

제공 용어: {"Dwemer": "드웨머"}

### Gemini 3.8 Flash / Low

amongst와 자연스러운 대사 문체를 유지했다.

```text
솔직히 말하자면 마드라스, 드웨머 건축 양식은 내가 본 것 중에서도 가장 뛰어난 축에 속하네.
```

[원본 응답](runs/gemini-3.8-flash-low/pilot-05.txt)

### Gemini 3.1 Flash Lite / Minimal

amongst의 완화가 약해지고 '말할 수밖에 없네요'가 조금 번역투이다.

```text
마드라스, 드웨머 건축술은 정말 제가 본 것 중 최고라고 말할 수밖에 없네요.
```

[원본 응답](runs/gemini-3.1-flash-lite-minimal/pilot-05.txt)

### Solar Pro 3 / Low

'말해야겠어'가 직역투이며 Madras를 '마다스'로 옮겼다.

```text
말해야겠어, 마다스, 드웨머 건축은 내가 본 가장 훌륭한 것 중 하나야.
```

[원본 응답](runs/solar-pro3-low/pilot-05.txt)

### Solar Pro 4 / Solar Chat 즉시

의미는 대체로 유지했지만 도입부는 다소 딱딱하다.

```text
말씀드려야겠네요, 마드라스. 드웨머 건축물은 제가 본 것 중 최고에 속해요.
```

[원본 응답](runs/solar-pro4-web-instant/pilot-05.txt)

## L12756 · INFO:NAM1

```text
Is that what we came here for? Can we go to another Dwemer ruin now?
```

제공 용어: {"Dwemer": "드웨머"}

### Gemini 3.8 Flash / Low

목적 확인과 다음 유적으로 가려는 질문을 유지했다.

```text
우리가 이것 때문에 여기까지 온 건가요? 이제 다른 드웨머 유적으로 가도 되나요?
```

[원본 응답](runs/gemini-3.8-flash-low/pilot-05.txt)

### Gemini 3.1 Flash Lite / Minimal

질문 의미를 유지했다.

```text
우리가 여기 온 목적이 그것 때문인가요? 이제 다른 드웨머 유적으로 가도 될까요?
```

[원본 응답](runs/gemini-3.1-flash-lite-minimal/pilot-05.txt)

### Solar Pro 3 / Low

질문 의미를 대체로 유지했다.

```text
그게 우리가 여기 온 이유야? 지금 다른 드웨머 폐허로 갈 수 있을까?
```

[원본 응답](runs/solar-pro3-low/pilot-05.txt)

### Solar Pro 4 / Solar Chat 즉시

'그게 다야'와 부정 질문으로 불만의 뉘앙스를 추가했다. 원문보다 강하다.

```text
우리 여기 온 이유가 그게 다야? 다른 드웨머 유적으로 갈 수 없을까?
```

[원본 응답](runs/solar-pro4-web-instant/pilot-05.txt)

## L16189 · INFO:NAM1

```text
Ah, I'm glad I caught you, I have something for you.
```

### Gemini 3.8 Flash / Low

'마침 잘 만났군'이 자연스럽다. 화자 신분에 맞는 높임말은 원자료로 판단 불가.

```text
아, 마침 잘 만났군. 자네에게 줄 게 있네.
```

[원본 응답](runs/gemini-3.8-flash-low/pilot-05.txt)

### Gemini 3.1 Flash Lite / Minimal

의미를 유지했다.

```text
아, 당신을 만나서 다행이군요. 드릴 게 있습니다.
```

[원본 응답](runs/gemini-3.1-flash-lite-minimal/pilot-05.txt)

### Solar Pro 3 / Low

'다행'보다 '반가워'에 치우치지만 의미상 큰 문제는 발견하지 못했다.

```text
아, 너를 만나서 반가워, 너에게 줄 게 있어.
```

[원본 응답](runs/solar-pro3-low/pilot-05.txt)

### Solar Pro 4 / Solar Chat 즉시

의미를 유지했다.

```text
아, 잘 만났네요. 당신에게 드릴 게 있어요.
```

[원본 응답](runs/solar-pro4-web-instant/pilot-05.txt)

## L12484 · INFO:NAM1

```text
Someone was leaving trash around. Don't worry this one cleaned it up.
```

### Gemini 3.8 Flash / Low

this one의 독특한 자기 지칭을 남겼으나 '이자가'는 다소 어색하다. 실제 화자 종족은 확인되지 않았다.

```text
누가 쓰레기를 마구 버려뒀더군요. 걱정 마세요, 이자가 다 치웠으니까요.
```

[원본 응답](runs/gemini-3.8-flash-low/pilot-05.txt)

### Gemini 3.1 Flash Lite / Minimal

'제가'로 자연스럽게 옮겼지만 원문의 독특한 자기 지칭 문체는 사라졌다.

```text
누군가 쓰레기를 버리고 갔더군요. 걱정 마세요, 제가 다 치웠으니까요.
```

[원본 응답](runs/gemini-3.1-flash-lite-minimal/pilot-05.txt)

### Solar Pro 3 / Low

Don't worry가 빠지고 '이 사람' 지칭이 모호하다.

```text
누군가가 쓰레기를 주변에 버리고 있었어. 이 사람은 그걸 치워 줬어.
```

[원본 응답](runs/solar-pro3-low/pilot-05.txt)

### Solar Pro 4 / Solar Chat 즉시

안심시키는 발화와 청소한 주체를 '다행히 이건 다 치워져'로 바꿨다.

```text
누가 쓰레기를 버리고 갔나 봐요. 다행히 이건 다 치워져 있네요.
```

[원본 응답](runs/solar-pro4-web-instant/pilot-05.txt)

## L15430 · INFO:NAM1

```text
I didn't think we'd ever manage to hunt all four guardians and with you wiping out the Morag Tong like that
```

제공 용어: {"Pale": "페일", "Skyrim": "스카이림", "Sovngarde": "소븐가드"}

### Gemini 3.8 Flash / Low

네 수호자와 Morag Tong를 유지했다. 다음 INFO 부분으로 이어지는 문장인 점을 감안해야 한다.

```text
네 수호자를 전부 사냥할 수 있으리라곤 생각도 못 했는데, 게다가 당신이 모라그 통을 그렇게 쓸어버려 주다니
```

[원본 응답](runs/gemini-3.8-flash-low/pilot-06.txt)

### Gemini 3.1 Flash Lite / Minimal

'네 분'은 수호자에게 높임을 추가한다. 문장 연결과 의미는 대체로 유지했다.

```text
네 분의 수호자를 모두 사냥해낼 줄은 몰랐군. 게다가 당신이 모라그 통을 그렇게 쓸어버리다니,
```

[원본 응답](runs/gemini-3.1-flash-lite-minimal/pilot-06.txt)

### Solar Pro 3 / Low

Morag Tong가 '모라그 콩'으로 변했다. 원문에 없는 '놀랐습니다'로 문장을 닫았다.

```text
저는 우리 모두가 네 명의 수호자를 모두 사냥할 수 있을 거라고 생각하지 않았고, 당신이 그렇게 모라그 콩을 전멸시킨 것도 놀랐습니다. 
```

[원본 응답](runs/solar-pro3-low/pilot-06.txt)

### Solar Pro 4 / Solar Chat 즉시

Morag Tong를 '모로긴 통'으로 옮겼고 '네 수호자 넷'이 중복이다.

```text
네 수호자 넷을 모두 사냥할 수 있으리라고는 생각하지 못했고, 당신이 모로긴 통을 그렇게 쓸어버린 걸 보면
```

[원본 응답](runs/solar-pro4-web-instant/pilot-06.txt)

## L16035 · INFO:NAM1

```text
Oh my, look there, the temple is down here!
```

### Gemini 3.8 Flash / Low

여기 아래라는 위치를 유지했다.

```text
어머나, 저기 봐요, 신전이 이 아래에 있어요!
```

[원본 응답](runs/gemini-3.8-flash-low/pilot-06.txt)

### Gemini 3.1 Flash Lite / Minimal

여기 아래라는 위치를 유지했다.

```text
어머나, 저길 봐, 사원이 여기 아래에 있어!
```

[원본 응답](runs/gemini-3.1-flash-lite-minimal/pilot-06.txt)

### Solar Pro 3 / Low

look there를 '여기 봐요'로 바꿨다.

```text
오, 여기 봐요, 사원이 여기 아래에 있어요!
```

[원본 응답](runs/solar-pro3-low/pilot-06.txt)

### Solar Pro 4 / Solar Chat 즉시

down here를 '저 아래'로 바꿔 지시 거리가 달라졌다.

```text
어머, 저기 봐, 신전이 저 아래에 있어!
```

[원본 응답](runs/solar-pro4-web-instant/pilot-06.txt)

## L12827 · INFO:NAM1

```text
You have been found! And now will be lost.
```

### Gemini 3.8 Flash / Low

위협의 의미를 자연스럽게 옮겼다. '영원히'는 원문보다 강한 첨가이다.

```text
찾아냈다! 그리고 이제 영원히 사라지리라.
```

[원본 응답](runs/gemini-3.8-flash-low/pilot-06.txt)

### Gemini 3.1 Flash Lite / Minimal

위협으로 자연스럽게 읽힌다.

```text
발각되었다! 이제 사라질 시간이다.
```

[원본 응답](runs/gemini-3.1-flash-lite-minimal/pilot-06.txt)

### Solar Pro 3 / Low

수동형과 존댓말이 위협 장면에 어색하지만 화자 정보가 부족하다.

```text
당신이 발견되었어요! 그리고 이제 사라질 것입니다.
```

[원본 응답](runs/solar-pro3-low/pilot-06.txt)

### Solar Pro 4 / Solar Chat 즉시

will be lost를 '잃게 되리라'로 바꿔 잃는 대상이 없는 능동문이 됐다.

```text
찾았다! 그리고 이제 잃게 되리라.
```

[원본 응답](runs/solar-pro4-web-instant/pilot-06.txt)

## L15123 · INFO:NAM1

```text
Maybe so, but that fraction is mighty important too.
```

### Gemini 3.8 Flash / Low

fraction을 문맥을 좁히지 않는 '일부'로 옮겼다.

```text
그럴지도 모르지만, 그 일부 역시 아주 중요하단 말이지.
```

[원본 응답](runs/gemini-3.8-flash-low/pilot-06.txt)

### Gemini 3.1 Flash Lite / Minimal

'작은 조각'으로 구체화했다. 선행 대화가 없어 적합성 확정 불가.

```text
그럴지도 모르지만, 그 작은 조각도 매우 중요하단 말이지.
```

[원본 응답](runs/gemini-3.1-flash-lite-minimal/pilot-06.txt)

### Solar Pro 3 / Low

'비율'로 좁혔다. 선행 대화가 없어 오역으로 확정하지 않는다.

```text
그럴 수도 있지만 그 비율도 매우 중요합니다.
```

[원본 응답](runs/solar-pro3-low/pilot-06.txt)

### Solar Pro 4 / Solar Chat 즉시

'단편'이 다소 딱딱하다. 선행 대화가 없어 의미 오류 확정 불가.

```text
그럴지도 모르지만, 그 단편이 아주 중요하기도 하다.
```

[원본 응답](runs/solar-pro4-web-instant/pilot-06.txt)

## L05541 · MISC:FULL

```text
Smithsonite
```

### Gemini 3.8 Flash / Low

광물명 '능아연석'을 사용했다. 이 표본의 용어집에는 지정값이 없다.

```text
능아연석
```

[원본 응답](runs/gemini-3.8-flash-low/pilot-06.txt)

### Gemini 3.1 Flash Lite / Minimal

음역을 사용했다. 지정 용어가 없어 표준화 검토 대상으로만 본다.

```text
스미스소나이트
```

[원본 응답](runs/gemini-3.1-flash-lite-minimal/pilot-06.txt)

### Solar Pro 3 / Low

음역을 사용했다. 지정 용어가 없어 표준화 검토 대상으로만 본다.

```text
스미스소나이트
```

[원본 응답](runs/solar-pro3-low/pilot-06.txt)

### Solar Pro 4 / Solar Chat 즉시

음역을 사용했다. 지정 용어가 없어 표준화 검토 대상으로만 본다.

```text
스미소나이트
```

[원본 응답](runs/solar-pro4-web-instant/pilot-06.txt)

## L06060 · WEAP:FULL

```text
Lunar Iron War Axe (replica)
```

### Gemini 3.8 Flash / Low

게임 아이템 이름으로 자연스럽게 번역했다.

```text
달빛 철 전투도끼 (복제품)
```

[원본 응답](runs/gemini-3.8-flash-low/pilot-06.txt)

### Gemini 3.1 Flash Lite / Minimal

의미를 유지했지만 '전쟁 도끼'보다 '전투도끼'가 아이템명으로 자연스럽다.

```text
달빛 철 전쟁 도끼 (복제품)
```

[원본 응답](runs/gemini-3.1-flash-lite-minimal/pilot-06.txt)

### Solar Pro 3 / Low

대부분 음역이며 replica가 영어로 남았다.

```text
루나 아이언 워 액스 (replica)
```

[원본 응답](runs/solar-pro3-low/pilot-06.txt)

### Solar Pro 4 / Solar Chat 즉시

Lunar를 '루나'로 음역해 달빛 의미가 약해졌다.

```text
루나 철 전쟁 도끼 (복제품)
```

[원본 응답](runs/solar-pro4-web-instant/pilot-06.txt)

## L02603 · ARMO:FULL

```text
Helm of Tiber Septim (replica)
```

제공 용어: {"Tiber Septim": "타이버 셉팀"}

### Gemini 3.8 Flash / Low

의미를 유지했다.

```text
타이버 셉팀의 투구 (복제품)
```

[원본 응답](runs/gemini-3.8-flash-low/pilot-07.txt)

### Gemini 3.1 Flash Lite / Minimal

의미를 유지했다.

```text
타이버 셉팀의 투구 (복제품)
```

[원본 응답](runs/gemini-3.1-flash-lite-minimal/pilot-07.txt)

### Solar Pro 3 / Low

잘못된 JSON 반복 안의 내용을 직접 검토했다. Helm을 '왕관'으로 바꾸고 replica를 남겼다. 자동 파싱 결과에는 포함하지 않는다.

[원본 응답](runs/solar-pro3-low/pilot-07.txt)

### Solar Pro 4 / Solar Chat 즉시

헬름 음역과 소유 관계 생략은 용어 통일 시 수정할 수 있다.

```text
타이버 셉팀 헬름 (복제품)
```

[원본 응답](runs/solar-pro4-web-instant/pilot-07.txt)

## L09410 · ARMO:DESC

```text
Stonehands' Stone Hands. 10 point bonus to Unarmed Attacks and Speechcraft and doubles damage with pickaxes.
```

제공 용어: {"Speechcraft": "화술"}

### Gemini 3.8 Flash / Low

10점과 곡괭이 두 배 효과를 유지했다.

```text
스톤핸드의 돌주먹. 맨손 공격력과 화술이 10점 증가하며 곡괭이의 공격력이 두 배가 됩니다.
```

[원본 응답](runs/gemini-3.8-flash-low/pilot-07.txt)

### Gemini 3.1 Flash Lite / Minimal

수치와 효과를 유지했다. 아이템 설명 문체는 더 간결하게 다듬을 수 있다.

```text
스톤핸즈의 돌 손. 맨손 공격과 화술에 10 포인트 보너스를 주고 곡괭이 피해량을 두 배로 증가시킵니다.
```

[원본 응답](runs/gemini-3.1-flash-lite-minimal/pilot-07.txt)

### Solar Pro 3 / Low

반복된 비정상 JSON 안에서 수치는 유지했으나 Stonehands/Stone Hands를 영어로 남겼다.

[원본 응답](runs/solar-pro3-low/pilot-07.txt)

### Solar Pro 4 / Solar Chat 즉시

수치와 효과는 유지했다. '비무장 공격', '포인트 보너스'는 용어 통일이 필요하다.

```text
스톤핸즈의 돌 손. 비무장 공격과 화술에 10 포인트 보너스, 곡괭이류 피해 두 배.
```

[원본 응답](runs/solar-pro4-web-instant/pilot-07.txt)

## L05062 · MISC:FULL

```text
Sea Creature Fossil
```

### Gemini 3.8 Flash / Low

의미를 유지했다.

```text
바다 생물 화석
```

[원본 응답](runs/gemini-3.8-flash-low/pilot-07.txt)

### Gemini 3.1 Flash Lite / Minimal

의미를 유지했다.

```text
해양 생물 화석
```

[원본 응답](runs/gemini-3.1-flash-lite-minimal/pilot-07.txt)

### Solar Pro 3 / Low

반복된 비정상 JSON 안에서 전부 영어로 남겼다.

[원본 응답](runs/solar-pro3-low/pilot-07.txt)

### Solar Pro 4 / Solar Chat 즉시

의미를 유지했다.

```text
해양 생물 화석
```

[원본 응답](runs/solar-pro4-web-instant/pilot-07.txt)

## L06028 · WEAP:FULL

```text
The Tonal Staff of Karagas (replica)
```

### Gemini 3.8 Flash / Low

의미를 유지했다. Tonal의 최종 게임 용어는 별도 합의가 필요하다.

```text
카라가스의 음조 지팡이 (복제품)
```

[원본 응답](runs/gemini-3.8-flash-low/pilot-07.txt)

### Gemini 3.1 Flash Lite / Minimal

의미를 유지했다. Tonal의 최종 게임 용어는 별도 합의가 필요하다.

```text
카라가스의 음조 지팡이 (복제품)
```

[원본 응답](runs/gemini-3.1-flash-lite-minimal/pilot-07.txt)

### Solar Pro 3 / Low

반복된 비정상 JSON 안에서 고유명사뿐 아니라 일반 명사·replica도 영어로 남겼다.

[원본 응답](runs/solar-pro3-low/pilot-07.txt)

### Solar Pro 4 / Solar Chat 즉시

많은 부분을 음역했다. 의미를 잃었다고 단정하기보다 용어 통일 대상으로 본다.

```text
카라가스 톤널 스태프 (복제품)
```

[원본 응답](runs/solar-pro4-web-instant/pilot-07.txt)

## L08510 · QUST:NNAM

```text
Reconstruct a <Alias=Artifact3>
```

### Gemini 3.8 Flash / Low

목표와 alias를 유지했다.

```text
<Alias=Artifact3> 복원하기
```

[원본 응답](runs/gemini-3.8-flash-low/pilot-07.txt)

### Gemini 3.1 Flash Lite / Minimal

목표와 alias를 유지했다.

```text
<Alias=Artifact3> 복원하기
```

[원본 응답](runs/gemini-3.1-flash-lite-minimal/pilot-07.txt)

### Solar Pro 3 / Low

반복된 비정상 JSON 안의 alias는 유지했다. 게임 목표로는 '복원'이 더 자연스럽다.

[원본 응답](runs/solar-pro3-low/pilot-07.txt)

### Solar Pro 4 / Solar Chat 즉시

alias는 유지했다. 게임 목표로는 '복원'이 더 자연스럽다.

```text
<Alias=Artifact3> 재구성
```

[원본 응답](runs/solar-pro4-web-instant/pilot-07.txt)

## L08319 · QUST:NNAM

```text
Return to Argus
```

### Gemini 3.8 Flash / Low

돌아갈 대상과 목표를 유지했다. 이름 음역은 용어집 확정 필요.

```text
아르거스에게 돌아가기
```

[원본 응답](runs/gemini-3.8-flash-low/pilot-07.txt)

### Gemini 3.1 Flash Lite / Minimal

돌아갈 대상과 목표를 유지했다. 이름 음역은 용어집 확정 필요.

```text
아르구스에게 돌아가기
```

[원본 응답](runs/gemini-3.1-flash-lite-minimal/pilot-07.txt)

### Solar Pro 3 / Low

반복된 비정상 JSON 안에서 Argus를 영어로 남겼다.

[원본 응답](runs/solar-pro3-low/pilot-07.txt)

### Solar Pro 4 / Solar Chat 즉시

목표 의미는 유지했다. 이름 음역은 용어집 확정 필요.

```text
아거스에게 복귀
```

[원본 응답](runs/solar-pro4-web-instant/pilot-07.txt)

## L08205 · QUST:NNAM

```text
Find Schneider's Sextant
```

### Gemini 3.8 Flash / Low

목표 의미를 유지했다.

```text
슈나이더의 육분의 찾기
```

[원본 응답](runs/gemini-3.8-flash-low/pilot-08.txt)

### Gemini 3.1 Flash Lite / Minimal

목표 의미를 유지했다.

```text
슈나이더의 육분의 찾기
```

[원본 응답](runs/gemini-3.1-flash-lite-minimal/pilot-08.txt)

### Solar Pro 3 / Low

Schneider를 '스키드너'로 옮겨 이름 음역이 흔들린다.

```text
스키드너의 육분의 찾기
```

[원본 응답](runs/solar-pro3-low/pilot-08.txt)

### Solar Pro 4 / Solar Chat 즉시

목표 의미를 유지했다.

```text
슈나이더의 육분의 찾기
```

[원본 응답](runs/solar-pro4-web-instant/pilot-08.txt)

## L08173 · QUST:NNAM

```text
Find the Nordic Runestone
```

### Gemini 3.8 Flash / Low

목표 의미는 유지했다. Nordic/Runestone 표준 용어 확인 필요.

```text
노드 룬스톤 찾기
```

[원본 응답](runs/gemini-3.8-flash-low/pilot-08.txt)

### Gemini 3.1 Flash Lite / Minimal

목표 의미를 유지했다.

```text
노르드 룬석 찾기
```

[원본 응답](runs/gemini-3.1-flash-lite-minimal/pilot-08.txt)

### Solar Pro 3 / Low

목표 의미를 유지했다. Nordic 음역은 용어 통일 필요.

```text
노르딕 룬석 찾기
```

[원본 응답](runs/solar-pro3-low/pilot-08.txt)

### Solar Pro 4 / Solar Chat 즉시

'런estone'에 영어 조각이 남았다.

```text
노드 런estone 찾기
```

[원본 응답](runs/solar-pro4-web-instant/pilot-08.txt)
