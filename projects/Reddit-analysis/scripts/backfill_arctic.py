"""
Backfill Reddit posts and top-N comments from Arctic Shift API.

Arctic Shift (https://arctic-shift.photon-reddit.com) provides historical
Reddit data with real upvote scores — unlike RSS which returns score=0.

Usage:
    python backfill_arctic.py --start-date 2026-03-02 --end-date 2026-03-15

IMPORTANT: This script should be used when RSS scraping cannot reach far
enough back in time (typically >7 days for high-volume subreddits).
"""

import argparse
import json
import logging
import os
import sqlite3
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

# Use curl for HTTP requests (urllib gets 403 from Arctic Shift)
# Arctic Shift blocks Python urllib User-Agent but allows curl

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
    datefmt="%H:%M:%S",
)
log = logging.getLogger("backfill")

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

ARCTIC_SHIFT_BASE = "https://arctic-shift.photon-reddit.com/api"
PAGE_SIZE = 100  # max per request
COMMENTS_PER_POST = 5  # top N comments to fetch per post
REQUEST_DELAY = 4  # seconds between API calls to avoid rate limiting
COMMENT_DELAY = 1  # seconds between comment fetches

DB_DIR = Path(__file__).resolve().parent.parent / "data"
DB_PATH = DB_DIR / "reddit.db"

# Product keyword patterns for cross-subreddit search
PRODUCT_KEYWORDS = {
    "ChatGPT": ["ChatGPT", "GPT-4", "GPT-4o", "GPT-5", "OpenAI"],
    "Claude": ["Claude AI", "Anthropic", "Claude Sonnet", "Claude Opus"],
    "Gemini": ["Google Gemini", "Gemini AI", "Gemini Pro"],
    "Copilot": ["Microsoft Copilot", "Windows Copilot", "Copilot Pro"],
    "M365 Copilot": ["Microsoft 365 Copilot", "Copilot for Microsoft 365", "Office Copilot"],
}

# General subreddits to search for AI product mentions (beyond dedicated product subs)
CROSS_SEARCH_SUBREDDITS = [
    "technology", "artificial", "MachineLearning", "singularity",
    "Futurology", "software", "productivity", "ArtificialInteligence",
    "OpenAI", "LocalLLaMA", "programming", "webdev", "datascience",
    "SaaS", "Entrepreneur", "smallbusiness", "coding",
]

# Same subreddit map as scrape.py
SUBREDDIT_MAP = {
    "ChatGPT": {"product": "ChatGPT", "display": "r/ChatGPT"},
    "ChatGPTcomplaints": {"product": "ChatGPT", "display": "r/ChatGPTcomplaints"},
    "OpenAI": {"product": "ChatGPT", "display": "r/OpenAI"},
    "ClaudeAI": {"product": "Claude", "display": "r/ClaudeAI"},
    "claude": {"product": "Claude", "display": "r/claude"},
    "GeminiAI": {"product": "Gemini", "display": "r/GeminiAI"},
    "GoogleGeminiAI": {"product": "Gemini", "display": "r/GoogleGeminiAI"},
    "GithubCopilot": {"product": "GitHub Copilot", "display": "r/GithubCopilot"},
    "MicrosoftCopilot": {"product": "M365 Copilot", "display": "r/MicrosoftCopilot"},
    "microsoft_365_copilot": {"product": "M365 Copilot", "display": "r/microsoft_365_copilot"},
}


# ---------------------------------------------------------------------------
# HTTP via curl (Arctic Shift blocks Python urllib)
# ---------------------------------------------------------------------------

