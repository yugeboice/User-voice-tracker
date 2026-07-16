"""
Reddit Competitive Intelligence Analyzer

Reads scraped Reddit data from SQLite, uses LLM (Copilot API) to:
1. Filter invalid/low-value content
2. Classify discussion topics
3. Analyze sentiment with attribution
4. Generate per-product summaries
5. Generate cross-product comparison

Usage:
    python analyze.py --days 14
    python analyze.py --days 14 --llm-endpoint http://localhost:4141 --model gpt-4
"""

import argparse
import json
import logging
import os
import re
import sqlite3
import sys
import uuid
from datetime import datetime, timedelta, timezone
from pathlib import Path

import requests

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

DB_DIR = Path(__file__).resolve().parent.parent / "data"
DB_PATH = DB_DIR / "reddit.db"
REPORTS_DIR = DB_DIR / "reports"

LLM_ENDPOINT = "http://localhost:4141"
LLM_MODEL = "gpt-4"
LLM_TIMEOUT = 300
BATCH_SIZE = 15  # Posts per LLM call

# Be10x/B10x "session on Copilot" training-camp spam. These are near-identical
# promotional posts flooding Copilot subreddits — dropped BEFORE the LLM filter
# so they never reach analysis or the report.
SPAM_RE = re.compile(
    r'\b(be?10x|be10x|b10x|'
    r'session on copilot|session for the copilot|amazing copilot session|'
    r'joined be10x|copilot session|copilot sesstion|b10x session|be10x session|'
    r'session on (m365|microsoft 365|power bi)[^.]{0,30}copilot|'
    r'had.{0,20}session.{0,20}copilot|copilot is amazing session)\b',
    re.IGNORECASE,
)


def is_spam_post(post: dict) -> bool:
    """Regex pre-filter for Be10x/B10x session spam. Checks title + body."""
    text = f"{post.get('title', '')} {post.get('body', '')}"
    return bool(SPAM_RE.search(text))


# Fallback LLM endpoints (tried in order). Keep GPT family for sentiment consistency.
LLM_FALLBACKS = [
    ("http://localhost:4141", "gpt-4"),
    (os.environ.get("EGRESS_LLM_API_ENDPOINT", "http://127.0.0.1:41891"), "gpt-4"),
]

TOPIC_TAXONOMY = [
    "model_capability",
    "product_experience",
    "feature_request",
    "bug_report",
    "comparison",
    "use_case",
    "news_update",
    "meta",
]

SCENARIO_TAXONOMY = [
    "image_upload",
    "image_creation",
    "multi_turn",
    "code_interpreter",
    "office_file_creation",
    "file_upload",
    "general_purpose_search",
    "general_purpose",
    "voice_single_turn",
]

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
    datefmt="%H:%M:%S",
)
log = logging.getLogger("analyzer")

# ---------------------------------------------------------------------------
# Database helpers
# ---------------------------------------------------------------------------

ANALYSIS_SCHEMA = """
CREATE TABLE IF NOT EXISTS analysis_runs (
    id              TEXT PRIMARY KEY,
    started_at      TEXT NOT NULL,
    completed_at    TEXT,
    period_start    TEXT NOT NULL,
    period_end      TEXT NOT NULL,
    subreddits      TEXT NOT NULL,
    status          TEXT DEFAULT 'running',
    total_posts     INTEGER DEFAULT 0,
    filtered_posts  INTEGER DEFAULT 0,
    config_json     TEXT
);

CREATE TABLE IF NOT EXISTS post_analysis (
    id              TEXT PRIMARY KEY,
    run_id          TEXT NOT NULL REFERENCES analysis_runs(id),
    post_id         TEXT NOT NULL REFERENCES posts(id),
    is_valid        INTEGER DEFAULT 1,
    filter_reason   TEXT,
    topic_category  TEXT,
    sentiment_score REAL,
    sentiment_label TEXT,
    sentiment_reason TEXT,
    key_points      TEXT,
    is_typical      INTEGER DEFAULT 0,
    typical_reason  TEXT,
    scenario_tags   TEXT
);

CREATE TABLE IF NOT EXISTS reports (
    id              TEXT PRIMARY KEY,
    run_id          TEXT NOT NULL REFERENCES analysis_runs(id),
    product_name    TEXT,
    report_type     TEXT NOT NULL,
    title           TEXT NOT NULL,
    summary_text    TEXT NOT NULL,
    report_json     TEXT NOT NULL,
    chart_data_json TEXT,
    created_at      TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_analysis_run ON post_analysis(run_id);
CREATE INDEX IF NOT EXISTS idx_analysis_post ON post_analysis(post_id);
CREATE INDEX IF NOT EXISTS idx_reports_run ON reports(run_id);
"""


def init_analysis_db(conn: sqlite3.Connection):
    conn.executescript(ANALYSIS_SCHEMA)
    # Migration: add scenario_tags column if missing
    try:
        conn.execute("SELECT scenario_tags FROM post_analysis LIMIT 1")
    except sqlite3.OperationalError:
        conn.execute("ALTER TABLE post_analysis ADD COLUMN scenario_tags TEXT")
    conn.commit()


def get_posts_with_comments(
    conn: sqlite3.Connection,
    subreddit: str,
    period_start: str,
    period_end: str,
    max_posts: int = 0,
) -> list[dict]:
    """Load posts and their comments for a given subreddit and time range.

    If max_posts > 0, only return the top N posts sorted by score (highest first).
    This is used to match reference period volumes for high-volume subreddits.
    """
    if max_posts > 0:
        posts = conn.execute(
            """SELECT * FROM posts
               WHERE subreddit_id = ? AND created_utc >= ? AND created_utc < ?
               ORDER BY score DESC, num_comments DESC
               LIMIT ?""",
            (subreddit, period_start, period_end, max_posts),
        ).fetchall()
    else:
        posts = conn.execute(
            """SELECT * FROM posts
               WHERE subreddit_id = ? AND created_utc >= ? AND created_utc < ?
               ORDER BY created_utc DESC""",
            (subreddit, period_start, period_end),
        ).fetchall()

    result = []
    for p in posts:
        post_dict = dict(p)
        comments = conn.execute(
            "SELECT * FROM comments WHERE post_id = ? ORDER BY created_utc",
            (p["id"],),
        ).fetchall()
        post_dict["comments"] = [dict(c) for c in comments]
        result.append(post_dict)

    return result


