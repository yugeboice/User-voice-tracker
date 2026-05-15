# Reddit Competitive Intelligence

Automated tool for scraping Reddit communities of AI competitors, analyzing discussions with LLM, and presenting structured insights on a bilingual (Chinese/English) web dashboard.

## Target Communities

| Product | Subreddits | Notes |
|---------|-----------|-------|
| ChatGPT | r/ChatGPT, r/ChatGPTcomplaints | OpenAI ChatGPT |
| Claude | r/ClaudeAI, r/claude | Anthropic Claude |
| Gemini | r/GeminiAI, r/GoogleGeminiAI | Google Gemini |
| Copilot | r/Copilot, r/CopilotMicrosoft, r/CopilotPro | Microsoft Copilot (consumer) |
| M365 Copilot | r/MicrosoftCopilot, r/microsoft_365_copilot | Microsoft 365 Copilot (enterprise) |
| GitHub Copilot | r/GithubCopilot | **Excluded** from analysis |

12 subreddits total, 5 active products analyzed (GitHub Copilot data is collected but excluded from reports).

## Biweekly Schedule

Reports are generated every 14 days with **date continuity** — each period starts the day after the previous period ended:

| Report | Period | Run ID |
|--------|--------|--------|
| P1 | 2026-03-02 ~ 2026-03-15 | `9d8bb56c` |
| P2 | 2026-03-16 ~ 2026-03-30 | `db2ee453` |
| P3 | 2026-03-30 ~ 2026-04-12 | `8e0776af` |
| P4 | 2026-04-13 ~ 2026-04-27 | `c2d17858` |
| P5 | 2026-04-28 ~ 2026-05-11 | `f0a678e1` |

**Date calculation rule**: `start = previous_end + 1 day`, `end = start + 13 days` (14 days total).

A scheduled task runs automatically every other Monday at 00:00 SGT (Asia/Singapore). To find the next period dates, read the latest `period_end` from `data/reports/index.json`.

## Pipeline Overview

The analysis pipeline runs in 6 stages:

```
┌──────────────┐    ┌──────────────┐    ┌──────────────┐    ┌──────────────┐    ┌──────────────┐    ┌──────────────┐
│  0. News      │───▶│  1. Scrape    │───▶│  2. Analyze   │───▶│  3. Translate │───▶│  4. Deploy   │───▶│  5. Notify   │
│  (search)     │    │  (data)       │    │  (LLM)       │    │  (LLM)       │    │  (gh-pages)  │    │  (Teams)     │
└──────────────┘    └──────────────┘    └──────────────┘    └──────────────┘    └──────────────┘    └──────────────┘
 Lumina Search       backfill_arctic.py   analyze.py         translate.py        deploy.py           notify.py
                     scrape.py (fallback)
```

### Stage 0: News Context Search (NEW)

Before each analysis cycle, search for industry news from the analysis period:

1. **Search** — Use Lumina Search (or web search) to find model releases, feature changes, pricing updates, and major controversies for all tracked products during the 14-day period
2. **Save** — Write structured news summary to `data/news_context_P{N}.md` (e.g., `news_context_P4.md` for the 4th report period)
3. **Associate** — After LLM analysis completes, cross-reference news events with Reddit discussion patterns. Search the DB for posts matching news keywords, extract sentiment stats and top posts as real Reddit sources.
4. **Competitor Updates** — Generate `competitor_updates` array in the report JSON with:
   - Per-event Reddit reaction summary with specific post/comment counts and sentiment percentages
   - Real Reddit post sources (id, title, score, comments, URL) for each news event
   - Stats object with classified sentiment breakdown
   - Bilingual title/body/key_points
5. **Executive News Summary** — Add `exec_news_summary` field (bilingual zh/en) highlighting the most important competitive dynamics with specific metrics from Reddit data

