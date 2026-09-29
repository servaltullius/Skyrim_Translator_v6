#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sys
import time
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable

import requests


PLACEHOLDER_RE = re.compile(
    r"(\r\n|\r|\n|[+-]?<[^>]+>|\[pagebreak\]|%[-0-9.]*[A-Za-z])",
    flags=re.IGNORECASE,
)


class TranslationError(RuntimeError):
    pass


class GeminiError(RuntimeError):
    pass


def _normalize_for_compare(text: str | None) -> str:
    return (text or "").strip()


def _cache_key(*, model: str, src_lang: str, dst_lang: str, source_text: str) -> str:
    h = hashlib.sha256()
    h.update(model.encode("utf-8"))
    h.update(b"\0")
    h.update(src_lang.encode("utf-8"))
    h.update(b"\0")
    h.update(dst_lang.encode("utf-8"))
    h.update(b"\0")
    h.update(source_text.encode("utf-8"))
    return h.hexdigest()


def mask_placeholders(text: str) -> tuple[str, dict[str, str]]:
    placeholder_map: dict[str, str] = {}

    def repl(match: re.Match[str]) -> str:
        idx = len(placeholder_map)
        if idx >= 9999:
            raise TranslationError("Too many placeholders in a single string (>= 9999).")

        original = match.group(0)
        label = _semantic_label_for_placeholder(original)
        marker = f"__XT_PH_{label}_{idx:04d}__" if label else f"__XT_PH_{idx:04d}__"
        placeholder_map[marker] = original
        return marker

    return PLACEHOLDER_RE.sub(repl, text), placeholder_map


def unmask_placeholders(text: str, placeholder_map: dict[str, str]) -> str:
    for marker, original in placeholder_map.items():
        if marker not in text:
            raise TranslationError(f"Missing placeholder marker in translation: {marker} (for {original!r})")

    out = text
    for marker, original in placeholder_map.items():
        out = out.replace(marker, original)
    return out


def _semantic_label_for_placeholder(placeholder: str) -> str | None:
    if not placeholder:
        return None

    s = placeholder
    if s[0] in "+-":
        s = s[1:]

    if len(s) < 3 or not (s.startswith("<") and s.endswith(">")):
        return None

    inner = s[1:-1].strip()
    if inner.lower() == "mag":
        return "MAG"
    if inner.lower() == "dur":
        return "DUR"
    if inner.isdigit():
        return "NUM"

    return None


def _count_line_breaks(text: str) -> int:
    return len(re.findall(r"\r\n|\r|\n", text))


def parse_model_json(text: str) -> Any:
    raw = text.strip()
    if raw.startswith("```"):
        raw = re.sub(r"^```[a-zA-Z]*\n", "", raw)
        raw = re.sub(r"\n```$", "", raw)
        raw = raw.strip()
    try:
        return json.loads(raw)
    except json.JSONDecodeError:
        match = re.search(r"(\{.*\}|\[.*\])", raw, flags=re.S)
        if match:
            return json.loads(match.group(1))
        raise


def read_xml_prolog(path: Path) -> tuple[bytes, bytes]:
    data = path.read_bytes()[:2048]
    bom = b"\xef\xbb\xbf" if data.startswith(b"\xef\xbb\xbf") else b""
    data_wo_bom = data[len(bom) :]
    first_line = data_wo_bom.splitlines(keepends=True)[:1]
    if first_line and first_line[0].lstrip().startswith(b"<?xml"):
        return bom, first_line[0].rstrip(b"\r\n")
    return bom, b'<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'


def write_xml(path: Path, root: ET.Element, *, bom: bytes, prolog: bytes) -> None:
    xml_body = ET.tostring(root, encoding="utf-8")
    with path.open("wb") as f:
        if bom:
            f.write(bom)
        f.write(prolog + b"\n")
        f.write(xml_body)


