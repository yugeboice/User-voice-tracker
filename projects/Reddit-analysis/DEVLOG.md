# Reddit Competitive Intelligence — 开发日志

## 项目概述

抓取 Reddit 上 AI 产品相关社区的帖子和评论，通过 LLM 分析情感、话题、痛点，生成竞品情报报告，展示在 Web dashboard。

**技术栈**：Python · SQLite · Copilot API (localhost:4141, OpenAI-compatible) · 静态 HTML dashboard

---

## 社区列表（11个）

| 社区 | 产品 |
|------|------|
| r/ChatGPT | ChatGPT |
| r/ChatGPTcomplaints | ChatGPT |
| r/ClaudeAI | Claude |
| r/claude | Claude |
| r/GeminiAI | Gemini |
| r/GoogleGeminiAI | Gemini |
| r/GithubCopilot | GitHub Copilot |
| r/microsoft_365_copilot | M365 Copilot |
| r/Copilot | Copilot |
| r/CopilotMicrosoft | CopilotMicrosoft |
| r/CopilotPro | CopilotPro |

> 早期还有 r/MicrosoftCopilot，因为完全无内容（全超时）已从 DB 删除。

---

## 数据库（data/reddit.db）

### 表结构
- `subreddits` — 社区列表
- `posts` — 帖子（INSERT OR IGNORE 去重）
- `comments` — 评论（INSERT OR IGNORE 去重）
- `analysis_runs` — 每次分析任务记录
- `post_analysis` — 每帖的分析结果（filter + topic + sentiment + key_points）
- `reports` — 生成的报告（JSON）

### 数据量（截至 2026-03-30）
- 帖子总数：~2896
- 评论总数：~20794
- 最新数据：2026-03-29（昨晚爬取）

---

## 分析流程（analyze.py）

```
对每个社区（串行）：
  Step 1: filter_posts         — LLM 过滤无效帖（batch=15）
  Step 2: classify_and_analyze — LLM 分类话题+情感（batch=15）
  Step 3: select_typical_posts — 纯计算，选代表性帖子
  Step 4: generate_product_summary    — 1次LLM，生成中文摘要
  Step 5: extract_structured_insights — 1次LLM，提取 pain_points/strengths/recommendations

最后：
  Step 6: cross_product_comparison — 1次LLM，跨产品对比报告
```

**速率**：约 2.8 帖/分钟（受 Copilot API 响应速度限制）

---

## 已完成的 Analysis Runs

| run_id | 时间段 | 帖数 | 社区数 | 状态 | 备注 |
|--------|--------|------|--------|------|------|
| e33d9e83 | 02-26~03-12 | 300 | 5 | completed | 早期测试，模型 gpt-4o |
| 43c5af8e | 02-26~03-12 | 300 | 5 | completed | 早期测试，模型 claude-opus-4.6 |
| de61b91f | 03-05~03-19 | 761 | 8 | completed | 最完整的旧 run，95% 有效率 |
| fb3752b6 | 03-06~03-20 | 100 | 8 | running（僵死）| 中途断开，未完成 |
| 6d5eb392 | 03-06~03-20 | 300 | 8 | running（僵死）| 中途断开，未完成 |
| c7501b57 | 03-10~03-24 | 711 | 8 | completed | 用于 dashboard，84% 有效率 |
| 0c8ecdeb | 03-01~03-30 | ~400 | 11 | running | 当前跑的，只完成 ClaudeAI+ChatGPT，剩余 9 个社区未开始 |

---

## 已知问题（待修复）

### Bug 1：pain_points / strengths / recommendations 全为空
- 所有已完成 run 的报告里这三个字段都是空数组
- 根因：Step 5 (`extract_structured_insights`) 的 LLM 返回 JSON 解析失败，fallback 静默返回空，未记录错误
- 影响：dashboard 缺少最核心的洞察层

### Bug 2：同产品多社区重复出现
- report JSON 的 `products` 数组里 ChatGPT 出现 2 次、Claude 出现 2 次、Gemini 出现 2 次
- 根因：analyze.py 按 subreddit 逐个分析，未在报告生成时合并同 product_name 的条目
- 影响：dashboard 展示混乱，图表数据不准确

### Bug 3：无断点续跑
- analyze.py 中断后只能从头重跑，已完成的社区会重复分析
- 根因：无 checkpoint 机制
- 影响：14天全量需要 3~5 小时，中途网络抖动即前功尽弃

---

## 操作历史

### 2026-03-12
- 首次运行，5 个社区，测试两个模型（gpt-4o、claude-opus-4.6）

### 2026-03-19~20
- 扩展到 8 个社区，运行 de61b91f，完成 761 帖分析
- 两次运行中途网络断开（fb3752b6、6d5eb392 僵死）

### 2026-03-24
- 成功完成 c7501b57（8社区，03-10~03-24，711帖）
- 这是目前 dashboard 展示的主要数据源

### 2026-03-29（隔夜）
- 新增 3 个社区：r/Copilot、r/CopilotMicrosoft、r/CopilotPro
- 删除 r/MicrosoftCopilot（无内容，全超时）
- 完成全量爬取（11个社区，数据截至 03-29）

### 2026-03-30
- 启动 0c8ecdeb（29天全量，11社区），但速率只有 2.8帖/分钟
- 发现 pain_points 全空、重复 product 等问题
- 决定：停 0c8ecdeb，修复 3 个 bug 后重跑 --days 14

---

## 下一步计划

### 当前（调试阶段，方案 B）
1. [ ] 修复 pain_points Bug（Step 5 解析失败）
2. [ ] 修复同产品多社区重复问题（报告合并逻辑）
3. [ ] 加断点续跑（per-subreddit checkpoint）
4. [ ] 停 0c8ecdeb，重跑 `python analyze.py --days 14`
5. [ ] 验证 dashboard 数据质量

### 未来（生产阶段，方案 C）
- 每周一固定运行流程：
  ```powershell
  python scrape.py --days 7
  python analyze.py --days 14
  ```
- analyze.py 增量机制：`post_analysis` 加 `(post_id, model)` 唯一约束，跳过已分析帖
- 报告聚合从 DB 全局查询，不依赖单次 run 的数据
- 预计每周运行时间：30~45 分钟（vs 当前 3~5 小时）

---

## 常用命令

```powershell
# 进入脚本目录
cd "c:/Users/dorisrao/vibeprojects/lab/projects/Reddit-analysis/scripts"

# 爬取
$env:PYTHONIOENCODING="utf-8"; python scrape.py --days 14

# 分析
$env:PYTHONIOENCODING="utf-8"; python analyze.py --days 14

# 启动 dashboard
python serve.py --port 8407
# 访问 http://localhost:8407

# 查看分析进度
python -c "import sqlite3; conn=sqlite3.connect('../data/reddit.db'); c=conn.cursor(); c.execute('SELECT id,status,started_at FROM analysis_runs ORDER BY rowid DESC LIMIT 3'); [print(r) for r in c.fetchall()]"

# 防止 Windows 休眠（爬取/分析期间）
powercfg /change standby-timeout-ac 0
# 恢复
powercfg /change standby-timeout-ac 30
```
