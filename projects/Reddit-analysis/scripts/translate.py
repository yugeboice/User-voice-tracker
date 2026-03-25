"""
Reddit Competitive Intelligence - Report Translator

Post-processing script that reads Chinese report JSON and adds English
translations for all text fields using LLM API. Also generates zh/en
markdown analysis reports.

Usage:
    python translate.py                          # Translate latest report
    python translate.py --file report_xxx.json   # Translate specific report
    python translate.py --all                    # Translate all reports
    python translate.py --llm-endpoint http://localhost:4141
"""

import argparse
import json
import logging
import re
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

import requests

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

DB_DIR = Path(__file__).resolve().parent.parent / "data"
REPORTS_DIR = DB_DIR / "reports"

LLM_ENDPOINT = "http://localhost:4141"
LLM_MODEL = "gpt-4"
LLM_TIMEOUT = 180

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
    datefmt="%H:%M:%S",
)
log = logging.getLogger(__name__)

# Product order for markdown report
PRODUCT_ORDER = ["M365 Copilot", "ChatGPT", "Gemini", "Claude", "GitHub Copilot"]


# ---------------------------------------------------------------------------
# LLM helpers (same pattern as analyze.py)
# ---------------------------------------------------------------------------

def call_llm(
    prompt: str,
    system_prompt: str = "",
    endpoint: str = LLM_ENDPOINT,
    model: str = LLM_MODEL,
    max_retries: int = 5,
) -> str:
    """Call the LLM API (OpenAI-compatible) and return the response text."""
    messages = []
    if system_prompt:
        messages.append({"role": "system", "content": system_prompt})
    messages.append({"role": "user", "content": prompt})

    for attempt in range(max_retries):
        try:
            resp = requests.post(
                f"{endpoint}/v1/chat/completions",
                json={
                    "model": model,
                    "messages": messages,
                    "temperature": 0.2,
                    "max_tokens": 4096,
                },
                timeout=LLM_TIMEOUT,
            )
            resp.raise_for_status()
            data = resp.json()
            return data["choices"][0]["message"]["content"]
        except (requests.exceptions.ConnectionError, ConnectionResetError) as e:
            wait = 30 * (2 ** attempt)
            log.warning("LLM connection failed (attempt %d/%d): %s", attempt + 1, max_retries, e)
            log.info("Waiting %ds for API to recover...", wait)
            time.sleep(wait)
        except Exception as e:
            log.error("LLM call failed: %s", e)
            return ""

    log.error("LLM call failed after %d retries", max_retries)
    return ""


def parse_json_response(text: str) -> list | dict | None:
    """Extract JSON from LLM response (handles markdown code blocks)."""
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        pass

    match = re.search(r"```(?:json)?\s*\n?(.*?)\n?```", text, re.DOTALL)
    if match:
        try:
            return json.loads(match.group(1))
        except json.JSONDecodeError:
            pass

    for start_char, end_char in [("[", "]"), ("{", "}")]:
        start = text.find(start_char)
        end = text.rfind(end_char)
        if start != -1 and end != -1 and end > start:
            try:
                return json.loads(text[start : end + 1])
            except json.JSONDecodeError:
                pass

    log.warning("Failed to parse JSON from LLM response")
    return None


# ---------------------------------------------------------------------------
# Translation functions
# ---------------------------------------------------------------------------

SYSTEM_PROMPT_JSON = (
    "You are a JSON-only API. Output ONLY valid JSON with no markdown, "
    "no code fences, no explanation. Start your response with { or [ as appropriate."
)


def translate_markdown(text: str, endpoint: str, model: str) -> str:
    """Translate a Chinese markdown document to English."""
    if not text or not text.strip():
        return ""

    prompt = f"""You are a professional translator specializing in technology and competitive intelligence.
Translate the following Chinese markdown to English.

Rules:
- Preserve ALL markdown formatting (headers ##, bold **, lists -, tables |)
- Do NOT translate product names: ChatGPT, Claude, Gemini, GitHub Copilot, M365 Copilot, Copilot
- Do NOT translate subreddit names (r/xxx)
- Keep numbers, percentages, and dates unchanged
- Translate section headers naturally (e.g. "整体社区情感" → "Overall Community Sentiment")
- Maintain the same professional, analytical tone
- Output the translated markdown directly, no wrapping

Chinese markdown:
{text}"""

    result = call_llm(prompt, endpoint=endpoint, model=model)
    return result.strip() if result else ""


