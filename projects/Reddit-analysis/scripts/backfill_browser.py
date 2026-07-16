"""
Backfill missing Reddit data using phantomwright-cli browser session.
Requires an already-logged-in phantomwright session.

Strategy: Use www.reddit.com/r/{sub}/search.json with sort=new, paginate
with 'after' token until we pass the target date range. For large subs,
uses multiple search queries for broader coverage.
"""
import argparse
import json
import re
import sqlite3
import subprocess
import sys
import time
import logging
from datetime import datetime, timezone
from pathlib import Path

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
    datefmt="%H:%M:%S",
)
log = logging.getLogger(__name__)

DB_PATH = Path(__file__).parent.parent / "data" / "reddit.db"
PW_CLI = r"C:\Users\dorisrao\AppData\Roaming\npm\phantomwright-cli.cmd"

# Rate limits
LISTING_SLEEP = 3   # seconds between listing pages
COMMENT_SLEEP = 2   # seconds between comment fetches

# For large subs, use multiple queries to get broader coverage
# Each query searches with sort=new and paginates until past target dates
SEARCH_QUERIES = [
    "",           # empty query = browse all (may not work, but try first)
    "self:yes",   # all self posts
    "self:no",    # all link posts
    "flair:discussion OR flair:question OR flair:help",
    "flair:news OR flair:article OR flair:resource",
]

# Fallback broad keyword queries if the above don't cover enough
KEYWORD_QUERIES = [
    "the OR a OR is OR to OR and",   # common words to match most posts
    "how OR what OR why OR when",
    "new OR update OR issue OR problem",
    "help OR question OR advice OR recommend",
]


def pw_open(url, timeout_ms=30000):
    """Use phantomwright-cli to open a URL and return stdout."""
    try:
        result = subprocess.run(
            [PW_CLI, "open", url],
            capture_output=True,
            text=True,
            timeout=timeout_ms // 1000 + 15,
            encoding="utf-8",
        )
        return result.stdout
    except subprocess.TimeoutExpired:
        log.warning("phantomwright-cli timed out for %s", url)
        return ""
    except Exception as e:
        log.warning("phantomwright-cli error: %s", e)
        return ""


def pw_fetch_json(url, timeout_ms=30000):
    """Use phantomwright-cli eval + fetch() to get JSON without caching issues."""
    js = (
        "(async()=>{"
        f"const r=await fetch('{url}');"
        "if(!r.ok) return JSON.stringify({error:r.status});"
        "const d=await r.json();"
        "return JSON.stringify(d);"
        "})()"
    )
    try:
        result = subprocess.run(
            [PW_CLI, "eval", js],
            capture_output=True,
            text=True,
            timeout=timeout_ms // 1000 + 15,
            encoding="utf-8",
        )
        output = result.stdout
        # Output format: result: "..."  or result: {...}
        m = re.search(r'^result:\s*"(.*)"$', output.strip(), re.DOTALL)
        if m:
            raw = m.group(1)
            # Unescape
            unescaped = raw.replace('\\"', '"').replace('\\\\', '\\')
            return json.loads(unescaped)
        # Try direct JSON
        m = re.search(r'^result:\s*(\{.*\})$', output.strip(), re.DOTALL)
        if m:
            return json.loads(m.group(1))
        log.warning("Could not parse eval output: %s", output[:200])
        return None
    except subprocess.TimeoutExpired:
        log.warning("pw_fetch_json timed out for %s", url)
        return None
    except json.JSONDecodeError as e:
        log.warning("JSON decode error: %s", e)
        return None
    except Exception as e:
        log.warning("pw_fetch_json error: %s", e)
        return None