@dataclass
class Cache:
    path: Path
    items: dict[str, str]

    @classmethod
    def load(cls, path: Path) -> "Cache":
        items: dict[str, str] = {}
        if not path.exists():
            return cls(path=path, items=items)
        with path.open("r", encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue
                try:
                    obj = json.loads(line)
                except json.JSONDecodeError:
                    continue
                key = obj.get("key")
                dst = obj.get("dst")
                if isinstance(key, str) and isinstance(dst, str):
                    items[key] = dst
        return cls(path=path, items=items)

    def append(self, *, key: str, dst: str) -> None:
        self.items[key] = dst
        record = {"key": key, "dst": dst}
        with self.path.open("a", encoding="utf-8") as f:
            f.write(json.dumps(record, ensure_ascii=False) + "\n")


class GeminiClient:
    def __init__(
        self,
        *,
        api_key: str,
        model: str,
        timeout_s: float = 60.0,
        base_url: str = "https://generativelanguage.googleapis.com/v1beta",
    ) -> None:
        self._timeout_s = timeout_s
        self._session = requests.Session()
        self._url = f"{base_url}/models/{model}:generateContent?key={api_key}"

    def generate_text(self, *, prompt: str, temperature: float, max_output_tokens: int) -> str:
        payload = {
            "contents": [{"role": "user", "parts": [{"text": prompt}]}],
            "generationConfig": {
                "temperature": temperature,
                "maxOutputTokens": max_output_tokens,
                "responseMimeType": "application/json",
            },
            "safetySettings": [
                {"category": "HARM_CATEGORY_HARASSMENT", "threshold": "BLOCK_NONE"},
                {"category": "HARM_CATEGORY_HATE_SPEECH", "threshold": "BLOCK_NONE"},
                {"category": "HARM_CATEGORY_SEXUALLY_EXPLICIT", "threshold": "BLOCK_NONE"},
                {"category": "HARM_CATEGORY_DANGEROUS_CONTENT", "threshold": "BLOCK_NONE"},
            ],
        }
        resp = self._session.post(self._url, json=payload, timeout=self._timeout_s)
        if resp.status_code != 200:
            raise GeminiError(f"Gemini API error HTTP {resp.status_code}: {resp.text[:500]}")
        data = resp.json()
        try:
            return data["candidates"][0]["content"]["parts"][0]["text"]
        except Exception as e:  # noqa: BLE001
            raise GeminiError(f"Unexpected Gemini response shape: {json.dumps(data)[:500]}") from e


def build_batch_prompt(*, src_lang: str, dst_lang: str, items: list[dict[str, Any]]) -> str:
    input_json = {
        "source_language": src_lang,
        "target_language": dst_lang,
        "items": items,
    }
    return (
        "You are a professional game localization translator.\n"
        f"Translate from {src_lang} to {dst_lang}.\n\n"
        "Rules:\n"
        "- Preserve any tokens like __XT_PH_0000__, __XT_PH_MAG_0000__, __XT_PH_DUR_0001__, or __XT_PH_NUM_0002__ exactly (do not alter or remove).\n"
        "- The output MUST contain every token that appears in the input (same counts). Do not delete, merge, or duplicate tokens.\n"
        "- Do NOT output any raw markup like <p ...>, <img ...>, or [pagebreak]. These are represented by placeholder tokens.\n"
        "- Placeholder token hints: __XT_PH_MAG_####__ = magnitude/amount, __XT_PH_NUM_####__ = another numeric value (points/%/amount), __XT_PH_DUR_####__ = duration in seconds.\n"
        "- You MAY reorder numeric placeholder tokens (__XT_PH_MAG_####__, __XT_PH_NUM_####__, __XT_PH_DUR_####__) to create natural grammar, but do not reorder other tokens.\n"
        "- Do not add or remove line breaks; line breaks are represented as placeholder tokens.\n"
        "- Output ONLY valid JSON, no markdown/code fences, no explanations.\n\n"
        "Return JSON schema:\n"
        '{"translations":[{"id":0,"text":"..."}]}\n\n'
        "Input JSON:\n"
        + json.dumps(input_json, ensure_ascii=False)
    )


def translate_batch(
    *,
    client: GeminiClient,
    src_lang: str,
    dst_lang: str,
    batch: list[dict[str, Any]],
    temperature: float,
    max_output_tokens: int,
    retries: int,
) -> dict[int, str]:
    prompt = build_batch_prompt(src_lang=src_lang, dst_lang=dst_lang, items=batch)
    last_err: Exception | None = None
    for attempt in range(retries + 1):
        try:
            text = client.generate_text(
                prompt=prompt,
                temperature=temperature,
                max_output_tokens=max_output_tokens,
            )
            obj = parse_model_json(text)
            translations = obj.get("translations") if isinstance(obj, dict) else None
            if not isinstance(translations, list):
                raise TranslationError("Model output JSON missing 'translations' list.")

            out: dict[int, str] = {}
            for entry in translations:
                if not isinstance(entry, dict):
                    continue
                item_id = entry.get("id")
                t = entry.get("text")
                if isinstance(item_id, int) and isinstance(t, str):
                    out[item_id] = t

            if len(out) != len(batch):
                raise TranslationError(
                    f"Batch size mismatch: expected {len(batch)} translations, got {len(out)}."
                )
            return out
        except Exception as e:  # noqa: BLE001
            last_err = e
            if attempt < retries:
                sleep_s = min(30.0, 1.5**attempt)
                time.sleep(sleep_s)
                continue
            break

    if len(batch) <= 1:
        raise TranslationError(f"Failed to translate batch: {last_err}") from last_err

    mid = len(batch) // 2
    left = translate_batch(
        client=client,
        src_lang=src_lang,
        dst_lang=dst_lang,
        batch=batch[:mid],
        temperature=temperature,
        max_output_tokens=max_output_tokens,
        retries=retries,
    )
    right = translate_batch(
        client=client,
        src_lang=src_lang,
        dst_lang=dst_lang,
        batch=batch[mid:],
        temperature=temperature,
        max_output_tokens=max_output_tokens,
        retries=retries,
    )
    merged = dict(left)
    merged.update(right)
    return merged


def chunk_work(
    work: list[dict[str, Any]], *, batch_size: int, max_chars: int
) -> Iterable[list[dict[str, Any]]]:
    batch: list[dict[str, Any]] = []
    chars = 0
    for item in work:
        text_len = len(item["masked"])
        if batch and (len(batch) >= batch_size or chars + text_len > max_chars):
            yield batch
            batch = []
            chars = 0
        batch.append(item)
        chars += text_len
    if batch:
        yield batch


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(
        description="Translate xTranslator XML export using Gemini (Google AI Studio) API.",
    )
    parser.add_argument("--input", required=True, type=Path, help="Input xTranslator XML file")
    parser.add_argument("--output", type=Path, help="Output translated XML file")
    parser.add_argument("--model", default="gemini-2.5-flash-lite", help="Gemini model name")
    parser.add_argument(
        "--api-key",
        default=None,
        help="Gemini API key (or set GEMINI_API_KEY env var)",
    )
    parser.add_argument("--batch-size", type=int, default=20, help="Strings per API request")
    parser.add_argument("--max-chars", type=int, default=12000, help="Max characters per API request")
    parser.add_argument("--max-output-tokens", type=int, default=8192, help="Gemini max output tokens")
    parser.add_argument("--temperature", type=float, default=0.2, help="Gemini temperature")
    parser.add_argument("--retries", type=int, default=3, help="Retries per batch")
    parser.add_argument("--sleep", type=float, default=0.0, help="Sleep seconds between API requests")
    parser.add_argument("--limit", type=int, default=0, help="Translate only first N matched strings (0=all)")
    parser.add_argument("--overwrite", action="store_true", help="Overwrite existing non-empty Dest values")
    parser.add_argument("--dry-run", action="store_true", help="Parse and report, but do not call API or write output")
    parser.add_argument("--cache", type=Path, default=None, help="JSONL cache file path")
    args = parser.parse_args(argv)

    api_key = args.api_key or os.environ.get("GEMINI_API_KEY")
    if not api_key and not args.dry_run:
        print("Missing Gemini API key. Set GEMINI_API_KEY or pass --api-key.", file=sys.stderr)
        return 2

    output_path = args.output or args.input.with_suffix(args.input.suffix + ".translated.xml")
    cache_path = args.cache or args.input.with_suffix(args.input.suffix + ".gemini_cache.jsonl")

    bom, prolog = read_xml_prolog(args.input)
    tree = ET.parse(args.input)
    root = tree.getroot()

    src_lang = root.findtext("./Params/Source") or "english"
    dst_lang = root.findtext("./Params/Dest") or "korean"

    strings = root.findall("./Content/String")
    total = len(strings)

    cache = Cache.load(cache_path)

    work: list[dict[str, Any]] = []
    already = 0
    skipped = 0
    for idx, node in enumerate(strings):
        src_elem = node.find("Source")
        if src_elem is None:
            skipped += 1
            continue
        src_text = src_elem.text or ""
        if not src_text:
            skipped += 1
            continue

        dst_elem = node.find("Dest")
        if dst_elem is None:
            dst_elem = ET.SubElement(node, "Dest")
        dst_text = dst_elem.text or ""

        if not args.overwrite:
            dst_norm = _normalize_for_compare(dst_text)
            src_norm = _normalize_for_compare(src_text)
            if dst_norm and dst_norm != src_norm:
                skipped += 1
                continue

        key = _cache_key(model=args.model, src_lang=src_lang, dst_lang=dst_lang, source_text=src_text)
        cached = cache.items.get(key)
        if isinstance(cached, str):
            dst_elem.text = cached
            already += 1
            continue

        masked, placeholder_map = mask_placeholders(src_text)
        work.append(
            {
                "id": idx,
                "src": src_text,
                "dst_elem": dst_elem,
                "key": key,
                "masked": masked,
                "placeholders": placeholder_map,
            }
        )

        if args.limit and len(work) >= args.limit:
            break

    print(
        f"Loaded {args.input} ({total} strings). "
        f"To translate: {len(work)}. From cache: {already}. Skipped: {skipped}.",
        file=sys.stderr,
    )

    if args.dry_run:
        return 0

    client = GeminiClient(api_key=api_key, model=args.model)
    translated = 0
    for batch_items in chunk_work(work, batch_size=args.batch_size, max_chars=args.max_chars):
        payload_items = [{"id": it["id"], "text": it["masked"]} for it in batch_items]
        result = translate_batch(
            client=client,
            src_lang=src_lang,
            dst_lang=dst_lang,
            batch=payload_items,
            temperature=args.temperature,
            max_output_tokens=args.max_output_tokens,
            retries=args.retries,
        )
        for it in batch_items:
            raw_t = result[it["id"]]
            try:
                out_t = unmask_placeholders(raw_t, it["placeholders"])
            except TranslationError as e:
                raise TranslationError(f"Validation failed for string index {it['id']}: {e}") from e

            if _count_line_breaks(out_t) != _count_line_breaks(it["src"]):
                raise TranslationError(
                    f"Newline count mismatch for string index {it['id']}: "
                    f"src has {_count_line_breaks(it['src'])} but dst has {_count_line_breaks(out_t)}"
                )

            it["dst_elem"].text = out_t
            cache.append(key=it["key"], dst=out_t)
            translated += 1

        if args.sleep:
            time.sleep(args.sleep)

        if translated and translated % 100 == 0:
            print(f"Translated {translated}/{len(work)}...", file=sys.stderr)

    write_xml(output_path, root, bom=bom, prolog=prolog)
    print(f"Done. Wrote: {output_path}", file=sys.stderr)
    print(f"Cache: {cache_path}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
