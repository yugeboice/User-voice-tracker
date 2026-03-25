# Reddit Competitive Intelligence - Technical Architecture

## 1. System Architecture Overview

```
                    ┌─────────────────────────────────────────────────────────┐
                    │                   User Browser                          │
                    │              http://localhost:8407                       │
                    │   ┌─────────────────────────────────────────────────┐   │
                    │   │           wwwroot/index.html                     │   │
                    │   │  ┌──────────┬───────────┬──────────┬─────────┐  │   │
                    │   │  │ Overview │ Per-Product│  Charts  │ History │  │   │
                    │   │  │   Tab    │    Tab     │   Tab    │   Tab   │  │   │
                    │   │  └──────────┴───────────┴──────────┴─────────┘  │   │
                    │   └────────────────────┬────────────────────────────┘   │
                    └────────────────────────┼────────────────────────────────┘
                                             │ REST API
                    ┌────────────────────────▼────────────────────────────────┐
                    │                .NET 8 Minimal API                       │
                    │                  Program.cs                             │
                    │                                                         │
                    │  ┌─────────────────────────────────────────────────┐    │
                    │  │              API Routes                          │    │
                    │  │  POST /api/reddit/scrape                        │    │
                    │  │  POST /api/reddit/analyze                       │    │
                    │  │  GET  /api/reddit/reports                       │    │
                    │  │  GET  /api/reddit/reports/{id}                  │    │
                    │  │  GET  /api/reddit/posts?product=X&period=Y     │    │
                    │  │  GET  /api/reddit/trends                       │    │
                    │  │  POST /api/reddit/screenshot                   │    │
                    │  └──────────┬──────────────────────────────────────┘    │
                    │             │                                            │
                    │  ┌──────────▼──────────────────────────────────────┐    │
                    │  │           Services Layer                         │    │
                    │  │                                                  │    │
                    │  │  ┌──────────────┐  ┌───────────────────────┐    │    │
                    │  │  │ RedditScraper│  │   RedditAnalyzer      │    │    │
                    │  │  │   .cs        │  │      .cs              │    │    │
                    │  │  │              │  │                       │    │    │
                    │  │  │ Orchestrates │  │ Filter → Classify →   │    │    │
                    │  │  │ Python       │  │ Sentiment → Summarize │    │    │
                    │  │  │ scraper      │  │                       │    │    │
                    │  │  └──────┬───────┘  └───────────┬───────────┘    │    │
                    │  │         │                       │                │    │
                    │  │  ┌──────▼───────┐  ┌───────────▼───────────┐    │    │
                    │  │  │ ScraperBridge│  │  ReportGenerator      │    │    │
                    │  │  │   .cs        │  │     .cs               │    │    │
                    │  │  │              │  │                       │    │    │
                    │  │  │ Process.Start│  │ Generates structured  │    │    │
                    │  │  │ → Python     │  │ HTML/JSON reports     │    │    │
                    │  │  └──────┬───────┘  └───────────┬───────────┘    │    │
                    │  │         │                       │                │    │
                    │  └─────────┼───────────────────────┼────────────────┘    │
                    │            │                       │                     │
                    │  ┌─────────▼───────────────────────▼────────────────┐    │
                    │  │              Data Layer                           │    │
                    │  │         data/reddit.db (SQLite)                   │    │
                    │  └──────────────────────────────────────────────────┘    │
                    └─────────────────────────────────────────────────────────┘
                                  │                    │
           ┌──────────────────────▼──┐    ┌────────────▼───────────────┐
           │   Python Scraper Layer  │    │   External Services        │
           │                         │    │                            │
           │ scripts/                │    │ ┌────────────────────────┐ │
           │ ├─ scrape.py            │    │ │ Copilot API (LLM)     │ │
           │ ├─ analyze.py           │    │ │ http://localhost:4141  │ │
           │ └─ vendor/              │    │ └────────────────────────┘ │
           │    └─ reddit-universal- │    │ ┌────────────────────────┐ │
           │       scraper/          │    │ │ Lumina Search API      │ │
           │                         │    │ │ (supplementary search) │ │
           │ Uses: requests,         │    │ └────────────────────────┘ │
           │ SQLite, no Reddit API   │    │ ┌────────────────────────┐ │
           └─────────────────────────┘    │ │ Lumina CUA             │ │
                                          │ │ (Reddit screenshots)   │ │
                                          │ └────────────────────────┘ │
                                          └────────────────────────────┘
```

