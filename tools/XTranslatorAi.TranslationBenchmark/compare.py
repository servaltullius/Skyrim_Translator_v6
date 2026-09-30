"""Compare plugin field manifests without ever providing Korean baselines to the API runner.

Usage: python compare.py experiment-inputs.json OUTPUT-FOLDER
The configuration is an array of plugin objects with old_source, baseline,
optional latest_source and optional translated_rows (JSON paths).
"""
import collections
import json
import re
import sys
from pathlib import Path


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def identity(document, field):
    parts = field["Key"].split("/")
    if len(parts) != 5:
        raise ValueError(f"Unexpected field key: {field['Key']}")
    form_id = field["FormId"]
    slot = form_id >> 24
    masters = document["Info"]["Masters"]
    if slot < len(masters):
        owner = masters[slot]
    elif slot == len(masters):
        owner = Path(document["Info"]["InputPath"].replace("\\", "/")).name
    else:
        raise ValueError(f"Invalid source-local master index in {field['Key']}")
    # File-local master slots can move across versions. Match their actual owners.
    return (owner.casefold(), form_id & 0xFFFFFF, parts[0], parts[2], parts[3], parts[4])


def index(document):
    result = {}
    for field in document["Fields"]:
        key = identity(document, field)
        if key in result:
            raise ValueError(f"Ambiguous field identity: {key}")
        result[key] = field
    return result


def same_editor(a, b):
    return (a.get("EditorId") or "").casefold() == (b.get("EditorId") or "").casefold()


def visible(text):
    return re.sub(r"<[^>]*>|\[pagebreak\]|%[A-Za-z0-9_]+%|\{[^}]*\}", "", text or "")


def flags(source, translated):
    if translated is None:
        return []
    result = []
    if source == translated and re.search(r"[A-Za-z]", visible(source)):
        result.append("원문과 동일")
    if re.search(r"[A-Za-z]{3,}", visible(translated)):
        result.append("영문 포함")
    if not translated.strip() and source.strip():
        result.append("빈 번역")
    if "__XT_" in translated:
        result.append("보호 토큰 잔존")
    if "\ufffd" in translated:
        result.append("대체 문자 포함")
    # These are review candidates, not automatically proven mistranslations.
    return result


def compare(configuration):
    all_rows, summaries = [], []
    for plugin in configuration:
        old_document = read(plugin["old_source"])
        baseline_document = read(plugin["baseline"])
        old = index(old_document)
        baseline = index(baseline_document)
        latest_available = bool(plugin.get("latest_source"))
        latest_document = read(plugin["latest_source"]) if latest_available else old_document
        latest = index(latest_document)
        translated_rows = read(plugin["translated_rows"]) if plugin.get("translated_rows") else []
        translations = {r["Key"]: r for r in translated_rows}
        if len(translations) != len(translated_rows):
            raise ValueError("Ambiguous translation row keys.")
        if translations and not latest_available:
            raise ValueError("A latest source manifest is required to compare new translations.")
        rows = []
        for key in sorted(old.keys() | latest.keys()):
            original, current, reference = old.get(key), latest.get(key), baseline.get(key)
            if not latest_available:
                change = "최신 원본 대기"
            elif current is None:
                change = "삭제"
            elif original is None:
                change = "추가"
            elif not same_editor(original, current):
                change = "레코드 식별자 변경"
            elif original["SourceText"] != current["SourceText"]:
                change = "원문 변경"
            else:
                change = "원문 동일"
            if reference is not None and original is not None and not same_editor(reference, original):
                reference = None
            translated = translations.get(current["Key"]) if current else None
            if translated is not None and translated["SourceText"] != current["SourceText"]:
                raise ValueError("Translation rows belong to a different source.")
            dest = translated["DestText"] if translated and translated["Status"] in ("Done", "Edited") else None
            existing = reference["SourceText"] if reference else None
            field = current or original
            row = {
                "Plugin": plugin["name"], "Identity": list(key), "Key": field["Key"],
                "Rec": field["Rec"], "EditorId": field.get("EditorId"), "Change": change,
                "OldEnglish": original["SourceText"] if original else None,
                "LatestEnglish": current["SourceText"] if current and latest_available else None,
                "ExistingKorean": existing, "NewKorean": dest,
                "NewStatus": translated["Status"] if translated else "미실행",
                "QualityComparable": change == "원문 동일" and existing is not None and dest is not None,
                "ExistingReviewFlags": flags(original["SourceText"] if original else "", existing),
                "NewReviewFlags": flags(current["SourceText"] if current else "", dest),
            }
            rows.append(row)
        comparable = [r for r in rows if r["QualityComparable"]]
        summaries.append({
            "Plugin": plugin["name"], "LatestSourceAvailable": latest_available,
            "OldFields": len(old), "BaselineFields": len(baseline),
            "LatestFields": len(latest) if latest_available else None,
            "BaselineMatchedToOld": sum(k in baseline and same_editor(v, baseline[k]) for k, v in old.items()),
            "LatestSourceCharacters": sum(len(f["SourceText"]) for f in latest.values()) if latest_available else None,
            "OldSourceCharacters": sum(len(f["SourceText"]) for f in old.values()),
            "ChangeCounts": dict(collections.Counter(r["Change"] for r in rows)),
            "NewStatusCounts": dict(collections.Counter(r["NewStatus"] for r in rows if r["Change"] != "삭제")),
            "QualityComparable": len(comparable),
            "ComparableIdenticalTranslations": sum(r["ExistingKorean"] == r["NewKorean"] for r in comparable),
            "ComparableDifferentTranslations": sum(r["ExistingKorean"] != r["NewKorean"] for r in comparable),
            "OldSourceSha256": old_document["Info"]["Sha256"],
            "BaselineSha256": baseline_document["Info"]["Sha256"],
            "LatestSourceSha256": latest_document["Info"]["Sha256"] if latest_available else None,
        })
        all_rows.extend(rows)
    return {"Summaries": summaries, "Rows": all_rows,
            "Note": "Same English text is the comparison subset. Difference counts are not quality scores. Flags require human review; mod names and internal labels can legitimately remain English."}


