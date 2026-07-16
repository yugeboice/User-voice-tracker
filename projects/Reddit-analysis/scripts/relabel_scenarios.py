"""
Relabel ONLY the scenario_tags field on an existing analysis run, using the
corrected prompt in analyze.py. Does NOT re-scrape and does NOT touch
sentiment / topic_category / key_points / is_valid / is_typical.

Why: audit (scenario_audit_20260608_174000) showed scenario_tags accuracy
was 42% overall and only 4% for code_interpreter. The new prompt adds a
mainline-chat scope rule + exclude list + tightened per-scenario definitions.

Usage (run from project root):
    ./scripts/venv/Scripts/python scripts/relabel_scenarios.py --run-id c0dfb8d8 --sample
    ./scripts/venv/Scripts/python scripts/relabel_scenarios.py --run-id c0dfb8d8 --commit
    ./scripts/venv/Scripts/python scripts/relabel_scenarios.py --run-id c0dfb8d8 --commit --limit 100
"""

from __future__ import annotations

import argparse
import json
import logging
import sqlite3
import sys
import time
from collections import Counter
from pathlib import Path

import requests

# Reuse parsing + taxonomy from analyze.py
sys.path.insert(0, str(Path(__file__).resolve().parent))
from analyze import (  # noqa: E402
    BATCH_SIZE,
    SCENARIO_TAXONOMY,
    parse_json_response,
)

# ---------------------------------------------------------------------------
# Config (hardcoded per main agent's instruction — endpoint 41891 verified OK)
# ---------------------------------------------------------------------------

PROJECT_ROOT = Path(__file__).resolve().parent.parent
DB_PATH = PROJECT_ROOT / "data" / "reddit.db"
AUDIT_DIR = PROJECT_ROOT / "scenario_audit_20260608_174000"
AUDIT_IN_PATH = AUDIT_DIR / "audit_data" / "sc_audit_in.json"
AUDIT_CI_PATH = AUDIT_DIR / "audit_data" / "ci_review_out.json"
BACKUP_PATH_TEMPLATE = str(AUDIT_DIR / "relabel_backup_{run_id}_v2.json")

# QA round-2 gold-standard (154 reviewed posts with correct/better labels)
GOLD_DIR = PROJECT_ROOT / "scenario_audit_20260625_155500"
GOLD_FINAL_PATH = GOLD_DIR / "audit_data" / "rerev_FINAL.json"

LLM_ENDPOINT = "http://127.0.0.1:41891"
LLM_MODEL = "gpt-4"
LLM_TIMEOUT = 300
LLM_TEMPERATURE = 0.1  # Lower than analyze.py's 0.2 for classification stability

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
    datefmt="%H:%M:%S",
)
log = logging.getLogger("relabel")


# ---------------------------------------------------------------------------
# LLM call — local copy so we can pin temperature without touching analyze.py
# ---------------------------------------------------------------------------

def call_llm_local(prompt: str, system_prompt: str = "", max_retries: int = 5) -> str:
    messages = []
    if system_prompt:
        messages.append({"role": "system", "content": system_prompt})
    messages.append({"role": "user", "content": prompt})

    for attempt in range(max_retries):
        try:
            resp = requests.post(
                f"{LLM_ENDPOINT}/v1/chat/completions",
                json={
                    "model": LLM_MODEL,
                    "messages": messages,
                    "temperature": LLM_TEMPERATURE,
                    "max_tokens": 4096,
                },
                timeout=LLM_TIMEOUT,
            )
            resp.raise_for_status()
            return resp.json()["choices"][0]["message"]["content"]
        except (requests.exceptions.ConnectionError, requests.exceptions.ReadTimeout, ConnectionResetError) as e:
            wait = 30 * (2 ** attempt)
            log.warning("LLM connection failed (attempt %d/%d): %s — waiting %ds",
                        attempt + 1, max_retries, e, wait)
            time.sleep(wait)
        except Exception as e:
            log.error("LLM call failed: %s", e)
            return ""

    log.error("LLM call failed after %d retries", max_retries)
    return ""


# ---------------------------------------------------------------------------
# Prompt — mirrors analyze.py scenario_tags section (sole task: relabel)
# ---------------------------------------------------------------------------

