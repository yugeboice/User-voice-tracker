"""Generate executive summary with news correlation for the report."""
import json
import re
import requests
import sys

REPORT_PATH = sys.argv[1]
NEWS_PATH = sys.argv[2]
ENDPOINT = sys.argv[3]
MODEL = sys.argv[4]

with open(REPORT_PATH, 'r', encoding='utf-8') as f:
    report = json.load(f)

with open(NEWS_PATH, 'r', encoding='utf-8') as f:
    news = f.read()

product_summaries = []
for p in report['products']:
    pps = [pp.get('text', '') + ': ' + pp.get('detail', '') for pp in p.get('pain_points', [])]
    strengths = [s.get('text', '') + ': ' + s.get('detail', '') for s in p.get('strengths', [])]
    product_summaries.append(
        f"\n**{p['product_name']}** ({p['post_count']} posts, {p['valid_post_count']} valid)\n"
        f"Sentiment: {json.dumps(p.get('sentiment_distribution', {}))}\n"
        f"Pain points: {'; '.join(pps[:3])}\n"
        f"Strengths: {'; '.join(strengths[:3])}\n"
    )

prompt = f"""You are an AI industry analyst. Based on the following news events and Reddit community analysis data for the period 2026-04-28 to 2026-05-11, write an executive summary.

## News Events This Period:
{news}

## Reddit Community Analysis:
{''.join(product_summaries)}

Write an executive_summary JSON object with these fields:
1. "news_events" - array of objects with "event", "date", "product", "reddit_reaction" (how Reddit users discussed it)
2. "product_trends" - object with each product name as key, value is a short trend summary
3. "key_insights_en" - 3-5 bullet points in English summarizing the most important findings
4. "key_insights_zh" - same insights in Chinese

Return ONLY valid JSON, no markdown code blocks."""

resp = requests.post(
    f'{ENDPOINT}/v1/chat/completions',
    json={
        'model': MODEL,
        'messages': [{'role': 'user', 'content': prompt}],
        'temperature': 0.3,
        'max_tokens': 4096,
    },
    timeout=300,
)
resp.raise_for_status()
content = resp.json()['choices'][0]['message']['content']

content = content.strip()
if content.startswith('```'):
    content = re.sub(r'^```(?:json)?\s*', '', content)
    content = re.sub(r'\s*```\s*$', '', content)

try:
    exec_summary = json.loads(content)
except json.JSONDecodeError:
    # Try to find JSON object in content
    match = re.search(r'\{[\s\S]*\}', content)
    if match:
        exec_summary = json.loads(match.group())
    else:
        print("ERROR: Could not parse JSON from LLM response")
        print(content[:500])
        sys.exit(1)

print(f'News events: {len(exec_summary.get("news_events", []))}')
print(f'Key insights EN: {len(exec_summary.get("key_insights_en", []))}')
print(f'Key insights ZH: {len(exec_summary.get("key_insights_zh", []))}')

report['executive_summary'] = exec_summary
with open(REPORT_PATH, 'w', encoding='utf-8') as f:
    json.dump(report, f, ensure_ascii=False, indent=2)
print('Executive summary added to report!')