# ---------------------------------------------------------------------------
# LLM Integration
# ---------------------------------------------------------------------------

def call_llm(
    prompt: str,
    system_prompt: str = "",
    endpoint: str = LLM_ENDPOINT,
    model: str = LLM_MODEL,
    max_retries: int = 5,
) -> str:
    """Call the LLM API (OpenAI-compatible) and return the response text.

    Retries with exponential backoff on connection errors (API crash/restart).
    """
    import time

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
        except (requests.exceptions.ConnectionError, requests.exceptions.ReadTimeout, ConnectionResetError) as e:
            wait = 30 * (2 ** attempt)  # 30s, 60s, 120s, 240s, 480s
            log.warning("LLM connection/timeout failed (attempt %d/%d): %s", attempt + 1, max_retries, e)
            log.info("Waiting %ds for API to recover...", wait)
            time.sleep(wait)
        except Exception as e:
            log.error("LLM call failed: %s", e)
            return ""

    log.error("LLM call failed after %d retries - API appears permanently down", max_retries)
    return ""


def parse_json_response(text: str) -> list | dict | None:
    """Extract JSON from LLM response (handles markdown code blocks)."""
    # Try direct parse first
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        pass

    # Try extracting from code block
    import re
    match = re.search(r"```(?:json)?\s*\n?(.*?)\n?```", text, re.DOTALL)
    if match:
        try:
            return json.loads(match.group(1))
        except json.JSONDecodeError:
            pass

    # Try finding array or object
    for start_char, end_char in [("[", "]"), ("{", "}")]:
        start = text.find(start_char)
        end = text.rfind(end_char)
        if start != -1 and end != -1 and end > start:
            try:
                return json.loads(text[start : end + 1])
            except json.JSONDecodeError:
                pass

    return None


# ---------------------------------------------------------------------------
# Analysis pipeline
# ---------------------------------------------------------------------------

def filter_posts(posts: list[dict], endpoint: str, model: str) -> list[dict]:
    """Use LLM to filter out invalid/low-value posts. Returns analysis dicts."""
    results = []

    # Pre-filter: drop Be10x/B10x session spam via regex before spending LLM calls.
    spam_count = 0
    llm_posts = []
    for p in posts:
        if is_spam_post(p):
            spam_count += 1
            results.append({
                "post_id": p["id"],
                "valid": False,
                "reason": "Be10x/B10x session-on-Copilot promotional spam (regex pre-filter)",
            })
        else:
            llm_posts.append(p)
    if spam_count:
        log.info("Spam pre-filter removed %d Be10x/B10x posts (%d remain for LLM)",
                 spam_count, len(llm_posts))

    for i in range(0, len(llm_posts), BATCH_SIZE):
        batch = llm_posts[i : i + BATCH_SIZE]
        posts_for_llm = []
        for p in batch:
            posts_for_llm.append({
                "post_id": p["id"],
                "title": p["title"],
                "body": (p.get("body") or "")[:500],
                "author": p.get("author", ""),
                "num_comments": p.get("num_comments", 0),
            })

        prompt = f"""You are a content quality filter for Reddit competitive intelligence analysis.

Classify each post below as VALID or INVALID.

INVALID criteria:
- Spam or promotional content with no discussion value
- Pure meme/joke posts with zero substantive insight about the AI product
- AutoModerator or bot-generated posts
- Completely off-topic (not related to the AI product at all)
- Extremely short posts (title only with <5 words, empty body) with no identifiable discussion topic

Be LENIENT - if a post has ANY substantive opinion, complaint, praise, question, or use case about an AI product, mark it VALID. User questions, complaints, feature requests, and experience reports are all VALID.

Posts:
{json.dumps(posts_for_llm, ensure_ascii=False)}

Respond ONLY with a JSON array:
[{{"post_id": "xxx", "valid": true, "reason": ""}}]"""

        log.info("Filtering batch %d-%d of %d posts...", i + 1, min(i + BATCH_SIZE, len(llm_posts)), len(llm_posts))
        response = call_llm(
            prompt,
            system_prompt="You are a JSON-only API. Output ONLY valid JSON arrays with no markdown, no code fences, no explanation. Start your response with [ and end with ].",
            endpoint=endpoint, model=model,
        )
        parsed = parse_json_response(response)

        if parsed and isinstance(parsed, list):
            for item in parsed:
                results.append(item)
        else:
            # If LLM fails, mark all as valid
            log.warning("Filter LLM parse failed, marking batch as valid")
            for p in batch:
                results.append({"post_id": p["id"], "valid": True, "reason": ""})

    return results