def curl_get_json(url: str, retries: int = 3) -> dict | None:
    """Fetch JSON from URL using curl subprocess.

    Uses a temp file for large responses to avoid Windows subprocess.PIPE
    truncation on payloads > 64 KB.
    """
    import tempfile
    for attempt in range(retries):
        tmp_path = None
        try:
            fd, tmp_path = tempfile.mkstemp(suffix=".json")
            os.close(fd)
            result = subprocess.run(
                ["curl", "-s", "--max-time", "60", "-o", tmp_path, url],
                stderr=subprocess.PIPE,
                timeout=65,
            )
            if result.returncode == 0:
                with open(tmp_path, "r", encoding="utf-8", errors="replace") as f:
                    raw = f.read()
                if raw.strip():
                    return json.loads(raw)
        except (subprocess.TimeoutExpired, json.JSONDecodeError) as e:
            log.warning("Attempt %d failed for %s: %s", attempt + 1, url[:80], e)
        except Exception as e:
            log.warning("Attempt %d error for %s: %s", attempt + 1, url[:80], e)
        finally:
            if tmp_path:
                try:
                    os.remove(tmp_path)
                except OSError:
                    pass
        if attempt < retries - 1:
            time.sleep(REQUEST_DELAY * (attempt + 1))
    return None


# ---------------------------------------------------------------------------
# Database
# ---------------------------------------------------------------------------

def get_db() -> sqlite3.Connection:
    conn = sqlite3.connect(str(DB_PATH), timeout=30)
    conn.row_factory = sqlite3.Row
    conn.executescript("""
        CREATE TABLE IF NOT EXISTS subreddits (
            id              TEXT PRIMARY KEY,
            product_name    TEXT NOT NULL,
            display_name    TEXT NOT NULL,
            url             TEXT NOT NULL,
            last_scraped_at TEXT
        );
        CREATE TABLE IF NOT EXISTS posts (
            id              TEXT PRIMARY KEY,
            subreddit_id    TEXT NOT NULL REFERENCES subreddits(id),
            title           TEXT NOT NULL,
            body            TEXT,
            author          TEXT,
            score           INTEGER DEFAULT 0,
            upvote_ratio    REAL,
            num_comments    INTEGER DEFAULT 0,
            url             TEXT NOT NULL,
            external_url    TEXT,
            created_utc     TEXT NOT NULL,
            scraped_at      TEXT NOT NULL,
            flair           TEXT,
            post_type       TEXT,
            source_type     TEXT DEFAULT 'official_sub',
            origin_subreddit TEXT
        );
        CREATE TABLE IF NOT EXISTS comments (
            id              TEXT PRIMARY KEY,
            post_id         TEXT NOT NULL REFERENCES posts(id),
            parent_id       TEXT,
            author          TEXT,
            body            TEXT NOT NULL,
            score           INTEGER DEFAULT 0,
            created_utc     TEXT NOT NULL,
            scraped_at      TEXT NOT NULL,
            depth           INTEGER DEFAULT 0
        );
    """)
    # Migration: add source_type/origin_subreddit if missing
    cols = {r[1] for r in conn.execute("PRAGMA table_info(posts)").fetchall()}
    if "source_type" not in cols:
        conn.execute("ALTER TABLE posts ADD COLUMN source_type TEXT DEFAULT 'official_sub'")
    if "origin_subreddit" not in cols:
        conn.execute("ALTER TABLE posts ADD COLUMN origin_subreddit TEXT")
    conn.commit()
    return conn


def upsert_post(conn: sqlite3.Connection, post: dict) -> bool:
    """Insert or update a post. Returns True if new."""
    try:
        conn.execute(
            """INSERT INTO posts (id, subreddit_id, title, body, author, score,
               upvote_ratio, num_comments, url, external_url, created_utc,
               scraped_at, flair, post_type, source_type, origin_subreddit)
               VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
               ON CONFLICT(id) DO UPDATE SET
                   score = excluded.score,
                   upvote_ratio = excluded.upvote_ratio,
                   num_comments = excluded.num_comments,
                   body = CASE WHEN excluded.body != '' AND excluded.body IS NOT NULL
                               THEN excluded.body ELSE posts.body END""",
            (
                post["id"], post["subreddit_id"], post["title"],
                post.get("body", ""), post.get("author", ""),
                post.get("score", 0), post.get("upvote_ratio"),
                post.get("num_comments", 0), post["url"],
                post.get("external_url"), post["created_utc"],
                datetime.now(timezone.utc).isoformat(),
                post.get("flair"), post.get("post_type", "self"),
                post.get("source_type", "official_sub"),
                post.get("origin_subreddit"),
            ),
        )
        conn.commit()
        return True
    except sqlite3.IntegrityError:
        return False


