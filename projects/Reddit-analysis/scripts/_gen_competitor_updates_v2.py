"""Generate competitor_updates with real Reddit sources from DB."""
import json
import re
import sqlite3
import requests
import sys

REPORT_PATH = sys.argv[1]
NEWS_PATH = sys.argv[2]
ENDPOINT = sys.argv[3]
MODEL = sys.argv[4]
DB_PATH = "data/reddit.db"
RUN_ID = "7fd7603b"
PERIOD_START = "2026-06-23"
PERIOD_END = "2026-07-07"

with open(REPORT_PATH, 'r', encoding='utf-8') as f:
    report = json.load(f)

with open(NEWS_PATH, 'r', encoding='utf-8') as f:
    news = f.read()

conn = sqlite3.connect(DB_PATH)
conn.row_factory = sqlite3.Row

# Get top posts per product by score
product_subreddits = {
    "ChatGPT": ["ChatGPT", "ChatGPTcomplaints"],
    "Claude": ["ClaudeAI", "claude"],
    "Gemini": ["GeminiAI", "GoogleGeminiAI"],
    "Copilot": ["Copilot", "CopilotMicrosoft", "CopilotPro"],
    "M365 Copilot": ["microsoft_365_copilot", "MicrosoftCopilot", "GithubCopilot"],
}

# For each news event, find related posts by keyword search
news_keywords = {
    "GPT-5.6 Rumors": {"product": "ChatGPT", "keywords": ["5.6", "gpt-5.6", "ember", "beacon", "new model", "faster", "slower", "codex"]},
    "ChatGPT Scheduled Tasks": {"product": "ChatGPT", "keywords": ["scheduled", "task", "reminder", "pulse", "recurring", "monitor"]},
    "ChatGPT Record Replay": {"product": "ChatGPT", "keywords": ["record", "replay", "workflow", "skill", "reusable"]},
    "Claude Fable 5 Launch": {"product": "Claude", "keywords": ["fable", "fable 5", "mythos", "most powerful", "best model", "new model"]},
    "Claude Fable 5 Suspended": {"product": "Claude", "keywords": ["suspend", "shut down", "export control", "commerce", "removed", "disabled", "gone"]},
    "Claude Fable Token Cost": {"product": "Claude", "keywords": ["token", "expensive", "burn", "cost", "drain", "limit", "2x", "double", "wallet"]},
    "Claude Fable Censorship": {"product": "Claude", "keywords": ["censor", "degrade", "sabotage", "self-limit", "restrict", "refuse", "guardrail", "safety"]},
    "Claude Opus 4.8 Flagship": {"product": "Claude", "keywords": ["opus 4.8", "4.8", "flagship", "dynamic workflow"]},
    "Gemini 3.5 Flash": {"product": "Gemini", "keywords": ["3.5 flash", "gemini 3.5", "new default", "i/o", "faster"]},
    "Gemini Omni": {"product": "Gemini", "keywords": ["omni", "video generation", "video edit", "multimodal", "create"]},
    "Antigravity Platform": {"product": "Gemini", "keywords": ["antigravity", "gemini cli", "agent platform", "managed agent"]},
    "GitHub Copilot Token Billing": {"product": "Copilot", "keywords": ["token", "credit", "billing", "usage-based", "pricing", "cost", "expensive", "10x", "50x"]},
    "GitHub Copilot Agentic Workflows": {"product": "Copilot", "keywords": ["agentic", "workflow", "agent", "cloud agent", "auto mode"]},
    "Copilot App GA": {"product": "Copilot", "keywords": ["app", "desktop", "copilot app", "generally available"]},
    "M365 Copilot Claude Integration": {"product": "M365 Copilot", "keywords": ["claude", "anthropic", "model choice", "multiple model", "flexibility"]},
    "M365 Copilot Licensing": {"product": "M365 Copilot", "keywords": ["license", "licensing", "restrict", "limit", "word", "excel", "copilot license"]},
    "M365 Scout Agent": {"product": "M365 Copilot", "keywords": ["scout", "personal agent", "always-on", "autopilot"]},
    "GitHub Copilot Model Deprecation": {"product": "M365 Copilot", "keywords": ["deprecat", "opus 4.6", "gpt-4.1", "gpt-5.2", "removed", "retire"]},
}