RELABEL_PROMPT_TEMPLATE = """You are an AI product competitive intelligence analyst.
Product: {product_name}

For each post, output ONLY the scenario_tags field: an array of 0 or more
applicable scenarios from [{taxonomy}].

===== CRITICAL SCOPE RULE — READ FIRST =====
Scenario tags describe USER EXPERIENCE WITH THE MAINLINE CHAT PRODUCT
(ChatGPT chat at chatgpt.com / Claude.ai chat / Gemini app or gemini.google.com).
Return [] when the post has NO actual in-chat usage (see EXCLUDE list).
Otherwise tag ONLY the SPECIFIC features the user actually used.
When in doubt about whether a feature was used -> DO NOT tag it.
general_purpose is for posts where in-chat usage IS described but no other specific
feature applies. It is NOT a default add-on tag and NOT a catch-all for any AI
discussion.

===== TAG SELECTION RULES =====
1. Multi-label IS allowed when the user genuinely used multiple distinct features.
   Examples:
   - Upload a CSV and ask the chat to summarize into a table -> [file_upload, office_file_creation]
   - Upload a screenshot and have a multi-turn back-and-forth -> [image_upload, multi_turn]
   - Use ChatGPT Search then chat about the results -> [general_purpose_search]
     (general_purpose is NOT added — the search is the activity)
   - Generate a chart in Code Interpreter from a CSV -> [file_upload, code_interpreter]
2. general_purpose is MUTUALLY EXCLUSIVE with other scenarios.
   - If ANY specific scenario (image_*, multi_turn, file_upload, code_interpreter,
     office_file_creation, general_purpose_search, voice_single_turn) applies,
     DO NOT add general_purpose on top.
   - Use general_purpose ONLY when the user clearly used the chat but no specific
     feature applies (general Q&A, casual chat, life advice, generic complaints
     about answer quality WITH USAGE DESCRIPTION).
3. multi_turn requires EXPLICIT memory / context-loss / multi-message language.
   A single complaint, a single question, a model preference debate, image gen
   issues, or "AI tool switching" musings are NOT multi_turn.
4. file_upload requires the user to UPLOAD A SPECIFIC FILE INTO THE CHAT.
   "Drive integration", "MCP connectors", "artifacts hosting", "Canvas broken"
   are NOT file_upload — they are integration/feature talk.
5. code_interpreter requires DIRECT EVIDENCE of in-chat code execution (output,
   chart, plot, "ran X", "executed"). Outcome stories like "wrote my thesis with
   Claude" or "built a website with ChatGPT" are NOT code_interpreter.

===== EXCLUDE — return [] when post has NO in-chat usage =====
- Standalone coding tools with no chat usage: Claude Code, Codex CLI, Cursor, aider,
  cline, antigravity, opencode, Continue, VS Code / JetBrains extensions, IDE plugins
- API / SDK usage, MCP server development, custom agents/apps the user built
- Self-promotion: "I built X with Claude Code", "Check out my prompt/template/library"
- Pure billing / quota / rate-limit / pricing / subscription complaints
- Pure news, leaks, version-bump announcements, model release posts
- Pure memes / jokes / cryptic one-liners / "What the hell?" with no scenario context
- Pure model rants / one-line opinions ("Opus 4.6 was peak", "And so it begins",
  "Screw You OpenAI", "Is it fixed?") with no description of any actual chat usage
- Pure model-version routing complaints without usage context
- Generic "AI tool switching" / "bouncing between AIs" musings with no concrete
  in-chat usage described
- Persona fiction / "A day as ChatGPT" creative writing about the AI
- Vague meta-discussion ("does vibe coding feel like X?") without describing usage
- "Best model for X?" comparison questions with NO description of actual chat usage

===== SCENARIO DEFINITIONS =====

- image_upload
  MUST: User uploaded/pasted/attached an image into the chat for the AI to analyze,
        describe, OCR, debug, identify, edit, restore, transform.
        Or post explicitly discusses deleting/managing UPLOADED images/files in chat.
  NOT: AI generated an image without an input image (-> image_creation only).
       Post is purely about generated image quality (-> image_creation only).

- image_creation
  MUST: User asked the chat to GENERATE an image (DALL-E, Imagen, native image gen,
        "make me a picture", "draw X", "create an image of Y").
        Or post complains about generated-image style / behavior of image gen.
  NOT: User uploaded an image but did NOT ask for a new one (-> image_upload only).

- multi_turn
  MUST: Post EXPLICITLY discusses one of:
        - Memory feature (ChatGPT Memory, Claude Projects memory, Gemini memory)
        - Custom instructions persisting across sessions
        - "Chat resetting", "losing context", "forgetting earlier messages",
          "context window full"
        - Long conversation breakdown / drift in same thread
        - User describes a concrete back-and-forth dialog with multiple turns
  NOT: One-off questions, single complaints, generic dissatisfaction, version posts,
       model-preference debates, image-gen complaints, tool-switching musings,
       censorship/sensitivity complaints, "losing ideas in OLD chats" (that is
       about searching old chats, not multi-turn), canvas / collaboration UI
       complaints. DO NOT add multi_turn just because the user said something
       negative about a model.

- code_interpreter
  MUST: Post contains DIRECT EVIDENCE the chat RAN CODE in the conversation:
        - Explicit names: "Advanced Data Analysis", "Code Interpreter",
          "Analysis tool", "Run code"
        - Output evidence: "it produced a chart/plot/visualization/graph",
          "interactive viz appeared", "Gemini visualizations"
        - Execution language: "ran Python in chat", "executed the code",
          "Claude ran analysis on my data file"
        - Music/media generated by running code in chat
  NOT: STRICTLY EXCLUDE (all -> NOT code_interpreter):
        - Claude Code / Codex CLI / Cursor / aider / any CLI / IDE / extension
        - API / SDK code generation, MCP server work, agent frameworks
        - "I used Claude for my thesis / SEO / website / coursework" (outcomes,
          not execution)
        - "Claude helped me build X" / "passed my thesis using Claude" (output
          only, no execution evidence)
        - Persona pieces / "A day as ChatGPT"
        - "Is it fixed?" status posts with no usage
        Rule: if the post only says the chat HELPED with code or the user got a
        code-related outcome but no execution evidence -> general_purpose (if any
        chat usage) or [] (if none).

- office_file_creation
  MUST: User asked the chat to PRODUCE a Word doc / Excel/Google Sheet / PowerPoint
        / PDF AS OUTPUT, AND there is evidence the chat actually attempted/produced one.
  NOT: Uploading an office file for analysis (-> file_upload only, unless the chat
       ALSO produced a new file). Generic writing assistance without an actual file
       artifact. "Canvas broken?" / Canvas UI / "side-by-side Canvas" UI complaints
       WITHOUT a specific creation request -> []. "Claude can't present HTML file" /
       "ChatGPT can't make a usable file" without a concrete file the user is trying
       to create is too vague -> [].

- file_upload
  MUST: User explicitly UPLOADED a PDF / doc / spreadsheet / text file / code file
        / screenshot of a doc INTO THE CHAT for AI to read, summarize, edit, or
        analyze. Concrete file or document must be mentioned.
  NOT: MCP / connector / Google Drive INTEGRATION talk without a specific upload.
       "Drive source docs slow" = integration, not upload. "Artifacts hosting"
       = external. Claude Code mentioning files = standalone tool. Building local
       multi-AI apps. Mere mention of an upload limit.

- general_purpose_search
  MUST: User used web search / browsing / live info lookup INSIDE THE CHAT,
        with EXPLICIT evidence the chat actually fetched live web information:
        - ChatGPT Search / Browse / "Search" tool usage explicitly mentioned
        - Claude web search, Gemini grounded search explicitly mentioned
        - Chat returned citations / links / web results in the conversation
        - "Asked GPT to search for X online and it returned 10 sources"
  NOT: Abstract "AI vs Google" musings. News about search features. Model
       comparison without search. "I looked up my name through chatgpt" without
       explicit search-mode mention is NOT search (could be from training data).
       "Wants something to track progress" / "give me info on X" is generic Q&A,
       not search. Be strict: when in doubt, do NOT tag search.

- general_purpose
  MUST (mutually exclusive — use ONLY when no other specific scenario applies):
        User describes ACTUAL in-chat usage of one of:
        - General Q&A, learning, asking for advice ("I asked Claude how to...")
        - Writing assistance, brainstorming, summarization (no file involved)
        - Casual conversation, emotional support, life-coaching, companionship
        - Generic answer-quality complaints WITH a usage description
          ("ChatGPT keeps refusing when I ask about X")
        - "How do you use ChatGPT for Y?" surveying community usage
  STRICT NOT — do NOT use general_purpose for:
        - Pure rants without usage ("Screw You OpenAI", "Gemini sucks")
        - Pure model-comparison shopping ("best model for studying", "Claude or
          ChatGPT?") without describing actual usage
        - Self-promotion of own builds / prompts / templates
        - Cryptic / promotional one-liners
        - Pure billing/news/version posts
        - Standalone-tool posts (Claude Code etc.)
        - Pure memes / "What the hell is that supposed to mean?"

- voice_single_turn
  MUST: Post mentions voice input/output, voice mode, speech-to-text, TTS,
        advanced voice, Whisper, "talking to ChatGPT", read-aloud feature
  NOT: No voice mention at all

===== DECISION PROCEDURE =====
Step 1: Is the post ENTIRELY about EXCLUDE list items with ZERO in-chat usage? -> []
Step 2: For each specific scenario, ask "is there DIRECT EVIDENCE the user used
        this feature in chat?" If YES, add it. If only HINTED or VAGUE, do NOT add.
Step 3: If at least one specific scenario from Step 2 applies, output JUST those
        specific scenarios (DO NOT add general_purpose).
Step 4: If NO specific scenario applies, ask "does the post describe actual
        in-chat usage (not just opinions, not just version talk, not just rants,
        not just self-promo)?" If YES -> [general_purpose]. If NO -> [].
Step 5: When uncertain between tagging and []: prefer the LESS aggressive choice
        (drop the tag). False positives hurt more than false negatives here.

===== QUICK SANITY CHECKS =====
- "I used Claude for my thesis / SEO / building a website" (no execution evidence)
  -> [general_purpose]   (NOT code_interpreter, NOT file_upload)
- "I built a budget gate for Claude Code" / "Check out my prompt template"
  -> []   (self-promo of standalone tool/prompt)
- "How do you use ChatGPT?" / "ChatGPT pisses me off when it refuses X"
  -> [general_purpose]
- "Gemini Pro shared quota?" / "Opus 4.8 limit?" / "Opus 4.6 was peak" /
  "And so it begins" / "Screw You OpenAI" / "Is it fixed?"
  -> []   (no usage described)
- "Best model for studying?" / "Claude Pro or ChatGPT Plus?" (pure shopping)
  -> []   (no usage described)
- "What the hell is that supposed to mean?" (cryptic)
  -> []   (unless body describes usage)
- "Canvas broken?" / "Restore the Collaborative Canvas workflow" / "Lost
  side-by-side Canvas access"
  -> []   (UI complaints without a specific creation request)
- "Claude can't present HTML file" / "ChatGPT cannot make a usable file"
  -> []   (vague file complaints; no specific creation request described)
- "Looked up my name through ChatGPT" (no search-mode mention)
  -> [general_purpose]   (NOT search — could be training-data lookup)
- "Want to make something that tracks my progress"
  -> [general_purpose]   (Q&A about ideas, not search)
- "Link Claude to Insta to summarize posts?" (integration question)
  -> []   (no actual usage)
- "Strange Audio Glitch (Unprompted Voices & Sounds)" without voice-mode mention
  -> []   (audio glitch != voice feature)
- "What AI are you using alongside ChatGPT?" (community survey)
  -> []   (tool-comparison, no usage)
- "Would you buy a ChatGPT robot I'm building?" (self-promo of hardware)
  -> []
- "claude for presentations" (interest in usage, no actual usage)
  -> [general_purpose]   (curious about presentations, no concrete creation)
- "I uploaded a PDF and asked it to summarize"
  -> [file_upload]   (NOT [file_upload, general_purpose])
- "Got ChatGPT to make a chart of my expenses from my CSV"
  -> [file_upload, code_interpreter]
- "Claude keeps forgetting earlier turns" / "ChatGPT lost my context"
  -> [multi_turn]
- "I keep losing good ideas in OLD chats" (about searching past chats)
  -> []   (not multi_turn — that's chat history navigation)
- "Anyone bouncing between AI tools?"
  -> []   (tool-switching musing)
- "Did ChatGPT get more censored?"
  -> [general_purpose]   (chat usage implied, not multi_turn)
- "A day as ChatGPT" (persona fiction)
  -> []
- "Drive docs are slow with Claude" (integration, not upload)
  -> []
- "Opus 4.8 in CC v2.1.154" (Claude Code version note)
  -> []
- "Asked ChatGPT to roast me and create an image"
  -> [image_creation]   (NOT [image_creation, general_purpose])
- "ChatGPT keeps using painting style for my images" / "There needs to be a toggle
  for image generation"
  -> [image_creation]   (NOT +multi_turn, NOT +general_purpose)
- "Gemini hates the 'analyze' feature when I submit an image"
  -> [image_upload]   (NOT +image_creation)
- "Is there any way to select all when deleting photos/files in chat?"
  -> [image_upload, file_upload]   (managing uploaded content)
- "Spreadsheet summarization at work — gave it a CSV, got insights"
  -> [file_upload, general_purpose]   (NOTE: rare exception when usage broader than file)

Posts:
{posts_json}

Respond ONLY with a JSON array, one entry per post:
[{{"post_id": "xxx", "scenario_tags": ["..."]}}]"""


