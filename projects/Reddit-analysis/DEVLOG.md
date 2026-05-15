# Reddit Competitive Intelligence — 开发日志

## 项目概述

抓取 Reddit 上 AI 产品相关社区的帖子和评论，通过 LLM 分析情感、话题、痛点，生成竞品情报报告，展示在双语 Web dashboard。

**技术栈**：Python · SQLite · Copilot API (localhost:4141, OpenAI-compatible) · 静态 HTML dashboard · GitHub Pages

---

## 社区列表（12个，5个产品 + 1个排除）

| 社区 | 产品 | 备注 |
|------|------|------|
| r/ChatGPT | ChatGPT | |
| r/ChatGPTcomplaints | ChatGPT | |
| r/ClaudeAI | Claude | |
| r/claude | Claude | |
| r/GeminiAI | Gemini | |
| r/GoogleGeminiAI | Gemini | |
| r/GithubCopilot | GitHub Copilot | **已排除**，不参与分析 |
| r/Copilot | Copilot | |
| r/CopilotMicrosoft | Copilot | |
| r/CopilotPro | Copilot | |
| r/MicrosoftCopilot | M365 Copilot | |
| r/microsoft_365_copilot | M365 Copilot | |

> Copilot 和 M365 Copilot 是**独立产品**，分别分析。

---

## 数据库（data/reddit.db）

### 表结构
- `subreddits` — 社区列表
- `posts` — 帖子（INSERT OR IGNORE 去重），含 score、upvote_ratio
- `comments` — 评论（INSERT OR IGNORE 去重），含 score
- `analysis_runs` — 每次分析任务记录
- `post_analysis` — 每帖的分析结果（filter + topic + sentiment + key_points）
- `reports` — 生成的报告（JSON）

---

## 分析流程（analyze.py）

```
对每个社区（串行）：
  Step 1: filter_posts         — LLM 过滤无效帖（batch=15）
  Step 2: classify_and_analyze — LLM 分类话题+情感（batch=15）
  Step 3: select_typical_posts — 纯计算，选代表性帖子

全局（按产品合并后）：
  Step 4: generate_product_summary    — 1次LLM/产品，生成中文摘要
  Step 5: extract_structured_insights — 1次LLM/产品，提取 pain_points/strengths/recommendations
  Step 6: cross_product_comparison    — 1次LLM，跨产品对比报告
```

**速率**：约 2.8 帖/分钟（受 Copilot API 响应速度限制）

---

## 已生成的报告

| run_id | 时间段 | 总帖数 | 有效帖 | 生成日期 | 备注 |
|--------|--------|--------|--------|----------|------|
| db2ee453 | 03-16~03-30 | 2,052 | 1,638 | 2026-03-30 | 5 产品 |
| 9d8bb56c | 03-02~03-15 | 3,041 | 2,328 | 2026-03-31 | 5 产品，用 fix_reports.py 补全 |

报告部署于 `wwwroot/data/reports/`，`index.json` 列出所有报告（含 `total_valid_posts` 字段）。

---

## 已完成的 Analysis Runs（历史）

| run_id | 时间段 | 帖数 | 社区数 | 状态 | 备注 |
|--------|--------|------|--------|------|------|
| e33d9e83 | 02-26~03-12 | 300 | 5 | completed | 早期测试，模型 gpt-4o |
| 43c5af8e | 02-26~03-12 | 300 | 5 | completed | 早期测试，模型 claude-opus-4.6 |
| de61b91f | 03-05~03-19 | 761 | 8 | completed | 最完整的旧 run，95% 有效率 |
| fb3752b6 | 03-06~03-20 | 100 | 8 | 僵死 | 中途断开 |
| 6d5eb392 | 03-06~03-20 | 300 | 8 | 僵死 | 中途断开 |
| c7501b57 | 03-10~03-24 | 711 | 8 | completed | 早期 dashboard 数据源 |
| 0c8ecdeb | 03-01~03-30 | ~400 | 11 | 中止 | 只完成 2 社区后停止，改用分期策略 |
| db2ee453 | 03-16~03-30 | 2,052 | 12 | completed | 正式报告 #1 |
| 9d8bb56c | 03-02~03-15 | 3,041 | 12 | completed | 正式报告 #2，补全了 ChatGPT/Copilot 结构化数据 |

---

## 操作历史

### 2026-03-12
- 首次运行，5 个社区，测试两个模型（gpt-4o、claude-opus-4.6）

### 2026-03-19~20
- 扩展到 8 个社区，运行 de61b91f，完成 761 帖分析
- 两次运行中途网络断开（fb3752b6、6d5eb392 僵死）

### 2026-03-24
- 成功完成 c7501b57（8社区，03-10~03-24，711帖）

### 2026-03-29（隔夜）
- 新增 3 个社区：r/Copilot、r/CopilotMicrosoft、r/CopilotPro
- 完成全量爬取（11个社区，数据截至 03-29）

### 2026-03-30
- 启动 0c8ecdeb（29天全量），发现 pain_points 全空 + 重复 product 等 bug
- 引入 Arctic Shift 归档服务替代 RSS，获得真实 upvote scores
- 完成 03-16~03-30 报告（db2ee453）

### 2026-03-31（重大更新）
- 通过 Arctic Shift 回填 03-02~03-15 数据（15,584 帖）
- 完成 9d8bb56c 报告分析 + 翻译
- 创建 fix_reports.py 解决 resume bug（产品摘要不完整）
- 创建 fix_display.py 从 markdown 提取结构化数据
- 修复 dashboard 多个前端问题（详见下方）
- 首次部署到 GitHub Pages

