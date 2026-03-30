# Reddit Competitive Intelligence

Automated tool for scraping Reddit communities of AI competitors, analyzing discussions with LLM, and presenting structured insights on a web dashboard.

## Target Communities

| Product | Subreddit | Description |
|---------|-----------|-------------|
| ChatGPT | r/ChatGPT | OpenAI ChatGPT community |
| Claude | r/ClaudeAI | Anthropic Claude community |
| Gemini | r/Gemini | Google Gemini community |
| GitHub Copilot | r/GithubCopilot | GitHub Copilot community |
| M365 Copilot | r/MicrosoftCopilot | Microsoft Copilot community |

## Quick Start

### 1. Setup (first time only)

```bash
cd scripts

# Create virtual environment
python -m venv venv

# Install dependencies
venv/Scripts/pip install requests
```

### 2. Scrape Reddit data

```bash
# Scrape all 5 subreddits (last 14 days, up to 100 posts each)
PYTHONIOENCODING=utf-8 venv/Scripts/python scrape.py

# Customize
PYTHONIOENCODING=utf-8 venv/Scripts/python scrape.py --subreddits ChatGPT,ClaudeAI --days 7 --limit 50

# Skip comments (faster)
PYTHONIOENCODING=utf-8 venv/Scripts/python scrape.py --skip-comments
```

### 3. Analyze with LLM

Requires a running LLM endpoint (Copilot API or OpenAI-compatible).

```bash
# Start Copilot API first
npx copilot-api@0.5.14 start

# Run analysis
PYTHONIOENCODING=utf-8 venv/Scripts/python analyze.py --days 14
```

### 4. View Dashboard

```bash
# Start dev server
PYTHONIOENCODING=utf-8 venv/Scripts/python serve.py

# Open http://localhost:8407
```

Or use the one-click pipeline:

```bash
PYTHONIOENCODING=utf-8 venv/Scripts/python run.py
```

## How It Works

1. **Scrape** - Fetches posts and comments via Reddit RSS feeds (no API key required)
2. **Analyze** - LLM pipeline: filter invalid content -> classify topics -> analyze sentiment -> generate summaries
3. **Present** - Web dashboard with charts, per-product analysis, typical posts with Reddit links

## Data Storage

All data is stored in `data/reddit.db` (SQLite):
- `posts` - Reddit posts with title, body, author, timestamp, URL
- `comments` - Post comments with body, author, timestamp
- `post_analysis` - LLM analysis results (topic, sentiment, key points)
- `reports` - Generated analysis reports (JSON)

Report JSONs are also saved to `data/reports/` for historical reference.

The `data/` directory is excluded from git. Use Azure Blob Storage to sync data across machines (see below).

## Cross-Machine Data Sync (Azure Blob Storage)

Scraped data is automatically uploaded to Azure Blob Storage after each full pipeline run, so you can access it from any machine.

### One-time Setup

1. Create an Azure Storage Account (LRS tier is sufficient)
2. Go to **Storage Account → Security + networking → Access keys → Show keys**
3. Copy the **Connection string** from key1
4. Paste it into `scripts/share.config.json`:

```json
{
  "azure_blob": {
    "connection_string": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net",
    "container_name": "reddit-analysis-data"
  }
}
```

5. Install the Azure SDK:

```bash
venv/Scripts/pip install azure-storage-blob
```

### Upload Data (this machine → Azure)

Runs automatically as part of the full pipeline (`run.py`). To upload manually:

```bash
venv/Scripts/python deploy.py --upload-only
```

### Download Data (Azure → another machine)

On any other machine, after cloning the repo and filling in `share.config.json`:

```bash
venv/Scripts/python download_data.py
```

This pulls `reddit.db`, `reports/`, and `last_run.json` from Azure into the local `data/` directory.

### Typical workflow on a second machine

```bash
# 1. Pull latest data from Azure
venv/Scripts/python download_data.py

# 2. View dashboard directly (no re-scraping needed)
venv/Scripts/python run.py --serve-only

# 3. Or re-run analysis on existing data
venv/Scripts/python run.py --analyze-only
```

## Project Structure

```
Reddit-analysis/
├── PLAN.md              # Project plan & verification checklist
├── ARCHITECTURE.md      # Technical architecture docs
├── README.md            # This file
├── .gitignore
├── scripts/
│   ├── scrape.py            # Reddit RSS scraper
│   ├── analyze.py           # LLM analysis pipeline
│   ├── serve.py             # Dashboard dev server
│   ├── run.py               # One-click pipeline
│   ├── deploy.py            # Deploy to GitHub Pages + upload data to Azure Blob
│   ├── download_data.py     # Download data from Azure Blob to local data/
│   ├── share.config.json    # Azure Blob connection string (not committed)
│   ├── requirements.txt
│   └── vendor/              # Third-party tools (gitignored)
├── data/                # Runtime data (gitignored)
│   ├── reddit.db
│   └── reports/
└── wwwroot/
    └── index.html       # Dashboard SPA
```

## Rate Limiting Notes

Reddit RSS feeds have rate limits (~100 requests/minute). The scraper includes:
- 4-second delay between post page requests
- 5-second delay between comment fetches
- 60-second cooldown between subreddits
- Exponential backoff on 429 responses

For 5 subreddits x 100 posts with comments, expect ~50 minutes for a full scrape.