# ---------------------------------------------------------------------------
# Data loading
# ---------------------------------------------------------------------------

PRODUCT_BY_SUBREDDIT = {
    "ClaudeAI": "Claude",
    "ChatGPT": "ChatGPT",
    "Bard": "Gemini",
    "GoogleGeminiAI": "Gemini",
    "GithubCopilot": "GitHub Copilot",
    "MicrosoftCopilot": "Microsoft Copilot",
}


def subreddit_to_product(subreddit: str) -> str:
    return PRODUCT_BY_SUBREDDIT.get(subreddit, subreddit)


def load_posts_for_run(conn: sqlite3.Connection, run_id: str,
                        post_ids: list[str] | None = None,
                        limit: int | None = None) -> list[dict]:
    """Load valid posts for a run with their content + comments (same truncation as analyze.py)."""
    query = """
        SELECT pa.post_id, pa.scenario_tags AS old_tags,
               p.title, p.body, p.subreddit_id
        FROM post_analysis pa
        JOIN posts p ON p.id = pa.post_id
        WHERE pa.run_id = ? AND pa.is_valid = 1
    """
    params: list = [run_id]
    if post_ids:
        placeholders = ",".join("?" for _ in post_ids)
        query += f" AND pa.post_id IN ({placeholders})"
        params.extend(post_ids)
    query += " ORDER BY pa.post_id"
    if limit:
        query += f" LIMIT {int(limit)}"

    rows = conn.execute(query, params).fetchall()
    posts = []
    for r in rows:
        d = dict(r)
        comments = conn.execute(
            "SELECT body, score FROM comments WHERE post_id = ? ORDER BY score DESC LIMIT 5",
            (d["post_id"],),
        ).fetchall()
        d["comments"] = [dict(c) for c in comments]
        posts.append(d)
    return posts