## 2. Project Structure

```
lab/projects/Reddit-analysis/
│
├── PLAN.md                          # Project plan & verification checklist
├── ARCHITECTURE.md                  # This file
│
├── Program.cs                       # .NET 8 entry point, API routes (Phase 2)
├── MinimalApiCall.csproj            # Project file (Phase 2)
├── nuget.config                     # NuGet feeds (Phase 2)
│
├── Services/                        # .NET service layer (Phase 2)
│   ├── RedditScraper.cs             # Orchestrates Python scraper
│   ├── RedditAnalyzer.cs            # LLM analysis pipeline
│   ├── ReportGenerator.cs           # Structured report generation
│   ├── ScraperBridge.cs             # .NET ↔ Python process bridge
│   └── TrendAnalyzer.cs             # Cross-period trend analysis (Phase 3)
│
├── Internal/                        # Shared infrastructure (Phase 2)
│   ├── PartnerContextConfiguration.cs  # Copied from ArenaWatch
│   └── TokenService.cs                # Copied from ArenaWatch
│
├── scripts/                         # Python scraper layer (Phase 1)
│   ├── scrape.py                    # Batch scraper wrapper
│   ├── analyze.py                   # LLM analysis script
│   ├── requirements.txt             # Python dependencies
│   └── vendor/                      # Third-party scraper
│       └── reddit-universal-scraper/  # git clone target
│
├── data/                            # Persistent data storage
│   ├── reddit.db                    # SQLite database (all data)
│   ├── reports/                     # Generated report JSONs
│   └── screenshots/                 # CUA screenshots of Reddit pages
│
├── wwwroot/                         # Frontend (static files)
│   └── index.html                   # Single-page dashboard
│
├── docs/                            # Documentation
│   └── screenshots/                 # README screenshots
│
├── SearchApi.cs                     # Lumina Search API (copied from ArenaWatch)
├── OpenApi.cs                       # Lumina Open API (copied from ArenaWatch)
├── FindApi.cs                       # Lumina Find API (copied from ArenaWatch)
├── CuaApi.cs                        # Lumina CUA API (copied from ArenaWatch)
└── LlmExample.cs                    # LLM integration (copied from ArenaWatch)
```

## 3. Data Model

### SQLite Schema (`data/reddit.db`)

