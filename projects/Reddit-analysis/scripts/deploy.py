"""
Deploy wwwroot/ to GitHub Pages via gh-pages branch.
Also uploads data/ to Azure Blob Storage for cross-machine access.

Usage:
    python deploy.py                    # Deploy pages + upload data
    python deploy.py --dry-run          # Show what would happen without doing it
    python deploy.py --upload-only      # Only upload data to Azure Blob
    python deploy.py --skip-upload      # Deploy pages only, skip Azure upload

Requirements:
    - git must be installed and in PATH
    - Remote repo must be set (git remote -v to check)
    - GitHub Pages must be enabled on gh-pages branch (one-time setup in GitHub settings)
    - For Azure upload: pip install azure-storage-blob
      Set connection_string in scripts/share.config.json under azure_blob
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
DATA_DIR = PROJECT_DIR / "data"
CONFIG_FILE = SCRIPTS_DIR / "share.config.json"

# Files/dirs to upload under data/
UPLOAD_TARGETS = [
    "reddit.db",
    "reports",
    "last_run.json",
]


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
            # Clone only gh-pages branch into tmp (from remote URL, not local dot)
            remote_url = run(["git", "remote", "get-url", "origin"], cwd=PROJECT_DIR).stdout.strip()
            run(["git", "clone", "--branch", "gh-pages", "--single-branch",
                 "--depth", "1", remote_url, str(tmp_path)], cwd=PROJECT_DIR)
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


def upload_data_to_blob(dry_run=False) -> bool:
    """
    Upload data files to Azure Blob Storage.
    Returns True on success, False on failure/skip.
    """
    print("\n" + "=" * 60)
    print("  Uploading data to Azure Blob Storage")
    print("=" * 60 + "\n")

    # Load config
    try:
        with open(CONFIG_FILE) as f:
            config = json.load(f)
        blob_cfg = config.get("azure_blob", {})
        conn_str = blob_cfg.get("connection_string", "").strip()
        container = blob_cfg.get("container_name", "reddit-analysis-data").strip()
    except Exception as e:
        print(f"[ERROR] Could not read {CONFIG_FILE}: {e}")
        return False

    if not conn_str:
        print("[SKIP] azure_blob.connection_string not set in share.config.json")
        print("  To enable: copy your Storage Account connection string there.")
        return False

    if dry_run:
        print("[DRY RUN] Would upload the following to Azure Blob:")
        for target in UPLOAD_TARGETS:
            path = DATA_DIR / target
            if path.exists():
                print(f"  {path}")
        return True

    try:
        from azure.storage.blob import BlobServiceClient
    except ImportError:
        print("[ERROR] azure-storage-blob not installed.")
        print("  Run: pip install azure-storage-blob")
        return False

    try:
        client = BlobServiceClient.from_connection_string(conn_str)
        container_client = client.get_container_client(container)
        # Create container if it doesn't exist
        try:
            container_client.create_container()
            print(f"  Created container: {container}")
        except Exception:
            pass  # Already exists

        uploaded = 0
        for target in UPLOAD_TARGETS:
            path = DATA_DIR / target
            if not path.exists():
                continue
            if path.is_file():
                blob_name = f"data/{target}"
                with open(path, "rb") as f:
                    container_client.upload_blob(blob_name, f, overwrite=True)
                print(f"  Uploaded: {blob_name}")
                uploaded += 1
            elif path.is_dir():
                for file in path.rglob("*"):
                    if file.is_file():
                        rel = file.relative_to(DATA_DIR)
                        blob_name = f"data/{rel.as_posix()}"
                        with open(file, "rb") as f:
                            container_client.upload_blob(blob_name, f, overwrite=True)
                        print(f"  Uploaded: {blob_name}")
                        uploaded += 1

        print(f"\n  Done. {uploaded} file(s) uploaded to container '{container}'.")
        return True

    except Exception as e:
        print(f"[ERROR] Azure Blob upload failed: {e}")
        return False


def main():
    parser = argparse.ArgumentParser(description="Deploy to GitHub Pages + upload data to Azure Blob")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--upload-only", action="store_true", help="Only upload data, skip GitHub Pages deploy")
    parser.add_argument("--skip-upload", action="store_true", help="Skip Azure Blob upload")
    args = parser.parse_args()

    if not args.upload_only:
        print("\n" + "=" * 60)
        print("  Deploying to GitHub Pages")
        print("=" * 60 + "\n")

        url = deploy(dry_run=args.dry_run)
        out = PROJECT_DIR / "data" / "last_deploy.json"
        out.parent.mkdir(exist_ok=True)
        with open(out, "w") as f:
            json.dump({"url": url}, f)
        print(f"\nDashboard URL: {url}")

    if not args.skip_upload:
        upload_data_to_blob(dry_run=args.dry_run)


if __name__ == "__main__":
    main()
