# Lumina API Demo Lab

A collection of educational projects demonstrating various Lumina API use cases, built by team members as part of vibe coding practice sessions.

## Getting Started

### Configuration

```bash
git clone https://github.com/ai-microsoft/Lumina-API-Demo.git
cd Lumina-API-Demo
cp appsettings.Template.json Launcher/appsettings.json
```

Edit `Launcher/appsettings.json` with your configuration (Tenant, ClientId, Partner context, etc.). All projects share this configuration through the Launcher.

### Run

```powershell
# Terminal 1: Start Copilot API
npx copilot-api@0.5.14 start

# Terminal 2: Start the Launcher (serves all projects)
cd Launcher
dotnet run
# Open http://localhost:8400 in your browser
```

## Projects

| Project | Lumina APIs | Description |
|---------|------------|-------------|
| [Arena Watch](./projects/arena-watch/) | Search, Open, Find, CUA | AI model ranking analysis platform with multi-leaderboard aggregation and smart comparison |
| [Competitor Trending News](./projects/competitor-trending-news/) | Search, Open, Find, CUA | Competitive intelligence tool: track competitor news and market trends |
| [Companion Chat](./projects/companion-chat/) | Search, Open, Find, CUA | Multi-feature AI companion: chat, PPT generation, TTS, agent system, local knowledge base |
| [Customer Service Agent](./projects/customer-service-agent/) | Search, Open, Find, CUA | Intelligent customer service assistant with knowledge base, multi-language support, and memory learning |
| [Infographic Gen](./projects/infographic-gen/) | Search, Open, Find, CUA | Infographic generation with 8 style domains and Python skill pipeline |
| [Notebook App](./projects/notebook-app/) | Search, Open, Find, CUA | NotebookLM-inspired workspace: AI chat, source management, mindmaps, and infographic generation |
| [AI Newsletter (AI Radar)](./projects/ai-newsletter/) | — | Automated AI news aggregation from ChatGPT, Gemini, Genspark |

## How to Add a New Project

1. Create a new folder under `projects/` with a descriptive name
2. Include all necessary files for independent build and run
3. Assign a unique port in `Program.cs`
4. Add a `README.md` with setup and usage instructions
5. Update this README with your project info
6. Submit a PR

## Branch Guide

| Branch | Description |
|--------|-------------|
| `main` | Full-featured web demo with UI, logging, all 4 APIs |
| `minimal-api-call` | Minimal Lumina + LLM integration starter template |
| `lab` (this branch) | Collection of team practice projects |