```sql
-- ============================================================
-- Core data tables (populated by scraper)
-- ============================================================

CREATE TABLE subreddits (
    id              TEXT PRIMARY KEY,    -- e.g., "ChatGPT"
    product_name    TEXT NOT NULL,       -- e.g., "ChatGPT"
    display_name    TEXT NOT NULL,       -- e.g., "r/ChatGPT"
    url             TEXT NOT NULL,       -- e.g., "https://reddit.com/r/ChatGPT"
    subscribers     INTEGER,
    last_scraped_at TEXT                 -- ISO 8601 timestamp
);

CREATE TABLE posts (
    id              TEXT PRIMARY KEY,    -- Reddit post ID (e.g., "abc123")
    subreddit_id    TEXT NOT NULL REFERENCES subreddits(id),
    title           TEXT NOT NULL,
    body            TEXT,                -- Self-text content (may be empty for link posts)
    author          TEXT,
    score           INTEGER DEFAULT 0,
    upvote_ratio    REAL,
    num_comments    INTEGER DEFAULT 0,
    url             TEXT NOT NULL,       -- Reddit permalink
    external_url    TEXT,                -- Linked URL (for link posts)
    created_utc     TEXT NOT NULL,       -- ISO 8601 timestamp
    scraped_at      TEXT NOT NULL,       -- When we scraped this
    flair           TEXT,                -- Post flair tag
    post_type       TEXT                 -- "self", "link", "image", "video"
);

CREATE TABLE comments (
    id              TEXT PRIMARY KEY,    -- Reddit comment ID
    post_id         TEXT NOT NULL REFERENCES posts(id),
    parent_id       TEXT,                -- Parent comment ID (NULL for top-level)
    author          TEXT,
    body            TEXT NOT NULL,
    score           INTEGER DEFAULT 0,
    created_utc     TEXT NOT NULL,
    scraped_at      TEXT NOT NULL,
    depth           INTEGER DEFAULT 0    -- Nesting depth (0 = top-level)
);

-- ============================================================
-- Analysis tables (populated by LLM analysis pipeline)
-- ============================================================

CREATE TABLE analysis_runs (
    id              TEXT PRIMARY KEY,    -- UUID
    started_at      TEXT NOT NULL,
    completed_at    TEXT,
    period_start    TEXT NOT NULL,       -- Analysis window start
    period_end      TEXT NOT NULL,       -- Analysis window end
    subreddits      TEXT NOT NULL,       -- JSON array of subreddit IDs
    status          TEXT DEFAULT 'running',  -- running, completed, failed
    total_posts     INTEGER DEFAULT 0,
    filtered_posts  INTEGER DEFAULT 0,  -- Posts removed as invalid
    config_json     TEXT                 -- Runtime config snapshot
);

CREATE TABLE post_analysis (
    id              TEXT PRIMARY KEY,    -- UUID
    run_id          TEXT NOT NULL REFERENCES analysis_runs(id),
    post_id         TEXT NOT NULL REFERENCES posts(id),
    is_valid        INTEGER DEFAULT 1,   -- 0 = filtered out (spam/meme/off-topic)
    filter_reason   TEXT,                -- Why it was filtered
    topic_category  TEXT,                -- See taxonomy below
    sentiment_score REAL,                -- -1.0 (very negative) to +1.0 (very positive)
    sentiment_label TEXT,                -- "positive", "negative", "neutral", "mixed"
    sentiment_reason TEXT,               -- Why this sentiment (LLM explanation)
    key_points      TEXT,                -- JSON array of key discussion points
    is_typical      INTEGER DEFAULT 0,   -- Selected as a "typical post" for report
    typical_reason  TEXT                 -- Why selected as typical
);

CREATE TABLE reports (
    id              TEXT PRIMARY KEY,    -- UUID
    run_id          TEXT NOT NULL REFERENCES analysis_runs(id),
    product_name    TEXT,                -- NULL for cross-product report
    report_type     TEXT NOT NULL,       -- "product_summary", "cross_product", "trend"
    title           TEXT NOT NULL,
    summary_text    TEXT NOT NULL,       -- Markdown summary
    report_json     TEXT NOT NULL,       -- Full structured report data
    chart_data_json TEXT,                -- Pre-computed chart data
    created_at      TEXT NOT NULL
);

-- ============================================================
-- Indexes
-- ============================================================

CREATE INDEX idx_posts_subreddit_date ON posts(subreddit_id, created_utc);
CREATE INDEX idx_posts_created ON posts(created_utc);
CREATE INDEX idx_comments_post ON comments(post_id);
CREATE INDEX idx_analysis_run ON post_analysis(run_id);
CREATE INDEX idx_analysis_post ON post_analysis(post_id);
CREATE INDEX idx_reports_run ON reports(run_id);
CREATE INDEX idx_reports_product ON reports(product_name);
```

### Topic Taxonomy