### 2026-04-15（安全升级：AAD 认证）
- Azure Blob 认证从 Key/Connection String 改为 **AAD (DefaultAzureCredential)**
- `share.config.json` 不再存储密钥，只保存 `account_url`
- Storage Account `uservoicedata` 已关闭 Key Auth（Allow storage account key access = Disabled）
- 需要 **Storage Blob Data Contributor** RBAC 角色才能读写 Blob
- 安装新依赖：`azure-identity`

---

## 已修复的问题

### Bug: pain_points / strengths / recommendations 全为空
- **根因**：Step 5 LLM 返回 JSON 解析失败，fallback 静默返回空
- **修复**：analyze.py 已修复解析逻辑；对已有报告用 fix_display.py 从 markdown 提取

### Bug: 同产品多社区重复出现
- **根因**：analyze.py 按 subreddit 逐个分析，未合并同 product_name 条目
- **修复**：analyze.py 增加产品合并逻辑

### Bug: analyze.py resume 后产品摘要不完整
- **根因**：Steps 4/5 只使用当前 session cache，不含之前已完成的社区数据
- **修复方案**：运行 fix_reports.py 从 DB 重新合并并生成摘要

### Frontend: index.json 缺少 total_valid_posts
- 前端 `reportIndex.filter(r => r.total_valid_posts !== 0)` 通过了（undefined !== 0），但显示 "0 帖子"
- **修复**：重建 index.json，计算并填入 total_valid_posts

### Frontend: 跨产品对比表全显示 "-"
- markdown 使用 `**优势**：`（粗体格式），前端正则 `^-\s+优势[：:]` 匹配不到
- **修复**：正则加入 `\*{0,2}` 匹配可选粗体标记，同时处理英文复数形式

### Frontend: Evidence Wall 英文版缺一个卡
- 语言提示 `<div>` 占用了 grid cell
- **修复**：添加 `grid-column:1/-1` 让它跨满整行

### Frontend: 卡片内容错位
- 各卡片 subreddit 数量不同导致百分比、关键词等区域垂直不对齐
- **修复**：使用 CSS Subgrid 让所有卡片共享行轨道

---

## Key Learnings

- **Arctic Shift > RSS**：Arctic Shift 提供真实 upvote scores，且可回溯任意时间段。RSS 评论 score 始终为 0。
- **Arctic Shift API 须用 curl**：API 屏蔽了 Python urllib 的 User-Agent，backfill_arctic.py 用 curl 子进程绕过。
- **Reddit JSON API 被封**：企业网络完全封锁 Reddit 官方 JSON API，只能用 Arctic Shift 和 RSS。
- **不要跳过评论**：评论包含最有价值的情感数据和用户反馈。
- **多会话冲突**：同时运行多个分析进程会导致 SQLite 锁冲突，进程互相 kill。
- **EXCLUDED_PRODUCTS**：analyze.py 第 685 行排除 GitHub Copilot，已确认不需要分析。
- **前端 index.json**：只保留正式报告，清理中间文件；必须包含 total_valid_posts 字段。

---

## 常用命令

```powershell
# 进入脚本目录
cd "c:/Users/dorisrao/vibeprojects/lab/projects/Reddit-analysis/scripts"

# 回填数据（Arctic Shift）
$env:PYTHONIOENCODING="utf-8"; python backfill_arctic.py --start-date 2026-04-01 --end-date 2026-04-14

# 分析（取每子版前200帖）
$env:PYTHONIOENCODING="utf-8"; python analyze.py --days 14 --max-posts-per-sub 200

# 翻译
$env:PYTHONIOENCODING="utf-8"; python translate.py

# 部署到 GitHub Pages + 上传数据到 Azure Blob
$env:PYTHONIOENCODING="utf-8"; python deploy.py

# 仅上传数据到 Azure Blob
$env:PYTHONIOENCODING="utf-8"; python deploy.py --upload-only

# 从 Azure Blob 下载数据到本地（另一台电脑）
$env:PYTHONIOENCODING="utf-8"; python download_data.py

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

---

## 未来计划

- 每周一固定运行：backfill_arctic → analyze → translate → deploy
- analyze.py 增量机制：`post_analysis` 加 `(post_id, model)` 唯一约束，跳过已分析帖
- 报告聚合从 DB 全局查询，不依赖单次 run 的数据

---

## P5 更新（2026-05-11）

### Bug 修复：analyze.py 空子版块导致多子版块产品报告缺失

**问题**：当产品包含多个子版块（如 M365 Copilot = microsoft_365_copilot + MicrosoftCopilot），某个子版块在分析期间内帖子为 0 时，该子版块被 `continue` 跳过但未加入 `done_subs`。后续产品完成检查发现"仍有未完成子版块"，永远不会触发 Step 4/5 汇总。

**修复**：空子版块现在会 (1) 加入 `done_subs`，(2) 检查是否完成了该产品的所有子版块，如果是则触发 Step 4/5。

### 新增：竞品动态带真实 Reddit 数据源

之前的 `competitor_updates` 没有 `sources`（Reddit 帖子链接）和 `stats`（情感统计）。新增 `_gen_competitor_updates_v2.py`：

1. 按新闻事件关键词在 DB 中搜索相关帖子
2. 提取每个事件的帖子数、评论数、情感分布
3. 选取 top 4 高分帖作为 `sources`（含 id、title、score、url）
4. 生成 `competitor_updates` 数组和 `exec_news_summary`

### 新增：LLM 端点回退

`localhost:4141`（Copilot API）不可用时，自动检查 `EGRESS_LLM_API_ENDPOINT` 环境变量（`http://127.0.0.1:41891`），使用 `claude-sonnet-4.6` 模型。

### index.json 清理

去除重复/测试报告（8产品测试报告、同一日期多个版本），从 15 条精简至 7 条。
- 预计每周运行时间：30~45 分钟
