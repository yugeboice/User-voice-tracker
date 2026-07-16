#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
collect_techcommunity.py — Microsoft Tech Community collector for the
competitive-analysis pipeline.

Collects discussion threads from the Microsoft Tech Community forums (the
official IT-Pro / admin community) within a date range and writes them into a
SQLite DB whose `posts` / `comments` / `subreddits` tables are field-compatible
with the production `data/reddit.db` so the rows could later be merged in.

Access method (VERIFIED — do not substitute):
  Tech Community exposes a public RSS 2.0 feed per board. No anti-scraping; a
  plain urllib request with a browser User-Agent returns HTTP 200.

    feed: https://techcommunity.microsoft.com/t5/s/gxcuf89792/rss/board?board.id=<BOARD>
    UA:   Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 ...

  Each <item> carries: <title>, <link>, <guid>, <pubDate> (RFC822),
  <dc:creator> (author), and <description> / <content:encoded> (body HTML).
  The feed returns the most-recent ~20 threads for the board.

IMPORTANT (verification phase):
  * Default --db is data/_test_tc.db (a scratch DB). NEVER point this at
    data/reddit.db — that is the deployed production database. A hard guard
    below refuses to write it.
  * This script only CREATES/append-writes to the target DB; it never touches
    reddit.db.

Schema note: production `posts` has NO `source_platform` column. We ADD one in
the scratch DB (filled with 'techcommunity') so TC rows are self-identifying; a
future merge into reddit.db would `ALTER TABLE posts ADD COLUMN source_platform`
first. This mirrors collect_hackernews.py exactly.
"""

import argparse
import datetime as dt
import html
import re
import sqlite3
import sys
import urllib.request
import xml.etree.ElementTree as ET
from email.utils import parsedate_to_datetime

# --------------------------------------------------------------------------- #
# Config
#
# One board is enough for this period's needs: Microsoft365Copilot. Each board
# maps to one pseudo-subreddit row so the forum fits the Reddit-shaped tables.
# To add boards later, append entries here (id must stay unique).
# --------------------------------------------------------------------------- #

BOARDS = {
    "Microsoft365Copilot": {
        "sub_id": "tc_m365",
        "product_name": "M365 Copilot",
        "display_name": "Tech Community: M365 Copilot",
    },
}

RSS_URL = "https://techcommunity.microsoft.com/t5/s/gxcuf89792/rss/board?board.id={board}"
BOARD_URL = "https://techcommunity.microsoft.com/t5/microsoft-365-copilot/bd-p/{board}"
USER_AGENT = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
              "(KHTML, like Gecko) Chrome/120.0 Safari/537.36")

# RSS namespaces present on the feed.
NS = {
    "content": "http://purl.org/rss/1.0/modules/content/",
    "dc": "http://purl.org/dc/elements/1.1/",
}

# The numeric message id lives in the link/guid as ".../m-p/<id>...".
MSG_ID_RE = re.compile(r"/m-p/(\d+)")

TAG_RE = re.compile(r"<[^>]+>")
P_OPEN_RE = re.compile(r"<p\b[^>]*>", re.I)
P_CLOSE_RE = re.compile(r"</p\s*>", re.I)
BR_RE = re.compile(r"<br\s*/?>", re.I)
MULTI_NL_RE = re.compile(r"\n{3,}")


# --------------------------------------------------------------------------- #
# Helpers
# --------------------------------------------------------------------------- #

def log(msg):
    print(msg, flush=True)


def now_iso():
    return dt.datetime.now(dt.timezone.utc).isoformat()


def fetch_feed(board, timeout=30):
    """Fetch the RSS feed for one board and return raw bytes."""
    url = RSS_URL.format(board=board)
    req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return resp.read()


def html_to_text(s):
    """Strip HTML to plain text. Tags here are often UPPERCASE (<P>, <STRONG>,
    <A>) and the content arrives entity-escaped, so handle both robustly."""
    if not s:
        return ""
    s = BR_RE.sub("\n", s)
    s = P_OPEN_RE.sub("\n\n", s)
    s = P_CLOSE_RE.sub("", s)
    s = TAG_RE.sub("", s)
    s = html.unescape(s)              # &nbsp; &amp; etc. -> real chars
    s = MULTI_NL_RE.sub("\n\n", s)
    return s.strip()


def rfc822_to_iso(pubdate):
    """Convert RFC822 pubDate ('Mon, 22 Jun 2026 13:10:09 GMT') to ISO 8601 in
    UTC, matching production created_utc format (e.g. 2026-06-22T13:10:09+00:00)."""
    d = parsedate_to_datetime(pubdate)
    if d.tzinfo is None:
        d = d.replace(tzinfo=dt.timezone.utc)
    else:
        d = d.astimezone(dt.timezone.utc)
    return d.isoformat()


def parse_date_arg(date_str, end_of_day=False):
    """Parse a --start/--end YYYY-MM-DD into a tz-aware UTC datetime boundary."""
    d = dt.datetime.strptime(date_str, "%Y-%m-%d").replace(tzinfo=dt.timezone.utc)
    if end_of_day:
        d = d.replace(hour=23, minute=59, second=59)
    return d


def msg_id_from(link, guid):
    """Extract the stable numeric message id from the link or guid."""
    for cand in (guid, link):
        if not cand:
            continue
        m = MSG_ID_RE.search(cand)
        if m:
            return m.group(1)
    return None


def first_text(item, *paths):
    """Return the first non-empty findtext over the given element paths."""
    for p in paths:
        if ":" in p:
            val = item.findtext(p, namespaces=NS)
        else:
            val = item.findtext(p)
        if val and val.strip():
            return val
    return None


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


def upsert_subreddit(conn, board, scraped_at):
    cfg = BOARDS[board]
    conn.execute(
        """INSERT INTO subreddits (id, product_name, display_name, url, last_scraped_at)
           VALUES (?,?,?,?,?)
           ON CONFLICT(id) DO UPDATE SET last_scraped_at=excluded.last_scraped_at""",
        (cfg["sub_id"], cfg["product_name"], cfg["display_name"],
         BOARD_URL.format(board=board), scraped_at),
    )
    conn.commit()


def insert_post(conn, post):
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


# --------------------------------------------------------------------------- #
# Collection
# --------------------------------------------------------------------------- #

def collect_board(board, start_dt, end_dt, scraped_at):
    """Fetch + parse one board's RSS, return list of post dicts in [start,end]."""
    cfg = BOARDS[board]
    raw = fetch_feed(board)
    root = ET.fromstring(raw)
    items = root.findall(".//item")
    log(f"  [{board}] feed items: {len(items)}")

    posts = []
    skipped_range = 0
    skipped_noid = 0
    for it in items:
        title = (first_text(it, "title") or "").strip()
        link = (first_text(it, "link") or "").strip()
        guid = (first_text(it, "guid") or "").strip()
        pubdate = first_text(it, "pubDate")
        author = first_text(it, "dc:creator")
        # Prefer the richer content:encoded; fall back to description.
        body_html = first_text(it, "content:encoded", "description")

        if not pubdate:
            continue
        created_iso = rfc822_to_iso(pubdate)
        created_dt = dt.datetime.fromisoformat(created_iso)
        if not (start_dt <= created_dt <= end_dt):
            skipped_range += 1
            continue

        mid = msg_id_from(link, guid)
        if not mid:
            skipped_noid += 1
            continue

        posts.append({
            "id": f"tc_{mid}",
            "subreddit_id": cfg["sub_id"],
            "title": title or "(no title)",
            "body": html_to_text(body_html),
            "author": author,
            "score": 0,                 # RSS exposes no kudos/score
            "upvote_ratio": None,
            "num_comments": 0,          # RSS exposes no reply count
            "url": link or guid,        # real clickable discussion permalink
            "external_url": None,
            "created_utc": created_iso,
            "scraped_at": scraped_at,
            "flair": None,
            "post_type": "self",
            "source_type": "official_sub",
            "origin_subreddit": None,
            "source_platform": "techcommunity",
        })

    log(f"  [{board}] in-range: {len(posts)}  (skipped out-of-range: "
        f"{skipped_range}, no-id: {skipped_noid})")
    return posts