```
model_capability    - Discussions about model quality, accuracy, reasoning,
                      intelligence, factual correctness, hallucination
product_experience  - UX, app performance, speed, reliability, pricing,
                      subscription, availability, interface design
feature_request     - Desired new features, feature comparisons with competitors
bug_report          - Specific bugs, errors, failures, regressions, crashes
comparison          - Direct comparisons between AI products/models
use_case            - Specific use case discussions (coding, writing, research, etc.)
news_update         - Product announcements, version updates, company news
meta                - Subreddit meta discussions, community rules, moderation
```

## 4. Component Design

### 4.1 Python Scraper Layer (`scripts/`)

**Phase 1 primary execution path.** Wraps `reddit-universal-scraper` for batch operations.

#### `scripts/scrape.py`

```
Input:  --subreddits ChatGPT,ClaudeAI,Gemini,GithubCopilot,MicrosoftCopilot
        --days 14
        --limit 200
        --output-db ../data/reddit.db

Flow:
  1. For each subreddit:
     a. Call reddit-universal-scraper: python main.py {subreddit} --mode full --limit {limit}
     b. Read output from scraper's SQLite DB
     c. Filter posts by date (created_utc within --days window)
     d. Insert into our unified reddit.db
     e. Respect rate limits - 5s delay between subreddits
  2. Output summary: posts scraped, comments scraped, per subreddit

Output: data/reddit.db populated with fresh posts and comments
```

#### `scripts/analyze.py`

```
Input:  --db ../data/reddit.db
        --days 14
        --llm-endpoint http://localhost:4141
        --model gpt-4

Flow:
  1. Read all posts within time window from reddit.db
  2. For each product/subreddit batch:
     a. Send posts to LLM in batches of 20 for filtering:
        Prompt: "Classify each post as valid/invalid. Invalid = spam, meme with
                 no insight, meta/mod post, completely off-topic."
     b. Send valid posts to LLM for classification + sentiment:
        Prompt: "For each post, determine: topic_category, sentiment_score,
                 sentiment_label, sentiment_reason, key_points."
     c. Select typical posts (top by engagement * information_density)
  3. Generate per-product summary via LLM:
     Prompt: "Given these N analyzed posts from r/{subreddit} over the past
              2 weeks, generate a competitive intelligence summary covering:
              - Overall sentiment and trend
              - Top discussion topics
              - Key user pain points
              - Feature requests / praise
              - Notable comparisons with other products"
  4. Generate cross-product comparison via LLM
  5. Write all results to analysis tables in reddit.db

Output: data/reddit.db updated with analysis results
        data/reports/report_{timestamp}.json
```

### 4.2 .NET Service Layer (Phase 2)

#### `Services/RedditScraper.cs`

Orchestrates the Python scraper from .NET via `Process.Start()`.

```csharp
public class RedditScraper
{
    // Key methods:
    Task<ScrapeResult> ScrapeAsync(List<string> subreddits, int days, int limit);
    Task<ScrapeStatus> GetStatusAsync(string jobId);

    // Implementation:
    // - Launches Python process: python scripts/scrape.py --subreddits X --days Y
    // - Captures stdout for progress reporting
    // - Returns structured result with stats
}
```

#### `Services/RedditAnalyzer.cs`

LLM-powered analysis pipeline. Mirrors ArenaWatch's `ArenaService.cs` pattern.

```csharp
public class RedditAnalyzer
{
    // Key methods:
    Task<AnalysisRun> AnalyzeAsync(string runId, List<string> subreddits,
                                    DateTime periodStart, DateTime periodEnd);

    // Internal pipeline:
    Task<List<PostAnalysis>> FilterPostsAsync(List<Post> posts);
    Task<List<PostAnalysis>> ClassifyPostsAsync(List<Post> validPosts);
    Task<List<PostAnalysis>> AnalyzeSentimentAsync(List<PostAnalysis> classified);
    Task<string> GenerateProductSummaryAsync(string product, List<PostAnalysis> posts);
    Task<string> GenerateCrossProductComparisonAsync(Dictionary<string, List<PostAnalysis>> allProducts);
    Task<List<Post>> SelectTypicalPostsAsync(List<PostAnalysis> posts, int count = 5);
}
```

