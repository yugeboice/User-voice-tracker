#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
collect_hackernews.py — Hacker News collector for the competitive-analysis pipeline.

Collects Hacker News stories (+ top comments) that mention the 5 tracked products
(ChatGPT / Claude / Gemini / GitHub Copilot / M365 Copilot) within a date range,
using the public HN Algolia API (no API key required), and writes them into a
SQLite DB whose `posts` / `comments` / `subreddits` tables are field-compatible
with the production `data/reddit.db` so the rows could later be merged in.

IMPORTANT (verification phase):
  * Default --db is data/_test_hn.db (a scratch DB). NEVER point this at
    data/reddit.db while testing — that is the deployed production database.
  * This script only CREATES/append-writes to the target DB; it never touches
    reddit.db.

HN Algolia API:
  search:  https://hn.algolia.com/api/v1/search_by_date
  item:    https://hn.algolia.com/api/v1/items/{id}

Schema note: production `posts` has NO `source_platform` column. We ADD one in the
scratch DB (filled with 'hackernews') so HN rows are self-identifying; a future
merge into reddit.db would `ALTER TABLE posts ADD COLUMN source_platform` first.
"""

import argparse
import datetime as dt
import html
import re
import sqlite3
import sys
import time

import requests

# --------------------------------------------------------------------------- #
# Product classification config
#
# Strategy: two-stage = broad RECALL via Algolia queries, then precise LOCAL
# include/exclude filtering to keep false positives out. A story is finally
# assigned to exactly ONE product (the most prominently mentioned) so that
# posts.id (a PRIMARY KEY) stays unique and per-product counts don't double-count.
# --------------------------------------------------------------------------- #

PRODUCTS = {
    "ChatGPT": {
        "sub_id": "hn_chatgpt",
        "product_name": "ChatGPT",
        # Sent to Algolia (AND-matched per query, deduped across queries):
        "queries": ["ChatGPT", "GPT-5", "GPT-4", "OpenAI GPT"],
        # Local precision filter — text (title+body, lowercased) must match >=1:
        "include": [r"chatgpt", r"gpt-5", r"gpt-4", r"openai gpt", r"\bgpt\b.*openai"],
        "exclude": [],
    },
    "Claude": {
        "sub_id": "hn_claude",
        "product_name": "Claude",
        "queries": ["Anthropic Claude", "Claude AI", "Claude Opus", "Claude Sonnet", "Claude Code"],
        # Bare \bclaude\b is the catch-all (Anthropic's Claude dominates HN's
        # vocabulary); the famous non-AI "Claude"s are gated by `exclude` below,
        # which classify() checks FIRST. This catches "Claude Code", "Claude
        # Fable", "Claude chats", "Claude 3.5", etc. without per-suffix listing.
        "include": [r"\bclaude\b"],
        "exclude": [
            r"claude shannon", r"claude monet", r"claude debussy",
            r"claude lévi", r"claude lanzmann", r"jean-claude",
        ],
    },
    "Gemini": {
        "sub_id": "hn_gemini",
        "product_name": "Gemini",
        "queries": ["Google Gemini", "Gemini AI", "Gemini Pro", "Gemini model"],
        "include": [
            r"google\s*gemini", r"gemini\s*(ai|pro|advanced|flash|ultra|nano|[0-9])",
            r"gemini.*\b(google|llm|model|chatbot|deepmind)\b",
        ],
        # Kill the crypto exchange / network protocol / zodiac false positives.
        "exclude": [
            r"gemini\s*(exchange|crypto|protocol|network|earn|dollar|trust|space)",
            r"winklevoss", r"zodiac", r"horoscope", r"gemini man",
        ],
    },
    "Copilot": {  # == GitHub Copilot (production product_name is "GitHub Copilot")
        "sub_id": "hn_copilot",
        "product_name": "GitHub Copilot",
        "queries": ["GitHub Copilot", "Copilot coding", "Copilot autocomplete"],
        # Bare "Copilot" is too broad → require GitHub context or a coding context.
        "include": [
            r"github\s*copilot",
            r"copilot.*\b(coding|code|autocomplete|ide|vscode|vs code|visual studio|programming|developer|pull request|cli)\b",
            r"\b(coding|code|ide|vscode|developer)\b.*copilot",
        ],
        # Don't steal Microsoft-365 Copilot stories.
        "exclude": [r"microsoft\s*365", r"m365", r"office\s*365", r"\bbizchat\b"],
    },
    "M365 Copilot": {
        "sub_id": "hn_m365",
        "product_name": "M365 Copilot",
        "queries": ["Microsoft 365 Copilot", "M365 Copilot", "Microsoft Copilot", "Office Copilot"],
        "include": [
            r"microsoft\s*365\s*copilot", r"m365\s*copilot", r"office\s*365\s*copilot",
            r"microsoft\s*copilot",
            r"copilot.*\b(microsoft 365|m365|office 365|outlook|excel|word|powerpoint|teams|sharepoint|bizchat)\b",
            r"\b(microsoft 365|m365|office 365|outlook|excel|powerpoint|sharepoint|bizchat)\b.*copilot",
        ],
        "exclude": [r"github\s*copilot"],
    },
}

# Order used only as a deterministic tie-break when a story is equally prominent
# for >1 product (more specific / enterprise products win ties).
TIE_BREAK = ["M365 Copilot", "Copilot", "Claude", "Gemini", "ChatGPT"]

SEARCH_URL = "https://hn.algolia.com/api/v1/search_by_date"
ITEM_URL = "https://hn.algolia.com/api/v1/items/{}"
HITS_PER_PAGE = 100
MAX_PAGES = 11           # Algolia hard-caps paging at ~1000 hits anyway
DEFAULT_MAX_COMMENTS = 5
TAG_RE = re.compile(r"<[^>]+>")


# --------------------------------------------------------------------------- #
# Helpers
# --------------------------------------------------------------------------- #

def log(msg):
    print(msg, flush=True)


def make_session():
    s = requests.Session()
    s.headers.update({"User-Agent": "competitive-analysis-hn-collector/1.0 (+internal)"})
    try:
        from requests.adapters import HTTPAdapter
        from urllib3.util.retry import Retry
        retry = Retry(total=4, backoff_factor=0.6,
                      status_forcelist=[429, 500, 502, 503, 504],
                      allowed_methods=["GET"])
        s.mount("https://", HTTPAdapter(max_retries=retry))
    except Exception:
        pass
    return s


def to_epoch(date_str, end_of_day=False):
    d = dt.datetime.strptime(date_str, "%Y-%m-%d").replace(tzinfo=dt.timezone.utc)
    if end_of_day:
        d = d.replace(hour=23, minute=59, second=59)
    return int(d.timestamp())


def epoch_to_iso(ts):
    """Match production created_utc format, e.g. 2026-06-21T17:40:19+00:00."""
    return dt.datetime.fromtimestamp(int(ts), tz=dt.timezone.utc).isoformat()


def now_iso():
    return dt.datetime.now(dt.timezone.utc).isoformat()


def html_to_text(s):
    if not s:
        return ""
    s = s.replace("<p>", "\n\n").replace("</p>", "")
    s = TAG_RE.sub("", s)
    return html.unescape(s).strip()


def matches_any(text, patterns):
    return any(re.search(p, text) for p in patterns)


def _best_match(text):
    """Return (best_product or None, matched_list) for one lowercased blob.

    A product matches if (>=1 include hits) and (no exclude hits). When several
    match, pick the one whose keyword appears earliest (most prominent); ties
    broken by TIE_BREAK order.
    """
    matched = []
    for prod, cfg in PRODUCTS.items():
        if cfg["exclude"] and matches_any(text, cfg["exclude"]):
            continue
        if matches_any(text, cfg["include"]):
            matched.append(prod)
    if not matched:
        return None, []
    if len(matched) == 1:
        return matched[0], matched

    def prominence(prod):
        first = min(
            (m.start() for pat in PRODUCTS[prod]["include"]
             for m in [re.search(pat, text)] if m),
            default=10 ** 9,
        )
        return (first, TIE_BREAK.index(prod))

    return min(matched, key=prominence), matched


def classify(title, body):
    """Title-priority classification → (product|None, scope, all_matched).

    A product named in the TITLE is the post's subject (STRONG signal). Only if
    no product appears in the title do we fall back to the body (WEAK signal —
    catches Ask/Show HN posts that discuss a product in the text, but also
    tangential "I built this with Claude Code" mentions). `scope` ('title' or
    'body') records which, so the report can weight signal strength honestly.
    """
    prod, matched = _best_match(title.lower())
    if prod:
        return prod, "title", matched
    prod, matched = _best_match(f"{title}\n{body}".lower())
    if prod:
        return prod, "body", matched
    return None, None, []


def search_product(session, queries, start_ts, end_ts):
    """Run all Algolia queries for a product, return {objectID: hit} deduped."""
    found = {}
    numeric = f"created_at_i>={start_ts},created_at_i<={end_ts}"
    for q in queries:
        page = 0
        while page < MAX_PAGES:
            params = {
                "query": q,
                "tags": "story",
                "numericFilters": numeric,
                "hitsPerPage": HITS_PER_PAGE,
                "page": page,
            }
            r = session.get(SEARCH_URL, params=params, timeout=30)
            r.raise_for_status()
            data = r.json()
            hits = data.get("hits", [])
            for h in hits:
                oid = h.get("objectID")
                cts = h.get("created_at_i")
                if not oid or cts is None:
                    continue
                if not (start_ts <= int(cts) <= end_ts):
                    continue  # defensive: enforce range locally too
                found.setdefault(oid, h)
            nb_pages = data.get("nbPages", 0)
            page += 1
            if page >= nb_pages:
                break
            time.sleep(0.05)
    return found


def fetch_top_comments(session, object_id, max_comments):
    """Fetch up to max_comments visible comments (BFS = top-level first)."""
    try:
        r = session.get(ITEM_URL.format(object_id), timeout=30)
        r.raise_for_status()
        item = r.json()
    except Exception as e:
        log(f"    ! comment fetch failed for {object_id}: {e}")
        return []

    out = []
    # Breadth-first so depth-0 (most visible) comments come first.
    queue = [(c, 0, None) for c in (item.get("children") or [])]
    while queue and len(out) < max_comments:
        node, depth, parent_hn_id = queue.pop(0)
        text = html_to_text(node.get("text"))
        cid = node.get("id")
        if cid and text:  # skip deleted/empty
            out.append({
                "id": f"hn_c_{cid}",
                "parent_id": parent_hn_id,
                "author": node.get("author"),
                "body": text,
                "score": node.get("points") or 0,
                "created_utc": epoch_to_iso(node["created_at_i"]) if node.get("created_at_i") else now_iso(),
                "depth": depth,
            })
        for child in (node.get("children") or []):
            queue.append((child, depth + 1, f"hn_c_{cid}" if cid else parent_hn_id))
    return out


# --------------------------------------------------------------------------- #
# DB
# --------------------------------------------------------------------------- #

def init_db(conn):
    cur = conn.cursor()
    cur.execute("""
        CREATE TABLE IF NOT EXISTS subreddits (
            id TEXT PRIMARY KEY,
            product_name TEXT NOT NULL,
            display_name TEXT NOT NULL,
            url TEXT NOT NULL,
            last_scraped_at TEXT
        )""")
    cur.execute("""
        CREATE TABLE IF NOT EXISTS posts (
            id TEXT PRIMARY KEY,
            subreddit_id TEXT NOT NULL,
            title TEXT NOT NULL,
            body TEXT,
            author TEXT,
            score INTEGER,
            upvote_ratio REAL,
            num_comments INTEGER,
            url TEXT NOT NULL,
            external_url TEXT,
            created_utc TEXT NOT NULL,
            scraped_at TEXT NOT NULL,
            flair TEXT,
            post_type TEXT,
            source_type TEXT,
            origin_subreddit TEXT,
            source_platform TEXT
        )""")
    cur.execute("""
        CREATE TABLE IF NOT EXISTS comments (
            id TEXT PRIMARY KEY,
            post_id TEXT NOT NULL,
            parent_id TEXT,
            author TEXT,
            body TEXT NOT NULL,
            score INTEGER,
            created_utc TEXT NOT NULL,
            scraped_at TEXT NOT NULL,
            depth INTEGER
        )""")
    conn.commit()


def upsert_subreddits(conn, scraped_at):
    cur = conn.cursor()
    for prod, cfg in PRODUCTS.items():
        cur.execute(
            """INSERT INTO subreddits (id, product_name, display_name, url, last_scraped_at)
               VALUES (?,?,?,?,?)
               ON CONFLICT(id) DO UPDATE SET last_scraped_at=excluded.last_scraped_at""",
            (cfg["sub_id"], cfg["product_name"], f"HN: {prod}",
             "https://news.ycombinator.com/", scraped_at),
        )
    conn.commit()


def insert_post(conn, hit, product, scraped_at):
    cfg = PRODUCTS[product]
    oid = hit["objectID"]
    link = hit.get("url")  # external link target (None for Ask/Show-text posts)
    body = html_to_text(hit.get("story_text"))
    post = {
        "id": f"hn_{oid}",
        "subreddit_id": cfg["sub_id"],
        "title": hit.get("title") or "(no title)",
        "body": body,
        "author": hit.get("author"),
        "score": hit.get("points") or 0,
        "upvote_ratio": None,  # HN has no upvote ratio
        "num_comments": hit.get("num_comments") or 0,
        "url": f"https://news.ycombinator.com/item?id={oid}",  # HN discussion permalink
        "external_url": link,
        "created_utc": epoch_to_iso(hit["created_at_i"]),
        "scraped_at": scraped_at,
        "flair": None,
        "post_type": "self" if not link else "link",
        "source_type": "keyword_search",
        "origin_subreddit": None,
        "source_platform": "hackernews",
    }
    conn.execute(
        """INSERT OR REPLACE INTO posts
           (id, subreddit_id, title, body, author, score, upvote_ratio, num_comments,
            url, external_url, created_utc, scraped_at, flair, post_type,
            source_type, origin_subreddit, source_platform)
           VALUES (:id,:subreddit_id,:title,:body,:author,:score,:upvote_ratio,
            :num_comments,:url,:external_url,:created_utc,:scraped_at,:flair,
            :post_type,:source_type,:origin_subreddit,:source_platform)""",
        post,
    )
    return post


def insert_comments(conn, post_id, comments, scraped_at):
    for c in comments:
        c["parent_id"] = c["parent_id"] or None
        conn.execute(
            """INSERT OR REPLACE INTO comments
               (id, post_id, parent_id, author, body, score, created_utc, scraped_at, depth)
               VALUES (?,?,?,?,?,?,?,?,?)""",
            (c["id"], post_id, c["parent_id"], c["author"], c["body"],
             c["score"], c["created_utc"], scraped_at, c["depth"]),
        )


# --------------------------------------------------------------------------- #
# Main
# --------------------------------------------------------------------------- #

def main():
    ap = argparse.ArgumentParser(description="Collect Hacker News mentions of tracked products.")
    ap.add_argument("--start-date", required=True, help="YYYY-MM-DD (inclusive, UTC)")
    ap.add_argument("--end-date", required=True, help="YYYY-MM-DD (inclusive, UTC)")
    ap.add_argument("--db", default="data/_test_hn.db", help="target SQLite DB (default: scratch)")
    ap.add_argument("--max-comments", type=int, default=DEFAULT_MAX_COMMENTS)
    ap.add_argument("--no-comments", action="store_true", help="skip comment collection")
    args = ap.parse_args()

    # Hard guard: refuse to write the production DB.
    if args.db.replace("\\", "/").endswith("data/reddit.db"):
        log("REFUSING to write production DB data/reddit.db. Use a scratch DB.")
        sys.exit(2)

    start_ts = to_epoch(args.start_date)
    end_ts = to_epoch(args.end_date, end_of_day=True)
    scraped_at = now_iso()
    session = make_session()

    log(f"HN collection window: {args.start_date} .. {args.end_date} "
        f"(epoch {start_ts}..{end_ts}) -> {args.db}")

    conn = sqlite3.connect(args.db)
    try:
        init_db(conn)
        upsert_subreddits(conn, scraped_at)

        # 1) Recall + dedup per product, then resolve final single classification.
        #    Collect all hits globally first to classify consistently.
        global_hits = {}      # objectID -> hit
        recall_by_prod = {}   # product -> set(objectID) (pre-classification recall)
        for prod, cfg in PRODUCTS.items():
            hits = search_product(session, cfg["queries"], start_ts, end_ts)
            recall_by_prod[prod] = set(hits.keys())
            for oid, h in hits.items():
                global_hits.setdefault(oid, h)
            log(f"  [{prod}] recall hits: {len(hits)}")

        # 2) Classify each unique story to exactly one product (title-priority).
        assigned = {p: [] for p in PRODUCTS}
        scope_counts = {p: {"title": 0, "body": 0} for p in PRODUCTS}
        multi = 0
        for oid, h in global_hits.items():
            title = h.get("title") or ""
            body = html_to_text(h.get("story_text"))
            prod, scope, all_m = classify(title, body)
            if prod is None:
                continue
            if len(all_m) > 1:
                multi += 1
            assigned[prod].append(h)
            scope_counts[prod][scope] += 1

        # 3) Write posts + comments.
        totals = {}
        comment_stats = {"posts_with_comments": 0, "comments_written": 0}
        for prod, hits in assigned.items():
            written = 0
            for h in hits:
                insert_post(conn, h, prod, scraped_at)
                if not args.no_comments and (h.get("num_comments") or 0) > 0:
                    cmts = fetch_top_comments(session, h["objectID"], args.max_comments)
                    if cmts:
                        insert_comments(conn, f"hn_{h['objectID']}", cmts, scraped_at)
                        comment_stats["posts_with_comments"] += 1
                        comment_stats["comments_written"] += len(cmts)
                    time.sleep(0.05)
                written += 1
            totals[prod] = written
            conn.commit()
            log(f"  [{prod}] posts written: {written}")

        log("\n=== SUMMARY ===")
        log(f"window         : {args.start_date} .. {args.end_date}")
        log(f"unique stories : {len(global_hits)}")
        log(f"classified     : {sum(totals.values())}  (multi-product mentions: {multi})")
        log(f"{'product':16}{'total':>7}{'title':>7}{'body':>7}   (title=strong signal, body=weak/tangential)")
        for prod in PRODUCTS:
            sc = scope_counts[prod]
            log(f"  {prod:14}{totals.get(prod, 0):>7}{sc['title']:>7}{sc['body']:>7}")
        log(f"posts w/ comments: {comment_stats['posts_with_comments']}")
        log(f"comments written : {comment_stats['comments_written']}")
        log(f"db             : {args.db}")
    finally:
        conn.close()


if __name__ == "__main__":
    main()
