# 📓 Notebook - Your AI-Powered Knowledge Hub

Notebook is a NotebookLM-inspired workspace that combines AI chat, source management, and intelligent content generation. Create organized knowledge bases, chat with your sources using AI, and generate beautiful infographics with automatic style intelligence.

## 🚀 Quick Start

1. **Start Services**: Ensure both egress-llm (port 4141) and MinimalApiCall (port 8400) are running
2. **Access the App**: Open [http://localhost:8400/notebook-home.html](http://localhost:8400/notebook-home.html)
3. **Create a Notebook**: Click "Create New Notebook", give it a name and description
4. **Add Sources**: Add text, URLs, or use web search to build your knowledge base
5. **Start Creating**: Chat with AI or generate mindmaps and infographics

## ✨ What Can You Do?

### 📚 Organize Your Knowledge
Create multiple notebooks for different topics - project research, learning materials, meeting notes, or anything you want to organize. Each notebook is an independent workspace with its own sources and chat history.

**How it works:**
- Create unlimited notebooks, each with a unique name and description
- Switch between notebooks from the home page
- All your work is automatically saved and persists between sessions

### 💬 Chat with AI
Have intelligent conversations powered by AI. Choose between two modes:

- **💭 General Chat**: Free-form conversation with AI on any topic
- **📖 Chat with Sources**: AI answers questions based on your notebook sources (RAG mode)

The AI remembers your conversation history and can reference previous messages, making it perfect for iterative discussions and deep dives into topics.

### 🎨 Generate Visual Content

#### Mindmaps
Transform your sources into structured mindmaps that visualize relationships and hierarchies. Perfect for brainstorming, summarizing complex topics, or planning projects.

#### Infographics with Intelligent Styling

Create stunning infographics using a **Python-based generation pipeline** that automatically adapts visual style based on your content!

**🔄 Generation Pipeline:**

```
┌─────────────────────────────────────────────────────────────┐
│  Step 1: Content Analysis (Claude LLM)                      │
│  → Analyzes notebook content                                │
│  → Generates structured infographic brief (markdown)        │
└─────────────────────────┬───────────────────────────────────┘
                          ▼
┌─────────────────────────────────────────────────────────────┐
│  Step 1b: Domain Detection (Keyword Matching)               │
│  → Scans brief for domain keywords                          │
│  → Calculates scores: priority × keyword_matches            │
│  → Anti-keywords can disqualify domains                     │
│  → Selects highest-scoring domain & variant                 │
└─────────────────────────┬───────────────────────────────────┘
                          ▼
┌─────────────────────────────────────────────────────────────┐
│  Step 2: Prompt Construction                                │
│  → Combines brief + domain-specific rendering instructions  │
│  → Adds text rendering and safety instructions              │
└─────────────────────────┬───────────────────────────────────┘
                          ▼
┌─────────────────────────────────────────────────────────────┐
│  Step 3: Image Generation (gpt-image-1-5)                   │
│  → Calls egress-llm /chatgpt/convo2im endpoint              │
│  → Receives SSE stream response                             │
└─────────────────────────┬───────────────────────────────────┘
                          ▼
┌─────────────────────────────────────────────────────────────┐
│  Step 4: Save PNG                                           │
│  → Parses base64 image from SSE stream                      │
│  → Saves to notebooks/Data/{id}/images/                     │
└─────────────────────────────────────────────────────────────┘
```

**🎯 8 Style Domains & Their Variants:**

| Domain | Description | Variants | Example Keywords |
|--------|-------------|----------|------------------|
| **Business** | Corporate, finance, enterprise | editorial, minimal-data, corporate | business, strategy, revenue, CEO, market |
| **History** | Historical events, heritage | vintage-timeline, documentary, archival | WWII, ancient, historical, era, century |
| **Science** | Scientific, research | laboratory, academic, discovery | research, experiment, data, hypothesis |
| **Nature & Space** | Natural world, cosmos | cosmic, wildlife, exploration | space, nature, planet, wildlife, outdoor |
| **Technology** | Tech, digital, innovation | blueprint, futuristic, developer | AI, code, software, innovation, digital |
| **Lifestyle** | Personal development, wellness | wellness, creative, productivity | health, fitness, mindfulness, balance |
| **Social** | Society, community, culture | community, cultural, impact | community, social, culture, audience |
| **Family** | Family, children, education | playful-learning, friendly-guide | kids, children, family, educational, playful |

**🧠 How Domain Detection Works:**

1. **Keyword Scanning**: The system scans your content brief for domain keywords
2. **Anti-keyword Filtering**: Domains with anti-keywords present are disqualified (e.g., "kids" disqualifies Business, Science, Technology)
3. **Score Calculation**: `score = domain_priority × matching_keywords_count`
4. **Variant Selection**: Within the winning domain, variant is chosen based on tone and specific keywords

### 🔍 Source Management
Build rich knowledge bases by adding diverse sources:

- **📝 Text Sources**: Paste any text content directly
- **🔗 URL Sources**: Add web pages - the system fetches and processes content automatically
- **🌐 Web Search**: Enter search terms and add relevant web results instantly
- **🗑️ Delete Sources**: Remove sources you no longer need

All sources are stored with your notebook and used as context for AI chat (RAG mode) and content generation.

## 🎯 Use Cases

| Scenario | How to Use Notebook |
|----------|-------------------|
| **📖 Learning & Research** | Create a notebook per topic, add articles/papers as sources, chat with AI to understand concepts, generate mindmaps to visualize |
| **💼 Project Management** | Add project docs, meeting notes, and specifications, use chat to clarify requirements, generate infographics for presentations |
| **✍️ Content Creation** | Gather research sources, brainstorm with AI chat, generate mindmaps to organize ideas, create infographics for social media |
| **👨‍🎓 Study Notes** | Add lecture notes and textbook excerpts, quiz yourself via chat, create summary mindmaps before exams |

## 💾 How Your Data is Stored

All notebooks, sources, chat history, and generated content are saved in the `notebooks/Data/` folder as JSON files:

```
Data/
├── index.json                    # List of all notebooks
└── {notebook-id}/                # Each notebook has its own folder
    ├── metadata.json             # Notebook name, description, dates
    ├── sources.json              # All your sources
    ├── chat-history.json         # Complete chat conversation
    ├── generations.json          # Mindmaps and infographics metadata
    └── images/                   # Generated images
        ├── {id}.png              # Generated infographic
        └── prompt_{id}.txt       # Prompt used for generation (for debugging)
```

**Your data is persistent**: Restart the server anytime - your notebooks will still be there!

## 🛠️ Technical Details

### Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    Browser (Web UI)                         │
│              notebook-home.html / notebook.html             │
└─────────────────────────────┬───────────────────────────────┘
                              │ HTTP
┌─────────────────────────────▼───────────────────────────────┐
│                C# Backend (port 8400)                       │
│   NotebookApi.cs  → Notebook CRUD operations                │
│   ChatApi.cs      → AI chat with RAG                        │
│   StudioApi.cs    → Calls Python skill for infographics     │
└──────────────┬──────────────────────────┬───────────────────┘
               │                          │
               │ (Chat/Mindmap)           │ (Infographic)
               │                          │ Process call
               ▼                          ▼
┌──────────────────────────┐  ┌───────────────────────────────┐
│    egress-llm (4141)     │  │  Python Skill                 │
│    /v1/messages          │  │  infographic-gen.py           │
│    (Claude LLM)          │  │  ├── content-analyzer.md      │
└──────────────────────────┘  │  ├── style-config.json        │
                              │  └── image-prompter.md        │
                              └──────────────┬────────────────┘
                                             │
                              ┌──────────────▼────────────────┐
                              │    egress-llm (4141)          │
                              │    /v1/messages (analysis)    │
                              │    /chatgpt/convo2im (image)  │
                              └───────────────────────────────┘
```

### Folder Structure
```
notebooks/
├── Backend/                      # C# API implementation
│   ├── Models/
│   │   └── Notebook.cs           # Data models
│   ├── NotebookApi.cs            # Notebook CRUD
│   ├── ChatApi.cs                # AI chat with RAG
│   ├── StudioApi.cs              # Calls Python skill for infographics
│   ├── ImageGenerationService.cs # Direct image generation (legacy)
│   └── NotebookStorage.cs        # JSON persistence
├── Frontend/                     # HTML/JavaScript UI
│   ├── notebook-home.html        # Notebook list
│   └── notebook.html             # Notebook workspace
└── Data/                         # Persisted data (auto-created)

skills/
└── infographic-gen/              # Python infographic generation skill
    ├── infographic-gen.py        # Main generation script (4 steps)
    ├── content-analyzer.md       # LLM system prompt for Step 1
    ├── image-prompter.md         # Domain-specific rendering instructions
    ├── style-config.json         # 8 domains, keywords, variants config
    └── generate_from_prompt.py   # Utility: regenerate from saved prompt
```

<details>
<summary><b>📡 API Reference</b></summary>

### Notebook Management
- `GET /api/notebooks` - List all notebooks
- `GET /api/notebooks/{id}` - Get specific notebook
- `POST /api/notebooks` - Create new notebook
- `PUT /api/notebooks/{id}` - Update notebook
- `DELETE /api/notebooks/{id}` - Delete notebook

### Sources
- `GET /api/notebook/sources?notebookId={id}` - Get sources
- `POST /api/notebook/sources/text` - Add text source
- `POST /api/notebook/sources/url` - Add URL source  
- `POST /api/notebook/sources/search` - Add search results
- `DELETE /api/notebook/sources/{id}?notebookId={id}` - Delete source

### Chat
- `POST /api/notebook/chat?notebookId={id}` - Send message (streaming)
- `GET /api/notebook/chat/history?notebookId={id}` - Get history
- `DELETE /api/notebook/chat/history?notebookId={id}` - Clear history

### Studio (Generation)
- `POST /api/notebook/studio/generate?notebookId={id}` - Generate content
- `GET /api/notebook/studio/generations?notebookId={id}` - List generations
- `GET /api/notebook/studio/generation/{id}?notebookId={id}` - Get specific generation
- `DELETE /api/notebook/studio/generation/{id}?notebookId={id}` - Delete generation

</details>

## 📝 Tips & Best Practices

- **🎨 Better Infographics**: The system automatically detects domain from your content - just write naturally! Keywords like "kids", "business", "history" will trigger appropriate styles
- **💬 Effective Chat**: Use "Chat with Sources" mode to get answers grounded in your sources - more accurate and contextual
- **📚 Organize Sources**: Add sources before generating content - more context = better output
- **🗂️ Multiple Notebooks**: Don't cram everything into one notebook - create separate notebooks for different projects or topics
- **🔄 Iterate**: Generate, review, refine your content, generate again - each generation is unique
- **🐛 Debug Prompts**: Check `images/prompt_{id}.txt` to see exactly what prompt was sent to the image generator

## 🆘 Troubleshooting

**Problem**: Can't access the app  
**Solution**: Make sure both services are running:
- egress-llm on port 4141: `Test-NetConnection localhost -Port 4141`
- MinimalApiCall on port 8400: `Test-NetConnection localhost -Port 8400`

**Problem**: Infographic generation fails with "403 Forbidden"  
**Solution**: Ensure egress-llm (not copilot-api) is running on port 4141. See `EgressGuide.md` for setup.

**Problem**: "No image data found in API response"  
**Solution**: The image API response format may have changed. Check egress-llm terminal logs for details.

**Problem**: Generated images don't appear  
**Solution**: Check `notebooks/Data/{notebook-id}/images/` folder. Check terminal for Python script errors.

**Problem**: Style selection seems off  
**Solution**: Check terminal output to see domain detection logs (which domain was selected and why). The system shows keyword matches and scores.

**Problem**: Chat or generation feels slow  
**Solution**: First generation may take longer as the AI warms up. Image generation typically takes 30-90 seconds.

---

## Access

Main entry point: [http://localhost:8400/notebook-home.html](http://localhost:8400/notebook-home.html)

Or from homepage: [http://localhost:8400/](http://localhost:8400/) → Click "进入 Notebook →"

---

**Last Updated**: 2026-01-09  
**Project**: Lumina-API-Demo  
**Branch**: user/tiantianguo/inforgraphic-gen-claude-skill