def translate_structured_fields(product: dict, endpoint: str, model: str) -> dict:
    """Translate structured fields (pain_points, strengths, recommendations, keywords) for one product."""
    # Build a compact JSON with all fields to translate in one call
    to_translate = {
        "pain_points": [
            {"text": pp.get("text", ""), "detail": pp.get("detail", "")}
            for pp in product.get("pain_points", [])
        ],
        "strengths": [
            {"text": s.get("text", ""), "detail": s.get("detail", "")}
            for s in product.get("strengths", [])
        ],
        "recommendations": [
            {"text": r.get("text", "")}
            for r in product.get("recommendations", [])
        ],
        "keywords": product.get("keywords", []),
    }

    # Skip if nothing to translate
    if not any([to_translate["pain_points"], to_translate["strengths"],
                to_translate["recommendations"], to_translate["keywords"]]):
        return {}

    prompt = f"""Translate ALL Chinese string values in this JSON to English.
Keep the JSON structure exactly the same. Only translate string values.
Do NOT translate product names (ChatGPT, Claude, Gemini, GitHub Copilot, M365 Copilot).
Keep it concise and professional.

{json.dumps(to_translate, ensure_ascii=False, indent=2)}"""

    result = call_llm(prompt, system_prompt=SYSTEM_PROMPT_JSON, endpoint=endpoint, model=model)
    parsed = parse_json_response(result)
    return parsed if isinstance(parsed, dict) else {}


def translate_posts(posts: list, endpoint: str, model: str) -> list:
    """Translate typical_posts fields (sentiment_reason, key_points) for one product."""
    if not posts:
        return []

    to_translate = []
    for p in posts:
        to_translate.append({
            "id": p.get("id", ""),
            "sentiment_reason": p.get("sentiment_reason", ""),
            "key_points": p.get("key_points", []),
        })

    prompt = f"""Translate ALL Chinese string values in this JSON array to English.
Keep the JSON structure exactly the same. Only translate string values.
Do NOT translate product names. Keep translations concise.

{json.dumps(to_translate, ensure_ascii=False, indent=2)}"""

    result = call_llm(prompt, system_prompt=SYSTEM_PROMPT_JSON, endpoint=endpoint, model=model)
    parsed = parse_json_response(result)
    return parsed if isinstance(parsed, list) else []


def extract_keywords(
    product_name: str, summary_zh: str, summary_en: str,
    endpoint: str, model: str,
) -> dict | None:
    """Extract bilingual evaluative keywords from product summaries via LLM.

    Returns {"zh": ["kw1", ...], "en": ["kw1", ...]}
    """
    prompt = f"""Analyze the following product intelligence summary for "{product_name}" and extract evaluative keywords/phrases for a word cloud visualization.

Extract 15-20 keywords in BOTH Chinese and English. Focus on:
- Pain points and complaints (e.g., 幻觉问题/Hallucination, 响应慢/Slow Response)
- Strengths and praised features (e.g., 代码能力强/Strong Coding, 推理出色/Great Reasoning)
- Feature requests and user needs (e.g., 功能缺失/Missing Features)
- Sentiment keywords (e.g., 失望/Disappointed, 印象深刻/Impressive)
- Product-specific technical terms (e.g., Agent模式/Agent Mode, MCP集成/MCP Integration)

Rules:
- Each keyword should be 2-6 Chinese characters OR 1-3 English words
- Keywords must be evaluative or descriptive, NOT generic (avoid: 用户/users, 产品/product, 功能/feature)
- Chinese and English lists should correspond 1:1 (same meaning, same order)
- More negative/positive sentiment words, fewer neutral descriptions

Chinese summary:
{summary_zh[:2000]}

English summary:
{summary_en[:2000]}

Return JSON: {{"zh": ["Chinese keyword1", "Chinese keyword2", ...], "en": ["English keyword1", "English keyword2", ...]}}"""

    response = call_llm(prompt, SYSTEM_PROMPT_JSON, endpoint, model)
    if not response:
        return None
    result = parse_json_response(response)
    if isinstance(result, dict) and "zh" in result and "en" in result:
        # Ensure lists are same length
        min_len = min(len(result["zh"]), len(result["en"]))
        result["zh"] = result["zh"][:min_len]
        result["en"] = result["en"][:min_len]
        return result
    return None