def parse_json_from_pw_output(output):
    """
    Parse JSON from phantomwright output.

    The output looks like:
      - generic [ref=e2]: "{\"kind\": \"Listing\", ...}"

    The JSON is an escaped string inside quotes. We need to:
    1. Find the escaped JSON string
    2. Unescape it
    3. Parse it
    """
    if not output:
        return None

    # Strategy 1: Look for escaped JSON in generic element
    # Pattern: generic [ref=...]: "{\"kind\"...}"
    # The value is a JSON-encoded string (with escaped quotes)
    match = re.search(r'generic\s+\[ref=[^\]]*\]:\s*"(.*)"', output, re.DOTALL)
    if match:
        raw = match.group(1)
        try:
            # The string has \" for quotes and \\ for backslashes
            unescaped = raw.replace('\\"', '"').replace('\\\\', '\\')
            return json.loads(unescaped)
        except json.JSONDecodeError:
            pass
        # Try unicode_escape
        try:
            unescaped = raw.encode("utf-8").decode("unicode_escape")
            return json.loads(unescaped)
        except (json.JSONDecodeError, UnicodeDecodeError):
            pass

    # Strategy 2: Look for raw JSON starting with {"kind"
    idx = output.find('{"kind"')
    if idx != -1:
        depth = 0
        for i in range(idx, len(output)):
            if output[i] == "{":
                depth += 1
            elif output[i] == "}":
                depth -= 1
            if depth == 0:
                try:
                    return json.loads(output[idx : i + 1])
                except json.JSONDecodeError:
                    break

    # Strategy 3: Look for escaped JSON with {"kind"
    idx = output.find('"{\\"kind\\"')
    if idx != -1:
        end = output.rfind('"', idx + 1)
        if end > idx:
            raw = output[idx + 1 : end]
            try:
                unescaped = raw.replace('\\"', '"').replace('\\\\', '\\')
                return json.loads(unescaped)
            except json.JSONDecodeError:
                pass

    # Strategy 4: Line-by-line search
    for line in output.split("\n"):
        line = line.strip()
        if "generic" in line and ("kind" in line or "Listing" in line):
            # Try to extract JSON from the line
            colon_idx = line.find(": ")
            if colon_idx != -1:
                val = line[colon_idx + 2 :].strip()
                if val.startswith('"') and val.endswith('"'):
                    val = val[1:-1]
                    try:
                        unescaped = val.replace('\\"', '"').replace('\\\\', '\\')
                        return json.loads(unescaped)
                    except json.JSONDecodeError:
                        pass

    return None


def parse_comments_json_from_pw_output(output):
    """Parse comment JSON (which is an array of two Listings)."""
    if not output:
        return None

    # Look for array format [{"kind":...
    match = re.search(r'generic\s+\[ref=[^\]]*\]:\s*"(.*)"', output, re.DOTALL)
    if match:
        raw = match.group(1)
        try:
            unescaped = raw.replace('\\"', '"').replace('\\\\', '\\')
            return json.loads(unescaped)
        except json.JSONDecodeError:
            pass

    # Try finding array directly
    idx = output.find('[{"kind"')
    if idx != -1:
        depth = 0
        for i in range(idx, len(output)):
            if output[i] == "[":
                depth += 1
            elif output[i] == "]":
                depth -= 1
            if depth == 0:
                try:
                    return json.loads(output[idx : i + 1])
                except json.JSONDecodeError:
                    break

    return None


def fetch_json(url, retries=3):
    """Fetch JSON from URL via phantomwright fetch() API."""
    for attempt in range(retries):
        data = pw_fetch_json(url)
        if data:
            if "error" in data:
                log.warning("HTTP %s (attempt %d/%d)", data["error"], attempt + 1, retries)
                time.sleep(10)
                continue
            return data
        log.warning("Failed to fetch JSON (attempt %d/%d)", attempt + 1, retries)
        time.sleep(5)
    return None


def fetch_comments_json(url, retries=2):
    """Fetch comment-thread JSON from URL via phantomwright fetch() API."""
    for attempt in range(retries):
        data = pw_fetch_json(url)
        if data:
            return data
        log.warning("Failed to fetch comments (attempt %d/%d)", attempt + 1, retries)
        time.sleep(3)
    return None


