"""
Send Teams notification with AI Voice Tracker report summary.

Usage:
    python notify.py --webhook-url <url>           # Send notification
    python notify.py --webhook-url <url> --dry-run # Print payload without sending

Config file (optional): create scripts/share.config.json with:
    { "teams_webhook_url": "<url>" }
Then just run: python notify.py
"""

import argparse
import json
import sys
import urllib.request
from pathlib import Path

SCRIPTS_DIR = Path(__file__).resolve().parent
PROJECT_DIR = SCRIPTS_DIR.parent
LATEST_JSON = PROJECT_DIR / "wwwroot" / "data" / "reports" / "latest.json"
LAST_DEPLOY_JSON = PROJECT_DIR / "data" / "last_deploy.json"
CONFIG_JSON = SCRIPTS_DIR / "share.config.json"

SENTIMENT_EMOJI = {
    "positive": "🟢",
    "negative": "🔴",
    "neutral": "⚪",
    "mixed": "🟡",
}


def load_config() -> dict:
    if CONFIG_JSON.exists():
        with open(CONFIG_JSON) as f:
            return json.load(f)
    return {}


def load_report() -> dict:
    with open(LATEST_JSON, encoding="utf-8") as f:
        return json.load(f)


def load_deploy_url() -> str:
    try:
        with open(LAST_DEPLOY_JSON) as f:
            return json.load(f).get("url", "")
    except Exception:
        return ""


def sentiment_bar(dist: dict) -> str:
    """e.g. '🟢9%  🔴30%  ⚪52%  🟡9%'"""
    total = sum(dist.values()) or 1
    parts = []
    for key in ("positive", "negative", "neutral", "mixed"):
        count = dist.get(key, 0)
        if count:
            pct = round(count / total * 100)
            parts.append(f"{SENTIMENT_EMOJI[key]}{pct}%")
    return "  ".join(parts)


def top_topics(topic_dist: dict, n=3) -> str:
    sorted_topics = sorted(topic_dist.items(), key=lambda x: x[1], reverse=True)[:n]
    labels = {
        "model_capability": "Model Capability",
        "product_experience": "Product Experience",
        "feature_request": "Feature Request",
        "bug_report": "Bug Report",
        "comparison": "Comparison",
        "use_case": "Use Case",
        "news_update": "News/Update",
        "meta": "Meta Discussion",
    }
    return ", ".join(labels.get(k, k) for k, _ in sorted_topics)


def build_payload(report: dict, dashboard_url: str) -> dict:
    period = report.get("period", {})
    start = period.get("start", "")[:10]
    end = period.get("end", "")[:10]
    products = report.get("products", [])

    # Build one section per product
    body_items = []

    for p in products:
        name = p.get("product_name", "")
        post_count = p.get("valid_post_count", p.get("post_count", 0))
        sent_dist = p.get("sentiment_distribution", {})
        topic_dist = p.get("topic_distribution", {})

        # First key_points_en from most negative post as a teaser
        teaser = ""
        posts = p.get("typical_posts", [])
        neg_posts = [x for x in posts if x.get("sentiment_label") == "negative"]
        if neg_posts:
            kp = neg_posts[0].get("key_points_en") or neg_posts[0].get("key_points", [])
            if kp:
                teaser = kp[0]

        body_items.append({
            "type": "TextBlock",
            "text": f"**{name}**  |  {post_count} posts  |  {sentiment_bar(sent_dist)}",
            "wrap": True,
            "spacing": "Medium",
        })
        body_items.append({
            "type": "TextBlock",
            "text": f"Top topics: {top_topics(topic_dist)}",
            "wrap": True,
            "isSubtle": True,
            "size": "Small",
        })
        if teaser:
            body_items.append({
                "type": "TextBlock",
                "text": f"💬 _{teaser}_",
                "wrap": True,
                "size": "Small",
                "spacing": "None",
            })

    # Adaptive Card payload for Teams Workflows webhook
    card = {
        "type": "message",
        "attachments": [
            {
                "contentType": "application/vnd.microsoft.card.adaptive",
                "contentUrl": None,
                "content": {
                    "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
                    "type": "AdaptiveCard",
                    "version": "1.4",
                    "body": [
                        {
                            "type": "TextBlock",
                            "text": f"AI Voice Tracker — {start} to {end}",
                            "weight": "Bolder",
                            "size": "Large",
                            "wrap": True,
                        },
                        {
                            "type": "TextBlock",
                            "text": f"Reddit sentiment analysis across {len(products)} AI products",
                            "isSubtle": True,
                            "wrap": True,
                            "spacing": "None",
                        },
                        {"type": "Separator"},
                        *body_items,
                    ],
                    "actions": (
                        [
                            {
                                "type": "Action.OpenUrl",
                                "title": "View Full Dashboard",
                                "url": dashboard_url,
                            }
                        ]
                        if dashboard_url
                        else []
                    ),
                },
            }
        ],
    }
    return card


def send(webhook_url: str, payload: dict) -> None:
    data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    req = urllib.request.Request(
        webhook_url,
        data=data,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            status = resp.status
            body = resp.read().decode("utf-8", errors="replace")
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"Teams returned HTTP {e.code}: {body}") from e
    if status not in (200, 202):
        raise RuntimeError(f"Teams returned HTTP {status}: {body}")
    print(f"  Teams response: HTTP {status}")


def main():
    parser = argparse.ArgumentParser(description="Send Teams notification")
    parser.add_argument("--webhook-url", default="")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    # Resolve webhook URL: CLI arg > config file
    webhook_url = args.webhook_url
    if not webhook_url:
        cfg = load_config()
        webhook_url = cfg.get("teams_webhook_url", "")
    if not webhook_url and not args.dry_run:
        print("[ERROR] No webhook URL provided. Pass --webhook-url or set in share.config.json")
        sys.exit(1)

    report = load_report()
    dashboard_url = load_deploy_url()
    payload = build_payload(report, dashboard_url)

    print("\n" + "=" * 60)
    print("  Sending Teams Notification")
    print("=" * 60 + "\n")
    print(f"  Period:    {report['period']['start'][:10]} to {report['period']['end'][:10]}")
    print(f"  Products:  {len(report['products'])}")
    print(f"  Dashboard: {dashboard_url or '(none)'}")

    if args.dry_run:
        print("\n[DRY RUN] Payload:")
        print(json.dumps(payload, indent=2, ensure_ascii=True))
        return

    send(webhook_url, payload)
    print("\n  Notification sent to Teams.")


if __name__ == "__main__":
    main()
