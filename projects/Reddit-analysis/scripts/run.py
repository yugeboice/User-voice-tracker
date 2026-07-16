"""
Reddit Competitive Intelligence - One-click pipeline

Runs: scrape -> analyze -> translate -> serve dashboard

Usage:
    python run.py                     # Full pipeline (scrape + analyze + serve)
    python run.py --scrape-only       # Just scrape
    python run.py --analyze-only      # Just analyze (requires prior scrape)
    python run.py --serve-only        # Just serve dashboard
"""

import argparse
import subprocess
import sys
from pathlib import Path

SCRIPTS_DIR = Path(__file__).resolve().parent
PYTHON = str(SCRIPTS_DIR / "venv" / "Scripts" / "python")


def run_step(name: str, cmd: list[str]) -> bool:
    """Run a pipeline step and return True on success."""
    print(f"\n{'='*60}")
    print(f"  {name}")
    print(f"{'='*60}\n")
    result = subprocess.run(cmd, env={**__import__("os").environ, "PYTHONIOENCODING": "utf-8"})
    if result.returncode != 0:
        print(f"\n[ERROR] {name} failed with exit code {result.returncode}")
        return False
    return True


def main():
    parser = argparse.ArgumentParser(description="Reddit CI Pipeline")
    parser.add_argument("--subreddits", default="ChatGPT,ClaudeAI,Gemini,MicrosoftCopilot,microsoft_365_copilot,OpenAI")
    parser.add_argument("--days", type=int, default=14)
    parser.add_argument("--limit", type=int, default=100)
    parser.add_argument("--llm-endpoint", default="http://localhost:4141")
    parser.add_argument("--model", default="gpt-4")
    parser.add_argument("--port", type=int, default=8407)
    parser.add_argument("--scrape-only", action="store_true")
    parser.add_argument("--analyze-only", action="store_true")
    parser.add_argument("--serve-only", action="store_true")
    parser.add_argument("--skip-comments", action="store_true")
    parser.add_argument("--skip-keyword", action="store_true", help="Skip cross-subreddit keyword search")
    parser.add_argument("--skip-scenario", action="store_true", help="Skip scenario analysis module")
    parser.add_argument("--skip-embed", action="store_true", help="Skip embedding generation")
    parser.add_argument("--start-date", default=None, metavar="YYYY-MM-DD")
    parser.add_argument("--end-date", default=None, metavar="YYYY-MM-DD")
    args = parser.parse_args()

    scrape_cmd = [
        PYTHON, str(SCRIPTS_DIR / "scrape.py"),
        "--subreddits", args.subreddits,
        "--days", str(args.days),
        "--limit", str(args.limit),
    ]
    if args.skip_comments:
        scrape_cmd.append("--skip-comments")

    analyze_cmd = [
        PYTHON, str(SCRIPTS_DIR / "analyze.py"),
        "--days", str(args.days),
        "--llm-endpoint", args.llm_endpoint,
        "--model", args.model,
    ]
    if args.start_date and args.end_date:
        analyze_cmd += ["--start-date", args.start_date, "--end-date", args.end_date]

    # Cross-subreddit keyword search (new)
    keyword_cmd = [
        PYTHON, str(SCRIPTS_DIR / "backfill_arctic.py"),
        "--keyword",
        "--start-date", args.start_date or "",
        "--end-date", args.end_date or "",
    ]

    # Scenario analysis module (new, independent)
    scenario_cmd = [PYTHON, str(SCRIPTS_DIR / "scenario_analysis.py")]
    if args.start_date and args.end_date:
        scenario_cmd += ["--start-date", args.start_date, "--end-date", args.end_date]

    # Embedding generation
    embed_cmd = [
        PYTHON, str(SCRIPTS_DIR / "embed.py"),
        "--embed",
        "--endpoint", args.llm_endpoint,
    ]
    if args.start_date and args.end_date:
        embed_cmd += ["--start-date", args.start_date, "--end-date", args.end_date]
    else:
        embed_cmd += ["--days", str(args.days)]

    translate_cmd = [
        PYTHON, str(SCRIPTS_DIR / "translate.py"),
        "--llm-endpoint", args.llm_endpoint,
        "--model", args.model,
    ]

    serve_cmd = [
        PYTHON, str(SCRIPTS_DIR / "serve.py"),
        "--port", str(args.port),
    ]

    deploy_cmd = [PYTHON, str(SCRIPTS_DIR / "deploy.py")]
    notify_cmd = [PYTHON, str(SCRIPTS_DIR / "notify.py")]

    if args.scrape_only:
        run_step("Scraping Reddit", scrape_cmd)
    elif args.analyze_only:
        if run_step("Analyzing with LLM", analyze_cmd):
            if not args.skip_scenario:
                run_step("Scenario Analysis (independent module)", scenario_cmd)
            run_step("Translating to English", translate_cmd)
    elif args.serve_only:
        run_step("Starting Dashboard", serve_cmd)
    else:
        # Full pipeline
        if not run_step("Step 1/7: Scraping Reddit (official subs)", scrape_cmd):
            sys.exit(1)
        if not args.skip_keyword and args.start_date and args.end_date:
            run_step("Step 2/7: Cross-subreddit keyword search", keyword_cmd)
        if not run_step("Step 3/7: Analyzing with LLM", analyze_cmd):
            print("\n[WARNING] LLM analysis failed - dashboard may show without analysis.")
            print("  Check that Copilot API is running: npx copilot-api@0.5.14 start")
            print("  Or run analysis separately: python analyze.py\n")
        else:
            if not args.skip_scenario:
                run_step("Step 4/7: Scenario Analysis (independent)", scenario_cmd)
            if not args.skip_embed:
                run_step("Step 4b: Generating Embeddings", embed_cmd)
            if not run_step("Step 5/7: Translating to English", translate_cmd):
                print("\n[WARNING] Translation failed - English mode will fall back to Chinese.\n")
        if not run_step("Step 6/7: Deploying to GitHub Pages", deploy_cmd):
            print("\n[WARNING] Deploy failed - skipping Teams notification.\n")
        else:
            if not run_step("Step 7/7: Sending Teams Notification", notify_cmd):
                print("\n[WARNING] Teams notification failed.\n")
        run_step("Starting Dashboard", serve_cmd)


if __name__ == "__main__":
    main()