def classify_and_analyze(
    posts: list[dict],
    product_name: str,
    endpoint: str,
    model: str,
) -> list[dict]:
    """Use LLM to classify topics and analyze sentiment for each post."""
    results = []

    for i in range(0, len(posts), BATCH_SIZE):
        batch = posts[i : i + BATCH_SIZE]
        posts_for_llm = []
        for p in batch:
            top_comments = ""
            if p.get("comments"):
                top_by_score = sorted(
                    p["comments"],
                    key=lambda c: c.get("score", 0),
                    reverse=True,
                )[:5]
                top_comments = " | ".join(c["body"][:300] for c in top_by_score)

            posts_for_llm.append({
                "post_id": p["id"],
                "title": p["title"],
                "body": (p.get("body") or "")[:800],
                "top_comments": top_comments[:1500],
            })

        prompt = f"""You are an AI product competitive intelligence analyst.
Product: {product_name}

For each post, determine:
1. topic_category: One of [{", ".join(TOPIC_TAXONOMY)}]
2. sentiment_score: Float from -1.0 (very negative) to 1.0 (very positive)
3. sentiment_label: "positive", "negative", "neutral", or "mixed"
4. sentiment_reason: One sentence in Chinese explaining why (reference specific user concerns/praise)
5. key_points: Array of 1-3 key discussion points in Chinese
6. scenario_tags: Array of 0 or more applicable scenarios from [{", ".join(SCENARIO_TAXONOMY)}].

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
   - "Canvas broken?" / "Restore the Collaborative Canvas workflow" / "Lost access
     to side-by-side Canvas"
     -> []   (UI feature complaints WITHOUT a specific document creation request)
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

Posts:
{json.dumps(posts_for_llm, ensure_ascii=False)}

Respond ONLY with a JSON array:
[{{"post_id": "xxx", "topic_category": "...", "sentiment_score": 0.0, "sentiment_label": "...", "sentiment_reason": "...", "key_points": ["..."], "scenario_tags": ["..."]}}]"""

        log.info("Analyzing batch %d-%d...", i + 1, min(i + BATCH_SIZE, len(posts)))
        response = call_llm(
            prompt,
            system_prompt="You are a JSON-only API. Output ONLY valid JSON arrays with no markdown, no code fences, no explanation. Start your response with [ and end with ].",
            endpoint=endpoint, model=model,
        )
        parsed = parse_json_response(response)

        if parsed and isinstance(parsed, list):
            # Flatten in case LLM returned nested list [[{...}]]
            for item in parsed:
                if isinstance(item, dict):
                    results.append(item)
                elif isinstance(item, list):
                    results.extend(item)
        else:
            log.warning("Analysis LLM parse failed for batch, using defaults")
            for p in batch:
                results.append({
                    "post_id": p["id"],
                    "topic_category": "meta",
                    "sentiment_score": 0.0,
                    "sentiment_label": "neutral",
                    "sentiment_reason": "LLM analysis unavailable",
                    "key_points": [],
                })

    return results


def generate_product_summary(
    product_name: str,
    subreddit: str,
    posts: list[dict],
    analysis_map: dict,
    period_start: str,
    period_end: str,
    endpoint: str,
    model: str,
) -> str:
    """Generate a per-product summary using LLM."""
    # Compute statistics
    valid_analyses = [a for a in analysis_map.values() if a.get("is_valid", True)]
    filtered_count = len(analysis_map) - len(valid_analyses)

    sentiment_counts = {"positive": 0, "negative": 0, "neutral": 0, "mixed": 0}
    topic_counts = {t: 0 for t in TOPIC_TAXONOMY}

    for a in valid_analyses:
        label = a.get("sentiment_label", "neutral")
        sentiment_counts[label] = sentiment_counts.get(label, 0) + 1
        topic = a.get("topic_category", "meta")
        if topic in topic_counts:
            topic_counts[topic] += 1

    total = len(valid_analyses) or 1
    sentiment_dist = {k: f"{v} ({v*100//total}%)" for k, v in sentiment_counts.items()}
    top_topics = sorted(topic_counts.items(), key=lambda x: -x[1])[:5]

    # Build top posts summary
    top_posts = sorted(posts, key=lambda p: p.get("num_comments", 0), reverse=True)[:10]
    top_posts_text = ""
    for p in top_posts:
        pid = p["id"]
        analysis = analysis_map.get(pid, {})
        top_posts_text += f"- [{analysis.get('sentiment_label', '?')}] {p['title'][:80]}\n"
        if analysis.get("key_points"):
            top_posts_text += f"  Key points: {', '.join(analysis['key_points'][:2])}\n"

    prompt = f"""你是一位竞品情报分析师，正在撰写Reddit社区双周报告。

产品: {product_name}
社区: r/{subreddit}
时间段: {period_start[:10]} 至 {period_end[:10]}
分析帖子数: {len(valid_analyses)} (过滤掉 {filtered_count} 条无效内容)

情感分布: {json.dumps(sentiment_dist, ensure_ascii=False)}

话题分布 (Top 5):
{chr(10).join(f'  {t}: {c}条' for t, c in top_topics)}

热门帖子 (按互动量排序):
{top_posts_text}

请撰写一份竞品情报摘要（中文，300-500字），涵盖：
1. 整体社区情感及其驱动因素
2. 最多讨论的话题及其重要性
3. 用户主要痛点或投诉
4. 用户赞赏或期望的功能
5. 与竞品的对比讨论（如有）
6. 对产品团队的可操作洞察

要求具体，引用实际帖子话题和用户关注点，避免空泛表述。"""

    log.info("Generating summary for %s...", product_name)
    return call_llm(prompt, endpoint=endpoint, model=model)


def generate_cross_product_comparison(
    product_summaries: dict[str, str],
    endpoint: str,
    model: str,
) -> str:
    """Generate cross-product comparison summary."""
    summaries_text = ""
    for product, summary in product_summaries.items():
        summaries_text += f"\n### {product}\n{summary}\n"

    prompt = f"""你是一位资深竞品情报分析师。

以下是过去2周内各AI产品Reddit社区的分析摘要：
{summaries_text}

重点关注产品：M365 Copilot、Copilot、ChatGPT、Gemini、Claude。

请撰写一份跨产品竞品对比报告（中文，500-800字），包含：
1. 各产品的用户满意度对比（重点对比上述5个核心产品）
2. 哪个产品社区最满意/最不满意，以及原因
3. 各产品共同的痛点
4. 各产品独特的优势或劣势
5. 新兴的竞争动态或用户偏好变化
6. 3-5条可操作建议

请以清晰的执行简报风格呈现。"""

    log.info("Generating cross-product comparison...")
    return call_llm(prompt, endpoint=endpoint, model=model)