# ---------------------------------------------------------------------------
# Main translation pipeline
# ---------------------------------------------------------------------------

def is_already_translated(report_data: dict) -> bool:
    """Check if report already has all _en fields including keywords."""
    if not report_data.get("cross_product_comparison_en"):
        return False
    products = report_data.get("products", [])
    if products and not products[0].get("summary_en"):
        return False
    # Also check keywords
    if products and (not products[0].get("keywords") or len(products[0].get("keywords", [])) < 5):
        return False
    return True


def translate_report(report_path: Path, endpoint: str, model: str, force: bool = False) -> bool:
    """Translate a single report JSON file, adding _en fields in-place."""
    log.info("=" * 60)
    log.info("Translating: %s", report_path.name)
    log.info("=" * 60)

    data = json.loads(report_path.read_text(encoding="utf-8"))

    if is_already_translated(data) and not force:
        log.info("Already translated, skipping. Use --force to re-translate.")
        return True

    products = data.get("products", [])
    llm_calls = 0

    # 1. Translate cross_product_comparison (1 LLM call)
    cpc = data.get("cross_product_comparison", "")
    if cpc and (not data.get("cross_product_comparison_en") or force):
        log.info("[1/5] Translating cross-product comparison...")
        data["cross_product_comparison_en"] = translate_markdown(cpc, endpoint, model)
        llm_calls += 1
    elif cpc:
        log.info("[1/5] Cross-product comparison: already translated, skipping.")
    else:
        data["cross_product_comparison_en"] = ""

    # 2. Translate per-product summaries (1 LLM call per product)
    log.info("[2/5] Translating product summaries (%d products)...", len(products))
    for i, p in enumerate(products):
        name = p.get("product_name", f"Product {i}")
        summary = p.get("summary", "")
        if summary and (not p.get("summary_en") or force):
            log.info("  Summary: %s (%d chars)", name, len(summary))
            p["summary_en"] = translate_markdown(summary, endpoint, model)
            llm_calls += 1
        elif summary:
            log.info("  Summary: %s (already done, skipping)", name)
        else:
            p["summary_en"] = ""

    # 3. Translate structured fields per product (1 LLM call per product)
    log.info("[3/5] Translating structured fields...")
    for i, p in enumerate(products):
        name = p.get("product_name", f"Product {i}")
        has_structured = any([p.get("pain_points"), p.get("strengths"),
                              p.get("recommendations")])
        if not has_structured:
            log.info("  Structured: %s (no fields, skipping)", name)
            continue
        already = p.get("pain_points") and p["pain_points"][0].get("text_en")
        if already and not force:
            log.info("  Structured: %s (already done, skipping)", name)
            continue
        log.info("  Structured: %s", name)
        translated = translate_structured_fields(p, endpoint, model)
        llm_calls += 1

        # Apply translations back to the product
        if translated.get("pain_points"):
            for j, pp in enumerate(p.get("pain_points", [])):
                if j < len(translated["pain_points"]):
                    pp["text_en"] = translated["pain_points"][j].get("text", "")
                    pp["detail_en"] = translated["pain_points"][j].get("detail", "")

        if translated.get("strengths"):
            for j, s in enumerate(p.get("strengths", [])):
                if j < len(translated["strengths"]):
                    s["text_en"] = translated["strengths"][j].get("text", "")
                    s["detail_en"] = translated["strengths"][j].get("detail", "")

        if translated.get("recommendations"):
            for j, r in enumerate(p.get("recommendations", [])):
                if j < len(translated["recommendations"]):
                    r["text_en"] = translated["recommendations"][j].get("text", "")

        if translated.get("keywords"):
            p["keywords_en"] = translated["keywords"]

    # 4. Translate typical_posts per product (1 LLM call per product)
    log.info("[4/5] Translating typical posts...")
    for i, p in enumerate(products):
        name = p.get("product_name", f"Product {i}")
        posts = p.get("typical_posts", [])
        if not posts:
            continue
        if posts[0].get("key_points_en") and not force:
            log.info("  Posts: %s (already done, skipping)", name)
            continue
        log.info("  Posts: %s (%d posts)", name, len(posts))
        translated_posts = translate_posts(posts, endpoint, model)
        llm_calls += 1

        # Apply translations back
        for j, post in enumerate(posts):
            if j < len(translated_posts):
                tp = translated_posts[j]
                post["sentiment_reason_en"] = tp.get("sentiment_reason", "")
                post["key_points_en"] = tp.get("key_points", [])

    # 5. Extract bilingual keywords per product via LLM (1 call per product)
    log.info("[5/5] Extracting bilingual keywords...")
    for i, p in enumerate(products):
        name = p.get("product_name", f"Product {i}")
        # Skip if keywords already populated (unless force)
        if p.get("keywords") and len(p["keywords"]) >= 5 and not force:
            log.info("  Keywords: %s (already has %d, skipping)", name, len(p["keywords"]))
            continue
        summary_zh = p.get("summary", "")
        summary_en = p.get("summary_en", "")
        if not summary_zh and not summary_en:
            log.info("  Keywords: %s (no summary, skipping)", name)
            continue
        log.info("  Keywords: %s", name)
        kw_result = extract_keywords(name, summary_zh, summary_en, endpoint, model)
        llm_calls += 1
        if kw_result:
            p["keywords"] = kw_result.get("zh", [])
            p["keywords_en"] = kw_result.get("en", [])
            log.info("    -> %d zh, %d en keywords", len(p["keywords"]), len(p["keywords_en"]))

    # Save updated JSON
    report_path.write_text(
        json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8"
    )
    log.info("Saved translated report: %s (%d LLM calls)", report_path.name, llm_calls)

    # Also update latest.json if this is the latest report
    latest_path = REPORTS_DIR / "latest.json"
    if latest_path.exists():
        latest_data = json.loads(latest_path.read_text(encoding="utf-8"))
        if latest_data.get("run_id") == data.get("run_id"):
            latest_path.write_text(
                json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8"
            )
            log.info("Also updated latest.json")

    # Generate markdown reports
    _generate_markdown_reports(data, report_path)

    return True


