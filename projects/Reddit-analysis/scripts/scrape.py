"""
Reddit Competitive Intelligence Scraper

Scrapes Reddit subreddits via RSS feeds (no API key required).
Collects posts and comments, stores in SQLite database.

Usage:
    python scrape.py --subreddits ChatGPT,ClaudeAI,Gemini,GithubCopilot,MicrosoftCopilot --days 14
"""

import argparse
import hashlib
import json
import logging
import re
import sqlite3
import sys
import time
import xml.etree.ElementTree as ET
from datetime import datetime, timedelta, timezone
from html import unescape
from pathlib import Path

import requests

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

SUBREDDIT_MAP = {
    # ChatGPT
    "ChatGPT": {"product": "ChatGPT", "display": "r/ChatGPT"},
    "ChatGPTcomplaints": {"product": "ChatGPT", "display": "r/ChatGPTcomplaints"},
    # Claude
    "ClaudeAI": {"product": "Claude", "display": "r/ClaudeAI"},
    "claude": {"product": "Claude", "display": "r/claude"},
    # Gemini
    "GeminiAI": {"product": "Gemini", "display": "r/GeminiAI"},
    "GoogleGeminiAI": {"product": "Gemini", "display": "r/GoogleGeminiAI"},
    # Copilot
    "GithubCopilot": {"product": "GitHub Copilot", "display": "r/GithubCopilot"},
    "MicrosoftCopilot": {"product": "M365 Copilot", "display": "r/MicrosoftCopilot"},
    "microsoft_365_copilot": {"product": "M365 Copilot", "display": "r/microsoft_365_copilot"},
}

RSS_BASE = "https://www.reddit.com"
ATOM_NS = {"atom": "http://www.w3.org/2005/Atom"}
REQUEST_DELAY = 4  # seconds between requests
COMMENT_DELAY = 5  # seconds between comment fetches (avoid 429 rate limits)
SUBREDDIT_COOLDOWN = 60  # seconds between subreddits to reset rate limits
MAX_RSS_PER_PAGE = 25  # Reddit RSS max per request
USER_AGENT = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
)

DB_DIR = Path(__file__).resolve().parent.parent / "data"
DB_PATH = DB_DIR / "reddit.db"

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
    datefmt="%H:%M:%S",
)
log = logging.getLogger("scraper")

# ---------------------------------------------------------------------------
# Database setup
# ---------------------------------------------------------------------------

SCHEMA_SQL = """
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
    post_type       TEXT
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

CREATE INDEX IF NOT EXISTS idx_posts_subreddit_date ON posts(subreddit_id, created_utc);
CREATE INDEX IF NOT EXISTS idx_posts_created ON posts(created_utc);
CREATE INDEX IF NOT EXISTS idx_comments_post ON comments(post_id);
"""


def init_db() -> sqlite3.Connection:
    """Initialize SQLite database and return connection."""
    DB_DIR.mkdir(parents=True, exist_ok=True)
    conn = sqlite3.connect(str(DB_PATH))
    conn.row_factory = sqlite3.Row
    conn.executescript(SCHEMA_SQL)
    conn.commit()
    return conn


def upsert_subreddit(conn: sqlite3.Connection, sub_id: str, info: dict):
    conn.execute(
        """INSERT INTO subreddits (id, product_name, display_name, url, last_scraped_at)
           VALUES (?, ?, ?, ?, ?)
           ON CONFLICT(id) DO UPDATE SET last_scraped_at=excluded.last_scraped_at""",
        (
            sub_id,
            info["product"],
            info["display"],
            f"https://www.reddit.com/r/{sub_id}/",
            datetime.now(timezone.utc).isoformat(),
        ),
    )
    conn.commit()