def build_posts_for_llm(batch: list[dict]) -> list[dict]:
    """Same truncation as analyze.py classify_and_analyze."""
    out = []
    for p in batch:
        top_comments = ""
        if p.get("comments"):
            top_comments = " | ".join((c["body"] or "")[:300] for c in p["comments"])
        out.append({
            "post_id": p["post_id"],
            "title": p["title"],
            "body": (p.get("body") or "")[:800],
            "top_comments": top_comments[:1500],
        })
    return out


# ---------------------------------------------------------------------------
# Relabel one batch
# ---------------------------------------------------------------------------

def relabel_batch(batch: list[dict]) -> dict[str, list[str]]:
    """Returns {post_id: new_tags}. Posts that fail get omitted (caller keeps old)."""
    if not batch:
        return {}
    # Group by product so the prompt's product_name is meaningful
    by_product: dict[str, list[dict]] = {}
    for p in batch:
        by_product.setdefault(subreddit_to_product(p.get("subreddit_id") or ""), []).append(p)

    new_tags: dict[str, list[str]] = {}
    for product, group in by_product.items():
        posts_for_llm = build_posts_for_llm(group)
        prompt = RELABEL_PROMPT_TEMPLATE.format(
            product_name=product,
            taxonomy=", ".join(SCENARIO_TAXONOMY),
            posts_json=json.dumps(posts_for_llm, ensure_ascii=False),
        )
        response = call_llm_local(
            prompt,
            system_prompt=("You are a JSON-only API. Output ONLY valid JSON arrays with no "
                           "markdown, no code fences, no explanation. Start with [ and end with ]."),
        )
        parsed = parse_json_response(response)
        if not parsed or not isinstance(parsed, list):
            log.warning("Batch parse failed for product=%s (%d posts) — keeping old tags",
                        product, len(group))
            continue
        for item in parsed:
            pid = item.get("post_id")
            tags = item.get("scenario_tags", [])
            if not isinstance(tags, list):
                continue
            # Filter to known taxonomy
            tags = [t for t in tags if t in SCENARIO_TAXONOMY]
            if pid:
                new_tags[pid] = tags
    return new_tags