# ---------------------------------------------------------------------------
# Markdown report generation
# ---------------------------------------------------------------------------

def _generate_markdown_reports(data: dict, report_path: Path):
    """Generate Chinese and English markdown analysis reports."""
    period = data.get("period", {})
    period_str = f"{period.get('start', '')[:10]} ~ {period.get('end', '')[:10]}"
    products = data.get("products", [])

    # Sort products by PRODUCT_ORDER
    def sort_key(p):
        name = p.get("product_name", "")
        try:
            return PRODUCT_ORDER.index(name)
        except ValueError:
            return 99

    sorted_products = sorted(products, key=sort_key)

    # Chinese report
    zh_lines = [
        f"# AI Voice Tracker - 竞品情报分析报告",
        f"**分析周期: {period_str}**",
        "",
        "---",
        "",
    ]
    for p in sorted_products:
        name = p.get("product_name", "Unknown")
        summary = p.get("summary", "")
        if summary:
            # Summary already has its own # header, strip it and use ## instead
            cleaned = re.sub(r'^#\s+.*?\n', '', summary, count=1).strip()
            zh_lines.append(f"## {name}")
            zh_lines.append("")
            zh_lines.append(cleaned)
            zh_lines.append("")
            zh_lines.append("---")
            zh_lines.append("")

    cpc = data.get("cross_product_comparison", "")
    if cpc:
        zh_lines.append("## 跨产品对比分析")
        zh_lines.append("")
        cleaned_cpc = re.sub(r'^#\s+.*?\n', '', cpc, count=1).strip()
        zh_lines.append(cleaned_cpc)
        zh_lines.append("")

    zh_path = report_path.with_name(report_path.stem + "_analysis_zh.md")
    zh_path.write_text("\n".join(zh_lines), encoding="utf-8")
    log.info("Generated Chinese report: %s", zh_path.name)

    # English report
    en_lines = [
        f"# AI Voice Tracker - Competitive Intelligence Report",
        f"**Analysis Period: {period_str}**",
        "",
        "---",
        "",
    ]
    for p in sorted_products:
        name = p.get("product_name", "Unknown")
        summary_en = p.get("summary_en", "")
        if summary_en:
            cleaned = re.sub(r'^#\s+.*?\n', '', summary_en, count=1).strip()
            en_lines.append(f"## {name}")
            en_lines.append("")
            en_lines.append(cleaned)
            en_lines.append("")
            en_lines.append("---")
            en_lines.append("")

    cpc_en = data.get("cross_product_comparison_en", "")
    if cpc_en:
        en_lines.append("## Cross-Product Comparison")
        en_lines.append("")
        cleaned_cpc_en = re.sub(r'^#\s+.*?\n', '', cpc_en, count=1).strip()
        en_lines.append(cleaned_cpc_en)
        en_lines.append("")

    en_path = report_path.with_name(report_path.stem + "_analysis_en.md")
    en_path.write_text("\n".join(en_lines), encoding="utf-8")
    log.info("Generated English report: %s", en_path.name)


