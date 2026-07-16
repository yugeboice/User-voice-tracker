"""
Reddit Competitive Intelligence - Embedding Retrieval

Generates embeddings for Reddit posts and provides semantic search.
Uses the local LLM endpoint (OpenAI-compatible /v1/embeddings).

Usage:
    python embed.py --embed --days 14                  # Embed recent posts
    python embed.py --embed --start-date 2026-03-01 --end-date 2026-03-15
    python embed.py --query "Claude code generation quality" --top-k 10
    python embed.py --query "file upload issues" --product ChatGPT
    python embed.py --rebuild                          # Re-embed all posts
    python embed.py --stats                            # Show embedding stats
"""

import argparse
import json
import logging
import sqlite3
import struct
import sys
import time
from datetime import datetime, timedelta, timezone
from pathlib import Path
from urllib.error import URLError
from urllib.request import Request, urlopen

logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(message)s", datefmt="%H:%M:%S")
log = logging.getLogger("embed")

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

CONFIG = {
    "endpoint": "http://localhost:4141",
    "model": "text-embedding-ada-002",
    "dimensions": 1536,
    "batch_size": 50,
    "max_text_length": 8000,
    "retry_attempts": 3,
    "retry_delay": 2,
}

DB_DIR = Path(__file__).resolve().parent.parent / "data"
DB_PATH = DB_DIR / "reddit.db"

# ---------------------------------------------------------------------------
# Database schema for embeddings
# ---------------------------------------------------------------------------

EMBED_SCHEMA = """
CREATE TABLE IF NOT EXISTS post_embeddings (
    post_id     TEXT PRIMARY KEY,
    embedding   BLOB NOT NULL,
    model       TEXT NOT NULL,
    dimensions  INTEGER NOT NULL,
    text_hash   TEXT NOT NULL,
    created_at  TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_embed_created ON post_embeddings(created_at);
"""


def init_db() -> sqlite3.Connection:
    conn = sqlite3.connect(str(DB_PATH))
    conn.row_factory = sqlite3.Row
    conn.executescript(EMBED_SCHEMA)
    conn.commit()
    return conn


# ---------------------------------------------------------------------------
# Embedding serialization (compact binary, no numpy dependency for storage)
# ---------------------------------------------------------------------------

def serialize_embedding(vec: list[float]) -> bytes:
    return struct.pack(f"{len(vec)}f", *vec)


def deserialize_embedding(blob: bytes, dims: int) -> list[float]:
    return list(struct.unpack(f"{dims}f", blob))


# ---------------------------------------------------------------------------
# Cosine similarity (pure Python, numpy optional for batch speed)
# ---------------------------------------------------------------------------

def _cosine_py(a: list[float], b: list[float]) -> float:
    dot = sum(x * y for x, y in zip(a, b))
    na = sum(x * x for x in a) ** 0.5
    nb = sum(x * x for x in b) ** 0.5
    if na == 0 or nb == 0:
        return 0.0
    return dot / (na * nb)


try:
    import numpy as np

    def cosine_similarity(a, b):
        a, b = np.asarray(a, dtype=np.float32), np.asarray(b, dtype=np.float32)
        dot = np.dot(a, b)
        na, nb = np.linalg.norm(a), np.linalg.norm(b)
        if na == 0 or nb == 0:
            return 0.0
        return float(dot / (na * nb))

    def batch_cosine(query_vec, matrix):
        q = np.asarray(query_vec, dtype=np.float32)
        m = np.asarray(matrix, dtype=np.float32)
        dots = m @ q
        norms = np.linalg.norm(m, axis=1) * np.linalg.norm(q)
        norms[norms == 0] = 1.0
        return (dots / norms).tolist()

    HAS_NUMPY = True
except ImportError:
    cosine_similarity = _cosine_py
    HAS_NUMPY = False


# ---------------------------------------------------------------------------
# Embedding API
# ---------------------------------------------------------------------------

