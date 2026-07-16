"""
Tests for embed.py - Embedding retrieval module

Run: python test_embed.py
"""

import json
import sqlite3
import struct
import sys
import unittest
from pathlib import Path
from unittest.mock import patch, MagicMock

sys.path.insert(0, str(Path(__file__).resolve().parent))
from embed import (
    serialize_embedding, deserialize_embedding, _cosine_py, _post_text,
    _text_hash, _keyword_search, init_db, CONFIG, EMBED_SCHEMA,
)


class TestSerialization(unittest.TestCase):
    def test_roundtrip(self):
        vec = [0.1, 0.2, -0.3, 0.0, 1.0]
        blob = serialize_embedding(vec)
        result = deserialize_embedding(blob, len(vec))
        for a, b in zip(vec, result):
            self.assertAlmostEqual(a, b, places=5)

    def test_empty(self):
        blob = serialize_embedding([])
        result = deserialize_embedding(blob, 0)
        self.assertEqual(result, [])

    def test_high_dim(self):
        vec = [float(i) / 1536 for i in range(1536)]
        blob = serialize_embedding(vec)
        self.assertEqual(len(blob), 1536 * 4)
        result = deserialize_embedding(blob, 1536)
        self.assertEqual(len(result), 1536)


class TestCosineSimilarity(unittest.TestCase):
    def test_identical(self):
        v = [1.0, 2.0, 3.0]
        self.assertAlmostEqual(_cosine_py(v, v), 1.0, places=5)

    def test_orthogonal(self):
        a = [1.0, 0.0]
        b = [0.0, 1.0]
        self.assertAlmostEqual(_cosine_py(a, b), 0.0, places=5)

    def test_opposite(self):
        a = [1.0, 0.0]
        b = [-1.0, 0.0]
        self.assertAlmostEqual(_cosine_py(a, b), -1.0, places=5)

    def test_zero_vector(self):
        self.assertEqual(_cosine_py([0, 0], [1, 1]), 0.0)


class TestPostText(unittest.TestCase):
    def test_title_only(self):
        result = _post_text({"title": "Hello"})
        self.assertEqual(result, "Hello")

    def test_title_body_comments(self):
        result = _post_text({"title": "T", "body": "B", "top_comments": "C"})
        self.assertIn("T", result)
        self.assertIn("B", result)
        self.assertIn("C", result)

    def test_empty_body(self):
        result = _post_text({"title": "T", "body": ""})
        self.assertEqual(result, "T")


class TestTextHash(unittest.TestCase):
    def test_deterministic(self):
        self.assertEqual(_text_hash("hello"), _text_hash("hello"))

    def test_different(self):
        self.assertNotEqual(_text_hash("hello"), _text_hash("world"))


class TestKeywordSearch(unittest.TestCase):
    def setUp(self):
        self.conn = sqlite3.connect(":memory:")
        self.conn.row_factory = sqlite3.Row
        self.conn.executescript("""
            CREATE TABLE subreddits (id TEXT PK, product_name TEXT, display_name TEXT, url TEXT, last_scraped_at TEXT);
            CREATE TABLE posts (id TEXT PK, subreddit_id TEXT, title TEXT, body TEXT, author TEXT,
                score INTEGER, num_comments INTEGER, url TEXT, created_utc TEXT, scraped_at TEXT);
            INSERT INTO subreddits VALUES ('ClaudeAI', 'Claude', 'r/ClaudeAI', '', '');
            INSERT INTO posts VALUES ('p1', 'ClaudeAI', 'Claude code generation is amazing', 'I love it', 'u1', 10, 2, 'http://x', '2026-01-01', '');
            INSERT INTO posts VALUES ('p2', 'ClaudeAI', 'Bug report', 'not related', 'u2', 5, 1, 'http://y', '2026-01-02', '');
        """)

    def test_finds_match(self):
        results = _keyword_search(self.conn, "code generation", 10, None)
        self.assertEqual(len(results), 1)
        self.assertEqual(results[0]["id"], "p1")

    def test_no_match(self):
        results = _keyword_search(self.conn, "nonexistent_xyz", 10, None)
        self.assertEqual(len(results), 0)

    def test_product_filter(self):
        results = _keyword_search(self.conn, "code", 10, "Claude")
        self.assertEqual(len(results), 1)

    def test_product_filter_excludes(self):
        results = _keyword_search(self.conn, "code", 10, "ChatGPT")
        self.assertEqual(len(results), 0)


class TestEmbedSchema(unittest.TestCase):
    def test_schema_creates_table(self):
        conn = sqlite3.connect(":memory:")
        conn.executescript(EMBED_SCHEMA)
        tables = conn.execute("SELECT name FROM sqlite_master WHERE type='table'").fetchall()
        names = [t[0] for t in tables]
        self.assertIn("post_embeddings", names)


class TestSpecialCharacters(unittest.TestCase):
    def test_emoji_in_text(self):
        result = _post_text({"title": "🔥 Claude is great!", "body": "Love the 🤖"})
        self.assertIn("🔥", result)

    def test_url_in_text(self):
        result = _post_text({"title": "Check https://example.com", "body": ""})
        self.assertIn("https://", result)

    def test_code_snippet(self):
        result = _post_text({"title": "Bug", "body": "```python\nprint('hello')\n```"})
        self.assertIn("print", result)


if __name__ == "__main__":
    unittest.main()
