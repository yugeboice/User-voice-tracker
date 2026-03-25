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
    parser.add_argument("--subreddits", default="ChatGPT,ClaudeAI,Gemini,GithubCopilot,MicrosoftCopilot")
    parser.add_argument("--days", type=int, default=14)
    parser.add_argument("--limit", type=int, default=100)
    parser.add_argument("--llm-endpoint", default="http://localhost:4141")
    parser.add_argument("--model", default="gpt-4")
    parser.add_argument("--port", type=int, default=8407)
    parser.add_argument("--scrape-only", action="store_true")
    parser.add_argument("--analyze-only", action="store_true")
    parser.add_argument("--serve-only", action="store_true")
    parser.add_argument("--skip-comments", action="store_true")
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

    translate_cmd = [
        PYTHON, str(SCRIPTS_DIR / "translate.py"),
        "--llm-endpoint", args.llm_endpoint,
        "--model", args.model,
    ]

    serve_cmd = [
        PYTHON, str(SCRIPTS_DIR / "serve.py"),
        "--port", str(args.port),
    ]

    if args.scrape_only:
        run_step("Scraping Reddit", scrape_cmd)
    elif args.analyze_only:
        if run_step("Analyzing with LLM", analyze_cmd):
            run_step("Translating to English", translate_cmd)
    elif args.serve_only:
        run_step("Starting Dashboard", serve_cmd)
    else:
        # Full pipeline
        if not run_step("Step 1/4: Scraping Reddit", scrape_cmd):
            sys.exit(1)
        if not run_step("Step 2/4: Analyzing with LLM", analyze_cmd):
            print("\n[WARNING] LLM analysis failed - dashboard may show without analysis.")
            print("  Check that Copilot API is running: npx copilot-api@0.5.14 start")
            print("  Or run analysis separately: python analyze.py\n")
        else:
            if not run_step("Step 3/4: Translating to English", translate_cmd):
                print("\n[WARNING] Translation failed - English mode will fall back to Chinese.\n")
        run_step("Step 4/4: Starting Dashboard", serve_cmd)


if __name__ == "__main__":
    main()
