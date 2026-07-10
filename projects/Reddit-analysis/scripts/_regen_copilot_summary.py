"""Regenerate summary + summary_en for Copilot and M365 Copilot after spam filtering."""
import json
import re
import sys
import requests

REPORT_PATH = 'data/reports/latest.json'
ENDPOINT = sys.argv[1] if len(sys.argv) > 1 else 'http://localhost:4141'
MODEL = sys.argv[2] if len(sys.argv) > 2 else 'gpt-4.1-2025-04-14'


def call(prompt, max_tokens=2000):
    resp = requests.post(
        f'{ENDPOINT}/v1/chat/completions',
        json={
            'model': MODEL,
            'messages': [{'role': 'user', 'content': prompt}],
            'temperature': 0.3,
            'max_tokens': max_tokens,
        },
        timeout=180,
    )
    resp.raise_for_status()
    return resp.json()['choices'][0]['message']['content'].strip()


with open(REPORT_PATH, 'r', encoding='utf-8') as f:
    report = json.load(f)

for prod in report['products']:
    name = prod['product_name']
    if name.lower() not in ('copilot', 'm365 copilot', 'm365copilot'):
        continue

    sd = prod['sentiment_distribution']
    tot = sum(sd.values())
    pos = sd.get('positive', 0)
    neg = sd.get('negative', 0)
    neu = sd.get('neutral', 0)
    mix = sd.get('mixed', 0)
    pos_pct = round(pos / tot * 100) if tot else 0
    neg_pct = round(neg / tot * 100) if tot else 0

    tp = prod.get('typical_posts', [])[:10]
    top_titles = [f"[{t.get('sentiment_label','?')} · {t.get('num_comments',0)}评] {t.get('title','')}: {t.get('sentiment_reason','')[:150]}" for t in tp]
    topic_dist = prod.get('topic_distribution', {})
    filter_note = prod.get('spam_filter_note', '')

    context = f"""产品: {name}
时间段: 2026-06-23 至 2026-07-07
帖子总数: {tot}
情感分布: 正面={pos}({pos_pct}%), 负面={neg}({neg_pct}%), 中性={neu}, 混合={mix}
话题分布: {json.dumps(topic_dist, ensure_ascii=False)}
数据清洗说明: {filter_note}

高热度代表帖(按 score+评论数 排序):
""" + "\n".join(f"- {t}" for t in top_titles)

    zh_prompt = f"""你是 AI 竞品情报分析师。基于以下 Reddit 社区数据，写一份该产品的深度分析摘要（中文）。

{context}

要求:
1. 400-600 字，段落式，语言简洁客观有洞察
2. 开头一句话概括本期社区舆情走势（用具体数字：帖数、正/负比例）
3. 分析主要痛点（3-5 条，引用具体帖子标题作证据）
4. 分析亮点/正面反馈（如果有的话）
5. 与主要竞品的对比（如用户提到 Claude/Gemini/ChatGPT）
6. 结尾给 2-3 条可操作建议
7. 如果数据清洗说明中有 spam 过滤，务必在开头 caveat 一句说明"本期已剔除 XX 条 spam/无关帖"
8. 只返回摘要正文，不要 markdown 标题"""

    en_prompt = f"""You are an AI competitive intelligence analyst. Based on the following Reddit community data, write a deep analysis summary for this product (English).

{context}

Requirements:
1. 250-400 words, paragraph style, concise and insightful
2. Opening sentence: community sentiment trend with specific numbers (posts, positive/negative %)
3. Main pain points (3-5, cite specific post titles as evidence)
4. Strengths / positive feedback (if any)
5. Competitor comparisons if users mentioned Claude/Gemini/ChatGPT
6. End with 2-3 actionable recommendations
7. If the data cleaning note mentions spam filtering, add a caveat sentence at the start
8. Return only the summary body, no markdown headers"""

    print(f'\n=== Regenerating {name} ===')
    prod['summary'] = call(zh_prompt, 2500)
    prod['summary_en'] = call(en_prompt, 2000)
    print(f'  zh: {prod["summary"][:80]}...')
    print(f'  en: {prod["summary_en"][:80]}...')

with open(REPORT_PATH, 'w', encoding='utf-8') as f:
    json.dump(report, f, ensure_ascii=False, indent=2)

print('\nSaved.')