# --------------------------------------------------------------------------- #
# Main
# --------------------------------------------------------------------------- #

def main():
    ap = argparse.ArgumentParser(
        description="Collect Microsoft Tech Community threads into a Reddit-compatible DB.")
    ap.add_argument("--start-date", required=True, help="YYYY-MM-DD (inclusive, UTC)")
    ap.add_argument("--end-date", required=True, help="YYYY-MM-DD (inclusive, UTC)")
    ap.add_argument("--db", default="data/_test_tc.db", help="target SQLite DB (default: scratch)")
    ap.add_argument("--boards", default=",".join(BOARDS),
                    help="comma-separated board ids (default: all configured)")
    args = ap.parse_args()

    # Hard guard: refuse to write the production DB.
    if args.db.replace("\\", "/").endswith("data/reddit.db"):
        log("REFUSING to write production DB data/reddit.db. Use a scratch DB.")
        sys.exit(2)

    boards = [b.strip() for b in args.boards.split(",") if b.strip()]
    unknown = [b for b in boards if b not in BOARDS]
    if unknown:
        log(f"Unknown board(s): {unknown}. Known: {list(BOARDS)}")
        sys.exit(2)

    start_dt = parse_date_arg(args.start_date)
    end_dt = parse_date_arg(args.end_date, end_of_day=True)
    scraped_at = now_iso()

    log(f"TC collection window: {args.start_date} .. {args.end_date} -> {args.db}")

    conn = sqlite3.connect(args.db)
    try:
        init_db(conn)
        totals = {}
        for board in boards:
            upsert_subreddit(conn, board, scraped_at)
            posts = collect_board(board, start_dt, end_dt, scraped_at)
            for p in posts:
                insert_post(conn, p)
            conn.commit()
            totals[board] = len(posts)
            log(f"  [{board}] posts written: {len(posts)}")

        log("\n=== SUMMARY ===")
        log(f"window        : {args.start_date} .. {args.end_date}")
        log(f"boards        : {boards}")
        log(f"posts written : {sum(totals.values())}")
        for b, n in totals.items():
            log(f"  {b:24}{n:>6}")
        log(f"db            : {args.db}")
    finally:
        conn.close()


if __name__ == "__main__":
    main()