#### `Services/ReportGenerator.cs`

Produces structured reports. Mirrors ArenaWatch's `ArenaMonitor.cs` pattern.

```csharp
public class ReportGenerator
{
    // Generates the final structured report JSON for frontend consumption
    Task<Report> GenerateReportAsync(AnalysisRun run);

    // Report JSON structure:
    // {
    //   overview: { totalPosts, filteredCount, dateRange, products[] },
    //   crossProductComparison: { summary, sentimentComparison, topicComparison },
    //   products: [{
    //     name, subreddit, postCount,
    //     summary, sentimentDistribution, topTopics,
    //     typicalPosts: [{ title, url, score, sentiment, keyPoints }],
    //     keyInsights: []
    //   }],
    //   chartData: { sentimentByProduct, topicsByProduct, timelineSentiment }
    // }
}
```

### 4.3 Frontend (`wwwroot/index.html`)

Single-page application following ArenaWatch's pattern (vanilla HTML/CSS/JS, no framework).

#### Page Layout

```
┌─────────────────────────────────────────────────────────────────────┐
│  Reddit Competitive Intelligence Dashboard                    [⚙]  │
│  Period: 2026-02-24 ~ 2026-03-10  │  [Refresh] [Custom Range]     │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  ┌──────────┬────────────┬──────────┬──────────┬──────────────┐    │
│  │ Overview │  ChatGPT   │  Gemini  │  Claude  │  Copilot ... │    │
│  └──────────┴────────────┴──────────┴──────────┴──────────────┘    │
│                                                                     │
│  [Overview Tab]                                                     │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │  Executive Summary (LLM-generated cross-product comparison) │   │
│  │  "Over the past 2 weeks, ChatGPT users are most vocal      │   │
│  │   about... while Claude community focused on..."            │   │
│  └─────────────────────────────────────────────────────────────┘   │
│                                                                     │
│  ┌──────────────────────┐  ┌──────────────────────────────────┐   │
│  │ Sentiment by Product │  │ Top Topics by Product            │   │
│  │  [Bar Chart]         │  │  [Stacked Bar / Heatmap]         │   │
│  │  ChatGPT  ██████ 0.3│  │  ChatGPT: model>product>feature  │   │
│  │  Gemini   ████── 0.1│  │  Gemini:  product>model>use_case │   │
│  │  Claude   ████████0.5│  │  Claude:  model>comparison>feat  │   │
│  │  Copilot  ███───-0.1│  │  Copilot: bug>product>feature    │   │
│  │  M365     ████── 0.1│  │  M365:    product>use_case>bug   │   │
│  └──────────────────────┘  └──────────────────────────────────┘   │
│                                                                     │
│  [Per-Product Tab - e.g., ChatGPT]                                 │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │  Product Summary (LLM-generated)                            │   │
│  │  Sentiment: 0.3 (Slightly Positive)  │  Posts: 187          │   │
│  └─────────────────────────────────────────────────────────────┘   │
│                                                                     │
│  ┌──────────────────────┐  ┌──────────────────────────────────┐   │
│  │ Topic Distribution   │  │ Sentiment Breakdown              │   │
│  │  [Pie/Donut Chart]   │  │  [Horizontal Bar]                │   │
│  └──────────────────────┘  └──────────────────────────────────┘   │
│                                                                     │
│  Typical Posts                                                      │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │ ▲ 342  "GPT-4o is getting worse at coding tasks"            │   │
│  │        Sentiment: Negative | Topic: model_capability        │   │
│  │        Key: Users report regression in code generation...   │   │
│  │        [View on Reddit ↗]                                    │   │
│  ├─────────────────────────────────────────────────────────────┤   │
│  │ ▲ 256  "The new memory feature is amazing"                  │   │
│  │        Sentiment: Positive | Topic: feature_request         │   │
│  │        Key: Users praise persistent memory across sessions  │   │
│  │        [View on Reddit ↗]                                    │   │
│  └─────────────────────────────────────────────────────────────┘   │
│                                                                     │
│  [History Tab]                                                      │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │  Past Reports                                               │   │
│  │  📄 2026-03-10 (5 products, 847 posts) [View]              │   │
│  │  📄 2026-02-24 (5 products, 723 posts) [View]              │   │
│  │  📄 2026-02-10 (5 products, 691 posts) [View]              │   │
│  └─────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────┘
```

