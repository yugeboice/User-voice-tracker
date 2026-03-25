"""
Deploy wwwroot/ to GitHub Pages via gh-pages branch.

Usage:
    python deploy.py                    # Deploy and return URL
    python deploy.py --dry-run          # Show what would happen without doing it

Requirements:
    - git must be installed and in PATH
    - Remote repo must be set (git remote -v to check)
    - GitHub Pages must be enabled on gh-pages branch (one-time setup in GitHub settings)
"""

import argparse
import json
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

SCRIPTS_DIR = Path(__file__).resolve().parent
PROJECT_DIR = SCRIPTS_DIR.parent
WWWROOT_DIR = PROJECT_DIR / "wwwroot"


def run(cmd: list[str], cwd=None, check=True) -> subprocess.CompletedProcess:
    return subprocess.run(cmd, cwd=cwd, capture_output=True, text=True, check=check)


def get_repo_info() -> tuple[str, str]:
    """Return (remote_url, repo_slug) e.g. ('https://github.com/user/repo', 'user/repo')"""
    try:
        result = run(["git", "remote", "get-url", "origin"], cwd=PROJECT_DIR)
        url = result.stdout.strip()
        # Normalize: git@github.com:user/repo.git -> https://github.com/user/repo
        if url.startswith("git@github.com:"):
            slug = url.replace("git@github.com:", "").removesuffix(".git")
            url = f"https://github.com/{slug}"
        else:
            slug = url.removeprefix("https://github.com/").removesuffix(".git")
        return url, slug
    except subprocess.CalledProcessError:
        return "", ""


def get_pages_url(slug: str) -> str:
    """Convert repo slug to GitHub Pages URL."""
    user, repo = slug.split("/", 1)
    # User pages: username.github.io -> https://username.github.io
    # Project pages: -> https://username.github.io/repo
    if repo == f"{user}.github.io":
        return f"https://{user}.github.io"
    return f"https://{user}.github.io/{repo}"


def get_latest_report_date() -> str:
    """Read date from latest.json for commit message."""
    try:
        with open(WWWROOT_DIR / "data" / "reports" / "latest.json") as f:
            data = json.load(f)
            end = data.get("period", {}).get("end", "")
            return end[:10] if end else "unknown"
    except Exception:
        return "unknown"


def deploy(dry_run=False) -> str:
    """
    Deploy wwwroot/ to gh-pages branch using a worktree.
    Returns the deployed URL.
    """
    _, slug = get_repo_info()
    if not slug:
        print("[ERROR] Could not determine git remote. Run: git remote add origin <url>")
        sys.exit(1)

    pages_url = get_pages_url(slug)
    report_date = get_latest_report_date()
    commit_msg = f"Deploy dashboard: report {report_date}"

    print(f"  Repo:   {slug}")
    print(f"  URL:    {pages_url}")
    print(f"  Commit: {commit_msg}")

    if dry_run:
        print("\n[DRY RUN] Would deploy wwwroot/ to gh-pages branch.")
        return pages_url

    # Use a temp dir as a clean gh-pages worktree
    with tempfile.TemporaryDirectory() as tmp:
        tmp_path = Path(tmp)

        # Check if gh-pages branch exists remotely
        remote_check = run(
            ["git", "ls-remote", "--heads", "origin", "gh-pages"],
            cwd=PROJECT_DIR,
            check=False,
        )
        branch_exists = bool(remote_check.stdout.strip())

        if branch_exists:
            # Clone only gh-pages branch into tmp
            run(["git", "clone", "--branch", "gh-pages", "--single-branch",
                 "--depth", "1", ".", str(tmp_path)], cwd=PROJECT_DIR)
            # Clear existing files (keep .git)
            for item in tmp_path.iterdir():
                if item.name == ".git":
                    continue
                if item.is_dir():
                    shutil.rmtree(item)
                else:
                    item.unlink()
        else:
            # Initialize fresh orphan branch
            run(["git", "init", str(tmp_path)])
            run(["git", "checkout", "--orphan", "gh-pages"], cwd=tmp_path)
            run(["git", "remote", "add", "origin",
                 run(["git", "remote", "get-url", "origin"], cwd=PROJECT_DIR).stdout.strip()],
                cwd=tmp_path)

        # Copy wwwroot/ contents into tmp
        shutil.copytree(str(WWWROOT_DIR), str(tmp_path), dirs_exist_ok=True)

        # Add .nojekyll so GitHub doesn't process files through Jekyll
        (tmp_path / ".nojekyll").touch()

        # Commit and push
        run(["git", "config", "user.email", "reddit-ci@local"], cwd=tmp_path)
        run(["git", "config", "user.name", "Reddit CI Bot"], cwd=tmp_path)
        run(["git", "add", "--all"], cwd=tmp_path)

        # Check if there's anything to commit
        status = run(["git", "status", "--porcelain"], cwd=tmp_path)
        if not status.stdout.strip():
            print("  Nothing changed, skipping push.")
            return pages_url

        run(["git", "commit", "-m", commit_msg], cwd=tmp_path)
        run(["git", "push", "origin", "HEAD:gh-pages", "--force"], cwd=tmp_path)

    print(f"\n  Deployed to: {pages_url}")
    return pages_url


def main():
    parser = argparse.ArgumentParser(description="Deploy to GitHub Pages")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    print("\n" + "=" * 60)
    print("  Deploying to GitHub Pages")
    print("=" * 60 + "\n")

    url = deploy(dry_run=args.dry_run)
    # Write URL to a file so run.py / notify.py can read it
    out = PROJECT_DIR / "data" / "last_deploy.json"
    out.parent.mkdir(exist_ok=True)
    with open(out, "w") as f:
        json.dump({"url": url}, f)

    print(f"\nDashboard URL: {url}")
    return url


if __name__ == "__main__":
    main()