# ---------------------------------------------------------------------------
# Sample IDs (for --sample mode)
# ---------------------------------------------------------------------------

def load_sample_post_ids() -> list[str]:
    ids = set()
    if AUDIT_IN_PATH.exists():
        for item in json.load(open(AUDIT_IN_PATH, encoding="utf-8")):
            if item.get("post_id"):
                ids.add(item["post_id"])
    if AUDIT_CI_PATH.exists():
        for item in json.load(open(AUDIT_CI_PATH, encoding="utf-8")):
            if item.get("post_id"):
                ids.add(item["post_id"])
    return sorted(ids)


# ---------------------------------------------------------------------------
# Gold-standard evaluation (uses QA round-2 rerev_FINAL.json)
# ---------------------------------------------------------------------------

def _parse_gold_better(better: str) -> list[str]:
    """Parse QA's 'better' field into a canonical list of tags.

    Examples: 'none' -> []; 'general_purpose' -> ['general_purpose'];
              'file_upload,general_purpose' -> ['file_upload','general_purpose']
    """
    if not better:
        return []
    s = better.strip().lower()
    if s in ("none", "[]", "empty", ""):
        return []
    tags = [t.strip() for t in s.replace(";", ",").split(",") if t.strip()]
    return [t for t in tags if t in SCENARIO_TAXONOMY]