def extract_structured_insights(
    product_name: str,
    posts: list[dict],
    analysis_map: dict,
    endpoint: str,
    model: str,
) -> dict:
    """Extract structured pain_points, strengths, recommendations, and keywords using LLM."""
    # Build context from analyzed posts
    negative_posts = []
    positive_posts = []
    all_key_points = []

    for p in posts:
        a = analysis_map.get(p["id"], {})
        if not a.get("is_valid", True):
            continue
        label = a.get("sentiment_label", "neutral")
        entry = {
            "post_id": p["id"],
            "title": p["title"][:80],
            "sentiment_reason": a.get("sentiment_reason", ""),
            "key_points": a.get("key_points", []),
            "num_comments": p.get("num_comments", 0),
        }
        if label == "negative":
            negative_posts.append(entry)
        elif label == "positive":
            positive_posts.append(entry)
        all_key_points.extend(a.get("key_points", []))

    # Sort by engagement
    negative_posts.sort(key=lambda x: -x["num_comments"])
    positive_posts.sort(key=lambda x: -x["num_comments"])

    prompt = f"""你是一位竞品情报分析师。根据以下Reddit社区数据，提取结构化洞察。

产品: {product_name}

负面帖子 (按互动量排序，最多15条):
{json.dumps(negative_posts[:15], ensure_ascii=False)}

正面帖子 (按互动量排序，最多15条):
{json.dumps(positive_posts[:15], ensure_ascii=False)}

所有关键讨论点:
{json.dumps(all_key_points[:50], ensure_ascii=False)}

请输出以下JSON结构:
{{
  "pain_points": [
    {{"text": "简短痛点描述(中文,15字以内)", "severity": "high/medium/low", "post_ids": ["关联的post_id"], "detail": "一句话详细说明"}}
  ],
  "strengths": [
    {{"text": "简短优势描述(中文,15字以内)", "post_ids": ["关联的post_id"], "detail": "一句话详细说明"}}
  ],
  "recommendations": [
    {{"priority": "P0/P1/P2", "text": "具体可操作建议(中文)"}}
  ],
  "keywords": ["关键词1", "关键词2", ...]
}}

要求:
- pain_points: 3-5条，按严重程度排序，必须关联真实post_id
- strengths: 2-4条，必须关联真实post_id
- recommendations: 3-5条，P0=紧急，P1=重要，P2=建议
- keywords: 15-25个中英文关键词，从讨论中提取高频主题词"""

    log.info("Extracting structured insights for %s...", product_name)
    response = call_llm(
        prompt,
        system_prompt="You are a JSON-only API. Output ONLY valid JSON with no markdown, no code fences, no explanation. Start your response with {{ and end with }}.",
        endpoint=endpoint, model=model,
    )
    parsed = parse_json_response(response)

    if parsed and isinstance(parsed, dict):
        return parsed

    log.warning("Structured insights parse failed for %s, using defaults", product_name)
    return {
        "pain_points": [],
        "strengths": [],
        "recommendations": [],
        "keywords": [],
    }


def select_typical_posts(
    posts: list[dict],
    analysis_map: dict,
    count: int = 5,
) -> list[str]:
    """Select typical/representative posts based on engagement and analysis richness.

    Source-aware: guarantees at least one slot for each source_platform that has
    valid posts (best-scored from each), then fills remaining slots by global score.
    This ensures low-engagement sources (e.g. TechCommunity posts that carry
    num_comments=0) are still showcased, not crowded out by high-comment Reddit posts.
    """
    scored = []
    platform_of = {}
    for p in posts:
        pid = p["id"]
        analysis = analysis_map.get(pid, {})
        if not analysis.get("is_valid", True):
            continue

        # Score = comments + info density (key points count) + body length indicator
        score = (
            (p.get("num_comments", 0) or 0) * 2
            + len(analysis.get("key_points", [])) * 10
            + (1 if (p.get("body") or "") and len(p.get("body", "")) > 100 else 0) * 5
            + abs(analysis.get("sentiment_score", 0)) * 10  # Strong sentiment = more interesting
        )
        scored.append((pid, score))
        platform_of[pid] = p.get("source_platform") or "reddit"

    scored.sort(key=lambda x: -x[1])

    # 1) Guarantee one representative per platform (best-scored), in score order.
    selected: list[str] = []
    seen_platforms: set = set()
    for pid, _ in scored:
        plat = platform_of[pid]
        if plat not in seen_platforms:
            seen_platforms.add(plat)
            selected.append(pid)
            if len(selected) >= count:
                return selected

    # 2) Fill remaining slots by global score, skipping already-picked posts.
    for pid, _ in scored:
        if pid not in selected:
            selected.append(pid)
            if len(selected) >= count:
                break
    return selected[:count]


def compute_source_breakdown(posts: list[dict]) -> dict:
    """Count posts per source platform for a product.

    Backward-compatible: posts from Reddit-only DBs lack the source_platform
    key, so they default to 'reddit'. Returns e.g. {"reddit": 1650, "hackernews": 65}.
    """
    breakdown: dict = {}
    for p in posts:
        platform = p.get("source_platform") or "reddit"
        breakdown[platform] = breakdown.get(platform, 0) + 1
    return breakdown


def _generate_report_index(reports_dir: Path):
    """Generate/update data/reports/index.json with metadata for all reports."""
    reports = []
    for f in sorted(reports_dir.glob("report_*.json"), reverse=True):
        if "_analysis_" in f.name:
            continue  # skip markdown analysis exports
        try:
            data = json.loads(f.read_text(encoding="utf-8"))
            period = data.get("period", {})
            products = data.get("products", [])
            total_posts = sum(p.get("post_count", 0) for p in products)
            total_valid = sum(p.get("valid_post_count", 0) for p in products)
            reports.append({
                "file": f.name,
                "run_id": data.get("run_id", ""),
                "period_start": period.get("start", "")[:10],
                "period_end": period.get("end", "")[:10],
                "generated_at": f.stat().st_mtime,
                "product_count": len(products),
                "total_posts": total_posts,
                "total_valid_posts": total_valid,
            })
        except Exception as e:
            log.warning("Skipping %s in index: %s", f.name, e)
    index_path = reports_dir / "index.json"
    index_path.write_text(
        json.dumps({"reports": reports}, indent=2, ensure_ascii=False),
        encoding="utf-8",
    )
    log.info("Updated report index: %s (%d reports)", index_path, len(reports))


# ---------------------------------------------------------------------------
# Main pipeline
# ---------------------------------------------------------------------------

