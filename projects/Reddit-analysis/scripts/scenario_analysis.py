"""
Scenario Analysis Module — Independent from main analyze.py

Reads post_analysis data (with scenario_tags from LLM) and generates
a standalone scenario report JSON. Does NOT modify existing reports.

Outputs:
  data/reports/scenario_report_{run_id}_{timestamp}.json
  data/reports/latest_scenario.json

Usage:
    python scenario_analysis.py --run-id <run_id>
    python scenario_analysis.py --start-date 2026-05-05 --end-date 2026-05-18
"""

import argparse
import json
import logging
import os
import sqlite3
import sys
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path

DB_DIR = Path(__file__).resolve().parent.parent / "data"
DB_PATH = DB_DIR / "reddit.db"
REPORTS_DIR = DB_DIR / "reports"

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

EXCLUDED_PRODUCTS = {"GitHub Copilot"}

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
    datefmt="%H:%M:%S",
)
log = logging.getLogger("scenario")


def generate_scenario_report(
    run_id: str = None,
    start_date: str = None,
    end_date: str = None,
) -> dict:
    """Generate scenario analysis report from existing post_analysis data."""

    conn = sqlite3.connect(str(DB_PATH))
    conn.row_factory = sqlite3.Row

    # Determine query scope
    if run_id:
        where_clause = "pa.run_id = ?"
        params = [run_id]
        # Get period from run
        run = conn.execute("SELECT * FROM analysis_runs WHERE id=?", (run_id,)).fetchone()
        if not run:
            log.error("Run %s not found", run_id)
            return {}
        period_start = run["period_start"]
        period_end = run["period_end"]
    elif start_date and end_date:
        where_clause = "p.created_utc >= ? AND p.created_utc < ?"
        params = [start_date, end_date + "T23:59:59"]
        period_start = start_date
        period_end = end_date
        # Find latest run_id for these dates
        run = conn.execute(
            "SELECT id FROM analysis_runs ORDER BY started_at DESC LIMIT 1"
        ).fetchone()
        run_id = run["id"] if run else "manual"
    else:
        # Use latest run
        run = conn.execute(
            "SELECT * FROM analysis_runs WHERE status='completed' ORDER BY started_at DESC LIMIT 1"
        ).fetchone()
        if not run:
            log.error("No completed analysis runs found")
            return {}
        run_id = run["id"]
        where_clause = "pa.run_id = ?"
        params = [run_id]
        period_start = run["period_start"]
        period_end = run["period_end"]

    log.info("Generating scenario report for run %s (period: %s to %s)", run_id, period_start[:10], period_end[:10])

    # Fetch analyzed posts with scenario_tags
    rows = conn.execute(f"""
        SELECT DISTINCT pa.post_id, pa.topic_category, pa.sentiment_label, pa.sentiment_score,
            pa.sentiment_reason, pa.key_points, pa.scenario_tags,
            p.title, p.body, p.score, p.num_comments, p.url, p.created_utc,
            p.source_type, p.origin_subreddit,
            s.product_name, s.display_name, s.id as subreddit_id
        FROM post_analysis pa
        JOIN posts p ON pa.post_id = p.id
        JOIN subreddits s ON p.subreddit_id = s.id
        WHERE {where_clause} AND pa.is_valid = 1
            AND s.product_name NOT IN ({','.join('?' for _ in EXCLUDED_PRODUCTS)})
        ORDER BY p.score DESC
    """, params + list(EXCLUDED_PRODUCTS)).fetchall()

    log.info("Found %d analyzed posts", len(rows))

    # Build scenario breakdown per product
    # Structure: product -> scenario -> {count, sentiment, posts}
    product_scenarios = defaultdict(lambda: defaultdict(lambda: {
        "count": 0, "positive": 0, "negative": 0, "neutral": 0, "mixed": 0,
        "avg_score": 0.0, "_scores": [],
        "top_posts": [],  # top 5 by score
        "source_breakdown": {"official_sub": 0, "keyword_search": 0},
    }))

    for r in rows:
        d = dict(r)
        product = d["product_name"]
        sentiment = d["sentiment_label"] or "neutral"
        source_type = d.get("source_type") or "official_sub"

        # Parse scenario_tags
        try:
            tags = json.loads(d["scenario_tags"]) if d["scenario_tags"] else []
        except (json.JSONDecodeError, TypeError):
            tags = []

        if not tags:
            tags = ["general_purpose"]

        post_info = {
            "post_id": d["post_id"],
            "title": d["title"][:100],
            "score": d["score"],
            "num_comments": d["num_comments"],
            "url": d["url"],
            "sentiment_label": sentiment,
            "sentiment_reason": d["sentiment_reason"] or "",
            "source_type": source_type,
            "origin_subreddit": d.get("origin_subreddit") or d["display_name"],
            "subreddit": d["display_name"],
        }

        for tag in tags:
            if tag not in SCENARIO_TAXONOMY:
                continue
            sc = product_scenarios[product][tag]
            sc["count"] += 1
            sc[sentiment] = sc.get(sentiment, 0) + 1
            sc["_scores"].append(d.get("sentiment_score") or 0.0)
            sc["source_breakdown"][source_type] = sc["source_breakdown"].get(source_type, 0) + 1

            # Keep top 5 posts by score
            if len(sc["top_posts"]) < 5:
                sc["top_posts"].append(post_info)
            elif d["score"] > min(p["score"] for p in sc["top_posts"]):
                sc["top_posts"].sort(key=lambda x: x["score"])
                sc["top_posts"][0] = post_info

    # Calculate averages and sort top_posts
    for product, scenarios in product_scenarios.items():
        for sc_name, sc in scenarios.items():
            if sc["_scores"]:
                sc["avg_score"] = round(sum(sc["_scores"]) / len(sc["_scores"]), 2)
            del sc["_scores"]
            sc["top_posts"].sort(key=lambda x: -x["score"])

    # Build cross-product scenario comparison
    scenario_comparison = {}
    for sc_name in SCENARIO_TAXONOMY:
        comparison = {}
        for product in product_scenarios:
            sc = product_scenarios[product].get(sc_name)
            if sc and sc["count"] > 0:
                total = sc["count"]
                comparison[product] = {
                    "count": total,
                    "positive_pct": round(sc["positive"] / total * 100, 1) if total else 0,
                    "negative_pct": round(sc["negative"] / total * 100, 1) if total else 0,
                    "avg_sentiment": sc["avg_score"],
                    "keyword_search_pct": round(sc["source_breakdown"].get("keyword_search", 0) / total * 100, 1),
                }
        if comparison:
            scenario_comparison[sc_name] = comparison

    # Build report
    report = {
        "report_type": "scenario_analysis",
        "run_id": run_id,
        "period": {"start": period_start, "end": period_end},
        "generated_at": datetime.now(timezone.utc).isoformat(),
        "total_posts_analyzed": len(rows),
        "products": dict(product_scenarios),
        "scenario_comparison": scenario_comparison,
        "metadata": {
            "scenario_taxonomy": SCENARIO_TAXONOMY,
            "note": "scenario_tags from LLM classification; keyword_search posts are from cross-subreddit search and may need quality review",
        },
    }

    # Save report
    REPORTS_DIR.mkdir(parents=True, exist_ok=True)
    ts = datetime.now().strftime("%Y%m%d_%H%M%S")
    report_filename = f"scenario_report_{run_id}_{ts}.json"
    report_path = REPORTS_DIR / report_filename
    report_json = json.dumps(report, indent=2, ensure_ascii=False)
    report_path.write_text(report_json, encoding="utf-8")
    (REPORTS_DIR / "latest_scenario.json").write_text(report_json, encoding="utf-8")

    # Sync to wwwroot if exists
    wwwroot_reports = Path(__file__).resolve().parent.parent / "wwwroot" / "data" / "reports"
    if wwwroot_reports.exists():
        import shutil
        shutil.copy2(report_path, wwwroot_reports / report_filename)
        (wwwroot_reports / "latest_scenario.json").write_text(report_json, encoding="utf-8")
        log.info("Synced scenario report to wwwroot")

    conn.close()

    log.info("=" * 60)
    log.info("SCENARIO REPORT COMPLETE")
    log.info("=" * 60)
    log.info("Report: %s", report_path)
    log.info("Products: %s", list(product_scenarios.keys()))
    for product, scenarios in product_scenarios.items():
        active = {k: v["count"] for k, v in scenarios.items() if v["count"] > 0}
        log.info("  %s: %s", product, active)

    return report


def main():
    parser = argparse.ArgumentParser(description="Scenario Analysis Module (independent)")
    parser.add_argument("--run-id", default=None, help="Analysis run ID to process")
    parser.add_argument("--start-date", default=None, metavar="YYYY-MM-DD")
    parser.add_argument("--end-date", default=None, metavar="YYYY-MM-DD")
    args = parser.parse_args()

    generate_scenario_report(
        run_id=args.run_id,
        start_date=args.start_date,
        end_date=args.end_date,
    )


if __name__ == "__main__":
    main()
