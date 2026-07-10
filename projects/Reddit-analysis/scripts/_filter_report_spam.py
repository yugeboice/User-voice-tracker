"""Post-process latest.json to remove Be10x/B10x session spam from Copilot data,
and remove GithubCopilot posts from M365 Copilot data.

Recomputes: valid_post_count, sentiment_distribution, topic_distribution, typical_posts,
strengths/pain_points (kept as-is from LLM but numbers in summary already stale).
"""
import json
import re
import sqlite3
import sys

REPORT_PATH = 'data/reports/latest.json'
DB_PATH = 'data/reddit.db'
RUN_ID = '7fd7603b'
START = '2026-06-23'
END = '2026-07-08'

SPAM_RE = re.compile(
    r'\b(be?10x|be10x|session on copilot|session for the copilot|amazing copilot session|'
    r'joined be10x|copilot session|copilot sesstion|b10x session|be10x session|'
    r'had.{0,20}session.{0,20}copilot|copilot is amazing session|b10x)\b',
    re.I,
)

conn = sqlite3.connect(DB_PATH)
conn.row_factory = sqlite3.Row


def fetch_posts(subs):
    rows = conn.execute(
        f"""
        SELECT p.id, p.title, p.body, p.score, p.num_comments, p.subreddit_id,
               p.created_utc, p.url, p.author,
               pa.sentiment_label, pa.sentiment_score, pa.sentiment_reason,
               pa.topic_category, pa.key_points
        FROM posts p
        JOIN post_analysis pa ON pa.post_id=p.id AND pa.run_id=?
        WHERE p.subreddit_id IN ({','.join(['?']*len(subs))})
          AND p.created_utc >= ? AND p.created_utc < ?
        """,
        (RUN_ID, *subs, START, END),
    ).fetchall()
    return [dict(r) for r in rows]


def is_spam(row):
    text = (row['title'] or '') + ' ' + (row['body'] or '')[:200]
    return bool(SPAM_RE.search(text))


def compute_stats(rows):
    sd = {'positive': 0, 'negative': 0, 'neutral': 0, 'mixed': 0}
    td = {}
    for r in rows:
        s = r.get('sentiment_label')
        if s in sd:
            sd[s] += 1
        t = r.get('topic_category') or 'unknown'
        td[t] = td.get(t, 0) + 1
    return sd, td


def build_typical_posts(rows, limit=10):
    """Pick top by (score + num_comments*2), diverse across sentiments."""
    ranked = sorted(rows, key=lambda x: (x.get('score', 0) or 0) + 2 * (x.get('num_comments', 0) or 0), reverse=True)
    out = []
    for r in ranked[:limit]:
        key_points = r.get('key_points')
        if isinstance(key_points, str):
            try:
                key_points = json.loads(key_points)
            except Exception:
                key_points = []
        out.append({
            'id': r['id'],
            'title': r['title'],
            'body': (r['body'] or '')[:500],
            'author': r.get('author'),
            'url': r.get('url') or f"https://www.reddit.com/r/{r['subreddit_id']}/comments/{r['id']}/",
            'source_platform': 'reddit',
            'num_comments': r.get('num_comments'),
            'created_utc': r.get('created_utc'),
            'sentiment_label': r.get('sentiment_label'),
            'sentiment_score': r.get('sentiment_score'),
            'sentiment_reason': r.get('sentiment_reason'),
            'topic_category': r.get('topic_category'),
            'key_points': key_points or [],
        })
    return out


with open(REPORT_PATH, 'r', encoding='utf-8') as f:
    report = json.load(f)

changes = []

for prod in report['products']:
    name = prod['product_name']
    if name.lower() == 'copilot':
        subs = prod['subreddits']
        raw = fetch_posts(subs)
        kept = [r for r in raw if not is_spam(r)]
        removed = len(raw) - len(kept)
        sd, td = compute_stats(kept)
        old_valid = prod.get('valid_post_count')
        old_sd = prod.get('sentiment_distribution')
        prod['valid_post_count'] = len(kept)
        prod['post_count'] = len(kept)
        prod['sentiment_distribution'] = sd
        prod['topic_distribution'] = td
        prod['typical_posts'] = build_typical_posts(kept)
        prod['filtered_count'] = (prod.get('filtered_count', 0) or 0) + removed
        prod['spam_filter_note'] = f'Removed {removed} Be10x/B10x training-camp spam posts (identical "session on Copilot" template)'
        changes.append(f'Copilot: {old_valid} -> {len(kept)} valid ({removed} spam removed)')
        changes.append(f'  sentiment: {old_sd} -> {sd}')

    elif name.lower() in ('m365 copilot', 'm365copilot'):
        # Remove GithubCopilot subreddit
        old_subs = prod['subreddits']
        new_subs = [s for s in old_subs if s.lower() != 'githubcopilot']
        prod['subreddits'] = new_subs
        prod['subreddit'] = '+'.join(new_subs)
        raw = fetch_posts(new_subs)
        sd, td = compute_stats(raw)
        old_valid = prod.get('valid_post_count')
        old_sd = prod.get('sentiment_distribution')
        prod['valid_post_count'] = len(raw)
        prod['post_count'] = len(raw)
        prod['sentiment_distribution'] = sd
        prod['topic_distribution'] = td
        prod['typical_posts'] = build_typical_posts(raw)
        prod['spam_filter_note'] = 'Removed r/GithubCopilot posts (belongs to GitHub Copilot IDE product, not M365 Copilot)'
        changes.append(f'M365 Copilot: {old_valid} -> {len(raw)} valid (GithubCopilot removed, subs: {old_subs} -> {new_subs})')
        changes.append(f'  sentiment: {old_sd} -> {sd}')

# Also update total counts if present
total_valid = sum(p.get('valid_post_count', 0) for p in report['products'])
if 'total_valid_posts' in report:
    changes.append(f'total_valid_posts: {report["total_valid_posts"]} -> {total_valid}')
    report['total_valid_posts'] = total_valid
if 'total_posts' in report:
    report['total_posts'] = total_valid

with open(REPORT_PATH, 'w', encoding='utf-8') as f:
    json.dump(report, f, ensure_ascii=False, indent=2)

print('=== Filter applied ===')
for c in changes:
    print(c)
print()
print('Saved:', REPORT_PATH)
