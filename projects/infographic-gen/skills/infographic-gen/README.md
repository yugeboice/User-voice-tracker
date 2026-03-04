# Infographic Generator Skill

Generate professional educational infographics from text files with intelligent domain detection and style adaptation.

## Features

- 🎨 **27 Visual Styles** across 8 domains (business, science, lifestyle, nature-space, technology, social, family, history)
- 🧠 **Intelligent Domain Detection** automatically identifies content type and selects appropriate visual style
- 📐 **Professional Layouts** including flow, timeline, comparison, radial, hero-focus, and more
- 🌍 **English-Only Output** ensures consistency and readability
- 💾 **Transparency** saves both PNG output and complete prompt for review
- 🎯 **Educational Focus** optimized for learning and comprehension

## Quick Start

### Prerequisites

Set required environment variables:

```bash
# Linux/Mac
export LLM_ENDPOINT="http://localhost:4141"
export LLM_MODEL="claude-sonnet-4"

# Windows (PowerShell)
setx LLM_ENDPOINT "http://localhost:4141"
setx LLM_MODEL "claude-sonnet-4"
```

### How to Use This Skill

This skill can be invoked through Claude Code (or any compatible Claude Agent SDK environment):

#### Method 1: Through Claude Code CLI

Simply describe your request in natural language:

```bash
# Launch Claude Code
claude-code

# Then in the interactive session, use natural language:
> Generate an infographic from my research.txt file
> Create an infographic from notes.md and save in ./output/
> Make an infographic from astronomy.txt with cosmic-wonder style
```

#### Method 2: Invoke Skill Directly

If you're using the Claude Agent SDK, you can invoke the skill programmatically:

```javascript
// Using the Skill tool
await agent.invokeTool('Skill', {
  skill: 'infographic-gen',
  args: 'research.txt'
});
```

#### Method 3: Using Slash Command (if configured)

If your environment supports slash commands:

```bash
/infographic-gen research.txt
/infographic-gen notes.md --output ./infographics/
/infographic-gen astronomy.txt --style cosmic-wonder
```

### Usage Examples

#### Basic Usage
```
Generate an infographic from research.txt
```

#### With Output Directory
```
Create infographic from notes.md and save in ./output/
```

#### With Style Preference
```
Make an infographic from astronomy.txt with cosmic-wonder style
```

#### With Full Path
```
Generate infographic from /Users/me/Documents/science-paper.md
```

## Available Styles

### Business Domain (3 variants)
- **editorial**: Magazine-style with bold layouts and premium aesthetic
  - Best for: Corporate reports, professional insights, thought leadership
- **minimal-data**: Clean data visualization with maximum clarity
  - Best for: Analytics, metrics, data-driven content
- **corporate**: Professional report style with structured information
  - Best for: Annual reports, formal business documents

### Nature-Space Domain (3 variants)
- **cosmic-wonder**: Astronomy and space themes with deep space aesthetic
  - Best for: Stargazing guides, astronomy education, space exploration
- **nature-minimal**: Ecology and environment with clean, peaceful design
  - Best for: Conservation, sustainability, environmental topics
- **outdoor-adventure**: Hiking and exploration with dynamic energy
  - Best for: Trail guides, camping tips, outdoor activities

### Technology Domain (3 variants)
- **cyberpunk**: Futuristic dark mode with neon elements
  - Best for: Tech innovation, cybersecurity, futuristic concepts
- **notion-isometric**: Friendly productivity style with 2.5D elements
  - Best for: Productivity tools, workflows, software features
- **data-dashboard**: Analytics-focused with dark mode optimization
  - Best for: Data visualizations, metrics, KPIs

### Lifestyle Domain (3 variants)
- **zen-minimal**: Wellness and mindfulness with extreme simplicity
  - Best for: Meditation, mindfulness, calm wellness topics
- **vibrant-lifestyle**: Fitness and energy with bold colors
  - Best for: Workout guides, nutrition, active lifestyle
- **editorial-lifestyle**: Editorial illustrated style with warm aesthetics
  - Best for: Travel, activities, lifestyle guides