def upsert_comment(conn: sqlite3.Connection, comment: dict) -> bool:
    """Insert or update a comment. Returns True if new."""
    try:
        conn.execute(
            """INSERT INTO comments (id, post_id, parent_id, author, body, score,
               created_utc, scraped_at, depth)
               VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
               ON CONFLICT(id) DO UPDATE SET
                   score = excluded.score,
                   body = CASE WHEN excluded.body != '' THEN excluded.body ELSE comments.body END""",
            (
                comment["id"], comment["post_id"],
                comment.get("parent_id"), comment.get("author", ""),
                comment["body"], comment.get("score", 0),
                comment["created_utc"],
                datetime.now(timezone.utc).isoformat(),
                comment.get("depth", 0),
            ),
        )
        conn.commit()
        return True
    except sqlite3.IntegrityError:
        return False


# ---------------------------------------------------------------------------
# Arctic Shift API
# ---------------------------------------------------------------------------

def fetch_posts(subreddit: str, after_ts: int, before_ts: int, title_query: str = None) -> list[dict]:
    """Fetch all posts from Arctic Shift for a subreddit and date range.

    If title_query is provided, filters posts whose title matches the keyword.
    Arctic Shift requires subreddit to be specified for all searches.
    """
    all_posts = []
    page_after = after_ts

    while True:
        from urllib.parse import quote
        params = f"?subreddit={subreddit}&after={page_after}&before={before_ts}&limit={PAGE_SIZE}&sort=asc"
        if title_query:
            params += f"&title={quote(title_query)}"
        url = f"{ARCTIC_SHIFT_BASE}/posts/search{params}"
        data = curl_get_json(url)
        if data is None:
            log.error("[%s] Failed to fetch posts page", subreddit)
            break

        posts = data.get("data", [])
        if not posts:
            # Arctic Shift may return empty on rate-limit; retry with backoff
            retried = False
            for _backoff in (8, 15):
                log.warning("[%s] Empty page at after=%d, retrying in %ds...", subreddit, page_after, _backoff)
                time.sleep(_backoff)
                data = curl_get_json(url)
                if data and data.get("data"):
                    posts = data["data"]
                    retried = True
                    break
            if not retried:
                break

        all_posts.extend(posts)
        log.info("[%s] Fetched %d posts (total: %d)", subreddit, len(posts), len(all_posts))

        # Paginate: use last post's created_utc as next 'after'
        last_ts = posts[-1].get("created_utc", 0)
        if last_ts <= page_after:
            break  # no progress
        page_after = last_ts

        if len(posts) < PAGE_SIZE:
            break  # last page

        time.sleep(REQUEST_DELAY)

    return all_posts


def fetch_top_comments(post_id: str, top_n: int = 5) -> list[dict]:
    """Fetch top N comments by score for a post from Arctic Shift."""
    url = (
        f"{ARCTIC_SHIFT_BASE}/comments/search"
        f"?link_id=t3_{post_id}"
        f"&limit={top_n}&sort=desc"
    )
    data = curl_get_json(url, retries=2)
    if data is None:
        return []

    comments = data.get("data") or []
    # Sort by score descending (API may not sort by score, just by date)
    comments.sort(key=lambda c: c.get("score", 0), reverse=True)
    return comments[:top_n]


# ---------------------------------------------------------------------------
# Main backfill logic
# ---------------------------------------------------------------------------