def load_gold() -> tuple[dict[str, list[str]], dict[str, dict]]:
    """Return (gold_tags {pid: list[str]}, posts_meta {pid: {product,title,body,tags}}).

    Gold tag derivation:
      - If review.correct == True, the gold = the labels CURRENT on that post (the
        ones being reviewed). We need to recover those from the posts.tags field which
        QA snapshotted at the review time.
      - If review.correct == False, the gold = parsed review.better field.
    """
    if not GOLD_FINAL_PATH.exists():
        raise FileNotFoundError(f"Gold file not found: {GOLD_FINAL_PATH}")
    d = json.load(open(GOLD_FINAL_PATH, encoding="utf-8"))
    reviews = d.get("reviews", {})
    posts_meta = d.get("posts", {})
    gold: dict[str, list[str]] = {}
    for pid, rev in reviews.items():
        if rev.get("correct"):
            # The tags being reviewed (snapshotted in posts_meta[pid]['tags']) ARE the gold
            snapshot = posts_meta.get(pid, {}).get("tags")
            if isinstance(snapshot, list):
                gold[pid] = [t for t in snapshot if t in SCENARIO_TAXONOMY]
            elif isinstance(snapshot, str):
                gold[pid] = _parse_gold_better(snapshot)
            else:
                # Fallback: parse 'better' anyway (QA sometimes filled it even when correct)
                gold[pid] = _parse_gold_better(rev.get("better", ""))
        else:
            gold[pid] = _parse_gold_better(rev.get("better", ""))
    return gold, posts_meta


def evaluate_against_gold(new_map: dict[str, list[str]], gold: dict[str, list[str]]):
    """Compute per-scenario precision + overall accuracy on the gold set.

    Per-scenario precision: of the posts where we PREDICTED tag T, how many
    have T in the gold set. (Matches how QA judged the v1 fix: random sample
    by predicted tag.)

    Per-scenario recall: of the posts where GOLD has tag T, how many we predicted.
    """
    from collections import defaultdict
    pred_set: dict[str, set[str]] = defaultdict(set)
    gold_set: dict[str, set[str]] = defaultdict(set)
    for pid, tags in new_map.items():
        if pid not in gold:
            continue
        for t in tags:
            pred_set[t].add(pid)
    for pid, tags in gold.items():
        for t in tags:
            gold_set[t].add(pid)

    # Empty-tag treated as a pseudo-tag "[]" for visibility
    empty_pred = {pid for pid, t in new_map.items() if not t and pid in gold}
    empty_gold = {pid for pid, t in gold.items() if not t}
    pred_set["__EMPTY__"] = empty_pred
    gold_set["__EMPTY__"] = empty_gold

    rows = []
    for tag in sorted(set(SCENARIO_TAXONOMY) | {"__EMPTY__"}):
        p = pred_set.get(tag, set())
        g = gold_set.get(tag, set())
        tp = len(p & g)
        prec = (tp / len(p)) if p else None
        rec = (tp / len(g)) if g else None
        rows.append((tag, len(p), len(g), tp, prec, rec))

    # Exact-match accuracy (whole-post tag-set equality)
    exact = 0
    n = 0
    for pid, gtags in gold.items():
        if pid not in new_map:
            continue
        n += 1
        if sorted(new_map[pid]) == sorted(gtags):
            exact += 1

    print()
    print(f"{'scenario':<24} | {'#pred':>5} | {'#gold':>5} | {'TP':>4} | {'prec':>6} | {'recall':>6} | target")
    print("-" * 90)
    targets = {
        "code_interpreter": 0.70,
        "__EMPTY__": 0.50,
    }
    pass_targets = 0
    total_targets = 0
    for tag, npred, ngold, tp, prec, rec in rows:
        tgt = targets.get(tag, 0.80)
        prec_s = f"{prec:.0%}" if prec is not None else "  -  "
        rec_s = f"{rec:.0%}" if rec is not None else "  -  "
        if prec is not None:
            total_targets += 1
            ok = prec >= tgt
            mark = "PASS" if ok else "FAIL"
            if ok:
                pass_targets += 1
        else:
            mark = "n/a"
        print(f"{tag:<24} | {npred:>5} | {ngold:>5} | {tp:>4} | {prec_s:>6} | {rec_s:>6} | {tgt:.0%} {mark}")
    print()
    print(f"Exact-match (whole tag-set) accuracy: {exact}/{n} = {(exact/n if n else 0):.0%}")
    print(f"Per-scenario precision targets passed: {pass_targets}/{total_targets}")