### Family Domain (4 variants)
- **playful-learning**: Kids activities with friendly characters
  - Best for: Science experiments, crafts, classroom projects
- **educational-story**: Narrative-driven learning for children
  - Best for: Story-based education, history for kids
- **illustrated-adventure**: Family-friendly exploration themes
  - Best for: Adventures, discovery, quests
- **friendly-guide**: Beginner-friendly how-to guides
  - Best for: Step-by-step tutorials, family activities

### Science Domain (3 variants)
- **academic-paper**: Scholarly style with formal presentation
  - Best for: Research summaries, academic content
- **lab-visual**: Laboratory and experimental aesthetic
  - Best for: Experiments, procedures, lab safety
- **illustrated-science**: Educational concept visualization
  - Best for: Science concepts, biology, chemistry

### History Domain (3 variants)
- **timeline-classic**: Vintage aesthetic with period typography
  - Best for: Historical timelines, archival content
- **documentary-style**: Cinematic historical presentation
  - Best for: War history, major events
- **cultural-heritage**: Museum-quality cultural content
  - Best for: Culture, traditions, artifacts

### Social Domain (3 variants)
- **pinterest-collage**: Social media inspiration style
  - Best for: Mood boards, aesthetic content
- **instagram-modern**: Mobile-first social media design
  - Best for: Social media posts, stories
- **influencer-bold**: Personal branding with bold aesthetics
  - Best for: Personal brand, viral content

## Output Files

For an input file named `astronomy.txt`, the skill generates:

1. **PNG Image**: `astronomy_20260104_143022.png`
   - High-quality infographic (typically 1920×1080 landscape)
   - Professionally styled with selected domain/variant

2. **Prompt File**: `astronomy_20260104_143022_prompt.txt`
   - Complete prompt used for generation
   - Useful for debugging and understanding style choices

## How It Works

### 1. Content Analysis
- Reads your text file
- Uses Claude to analyze content and generate detailed infographic description
- Identifies topic, key points, layout type, and target audience

### 2. Style Detection
- Automatically detects content domain based on keywords
- Selects optimal style variant within that domain
- Considers tone, topic, and content type

### 3. Prompt Construction
- Combines infographic description with domain-specific rendering instructions
- Adds general rules (landscape format, English-only, readability guidelines)
- Creates comprehensive prompt for image generation

### 4. Image Generation
- Calls GPT Image API with constructed prompt
- Generates professional infographic image
- Saves both image and prompt files

### 5. Display
- Shows image in terminal (if supported: iTerm2, kitty, VSCode)
- Provides summary with file locations
- Reports domain and variant used

## Configuration

### Environment Variables

| Variable | Description | Default Value |
|----------|-------------|---------------|
| `LLM_ENDPOINT` | Unified API endpoint for both LLM content analysis and image generation | `http://localhost:4141` |
| `LLM_MODEL` | LLM model name for content analysis | `claude-sonnet-4` |

**Note**: Both LLM content analysis and image generation use the same `LLM_ENDPOINT`. The egress-llm service routes requests to appropriate backends based on the API path.

### Configuration File (Optional)

You can also create a `config.json` file in your project root:

```json
{
  "llm": {
    "endpoint": "http://localhost:4141",
    "model": "claude-sonnet-4"
  }
}
```

**Note**: Image generation uses the same endpoint as LLM, so no separate configuration is needed.

## Troubleshooting

### "Cannot connect to image API"

**Problem**: Image generation API is unreachable

**Solutions**:
1. Check `LLM_ENDPOINT` environment variable is set correctly (default: `http://localhost:4141`)
2. Verify egress-llm service is running on port 4141
3. Test connection: `curl $LLM_ENDPOINT/health`
4. Check firewall settings
5. Ensure egress-llm service is configured to handle both LLM and image generation requests

### "File not found"

**Problem**: Input file path is incorrect

**Solutions**:
1. Use absolute path: `/full/path/to/file.txt`
2. Verify file exists: `ls /path/to/file.txt`
3. Check current directory: `pwd`
4. Ensure file has read permissions