def backfill(
    subreddits: list[str],
    start_date: str,
    end_date: str,
    comments_per_post: int = 5,
    skip_comments: bool = False,
    min_score_for_comments: int = 0,
) -> dict:
    """
    Backfill posts and comments from Arctic Shift API.

    IMPORTANT: Comments are always fetched by default. Only skip if
    explicitly requested by user with --skip-comments flag.

    min_score_for_comments: Only fetch comments for posts with score >= this value.
    Set to 0 to fetch for all posts (default). Higher values speed up processing
    by skipping low-engagement posts.
    """
    conn = get_db()

    start_dt = datetime.strptime(start_date, "%Y-%m-%d").replace(tzinfo=timezone.utc)
    end_dt = datetime.strptime(end_date, "%Y-%m-%d").replace(hour=23, minute=59, second=59, tzinfo=timezone.utc)
    after_ts = int(start_dt.timestamp())
    before_ts = int(end_dt.timestamp())

    stats = {"total_posts": 0, "new_posts": 0, "total_comments": 0, "new_comments": 0, "subreddits": {}}

    for sub in subreddits:
        info = SUBREDDIT_MAP.get(sub, {"product": sub, "display": f"r/{sub}"})

        # Ensure subreddit row exists
        conn.execute(
            """INSERT INTO subreddits (id, product_name, display_name, url, last_scraped_at)
               VALUES (?, ?, ?, ?, ?)
               ON CONFLICT(id) DO UPDATE SET
                   product_name=excluded.product_name,
                   display_name=excluded.display_name,
                   last_scraped_at=excluded.last_scraped_at""",
            (sub, info["product"], info["display"],
             f"https://www.reddit.com/r/{sub}/",
             datetime.now(timezone.utc).isoformat()),
        )
        conn.commit()

        sub_stats = {"posts_scraped": 0, "posts_new": 0, "comments_scraped": 0, "comments_new": 0}

        log.info("=" * 60)
        log.info("Backfilling %s (%s) [%s to %s]", info["display"], info["product"], start_date, end_date)
        log.info("=" * 60)

        # Fetch all posts
        raw_posts = fetch_posts(sub, after_ts, before_ts)
        sub_stats["posts_scraped"] = len(raw_posts)

        new_posts = []
        for rp in raw_posts:
            post_id = rp.get("id", "")
            if not post_id:
                continue

            created_utc = datetime.utcfromtimestamp(
                rp.get("created_utc", 0)
            ).replace(tzinfo=timezone.utc).isoformat()

            permalink = rp.get("permalink", f"/r/{sub}/comments/{post_id}/")
            post_url = f"https://www.reddit.com{permalink}"

            post = {
                "id": post_id,
                "subreddit_id": sub,
                "title": rp.get("title", ""),
                "body": rp.get("selftext", ""),
                "author": rp.get("author", ""),
                "score": rp.get("score", 0),
                "upvote_ratio": rp.get("upvote_ratio"),
                "num_comments": rp.get("num_comments", 0),
                "url": post_url,
                "external_url": rp.get("url_overridden_by_dest"),
                "created_utc": created_utc,
                "flair": rp.get("link_flair_text"),
                "post_type": "self" if rp.get("is_self") else "link",
            }

            is_new = upsert_post(conn, post)
            if is_new:
                sub_stats["posts_new"] += 1
            new_posts.append(post)

        log.info("[%s] %d posts fetched, %d new/updated", sub, len(raw_posts), sub_stats["posts_new"])

        # Fetch top comments for each post
        if not skip_comments:
            # Filter: only fetch comments for posts above min score threshold
            eligible_posts = [p for p in new_posts if p.get("score", 0) >= min_score_for_comments]

            # Skip posts that already have scored comments in DB
            posts_needing_comments = []
            for post in eligible_posts:
                existing = conn.execute(
                    "SELECT COUNT(*) FROM comments WHERE post_id = ? AND score > 0",
                    (post["id"],),
                ).fetchone()[0]
                if existing < comments_per_post:
                    posts_needing_comments.append(post)

            skipped = len(new_posts) - len(eligible_posts)
            already_done = len(eligible_posts) - len(posts_needing_comments)
            log.info(
                "[%s] Comments: %d eligible (score>=%d), %d skipped (low score), %d already done, %d to fetch",
                sub, len(eligible_posts), min_score_for_comments,
                skipped, already_done, len(posts_needing_comments),
            )

            for i, post in enumerate(posts_needing_comments):
                comments = fetch_top_comments(post["id"], top_n=comments_per_post)
                sub_stats["comments_scraped"] += len(comments)

                for rc in comments:
                    comment_id = rc.get("id", "")
                    if not comment_id:
                        continue

                    created_utc = datetime.utcfromtimestamp(
                        rc.get("created_utc", 0)
                    ).replace(tzinfo=timezone.utc).isoformat()

                    comment = {
                        "id": comment_id,
                        "post_id": post["id"],
                        "parent_id": rc.get("parent_id"),
                        "author": rc.get("author", ""),
                        "body": rc.get("body", ""),
                        "score": rc.get("score", 0),
                        "created_utc": created_utc,
                        "depth": rc.get("depth", 0),
                    }

                    is_new = upsert_comment(conn, comment)
                    if is_new:
                        sub_stats["comments_new"] += 1

                if (i + 1) % 50 == 0:
                    log.info("[%s] Comments progress: %d/%d posts", sub, i + 1, len(posts_needing_comments))

                time.sleep(COMMENT_DELAY)

            log.info("[%s] %d comments fetched, %d new", sub, sub_stats["comments_scraped"], sub_stats["comments_new"])

        stats["total_posts"] += sub_stats["posts_scraped"]
        stats["new_posts"] += sub_stats["posts_new"]
        stats["total_comments"] += sub_stats["comments_scraped"]
        stats["new_comments"] += sub_stats["comments_new"]
        stats["subreddits"][sub] = sub_stats

        # Brief pause between subreddits
        if sub != subreddits[-1]:
            time.sleep(REQUEST_DELAY)

    conn.close()

    # Summary
    log.info("=" * 60)
    log.info("BACKFILL COMPLETE")
    log.info("=" * 60)
    log.info("Total posts: %d (%d new)", stats["total_posts"], stats["new_posts"])
    log.info("Total comments: %d (%d new)", stats["total_comments"], stats["new_comments"])
    for sub, s in stats["subreddits"].items():
        log.info("  %s: %d posts (%d new), %d comments (%d new)",
                 sub, s["posts_scraped"], s["posts_new"],
                 s["comments_scraped"], s["comments_new"])

    # Save metadata
    meta = {
        "timestamp": datetime.now(timezone.utc).isoformat(),
        "source": "arctic_shift",
        "start_date": start_date,
        "end_date": end_date,
        "stats": stats,
    }
    meta_path = DB_DIR / "last_backfill.json"
    meta_path.write_text(json.dumps(meta, indent=2, ensure_ascii=False), encoding="utf-8")

    return stats


