# Lumina API Demo Lab

A collection of educational projects demonstrating various Lumina API use cases, built by team members as part of vibe coding practice sessions.

## Quick Start

```bash
git clone https://github.com/ai-microsoft/Lumina-API-Demo.git
cd Lumina-API-Demo
git checkout lab
```

Open `docs/index.html` in your browser to see the project landing page with links to each running project.

## Projects

| Project | Author | Tech Stack | Port | Description |
|---------|--------|-----------|------|-------------|
| [Competitor Trending News](./projects/competitor-trending-news/) | sunting | C#/.NET 8 | 8401 | Competitive intelligence tool: track competitor news and market trends |
| [AI Newsletter (AI Radar)](./projects/ai-newsletter/) | Doris | Node.js | — | Automated AI news aggregation from ChatGPT, Gemini, Genspark |

> More projects coming soon.

## How to Run

### C# Projects (.NET 8)

Each C# project runs on its own port so you can have multiple projects running simultaneously.

```bash
cd projects/competitor-trending-news
cp appsettings.Template.json appsettings.json
# Edit appsettings.json with your Azure AD credentials
dotnet run
# Open http://localhost:8401
```

### Node.js Projects

```bash
cd projects/ai-newsletter
npm install
# See project README for detailed usage
```

## Prerequisites

- .NET 8 SDK
- Node.js (for Copilot API proxy and Node.js projects)
- Azure AD credentials for Lumina API access

## How to Add a New Project

1. Create a new folder under `projects/` with a descriptive name
2. Include all necessary files for independent build and run
3. Assign a unique port (next available: 8403) in `Program.cs`
4. Add a `README.md` with setup and usage instructions
5. Update this README and `docs/index.html` with your project info
6. Submit a PR to the `lab` branch

## Branch Guide

| Branch | Description |
|--------|-------------|
| `main` | Full-featured web demo with UI, logging, all 4 APIs |
| `minimal-api-call` | Minimal Lumina + LLM integration starter template |
| `lab` (this branch) | Collection of team practice projects |