def run_analysis(
    days: int = 14,
    endpoint: str = LLM_ENDPOINT,
    model: str = LLM_MODEL,
    resume_run_id: str = None,
    start_date: str = None,
    end_date: str = None,
    max_posts_per_sub: int = 0,
    db_path: Path = None,
    output_path: str = None,
    scratch: bool = False,
) -> dict:
    """Run full analysis pipeline.

    If resume_run_id is given, resumes an interrupted run instead of starting a new one.
    Subreddits that already have post_analysis records in that run are skipped.

    If start_date / end_date are given (YYYY-MM-DD), they override the default
    Monday-aligned window calculated from --days.

    If max_posts_per_sub > 0, only analyze the top N posts (by score) per subreddit.
    This is useful to match reference period volumes for high-volume subreddits.

    db_path overrides the source database (default: data/reddit.db).
    output_path overrides the report output file path.
    scratch=True disables all online-publish side effects (latest.json, index.json,
    wwwroot sync) so isolated validation runs never touch deployed artifacts.
    """

    active_db = Path(db_path) if db_path else DB_PATH
    conn = sqlite3.connect(str(active_db))
    conn.row_factory = sqlite3.Row
    init_analysis_db(conn)

    now = datetime.now(timezone.utc)

    # --- Resume or new run ---
    if resume_run_id:
        existing = conn.execute(
            "SELECT * FROM analysis_runs WHERE id=?", (resume_run_id,)
        ).fetchone()
        if not existing:
            log.error("Resume run_id %s not found in DB.", resume_run_id)
            return {}
        run_id = resume_run_id
        period_start = existing["period_start"]
        period_end = existing["period_end"]
        log.info("Resuming run %s (period: %s to %s)", run_id, period_start[:10], period_end[:10])
        # Find already-completed subreddits (those with any post_analysis in this run)
        done_subs = {
            row[0]
            for row in conn.execute(
                """SELECT DISTINCT p.subreddit_id FROM post_analysis pa
                   JOIN posts p ON pa.post_id=p.id WHERE pa.run_id=?""",
                (run_id,),
            ).fetchall()
        }
        log.info("Already completed subreddits: %s", done_subs or "(none)")
    else:
        if start_date and end_date:
            # Use explicitly provided date range
            # end_date is inclusive (user means "up to and including this day"),
            # so we add 1 day to make it exclusive (next day 00:00:00) for
            # consistent half-open interval [start, end) queries.
            period_start = datetime.strptime(start_date, "%Y-%m-%d").replace(tzinfo=timezone.utc).isoformat()
            end_dt = datetime.strptime(end_date, "%Y-%m-%d").replace(tzinfo=timezone.utc)
            period_end = (end_dt + timedelta(days=1)).isoformat()
            log.info("Using custom date range: %s to %s (exclusive)", start_date, (end_dt + timedelta(days=1)).strftime("%Y-%m-%d"))
        else:
            # Align to Monday boundaries for clean, non-overlapping 14-day periods
            # period_end = this Monday (00:00), period_start = 14 days before that
            today_midnight = now.replace(hour=0, minute=0, second=0, microsecond=0)
            # Roll back to most recent Monday (0=Monday in weekday())
            days_since_monday = today_midnight.weekday()  # 0=Mon, 6=Sun
            this_monday = today_midnight - timedelta(days=days_since_monday)
            period_end = this_monday.isoformat()
            period_start = (this_monday - timedelta(days=days)).isoformat()
        done_subs = set()
        run_id = str(uuid.uuid4())[:8]
        conn.execute(
            """INSERT INTO analysis_runs (id, started_at, period_start, period_end, subreddits, status, config_json)
               VALUES (?, ?, ?, ?, ?, 'running', ?)""",
            (run_id, now.isoformat(), period_start, period_end,
             json.dumps([s["id"] for s in conn.execute("SELECT id FROM subreddits").fetchall()]),
             json.dumps({"days": days, "model": model})),
        )
        conn.commit()
        log.info("Analysis run %s started (period: %s to %s)", run_id, period_start[:10], period_end[:10])

    # Core products to analyze (excludes GitHub Copilot)
    EXCLUDED_PRODUCTS = {"GitHub Copilot"}  # User confirmed: GitHub Copilot not needed

    # Get all subreddits that have been scraped (excluding non-core products)
    subreddits = [
        s for s in conn.execute("SELECT * FROM subreddits").fetchall()
        if s["product_name"] not in EXCLUDED_PRODUCTS
    ]
    if not subreddits:
        log.error("No subreddits found in database. Run scrape.py first.")
        return {}

    product_summaries = {}
    product_chart_data = {}
    all_report_data = {"run_id": run_id, "period": {"start": period_start, "end": period_end}, "products": []}
    total_posts_analyzed = 0
    total_filtered = 0

    # Group subreddits by product_name for merged Step 4/5
    from collections import defaultdict
    product_to_subs = defaultdict(list)
    for s in subreddits:
        product_to_subs[s["product_name"]].append(s)

    # Cache to accumulate multi-subreddit data before running Step 4/5
    _product_cache: dict = {}

    for sub_idx, sub in enumerate(subreddits):
        sub_id = sub["id"]
        product_name = sub["product_name"]

        # --- Checkpoint: skip already-completed subreddits ---
        if sub_id in done_subs:
            log.info("[%s] Already completed in this run, skipping.", sub_id)
            continue

        # Health-check: wait for API to be available before each subreddit
        import time as _time
        for _hc in range(10):
            try:
                _r = requests.get(f"{endpoint}/v1/models", timeout=5)
                if _r.status_code == 200:
                    break
            except Exception:
                pass
            log.warning("API health-check failed, waiting 30s (attempt %d/10)...", _hc + 1)
            _time.sleep(30)

        # Small cooldown between subreddits to avoid overwhelming the API
        if sub_idx > 0:
            log.info("Cooling down 10s before next subreddit...")
            _time.sleep(10)

        log.info("=" * 60)
        log.info("Analyzing %s (%s)...", sub["display_name"], product_name)
        log.info("=" * 60)

        # Load posts with comments
        posts = get_posts_with_comments(conn, sub_id, period_start, period_end,
                                         max_posts=max_posts_per_sub)
        if not posts:
            log.warning("[%s] No posts found in time range", sub_id)
            done_subs.add(sub_id)
            # Check if this empty sub completes the product group
            subs_for_product = [s["id"] for s in product_to_subs[product_name]]
            remaining_subs = [s for s in subs_for_product if s not in done_subs]
            if not remaining_subs and product_name in _product_cache:
                pc = _product_cache[product_name]
                log.info("All subreddits for '%s' complete (last was empty): %s", product_name, pc["subreddits"])
                merged_posts = pc["all_posts"]
                merged_valid = pc["all_valid_posts"]
                merged_map = pc["merged_analysis_map"]
                merged_typical = pc["all_typical_ids"]
                sentiment_counts = {"positive": 0, "negative": 0, "neutral": 0, "mixed": 0}
                topic_counts = {t: 0 for t in TOPIC_TAXONOMY}
                scenario_counts = {s: 0 for s in SCENARIO_TAXONOMY}
                for a in merged_map.values():
                    if a.get("is_valid", True):
                        lbl = a.get("sentiment_label", "neutral")
                        sentiment_counts[lbl] = sentiment_counts.get(lbl, 0) + 1
                        tc = a.get("topic_category", "meta")
                        if tc in topic_counts:
                            topic_counts[tc] += 1
                        for st in a.get("scenario_tags", []):
                            if st in scenario_counts:
                                scenario_counts[st] += 1
                chart_data = {"sentiment": sentiment_counts, "topics": topic_counts, "scenarios": scenario_counts}
                product_chart_data[product_name] = chart_data
                global_typical = select_typical_posts(merged_posts, merged_map, count=10)
                typical_posts_data = []
                for tid in global_typical:
                    post = next((p for p in merged_posts if p["id"] == tid), None)
                    if post:
                        a = merged_map.get(tid, {})
                        typical_posts_data.append({
                            "id": tid, "title": post["title"],
                            "body": (post.get("body") or "")[:300],
                            "author": post.get("author", ""),
                            "url": post["url"],
                            "source_platform": post.get("source_platform") or "reddit",
                            "num_comments": post.get("num_comments", 0),
                            "created_utc": post.get("created_utc", ""),
                            "sentiment_label": a.get("sentiment_label", "neutral"),
                            "sentiment_score": a.get("sentiment_score", 0.0),
                            "sentiment_reason": a.get("sentiment_reason", ""),
                            "topic_category": a.get("topic_category", ""),
                    "key_points": a.get("key_points", []),
                            "scenario_tags": a.get("scenario_tags", []),
                        })
                subreddits_label = "+".join(pc["subreddits"])
                summary = generate_product_summary(
                    product_name, subreddits_label, merged_valid, merged_map,
                    period_start, period_end, endpoint, model,
                )
                product_summaries[product_name] = summary
                structured = extract_structured_insights(
                    product_name, merged_valid, merged_map, endpoint, model,
                )
                product_report = {
                    "product_name": product_name,
                    "subreddit": subreddits_label,
                    "subreddits": pc["subreddits"],
                    "post_count": pc["total_posts"],
                    "valid_post_count": pc["total_valid"],
                    "filtered_count": pc["total_filtered"],
                    "source_breakdown": compute_source_breakdown(merged_posts),
                    "summary": summary,
                    "sentiment_distribution": sentiment_counts,
                    "topic_distribution": topic_counts,
                    "scenario_distribution": scenario_counts,
                    "typical_posts": typical_posts_data,
                    "pain_points": structured.get("pain_points", []),
                    "strengths": structured.get("strengths", []),
                    "recommendations": structured.get("recommendations", []),
                    "keywords": structured.get("keywords", []),
                }
                all_report_data["products"].append(product_report)
                total_filtered += pc["total_filtered"]
            continue

        log.info("[%s] Loaded %d posts", sub_id, len(posts))

        # Step 1: Filter invalid content
        log.info("[%s] Step 1: Filtering content...", sub_id)
        filter_results = filter_posts(posts, endpoint, model)
        filter_map = {r["post_id"]: r for r in filter_results}
        valid_post_ids = {r["post_id"] for r in filter_results if r.get("valid", True)}
        filtered_count = len(posts) - len(valid_post_ids)
        total_filtered += filtered_count
        log.info("[%s] Filtered: %d invalid, %d valid", sub_id, filtered_count, len(valid_post_ids))

        valid_posts = [p for p in posts if p["id"] in valid_post_ids]

        # Step 2: Classify and analyze sentiment
        log.info("[%s] Step 2: Classifying and analyzing sentiment...", sub_id)
        analysis_results = classify_and_analyze(valid_posts, product_name, endpoint, model)
        analysis_map = {r["post_id"]: r for r in analysis_results}

        # Merge filter info into analysis map
        for pid, finfo in filter_map.items():
            if pid in analysis_map:
                analysis_map[pid]["is_valid"] = finfo.get("valid", True)
                analysis_map[pid]["filter_reason"] = finfo.get("reason", "")
            else:
                analysis_map[pid] = {
                    "post_id": pid,
                    "is_valid": finfo.get("valid", True),
                    "filter_reason": finfo.get("reason", ""),
                    "topic_category": "meta",
                    "sentiment_score": 0.0,
                    "sentiment_label": "neutral",
                    "sentiment_reason": "",
                    "key_points": [],
                }

        # Step 3: Select typical posts
        log.info("[%s] Step 3: Selecting typical posts...", sub_id)
        typical_ids = select_typical_posts(posts, analysis_map, count=5)
        for tid in typical_ids:
            if tid in analysis_map:
                analysis_map[tid]["is_typical"] = True

        # Save post analysis to DB (Steps 1-3 results)
        for pid, analysis in analysis_map.items():
            conn.execute(
                """INSERT OR REPLACE INTO post_analysis
                   (id, run_id, post_id, is_valid, filter_reason, topic_category,
                    sentiment_score, sentiment_label, sentiment_reason, key_points,
                    is_typical, typical_reason, scenario_tags)
                   VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
                (
                    str(uuid.uuid4())[:8],
                    run_id,
                    pid,
                    1 if analysis.get("is_valid", True) else 0,
                    analysis.get("filter_reason", ""),
                    analysis.get("topic_category", ""),
                    analysis.get("sentiment_score", 0.0),
                    analysis.get("sentiment_label", "neutral"),
                    analysis.get("sentiment_reason", ""),
                    json.dumps(analysis.get("key_points", []), ensure_ascii=False),
                    1 if analysis.get("is_typical") else 0,
                    analysis.get("typical_reason", ""),
                    json.dumps(analysis.get("scenario_tags", []), ensure_ascii=False),
                ),
            )
        conn.commit()

        # Cache per-subreddit data for merged product summary (Step 4/5)
        # Key: product_name → accumulated posts + analysis_map across all its subreddits
        if product_name not in _product_cache:
            _product_cache[product_name] = {
                "all_posts": [],
                "all_valid_posts": [],
                "merged_analysis_map": {},
                "all_typical_ids": [],
                "total_posts": 0,
                "total_valid": 0,
                "total_filtered": 0,
                "subreddits": [],
            }
        pc = _product_cache[product_name]
        pc["all_posts"].extend(posts)
        pc["all_valid_posts"].extend(valid_posts)
        pc["merged_analysis_map"].update(analysis_map)
        pc["all_typical_ids"].extend(typical_ids)
        pc["total_posts"] += len(posts)
        pc["total_valid"] += len(valid_posts)
        pc["total_filtered"] += filtered_count
        pc["subreddits"].append(sub_id)
        total_posts_analyzed += len(posts)

        # Check if this product's last subreddit just finished
        subs_for_product = [s["id"] for s in product_to_subs[product_name]]
        remaining_subs = [s for s in subs_for_product if s not in done_subs and s != sub_id]
        # Mark current sub as done for checkpoint tracking purposes
        done_subs.add(sub_id)

        if remaining_subs:
            log.info("[%s] Product '%s' has more subreddits pending: %s — deferring Step 4/5",
                     sub_id, product_name, remaining_subs)
            log.info("[%s] Per-subreddit analysis complete (Step 4/5 deferred)", sub_id)
            continue

        # All subreddits for this product are done — run Step 4/5 now
        log.info("=" * 60)
        log.info("All subreddits for '%s' complete: %s", product_name, pc["subreddits"])
        log.info("Merged: %d posts (%d valid, %d filtered)", pc["total_posts"], pc["total_valid"], pc["total_filtered"])

        merged_posts = pc["all_posts"]
        merged_valid = pc["all_valid_posts"]
        merged_map = pc["merged_analysis_map"]
        merged_typical = pc["all_typical_ids"]

        # Build chart data from merged
        sentiment_counts = {"positive": 0, "negative": 0, "neutral": 0, "mixed": 0}
        topic_counts = {t: 0 for t in TOPIC_TAXONOMY}
        scenario_counts = {s: 0 for s in SCENARIO_TAXONOMY}
        for a in merged_map.values():
            if a.get("is_valid", True):
                lbl = a.get("sentiment_label", "neutral")
                sentiment_counts[lbl] = sentiment_counts.get(lbl, 0) + 1
                tc = a.get("topic_category", "meta")
                if tc in topic_counts:
                    topic_counts[tc] += 1
                for st in a.get("scenario_tags", []):
                    if st in scenario_counts:
                        scenario_counts[st] += 1

        chart_data = {"sentiment": sentiment_counts, "topics": topic_counts, "scenarios": scenario_counts}
        product_chart_data[product_name] = chart_data

        # Build typical posts data from merged.
        # Re-rank across the FULL merged pool (all subreddits incl. non-reddit
        # sources) instead of concatenating per-subreddit top-5s, so high-value
        # posts from any source can surface (the old [:10] truncation dropped
        # later-iterated subs entirely).
        global_typical = select_typical_posts(merged_posts, merged_map, count=10)
        typical_posts_data = []
        for tid in global_typical:
            post = next((p for p in merged_posts if p["id"] == tid), None)
            if post:
                a = merged_map.get(tid, {})
                typical_posts_data.append({
                    "id": tid,
                    "title": post["title"],
                    "body": (post.get("body") or "")[:300],
                    "author": post.get("author", ""),
                    "url": post["url"],
                    "source_platform": post.get("source_platform") or "reddit",
                    "num_comments": post.get("num_comments", 0),
                    "created_utc": post.get("created_utc", ""),
                    "sentiment_label": a.get("sentiment_label", "neutral"),
                    "sentiment_score": a.get("sentiment_score", 0.0),
                    "sentiment_reason": a.get("sentiment_reason", ""),
                    "topic_category": a.get("topic_category", ""),
                    "key_points": a.get("key_points", []),
                })

        # Step 4: Generate product summary (merged across all subreddits)
        subreddits_label = "+".join(pc["subreddits"])
        log.info("[%s] Step 4: Generating merged product summary...", subreddits_label)
        summary = generate_product_summary(
            product_name, subreddits_label, merged_valid, merged_map,
            period_start, period_end, endpoint, model,
        )
        product_summaries[product_name] = summary

        # Step 5: Extract structured insights (merged across all subreddits)
        log.info("[%s] Step 5: Extracting merged structured insights...", subreddits_label)
        structured = extract_structured_insights(
            product_name, merged_valid, merged_map, endpoint, model,
        )

        # Build merged product report (one entry per product, not per subreddit)
        product_report = {
            "product_name": product_name,
            "subreddit": subreddits_label,
            "subreddits": pc["subreddits"],
            "post_count": pc["total_posts"],
            "valid_post_count": pc["total_valid"],
            "filtered_count": pc["total_filtered"],
            "source_breakdown": compute_source_breakdown(merged_posts),
            "summary": summary,
            "sentiment_distribution": sentiment_counts,
            "topic_distribution": topic_counts,
            "typical_posts": typical_posts_data,
            "pain_points": structured.get("pain_points", []),
            "strengths": structured.get("strengths", []),
            "recommendations": structured.get("recommendations", []),
            "keywords": structured.get("keywords", []),
        }
        all_report_data["products"].append(product_report)
        total_filtered += pc["total_filtered"]

        # Save per-product report to DB
        conn.execute(
            """INSERT INTO reports (id, run_id, product_name, report_type, title,
               summary_text, report_json, chart_data_json, created_at)
               VALUES (?, ?, ?, 'product_summary', ?, ?, ?, ?, ?)""",
            (
                str(uuid.uuid4())[:8],
                run_id,
                product_name,
                f"{product_name} Reddit Community Report",
                summary,
                json.dumps(product_report, ensure_ascii=False),
                json.dumps(chart_data, ensure_ascii=False),
                now.isoformat(),
            ),
        )
        conn.commit()
        log.info("[%s] Product '%s' fully complete", subreddits_label, product_name)

    # Step 6: Cross-product comparison
    if len(product_summaries) > 1:
        log.info("=" * 60)
        log.info("Generating cross-product comparison...")
        log.info("=" * 60)
        comparison = generate_cross_product_comparison(product_summaries, endpoint, model)
        all_report_data["cross_product_comparison"] = comparison
        all_report_data["chart_data"] = product_chart_data

        conn.execute(
            """INSERT INTO reports (id, run_id, product_name, report_type, title,
               summary_text, report_json, chart_data_json, created_at)
               VALUES (?, ?, NULL, 'cross_product', ?, ?, ?, ?, ?)""",
            (
                str(uuid.uuid4())[:8],
                run_id,
                "Cross-Product Competitive Comparison",
                comparison,
                json.dumps(all_report_data, ensure_ascii=False),
                json.dumps(product_chart_data, ensure_ascii=False),
                now.isoformat(),
            ),
        )
        conn.commit()

    # Update analysis run status
    conn.execute(
        """UPDATE analysis_runs
           SET completed_at = ?, status = 'completed',
               total_posts = ?, filtered_posts = ?
           WHERE id = ?""",
        (datetime.now(timezone.utc).isoformat(), total_posts_analyzed, total_filtered, run_id),
    )
    conn.commit()

    # Save report JSON to file
    REPORTS_DIR.mkdir(parents=True, exist_ok=True)
    report_json_str = json.dumps(all_report_data, indent=2, ensure_ascii=False)

    if scratch:
        # ISOLATION MODE: write ONLY to the explicit scratch output path.
        # Never touch latest.json, index.json, or wwwroot (deployed artifacts).
        if output_path:
            report_path = Path(output_path)
        else:
            report_path = REPORTS_DIR / f"_SCRATCH_report_{run_id}_{now.strftime('%Y%m%d_%H%M%S')}.json"
        report_path.parent.mkdir(parents=True, exist_ok=True)
        report_path.write_text(report_json_str, encoding="utf-8")
        log.info("SCRATCH mode: wrote report to %s (no latest.json/index.json/wwwroot sync)", report_path)
    else:
        report_filename = f"report_{run_id}_{now.strftime('%Y%m%d_%H%M%S')}.json"
        report_path = Path(output_path) if output_path else (REPORTS_DIR / report_filename)
        report_path.write_text(report_json_str, encoding="utf-8")

        # Also update latest.json
        (REPORTS_DIR / "latest.json").write_text(report_json_str, encoding="utf-8")

        # Generate index.json for all reports
        _generate_report_index(REPORTS_DIR)

        # Sync to wwwroot for frontend
        wwwroot_reports = Path(__file__).resolve().parent.parent / "wwwroot" / "data" / "reports"
        if wwwroot_reports.exists():
            import shutil
            shutil.copy2(report_path, wwwroot_reports / report_path.name)
            (wwwroot_reports / "latest.json").write_text(report_json_str, encoding="utf-8")
            shutil.copy2(REPORTS_DIR / "index.json", wwwroot_reports / "index.json")
            log.info("Synced report to wwwroot")

    conn.close()

    log.info("=" * 60)
    log.info("ANALYSIS COMPLETE")
    log.info("=" * 60)
    log.info("Run ID: %s", run_id)
    log.info("Posts analyzed: %d (filtered: %d)", total_posts_analyzed, total_filtered)
    log.info("Products: %s", ", ".join(product_summaries.keys()))
    log.info("Report saved: %s", report_path)
    log.info("Database: %s", active_db)

    return all_report_data


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def main():
    parser = argparse.ArgumentParser(description="Reddit Competitive Intelligence Analyzer")
    parser.add_argument("--days", type=int, default=14, help="Analysis time window in days (default: 14)")
    parser.add_argument("--llm-endpoint", default=LLM_ENDPOINT, help="LLM API endpoint")
    parser.add_argument("--model", default=LLM_MODEL, help="LLM model name")
    parser.add_argument("--resume", default=None, metavar="RUN_ID",
                        help="Resume an interrupted run by its run_id (skips already-completed subreddits)")
    parser.add_argument("--start-date", default=None, metavar="YYYY-MM-DD",
                        help="Analysis period start date (overrides --days)")
    parser.add_argument("--end-date", default=None, metavar="YYYY-MM-DD",
                        help="Analysis period end date, inclusive (overrides --days)")
    parser.add_argument("--max-posts-per-sub", type=int, default=0, metavar="N",
                        help="Max posts per subreddit (0=all, sorted by score desc)")
    parser.add_argument("--db", default=None, metavar="PATH",
                        help="Source SQLite DB (default: data/reddit.db). Use for isolated/scratch runs.")
    parser.add_argument("--output", default=None, metavar="PATH",
                        help="Explicit report output path (default: data/reports/report_<id>_<ts>.json)")
    parser.add_argument("--scratch", action="store_true",
                        help="Isolation mode: write ONLY to --output (or a _SCRATCH_ file); "
                             "skip latest.json, index.json, and wwwroot sync.")
    args = parser.parse_args()

    # Auto-detect working LLM endpoint if user didn't override
    endpoint = args.llm_endpoint
    model = args.model
    if endpoint == LLM_ENDPOINT and model == LLM_MODEL:
        for fb_endpoint, fb_model in LLM_FALLBACKS:
            try:
                r = requests.get(f"{fb_endpoint}/v1/models", timeout=5)
                if r.status_code == 200:
                    endpoint = fb_endpoint
                    model = fb_model
                    log.info("Using LLM: %s (model: %s)", endpoint, model)
                    break
            except Exception:
                continue
        else:
            log.error("No LLM endpoint available. Tried: %s", [f[0] for f in LLM_FALLBACKS])
            sys.exit(1)

    active_db = Path(args.db) if args.db else DB_PATH
    if not active_db.exists():
        log.error("Database not found at %s. Run scrape.py first.", active_db)
        sys.exit(1)

    run_analysis(
        days=args.days,
        endpoint=endpoint,
        model=model,
        resume_run_id=args.resume,
        start_date=args.start_date,
        end_date=args.end_date,
        max_posts_per_sub=args.max_posts_per_sub,
        db_path=active_db,
        output_path=args.output,
        scratch=args.scratch,
    )


if __name__ == "__main__":
    main()