def backfill_keyword_search(
    start_date: str,
    end_date: str,
    products: list[str] | None = None,
    comments_per_post: int = 5,
    skip_comments: bool = False,
    min_score_for_comments: int = 1,
) -> dict:
    """
    Search general subreddits for AI product mentions by title keyword.
    Arctic Shift requires subreddit per query, so we iterate CROSS_SEARCH_SUBREDDITS × keywords.
    Skips posts from already-tracked subreddits to avoid duplicates.
    """
    conn = get_db()

    start_dt = datetime.strptime(start_date, "%Y-%m-%d").replace(tzinfo=timezone.utc)
    end_dt = datetime.strptime(end_date, "%Y-%m-%d").replace(hour=23, minute=59, second=59, tzinfo=timezone.utc)
    after_ts = int(start_dt.timestamp())
    before_ts = int(end_dt.timestamp())

    tracked_subs = set(SUBREDDIT_MAP.keys())
    target_products = products or list(PRODUCT_KEYWORDS.keys())
    seen_post_ids = set()

    stats = {"total_posts": 0, "new_posts": 0, "deduped": 0,
             "total_comments": 0, "new_comments": 0, "products": {}}

    for product_name in target_products:
        keywords = PRODUCT_KEYWORDS.get(product_name)
        if not keywords:
            log.warning("No keywords defined for product '%s', skipping", product_name)
            continue

        product_stats = {"posts_found": 0, "posts_new": 0, "comments": 0}

        for search_sub in CROSS_SEARCH_SUBREDDITS:
            if search_sub in tracked_subs:
                continue

            for kw in keywords:
                raw_posts = fetch_posts(search_sub, after_ts, before_ts, title_query=kw)
                if not raw_posts:
                    continue

                log.info("[%s] title='%s' → %d posts (product: %s)",
                         search_sub, kw, len(raw_posts), product_name)

                for rp in raw_posts:
                    post_id = rp.get("id", "")
                    if not post_id or post_id in seen_post_ids:
                        if post_id in seen_post_ids:
                            stats["deduped"] += 1
                        continue
                    seen_post_ids.add(post_id)

                    sub_name = rp.get("subreddit", search_sub)
                    cross_sub_id = f"_keyword_{sub_name}"

                    conn.execute(
                        """INSERT INTO subreddits (id, product_name, display_name, url, last_scraped_at)
                           VALUES (?, ?, ?, ?, ?)
                           ON CONFLICT(id) DO UPDATE SET last_scraped_at=excluded.last_scraped_at""",
                        (cross_sub_id, product_name, f"r/{sub_name} (keyword)",
                         f"https://www.reddit.com/r/{sub_name}/",
                         datetime.now(timezone.utc).isoformat()),
                    )

                    created_utc = datetime.utcfromtimestamp(
                        rp.get("created_utc", 0)
                    ).replace(tzinfo=timezone.utc).isoformat()

                    permalink = rp.get("permalink", f"/r/{sub_name}/comments/{post_id}/")
                    post_url = f"https://www.reddit.com{permalink}"

                    post = {
                        "id": post_id,
                        "subreddit_id": cross_sub_id,
                        "title": rp.get("title", ""),
                        "body": rp.get("selftext", ""),
                        "author": rp.get("author", ""),
                        "score": rp.get("score", 0),
                        "upvote_ratio": rp.get("upvote_ratio"),
                        "num_comments": rp.get("num_comments", 0),
                        "url": post_url,
                        "external_url": rp.get("url_overridden_by_dest"),
                        "created_utc": created_utc,
                        "flair": rp.get("link_flair_text"),
                        "post_type": "self" if rp.get("is_self") else "link",
                        "source_type": "keyword_search",
                        "origin_subreddit": sub_name,
                    }

                    is_new = upsert_post(conn, post)
                    if is_new:
                        product_stats["posts_new"] += 1
                    product_stats["posts_found"] += 1

                    if not skip_comments and post.get("score", 0) >= min_score_for_comments:
                        existing = conn.execute(
                            "SELECT COUNT(*) FROM comments WHERE post_id = ? AND score > 0",
                            (post_id,),
                        ).fetchone()[0]
                        if existing < comments_per_post:
                            comments = fetch_top_comments(post_id, top_n=comments_per_post)
                            product_stats["comments"] += len(comments)
                            for rc in comments:
                                comment_id = rc.get("id", "")
                                if not comment_id:
                                    continue
                                c_created = datetime.utcfromtimestamp(
                                    rc.get("created_utc", 0)
                                ).replace(tzinfo=timezone.utc).isoformat()
                                upsert_comment(conn, {
                                    "id": comment_id,
                                    "post_id": post_id,
                                    "parent_id": rc.get("parent_id"),
                                    "author": rc.get("author", ""),
                                    "body": rc.get("body", ""),
                                    "score": rc.get("score", 0),
                                    "created_utc": c_created,
                                    "depth": rc.get("depth", 0),
                                })
                            time.sleep(COMMENT_DELAY)

                time.sleep(REQUEST_DELAY)

        stats["total_posts"] += product_stats["posts_found"]
        stats["new_posts"] += product_stats["posts_new"]
        stats["total_comments"] += product_stats["comments"]
        stats["products"][product_name] = product_stats
        log.info("Product '%s': %d found, %d new, %d comments",
                 product_name, product_stats["posts_found"], product_stats["posts_new"], product_stats["comments"])

    conn.commit()
    conn.close()

    log.info("=" * 60)
    log.info("KEYWORD SEARCH COMPLETE")
    log.info("=" * 60)
    log.info("Total found: %d, New: %d, Deduped: %d, Comments: %d",
             stats["total_posts"], stats["new_posts"], stats["deduped"], stats["total_comments"])
    for p, s in stats["products"].items():
        log.info("  %s: %d found, %d new", p, s["posts_found"], s["posts_new"])

    meta = {
        "timestamp": datetime.now(timezone.utc).isoformat(),
        "source": "arctic_shift_keyword",
        "start_date": start_date,
        "end_date": end_date,
        "stats": stats,
    }
    meta_path = DB_DIR / "last_keyword_search.json"
    meta_path.write_text(json.dumps(meta, indent=2, ensure_ascii=False), encoding="utf-8")

    return stats


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def main():
    parser = argparse.ArgumentParser(
        description="Backfill Reddit data from Arctic Shift API (historical posts + scored comments)"
    )
    parser.add_argument("--start-date", required=True, metavar="YYYY-MM-DD",
                        help="Start date for backfill period")
    parser.add_argument("--end-date", required=True, metavar="YYYY-MM-DD",
                        help="End date for backfill period")
    parser.add_argument(
        "--subreddits",
        # Default excludes r/GithubCopilot: it belongs to the GitHub Copilot IDE
        # product, which analyze.py excludes from the M365/Copilot report.
        default=",".join(
            k for k, v in SUBREDDIT_MAP.items() if v["product"] != "GitHub Copilot"
        ),
        help="Comma-separated subreddit names (default: all except GitHub Copilot)",
    )
    parser.add_argument("--top-comments", type=int, default=5,
                        help="Number of top comments per post (default: 5)")
    parser.add_argument("--min-score", type=int, default=1,
                        help="Minimum post score to fetch comments for (default: 1, skips score<=0 posts)")
    parser.add_argument("--skip-comments", action="store_true",
                        help="Skip comment fetching (NOT recommended - only use if explicitly told)")
    parser.add_argument("--keyword", action="store_true",
                        help="Use keyword search mode: search across ALL of Reddit for AI product mentions")
    parser.add_argument("--products", default=None,
                        help="Comma-separated product names for keyword search (default: all)")
    args = parser.parse_args()

    if args.keyword:
        products = None
        if args.products:
            products = [p.strip() for p in args.products.split(",") if p.strip()]
        backfill_keyword_search(
            start_date=args.start_date,
            end_date=args.end_date,
            products=products,
            comments_per_post=args.top_comments,
            skip_comments=args.skip_comments,
            min_score_for_comments=args.min_score,
        )
    else:
        subreddits = [s.strip() for s in args.subreddits.split(",") if s.strip()]

        if args.skip_comments:
            log.warning("⚠️  --skip-comments is enabled. Comments will NOT be fetched.")
            log.warning("   Only use this flag if explicitly instructed by the user.")

        backfill(
            subreddits=subreddits,
            start_date=args.start_date,
            end_date=args.end_date,
            comments_per_post=args.top_comments,
            skip_comments=args.skip_comments,
            min_score_for_comments=args.min_score,
        )


if __name__ == "__main__":
    main()