The news context file should cover:
- New model releases (e.g., GPT-5.5, Claude Opus 4.7)
- Feature launches or removals
- Pricing/plan changes (e.g., GitHub Copilot Pro signup pause)
- Major controversies or outages
- Key themes to watch in Reddit data

### Stage 1: Data Collection

Two scraping methods are available:

| Method | Script | Data Quality | History Depth | Speed |
|--------|--------|-------------|---------------|-------|
| **Arctic Shift** (preferred) | `backfill_arctic.py` | Real upvote scores for posts AND comments | Unlimited | ~2 sec/request |
| RSS (fallback) | `scrape.py` | No upvote scores (all score=0) | ~7 days for active subs | ~4-5 sec/request |

### Optimization: Parallel Crawl + Early Analysis

When crawling multiple products in parallel, some products finish much earlier than others (e.g., Copilot with ~130 posts finishes in minutes, while Claude with ~7,000 posts takes over an hour). To save time:

1. **Start `analyze.py` as soon as any product finishes crawling** — don't wait for all products to complete
2. `analyze.py` processes subreddits sequentially; products whose data is already in the DB will be analyzed immediately
3. For products still being crawled, their posts are already in the DB (comments may still be arriving) — analysis can proceed with available comments
4. If a subreddit has 0 posts when `analyze.py` reaches it (e.g., r/claude still being crawled by backfill), it will be skipped with a warning — use `--resume` to pick it up later

This optimization typically saves **1-2 hours** per cycle by overlapping crawling and analysis.

### Stage 2: LLM Analysis (`analyze.py`)

Six-step pipeline:
1. **Filter** — Remove invalid/off-topic posts
2. **Classify + Sentiment** — Topic classification and sentiment analysis per post
3. **Select Typical** — Pick representative posts per topic
4. **Product Summary** — Generate per-product analysis summaries (Chinese)
5. **Structured Insights** — Extract pain points, strengths, recommendations
6. **Cross-Product Comparison** — Compare all products across dimensions

### Stage 3: Translation (`translate.py`)

Translates Chinese analysis to English using LLM. Supports incremental checkpointing — can resume if interrupted.

### Stage 4+5: Deploy & Notify

Pushes dashboard to GitHub Pages and sends Teams/Telegram notification.

## Quick Start

### 1. Setup

```bash
cd scripts
python -m venv venv

# Install dependencies
venv/Scripts/pip install requests azure-storage-blob azure-identity
```

### 2. Collect Data (Arctic Shift — recommended)