### "Content too short"

**Problem**: Input file has insufficient content (< 100 characters)

**Solution**: Add more detailed content to the file. The more information provided, the better the infographic quality.

### Image quality issues

**Problem**: Generated image doesn't match expectations

**Solutions**:
1. Check saved prompt file to see exact instructions sent
2. Verify domain detection was correct (shown in output)
3. Try specifying style preference explicitly
4. Ensure input content is substantial and well-structured

### Rate limiting

**Problem**: API returns "Too many requests" error

**Solution**: The skill automatically retries up to 3 times with 60-second delays. If persistent, wait a few minutes before trying again.

## Technical Details

### Architecture
- **Input**: Text files (txt, md, or similar)
- **LLM**: Used for content analysis and description generation
- **Image Model**: gpt-image-1-5 via GPT Image API
- **Format**: PNG, landscape orientation (16:9), typically ~1920×1080px
- **Processing time**: 30-90 seconds depending on content complexity

### Domain Detection Algorithm
1. Load style-config.json with 8 domains
2. Check anti-keywords first (exclusion rules)
3. Match keywords using word-boundary regex
4. Select highest priority matching domain
5. Score variants within domain by keyword/tone matches
6. Select best variant

### Style System
- 8 domains: business, lifestyle, academic/tech, kids, history, science, nature-space, social
- 27 variants total with detailed rendering instructions
- Each variant specifies: color palette, typography, layout, visual elements, mood
- Comprehensive style rules (1000+ lines per major variant)

## Examples

### Example 1: Astronomy Content

**Input** (`astronomy_basics.txt`):
```
Introduction to Stargazing

Learn about constellations, planets, and using a telescope.
Best times for observation, essential equipment, and safety tips.
```

**Output**:
- Domain detected: `nature-space`
- Variant selected: `cosmic-wonder`
- Style: Deep space aesthetic with nebula colors, glowing elements
- Files: `astronomy_basics_20260104_143022.png` + prompt file

### Example 2: Business Strategy

**Input** (`quarterly_review.md`):
```
Q4 Business Performance

Revenue increased 40% vs Q3. Key metrics analysis.
Strategic initiatives for next quarter.
```

**Output**:
- Domain detected: `business`
- Variant selected: `minimal-data`
- Style: Clean data visualization with Swiss design principles
- Files: `quarterly_review_20260104_143515.png` + prompt file

### Example 3: Kids Science Activity

**Input** (`volcano_experiment.txt`):
```
Make a Volcano!

Easy science experiment for kids. Materials needed and step-by-step instructions.
```

**Output**:
- Domain detected: `family`
- Variant selected: `playful-learning`
- Style: Bright colors, friendly characters, large readable text
- Files: `volcano_experiment_20260104_144230.png` + prompt file

## Advanced Usage

### Override Style Detection

If automatic detection doesn't select your preferred style, you can specify it:

```
Generate infographic from business_report.txt with editorial style
```

This forces the use of `business-editorial` variant regardless of automatic detection.

### Batch Processing

Process multiple files by calling the skill multiple times:

```bash
for file in *.txt; do
    echo "Generate infographic from $file"
done
```

### Custom Output Directory

Organize outputs by specifying directory:

```
Create infographic from research.txt and save in ./infographics/
```

## Contributing

This skill is part of the Lumina-API-Demo project. To modify or extend:

1. **Add new styles**: Edit `image-prompter.md` with new rendering instructions
2. **Modify domains**: Update `style-config.json` with new keywords/variants
3. **Adjust prompts**: Edit `content-analyzer.md` system prompt
4. **Change workflow**: Modify `SKILL.md` steps

## License

Part of the Lumina-API-Demo project.

## Support

For issues or questions:
- Check troubleshooting section above
- Review saved prompt files for debugging
- Verify environment variables are set correctly
- Ensure API services are running and accessible

---

**Generated with Claude Code** - Professional infographics from text, intelligently styled.