def print_gold_misses(new_map: dict[str, list[str]], gold: dict[str, list[str]],
                       posts_meta: dict[str, dict], focus_tag: str | None = None,
                       limit: int = 30):
    """Print posts where new prediction != gold for inspection."""
    rows = []
    for pid, gtags in gold.items():
        if pid not in new_map:
            continue
        new = new_map[pid]
        if sorted(new) != sorted(gtags):
            if focus_tag and (focus_tag not in new and focus_tag not in gtags):
                continue
            title = (posts_meta.get(pid, {}).get("title") or "")[:70]
            rows.append((pid, title, gtags, new))
    print()
    suffix = f" (filter={focus_tag})" if focus_tag else ""
    print(f"--- Gold-vs-Pred mismatches{suffix} ({min(limit,len(rows))}/{len(rows)}) ---")
    for pid, title, g, n in rows[:limit]:
        g_s = ",".join(g) if g else "[]"
        n_s = ",".join(n) if n else "[]"
        title_safe = title.encode("ascii", "replace").decode()
        print(f"  {pid:<10} | {title_safe:<70} | gold={g_s:<35} | pred={n_s}")


# ---------------------------------------------------------------------------
# Backup + commit
# ---------------------------------------------------------------------------

def backup_old_tags(conn: sqlite3.Connection, run_id: str, post_ids: list[str] | None,
                     backup_path: Path) -> int:
    """Dump {post_id: old_scenario_tags} to JSON. Returns row count."""
    query = "SELECT post_id, scenario_tags FROM post_analysis WHERE run_id = ? AND is_valid = 1"
    params: list = [run_id]
    if post_ids:
        ph = ",".join("?" for _ in post_ids)
        query += f" AND post_id IN ({ph})"
        params.extend(post_ids)
    rows = conn.execute(query, params).fetchall()
    backup = {}
    for r in rows:
        try:
            backup[r["post_id"]] = json.loads(r["scenario_tags"]) if r["scenario_tags"] else []
        except Exception:
            backup[r["post_id"]] = r["scenario_tags"]
    backup_path.parent.mkdir(parents=True, exist_ok=True)
    backup_path.write_text(json.dumps(backup, ensure_ascii=False, indent=2), encoding="utf-8")
    return len(backup)


# ---------------------------------------------------------------------------
# Reporting helpers
# ---------------------------------------------------------------------------

def tag_distribution(tag_map: dict[str, list[str]]) -> Counter:
    c = Counter()
    empty = 0
    for tags in tag_map.values():
        if not tags:
            empty += 1
        for t in tags:
            c[t] += 1
    c["__EMPTY__"] = empty
    return c


def print_diff_table(old_map: dict, new_map: dict, title_map: dict, limit: int | None = None):
    """Per-post diff for inspection."""
    keys = list(new_map.keys())
    if limit:
        keys = keys[:limit]
    print()
    print(f"{'post_id':<10} | {'title (60)':<62} | {'old':<35} | new")
    print("-" * 160)
    changed = 0
    for pid in keys:
        old = sorted(old_map.get(pid, []))
        new = sorted(new_map.get(pid, []))
        if old != new:
            changed += 1
        title = (title_map.get(pid) or "").replace("\n", " ")[:60]
        old_s = ",".join(old) if old else "[]"
        new_s = ",".join(new) if new else "[]"
        marker = "*" if old != new else " "
        print(f"{marker}{pid:<9} | {title:<62} | {old_s:<35} | {new_s}")
    print(f"\nChanged: {changed}/{len(keys)}")