# ---------------------------------------------------------------------------
# Report discovery
# ---------------------------------------------------------------------------

def get_latest_report() -> Path | None:
    """Find the most recent report file."""
    if not REPORTS_DIR.exists():
        return None
    files = list(REPORTS_DIR.glob("report_*.json"))
    # Filter out analysis md files
    files = [f for f in files if not f.stem.endswith("_analysis_zh") and not f.stem.endswith("_analysis_en")]
    # Sort by file modification time (most recent first) since filenames use random hash prefixes
    files.sort(key=lambda f: f.stat().st_mtime, reverse=True)
    return files[0] if files else None


def get_all_reports() -> list[Path]:
    """Get all report JSON files."""
    if not REPORTS_DIR.exists():
        return []
    files = sorted(REPORTS_DIR.glob("report_*.json"))
    return [f for f in files if f.suffix == ".json"
            and not f.stem.endswith("_analysis_zh")
            and not f.stem.endswith("_analysis_en")]


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def main():
    parser = argparse.ArgumentParser(description="Translate report JSON to English")
    parser.add_argument("--file", help="Specific report file to translate")
    parser.add_argument("--all", action="store_true", help="Translate all reports")
    parser.add_argument("--force", action="store_true", help="Re-translate even if already done")
    parser.add_argument("--llm-endpoint", default=LLM_ENDPOINT)
    parser.add_argument("--model", default=LLM_MODEL)
    args = parser.parse_args()

    if args.file:
        path = REPORTS_DIR / args.file
        if not path.exists():
            log.error("File not found: %s", path)
            sys.exit(1)
        translate_report(path, args.llm_endpoint, args.model, force=args.force)

    elif args.all:
        reports = get_all_reports()
        if not reports:
            log.error("No reports found in %s", REPORTS_DIR)
            sys.exit(1)
        log.info("Found %d reports to translate", len(reports))
        for rp in reports:
            translate_report(rp, args.llm_endpoint, args.model, force=args.force)

    else:
        # Translate latest report
        latest = get_latest_report()
        if not latest:
            log.error("No reports found in %s", REPORTS_DIR)
            sys.exit(1)
        translate_report(latest, args.llm_endpoint, args.model, force=args.force)

    log.info("Translation complete!")


if __name__ == "__main__":
    main()