def find_related_posts(product, keywords, limit=4):
    """Find top posts matching keywords for a product."""
    subs = product_subreddits.get(product, [])
    if not subs:
        return []

    placeholders = ",".join(["?"] * len(subs))
    all_posts = conn.execute(f"""
        SELECT p.id, p.title, p.body, p.score, p.num_comments, p.url, p.subreddit_id, p.created_utc
        FROM posts p
        WHERE p.subreddit_id IN ({placeholders})
        AND p.created_utc >= ? AND p.created_utc < ?
        ORDER BY p.score DESC
    """, (*subs, PERIOD_START, PERIOD_END)).fetchall()

    matched = []
    for post in all_posts:
        title_lower = (post["title"] or "").lower()
        body_lower = (post["body"] or "").lower()
        text = title_lower + " " + body_lower
        if any(kw.lower() in text for kw in keywords):
            matched.append(post)

    # Return top by score
    matched.sort(key=lambda x: x["score"], reverse=True)
    return matched[:limit]

def get_subreddit_url(subreddit_id, post_id):
    return f"https://www.reddit.com/r/{subreddit_id}/comments/{post_id}/"

# Build events with real sources
events_data = {}
for event_name, config in news_keywords.items():
    product = config["product"]
    posts = find_related_posts(product, config["keywords"])
    if posts:
        sources = []
        for p in posts:
            sources.append({
                "id": p["id"],
                "title": p["title"],
                "score": p["score"],
                "comments": p["num_comments"],
                "url": get_subreddit_url(p["subreddit_id"], p["id"]),
            })

        # Get sentiment stats for these posts
        post_ids = [p["id"] for p in posts]
        # Also get broader stats - all posts matching keywords
        all_matched = find_related_posts(product, config["keywords"], limit=500)
        total_posts = len(all_matched)
        total_comments = sum(p["num_comments"] for p in all_matched)
        total_score = sum(p["score"] for p in all_matched)

        # Get sentiment from analysis
        sentiment_counts = {"positive": 0, "negative": 0, "neutral": 0, "mixed": 0}
        classified = 0
        for p in all_matched:
            pa = conn.execute("SELECT sentiment_label FROM post_analysis WHERE run_id=? AND post_id=?",
                            (RUN_ID, p["id"])).fetchone()
            if pa:
                label = pa["sentiment_label"]
                if label in sentiment_counts:
                    sentiment_counts[label] += 1
                    classified += 1

        events_data[event_name] = {
            "product": product,
            "sources": sources,
            "stats": {
                "posts": total_posts,
                "comments": total_comments,
                "score": total_score,
                "classified": classified,
                "pos": sentiment_counts["positive"],
                "neg": sentiment_counts["negative"],
                "mix": sentiment_counts["mixed"],
                "neu": sentiment_counts["neutral"],
                "pos_pct": round(sentiment_counts["positive"] / classified * 100) if classified else 0,
                "neg_pct": round(sentiment_counts["negative"] / classified * 100) if classified else 0,
            }
        }

# Now use LLM to generate the competitor_updates text with real data
events_context = []
for name, data in events_data.items():
    s = data["stats"]
    events_context.append(
        f"Event: {name}\n"
        f"Product: {data['product']}\n"
        f"Stats: {s['posts']} posts, {s['comments']} comments, {s['classified']} classified, "
        f"{s['pos_pct']}% positive, {s['neg_pct']}% negative\n"
        f"Top posts: {json.dumps([{'title': src['title'], 'score': src['score']} for src in data['sources']], ensure_ascii=False)}\n"
    )

prompt = f"""You are writing competitor update cards for an AI competitive intelligence dashboard.
Period: 2026-06-11 to 2026-06-21.

## News Events This Period:
{news}

## Reddit Data Per Event:
{''.join(events_context)}

Generate a JSON array of 6-8 competitor_update objects. Each object MUST have:
{{
  "event_key": "the event name from above",
  "product": "product name",
  "icon": "🟢 (positive) / 🟡 (mixed) / 🔴 (negative) based on sentiment",
  "title": "Chinese title 10-15 chars describing the event",
  "title_en": "English title 4-6 words",
  "body": "Chinese body 80-120 chars - describe what happened + Reddit reaction with specific numbers",
  "body_en": "English body 50-80 words with specific numbers",
  "sentiment": "positive/negative/mixed/neutral",
  "key_points": ["3-4 Chinese key takeaways"],
  "key_points_en": ["3-4 English key takeaways"]
}}

Pick the most important 6-8 events. Use the EXACT stats provided (posts, comments, sentiment percentages).
Return ONLY a JSON array, no markdown."""

