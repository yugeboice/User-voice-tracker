"""
Lumina Search API client for Reddit-analysis.

Manages the C# sidecar (lumina/) as a subprocess and provides a simple
Python interface to search for Reddit post content.

Usage:
    with LuminaClient() as lumina:
        content = lumina.search_post(post_url, post_title)

The sidecar reads credentials from lumina/appsettings.json (Azure AD OBO flow).
On first run it will open a browser for interactive login; subsequent runs use
the cached MSAL token (~30 days).
"""

import json
import logging
import os
import subprocess
import time
import urllib.request
from pathlib import Path

log = logging.getLogger("lumina")

LUMINA_DIR = Path(__file__).resolve().parent.parent / "lumina"
APPSETTINGS = LUMINA_DIR / "appsettings.json"
APPSETTINGS_TEMPLATE = LUMINA_DIR / "appsettings.Template.json"
SIDECAR_PORT = 8400
SIDECAR_URL = f"http://localhost:{SIDECAR_PORT}"
STARTUP_TIMEOUT = 30  # seconds to wait for dotnet to be ready


class LuminaClient:
    """
    Thin Python wrapper around the Lumina C# sidecar.
    Can be used as a context manager (starts/stops sidecar automatically)
    or against an already-running sidecar (pass managed=False).
    """

    def __init__(self, managed: bool = True):
        """
        managed=True  → client owns the sidecar lifecycle (start on enter, stop on exit)
        managed=False → assumes sidecar is already running externally
        """
        self._managed = managed
        self._proc: subprocess.Popen | None = None

    # ------------------------------------------------------------------
    # Context manager
    # ------------------------------------------------------------------

    def __enter__(self) -> "LuminaClient":
        if self._managed:
            self.start()
        return self

    def __exit__(self, *_):
        if self._managed and self._proc:
            self.stop()

    # ------------------------------------------------------------------
    # Sidecar lifecycle
    # ------------------------------------------------------------------

    def start(self):
        """Start the C# sidecar if not already running."""
        if self._is_running():
            log.info("[Lumina] Sidecar already running on port %d", SIDECAR_PORT)
            self._proc = None  # not owned by us
            self._managed = False
            return

        self._check_appsettings()

        log.info("[Lumina] Starting sidecar (dotnet run)...")
        self._proc = subprocess.Popen(
            ["dotnet", "run"],
            cwd=str(LUMINA_DIR),
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )

        # Wait until the HTTP server is up
        deadline = time.time() + STARTUP_TIMEOUT
        while time.time() < deadline:
            if self._is_running():
                log.info("[Lumina] Sidecar ready on port %d", SIDECAR_PORT)
                return
            time.sleep(1)

        self._proc.kill()
        raise RuntimeError(
            f"Lumina sidecar did not start within {STARTUP_TIMEOUT}s. "
            "Check that 'dotnet' is installed and lumina/appsettings.json is configured."
        )

    def stop(self):
        """Stop the sidecar process."""
        if self._proc and self._proc.poll() is None:
            log.info("[Lumina] Stopping sidecar...")
            self._proc.terminate()
            try:
                self._proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self._proc.kill()
        self._proc = None

    def _is_running(self) -> bool:
        try:
            with urllib.request.urlopen(f"{SIDECAR_URL}/", timeout=2):
                return True
        except Exception:
            return False

    def _check_appsettings(self):
        if not APPSETTINGS.exists():
            raise FileNotFoundError(
                f"lumina/appsettings.json not found.\n"
                f"Copy {APPSETTINGS_TEMPLATE} to {APPSETTINGS} and fill in:\n"
                f"  AzureAd.TenantId, AzureAd.ClientId, AzureAd.RedirectUri"
            )

    # ------------------------------------------------------------------
    # Search API
    # ------------------------------------------------------------------

    def search(self, query: str, top_n: int = 3) -> list[dict]:
        """
        Call the Lumina Search API. Returns list of result dicts with keys:
        url, title, semanticDocument, dateLastCrawled, source
        """
        payload = json.dumps({"query": query, "topN": top_n}).encode()
        req = urllib.request.Request(
            f"{SIDECAR_URL}/api/search",
            data=payload,
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        with urllib.request.urlopen(req, timeout=30) as resp:
            data = json.loads(resp.read().decode())
            return data.get("results", [])

    def search_post(self, post_url: str, post_title: str = "", top_n: int = 3) -> str | None:
        """
        Search for a Reddit post's full content by title + URL.
        Returns the semanticDocument text of the best matching result, or None.

        Note: Lumina's index has a crawl lag of ~1-2 weeks.
        Posts newer than that will return related-but-different pages.
        """
        query = f"site:reddit.com {post_title}" if post_title else f"reddit {post_url}"
        try:
            results = self.search(query, top_n=top_n)
        except Exception as e:
            log.warning("[Lumina] Search failed for '%s': %s", query, e)
            return None

        # Prefer an exact URL match (post ID in URL)
        post_id = ""
        if "/comments/" in post_url:
            post_id = post_url.split("/comments/")[1].split("/")[0]

        for result in results:
            if post_id and post_id in result.get("url", ""):
                doc = result.get("semanticDocument", "")
                if doc and len(doc) > 100:
                    return doc

        # Fall back to first substantial result
        for result in results:
            doc = result.get("semanticDocument", "")
            if doc and len(doc) > 500:
                return doc

        return None