HTML = """<!doctype html><html lang="ko"><meta charset="utf-8">
<title>엘든림 번역 비교</title><style>
body{font:15px/1.65 system-ui,sans-serif;color:#e6edf3;background:#111820;margin:24px}
h1{font-size:24px;margin-bottom:6px}p{color:#aebccd;max-width:1200px}
input,select,button{background:#202d3c;color:#e6edf3;border:1px solid #52657a;padding:9px;border-radius:5px;margin:3px}
input{width:380px}table{border-collapse:collapse;width:100%;table-layout:fixed;margin-top:12px}
td,th{border:1px solid #394b60;padding:10px;vertical-align:top;text-align:left;white-space:pre-wrap;overflow-wrap:anywhere}
th{background:#223349}td{font-size:14px}th:first-child{width:13%}.meta{color:#99b1cf;font-size:12px}
.waiting{color:#caa973}.difference{border-left:3px solid #deaa55}.summary{display:flex;flex-wrap:wrap;gap:10px}
.card{background:#1b2939;padding:12px;border-radius:6px;min-width:260px}strong{color:#9ccaff}
</style><h1>엘든림 기존 한글판 · Gemini 3.8 Flash 비교</h1>
<p>최신 영문 원본은 기존 번역·TM 없이 별도 프로젝트에서 번역합니다. 원문 동일 항목만 번역 비교에 포함하며, 추가·삭제·원문 변경은 별도로 표시합니다. 번역 문구가 다르다는 사실은 품질 개선 점수가 아닙니다. 영문 포함 표시도 검토 후보입니다.</p>
<div id="summary" class="summary"></div>
<p id="stage"></p><input id="search" placeholder="원문 / 번역 / EDID / FormID 검색">
<select id="plugin"><option value="">모든 플러그인</option></select>
<select id="filter"><option value="all">모든 항목</option><option value="comparable">원문 동일 · 양쪽 번역 있음</option><option value="different">원문 동일 · 번역 표현 다름</option><option value="version">버전 변경</option><option value="flags">검토 후보</option><option value="pending">새 번역 미완료</option></select>
<button id="prev">이전</button><button id="next">다음</button><span id="count"></span>
<table><thead><tr><th>항목</th><th>기존 영문 원본</th><th>최신 영문 원본</th><th>기존 한글판</th><th>새 3.8 Flash 번역</th></tr></thead><tbody id="rows"></tbody></table>
<script id="data" type="application/json">__DATA__</script><script>
const data=JSON.parse(document.getElementById('data').textContent);let page=0,selected=[];
const $=id=>document.getElementById(id);
for(const s of data.Summaries){const c=document.createElement('div');c.className='card';
c.textContent=s.Plugin+' · 기존 '+s.OldFields+'항목 / 최신 '+(s.LatestFields??'대기')+' · 새 번역 완료 '+(s.NewStatusCounts.Done??0)+' · 원문 동일 비교 '+s.QualityComparable;
$('summary').append(c);const o=document.createElement('option');o.value=s.Plugin;o.textContent=s.Plugin;$('plugin').append(o)}
const waiting=data.Summaries.filter(s=>!s.LatestSourceAvailable),unfinished=data.Summaries.filter(s=>s.LatestSourceAvailable&&Object.keys(s.NewStatusCounts).some(k=>!['Done','Edited'].includes(k)));
const done=data.Summaries.reduce((n,s)=>n+(s.NewStatusCounts.Done??0)+(s.NewStatusCounts.Edited??0),0);
$('stage').textContent='새 번역 완료 '+done+'개. '+(unfinished.length?'번역 미완료: '+unfinished.map(s=>s.Plugin).join(', ')+'. ':'')+(waiting.length?'최신 원본 대기: '+waiting.map(s=>s.Plugin).join(', ')+'. ':'')+'완료된 항목만 비교에 포함합니다. 게임 화면 확인은 별도입니다.';
function apply(){page=0;const q=$('search').value.toLowerCase(),p=$('plugin').value,f=$('filter').value;
selected=data.Rows.filter(r=>(!p||r.Plugin===p)&&(!q||[r.Key,r.EditorId,r.OldEnglish,r.LatestEnglish,r.ExistingKorean,r.NewKorean].some(x=>(x??'').toLowerCase().includes(q)))&&
(f==='all'||f==='comparable'&&r.QualityComparable||f==='different'&&r.QualityComparable&&r.ExistingKorean!==r.NewKorean||f==='version'&&['추가','삭제','원문 변경','레코드 식별자 변경'].includes(r.Change)||f==='flags'&&(r.ExistingReviewFlags.length||r.NewReviewFlags.length)||f==='pending'&&r.NewKorean===null&&r.Change!=='삭제'));render()}
function render(){const body=$('rows');body.replaceChildren();for(const r of selected.slice(page*100,(page+1)*100)){const tr=document.createElement('tr');
for(const [i,value] of [r.Plugin+'\\n'+r.Key+'\\n'+(r.EditorId??'')+'\\n'+r.Change+' / '+r.NewStatus,r.OldEnglish,r.LatestEnglish,r.ExistingKorean,r.NewKorean].entries()){
const td=document.createElement('td');td.textContent=value??(i===4?'번역 미실행 또는 미완료':'없음 / 대기');
if(i===0)td.className='meta';if(i===4&&value===null)td.className='waiting';if(i===4&&r.QualityComparable&&r.NewKorean!==r.ExistingKorean)td.classList.add('difference');tr.append(td)}body.append(tr)}
$('count').textContent=' '+selected.length+'개 · '+(page+1)+' / '+Math.max(1,Math.ceil(selected.length/100));$('prev').disabled=page===0;$('next').disabled=(page+1)*100>=selected.length}
for(const id of ['search','plugin','filter'])$(id).addEventListener(id==='search'?'input':'change',apply);
$('prev').onclick=()=>{page--;render()};$('next').onclick=()=>{page++;render()};apply();
</script></html>"""


def main():
    data = compare(read(sys.argv[1]))
    output = Path(sys.argv[2])
    output.mkdir(parents=True, exist_ok=True)
    (output / "comparison.json").write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
    embedded = json.dumps(data, ensure_ascii=False).replace("<", "\\u003c").replace(">", "\\u003e").replace("&", "\\u0026")
    (output / "comparison.html").write_text(HTML.replace("__DATA__", embedded), encoding="utf-8")
    print(json.dumps(data["Summaries"], ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