[Arctic Shift](https://arctic-shift.photon-reddit.com) is a community project that archives Reddit data with real upvote scores, comment scores, and near-real-time freshness (~1 day lag).

```bash
# Backfill posts + top 5 comments per post for a date range
PYTHONIOENCODING=utf-8 venv/Scripts/python backfill_arctic.py \
    --start-date 2026-03-16 --end-date 2026-03-30

# Specify subreddits (default: all 12)
PYTHONIOENCODING=utf-8 venv/Scripts/python backfill_arctic.py \
    --start-date 2026-03-16 --end-date 2026-03-30 \
    --subreddits ChatGPT,ClaudeAI
```

**Arctic Shift API details:**
- Base URL: `https://arctic-shift.photon-reddit.com/api`
- Posts endpoint: `/posts/search?subreddit=X&after=TIMESTAMP&before=TIMESTAMP&limit=100&sort=asc`
- Comments endpoint: `/comments/search?link_id=POST_ID&limit=100&sort=desc&sort_type=score`
- **Important:** Blocks Python `urllib` User-Agent — must use `curl` subprocess
- Rate limiting: ~100 requests/minute, use 1-2 sec delay between requests
- Data freshness: ~1 day lag (today can see yesterday's posts)

### 2b. Collect Data (RSS — fallback)

```bash
# Scrape recent posts via RSS (limited history, no upvote scores)
PYTHONIOENCODING=utf-8 venv/Scripts/python scrape.py

# Customize
PYTHONIOENCODING=utf-8 venv/Scripts/python scrape.py --subreddits ChatGPT,ClaudeAI --days 7 --limit 50
```

### 3. Analyze with LLM

Requires a running LLM endpoint (Copilot API or OpenAI-compatible).

**LLM endpoint fallback**: If `localhost:4141` (Copilot API) is unavailable, check `EGRESS_LLM_API_ENDPOINT` env var (e.g., `http://127.0.0.1:41891`) which provides OpenAI-compatible access to Claude/GPT models. Pass `--llm-endpoint` and `--model` to `analyze.py` and `translate.py`.

```bash
# Start Copilot API
npx copilot-api@0.5.14 start

# Run analysis (top 200 posts per subreddit by upvote score)
PYTHONIOENCODING=utf-8 venv/Scripts/python analyze.py \
    --days 14 --max-posts-per-sub 200

# If analysis was interrupted and resumed, regenerate product summaries from DB:
PYTHONIOENCODING=utf-8 venv/Scripts/python fix_reports.py
```

### 4. Translate to English

```bash
# Translate the latest report
PYTHONIOENCODING=utf-8 venv/Scripts/python translate.py

# Translate a specific report
PYTHONIOENCODING=utf-8 venv/Scripts/python translate.py --file report_xxx.json
```

### 5. Deploy & View

```bash
# Deploy to GitHub Pages
PYTHONIOENCODING=utf-8 venv/Scripts/python deploy.py

# Or just start local dev server
PYTHONIOENCODING=utf-8 venv/Scripts/python serve.py
# Open http://localhost:8407
```

### One-Click Pipeline

```bash
# Full pipeline: scrape → analyze → translate → deploy → notify → serve
PYTHONIOENCODING=utf-8 venv/Scripts/python run.py

# Partial runs
PYTHONIOENCODING=utf-8 venv/Scripts/python run.py --scrape-only
PYTHONIOENCODING=utf-8 venv/Scripts/python run.py --analyze-only   # includes translate
PYTHONIOENCODING=utf-8 venv/Scripts/python run.py --serve-only
```

## Data Storage

All data is stored in `data/reddit.db` (SQLite):
- `posts` — Reddit posts with title, body, author, score, upvote_ratio, timestamp, URL
- `comments` — Post comments with body, author, score, timestamp
- `post_analysis` — LLM analysis results (topic, sentiment, key points)
- `reports` — Generated analysis reports (JSON)

Report JSONs are saved to `data/reports/` and synced to `wwwroot/data/reports/` for the dashboard.

The `data/` directory is excluded from git. Use Azure Blob Storage to sync data across machines (see below).

## Cross-Machine Data Sync (Azure Blob Storage)

Scraped data is automatically uploaded to Azure Blob Storage after each full pipeline run.
Authentication uses **Azure AD (DefaultAzureCredential)** — no keys or connection strings needed.

### One-time Setup

1. Create an Azure Storage Account (LRS tier is sufficient)
2. Assign yourself the **Storage Blob Data Contributor** role:
   - Storage Account → **Access Control (IAM)** → **+ Add** → **Add role assignment**
   - Role: **Storage Blob Data Contributor** → Members: select your account → **Review + assign**
3. (Recommended) Disable key access for security:
   - Storage Account → **Configuration** → **Allow storage account key access** → **Disabled** → **Save**
4. Set the account URL in `scripts/share.config.json`:

```json
{
  "azure_blob": {
    "account_url": "https://<your-account>.blob.core.windows.net",
    "container_name": "reddit-analysis-data"
  }
}
```

5. Install the Azure SDKs:

```bash
venv/Scripts/pip install azure-storage-blob azure-identity
```

6. Make sure you are logged into Azure on this machine (any one of these):
   - **Azure CLI**: `az login`
   - **VS Code**: Sign in with Azure Account extension
   - **Environment variable**: Set `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET` (for service principal)

### Upload / Download

```bash
# Upload: this machine → Azure
venv/Scripts/python deploy.py --upload-only

# Download: Azure → another machine
venv/Scripts/python download_data.py
```

## Project Structure

```
Reddit-analysis/
├── PLAN.md                  # Project plan & verification checklist
├── ARCHITECTURE.md          # Technical architecture docs
├── README.md                # This file
├── .gitignore
├── scripts/
│   ├── backfill_arctic.py       # Arctic Shift historical data scraper (preferred)
│   ├── scrape.py                # Reddit RSS scraper (fallback)
│   ├── analyze.py               # LLM analysis pipeline (6-step)
│   ├── translate.py             # Bilingual translation with checkpointing
│   ├── fix_reports.py           # Regenerate product summaries from DB (resume bug fix)
│   ├── fix_display.py           # Extract structured data from markdown summaries
│   ├── serve.py                 # Dashboard dev server (port 8407)
│   ├── run.py                   # One-click pipeline orchestrator
│   ├── deploy.py                # Deploy to GitHub Pages + Azure Blob upload
│   ├── download_data.py         # Download data from Azure Blob
│   ├── notify.py                # Teams/Telegram notifications
│   ├── lumina_client.py         # Lumina Search API wrapper
│   ├── inject_en.py             # Inject English translations into reports
│   ├── _gen_exec_summary.py     # Generate executive_summary from news context
│   ├── _gen_competitor_updates_v2.py  # Generate competitor_updates with real Reddit sources
│   ├── share.config.json        # Azure Blob connection string (not committed)
│   ├── requirements.txt
│   └── venv/                    # Python virtual environment (not committed)
├── data/                    # Runtime data (gitignored)
│   ├── reddit.db                # SQLite database
│   ├── news_context_P{N}.md     # News context per analysis period (Stage 0 output)
│   └── reports/                 # Report JSONs
└── wwwroot/                 # Dashboard static site
    ├── index.html               # Dashboard SPA (bilingual zh/en)
    └── data/reports/            # Report JSONs served to frontend
```

## Known Issues & Lessons Learned

1. **analyze.py resume bug** (FIXED): When analysis resumes after interruption, product summaries (Steps 4/5) only use data from the current session's cache, not from previously completed subreddits. Products spanning multiple subreddits (e.g., Gemini = GeminiAI + GoogleGeminiAI) get incomplete reports. **Workaround**: Run `fix_reports.py` to regenerate from DB data.

2. **analyze.py empty subreddit bug** (FIXED P5): When a subreddit has 0 posts in the date range, it was skipped without being marked as "done". This prevented multi-subreddit products (e.g., M365 Copilot = microsoft_365_copilot + MicrosoftCopilot) from completing Step 4/5 if any subreddit was empty. Fix: empty subreddits are now marked done, and product completion is checked after skip.

2. **Multi-session conflicts**: Running multiple Eureka/analysis sessions simultaneously causes SQLite lock conflicts and processes may kill each other. Always check for running instances before starting a new analysis.

3. **Arctic Shift User-Agent blocking**: The API blocks Python `urllib` default User-Agent. `backfill_arctic.py` uses `curl` subprocess as a workaround.

4. **Reddit JSON API blocked**: Reddit's official JSON API is fully blocked from the corporate network. Arctic Shift and RSS are the only viable data sources.

5. **Comments are critical**: Never skip comment scraping — comments contain the most valuable sentiment data and user feedback. RSS comment scores are always 0; only Arctic Shift provides real comment scores.

## Rate Limiting Notes

**Arctic Shift API:** No official rate limit, but be respectful — use 1-2 second delays between requests.

**Reddit RSS feeds:** Rate limits (~100 requests/minute). The scraper includes:
- 4-second delay between post page requests
- 5-second delay between comment fetches
- 60-second cooldown between subreddits
- Exponential backoff on 429 responses