#### Chart Library

Use **Chart.js** via CDN (lightweight, no build step needed, same pattern as ArenaWatch using CDN libraries).

## 5. LLM Prompt Design

### 5.1 Content Filtering Prompt

```
You are a content quality filter for Reddit competitive intelligence analysis.

Given the following Reddit posts from r/{subreddit}, classify each as VALID or INVALID.

INVALID criteria:
- Spam or promotional content
- Memes/jokes with no substantive discussion
- Moderator/meta posts about subreddit rules
- Completely off-topic (not related to the AI product)
- Extremely short posts with no context (title-only with <5 words, no body)
- Duplicate/repost of another post in the batch

For each post, respond in JSON:
{"post_id": "xxx", "valid": true/false, "reason": "brief reason if invalid"}

Posts:
{posts_json}
```

### 5.2 Classification + Sentiment Prompt

```
You are an AI product competitive intelligence analyst.

Analyze each Reddit post from r/{subreddit} (product: {product_name}).

For each post, determine:
1. topic_category: One of [model_capability, product_experience, feature_request,
   bug_report, comparison, use_case, news_update, meta]
2. sentiment_score: Float from -1.0 (very negative) to 1.0 (very positive)
3. sentiment_label: "positive", "negative", "neutral", or "mixed"
4. sentiment_reason: One sentence explaining why (reference specific user concerns/praise)
5. key_points: Array of 1-3 key discussion points extracted from post + top comments

Respond in JSON array format:
[{
  "post_id": "xxx",
  "topic_category": "...",
  "sentiment_score": 0.0,
  "sentiment_label": "...",
  "sentiment_reason": "...",
  "key_points": ["...", "..."]
}]

Posts with comments:
{posts_with_comments_json}
```

### 5.3 Product Summary Prompt

```
You are a competitive intelligence analyst writing a bi-weekly Reddit community report.

Product: {product_name}
Subreddit: r/{subreddit}
Period: {period_start} to {period_end}
Total posts analyzed: {count} (after filtering {filtered_count} invalid posts)

Sentiment distribution:
- Positive: {pos_count} ({pos_pct}%)
- Negative: {neg_count} ({neg_pct}%)
- Neutral: {neu_count} ({neu_pct}%)
- Mixed: {mix_count} ({mix_pct}%)

Topic distribution:
{topic_distribution}

Top 10 highest-engagement analyzed posts:
{top_posts_json}

Write a competitive intelligence summary (in Chinese, 300-500 characters) covering:
1. Overall community sentiment and its drivers
2. Most discussed topics and why they matter
3. Key user pain points or complaints
4. Features users praise or request
5. Any notable comparisons with competitor products
6. Actionable insights for product team

Be specific - cite actual post topics and user concerns. Avoid generic statements.
```

### 5.4 Cross-Product Comparison Prompt

```
You are a senior competitive intelligence analyst.

Below are individual product summaries from the past 2 weeks across 5 AI product Reddit communities:

{per_product_summaries}

Write a cross-product competitive comparison (in Chinese, 500-800 characters) that:
1. Compares overall user satisfaction across products
2. Identifies which product communities are most/least happy and why
3. Highlights common pain points shared across products
4. Notes unique strengths or weaknesses per product
5. Identifies emerging competitive dynamics or shifting user preferences
6. Provides 3-5 actionable recommendations

Structure as a clear executive briefing.
```

