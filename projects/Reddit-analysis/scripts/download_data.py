"""
Download data files from Azure Blob Storage to local data/ directory.

Usage:
    python download_data.py          # Download all data files
    python download_data.py --dry-run  # Show what would be downloaded
"""

import argparse
import json
from pathlib import Path

SCRIPTS_DIR = Path(__file__).resolve().parent
PROJECT_DIR = SCRIPTS_DIR.parent
DATA_DIR = PROJECT_DIR / "data"
CONFIG_FILE = SCRIPTS_DIR / "share.config.json"


def main():
    parser = argparse.ArgumentParser(description="Download data from Azure Blob Storage")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    # Load config
    try:
        with open(CONFIG_FILE) as f:
            config = json.load(f)
        blob_cfg = config.get("azure_blob", {})
        conn_str = blob_cfg.get("connection_string", "").strip()
        container = blob_cfg.get("container_name", "reddit-analysis-data").strip()
    except Exception as e:
        print(f"[ERROR] Could not read {CONFIG_FILE}: {e}")
        return

    if not conn_str:
        print("[ERROR] azure_blob.connection_string not set in share.config.json")
        print("  Copy the connection string from Azure portal > Storage Account > Access keys")
        return

    try:
        from azure.storage.blob import BlobServiceClient
    except ImportError:
        print("[ERROR] azure-storage-blob not installed.")
        print("  Run: pip install azure-storage-blob")
        return

    client = BlobServiceClient.from_connection_string(conn_str)
    container_client = client.get_container_client(container)

    try:
        blobs = list(container_client.list_blobs())
    except Exception as e:
        print(f"[ERROR] Could not list blobs: {e}")
        return

    if not blobs:
        print("No files found in container.")
        return

    print(f"Found {len(blobs)} file(s) in '{container}':\n")
    for blob in blobs:
        local_path = PROJECT_DIR / blob.name
        print(f"  {blob.name}")
        if args.dry_run:
            continue
        local_path.parent.mkdir(parents=True, exist_ok=True)
        with open(local_path, "wb") as f:
            data = container_client.download_blob(blob.name).readall()
            f.write(data)

    if args.dry_run:
        print("\n[DRY RUN] No files downloaded.")
    else:
        print(f"\nDone. {len(blobs)} file(s) downloaded to data/")


if __name__ == "__main__":
    main()