resp = requests.post(
    f'{ENDPOINT}/v1/chat/completions',
    json={
        'model': MODEL,
        'messages': [{'role': 'user', 'content': prompt}],
        'temperature': 0.2,
        'max_tokens': 6000,
    },
    timeout=300,
)
resp.raise_for_status()
content = resp.json()['choices'][0]['message']['content'].strip()

if content.startswith('```'):
    content = re.sub(r'^```(?:json)?\s*', '', content)
    content = re.sub(r'\s*```\s*$', '', content)

updates = json.loads(content)

# Merge real sources and stats into each update
for update in updates:
    event_key = update.pop("event_key", None)
    if event_key and event_key in events_data:
        update["sources"] = events_data[event_key]["sources"]
        update["stats"] = events_data[event_key]["stats"]
    else:
        # Try to match by product
        for ek, ed in events_data.items():
            if ed["product"] == update["product"] and "sources" not in update:
                update["sources"] = ed["sources"]
                update["stats"] = ed["stats"]
                break
        if "sources" not in update:
            update["sources"] = []
            update["stats"] = {}

report['competitor_updates'] = updates

# Also regenerate exec_news_summary with specific numbers
exec_prompt = f"""Write an executive news summary for an AI competitive intelligence dashboard.

Period: {PERIOD_START} to {PERIOD_END}

Events and stats:
{''.join(events_context)}

Requirements:
- Write 2-4 sentences covering the TOP 3-4 most impactful events this period
- Start with a theme/headline phrase like "本期三大焦点：" or "本期关键动态："
- Use emoji prefix per event: 🟢 (positive reaction), 🟡 (mixed), 🔴 (negative)
- Include SPECIFIC numbers: post counts, comment counts, sentiment percentages, scores
- Format: 🟢 Product Name（date）summary with numbers；🟡 Product Name...
- NO markdown bold (**), NO product name in bold - just plain text with emoji
- Each event separated by Chinese semicolon ；

Good example:
"本期三大焦点：🟢 GPT-5.5（4-15 发布）反响积极，125 帖中 37% 正面（25/68），用户称更有个性；🟡 Claude Opus 4.7（4-13 发布）讨论量爆炸（759 帖、11,526 评论），但 42% 负面 > 21% 正面；🔴 Gemini 图像生成质量下滑，387 帖 bug 报告占比 27%"

Return JSON: {{"zh": "Chinese summary as described", "en": "English summary same content and style"}}
Return ONLY JSON, no markdown."""

resp2 = requests.post(
    f'{ENDPOINT}/v1/chat/completions',
    json={
        'model': MODEL,
        'messages': [{'role': 'user', 'content': exec_prompt}],
        'temperature': 0.2,
        'max_tokens': 1000,
    },
    timeout=120,
)
resp2.raise_for_status()
content2 = resp2.json()['choices'][0]['message']['content'].strip()
if content2.startswith('```'):
    content2 = re.sub(r'^```(?:json)?\s*', '', content2)
    content2 = re.sub(r'\s*```\s*$', '', content2)
try:
    report['exec_news_summary'] = json.loads(content2)
except json.JSONDecodeError:
    # Fix common JSON issues: unescaped quotes in values
    fixed = content2
    try:
        match = re.search(r'\{[\s\S]*\}', fixed)
        if match:
            report['exec_news_summary'] = json.loads(match.group())
        else:
            raise ValueError("no JSON object found")
    except (json.JSONDecodeError, ValueError):
        # Last resort: build from competitor updates
        zh_parts = [f"{u.get('icon','')} {u['product']} {u.get('title','')}" for u in updates[:5]]
        en_parts = [f"{u.get('icon','')} {u['product']} {u.get('title_en','')}" for u in updates[:5]]
        report['exec_news_summary'] = {"zh": "；".join(zh_parts), "en": "; ".join(en_parts)}

with open(REPORT_PATH, 'w', encoding='utf-8') as f:
    json.dump(report, f, ensure_ascii=False, indent=2)

print(f"Generated {len(updates)} competitor updates")
for u in updates:
    print(f"  {u['product']}: {u['title']} ({len(u.get('sources',[]))} sources)")
print(f"exec_news_summary: {report['exec_news_summary']['zh'][:80]}...")
print("Done!")
