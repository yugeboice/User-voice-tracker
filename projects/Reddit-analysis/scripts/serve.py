"""
Simple development server for the Reddit Competitive Intelligence dashboard.

Serves the static frontend and provides API endpoints to access report data.

Usage:
    python serve.py [--port 8407]
"""

import argparse
import json
import logging
import sqlite3
from http.server import HTTPServer, SimpleHTTPRequestHandler
from pathlib import Path
from urllib.parse import urlparse, parse_qs

PROJECT_DIR = Path(__file__).resolve().parent.parent
WWWROOT_DIR = PROJECT_DIR / "wwwroot"
DATA_DIR = PROJECT_DIR / "data"
REPORTS_DIR = DATA_DIR / "reports"
DB_PATH = DATA_DIR / "reddit.db"

logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(message)s")
log = logging.getLogger("server")


class DashboardHandler(SimpleHTTPRequestHandler):
    """Serves static files from wwwroot/ and API endpoints from data/."""

    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=str(WWWROOT_DIR), **kwargs)

    def do_GET(self):
        # API: latest report
        if self.path == "/data/reports/latest.json" or self.path == "/report.json":
            self.serve_latest_report()
            return

        # API: report index
        if self.path == "/data/reports/index.json":
            index_path = REPORTS_DIR / "index.json"
            if index_path.exists():
                self.send_json_file(index_path)
            else:
                self.send_json({"reports": []})
            return

        # Serve specific report file by name
        if self.path.startswith("/data/reports/report_") and self.path.endswith(".json"):
            filename = self.path.split("/")[-1]
            filepath = REPORTS_DIR / filename
            if filepath.exists():
                self.send_json_file(filepath)
            else:
                self.send_error(404, f"Report file {filename} not found")
            return

        # API: list reports
        if self.path == "/api/reddit/reports":
            self.serve_report_list()
            return

        # API: specific report
        if self.path.startswith("/api/reddit/reports/"):
            report_id = self.path.split("/")[-1]
            self.serve_report_by_id(report_id)
            return

        # API: semantic search
        if self.path.startswith("/api/reddit/search"):
            self.serve_search()
            return

        # Default: serve static files
        super().do_GET()

    def serve_latest_report(self):
        """Serve the most recent report JSON."""
        if not REPORTS_DIR.exists():
            self.send_error(404, "No reports directory")
            return

        report_files = list(REPORTS_DIR.glob("report_*.json"))
        if not report_files:
            self.send_error(404, "No reports found")
            return

        # Sort by the date portion in filename (report_XXXX_YYYYMMDD_HHMMSS.json)
        def sort_key(f):
            parts = f.stem.split("_")
            # Extract date+time parts: YYYYMMDD_HHMMSS
            if len(parts) >= 4:
                return parts[2] + parts[3]
            return f.stem

        report_files.sort(key=sort_key, reverse=True)
        self.send_json_file(report_files[0])

    def serve_report_list(self):
        """List all available reports (reads curated index.json first, falls back to file scan)."""
        index_path = REPORTS_DIR / "index.json"
        if index_path.exists():
            try:
                idx = json.loads(index_path.read_text(encoding="utf-8"))
                self.send_json(idx.get("reports", []))
                return
            except Exception:
                pass

        # Fallback: scan report files
        if not REPORTS_DIR.exists():
            self.send_json([])
            return

        reports = []
        for f in sorted(REPORTS_DIR.glob("report_*.json"), reverse=True):
            if "_analysis_" in f.name:
                continue
            try:
                data = json.loads(f.read_text(encoding="utf-8"))
                period = data.get("period", {})
                products = data.get("products", [])
                total_valid = sum(p.get("valid_post_count", 0) for p in products)
                reports.append({
                    "file": f.name,
                    "run_id": data.get("run_id", ""),
                    "period_start": period.get("start", "")[:10],
                    "period_end": period.get("end", "")[:10],
                    "product_count": len(products),
                    "total_posts": sum(p.get("post_count", 0) for p in products),
                    "total_valid_posts": total_valid,
                })
            except Exception:
                continue

        self.send_json(reports)

    def serve_report_by_id(self, report_id):
        """Serve a specific report by filename stem."""
        if not REPORTS_DIR.exists():
            self.send_error(404, "No reports directory")
            return

        for f in REPORTS_DIR.glob("report_*.json"):
            if f.stem == report_id or report_id in f.stem:
                self.send_json_file(f)
                return

        self.send_error(404, f"Report {report_id} not found")

    def serve_search(self):
        """Semantic search via embedding similarity."""
        parsed = urlparse(self.path)
        params = parse_qs(parsed.query)
        q = params.get("q", [""])[0]
        if not q:
            self.send_json({"error": "Missing 'q' parameter"})
            return
        top_k = int(params.get("top_k", ["10"])[0])
        product = params.get("product", [None])[0]
        try:
            from embed import search as embed_search, init_db as embed_init_db
            conn = embed_init_db()
            results = embed_search(conn, q, top_k=top_k, product=product)
            conn.close()
            self.send_json({"query": q, "count": len(results), "results": results})
        except Exception as e:
            log.error("Search failed: %s", e)
            self.send_json({"error": str(e), "results": []})

    def send_json_file(self, filepath: Path):
        """Send a JSON file as response."""
        try:
            content = filepath.read_bytes()
            self.send_response(200)
            self.send_header("Content-Type", "application/json; charset=utf-8")
            self.send_header("Content-Length", str(len(content)))
            self.send_header("Access-Control-Allow-Origin", "*")
            self.end_headers()
            self.wfile.write(content)
        except Exception as e:
            self.send_error(500, str(e))

    def send_json(self, data):
        """Send JSON data as response."""
        content = json.dumps(data, ensure_ascii=False).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(content)))
        self.send_header("Access-Control-Allow-Origin", "*")
        self.end_headers()
        self.wfile.write(content)

    def log_message(self, format, *args):
        if "/favicon.ico" not in str(args):
            log.info(format % args)


def main():
    parser = argparse.ArgumentParser(description="Reddit CI Dashboard Server")
    parser.add_argument("--port", type=int, default=8407, help="Port (default: 8407)")
    args = parser.parse_args()

    server = HTTPServer(("localhost", args.port), DashboardHandler)
    log.info("=" * 50)
    log.info("Reddit Competitive Intelligence Dashboard")
    log.info("Open: http://localhost:%d", args.port)
    log.info("=" * 50)
    log.info("Serving wwwroot from: %s", WWWROOT_DIR)
    log.info("Reports from: %s", REPORTS_DIR)

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        log.info("Server stopped.")
        server.server_close()


if __name__ == "__main__":
    main()
