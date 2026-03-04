# CLAUDE.md - Project Context for Claude Code

## Project Overview
**AI Radar (auto_ainewsletter)** - An automated AI intelligence system that aggregates daily AI news from multiple sources, generates summaries, and creates audio podcasts.

## Tech Stack
- **Runtime**: Node.js (CommonJS modules)
- **Browser Automation**: Playwright
- **Output Formats**: Markdown, HTML, Audio (via NotebookLM)

## Project Structure
```
├── run_daily.js          # Main entry point - orchestrates daily workflow
├── scrape_chatgpt_chrome.js  # Scrapes ChatGPT conversations
├── scrape_gemini_debug.js    # Scrapes Google Gemini
├── scrape_genspark.js        # Scrapes Genspark
├── merge_reports.js          # Merges reports from all sources
├── generate_summary.js       # Generates McKinsey-style HTML summary
├── upload_notebooklm.js      # Uploads to NotebookLM for audio generation
├── utils.js                  # Shared utilities
├── setup.js                  # Initial setup script
├── reports/                  # Generated reports (date-stamped)
├── chrome-data*/             # Chrome user data directories (persistent login)
├── logs/                     # Execution logs
└── auth/                     # Authentication data
```

## Key Commands
```bash
npm run daily      # Full pipeline: scrape + merge + summary + upload
npm run scrape     # Scrape only (no upload)
npm run upload     # Upload only (skip scraping)
npm run chatgpt    # Scrape ChatGPT only
npm run gemini     # Scrape Gemini only
npm run genspark   # Scrape Genspark only
npm run merge      # Merge existing reports
npm run summary    # Generate summary from combined report
npm run notebooklm # Upload to NotebookLM
```

## Coding Conventions
- Use `const` and `let`, avoid `var`
- Use async/await for asynchronous operations
- Error handling with try/catch blocks
- Console logging with emojis for visual feedback (✅ ❌ 🔄 📊)
- Date format: YYYY-MM-DD (e.g., 2026-02-05)
- Chinese comments are acceptable

## Important Notes
- Chrome data directories contain persistent login sessions - DO NOT commit
- Reports are date-stamped and stored in `reports/` directory
- The system uses headless browsers for scraping
- NotebookLM upload requires Google authentication

## File Naming Conventions
- Reports: `report-{source}-{date}.md` (e.g., `report-chatgpt-2026-02-05.md`)
- Combined: `combined-{date}.md`
- Summary: `summary-{date}.html` and `summary-{date}.md`

## When Making Changes
1. Test with individual scripts before running full pipeline
2. Check `logs/` for debugging information
3. Validate output in `reports/` directory
4. Screenshots saved to `img&record/` for debugging
