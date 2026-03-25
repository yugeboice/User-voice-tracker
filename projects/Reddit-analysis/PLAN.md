# Reddit Competitive Intelligence - Project Plan

## 1. Project Overview

Build an automated competitive intelligence tool that scrapes Reddit communities of major AI competitors (ChatGPT, Gemini, Claude, Copilot, M365 Copilot), analyzes user discussions using LLM, and presents structured insights on a web dashboard.

### Target Reddit Communities

| Product | Subreddit | Estimated Activity |
|---------|-----------|-------------------|
| ChatGPT | r/ChatGPT | Very High (~3M members) |
| Gemini | r/Gemini | Medium |
| Claude | r/ClaudeAI | Medium-High |
| GitHub Copilot | r/GithubCopilot | Medium |
| M365 Copilot | r/MicrosoftCopilot | Medium |

### Core Requirements

1. **Data Collection**: Scrape latest posts and comments (within configurable time window, default 2 weeks) from target subreddits. All content must be real, verifiable, and timestamped.
2. **Intelligent Analysis**: Use LLM to analyze discussion topics, sentiment, and product/feature-specific feedback. Filter out spam, memes, and low-value content.
3. **Structured Presentation**: Single-page web dashboard showing overall conclusions, analysis charts, typical posts, with original content and Reddit links.
4. **Automation**: Bi-weekly auto-run on specified communities.
5. **Manual Trigger**: Support custom time ranges for on-demand analysis.
6. **Historical Storage**: Accumulate all data and reports for longitudinal review.

### Constraints

- No Reddit developer account / API credentials available
- Must use zero-auth scraping approach (reddit-universal-scraper / ScrapiReddit)
- Follow the same tech pattern as ArenaWatch (Lumina SDK + CUA + LLM + .NET 8 Minimal API + single-page frontend)

---

## 2. Three-Phase Solution Plan

### Phase 1: MVP - Validate Scraping + Basic Analysis

**Goal**: Prove that we can reliably scrape Reddit without API, get enough data, and produce useful LLM analysis.

**What we build**:
- Python `scripts/scrape.py`: Wraps `reddit-universal-scraper` to batch-scrape 5 subreddits
- Python `scripts/analyze.py`: Reads scraped SQLite data, calls LLM for analysis, outputs JSON
- Static HTML `wwwroot/index.html`: Simple report page reading the JSON output

**Verification Checklist** (must all pass before moving to Phase 2):

| # | Verification Item | How to Verify | Pass Criteria |
|---|-------------------|---------------|---------------|
| V1 | Scraping works without API key | Run `python main.py ChatGPT --mode full --limit 50` | Returns 40+ posts with titles, scores, timestamps, URLs |
| V2 | Comments are captured | Check scraped data for comment content | At least 60% of posts have associated comments |
| V3 | Data is within time window | Query SQLite: `SELECT * FROM posts WHERE created_utc > datetime('now', '-14 days')` | >80% of returned posts are within 2 weeks |
| V4 | All 5 subreddits work | Run scrape for each subreddit sequentially | All 5 complete without errors |
| V5 | Rate limiting is handled | Scrape 200+ posts from r/ChatGPT | No IP ban, completes with backoff |
| V6 | Data quality is sufficient | Manually review 20 posts from each subreddit | Posts have title, body, score, author, url, timestamp |
| V7 | LLM analysis produces useful output | Feed 50 posts to LLM with analysis prompt | Returns structured JSON with sentiment, topics, summary |
| V8 | Invalid content can be filtered | Review LLM-filtered results | Spam/meme/low-value posts are correctly identified |
| V9 | SQLite storage is persistent | Scrape, close, re-open SQLite | Data persists across runs |
| V10 | Scraping is repeatable | Run same scrape 24h apart | Second run shows new/updated content |

**Deliverables**:
- `scripts/scrape.py` - Batch scraper wrapper
- `scripts/analyze.py` - LLM analysis script
- `data/` - SQLite database + raw JSON outputs
- `wwwroot/report.html` - Static report page (manually refreshed)

---

### Phase 2: Enhanced Analysis - LLM Pipeline + Interactive Dashboard

**Goal**: Build the full .NET 8 backend (like ArenaWatch) with LLM-powered analysis pipeline and interactive web dashboard.

**What we build**:
- `Services/RedditScraper.cs` - Orchestrates Python scraper via Process, manages data ingestion
- `Services/RedditAnalyzer.cs` - LLM analysis pipeline (filter -> classify -> sentiment -> summarize)
- `Services/ReportGenerator.cs` - Generates structured reports, stores to SQLite
- `Program.cs` - API routes for dashboard
- `wwwroot/index.html` - Full interactive single-page dashboard

**New Capabilities**:
- LLM-powered content filtering (spam, memes, off-topic removal)
- Discussion topic classification taxonomy:
  - `model_capability` - Model quality, accuracy, reasoning
  - `product_experience` - UX, speed, reliability, pricing
  - `feature_request` - Desired new features
  - `bug_report` - Bugs, errors, failures
  - `comparison` - Cross-product comparisons
  - `use_case` - Specific use case discussions
- Deep sentiment analysis with attribution (why positive/negative)
- Cross-product comparative summary
- Typical post auto-selection (by engagement + information density)
- Interactive charts (sentiment distribution, topic breakdown, trend)
- Original post viewer with Reddit links
- Manual trigger with custom time range
- CUA integration for capturing Reddit page screenshots as visual proof