def ensure_tables(conn):
    """Ensure DB tables exist."""
    conn.execute("""
        CREATE TABLE IF NOT EXISTS posts (
            id TEXT PRIMARY KEY,
            subreddit_id TEXT,
            title TEXT,
            body TEXT,
            author TEXT,
            score INTEGER,
            upvote_ratio REAL,
            num_comments INTEGER,
            url TEXT,
            external_url TEXT,
            created_utc TEXT,
            scraped_at TEXT,
            flair TEXT,
            post_type TEXT,
            source_type TEXT
        )
    """)
    conn.execute("""
        CREATE TABLE IF NOT EXISTS comments (
            id TEXT PRIMARY KEY,
            post_id TEXT,
            subreddit_id TEXT,
            body TEXT,
            author TEXT,
            score INTEGER,
            created_utc TEXT,
            scraped_at TEXT,
            depth INTEGER
        )
    """)
    conn.commit()


def upsert_post(conn, data, subreddit):
    """Insert or update a post in the DB. Returns post id."""
    post_id = data["id"]
    created = datetime.fromtimestamp(data["created_utc"], tz=timezone.utc).isoformat()
    now = datetime.now(timezone.utc).isoformat()
    body = data.get("selftext") or ""
    url = f"https://www.reddit.com{data['permalink']}"
    ext_url = data.get("url_overridden_by_dest")
    flair = data.get("link_flair_text")
    ptype = "self" if data.get("is_self") else "link"

    conn.execute(
        """INSERT INTO posts (id, subreddit_id, title, body, author, score,
            upvote_ratio, num_comments, url, external_url, created_utc, scraped_at,
            flair, post_type, source_type)
            VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)
            ON CONFLICT(id) DO UPDATE SET
                score=excluded.score,
                num_comments=excluded.num_comments,
                upvote_ratio=excluded.upvote_ratio,
                body=excluded.body,
                scraped_at=excluded.scraped_at""",
        (
            post_id, subreddit, data.get("title", ""), body,
            str(data.get("author", "")), data.get("score", 0),
            data.get("upvote_ratio"), data.get("num_comments", 0),
            url, ext_url, created, now, flair, ptype, "browser_backfill",
        ),
    )
    return post_id


def save_comments(conn, children, post_id, subreddit, depth=0):
    """Recursively save comments. Returns count saved."""
    count = 0
    now = datetime.now(timezone.utc).isoformat()
    for child in children:
        if child.get("kind") != "t1":
            continue
        data = child["data"]
        body = data.get("body", "")
        if not body or body in ("[deleted]", "[removed]"):
            continue
        cid = data["id"]
        created = datetime.fromtimestamp(data["created_utc"], tz=timezone.utc).isoformat()
        conn.execute(
            """INSERT INTO comments (id, post_id, parent_id, author, body,
                score, created_utc, scraped_at, depth)
                VALUES (?,?,?,?,?,?,?,?,?)
                ON CONFLICT(id) DO UPDATE SET score=excluded.score, body=excluded.body""",
            (
                cid, post_id, data.get("parent_id", ""), str(data.get("author", "")),
                body, data.get("score", 0), created, now, depth,
            ),
        )
        count += 1
        replies = data.get("replies")
        if isinstance(replies, dict):
            count += save_comments(
                conn,
                replies.get("data", {}).get("children", []),
                post_id, subreddit, depth + 1,
            )
    return count


def fetch_and_save_comments(conn, post_id, subreddit):
    """Fetch top comments for a post via www.reddit.com."""
    url = (
        f"https://www.reddit.com/r/{subreddit}/comments/{post_id}.json"
        f"?limit=200&sort=top&raw_json=1"
    )
    data = fetch_comments_json(url)
    if not data or not isinstance(data, list) or len(data) < 2:
        return 0
    children = data[1].get("data", {}).get("children", [])
    return save_comments(conn, children, post_id, subreddit)