def insert_post(conn: sqlite3.Connection, post: dict) -> bool:
    """Insert post, return True if new (not duplicate)."""
    try:
        conn.execute(
            """INSERT INTO posts (id, subreddit_id, title, body, author, score,
               upvote_ratio, num_comments, url, external_url, created_utc,
               scraped_at, flair, post_type)
               VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
            (
                post["id"],
                post["subreddit_id"],
                post["title"],
                post.get("body"),
                post.get("author"),
                post.get("score", 0),
                post.get("upvote_ratio"),
                post.get("num_comments", 0),
                post["url"],
                post.get("external_url"),
                post["created_utc"],
                datetime.now(timezone.utc).isoformat(),
                post.get("flair"),
                post.get("post_type"),
            ),
        )
        conn.commit()
        return True
    except sqlite3.IntegrityError:
        return False  # duplicate


def insert_comment(conn: sqlite3.Connection, comment: dict) -> bool:
    """Insert comment, return True if new."""
    try:
        conn.execute(
            """INSERT INTO comments (id, post_id, parent_id, author, body, score,
               created_utc, scraped_at, depth)
               VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)""",
            (
                comment["id"],
                comment["post_id"],
                comment.get("parent_id"),
                comment.get("author"),
                comment["body"],
                comment.get("score", 0),
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
# HTML / text helpers
# ---------------------------------------------------------------------------

TAG_RE = re.compile(r"<[^>]+>")
WHITESPACE_RE = re.compile(r"\s+")


def strip_html(html_text: str) -> str:
    """Strip HTML tags and decode entities to plain text."""
    if not html_text:
        return ""
    text = unescape(html_text)
    text = TAG_RE.sub(" ", text)
    text = WHITESPACE_RE.sub(" ", text).strip()
    return text


def extract_post_id(link: str) -> str:
    """Extract Reddit post ID from permalink URL."""
    # Link format: https://www.reddit.com/r/sub/comments/POST_ID/slug/
    parts = link.rstrip("/").split("/")
    try:
        idx = parts.index("comments")
        return parts[idx + 1]
    except (ValueError, IndexError):
        return hashlib.md5(link.encode()).hexdigest()[:12]


# ---------------------------------------------------------------------------
# RSS Scraping
# ---------------------------------------------------------------------------

def create_session() -> requests.Session:
    session = requests.Session()
    session.headers.update({
        "User-Agent": USER_AGENT,
        "Accept": "application/atom+xml,application/xml,text/xml;q=0.9,*/*;q=0.8",
        "Accept-Language": "en-US,en;q=0.5",
    })
    return session


def fetch_rss(session: requests.Session, url: str, retries: int = 3) -> ET.Element | None:
    """Fetch and parse RSS feed with retry and 429 backoff."""
    for attempt in range(retries):
        try:
            resp = session.get(url, timeout=20)
            if resp.status_code == 200:
                return ET.fromstring(resp.text)
            if resp.status_code == 429:
                wait = REQUEST_DELAY * (attempt + 2) * 2  # exponential backoff for rate limits
                log.warning("RSS %s rate limited (429), waiting %ds...", url, wait)
                time.sleep(wait)
                continue
            log.warning("RSS %s returned status %d", url, resp.status_code)
        except Exception as e:
            log.warning("RSS fetch attempt %d failed for %s: %s", attempt + 1, url, e)
        if attempt < retries - 1:
            time.sleep(REQUEST_DELAY * (attempt + 1))
    return None


def parse_post_entry(entry: ET.Element, subreddit_id: str) -> dict | None:
    """Parse a single RSS <entry> element into a post dict."""
    title_el = entry.find("atom:title", ATOM_NS)
    link_el = entry.find("atom:link", ATOM_NS)
    updated_el = entry.find("atom:updated", ATOM_NS)
    author_el = entry.find("atom:author/atom:name", ATOM_NS)
    content_el = entry.find("atom:content", ATOM_NS)

    if title_el is None or link_el is None:
        return None

    link = link_el.attrib.get("href", "")
    post_id = extract_post_id(link)
    title = title_el.text or ""

    # Parse body from HTML content
    body = ""
    if content_el is not None and content_el.text:
        body = strip_html(content_el.text)

    # Parse timestamp
    created_utc = ""
    if updated_el is not None and updated_el.text:
        created_utc = updated_el.text

    author = ""
    if author_el is not None and author_el.text:
        author = author_el.text.replace("/u/", "")

    return {
        "id": post_id,
        "subreddit_id": subreddit_id,
        "title": title,
        "body": body,
        "author": author,
        "score": 0,  # RSS doesn't provide score
        "upvote_ratio": None,
        "num_comments": 0,  # Will be updated from comment scrape
        "url": link,
        "external_url": None,
        "created_utc": created_utc,
        "flair": None,
        "post_type": "self" if body else "link",
    }


def scrape_subreddit_posts(
    session: requests.Session,
    subreddit: str,
    limit: int = 200,
    cutoff: datetime | None = None,
) -> list[dict]:
    """Scrape posts from a subreddit via RSS with pagination."""
    posts = []
    seen_ids = set()
    after_param = ""
    page = 0

    while len(posts) < limit:
        batch_size = min(MAX_RSS_PER_PAGE, limit - len(posts))
        feed_type = "new"
        url = f"{RSS_BASE}/r/{subreddit}/{feed_type}.rss?limit={batch_size}"
        if after_param:
            url += f"&after={after_param}"

        page += 1
        log.info("[%s] Fetching page %d (have %d/%d posts)...", subreddit, page, len(posts), limit)

        root = fetch_rss(session, url)
        # Some smaller/restricted subreddits intermittently fail on new.rss.
        # On first-page failure, fall back to hot.rss to avoid full subreddit dropout.
        if root is None and page == 1 and not after_param:
            feed_type = "hot"
            fallback_url = f"{RSS_BASE}/r/{subreddit}/{feed_type}.rss?limit={batch_size}"
            log.warning("[%s] new.rss failed on first page, trying hot.rss fallback...", subreddit)
            root = fetch_rss(session, fallback_url)
        if root is None:
            log.error("[%s] Failed to fetch %s RSS page %d, stopping", subreddit, feed_type, page)
            break

        entries = root.findall("atom:entry", ATOM_NS)
        if not entries:
            log.info("[%s] No more entries on page %d", subreddit, page)
            break

        new_in_page = 0
        last_post_id = None
        for entry in entries:
            post = parse_post_entry(entry, subreddit)
            if post is None:
                continue
            if post["id"] in seen_ids:
                continue

            # Check time cutoff
            if cutoff and post["created_utc"]:
                try:
                    post_time = datetime.fromisoformat(post["created_utc"])
                    if post_time.tzinfo is None:
                        post_time = post_time.replace(tzinfo=timezone.utc)
                    if post_time < cutoff:
                        log.info("[%s] Post %s is before cutoff, stopping", subreddit, post["id"])
                        return posts
                except (ValueError, TypeError):
                    pass

            seen_ids.add(post["id"])
            posts.append(post)
            last_post_id = post["id"]
            new_in_page += 1

        if new_in_page == 0:
            log.info("[%s] No new posts on page %d, stopping", subreddit, page)
            break

        # Pagination: use Reddit 'after' parameter with fullname
        if last_post_id:
            after_param = f"t3_{last_post_id}"

        time.sleep(REQUEST_DELAY)

    log.info("[%s] Scraped %d posts total", subreddit, len(posts))
    return posts


def scrape_post_comments(
    session: requests.Session,
    post: dict,
    max_comments: int = 50,
) -> list[dict]:
    """Scrape comments for a single post via its RSS feed."""
    # Construct comment RSS URL from post URL
    post_url = post["url"].rstrip("/")
    rss_url = f"{post_url}.rss"

    root = fetch_rss(session, rss_url, retries=2)
    if root is None:
        return []

    entries = root.findall("atom:entry", ATOM_NS)
    comments = []

    for entry in entries:
        title_el = entry.find("atom:title", ATOM_NS)
        content_el = entry.find("atom:content", ATOM_NS)
        author_el = entry.find("atom:author/atom:name", ATOM_NS)
        updated_el = entry.find("atom:updated", ATOM_NS)
        link_el = entry.find("atom:link", ATOM_NS)

        # First entry is usually the post itself, skip it
        title_text = title_el.text if title_el is not None else ""
        if title_text and not title_text.startswith("/u/"):
            continue

        body = ""
        if content_el is not None and content_el.text:
            body = strip_html(content_el.text)

        if not body or body.strip() == "":
            continue

        author = ""
        if author_el is not None and author_el.text:
            author = author_el.text.replace("/u/", "")

        created_utc = ""
        if updated_el is not None and updated_el.text:
            created_utc = updated_el.text

        comment_link = ""
        if link_el is not None:
            comment_link = link_el.attrib.get("href", "")

        # Generate a unique comment ID from the link or content
        comment_id = ""
        if comment_link:
            parts = comment_link.rstrip("/").split("/")
            comment_id = parts[-1] if parts else ""
        if not comment_id:
            comment_id = hashlib.md5(f"{post['id']}_{author}_{body[:50]}".encode()).hexdigest()[:12]

        comments.append({
            "id": comment_id,
            "post_id": post["id"],
            "parent_id": None,  # RSS doesn't provide threading info
            "author": author,
            "body": body,
            "score": 0,  # RSS doesn't provide score
            "created_utc": created_utc,
            "depth": 0,
        })

        if len(comments) >= max_comments:
            break

    return comments


# ---------------------------------------------------------------------------
# Lumina enrichment (optional deep-scrape via Lumina Search API)
# ---------------------------------------------------------------------------

LUMINA_SEARCH_DELAY = 3  # seconds between Lumina API calls


def enrich_top_posts_with_lumina(
    conn: sqlite3.Connection,
    subreddit: str,
    top_n: int = 10,
) -> int:
    """
    For the top N most-commented posts in a subreddit, use Lumina Search API
    to fetch full page content and store it as enriched comments in SQLite.
    Automatically starts/stops the Lumina C# sidecar (lumina/).
    Returns the number of posts successfully enriched.
    """
    from lumina_client import LuminaClient

    rows = conn.execute(
        """SELECT id, url, title, num_comments
           FROM posts WHERE subreddit_id = ?
           ORDER BY num_comments DESC, created_utc DESC
           LIMIT ?""",
        (subreddit, top_n),
    ).fetchall()

    if not rows:
        log.info("[Lumina] No posts found for %s", subreddit)
        return 0

    log.info("[Lumina] Enriching top %d posts in r/%s...", len(rows), subreddit)
    enriched = 0

    with LuminaClient() as lumina:
        for row in rows:
            post_id, post_url, title = row["id"], row["url"], row["title"]
            log.info("[Lumina] Fetching: %s", title[:60])

            content = lumina.search_post(post_url, post_title=title)
            if not content:
                log.warning("[Lumina] No content returned for post %s", post_id)
                time.sleep(LUMINA_SEARCH_DELAY)
                continue

            comment_id = hashlib.md5(f"lumina_{post_id}".encode()).hexdigest()[:12]
            comment = {
                "id": comment_id,
                "post_id": post_id,
                "parent_id": None,
                "author": "[lumina_enriched]",
                "body": f"[LUMINA FULL CONTENT]\n{content}",
                "score": 0,
                "created_utc": datetime.now(timezone.utc).isoformat(),
                "depth": 0,
            }

            is_new = insert_comment(conn, comment)
            if is_new:
                log.info("[Lumina] Stored enriched content for post %s (%d chars)", post_id, len(content))
                enriched += 1
            else:
                log.info("[Lumina] Post %s already enriched, skipping", post_id)

            time.sleep(LUMINA_SEARCH_DELAY)

    log.info("[Lumina] Enriched %d/%d posts in r/%s", enriched, len(rows), subreddit)
    return enriched


# ---------------------------------------------------------------------------
# Main orchestration
# ---------------------------------------------------------------------------

def scrape_all(
    subreddits: list[str],
    days: int = 14,
    post_limit: int = 200,
    skip_comments: bool = False,
    lumina_enrich: int = 0,
) -> dict:
    """Run full scrape for all specified subreddits."""

    conn = init_db()
    session = create_session()
    cutoff = datetime.now(timezone.utc) - timedelta(days=days)

    stats = {
        "total_posts": 0,
        "total_comments": 0,
        "new_posts": 0,
        "new_comments": 0,
        "subreddits": {},
    }

    for sub in subreddits:
        info = SUBREDDIT_MAP.get(sub, {"product": sub, "display": f"r/{sub}"})
        upsert_subreddit(conn, sub, info)

        sub_stats = {"posts_scraped": 0, "posts_new": 0, "comments_scraped": 0, "comments_new": 0}

        log.info("=" * 60)
        log.info("Scraping %s (%s)...", info["display"], info["product"])
        log.info("=" * 60)

        # Scrape posts
        posts = scrape_subreddit_posts(session, sub, limit=post_limit, cutoff=cutoff)
        sub_stats["posts_scraped"] = len(posts)

        new_posts = []
        for post in posts:
            is_new = insert_post(conn, post)
            if is_new:
                sub_stats["posts_new"] += 1
                new_posts.append(post)

        log.info("[%s] %d posts scraped, %d new", sub, len(posts), sub_stats["posts_new"])

        # Scrape comments for new posts
        if not skip_comments and new_posts:
            log.info("[%s] Fetching comments for %d new posts...", sub, len(new_posts))
            for i, post in enumerate(new_posts):
                comments = scrape_post_comments(session, post)
                sub_stats["comments_scraped"] += len(comments)
                for comment in comments:
                    is_new = insert_comment(conn, comment)
                    if is_new:
                        sub_stats["comments_new"] += 1

                # Update post num_comments
                if comments:
                    conn.execute(
                        "UPDATE posts SET num_comments = ? WHERE id = ?",
                        (len(comments), post["id"]),
                    )
                    conn.commit()

                if (i + 1) % 10 == 0:
                    log.info("[%s] Comments progress: %d/%d posts", sub, i + 1, len(new_posts))
                time.sleep(COMMENT_DELAY)

            log.info(
                "[%s] %d comments scraped, %d new",
                sub, sub_stats["comments_scraped"], sub_stats["comments_new"],
            )

        # Lumina deep-enrichment for top N posts
        if lumina_enrich > 0:
            enrich_top_posts_with_lumina(conn, sub, top_n=lumina_enrich)

        stats["total_posts"] += sub_stats["posts_scraped"]
        stats["total_comments"] += sub_stats["comments_scraped"]
        stats["new_posts"] += sub_stats["posts_new"]
        stats["new_comments"] += sub_stats["comments_new"]
        stats["subreddits"][sub] = sub_stats

        # Delay between subreddits (longer cooldown to avoid 429)
        if sub != subreddits[-1]:
            log.info("Cooling down %ds before next subreddit...", SUBREDDIT_COOLDOWN)
            time.sleep(SUBREDDIT_COOLDOWN)

    conn.close()

    # Print summary
    log.info("=" * 60)
    log.info("SCRAPE COMPLETE")
    log.info("=" * 60)
    log.info("Total posts: %d (%d new)", stats["total_posts"], stats["new_posts"])
    log.info("Total comments: %d (%d new)", stats["total_comments"], stats["new_comments"])
    log.info("Database: %s", DB_PATH)
    for sub, s in stats["subreddits"].items():
        log.info("  %s: %d posts (%d new), %d comments (%d new)",
                 sub, s["posts_scraped"], s["posts_new"], s["comments_scraped"], s["comments_new"])

    # Save run metadata
    meta_path = DB_DIR / "last_run.json"
    meta = {
        "timestamp": datetime.now(timezone.utc).isoformat(),
        "subreddits": subreddits,
        "days": days,
        "post_limit": post_limit,
        "stats": stats,
    }
    meta_path.write_text(json.dumps(meta, indent=2, ensure_ascii=False), encoding="utf-8")

    return stats


# ---------------------------------------------------------------------------
# CLI entry point
# ---------------------------------------------------------------------------

def main():
    parser = argparse.ArgumentParser(description="Reddit Competitive Intelligence Scraper")
    parser.add_argument(
        "--subreddits",
        default="ChatGPT,ChatGPTcomplaints,ClaudeAI,claude,GeminiAI,GoogleGeminiAI,GithubCopilot,MicrosoftCopilot,microsoft_365_copilot",
        help="Comma-separated subreddit names (default: all 5 competitors)",
    )
    parser.add_argument("--days", type=int, default=14, help="Time window in days (default: 14)")
    parser.add_argument("--limit", type=int, default=200, help="Max posts per subreddit (default: 200)")
    parser.add_argument("--skip-comments", action="store_true", help="Skip comment scraping")
    parser.add_argument("--lumina-enrich", type=int, default=0, metavar="N",
                        help="Deep-enrich top N posts via Lumina Search API (requires localhost:8400)")
    args = parser.parse_args()

    subreddits = [s.strip() for s in args.subreddits.split(",") if s.strip()]
    log.info("Starting scrape: subreddits=%s, days=%d, limit=%d", subreddits, args.days, args.limit)

    scrape_all(
        subreddits=subreddits,
        days=args.days,
        post_limit=args.limit,
        skip_comments=args.skip_comments,
        lumina_enrich=args.lumina_enrich,
    )


if __name__ == "__main__":
    main()