**Verification Checklist**:

| # | Verification Item | How to Verify | Pass Criteria |
|---|-------------------|---------------|---------------|
| V11 | .NET backend starts and serves API | `dotnet run`, open http://localhost:8407 | Dashboard loads |
| V12 | API trigger scrape works | POST /api/reddit/scrape `{subreddits: ["ChatGPT"], days: 14}` | Returns scrape job status |
| V13 | LLM filtering works | Review filtered vs unfiltered post counts | 10-30% of posts filtered as invalid |
| V14 | Topic classification is accurate | Manually check 50 classified posts | >75% correctly classified |
| V15 | Sentiment analysis is meaningful | Review sentiment scores with actual post content | Positive posts about good features scored positive, complaints scored negative |
| V16 | Cross-product comparison generated | Trigger analysis for all 5 products | Summary compares products on key dimensions |
| V17 | Dashboard shows all required views | Navigate through dashboard tabs | Overall view, per-product view, charts, typical posts, original text + links |
| V18 | Time range selector works | Select different date ranges in UI | Data filters correctly |
| V19 | Historical data persists | Run multiple scrape cycles, check old data | All past reports and raw data accessible |
| V20 | CUA screenshots captured | Trigger screenshot of subreddit pages | Screenshots appear in dashboard |

---

### Phase 3: Production - Automation + Trend Analysis + Export

**Goal**: Add scheduled automation, historical trend analysis, and team-sharing capabilities.

**What we build**:
- `Services/Scheduler.cs` - Automated bi-weekly scrape + analysis pipeline
- `Services/TrendAnalyzer.cs` - Cross-period trend analysis
- Export to PDF/email
- Deployment via Docker

**New Capabilities**:
- Bi-weekly auto-run (Windows Task Scheduler / GitHub Actions)
- Incremental scraping (only new posts since last run)
- Historical trend analysis (sentiment shift, emerging topics, recurring complaints)
- PDF export of reports
- Email/Teams notification on completion
- Docker containerization for deployment
- Full-text search across all historical posts

**Verification Checklist**:

| # | Verification Item | How to Verify | Pass Criteria |
|---|-------------------|---------------|---------------|
| V21 | Scheduler runs automatically | Set 1-hour test interval, wait for 2 cycles | Two reports generated without manual trigger |
| V22 | Incremental scrape works | Run twice in 1 day | Second run fetches only new posts, no duplicates |
| V23 | Trend analysis shows changes | Compare 3+ periods of data | Visible sentiment trends and topic evolution |
| V24 | PDF export works | Click "Export PDF" button | Clean formatted PDF downloads |
| V25 | Docker container runs | `docker build && docker run` | App accessible at configured port |
| V26 | Historical search works | Search for keyword across all periods | Returns matching posts across multiple scrape cycles |

---

## 3. Task Breakdown for Phase 1 (MVP)

### Step 1: Set up Python scraper environment
- [ ] Clone `reddit-universal-scraper` into `scripts/vendor/`
- [ ] Create Python virtualenv, install dependencies
- [ ] Test basic scrape: `python main.py ChatGPT --mode full --limit 20`
- [ ] Verify output in SQLite database

### Step 2: Build batch scraper wrapper
- [ ] Create `scripts/scrape.py` that scrapes all 5 subreddits
- [ ] Add time-window filtering (only keep posts within N days)
- [ ] Add structured JSON export of scraped data
- [ ] Test with all 5 subreddits, verify data completeness

### Step 3: Build LLM analysis script
- [ ] Create `scripts/analyze.py` that reads scraped data
- [ ] Design LLM prompt for content filtering + classification + sentiment
- [ ] Call local Copilot API (http://localhost:4141) for analysis
- [ ] Output structured analysis results as JSON
- [ ] Test with sample data, verify analysis quality

### Step 4: Build basic report page
- [ ] Create `wwwroot/report.html` - static single-page report
- [ ] Show: overall summary, per-product sentiment, topic distribution
- [ ] Show: typical posts with original text + Reddit links
- [ ] Test with real analysis output

### Step 5: End-to-end validation
- [ ] Run full pipeline: scrape -> analyze -> report
- [ ] Complete V1-V10 verification checklist
- [ ] Document any issues or limitations found
- [ ] Decision point: proceed to Phase 2 or adjust approach

---

## 4. Risk Register

| Risk | Impact | Mitigation |
|------|--------|------------|
| Reddit rate limits / IP ban | Scraping fails | Use delays (3-4s), rotate user-agent, limit batch size |
| Subreddit content too noisy | Low analysis quality | LLM filtering + manual review taxonomy |
| reddit-universal-scraper breaks | Cannot scrape | ScrapiReddit as fallback; Reddit RSS as emergency backup |
| LLM analysis hallucination | Wrong conclusions | Always link back to original posts; human review step |
| Data freshness | Stale insights | Timestamp validation; reject posts outside time window |
| Cost of LLM API calls | Budget concern | Batch posts, use efficient prompts, cache analysis results |

---

## 5. Success Metrics

After Phase 2 completion, the tool should enable:
- **Time to insight**: From trigger to complete report in under 30 minutes
- **Coverage**: 100+ posts analyzed per product per 2-week cycle
- **Accuracy**: >75% topic classification accuracy (spot-checked)
- **Actionability**: PM can identify top 3 user pain points per product from one report
- **Freshness**: All data verifiable as within specified time window via Reddit links