def search_subreddit_page(subreddit, query, after_token=None):
    """
    Fetch one page of search results from www.reddit.com.
    Uses sort=new, t=month, limit=100.
    Returns (listing_data_dict, children_list, after_token).
    """
    url = (
        f"https://www.reddit.com/r/{subreddit}/search.json"
        f"?q={query}&restrict_sr=on&sort=new&t=month&limit=100&type=link&raw_json=1"
    )
    if after_token:
        url += f"&after={after_token}"

    data = fetch_json(url)
    if not data:
        return None, [], None

    listing = data.get("data", {})
    children = listing.get("children", [])
    next_after = listing.get("after")
    return listing, children, next_after


def backfill_with_query(subreddit, query, target_start_ts, target_end_ts, conn, seen_ids):
    """
    Paginate through search results for one query until we pass the target dates.
    Returns (new_posts_count, new_comments_count).
    """
    after_token = None
    total_posts = 0
    total_comments = 0
    page_num = 0
    consecutive_empty = 0

    while True:
        page_num += 1
        listing, children, next_after = search_subreddit_page(
            subreddit, query, after_token
        )

        if listing is None:
            log.error("  Failed to fetch page %d for query '%s'", page_num, query)
            break

        if not children:
            log.info("  No more results on page %d", page_num)
            break

        posts_in_range = 0
        oldest_ts = None
        passed_target = False

        for child in children:
            if child.get("kind") != "t3":
                continue
            d = child["data"]
            ts = d["created_utc"]
            oldest_ts = ts

            # Skip posts newer than our end date
            if ts > target_end_ts:
                continue

            # If we've gone past our start date, we're done
            if ts < target_start_ts:
                passed_target = True
                break

            pid = d["id"]
            if pid in seen_ids:
                continue
            seen_ids.add(pid)

            upsert_post(conn, d, subreddit)
            posts_in_range += 1
            total_posts += 1

            # Fetch comments for posts with score >= 1
            if d.get("num_comments", 0) > 0 and d.get("score", 0) >= 1:
                time.sleep(COMMENT_SLEEP)
                nc = fetch_and_save_comments(conn, pid, subreddit)
                total_comments += nc

        conn.commit()

        oldest_str = ""
        if oldest_ts:
            oldest_str = datetime.fromtimestamp(oldest_ts, tz=timezone.utc).strftime(
                "%Y-%m-%d %H:%M"
            )

        log.info(
            "  Page %d: %d new posts in range (oldest: %s, cumul: %d posts, %d comments)",
            page_num, posts_in_range, oldest_str, total_posts, total_comments,
        )

        if passed_target:
            log.info("  Reached posts before target start date")
            break

        if not next_after:
            log.info("  No more pages (no after token)")
            break

        if posts_in_range == 0:
            consecutive_empty += 1
            if consecutive_empty >= 3:
                log.info("  3 consecutive pages with no new posts in range, stopping query")
                break
        else:
            consecutive_empty = 0

        after_token = next_after
        time.sleep(LISTING_SLEEP)

    return total_posts, total_comments