def print_distribution_diff(old_dist: Counter, new_dist: Counter):
    keys = sorted(set(old_dist) | set(new_dist), key=lambda k: -max(old_dist[k], new_dist[k]))
    print(f"\n{'scenario':<28} | {'old':>6} | {'new':>6} | delta")
    print("-" * 60)
    for k in keys:
        o, n = old_dist[k], new_dist[k]
        d = n - o
        print(f"{k:<28} | {o:>6} | {n:>6} | {d:+d}")


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--run-id", default="c0dfb8d8")
    ap.add_argument("--sample", action="store_true",
                    help="Only process the 182 audit sample post_ids; never writes to DB")
    ap.add_argument("--gold", action="store_true",
                    help="Run on QA round-2 gold set (154 posts) and compute per-scenario "
                         "precision/recall vs gold labels; never writes to DB")
    ap.add_argument("--gold-focus", default=None,
                    help="With --gold, only print mismatches involving this tag (e.g. code_interpreter)")
    ap.add_argument("--commit", action="store_true",
                    help="UPDATE post_analysis with new tags (otherwise dry-run)")
    ap.add_argument("--limit", type=int, default=None, help="Process only first N posts (debug)")
    ap.add_argument("--db", default=str(DB_PATH))
    args = ap.parse_args()

    if (args.sample or args.gold) and args.commit:
        log.error("--sample / --gold are dry-run only; do not combine with --commit")
        sys.exit(2)

    conn = sqlite3.connect(args.db)
    conn.row_factory = sqlite3.Row

    gold_map: dict[str, list[str]] | None = None
    gold_posts_meta: dict | None = None
    if args.gold:
        gold_map, gold_posts_meta = load_gold()
        log.info("Gold mode: %d posts loaded from %s", len(gold_map), GOLD_FINAL_PATH.name)
        target_ids = list(gold_map.keys())
    elif args.sample:
        target_ids = load_sample_post_ids()
        log.info("Sample mode: %d audit post_ids loaded", len(target_ids))
    else:
        target_ids = None

    posts = load_posts_for_run(conn, args.run_id, target_ids, args.limit)
    log.info("Loaded %d valid posts for run_id=%s", len(posts), args.run_id)
    if not posts:
        log.error("No posts to process")
        return

    title_map = {p["post_id"]: p["title"] for p in posts}
    old_map: dict[str, list[str]] = {}
    for p in posts:
        try:
            old_map[p["post_id"]] = json.loads(p["old_tags"]) if p["old_tags"] else []
        except Exception:
            old_map[p["post_id"]] = []

    # --- Run relabel ---
    new_map: dict[str, list[str]] = {}
    total = len(posts)
    for i in range(0, total, BATCH_SIZE):
        batch = posts[i : i + BATCH_SIZE]
        log.info("Relabel batch %d-%d / %d", i + 1, min(i + BATCH_SIZE, total), total)
        batch_new = relabel_batch(batch)
        new_map.update(batch_new)
        # Fill in missing (LLM failed) with old tags so downstream UPDATE is a no-op
        for p in batch:
            new_map.setdefault(p["post_id"], old_map.get(p["post_id"], []))

    # --- Reporting ---
    old_dist = tag_distribution(old_map)
    new_dist = tag_distribution(new_map)
    print_distribution_diff(old_dist, new_dist)

    if args.gold:
        evaluate_against_gold(new_map, gold_map)
        print_gold_misses(new_map, gold_map, gold_posts_meta, focus_tag=args.gold_focus)
        # Also show old-vs-new for the gold set (debug)
        ci_pred = sorted(pid for pid, t in new_map.items() if "code_interpreter" in t)
        ci_old_in_gold = sorted(pid for pid, t in old_map.items() if "code_interpreter" in t)
        print(f"\ncode_interpreter | old={len(ci_old_in_gold)} (in v1 gold subset) | "
              f"new pred on gold={len(ci_pred)}")
        log.info("Gold dry-run complete — no DB writes")
        return

    if args.sample:
        # Verbose per-post diff
        print_diff_table(old_map, new_map, title_map)
        # Specifically: of old code_interpreter posts, how many were cleared / kept?
        ci_old = [pid for pid, t in old_map.items() if "code_interpreter" in t]
        ci_kept = sum(1 for pid in ci_old if "code_interpreter" in new_map.get(pid, []))
        print(f"\nold code_interpreter sample: {len(ci_old)} | "
              f"kept code_interpreter: {ci_kept} | "
              f"dropped/replaced: {len(ci_old) - ci_kept}")
        print(f"posts now empty []: "
              f"{sum(1 for t in new_map.values() if not t)} / {len(new_map)}")
        log.info("Sample dry-run complete — no DB writes")
        return

    if not args.commit:
        log.info("Dry-run complete (no --commit). Pass --commit to UPDATE the DB.")
        return

    # --- Backup + commit ---
    backup_path = Path(BACKUP_PATH_TEMPLATE.format(run_id=args.run_id))
    rows = backup_old_tags(conn, args.run_id, None, backup_path)
    log.info("Backed up %d old scenario_tags to %s", rows, backup_path)

    updated = 0
    skipped_same = 0
    with conn:
        for pid, tags in new_map.items():
            old = old_map.get(pid, [])
            if sorted(old) == sorted(tags):
                skipped_same += 1
                continue
            conn.execute(
                "UPDATE post_analysis SET scenario_tags = ? WHERE run_id = ? AND post_id = ?",
                (json.dumps(tags, ensure_ascii=False), args.run_id, pid),
            )
            updated += 1
    log.info("UPDATE complete: %d changed, %d unchanged (skipped)", updated, skipped_same)


if __name__ == "__main__":
    main()
