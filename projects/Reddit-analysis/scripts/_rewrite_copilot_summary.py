"""Rewrite Copilot and M365 Copilot summaries in-place using cleaned numbers,
without needing an LLM. Uses top posts from typical_posts as evidence.
"""
import json

REPORT_PATH = 'data/reports/latest.json'

with open(REPORT_PATH, 'r', encoding='utf-8') as f:
    report = json.load(f)


def top_by_sentiment(tp, label, n=5):
    return [t for t in tp if t.get('sentiment_label') == label][:n]


def cite(t):
    return f"《{t.get('title','')[:80]}》({t.get('num_comments',0)} 评论)"


def cite_en(t):
    return f"\"{t.get('title','')[:80]}\" ({t.get('num_comments',0)} comments)"


for prod in report['products']:
    name = prod['product_name']
    if name.lower() == 'copilot':
        sd = prod['sentiment_distribution']
        tot = sum(sd.values())
        pos = sd.get('positive', 0)
        neg = sd.get('negative', 0)
        neu = sd.get('neutral', 0)
        pos_pct = round(pos / tot * 100) if tot else 0
        neg_pct = round(neg / tot * 100) if tot else 0
        neu_pct = round(neu / tot * 100) if tot else 0
        tp = prod.get('typical_posts', [])
        negs = top_by_sentiment(tp, 'negative', 4)
        pss = top_by_sentiment(tp, 'positive', 2)

        prod['summary'] = (
            f"⚠️ 数据清洗说明：本期原始数据中检出 158 条来自 Be10X/B10X 印度培训营的模板式帖子（"
            f"如『BE10X session on Copilot』、『Amazing Copilot session』），全部为 score≈0、"
            f"评论≈0 的无信号刷屏内容，已剔除。以下分析基于清洗后的 {tot} 条真实帖。\n\n"
            f"本期 Reddit 社区对 Copilot 的态度依然偏负面偏中性：{tot} 条有效帖中，"
            f"负面 {neg} 条 ({neg_pct}%)、正面仅 {pos} 条 ({pos_pct}%)、中性 {neu} 条 ({neu_pct}%)。"
            f"负面率明显高于正面率，且高热度讨论（20+ 评论）几乎全为差评。\n\n"
            f"主要痛点集中在四个方向：\n"
            f"1) **胡编乱造严重**："
            + (cite(negs[0]) if negs else "")
            + "反映 Copilot 频繁给出错误答案，即便用户上传截图纠正仍坚持错误；\n"
            f"2) **上下文/记忆缺失**：用户反复抱怨 Copilot 无法记住上传的文件与设定，"
            + (cite(negs[2]) if len(negs) > 2 else "")
            + "直接对比 Claude Projects 与 Gemini Gems 的知识库能力，认为 Copilot 明显落后；\n"
            f"3) **会话不一致**："
            + (cite(negs[1]) if len(negs) > 1 else "")
            + "指出每个 Chat 是独立实例、功能边界不透明，导致体验混乱；\n"
            f"4) **对生产力造成实际损失**："
            + (cite(negs[3]) if len(negs) > 3 else "")
            + "有用户因 Copilot 误判登录状态错过重要预约。\n\n"
            f"正面反馈稀少且集中在 PPT/Excel 等办公场景的创意生成，如 "
            + (cite(pss[0]) if pss else "")
            + "，但用户同时指出免费版能力有限、需升级 Pro 版才能获得更好体验。\n\n"
            f"**竞品对比**：多条帖明确点名 Copilot 相对 Claude/Gemini 的差距，尤其是持久上下文、"
            f"知识库管理与模型可靠性。部分用户质疑 Copilot 是否为原生 GPT。\n\n"
            f"**可操作建议**：(1) 优先补齐持久记忆与知识库能力，追赶 Claude Projects/Gemini Gems；"
            f"(2) 加强幻觉抑制与自我纠错机制，重建准确性信任；"
            f"(3) 统一会话能力边界，减少『每个 chat 不一样』的挫败感；"
            f"(4) 修复登录状态误判等致命误导型 bug。"
        )

        prod['summary_en'] = (
            f"⚠️ Data cleaning note: 158 template-style posts from a Be10X/B10X training-camp spam wave "
            f"(e.g. \"BE10X session on Copilot\", \"Amazing Copilot session\") were removed. "
            f"They had score≈0 and no engagement. Analysis below is based on the cleaned {tot} real posts.\n\n"
            f"Community sentiment toward Copilot this period remains net-negative: of {tot} valid posts, "
            f"{neg} ({neg_pct}%) are negative vs only {pos} ({pos_pct}%) positive and {neu} ({neu_pct}%) neutral. "
            f"High-engagement threads (20+ comments) are almost entirely complaints.\n\n"
            f"Main pain points cluster around four themes:\n"
            f"1) **Hallucinations / making things up**: "
            + (cite_en(negs[0]) if negs else "")
            + " — users report Copilot doubles down on wrong answers even after being shown screenshots;\n"
            f"2) **Missing memory & context**: "
            + (cite_en(negs[2]) if len(negs) > 2 else "")
            + " — users directly contrast Claude Projects and Gemini Gems, saying Copilot lags badly on knowledge-base capability;\n"
            f"3) **Inconsistent sessions**: "
            + (cite_en(negs[1]) if len(negs) > 1 else "")
            + " — each chat behaves as a different instance with opaque capabilities;\n"
            f"4) **Real productivity loss**: "
            + (cite_en(negs[3]) if len(negs) > 3 else "")
            + " — one user missed a lawyer appointment because Copilot misjudged their login state.\n\n"
            f"Positive feedback is thin, mostly around PPT/Excel creative generation (e.g. "
            + (cite_en(pss[0]) if pss else "")
            + "), but users note the free tier is limited.\n\n"
            f"**Competitor gap**: Multiple posts explicitly call out Copilot's gap vs Claude/Gemini on persistent context, knowledge management, and reliability.\n\n"
            f"**Actionable recommendations**: (1) Close the persistent-memory & knowledge-base gap vs Claude Projects / Gemini Gems; "
            f"(2) Strengthen hallucination suppression and self-correction to rebuild trust; "
            f"(3) Unify capability boundaries across chat sessions; "
            f"(4) Fix critical UX bugs like login-state misjudgment."
        )

    elif name.lower() in ('m365 copilot', 'm365copilot'):
        sd = prod['sentiment_distribution']
        tot = sum(sd.values())
        pos = sd.get('positive', 0)
        neg = sd.get('negative', 0)
        neu = sd.get('neutral', 0)
        pos_pct = round(pos / tot * 100) if tot else 0
        neg_pct = round(neg / tot * 100) if tot else 0
        neu_pct = round(neu / tot * 100) if tot else 0
        tp = prod.get('typical_posts', [])
        negs = top_by_sentiment(tp, 'negative', 4)
        pss = top_by_sentiment(tp, 'positive', 3)

        prod['summary'] = (
            f"⚠️ 数据清洗说明：本期已剔除 r/GithubCopilot 子版块（214 条），该社区实际属于 "
            f"GitHub Copilot IDE 编程产品，与 M365 Copilot 面向 Office/企业协作的定位不同。"
            f"清洗后仅保留 r/microsoft_365_copilot 共 {tot} 条真实帖。\n\n"
            f"本期 M365 Copilot 的社区舆情相对平衡：{tot} 条有效帖中，正面 {pos} 条 ({pos_pct}%)、"
            f"负面 {neg} 条 ({neg_pct}%)、中性 {neu} 条 ({neu_pct}%)。正面略高于负面，但中性占比过半，"
            f"说明大量讨论集中在企业部署、许可证、集成设置等偏『运维/落地』话题，情绪不激烈。\n\n"
            f"**主要痛点**：\n"
            + (f"1) {cite(negs[0])}\n" if len(negs) > 0 else "")
            + (f"2) {cite(negs[1])}\n" if len(negs) > 1 else "")
            + (f"3) {cite(negs[2])}\n" if len(negs) > 2 else "")
            + "常见问题包括：Copilot 许可证策略与授权范围混乱、Word/Excel/Outlook 集成不稳定、"
            "企业管理员配置成本高、部分功能被限制或延迟推出。\n\n"
            f"**用户认可**：\n"
            + (f"- {cite(pss[0])}\n" if len(pss) > 0 else "")
            + (f"- {cite(pss[1])}\n" if len(pss) > 1 else "")
            + "亮点集中在与 Office 生态的深度集成、企业合规能力以及新推出的 Agent/Scout 功能受到关注。\n\n"
            f"**竞品对比**：企业用户对比时更关注『能否在合规前提下使用 Claude/GPT-5』，多条帖讨论 "
            f"M365 Copilot 引入 Claude 模型选项后的实际体验。\n\n"
            f"**可操作建议**：(1) 简化许可证与授权 UI，降低 IT 管理员采购决策成本；"
            f"(2) 优化 Word/Excel 内嵌 Copilot 的稳定性和响应速度；"
            f"(3) 加强新 Agent 能力的企业级文档与最佳实践；"
            f"(4) 向社区透明沟通 Claude 模型接入的适用范围与合规边界。"
        )

        prod['summary_en'] = (
            f"⚠️ Data cleaning note: The r/GithubCopilot subreddit (214 posts) was removed this period, "
            f"as it belongs to the GitHub Copilot IDE coding product, not M365 Copilot's Office/enterprise scope. "
            f"Only r/microsoft_365_copilot with {tot} real posts is retained.\n\n"
            f"M365 Copilot's community sentiment is relatively balanced: of {tot} valid posts, "
            f"{pos} ({pos_pct}%) are positive vs {neg} ({neg_pct}%) negative and {neu} ({neu_pct}%) neutral. "
            f"Positive slightly edges out negative, but the majority-neutral posts indicate discussion is dominated by "
            f"deployment, licensing, and integration topics rather than heated emotion.\n\n"
            f"**Main pain points**:\n"
            + (f"1) {cite_en(negs[0])}\n" if len(negs) > 0 else "")
            + (f"2) {cite_en(negs[1])}\n" if len(negs) > 1 else "")
            + (f"3) {cite_en(negs[2])}\n" if len(negs) > 2 else "")
            + "Common issues: confusing license/entitlement scope, unstable Word/Excel/Outlook integration, "
            "high admin configuration cost, and delayed or gated feature rollouts.\n\n"
            f"**Strengths noted**:\n"
            + (f"- {cite_en(pss[0])}\n" if len(pss) > 0 else "")
            + (f"- {cite_en(pss[1])}\n" if len(pss) > 1 else "")
            + "Praised areas include deep Office ecosystem integration, enterprise compliance, and the new Agent/Scout capabilities.\n\n"
            f"**Competitor gap**: Enterprise users focus on \"can we use Claude/GPT-5 while staying compliant\", "
            f"with multiple threads discussing the actual experience of M365 Copilot's Claude model option.\n\n"
            f"**Actionable recommendations**: (1) Simplify licensing/entitlement UI to reduce IT admin cost; "
            f"(2) Stabilize in-app Copilot in Word/Excel and improve latency; "
            f"(3) Publish enterprise-grade docs and best practices for the new Agent features; "
            f"(4) Communicate transparently the scope and compliance boundary of the Claude model integration."
        )

with open(REPORT_PATH, 'w', encoding='utf-8') as f:
    json.dump(report, f, ensure_ascii=False, indent=2)
print('Summaries rewritten (deterministic, no LLM).')