## 6. API Routes (Phase 2)

| Method | Route | Description | Request | Response |
|--------|-------|-------------|---------|----------|
| POST | `/api/reddit/scrape` | Trigger scrape job | `{subreddits: [], days: 14, limit: 200}` | `{jobId, status}` |
| GET | `/api/reddit/scrape/{jobId}` | Get scrape job status | - | `{jobId, status, progress, stats}` |
| POST | `/api/reddit/analyze` | Trigger analysis | `{runId?, subreddits: [], periodStart, periodEnd}` | `{runId, status}` |
| GET | `/api/reddit/reports` | List all reports | `?limit=10` | `[{id, date, products, postCount}]` |
| GET | `/api/reddit/reports/{id}` | Get full report | - | Full report JSON (see ReportGenerator) |
| GET | `/api/reddit/posts` | Browse analyzed posts | `?product=X&topic=Y&sentiment=Z&page=1` | Paginated posts with analysis |
| GET | `/api/reddit/trends` | Get trend data | `?products=X,Y&periods=5` | Trend data across periods |
| POST | `/api/reddit/screenshot` | Capture Reddit page | `{subreddit: "ChatGPT"}` | `{screenshot: base64}` |
| GET | `/api/reddit/config` | Get current config | - | `{subreddits, schedule, defaults}` |

## 7. Integration with Lumina SDK

Following the same pattern as ArenaWatch:

| Lumina Capability | Usage in This Project |
|------------------|-----------------------|
| **Search API** | Supplementary web search for recent product news/announcements to enrich analysis |
| **CUA** | Capture screenshots of Reddit subreddit pages as visual evidence for reports |
| **LLM (Copilot API)** | Content filtering, topic classification, sentiment analysis, summary generation |
| **Open API** | (Optional) Open specific Reddit posts to extract full content if scraper misses detail |

The project reuses `SearchApi.cs`, `CuaApi.cs`, `OpenApi.cs`, `FindApi.cs`, and `LlmExample.cs` from ArenaWatch with minimal modification.

## 8. Deployment & Scheduling

### Phase 1 (MVP): Manual via CLI
```bash
cd scripts/
python scrape.py --subreddits ChatGPT,ClaudeAI,Gemini,GithubCopilot,MicrosoftCopilot --days 14
python analyze.py --db ../data/reddit.db --days 14
# Open wwwroot/report.html in browser
```

### Phase 2: Manual via Web UI
```bash
dotnet run
# Open http://localhost:8407
# Click [Scrape Now] → [Analyze] → view results
```

### Phase 3: Automated via Windows Task Scheduler / GitHub Actions
```
# Windows Task Scheduler (every 2 weeks)
schtasks /create /tn "RedditAnalysis" /tr "dotnet run --project ... -- --auto-run" /sc weekly /d MON /st 09:00:00 /ri 10080

# Or GitHub Actions (see .github/workflows/reddit-analysis.yml)
on:
  schedule:
    - cron: '0 9 */14 * *'  # Every 14 days at 9 AM UTC
```

## 9. Technology Stack Summary

| Layer | Technology | Rationale |
|-------|-----------|-----------|
| Backend | .NET 8 Minimal API | Consistent with ArenaWatch, direct Lumina SDK access |
| Scraper | Python + reddit-universal-scraper | Best available zero-auth Reddit scraper |
| LLM | Copilot API (localhost:4141) | Same as ArenaWatch, OpenAI-compatible |
| Database | SQLite | Zero deployment complexity, sufficient for this scale |
| Frontend | Vanilla HTML/CSS/JS + Chart.js | Same pattern as ArenaWatch, no build step |
| Screenshots | Lumina CUA | Visual proof of Reddit pages |
| Search | Lumina Search API | Supplement Reddit data with web news |