def backfill_subreddit(subreddit, target_start_ts, target_end_ts, conn):
    """Backfill a subreddit using /new pagination (primary) + search queries (fallback)."""
    start_str = datetime.fromtimestamp(target_start_ts, tz=timezone.utc).strftime("%Y-%m-%d")
    end_str = datetime.fromtimestamp(target_end_ts, tz=timezone.utc).strftime("%Y-%m-%d")
    log.info("=" * 60)
    log.info("Backfilling r/%s for %s to %s", subreddit, start_str, end_str)
    log.info("=" * 60)

    seen_ids = set()

    # Check what we already have
    existing = conn.execute(
        """SELECT id FROM posts WHERE subreddit_id = ?
           AND created_utc >= ? AND created_utc <= ?""",
        (
            subreddit,
            datetime.fromtimestamp(target_start_ts, tz=timezone.utc).isoformat(),
            datetime.fromtimestamp(target_end_ts, tz=timezone.utc).isoformat(),
        ),
    ).fetchall()
    for row in existing:
        seen_ids.add(row[0])
    log.info("Already have %d posts in DB for this range", len(seen_ids))

    grand_posts = 0
    grand_comments = 0

    # PRIMARY: paginate through /new using fetch() API
    log.info("--- Strategy: /new pagination ---")
    after_token = None
    page_num = 0
    passed_target = False

    while not passed_target:
        page_num += 1
        url = f"https://www.reddit.com/r/{subreddit}/new/.json?limit=100&raw_json=1"
        if after_token:
            url += f"&after={after_token}"

        data = fetch_json(url)
        if not data:
            log.error("Failed on page %d", page_num)
            break

        children = data.get("data", {}).get("children", [])
        if not children:
            break

        after_token = data.get("data", {}).get("after")
        posts_in_range = 0

        for child in children:
            if child.get("kind") != "t3":
                continue
            d = child["data"]
            ts = d["created_utc"]
            if ts > target_end_ts:
                continue
            if ts < target_start_ts:
                passed_target = True
                break

            pid = d["id"]
            if pid in seen_ids:
                continue
            seen_ids.add(pid)

            upsert_post(conn, d, subreddit)
            posts_in_range += 1
            grand_posts += 1

            if d.get("num_comments", 0) > 0 and d.get("score", 0) >= 1:
                time.sleep(COMMENT_SLEEP)
                nc = fetch_and_save_comments(conn, pid, subreddit)
                grand_comments += nc

        conn.commit()
        oldest_ts = min((c["data"]["created_utc"] for c in children if c.get("kind") == "t3"), default=0)
        oldest_str = datetime.fromtimestamp(oldest_ts, tz=timezone.utc).strftime("%Y-%m-%d %H:%M") if oldest_ts else "?"
        log.info("Page %d: %d new in range (cumul: %d posts, %d comments) oldest=%s",
                 page_num, posts_in_range, grand_posts, grand_comments, oldest_str)

        if not after_token:
            break
        time.sleep(LISTING_SLEEP)

    log.info(
        "r/%s COMPLETE: %d new posts, %d new comments (total in DB incl prior: %d)",
        subreddit, grand_posts, grand_comments, len(seen_ids),
    )
    return grand_posts, grand_comments


def main():
    parser = argparse.ArgumentParser(
        description="Backfill Reddit data using phantomwright browser session"
    )
    parser.add_argument(
        "--subreddits", required=True,
        help="Comma-separated list of subreddit names (without r/)",
    )
    parser.add_argument(
        "--start-date", required=True,
        help="Start date YYYY-MM-DD (inclusive, UTC)",
    )
    parser.add_argument(
        "--end-date", required=True,
        help="End date YYYY-MM-DD (inclusive, UTC)",
    )
    args = parser.parse_args()

    subreddits = [s.strip() for s in args.subreddits.split(",")]
    start_dt = datetime.strptime(args.start_date, "%Y-%m-%d").replace(tzinfo=timezone.utc)
    end_dt = datetime.strptime(args.end_date, "%Y-%m-%d").replace(
        hour=23, minute=59, second=59, tzinfo=timezone.utc
    )

    log.info("Backfill target: %s to %s", args.start_date, args.end_date)
    log.info("Subreddits: %s", ", ".join(subreddits))
    log.info("DB: %s", DB_PATH)

    conn = sqlite3.connect(str(DB_PATH))
    ensure_tables(conn)

    grand_posts = 0
    grand_comments = 0
    for sub in subreddits:
        p, c = backfill_subreddit(sub, start_dt.timestamp(), end_dt.timestamp(), conn)
        grand_posts += p
        grand_comments += c
        if len(subreddits) > 1:
            time.sleep(5)

    conn.close()
    log.info(
        "BACKFILL COMPLETE: %d new posts, %d new comments across %d subreddits",
        grand_posts, grand_comments, len(subreddits),
    )


if __name__ == "__main__":
    main()