def generate_embedding(text: str, endpoint: str = None, model: str = None) -> list[float] | None:
    endpoint = endpoint or CONFIG["endpoint"]
    model = model or CONFIG["model"]
    text = text[:CONFIG["max_text_length"]]

    payload = json.dumps({"input": text, "model": model}).encode("utf-8")
    req = Request(
        f"{endpoint}/v1/embeddings",
        data=payload,
        headers={"Content-Type": "application/json"},
        method="POST",
    )

    for attempt in range(CONFIG["retry_attempts"]):
        try:
            with urlopen(req, timeout=30) as resp:
                data = json.loads(resp.read())
                return data["data"][0]["embedding"]
        except (URLError, KeyError, json.JSONDecodeError) as e:
            if attempt < CONFIG["retry_attempts"] - 1:
                time.sleep(CONFIG["retry_delay"])
            else:
                log.error("Embedding API failed after %d attempts: %s", CONFIG["retry_attempts"], e)
                return None


def batch_embed(texts: list[str], endpoint: str = None, model: str = None) -> list[list[float] | None]:
    endpoint = endpoint or CONFIG["endpoint"]
    model = model or CONFIG["model"]
    results = []
    bs = CONFIG["batch_size"]

    for i in range(0, len(texts), bs):
        batch = [t[:CONFIG["max_text_length"]] for t in texts[i:i + bs]]
        payload = json.dumps({"input": batch, "model": model}).encode("utf-8")
        req = Request(
            f"{endpoint}/v1/embeddings",
            data=payload,
            headers={"Content-Type": "application/json"},
            method="POST",
        )

        success = False
        for attempt in range(CONFIG["retry_attempts"]):
            try:
                with urlopen(req, timeout=60) as resp:
                    data = json.loads(resp.read())
                    sorted_data = sorted(data["data"], key=lambda x: x["index"])
                    results.extend([d["embedding"] for d in sorted_data])
                    success = True
                    break
            except (URLError, KeyError, json.JSONDecodeError) as e:
                if attempt < CONFIG["retry_attempts"] - 1:
                    log.warning("Batch %d/%d attempt %d failed: %s", i // bs + 1, (len(texts) + bs - 1) // bs, attempt + 1, e)
                    time.sleep(CONFIG["retry_delay"])
                else:
                    log.error("Batch %d/%d failed permanently: %s", i // bs + 1, (len(texts) + bs - 1) // bs, e)

        if not success:
            results.extend([None] * len(batch))

        if i + bs < len(texts):
            time.sleep(0.5)

    return results


# ---------------------------------------------------------------------------
# Post text preparation
# ---------------------------------------------------------------------------

def _post_text(post: dict) -> str:
    parts = [post.get("title", "")]
    body = post.get("body") or ""
    if body:
        parts.append(body[:2000])
    comments_text = post.get("top_comments") or ""
    if comments_text:
        parts.append(f"Comments: {comments_text[:2000]}")
    return "\n\n".join(parts)


def _text_hash(text: str) -> str:
    import hashlib
    return hashlib.md5(text.encode("utf-8")).hexdigest()[:12]


# ---------------------------------------------------------------------------
# Core operations
# ---------------------------------------------------------------------------

def embed_posts(conn: sqlite3.Connection, period_start: str = None, period_end: str = None,
                rebuild: bool = False, endpoint: str = None) -> dict:
    if rebuild:
        conn.execute("DELETE FROM post_embeddings")
        conn.commit()
        log.info("Cleared all existing embeddings (rebuild mode)")

    query = """
        SELECT p.id, p.title, p.body, p.subreddit_id, p.score, p.created_utc,
               GROUP_CONCAT(c.body, ' ||| ') as top_comments
        FROM posts p
        LEFT JOIN (
            SELECT post_id, body, score,
                   ROW_NUMBER() OVER (PARTITION BY post_id ORDER BY score DESC) as rn
            FROM comments
        ) c ON c.post_id = p.id AND c.rn <= 3
    """
    params = []
    conditions = []
    if period_start:
        conditions.append("p.created_utc >= ?")
        params.append(period_start)
    if period_end:
        conditions.append("p.created_utc <= ?")
        params.append(period_end)

    if conditions:
        query += " WHERE " + " AND ".join(conditions)
    query += " GROUP BY p.id"

    rows = conn.execute(query, params).fetchall()
    log.info("Found %d posts in period", len(rows))

    if not rebuild:
        existing = set(r[0] for r in conn.execute("SELECT post_id FROM post_embeddings").fetchall())
        rows = [r for r in rows if r["id"] not in existing]
        log.info("After filtering existing: %d posts to embed", len(rows))

    if not rows:
        log.info("No new posts to embed")
        return {"total": 0, "embedded": 0, "failed": 0}

    posts_data = [dict(r) for r in rows]
    texts = [_post_text(p) for p in posts_data]
    hashes = [_text_hash(t) for t in texts]

    log.info("Generating embeddings for %d posts...", len(texts))
    embeddings = batch_embed(texts, endpoint=endpoint)

    embedded, failed = 0, 0
    now = datetime.now(timezone.utc).isoformat()
    dims = None

    for post, emb, h in zip(posts_data, embeddings, hashes):
        if emb is None:
            failed += 1
            continue
        if dims is None:
            dims = len(emb)
            log.info("Embedding dimensions: %d", dims)
        conn.execute(
            """INSERT OR REPLACE INTO post_embeddings (post_id, embedding, model, dimensions, text_hash, created_at)
               VALUES (?, ?, ?, ?, ?, ?)""",
            (post["id"], serialize_embedding(emb), CONFIG["model"], dims, h, now),
        )
        embedded += 1
        if embedded % 100 == 0:
            conn.commit()
            log.info("Progress: %d/%d embedded", embedded, len(texts))

    conn.commit()
    log.info("Done: %d embedded, %d failed", embedded, failed)
    return {"total": len(texts), "embedded": embedded, "failed": failed}


def search(conn: sqlite3.Connection, query: str, top_k: int = 10,
           product: str = None, endpoint: str = None) -> list[dict]:
    query_emb = generate_embedding(query, endpoint=endpoint)

    if query_emb is None:
        log.warning("Embedding API unavailable, falling back to keyword search")
        return _keyword_search(conn, query, top_k, product)

    rows = conn.execute(
        "SELECT post_id, embedding, dimensions FROM post_embeddings"
    ).fetchall()

    if not rows:
        log.warning("No embeddings in database")
        return []

    dims = rows[0]["dimensions"]
    post_ids = [r["post_id"] for r in rows]
    all_embs = [deserialize_embedding(r["embedding"], dims) for r in rows]

    if HAS_NUMPY:
        scores = batch_cosine(query_emb, all_embs)
    else:
        scores = [cosine_similarity(query_emb, e) for e in all_embs]

    ranked = sorted(zip(post_ids, scores), key=lambda x: x[1], reverse=True)

    results = []
    for pid, score in ranked:
        if len(results) >= top_k:
            break
        post = conn.execute(
            """SELECT p.id, p.title, p.body, p.author, p.score as upvotes, p.num_comments,
                      p.url, p.created_utc, p.subreddit_id, s.product_name
               FROM posts p JOIN subreddits s ON p.subreddit_id = s.id
               WHERE p.id = ?""",
            (pid,),
        ).fetchone()
        if post is None:
            continue
        if product and post["product_name"].lower() != product.lower():
            continue
        results.append({
            "id": post["id"],
            "title": post["title"],
            "body": (post["body"] or "")[:500],
            "author": post["author"],
            "upvotes": post["upvotes"],
            "num_comments": post["num_comments"],
            "url": post["url"],
            "created_utc": post["created_utc"],
            "subreddit": post["subreddit_id"],
            "product": post["product_name"],
            "similarity": round(score, 4),
        })

    return results


def _keyword_search(conn: sqlite3.Connection, query: str, top_k: int, product: str = None) -> list[dict]:
    words = query.split()
    conditions = " AND ".join(["(p.title LIKE ? OR p.body LIKE ?)"] * len(words))
    params = []
    for w in words:
        params.extend([f"%{w}%", f"%{w}%"])

    sql = f"""
        SELECT p.id, p.title, p.body, p.author, p.score as upvotes, p.num_comments,
               p.url, p.created_utc, p.subreddit_id, s.product_name
        FROM posts p JOIN subreddits s ON p.subreddit_id = s.id
        WHERE {conditions}
    """
    if product:
        sql += " AND s.product_name = ?"
        params.append(product)
    sql += " ORDER BY p.score DESC LIMIT ?"
    params.append(top_k)

    results = []
    for row in conn.execute(sql, params).fetchall():
        results.append({
            "id": row["id"], "title": row["title"],
            "body": (row["body"] or "")[:500], "author": row["author"],
            "upvotes": row["upvotes"], "num_comments": row["num_comments"],
            "url": row["url"], "created_utc": row["created_utc"],
            "subreddit": row["subreddit_id"], "product": row["product_name"],
            "similarity": None,
        })
    return results


def get_stats(conn: sqlite3.Connection) -> dict:
    total_posts = conn.execute("SELECT COUNT(*) FROM posts").fetchone()[0]
    total_embedded = conn.execute("SELECT COUNT(*) FROM post_embeddings").fetchone()[0]
    models = conn.execute("SELECT DISTINCT model FROM post_embeddings").fetchall()
    latest = conn.execute("SELECT MAX(created_at) FROM post_embeddings").fetchone()[0]
    return {
        "total_posts": total_posts,
        "total_embedded": total_embedded,
        "coverage": f"{total_embedded / total_posts * 100:.1f}%" if total_posts > 0 else "0%",
        "models": [r[0] for r in models],
        "latest_embedding": latest,
        "has_numpy": HAS_NUMPY,
    }


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def main():
    parser = argparse.ArgumentParser(description="Reddit CI - Embedding Retrieval")
    parser.add_argument("--embed", action="store_true", help="Generate embeddings for posts")
    parser.add_argument("--query", type=str, help="Semantic search query")
    parser.add_argument("--top-k", type=int, default=10, help="Number of results (default: 10)")
    parser.add_argument("--product", type=str, help="Filter by product name")
    parser.add_argument("--rebuild", action="store_true", help="Re-embed all posts (clear existing)")
    parser.add_argument("--stats", action="store_true", help="Show embedding statistics")
    parser.add_argument("--start-date", type=str, help="Period start (YYYY-MM-DD)")
    parser.add_argument("--end-date", type=str, help="Period end (YYYY-MM-DD)")
    parser.add_argument("--days", type=int, default=14, help="Days back from today (default: 14)")
    parser.add_argument("--endpoint", type=str, default=CONFIG["endpoint"], help="LLM endpoint")
    args = parser.parse_args()

    conn = init_db()

    if args.stats:
        stats = get_stats(conn)
        print(json.dumps(stats, indent=2, ensure_ascii=False))
        return

    if args.embed or args.rebuild:
        start = args.start_date
        end = args.end_date
        if not start:
            end_dt = datetime.now(timezone.utc)
            start_dt = end_dt - timedelta(days=args.days)
            start = start_dt.strftime("%Y-%m-%d")
            end = end_dt.strftime("%Y-%m-%d")
        result = embed_posts(conn, start, end, rebuild=args.rebuild, endpoint=args.endpoint)
        print(json.dumps(result, indent=2))
        return

    if args.query:
        results = search(conn, args.query, top_k=args.top_k, product=args.product, endpoint=args.endpoint)
        if not results:
            print("No results found.")
            return
        for i, r in enumerate(results, 1):
            sim = f" (similarity: {r['similarity']})" if r['similarity'] is not None else " (keyword match)"
            print(f"\n{'='*60}")
            print(f"#{i}{sim}")
            print(f"  [{r['product']}] r/{r['subreddit']} | ↑{r['upvotes']} | {r['num_comments']} comments")
            print(f"  {r['title']}")
            print(f"  {r['url']}")
            if r['body']:
                print(f"  {r['body'][:200]}...")
        return

    parser.print_help()


if __name__ == "__main__":
    main()
