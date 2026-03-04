using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace MinimalApiCall;

// Style configuration models
public record StyleVariant(string Name, List<string> Keywords, List<string> PreferredTones);
public record StyleDomain(string Domain, string Description, List<string> Keywords, List<string> AntiKeywords, int Priority, List<StyleVariant> Variants);

/// <summary>
/// Studio API for generating various content types from sources.
/// Supports: Study Guide, FAQ, Summary, Key Points, etc.
/// </summary>
public class StudioApi
{
    private readonly NotebookApi _notebookApi;
    private readonly NotebookStorage _storage;
    private readonly string _llmEndpoint;
    private readonly string _llmModel;
    private readonly ImageGenerationService _imageService;
    private readonly SkillInvoker _skillInvoker;
    private const int MindmapNodeLimit = 30;
    private const int MindmapEdgeLimit = 60;
    
    // Per-notebook generation cache
    private readonly Dictionary<string, ConcurrentDictionary<string, StudioGeneration>> _notebookGenerations = new();
    
    // Style configuration cache
    private static List<StyleDomain>? _styleConfig;
    private static readonly object _configLock = new();

    public StudioApi(NotebookApi notebookApi, NotebookStorage storage, string llmEndpoint, string llmModel, ImageGenerationService imageService, SkillInvoker skillInvoker)
    {
        _notebookApi = notebookApi;
        _storage = storage;
        _llmEndpoint = llmEndpoint;
        _llmModel = llmModel;
        _imageService = imageService;
        _skillInvoker = skillInvoker;
    }

    private ConcurrentDictionary<string, StudioGeneration> GetGenerations(string notebookId)
    {
        if (!_notebookGenerations.ContainsKey(notebookId))
        {
            // Load from disk
            var generations = _storage.LoadGenerations(notebookId);
            var dict = new ConcurrentDictionary<string, StudioGeneration>();
            foreach (var gen in generations)
            {
                dict[gen.Id] = gen;
            }
            _notebookGenerations[notebookId] = dict;
        }
        return _notebookGenerations[notebookId];
    }

    private void SaveGenerations(string notebookId)
    {
        if (_notebookGenerations.ContainsKey(notebookId))
        {
            _storage.SaveGenerations(notebookId, _notebookGenerations[notebookId].Values);
        }
    }

    /// <summary>
    /// Start content generation asynchronously.
    /// Returns immediately with generating status, actual generation happens in background.
    /// </summary>
    public StudioGeneration StartGeneration(string notebookId, string type, string? customPrompt = null)
    {
        var sources = _notebookApi.GetAllSources(notebookId).ToList();
        
        if (!sources.Any())
        {
            throw new Exception("No sources available. Please add sources first.");
        }

        // Create generation record
        var generation = new StudioGeneration
        {
            Id = Guid.NewGuid().ToString(),
            Type = type,
            Status = "generating",
            CreatedAt = DateTime.UtcNow,
            Title = GetGenerationTitle(type)
        };

        var generations = GetGenerations(notebookId);
        generations[generation.Id] = generation;
        SaveGenerations(notebookId);

        // Start generation in background
        _ = Task.Run(async () =>
        {
            try
            {
                // Build context from sources
                var context = BuildContextFromSources(sources);

                string content;
                string? imagePath = null;
                string? imagePrompt = null;
                
                if (string.Equals(type, "mindmap", StringComparison.OrdinalIgnoreCase))
                {
                    // Generate summary internally, then build mindmap JSON
                    content = await GenerateMindmapAsync(context);
                }
                else if (string.Equals(type, "infographic", StringComparison.OrdinalIgnoreCase))
                {
                    // Generate infographic image with optional custom style
                    var result = await GenerateInfographicAsync(notebookId, generation.Id, context, customPrompt);
                    
                    // Parse result to extract prompt if available
                    var parts = result.Split(new[] { "|||PROMPT:" }, StringSplitOptions.None);
                    content = parts[0];
                    if (parts.Length > 1)
                    {
                        imagePrompt = parts[1];
                    }
                    
                    imagePath = $"{generation.Id}.png";
                }
                else
                {
                    // Get system prompt based on type
                    var systemPrompt = GetSystemPrompt(type, customPrompt);

                    // Generate textual content
                    var userContent = BuildSourcesUserContent(context);
                    content = await CallLlmAsync(systemPrompt, userContent);
                }

                // Update generation (preserve or update ImagePrompt)
                var currentGen = generations[generation.Id];
                var updatedGen = currentGen with
                {
                    Status = "completed",
                    Content = content,
                    ImagePath = imagePath,
                    ImagePrompt = imagePrompt ?? currentGen.ImagePrompt, // Use new prompt or keep existing
                    CompletedAt = DateTime.UtcNow
                };

                generations[generation.Id] = updatedGen;
                SaveGenerations(notebookId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Generation] ❌ FAILED for {type}: {ex.Message}");
                Console.WriteLine($"[Generation] Stack trace: {ex.StackTrace}");
                var failedGen = generation with
                {
                    Status = "failed",
                    Content = $"Generation failed: {ex.Message}"
                };
                generations[generation.Id] = failedGen;
                SaveGenerations(notebookId);
            }
        });

        return generation;
    }

    /// <summary>
    /// Get all generations.
    /// </summary>
    public IEnumerable<StudioGeneration> GetAllGenerations(string notebookId)
    {
        var generations = GetGenerations(notebookId);
        return generations.Values.OrderByDescending(g => g.CreatedAt);
    }

    /// <summary>
    /// Get a specific generation.
    /// </summary>
    public StudioGeneration? GetGeneration(string notebookId, string id)
    {
        var generations = GetGenerations(notebookId);
        generations.TryGetValue(id, out var generation);
        return generation;
    }

    /// <summary>
    /// Delete a generation.
    /// </summary>
    public bool DeleteGeneration(string notebookId, string id)
    {
        var generations = GetGenerations(notebookId);
        var removed = generations.TryRemove(id, out var generation);
        if (removed)
        {
            // Delete associated image if exists
            if (generation != null && !string.IsNullOrEmpty(generation.ImagePath))
            {
                _storage.DeleteGenerationImage(notebookId, generation.ImagePath);
            }
            SaveGenerations(notebookId);
        }
        return removed;
    }

    /// <summary>
    /// Clear all generations.
    /// </summary>
    public void ClearAllGenerations(string notebookId)
    {
        var generations = GetGenerations(notebookId);
        generations.Clear();
        _storage.ClearImagesDirectory(notebookId);
        SaveGenerations(notebookId);
    }

    /// <summary>
    /// Build context from sources.
    /// </summary>
    private string BuildContextFromSources(List<Source> sources)
    {
        var sb = new StringBuilder();
        
        for (int i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            sb.AppendLine($"## Source {i + 1}: {source.Title}");
            sb.AppendLine($"Type: {source.Type}");
            if (!string.IsNullOrEmpty(source.Url))
            {
                sb.AppendLine($"URL: {source.Url}");
            }
            sb.AppendLine();
            sb.AppendLine(source.Content);
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Standard user message for source-based generations.
    /// </summary>
    private string BuildSourcesUserContent(string context)
    {
        return $"Here are the sources to analyze:\n\n{context}";
    }

    /// <summary>
    /// Get system prompt based on generation type.
    /// </summary>
    private string GetSystemPrompt(string type, string? customPrompt)
    {
        if (!string.IsNullOrEmpty(customPrompt))
        {
            return customPrompt;
        }

        return type.ToLower() switch
        {
            "study-guide" => @"You are an expert educator creating comprehensive study guides.

Based on the provided sources, create a detailed study guide that includes:

1. **Overview**: Brief summary of the main topics covered
2. **Key Concepts**: List and explain the most important concepts with clear definitions
3. **Learning Objectives**: What students should understand after studying this material
4. **Detailed Notes**: Organized notes covering all major topics with:
   - Main ideas and supporting details
   - Important terms and definitions
   - Examples and explanations
5. **Study Tips**: Suggestions for how to study this material effectively
6. **Review Questions**: 5-10 questions to test understanding

Format the output in clear markdown with headers, bullet points, and numbered lists.
Make it comprehensive but easy to read and understand.",

            "faq" => @"You are an expert at creating helpful FAQ documents.

Based on the provided sources, create a comprehensive FAQ that includes:

1. **General Questions**: 5-8 common questions about the main topics
2. **Detailed Questions**: 5-8 more specific questions about key concepts
3. **Advanced Questions**: 3-5 deeper questions for advanced understanding

For each question:
- Provide a clear, concise answer (2-4 sentences)
- Include relevant details from the sources
- Use examples when helpful

Format as:
## Q: [Question]
**A:** [Answer]

Make sure the questions cover the breadth of the material.",

                "summary" => @"You are an expert at explaining complex topics to high-school students.

Based on the provided sources, write a friendly summary that a smart teenager could read quickly. Make it approachable, concrete, and avoid jargon.

Structure the output as:
1. **Quick Overview** — 2 short sentences in everyday language.
2. **Why It Matters** — 2-3 bullets explaining the real-world impact or relevance.
3. **Main Ideas** — 4-6 bullets. Each bullet should:
    - Use simple words
    - Start with a bold keyword (e.g., **Planning**)
    - Include a short explanation (1 sentence) and an example when helpful
4. **Key Takeaways for Students** — 3 bullets that tell readers what to remember or try next.

Tone: encouraging, plain, and visual (use bullets and short paragraphs).",

            "key-points" => @"You are an expert at extracting and organizing key information.

Based on the provided sources, identify and present:

1. **Top 10 Key Points**: Most important ideas, ranked by significance
2. **Core Concepts**: Essential concepts that underpin the material
3. **Critical Details**: Important facts, figures, or findings
4. **Actionable Insights**: Practical takeaways or applications

Format each point clearly with:
- Bold title
- 1-2 sentence explanation
- Why it matters

Make it scannable and easy to digest.",

            "infographic" => @"You are an expert Infographic Synthesis & Design Agent.

Your job: Given sources + user context, produce ONE detailed infographic specification optimized for learning and comprehension.

# Core Principles
- Default objective: Help users learn the sources quickly
- Prioritize clarity, structure, and comprehension over decoration
- Never invent facts not in sources; label uncertainties clearly
- Make content scannable with bullets, short phrases, visual hierarchy

# Output Structure (produce detailed text description)

## 1. INFOGRAPHIC BRIEF
**CRITICAL LANGUAGE REQUIREMENT**: Generate ALL content in ENGLISH ONLY.
- Do NOT use Chinese, Japanese, Korean, or any other non-English languages
- Do NOT include decorative foreign text (e.g., Japanese characters in Japanese Minimal style)
- Translate source content to English if needed
- This applies to: titles, headings, body text, labels, captions, all visible text

- Title: Clear, engaging (≤12 English words) - MUST BE ENGLISH
- One-sentence takeaway: Core message (≤20 English words) - MUST BE ENGLISH
- Topic domain: professional | lifestyle | kids | academic | tech
- Tone: serious | friendly | cute | calm | energetic
- Aspect ratio: landscape (16:9 or 4:3)
- Target audience: who will read this

## 2. CONTENT OUTLINE (learning-first structure)
Always include:
a) What it is - brief definition/overview
b) Why it matters - relevance and impact (2-3 bullets)
c) Key points - 5-7 most important concepts with:
   - Bold keyword heading
   - 1-2 sentence explanation
   - Concrete example when available
   - Source indication (e.g., 'from Source 2')
d) How to apply/use (if procedural)
e) Visual suggestions per section

## 3. LAYOUT PLAN
Choose ONE layout type based on content:
- FLOW: for steps/how-to/workflows -> dynamic S-curve or circular path with directional arrows
- TIMELINE: for history/evolution/milestones -> curved timeline or winding path (not straight line)
- COMPARISON: for A vs B/options -> asymmetric split with overlapping elements or Venn-style
- MOSAIC: for multi-theme summary -> magazine-style with varied card sizes and organic arrangement
- RADIAL: for concepts & relationships -> central hub with flowing connections or mind-map style
- HERO-FOCUS: for visually-striking topics with strong imagery -> large hero image/illustration (50-60% height) at top with title overlay, content cards grid below with supporting details and optional table/timeline at bottom
- CIRCULAR-FLOW: for ecosystem/multiple equal themes -> central theme (300-400px) at canvas center with 3-6 circular cards orbiting around it, connected by curved paths/lines, decorative background (galaxy/space/organic pattern)
- SPLIT-STORY: for knowledge + action guides -> three-column layout with left column (40%, knowledge cards with circular images), central decorative connector (20%, galaxy/timeline/path illustration), right column (40%, actionable tips with icons)
- DUAL-PATH: for step-by-step learning with preparation and action phases -> two parallel vertical tracks showing ""Get Ready"" and ""Do It"" stages, connected by flowing path with directional markers, large circular hero illustration at top establishing context, footer band for tips or safety notes, background with contextually appropriate decorative elements guiding visual flow

Describe:
- Visual hierarchy: [Hero title with graphic element] -> [Key insight callout box] -> [Flowing content sections] -> [Visual connectors] -> [Sources footnote]
- Component layout: asymmetric title placement, varied section sizes, mixed content blocks (not uniform grid)
- Visual flow: describe how eye should travel through the design (e.g., ""Z-pattern"", ""F-pattern"", ""circular flow"")
- Density: spacious with breathing room | balanced information density | information-rich but organized

## 4. STYLE GUIDE (auto-select by domain)

For PROFESSIONAL topics (business/finance/enterprise/research):
- Look: modern, confident, editorial magazine style - NOT corporate boring
- Layout: asymmetric compositions, overlapping elements, dynamic angles
- Typography: bold sans-serif titles with serif accents, varied font weights for hierarchy
- Colors: bold primary color + neutrals + metallic accent (gold/silver for premium feel)
- Visual elements: abstract shapes, data viz with style, subtle gradients, photography crops
- Avoid: rigid grids, stock icons, outdated clipart

For LIFESTYLE topics (health/travel/productivity/home):
- Look: organic, inviting, Instagram-worthy, editorial vibe
- Layout: Pinterest-style mixed media, photo collages, flowing text wraps
- Typography: mix of script/handwritten headers with clean body text
- Colors: curated palette (pastels, earth tones, or vibrant depending on topic)
- Visual elements: lifestyle photography, hand-drawn doodles, botanical elements
- Include: whitespace as design element, overlapping layers, texture

For ACADEMIC/TECH topics (papers/algorithms/architecture):
- Look: contemporary infographic style, not textbook bland
- Layout: modular but dynamic, geometric shapes with flow, isometric elements
- Typography: monospace for code/data, geometric sans-serif for headers
- Colors: tech palette (electric blue, neon accents, dark mode aesthetic) or minimal B&W with one accent
- Visual elements: 3D isometric icons, circuit-board patterns, animated-style diagrams
- Include: connecting lines with style, nodes and networks, layered transparency

For KIDS topics (classroom projects/family DIY/elementary science/children's activities):
- Look: storybook energy, welcoming educational vibe, safe for classrooms and family settings
- Layout: large hero scene establishing topic, chunky content sections with clear visual separation, oversized numbered badges for steps
- Typography: bold rounded sans-serif for headers, high-contrast sans-serif body text at 18-22pt for easy reading by young learners
- Colors: bright yet approachable palette (primary colors with soft variants), high saturation balanced with pastel backgrounds for readability
- Visual elements: friendly illustrated characters, chunky rounded icons (no sharp edges), speech bubbles for tips, sticker-style badges, playful shapes
- Include: tactile textures (paper grain, crayon shading, watercolor effects), motion lines showing action, visual connectors with personality
- Avoid: tiny typography, monochrome palettes, complex gradients, mature themes, corporate sterility, any non-English lettering

## 5. READABILITY RULES (strictly apply)
- ALL TEXT MUST BE IN ENGLISH (no Chinese, Japanese, Korean, or other languages)
- Each bullet: <=12 English words
- Define jargon in-place once: 'RAG (Retrieval-Augmented Generation)'
- Use concrete examples from sources (translated to English if needed)
- No dense paragraphs - use bullets, chips, labels
- Numbers must have context: '40% increase (vs. 2023)'
- Pick 5-7 'most teachable' points (signal > coverage)

## 6. SOURCE GROUNDING
- Every key point must reference a source: 'according to Source 1' or '(Source 2)'
- If sources conflict: note it clearly
- Include source footer at bottom with 3-5 most important source titles

Your output: A comprehensive TEXT description covering all above elements that an image generator can use to create a professional, educational infographic in landscape format. Be specific about layout, visual elements, exact text to render, and style choices.",

            _ => $"Analyze the provided sources and create a detailed {type} document. Be comprehensive and well-structured."
        };
    }

    /// <summary>
    /// Get generation title based on type.
    /// </summary>
    private string GetGenerationTitle(string type)
    {
        return type.ToLower() switch
        {
            "study-guide" => "Study Guide",
            "faq" => "Frequently Asked Questions",
            "summary" => "Summary",
            "key-points" => "Key Points",
            "mindmap" => "Mind Map",
            "infographic" => "Infographic",
            _ => type
        };
    }

    /// <summary>
    /// Build mindmap JSON by first creating an internal summary.
    /// </summary>
    private async Task<string> GenerateMindmapAsync(string context)
    {
        var summaryPrompt = GetSystemPrompt("summary", null);
        var sourceUserContent = BuildSourcesUserContent(context);
        var summary = await CallLlmAsync(summaryPrompt, sourceUserContent);

        var mindmapPrompt = GetMindmapSystemPrompt();
        var mindmapUserContent = BuildMindmapUserContent(context);  // 直接使用原始context而非summary
        var rawMindmap = await CallLlmAsync(mindmapPrompt, mindmapUserContent, maxTokens: 3000);  // 增加token以容纳详细内容

        Console.WriteLine("[Mindmap] Raw response snippet: " + TruncateForLog(rawMindmap, 500));

        try
        {
            var normalized = NormalizeMindmapJson(rawMindmap);
            Console.WriteLine("[Mindmap] Successfully normalized JSON, length: " + normalized.Length);
            Console.WriteLine("[Mindmap] Normalized JSON snippet: " + TruncateForLog(normalized, 300));
            return normalized;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[Mindmap] JSON normalization failed: " + ex.Message);
            Console.WriteLine("[Mindmap] Falling back to deterministic mindmap built from summary.");
            return BuildFallbackMindmap(summary);
        }
    }

    /// <summary>
    /// Generate infographic by passing notebook content to Python skill.
    /// Python skill handles: LLM analysis, domain detection, prompt building, image generation.
    /// </summary>
    private async Task<string> GenerateInfographicAsync(string notebookId, string generationId, string context, string? customPrompt = null)
    {
        // Pass raw notebook content to Python skill
        // Python skill will handle:
        // - Step 1: LLM content analysis and domain detection
        // - Step 2: Build image prompt with style-specific rendering
        // - Step 3: Generate image via GPT Image API
        // - Step 4: Save PNG and prompt files
        
        var imagesDir = _storage.GetImagesDirectory(notebookId);
        var imagePath = Path.Combine(imagesDir, $"{generationId}.png");

        // Create temporary file with raw notebook content for Python script
        var tempInputFile = Path.Combine(Path.GetTempPath(), $"infographic_input_{generationId}.txt");
        try
        {
            await File.WriteAllTextAsync(tempInputFile, context);

            Console.WriteLine($"[Infographic] Calling Python skill for complete infographic generation...");
            Console.WriteLine($"[Infographic] Input: {tempInputFile} ({context.Length} chars)");
            Console.WriteLine($"[Infographic] Output: {imagePath}");
            if (!string.IsNullOrWhiteSpace(customPrompt))
            {
                Console.WriteLine($"[Infographic] Custom style: {customPrompt.Substring(0, Math.Min(100, customPrompt.Length))}...");
            }

            // Execute Python skill (handles all 4 steps)
            var result = await _skillInvoker.ExecuteInfographicGenerationAsync(
                tempInputFile,
                imagePath,
                _llmEndpoint,
                _llmModel,
                customPrompt,
                timeoutMs: 300_000 // 5 minutes
            );

            // Log Python output
            if (!string.IsNullOrEmpty(result.StandardOutput))
            {
                Console.WriteLine($"[Infographic] Python stdout:\n{result.StandardOutput}");
            }

            if (!string.IsNullOrEmpty(result.StandardError))
            {
                Console.WriteLine($"[Infographic] Python stderr:\n{result.StandardError}");
            }

            if (!result.Success)
            {
                throw new Exception($"Python skill execution failed: {result.ErrorMessage}\n{result.GetCombinedOutput()}");
            }

            // Verify image was created
            if (!File.Exists(imagePath))
            {
                throw new Exception($"Python skill completed but image file not found at {imagePath}");
            }

            var fileInfo = new FileInfo(imagePath);
            Console.WriteLine($"[Infographic] ✅ Image generated successfully ({fileInfo.Length / 1024.0:F2} KB)");

            // Read prompt file if it exists (saved by Python script)
            var promptFilePath = Path.Combine(imagesDir, $"prompt_{generationId}.txt");
            string? imagePrompt = null;
            if (File.Exists(promptFilePath))
            {
                try
                {
                    imagePrompt = await File.ReadAllTextAsync(promptFilePath);
                    Console.WriteLine($"[Infographic] Prompt file loaded: {promptFilePath} ({imagePrompt.Length} chars)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Infographic] Warning: Could not read prompt file: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"[Infographic] Warning: Prompt file not found at {promptFilePath}");
            }

            return $"Infographic generated successfully.|||PROMPT:{imagePrompt ?? "N/A"}";
        }
        finally
        {
            // Clean up temporary input file
            try
            {
                if (File.Exists(tempInputFile))
                {
                    File.Delete(tempInputFile);
                }
            }
            catch { /* Ignore cleanup errors */ }
        }
    }

    private string GetMindmapSystemPrompt()
    {
        return @"You are an expert learning designer creating educational mindmaps that help users deeply understand complex topics.

Your goal: Break down the topic into LOGICAL LEARNING DIMENSIONS that guide users from foundational concepts to deeper understanding. Always produce VALID JSON only (no prose, no markdown fences).

Schema:
{
  ""nodes"": [
    {
      ""id"": ""string (unique)"",
      ""label"": ""Descriptive phrase (10-20 words max, include key details/specifics)"",
      ""parentId"": ""parent node id or null for root"",
      ""summary"": ""optional brief note"",
      ""depth"": number (0 for root)
    }
  ],
  ""edges"": [
    {
      ""from"": ""node id"",
      ""to"": ""node id"",
      ""relation"": ""relationship type""
    }
  ]
}

LEARNING-ORIENTED Structure:
- Organize nodes by pedagogical dimensions (e.g., Core Concepts, Historical Context, Key Applications, Common Misconceptions, Real-World Examples)
- Progress from fundamental ideas to advanced nuances
- Each branch should answer: ""What does the learner need to know about this aspect?""
- 8-15 nodes total, grouped by clear learning themes

CRITICAL Rules for Labels:
- Make labels INFORMATIVE and LEARNING-FOCUSED
- Include key insights that aid comprehension
- Keep under 10 words, use parentheses for clarifications
- Examples: 
  - BAD: ""Main Characters"" 
  - GOOD: ""Judy Hopps (Rookie) & Nick Wilde (Partner)""
  - BAD: ""Features""
  - GOOD: ""Real-time Collaboration (Multiple Users Editing)""

Technical:
- 1 root node (depth 0); children depth = parent depth + 1
- Use edges sparingly for important conceptual connections
- Do NOT include markdown fences or commentary—JSON only";
    }

    private string BuildMindmapUserContent(string sourcesContext)
    {
        return $@"Source Materials:

{sourcesContext}

Create an educational mindmap JSON that helps learners understand this topic deeply. For each node:

LEARNING DESIGN PRINCIPLES:
1. Organize by pedagogical dimensions (e.g., ""Core Concepts"", ""Why It Matters"", ""How It Works"", ""Common Pitfalls"", ""Practical Examples"", ""Key Differences"")
2. Start with foundational ideas, progress to nuanced understanding
3. Each node should answer: ""What insight helps the learner grasp this better?""
4. Include concrete examples, analogies, or contrasts that aid comprehension

NODE REQUIREMENTS:
- 'label': Clear, specific, information-rich (under 10 words)
- 'content': 2-3 sentences explaining WHY this matters for learning, with specific details from sources
- 'summary': One-line takeaway that reinforces understanding
- 'sources': Array of supporting source references
- 'edges': Connect related concepts that illuminate each other

Focus on creating a LEARNING JOURNEY—not just information dump. Structure nodes to guide progressive understanding. Remember: JSON only, no markdown fences.";
    }

    private string NormalizeMindmapJson(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstBrace = trimmed.IndexOf('{');
            var lastBrace = trimmed.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                trimmed = trimmed.Substring(firstBrace, lastBrace - firstBrace + 1);
            }
        }

        MindmapPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<MindmapPayload>(trimmed, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            throw new Exception($"Mindmap JSON invalid: {ex.Message}");
        }

        if (payload == null || payload.Nodes == null || payload.Nodes.Count == 0)
        {
            throw new Exception("Mindmap JSON must include at least one node.");
        }

        if (payload.Nodes.Count > MindmapNodeLimit)
        {
            throw new Exception($"Mindmap contains {payload.Nodes.Count} nodes, exceeding the limit of {MindmapNodeLimit}.");
        }

        if (payload.Edges != null && payload.Edges.Count > MindmapEdgeLimit)
        {
            throw new Exception($"Mindmap contains {payload.Edges.Count} edges, exceeding the limit of {MindmapEdgeLimit}.");
        }

        var nodeLookup = new Dictionary<string, MindmapNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in payload.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id) || string.IsNullOrWhiteSpace(node.Label))
            {
                throw new Exception("Mindmap nodes require both 'id' and 'label'.");
            }

            if (!nodeLookup.TryAdd(node.Id, node))
            {
                throw new Exception($"Duplicate mindmap node id '{node.Id}'.");
            }
        }

        foreach (var node in payload.Nodes)
        {
            if (!string.IsNullOrEmpty(node.ParentId) && !nodeLookup.ContainsKey(node.ParentId))
            {
                throw new Exception($"Node '{node.Id}' references missing parent '{node.ParentId}'.");
            }
        }

        if (payload.Edges != null)
        {
            foreach (var edge in payload.Edges)
            {
                if (string.IsNullOrWhiteSpace(edge.From) || string.IsNullOrWhiteSpace(edge.To))
                {
                    throw new Exception("Mindmap edges require both 'from' and 'to'.");
                }

                if (!nodeLookup.ContainsKey(edge.From) || !nodeLookup.ContainsKey(edge.To))
                {
                    throw new Exception("Mindmap edges must reference existing node ids.");
                }
            }
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return JsonSerializer.Serialize(payload, options);
    }

    private string BuildFallbackMindmap(string summary)
    {
        var lines = summary.Split('\n', StringSplitOptions.None);
        var rootLabel = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? "Mindmap Overview";
        rootLabel = SanitizeNodeLabel(rootLabel);
        if (string.IsNullOrWhiteSpace(rootLabel))
        {
            rootLabel = "Mindmap Overview";
        }

        var nodes = new List<MindmapNode>
        {
            new MindmapNode
            {
                Id = "root",
                Label = rootLabel,
                Summary = "Primary topic",
                Depth = 0
            }
        };

        var childTopics = ExtractFallbackTopics(lines);
        int i = 0;
        foreach (var topic in childTopics)
        {
            nodes.Add(new MindmapNode
            {
                Id = $"node{++i}",
                Label = topic.Label,
                Summary = topic.Summary,
                ParentId = "root",
                Depth = 1
            });
        }

        if (nodes.Count == 1)
        {
            nodes.Add(new MindmapNode
            {
                Id = "node1",
                Label = "Key Points (Summary not available)",
                Summary = "No content extracted",
                ParentId = "root",
                Depth = 1
            });
        }

        var payload = new MindmapPayload
        {
            Nodes = nodes
        };

        var options = new JsonSerializerOptions 
        { 
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        return JsonSerializer.Serialize(payload, options);
    }

    private List<(string Label, string? Summary)> ExtractFallbackTopics(string[] lines)
    {
        var topics = new List<(string Label, string? Summary)>();

        foreach (var raw in lines)
        {
            if (topics.Count >= 8) break;

            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            string? label = null;

            if (line.StartsWith("##"))
            {
                label = line.Trim('#', ' ');
            }
            else if (line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("• "))
            {
                label = line[2..].Trim();
            }
            else if (char.IsDigit(line.FirstOrDefault()) && line.Contains('.'))
            {
                var idx = line.IndexOf('.');
                if (idx >= 0 && idx + 1 < line.Length)
                {
                    label = line[(idx + 1)..].Trim();
                }
            }

            if (!string.IsNullOrWhiteSpace(label))
            {
                label = SanitizeNodeLabel(label);
                if (!string.IsNullOrWhiteSpace(label))
                {
                    var summary = label.Length > 80 ? label[..80] : label;
                    topics.Add((label, summary));
                }
            }
        }

        return topics;
    }

    private string SanitizeNodeLabel(string input)
    {
        var clean = input.Replace("**", string.Empty)
                          .Replace("__", string.Empty)
                          .Replace("`", string.Empty)
                          .Replace("##", string.Empty)
                          .Trim();

        if (clean.Length > 60)
        {
            clean = clean[..60];
        }

        return clean;
    }

    private string TruncateForLog(string? text, int max)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= max ? text : text[..max] + "...";
    }

    /// <summary>
    /// Call LLM to generate content.
    /// </summary>
    private async Task<string> CallLlmAsync(string systemPrompt, string userContent, int maxTokens = 3000)
    {
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromMinutes(2); // Longer timeout for generation

        // Use Anthropic API format (for egress-llm compatibility)
        var messages = new[]
        {
            new { role = "user", content = userContent }
        };

        var requestBody = new
        {
            model = _llmModel,
            system = systemPrompt, // Anthropic uses separate 'system' field
            messages = messages,
            temperature = 0.7,
            max_tokens = maxTokens // Allow caller to tune length per artifact
        };

        try
        {
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Use Anthropic endpoint /v1/messages instead of OpenAI's /v1/chat/completions
            var response = await client.PostAsync($"{_llmEndpoint}/v1/messages", content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"LLM API error: {response.StatusCode} - {responseText}");
            }

            var result = JsonSerializer.Deserialize<JsonElement>(responseText);
            
            // Anthropic API returns content in different format
            var content_array = result.GetProperty("content");
            if (content_array.ValueKind == JsonValueKind.Array && content_array.GetArrayLength() > 0)
            {
                var generatedContent = content_array[0].GetProperty("text").GetString();
                return generatedContent ?? "No content generated";
            }

            return "No content generated";
        }
        catch (HttpRequestException ex)
        {
            throw new Exception($"Cannot connect to Copilot API at {_llmEndpoint}. Error: {ex.Message}");
        }
    }

    // ==================== Style-Specific Rendering Methods ====================

    /// <summary>
    /// Load style configuration from JSON file.
    /// </summary>
    private static List<StyleDomain> LoadStyleConfig()
    {
        if (_styleConfig != null) return _styleConfig;
        
        lock (_configLock)
        {
            if (_styleConfig != null) return _styleConfig;
            
            var configPath = Path.Combine("notebooks", "Backend", "style-config.json");
            var json = File.ReadAllText(configPath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            _styleConfig = JsonSerializer.Deserialize<List<StyleDomain>>(json, options) ?? new();
            
            Console.WriteLine($"[Config] Loaded {_styleConfig.Count} style domains from {configPath}");
            return _styleConfig;
        }
    }

    /// <summary>
    /// Get style-specific rendering instructions based on domain extracted from LLM description.
    /// </summary>
    private string GetStyleSpecificRenderingInstructions(string description)
    {
        var config = LoadStyleConfig();
        var domain = ExtractDomainFromDescription(description, config);
        var variant = SelectIntelligentVariant(description, domain, config);
        
        Console.WriteLine($"[Style] Domain: {domain}, Variant: {variant}");
        
        return domain switch
        {
            "business" => GetBusinessRenderingInstructions(variant),
            "history" => GetHistoryRenderingInstructions(variant),
            "science" => GetScienceRenderingInstructions(variant),
            "nature-space" => GetNatureSpaceRenderingInstructions(variant),
            "technology" => GetTechnologyRenderingInstructions(variant),
            "lifestyle" => GetLifestyleRenderingInstructions(variant),
            "social" => GetSocialRenderingInstructions(variant),
            "family" => GetFamilyRenderingInstructions(variant),
            _ => GetDefaultRenderingInstructions()
        };
    }

    /// <summary>
    /// Extract domain from LLM description based on style-config.json keywords.
    /// Uses word boundary matching to avoid partial word matches (e.g., "space" in "workspace").
    /// </summary>
    private string ExtractDomainFromDescription(string description, List<StyleDomain> config)
    {
        var lowerDesc = description.ToLower();
        
        // Check anti-keywords first to exclude inappropriate domains
        foreach (var domain in config.OrderByDescending(d => d.Priority))
        {
            if (domain.AntiKeywords.Any(ak => ContainsWord(lowerDesc, ak.ToLower())))
                continue;
                
            if (domain.Keywords.Any(kw => ContainsWord(lowerDesc, kw.ToLower())))
                return domain.Domain;
        }
        
        return "business"; // Default
    }

    /// <summary>
    /// Check if text contains a whole word (not partial match).
    /// </summary>
    private bool ContainsWord(string text, string word)
    {
        // Use word boundary to match complete words only
        var pattern = $@"\b{System.Text.RegularExpressions.Regex.Escape(word)}\b";
        return System.Text.RegularExpressions.Regex.IsMatch(text, pattern);
    }

    /// <summary>
    /// Select variant intelligently based on keywords and preferred tones.
    /// </summary>
    private string SelectIntelligentVariant(string description, string domainName, List<StyleDomain> config)
    {
        var domain = config.FirstOrDefault(d => d.Domain == domainName);
        if (domain == null || domain.Variants.Count == 0)
            return "default";
        
        var lowerDesc = description.ToLower();
        
        // Score each variant based on keyword and tone matches
        var bestVariant = domain.Variants
            .Select(v => new
            {
                Variant = v,
                Score = v.Keywords.Count(kw => lowerDesc.Contains(kw.ToLower())) * 2 +
                       v.PreferredTones.Count(t => lowerDesc.Contains(t.ToLower()))
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => Random.Shared.Next()) // Randomize if tied
            .First();
        
        return bestVariant.Variant.Name;
    }

    // ==================== Business Domain Variants ====================

    private string GetBusinessRenderingInstructions(string variant)
    {
        return variant switch
        {
            "editorial" => GetEditorialMagazineStyle(),
            "minimal" => GetMinimalDataStyle(),
            "corporate" => GetCorporateReportStyle(),
            _ => GetEditorialMagazineStyle()
        };
    }

    private string GetEditorialMagazineStyle() => @"
### EDITORIAL MAGAZINE STYLE (Professional)

#### Color Palette:
- Background: Deep navy (#1A2332) or charcoal (#2D3436) with subtle texture
- Primary: Burgundy/wine red (#8B2635) for bold headlines and accents
- Accent: Gold metallic (#D4AF37) for premium feel, use sparingly
- Text: White (#FFFFFF) for titles, light gray (#E8E8E8) for body

#### Typography:
- Headlines: Extra bold geometric sans-serif (72pt+), dramatic size contrast
- Subheadings: Bold serif for elegant callouts, 36-48pt
- Body text: Clean sans-serif, 16-18pt, generous line spacing (1.6x)
- Pull quotes: Large italic serif, 48pt+, used as visual anchors

#### Layout & Composition:
- Asymmetric split layout with 60/40 or 70/30 proportions
- Overlapping photo blocks with text wraps creating depth
- Angled section dividers (diagonal lines at 15-20 degrees)
- Content blocks break grid intentionally for editorial feel
- Large photography crops as visual anchors

#### Visual Elements:
- High-contrast photography with dramatic cropping
- Bold geometric color blocks for section separation
- Minimal icons (solid or duotone style), used sparingly
- Data visualizations styled with brand colors, thick lines
- White space used dramatically, not just as filler

#### Mood & References:
- Confident, authoritative, premium magazine aesthetic
- Think: Bloomberg Businessweek covers, The Economist infographics
- Sophisticated but accessible, not stuffy
- Editorial photography style (not stock photos)

#### Key Features:
- Dramatic hierarchy (huge vs small, not gradual)
- Bold pull quotes as design elements
- Unexpected angles and overlaps
- Information density balanced with breathing room

#### Avoid:
- Cute rounded corners or playful elements
- Pastel or soft colors
- Symmetrical centered layouts
- Generic stock icons
- Overly decorative flourishes";

    private string GetMinimalDataStyle() => @"
### MINIMAL DATA STYLE (Professional)

#### Color Palette:
- Background: Pure white (#FFFFFF) or very light gray (#F8F9FA)
- Primary: Single bold accent - electric blue (#0066FF) OR deep forest green (#006B3F)
- Data colors: Monochromatic variations of primary (3-4 shades max)
- Text: Near-black (#1A1A1A) for perfect contrast

#### Typography:
- All text: Clean geometric sans-serif (Inter, SF Pro Display, Helvetica Neue)
- Headlines: Bold weight, 48-64pt, minimal letter spacing
- Body: Regular weight, 14-16pt, optimal line height (1.5x)
- Data labels: Medium weight, 12-14pt, uppercase for emphasis
- Consistent font sizing system (scale: 12, 14, 16, 24, 48, 64)

#### Layout & Composition:
- Grid-based but with generous margins (20-25% whitespace minimum)
- Centered content zones with symmetrical padding
- Clear visual hierarchy through size and spacing (not color)
- Elements aligned to precise grid (8pt or 12pt baseline)
- Maximum 3-4 content sections to maintain clarity

#### Visual Elements:
- Clean line charts with minimal gridlines (1-2 reference lines max)
- Simple bar graphs with rounded ends (4px radius)
- Minimalist icons (outline style, 2px stroke, no fills)
- Subtle drop shadows only where necessary (2px blur, 10% opacity)
- No decorative elements - every element serves a purpose

#### Mood & References:
- Clear, precise, maximum signal-to-noise ratio
- Think: Apple Keynote slides, Stripe documentation, Linear app
- Data speaks for itself without embellishment
- Swiss design principles - form follows function

#### Key Features:
- 40-50% whitespace as design element
- One focal point per section
- Consistent spacing system throughout
- Perfect alignment and symmetry
- Breathing room around every element

#### Avoid:
- Gradients, textures, or busy backgrounds
- Multiple accent colors or color coding
- Overlapping elements or layering
- Decorative graphics or illustrations
- Information overload";

    private string GetCorporateReportStyle() => @"
### CORPORATE REPORT STYLE (Professional)

#### Color Palette:
- Background: White (#FFFFFF) with light gray sections (#F5F7FA)
- Primary: Professional navy (#002B5C) for headers and key data
- Secondary: Medium gray (#5A6C7D) for supporting text
- Accent: Warm orange (#FF6B35) for highlights and CTAs
- Chart colors: 4-5 color palette (blue, teal, orange, gray, green)

#### Typography:
- Headers: Bold Arial/Helvetica, 32-48pt, traditional business feel
- Subheaders: Medium weight, 24-28pt, clear hierarchy
- Body: Regular Arial/Helvetica, 14-16pt, high readability
- Captions: Small text, 11-12pt, gray color for de-emphasis
- Numbers/data: Tabular figures for alignment, bold for emphasis

#### Layout & Composition:
- Structured sections with clear dividers (1-2px lines)
- Boxed information panels with subtle borders
- Consistent left/right margins (15-20% of width)
- Numbered sections and subsections for easy reference
- Header/footer with page info and branding

#### Visual Elements:
- Professional charts: pie charts, bar graphs, line graphs with legends
- Data tables with alternating row colors (zebra striping)
- Icon badges for categories (solid style, corporate palette)
- Process diagrams with numbered steps and arrows
- Callout boxes with border and light background fill

#### Mood & References:
- Trustworthy, organized, institutional authority
- Think: McKinsey reports, Deloitte insights, corporate annual reports
- Formal but not boring - professional polish
- Information-dense but well-organized

#### Key Features:
- High information density with clear organization
- Consistent visual system (colors, icons, charts)
- Clear sectioning with numbered hierarchy
- Source citations prominent (footnotes, references)
- Professional polish in every detail

#### Avoid:
- Artistic or unconventional layouts
- Trendy design elements or effects
- Playful colors or casual typography
- Asymmetry or broken grids
- Oversimplification - show the complexity";

    // ==================== Lifestyle Domain Variants ====================

    private string GetLifestyleRenderingInstructions(string variant)
    {
        return variant switch
        {
            "pinterest" => GetPinterestMixedMediaStyle(),
            "japanese" => GetJapaneseMinimalStyle(),
            "vibrant" => GetVibrantSocialMediaStyle(),
            "illustrated" => GetIllustratedEditorialStyle(),
            _ => GetPinterestMixedMediaStyle()
        };
    }

    private string GetPinterestMixedMediaStyle() => @"
### PINTEREST MIXED MEDIA STYLE (Lifestyle)

#### Color Palette:
- Background: Soft cream (#F4F1DE) or warm white (#FAF8F5)
- Primary pastels: Blush pink (#FFB6C1), sage green (#C8D5B9), terracotta (#E07A5F)
- Neutrals: Warm beige (#E8DCC4), soft gray (#D4D4D4)
- Accent: Dusty rose (#C89595) or muted gold (#D4A574)

#### Typography:
- Headlines: Mix of script/handwritten fonts (48-64pt) for personality
- Subheadings: Clean rounded sans-serif (24-32pt) for readability
- Body: Friendly sans-serif (14-16pt), relaxed spacing (1.7x line height)
- Decorative text: Cursive or calligraphy for special emphasis
- Varied organic spacing - not rigid grid alignment

#### Layout & Composition:
- Collage-style with photo blocks at varied angles (0-5 degree tilts)
- Overlapping layers creating depth (3-4 layers deep)
- Irregular shapes for content blocks (rounded rectangles, organic blobs)
- Flowing composition - eye travels in S-curve or circular path
- Scrapbook aesthetic with intentional ""imperfection""

#### Visual Elements:
- Lifestyle photography (lifestyle moments, flat lays, natural scenes)
- Hand-drawn doodles (arrows, stars, hearts) - USE SPARINGLY
- Botanical illustrations (leaves, flowers, branches) as accent elements
- Texture overlays (paper texture, fabric, watercolor washes) - SUBTLE only
- Mixed media feel (photo + illustration + hand-drawn)
- Organic shapes and blobs for content containers (not rectangular frames)

#### Mood & References:
- Warm, inviting, aspirational yet attainable
- Think: Pinterest mood boards, Instagram aesthetic accounts
- Curated imperfection - polished but not sterile
- Personal touch like a handmade journal
- Clean breathing room between elements

#### Key Features:
- Photo-heavy with visual storytelling
- Organic arrangement (not rigid grid)
- Decorative flourishes enhance, not overwhelm - LESS IS MORE
- Mixed media layering (photos + drawings + text)
- Warm color harmony throughout
- Generous whitespace between sections

#### Avoid:
- ❌ Decorative horizontal lines separating sections (makes it cluttered)
- ❌ Unnecessary borders around text or images
- ❌ Underlines beneath headings or subheadings
- ❌ Divider lines between content blocks
- ❌ Frame borders around photos (let them breathe)
- ❌ Too many decorative elements competing for attention
- Corporate stiffness or formality
- Harsh contrasts or neon colors
- Perfect alignment or rigid grids
- Tech elements or futuristic design
- Cold color palettes";

    private string GetJapaneseMinimalStyle() => @"
### JAPANESE MINIMAL STYLE (Lifestyle)

⚠️ CRITICAL LANGUAGE REQUIREMENT:
- ALL text must be in ENGLISH alphabet only (A-Z, a-z, 0-9)
- Do NOT use Japanese characters (hiragana, katakana, kanji) ANYWHERE
- Do NOT use Chinese, Korean, or any non-English text
- ""Japanese Minimal"" refers to the AESTHETIC STYLE (simplicity, whitespace, wabi-sabi)
- NOT the language - all content must be readable English text

#### Color Palette:
- Background: Natural cream (#F5F5DC) or soft beige (#E8DCC4)
- Primary: Soft gray (#B8B8B8) for text and lines
- Accent: Single earth tone - clay (#C4A57B) OR charcoal (#4A4A4A)
- Highlight: Very subtle - warm sand (#D4C5B0) or cool stone (#C5C9C7)
- Maximum 3-4 colors total including background

#### Typography:
- All text: Light to regular weight, never heavy bold - ENGLISH TEXT ONLY
- Headlines: Elegant sans-serif or serif, 36-48pt, generous spacing - ENGLISH ONLY
- Body: Light sans-serif, 14-16pt, 2.0x line height for breathing room - ENGLISH ONLY
- NO vertical text in Japanese style (use English horizontal text)
- Haiku-like brevity in ENGLISH - fewer words, more meaning
- Perfect kerning and spacing - precision matters

#### Layout & Composition:
- Zen-like composition with intentional emptiness (60-70% whitespace)
- Asymmetric balance inspired by ikebana (flower arrangement)
- Golden ratio proportions (1:1.618) for section divisions
- Minimal elements with maximum impact - each element carefully placed
- Negative space as primary design element (ma - 間)
- Nothing is accidental - every space has purpose

#### Visual Elements:
- Simple line drawings with minimal strokes (sumi-e style) - ONLY when essential
- Negative space illustrations (what's NOT drawn is as important)
- Natural textures (paper grain, wood texture, rice paper) - VERY subtle
- Small accent marks (single dot or shape for emphasis) - RARE use
- Photography only if necessary - prefer illustration
- NO borders, NO dividing lines, NO decorative rules

#### Mood & References:
- Calm, meditative, contemplative quiet
- Think: Muji aesthetic, Japanese tea ceremony, wabi-sabi philosophy
- Imperfect perfection - embrace natural imperfection
- Mindful simplicity - less is profoundly more
- Empty space is design (ma - 間)

#### Key Features:
- Extreme restraint - resist adding more
- Breathing room around every element (minimum 40px margins)
- Fewer elements with deeper presence
- Quiet elegance - whispers, not shouts
- Natural materials and textures
- Separation through whitespace, NOT lines

#### Avoid:
- ❌ ANY Japanese, Chinese, Korean, or non-English text characters
- ❌ Vertical Japanese-style text layout (use horizontal English text)
- ❌ Foreign language decorations or calligraphy
- ❌ ANY horizontal or vertical dividing lines between sections
- ❌ Borders around content areas or images
- ❌ Decorative rules or separators
- ❌ Underlines or strikethroughs for emphasis
- ❌ Frames of any kind
- Bold vibrant colors or high saturation
- Busy patterns or decorative elements
- Multiple focal points or visual competition
- Information density or cramping
- Western maximalism or exuberance

REMEMBER: This is ""Japanese Minimal AESTHETIC"" with ENGLISH TEXT, not Japanese language content.";

    private string GetVibrantSocialMediaStyle() => @"
### VIBRANT SOCIAL MEDIA STYLE (Lifestyle)

#### Color Palette:
- Background: Bright white (#FFFFFF) or vibrant color blocks
- Primary: Bold saturated colors - hot pink (#FF006E), electric blue (#00C2FF)
- Secondary: Sunshine yellow (#FFD60A), lime green (#B7FF00), coral (#FF7F51)
- Use 4-5 bold colors simultaneously - embrace color chaos
- High contrast combinations for maximum pop

#### Typography:
- Headlines: Fun rounded sans-serif (Quicksand, Fredoka), 48-72pt, BOLD
- Body: Geometric sans-serif, 16-20pt (large for readability on mobile)
- Varied sizes for visual rhythm (mix 12pt, 24pt, 48pt, 72pt+)
- ALL CAPS for emphasis, lowercase for casual feel
- Emoji integration as design elements ✨💡🎨
- Playful text on curved paths or circles

#### Layout & Composition:
- Dynamic diagonal angles (15-30 degrees) for energy
- Instagram Story format inspiration (9:16 aspect adapted to landscape)
- Sticker-like elements scattered asymmetrically
- Layered composition with 4-5 depth levels
- Intentional ""organized chaos"" - busy but navigable
- Mobile-first thinking - bold clear elements

#### Visual Elements:
- Colorful abstract shapes (blobs, circles, stars, lightning bolts)
- Gradient meshes (multi-color gradients, 3-4 colors)
- Sticker overlays (3D-looking stickers with shadows)
- Playful icons (rounded, friendly, expressive faces)
- Animated-style illustrations (frame-like elements)
- Speech bubbles and callout shapes
- Pattern fills (dots, stripes, doodles)

#### Mood & References:
- Energetic, youthful, optimistic, playful
- Think: TikTok/Instagram Stories, Gen-Z aesthetic, Canva templates
- FOMO-inducing - looks fun and engaging
- Social media native design language

#### Key Features:
- High energy through color and movement
- Color blocking with multiple bold colors - USE COLORED BACKGROUNDS instead of lines
- Playful overlays and stickers
- Motion implied through angles and overlaps
- Trendy and current (2024-2025 aesthetics)
- Separation through color blocks and shapes, NOT lines

#### Avoid:
- ❌ Thin horizontal lines separating sections (outdated)
- ❌ Border strokes around elements (use filled shapes instead)
- ❌ Underlines or decorative rules
- ❌ Frame outlines (use solid color blocks)
- Conservative corporate colors
- Formal structured layouts
- Traditional serif fonts
- Minimal or subdued palettes
- Serious or somber tone
- ANY use of dividing lines when color blocks can do the job";

    private string GetIllustratedEditorialStyle() => @"
### ILLUSTRATED EDITORIAL STYLE (Lifestyle/Educational)

**Domain**: Educational content, travel guides, family activities, how-to guides
**Mood**: Friendly, warm, approachable, story-driven, suitable for all ages

#### Color Palette:
- Background: Warm cream (#F5F2E8) or soft beige (#E8DCC4)
- Primary earth tones:
  * Earth brown: #8B6F47 (grounding, natural)
  * Sky blue: #7BA7BC (calm, open)
  * Forest green: #5A7553 (fresh, organic)
- Accent colors (choose 2-3 per design):
  * Sunset orange: #D4765F (warm energy)
  * Dusk purple: #8B7BA8 (contemplative)
  * Dawn yellow: #F4C542 (optimism)
- Text:
  * Deep brown-black: #2C2416 (softer than pure black)
  * Medium brown: #5A4A3A (secondary text)
  * Light brown: #8B7355 (captions, metadata)

#### Typography:
- Headlines: Friendly serif or rounded sans-serif
  * Fonts: Merriweather, Lora, Raleway, Quicksand
  * Size: 36-56pt
  * Weight: Bold (700) or Black (900)
  * Color: Deep brown-black
  * Personality: Warm, inviting, readable
- Body text: Humanist sans-serif
  * Fonts: Open Sans, Nunito, Source Sans Pro
  * Size: 15-18pt (larger for better readability)
  * Weight: Regular (400)
  * Line height: 1.7x (generous spacing)
  * Color: Medium brown
- Labels/captions:
  * Same font as body
  * Size: 12-14pt
  * Weight: Semi-bold (600)
  * Color: Light brown or theme color
- Optional decorative text: Hand-drawn style for accents only (not body text)

#### Layout & Composition:
- Circular card design (primary visual element):
  1. Illustration circles:
     - Diameter: 120-200px
     - Content: Custom scene illustration (NOT photos)
     - Border: 4-8px white border + thin outer ring (1-2px theme color)
     - Shadow: Soft drop shadow (0 4px 12px rgba(0,0,0,0.15))
  
  2. Text cards:
     - Background: White or light cream (90-95% opacity)
     - Corner radius: 12-20px
     - Padding: 20-32px
     - Arrangement: Staggered with illustration circles

- Layout principles:
  * Asymmetric balance (avoid strict grid)
  * Circles and rounded corners dominate (80% of elements)
  * Breathing space: Minimum 24px between elements
  * Visual flow: S-curve or zigzag reading path
  * Non-uniform sizing: Hero circle (200px), supporting circles (120-160px)

#### Visual Elements:
- Illustration style: Hand-drawn digital illustration
  * Aesthetic: Warm, organic, slightly imperfect (embraces humanity)
  * Color saturation: 40-70% (soft, not vibrant)
  * Line quality: Rounded, organic, variable width
  * Detail level: Medium (not overly simplified, not photo-realistic)
  * Themes: Camping scenes, landscapes, people engaged in activities, equipment/gear
  
- Scene compositions for circular frames:
  * Camping: Tent + campfire + starry sky
  * Landmarks: Mountain/canyon/lake with characteristic features
  * Activities: People stargazing, hiking, picnicking
  * Still life: Telescope, backpack, map, binoculars arranged artfully

- Decorative elements (use sparingly):
  1. Hand-drawn icons:
     - Style: Doodle/sketch aesthetic
     - Themes: Moon phases, stars, compass, map markers
     - Use: Bullet points, dividers, accent elements
     - Size: 32-64px
  
  2. Decorative lines/paths:
     - Type: Dotted lines, wavy curves, simple arrows
     - Width: 2-4px
     - Color: Theme color at 50% opacity
     - Purpose: Connect related content, guide eye flow
  
  3. Texture overlays (very subtle):
     - Paper texture (5-10% opacity)
     - Watercolor wash effects (background areas)
     - Canvas grain (subtle throughout)

- Icon style:
  * Type: Flat illustration icons (friendly, colorful)
  * Aesthetic: Rounded, warm, accessible
  * Size: 48-96px
  * Examples: Crescent moon, tent, red-light headlamp, folding chair, backpack

#### Mood & References:
- Visual inspiration:
  * Airbnb Experiences page illustrations
  * Duolingo app interface style
  * Headspace meditation app visuals
  * National Geographic Kids magazine

- Emotional tone:
  * Friendly without being childish
  * Adventurous without being extreme
  * Educational without being preachy
  * Relaxed without being sloppy
  * ""Weekend family outing"" vibe

#### Key Features:
1. **Illustration-first**: Every major content point has custom illustration
2. **Circle dominant**: 80% of visual elements use circles or rounded corners
3. **Warm palette**: Earth tones + soft accent colors
4. **Organic feel**: Embrace slight imperfections, avoid perfect alignment
5. **Layered depth**: Illustration cards in foreground, decorative elements in background
6. **High readability**: Large fonts, generous line spacing, short paragraphs
7. **Narrative flow**: Visual guides create story-like reading experience

#### Avoid:
- ❌ Photography collages (must use illustrations)
- ❌ Cold colors or high-tech aesthetics (keep warm and approachable)
- ❌ Dense information blocks (need whitespace)
- ❌ Formal academic layouts (avoid rigid structure)
- ❌ Complex data charts (simplify to icons + numbers)
- ❌ Heavy decorative borders (simple circles are enough)
- ❌ Pure black text (use warm brown tones)
- ❌ Serious corporate tone (maintain friendly accessibility)

#### Illustration Specifications:
- **Scene Lighting**: Warm natural light (golden hour or soft daylight)
- **Color Harmony**: Analogous color schemes within each illustration
- **Perspective**: Slight elevated view (not flat, not extreme angle)
- **Character style** (if people included): Simple, friendly, diverse, engaged in activity
- **Background treatment**: Simplified but recognizable (not photo-detailed)
- **Foreground focus**: Main subject clear and prominent
- **Edge treatment**: Soft edges where illustration meets circle border

REMEMBER: This is ""Editorial Illustrated"" - professional storytelling through custom artwork, not clipart or stock photos. Every illustration should feel like it was specifically created for this content.";

    // Note: Kids domain renamed to Family domain - see GetFamilyRenderingInstructions above

    private string GetPlayfulLearningStyle() => @"
### PLAYFUL LEARNING STYLE (Kids Activities & How-To)

#### Color Palette:
- Background: Soft gradient appropriate to topic (sky blue to cream for outdoor, warm yellow to peach for indoor, soft green to mint for nature)
- Primary: Bright saturated color for emphasis (#FF5252 red, #2196F3 blue, #4CAF50 green - choose one per design)
- Secondary: Complementary bright colors (orange #FF9800, purple #9C27B0, teal #00BCD4)
- Accent: Soft pastels for backgrounds (#FFF9C4 pale yellow, #E1F5FE pale blue, #F3E5F5 pale purple)
- Text: Deep charcoal (#2C3E50) for maximum readability on light backgrounds

#### Typography:
- Headers: Bold rounded sans-serif (Baloo, Nunito, Quicksand) 48-60pt with slight letter spacing
- Body: Clean friendly sans-serif (Source Sans, Poppins) 18-22pt, optimized for young readers
- Step numbers: Extra-bold 72-96pt in circular badges with high contrast
- Callouts: Slightly playful sans-serif (Comic Neue, Patrick Hand) for tips and fun facts

#### Layout & Composition:
- Best paired with DUAL-PATH or FLOW layouts
- Clear visual hierarchy: hero illustration (250-300px circle) → numbered steps → supporting tips
- Chunky content cards (min 200px width) with rounded corners (24px radius)
- Flowing connectors between steps using contextual icons (arrows, footprints, stars, checkmarks)
- Footer band for adult/teacher guidance with distinct visual treatment

#### Visual Elements:
- Friendly illustrated characters showing diverse representation
- Context-appropriate icons: science (beakers, magnifying glass), craft (scissors, glue), cooking (whisk, bowl), outdoor (compass, binoculars)
- Sticker-style badges for achievements, safety notes, or key concepts
- Motion lines showing action and sequence
- Chunky numbered badges (80-100px) with double outline for tactile appearance
- Speech bubbles for tips, warnings, or encouraging comments

#### Mood & References:
- PBS Kids educational content, Sesame Street Workshop materials
- Museum children's exhibits, National Geographic Kids spreads
- Encouraging coach energy, celebrates curiosity and trying
- Safe, inclusive, builds confidence through clear guidance

#### Key Features:
1. Large hero illustration immediately establishes what activity is about
2. Every step has: number badge + icon + short instruction (≤10 words) + supporting illustration
3. Safety or adult-help callouts in distinct colored speech bubbles
4. Visual flow uses contextual connecting elements (not generic arrows)
5. Background with subtle theme-appropriate texture or pattern (10-20% opacity)

#### Avoid:
- Small fonts or thin strokes that reduce readability
- Overly saturated backgrounds that compete with content
- Scary, violent, or mature themes
- Realistic medical/injury depictions
- Corporate sterility or academic formality
- Any non-English text or decorative foreign scripts";

    private string GetEducationalStoryStyle() => @"
### EDUCATIONAL STORY STYLE (Kids Learning Through Narrative)

#### Color Palette:
- Background: Warm textured paper (#FFF9EE cream, #F5F5DC beige, #FDF6E3 parchment)
- Primary: Rich story colors (deep blue #3D7DFF, forest green #6CCF64, golden yellow #FFD447)
- Accent: Jewel tones for emphasis (amethyst #A259FF, ruby #E53935, emerald #43A047)
- Card backgrounds: Soft white (#FFFFFF) or cream with subtle texture
- Shadows: Soft colored shadows (#E1D7FF lavender, #FFE0B2 peach) for layered paper effect

#### Typography:
- Titles: Friendly bold fonts (Fredoka, Chewy, Baloo) 52-68pt, slightly playful but readable
- Section headers: Rounded sans-serif (24-32pt) with icon badges or decorative prefix
- Body text: Large, clear sans-serif 18-22pt (#1A1A1A) for young reader accessibility
- Narrative callouts: Slightly handwritten feel (Patrick Hand, Kalam) for story elements, quotes, or discoveries

#### Layout & Composition:
- Works with FLOW, DUAL-PATH, or RADIAL layouts
- Hero section styled as open storybook or illustrated scene setting context
- Content panels as ""pages"" with subtle book-like treatments (soft shadows, slight rotation)
- Story progression markers (chapter numbers, scene badges) guide narrative flow
- Decorative connectors: ribbons, pennant flags, illustrated paths between sections
- Footer styled as notebook margin or story epilogue

#### Visual Elements:
- Illustrated characters and scenes (not photos) with expressive, engaging style
- Layered collage aesthetic: cut-paper effect with 3-4 visual depth layers
- Icon badges styled as tangible objects: crayons, stamps, stickers, washi tape
- Educational overlays: graph paper for math, lined paper for writing, map elements for geography
- Frame variations: polaroid borders, washi tape corners, paper clips, pushpins
- Visual metaphors appropriate to topic (science: lab notebook, history: scroll, art: canvas)

#### Mood & References:
- Children's museum interactive exhibits, Highlights magazine educational spreads
- PBS educational programming aesthetic, Scholastic classroom materials
- Story-driven learning: information embedded in engaging narrative
- Warm, encouraging tone that celebrates learning as adventure

#### Key Features:
1. Narrative structure: information presented as story with beginning, middle, end
2. Large, friendly numbers in decorative badges anchor each learning point
3. ""Did you know?"" or ""Try this!"" callouts in distinct decorative boxes
4. Safety or supervision notes in ribbon banners with appropriate icons
5. Visual texture throughout (paper grain, watercolor washes, crayon shading) at subtle opacity
6. Section transitions use thematic decorative elements, not generic dividers

#### Avoid:
- Serious academic tone or formal language
- Thin or script-heavy fonts that reduce readability
- Dense text blocks without visual breaks
- Commercial/marketing aesthetic (stay educational)
- Dark or moody color palettes
- Realistic violence, injury, or age-inappropriate content
- Non-English decorative text or foreign language flourishes";


    // Note: Tech domain renamed to Technology domain - see GetTechnologyRenderingInstructions above

    private string GetCyberpunkDarkStyle() => @"
### CYBERPUNK DARK STYLE (Tech)

#### Color Palette:
- Background: Near-black (#0D1117) or very dark navy (#0A0E27)
- Primary neon: Cyan (#00FFFF), magenta (#FF00FF), electric green (#39FF14)
- Secondary: Deep purple (#6B2C91), electric blue (#0080FF)
- Glow colors: Same as primary but with blur/glow effects
- Text: White (#FFFFFF) with subtle cyan tint for screens

#### Typography:
- Headlines: Futuristic geometric sans-serif, 48-72pt, wide letter spacing
- Tech labels: Monospace font (JetBrains Mono, Fira Code), 14-16pt
- Body: Clean geometric sans-serif, 16-18pt, crisp rendering
- Glowing text effects on key words (neon sign aesthetic)
- Uppercase for headers, lowercase for body
- Digital/terminal aesthetic

#### Layout & Composition:
- Layered depth with glowing panels floating in dark space
- Holographic UI elements with semi-transparency (20-30% opacity layers)
- Perspective grids in background (vanishing point perspective)
- Asymmetric tech panels with angled edges (45-degree cuts)
- Scanline effects or subtle noise texture on background

#### Visual Elements:
- 3D isometric tech icons (30-degree angle, glowing edges)
- Circuit board patterns in background (thin glowing lines)
- Hexagonal grids and geometric patterns
- Glowing nodes and connection lines (particle effects)
- Data stream visualizations (flowing particles)
- Holographic interface mockups
- Neon outline icons (2-3px stroke with glow)

#### Mood & References:
- Futuristic, high-tech, cyberpunk/sci-fi aesthetic
- Think: Blade Runner UI, Tron, cyberpunk 2077, hacker interfaces
- Digital dystopia meets cutting-edge technology
- Night-time neon city vibes

#### Key Features:
- Neon glow effects everywhere (outer glow, 10-20px, 80% opacity)
- Dark mode supreme - embrace the darkness
- Tech-heavy visuals (circuits, grids, holograms)
- Layered depth with transparency
- Motion and energy through glowing elements

#### Avoid:
- Warm colors (oranges, yellows, earth tones)
- Organic shapes or natural elements
- Light backgrounds or pastels
- Friendly approachable design
- Minimalism - embrace the complexity";

    private string GetAcademicCleanStyle() => @"
### ACADEMIC CLEAN STYLE (Tech/Academic)

#### Color Palette:
- Background: Pure white (#FFFFFF) or very light gray (#F9FAFB)
- Text: True black (#000000) for maximum readability
- Accent: Single academic color - deep blue (#1E3A8A) OR forest green (#065F46)
- Chart colors: Grayscale with single accent color for emphasis
- Minimal color - prioritize clarity over aesthetics

#### Typography:
- Body text: Clean serif for readability (Georgia, Merriweather), 14-16pt
- Headlines: Bold sans-serif (Arial, Helvetica), 24-32pt, clear hierarchy
- Technical terms: Monospace (Courier New, Consolas), 14pt
- Captions: Small serif, 11-12pt, gray color
- Footnotes: Very small serif, 9-10pt
- Line spacing optimized for long-form reading (1.6-1.8x)

#### Layout & Composition:
- Clear hierarchical structure (1, 1.1, 1.2 numbering system)
- Generous margins (2.5cm equivalent, ~15-20% width)
- Numbered sections and subsections
- Figure captions below images (""Figure 1: Description"")
- Footnote references and bibliography at bottom
- Two-column layout for body text if long-form

#### Visual Elements:
- Simple diagrams with clear labels and arrows
- Flowcharts with standard shapes (rectangles, diamonds, circles)
- Architectural drawings (clean lines, proper annotations)
- Clean line illustrations (technical drawing style)
- Tables with proper formatting (header row, borders)
- Equations and formulas properly formatted
- Citation numbers in brackets [1][2]

#### Mood & References:
- Scholarly, precise, authoritative without pretension
- Think: Academic papers, IEEE publications, arXiv papers, textbooks
- Education and knowledge dissemination primary goal
- Timeless design - will look correct in 10 years

#### Key Features:
- Clarity and readability above all else
- Consistent formatting and structure
- Proper citations and references
- Information density appropriate for academic audience
- Professional but not designed - content first

#### Avoid:
- Flashy effects or trendy design
- Decorative elements without purpose
- Vibrant colors or gradients
- Commercial aesthetics or marketing feel
- Unnecessary graphics or illustrations";

    private string GetSpaceCosmicStyle() => @"
### SPACE/COSMIC STYLE (Tech/Science/Astronomy)

**Domain**: Science, Astronomy, Space exploration, National Parks (stargazing), Astrophotography
**Mood**: Mysterious, awe-inspiring, exploratory, grand cosmic perspective

#### Color Palette:
- Background: Deep space gradient
  * Base: Navy to purple gradient (#0A1628 → #1A0B2E → #16213E)
  * Nebula overlay: Radial gradient with purple/blue hues (30-40% opacity)
- Primary colors:
  * Starlight gold: #FFD700 (titles, highlights, important text)
  * Cosmic blue: #4A90E2 (headings, data points, icons)
  * Nebula purple: #9D4EDD (accents, decorative elements)
- Accent colors:
  * Aurora green: #00FF88 (call-to-action, special highlights)
  * Galaxy pink: #FF006E (secondary accents, emphasis)
- Text:
  * Bright white: #FFFFFF (main titles, high contrast)
  * Dim white: rgba(255,255,255,0.85) (body text)
  * Caption gray: rgba(255,255,255,0.6) (labels, metadata)

#### Typography:
- Headlines: Futuristic geometric sans-serif (think Orbitron, Exo 2, Space Grotesk)
  * Size: 48-72pt for main title
  * Weight: Bold (700)
  * Letter spacing: 2-4% (wide tracking for tech feel)
  * Effect: Subtle glow (text-shadow: 0 2px 20px rgba(255,215,0,0.5))
- Subheadings: Clean geometric sans-serif (Inter, DM Sans)
  * Size: 24-36pt
  * Weight: Semi-bold (600)
  * Color: Cosmic blue or starlight gold
- Body text: Regular geometric sans-serif
  * Size: 14-18pt
  * Weight: Regular (400) or Medium (500)
  * Line height: 1.6x
  * Color: 85% white for readability on dark background
- Labels/metadata: Uppercase sans-serif
  * Size: 11-14pt
  * Weight: Semi-bold (600)
  * Letter spacing: 5-8%

#### Layout & Composition:
- Layered depth (5 distinct Z-layers):
  1. Background: Deep space gradient + star particles (500-1000 tiny dots)
  2. Nebula layer: Blurred colorful light clouds (Gaussian blur 40-60px, 30% opacity)
  3. Decorative layer: Constellation lines, orbital paths, planet icons
  4. Content layer: Illustration cards, text blocks, data visualizations
  5. Foreground: Glow effects, lens flares, floating particles

- Spatial depth illusion:
  * Near elements: Large size, high contrast, sharp edges
  * Mid elements: Medium size, soft shadows
  * Far elements: Small size, blurred (30-50% opacity), faded

- Card/module styling:
  * Background: Semi-transparent dark rgba(26,11,46,0.8)
  * Border: 1px glowing border (box-shadow: 0 0 20px rgba(74,144,226,0.6))
  * Corner radius: 16-24px
  * Padding: 24-32px
  * Hover effect: Elevated shadow (0 8px 32px rgba(0,0,0,0.6))

#### Visual Elements:
- Illustration style: Digital painted illustration (semi-realistic)
  * Themes: Observatory + galaxy, national park landmarks + starry sky, 
    telescope + planets, camping scene + aurora borealis
  * Detail level: Medium-high (painterly, not photorealistic)
  * Lighting: Dramatic with strong light source (usually from celestial object)

- Decorative particles and effects:
  1. Star particles:
     - Size: 1-4px randomly distributed
     - Density: 50-100 stars per 1000 square pixels
     - Opacity variation: 40-100% for twinkling effect
  
  2. Nebula clouds:
     - Position: Canvas corners or behind main content
     - Colors: Purple (#9D4EDD), blue (#4A90E2), pink (#FF006E) gradients
     - Blur: 40-80px Gaussian blur
     - Opacity: 20-40%
  
  3. Orbital paths/connection lines:
     - Style: Dashed curves or ellipses
     - Color: Cosmic blue or starlight gold at 30% opacity
     - Width: 2-3px
     - Use: Connect related content modules
  
  4. Light effects:
     - Lens flare: Star-burst pattern at bright points
     - Glow: 20-40px soft light around important elements
     - Light rays: Radiating from focal points

- Icons and symbols:
  * Style: Line icons with gradient fill (gold → blue)
  * Themes: Telescope, moon phases, constellations, planets, rocket
  * Size: 64-128px
  * Treatment: Subtle outer glow

#### Mood & References:
- Mysterious and awe-inspiring (not scary or cold)
- Grand cosmic scale (think Carl Sagan's ""billions and billions"")
- Exploration and discovery spirit
- References:
  * NASA official posters and infographics
  * Interstellar movie visual style
  * Sky & Telescope magazine covers
  * Apple ""Shot on iPhone"" astrophotography series

Emotional tone: ""Look up and feel small but connected to something vast""

#### Key Features:
1. **Dark dominance**: 90% dark background, 10% bright accents
2. **Glow everywhere**: Text, borders, icons, connecting lines all have subtle luminescence
3. **Illustration-driven**: Every major content block has custom scene illustration
4. **Rich layering**: Minimum 5 visual depth layers
5. **Tech meets nature**: Hard sci-fi elements (grids, data) + natural beauty (stars, mountains)
6. **Circular/orbital motifs**: Round frames, circular layouts, orbital paths

#### Avoid:
- ❌ Pure black background (use deep blue-purple gradients instead)
- ❌ Over-saturated neon colors (maintain elegant cosmic palette)
- ❌ Cartoon-style illustrations (need semi-realistic digital painting)
- ❌ Flat design without depth (must have layers and glow effects)
- ❌ Text-heavy layouts (prioritize visual storytelling)
- ❌ Cold sterile tech aesthetic (add warmth through exploration theme)
- ❌ Cluttered space (even cosmos needs breathing room - let darkness speak)";

    private string GetNotionIsometricStyle() => @"
### NOTION FRIENDLY ILLUSTRATION STYLE (Tech/Productivity)

**Design Philosophy**:
Create friendly, approachable illustrations with subtle 3D depth - think Notion marketing pages, 
Slack blog graphics, or modern SaaS landing pages. Prioritize clarity and warmth over technical precision.

**Core Aesthetic**:
- Semi-flat 2.5D style (simplified isometric, NOT strict 3D)
- Light, clean backgrounds with optional subtle color zones
- Friendly geometric shapes with soft edges
- Pastel colors with good contrast
- Inviting and accessible, not intimidating

#### Color Palette:
- **Background**: Light gradient (#FDFEFF → #F0F9FF) or solid white (#FFFFFF)
  * Optional color zones for narrative sections: beige #FFF8E7, light blue #EBF5FB, mint #E8F9F5
- **Organic shapes**: Soft pastels with gentle gradients
  * Coral/pink: #FFB5B5 → #FFD5D5
  * Mint green: #A8E6E0 → #C5F2ED
  * Butter yellow: #FFEAA7 → #FFF5D0
  * Sky blue: #B8E0FF → #D4EDFF
- **UI elements**: White cards with subtle shadows
  * Card surfaces: #FFFFFF or #FAFBFC
  * Very light edges: #F0F2F5 (minimal depth indication)
- **Text**: Dark charcoal (#2C3E50) for titles, medium gray (#5A6C7D) for body
- **Shadows**: Single soft shadow
  * 0-4px offset, 20px blur, 30% opacity, color #1A2332

#### Typography:
- Friendly sans-serif fonts: Poppins, Quicksand, Nunito, Circular
- Headlines: Bold or SemiBold, 36-48pt, warm and inviting
- Body text: Regular weight, 16-18pt, excellent readability
- Labels: Medium weight, 12-14pt, subtle and supportive
- Letter spacing: Slightly increased for friendliness (+0.02em)
- Line height: Generous (1.6-1.8) for easy reading

#### Layout & Composition:
- **Simplified 2.5D depth**: Gentle layering, not strict isometric
  * Use subtle z-axis stacking (foreground/midground/background)
  * Approximate 25-35° angles (relaxed, not exact)
  * 2 depth layers sufficient (not mandatory 3)
- **Light and airy spacing**: Generous white space, not crowded
- **Floating elements**: Cards and shapes appear to gently hover
- **Organic flow**: Curved paths, rounded shapes, natural movement
- **Balanced composition**: Asymmetrical but harmonious
- **Color zones** (if narrative content): Divide canvas into 2-3 zones with different background tints

#### Visual Elements:
- **Cards with minimal depth**:
  * Primarily show front face (white or light colored)
  * Optional subtle top/side edges (2-6px visible, very light gray)
  * Rounded corners: 12-20px radius
  * Soft shadow underneath for floating effect
  * Clean, friendly content display

- **Organic blob shapes** (decorative):
  * Soft irregular rounded forms (not perfect circles)
  * Light gradient fills to suggest gentle volume
  * Float behind or beside main content
  * Complement, don't overwhelm
  * Semi-transparent overlays possible (80-95% opacity)

- **Symbolic illustrations** (theme-based):
  * Simplified iconic objects matching content theme:
    - Building/construction: LEGO blocks, toy houses, tools
    - Journey/growth: Rockets, stairs, paths, mountains
    - Culture/tradition: Simplified temples, books, artifacts
  * 2.5D style with 6-10px depth (minimal extrusion)
  * Friendly and approachable, not technical
  * Pastel colors matching overall palette

- **UI mockup elements** (if showing interfaces):
  * Simplified screen/window previews
  * Light borders, subtle shadows
  * Clean and minimal (not overly detailed)
  * Emphasize key features with highlights

- **Connection elements**:
  * Curved dotted lines or gentle arrows
  * Soft colors from the palette
  * Can weave gently between layers
  * 2-3px stroke weight, rounded caps

- **Lighting**: Very gentle and subtle
  * Soft light from top-left (no harsh highlights)
  * Minimal shading (just enough to show form)
  * Ambient, diffused lighting (not dramatic)

#### Mood & References:
- **Visual Inspiration**: Notion founding story infographic, Slack illustrations, 
  Dropbox Paper marketing graphics, Asana product pages
- Friendly and approachable, never intimidating
- Modern productivity aesthetic with warmth
- Balanced between playful and professional
- Optimistic and inviting tone
- Hand-crafted feel with digital polish

#### Step-by-Step Process:

1. **Establish Background**:
   - Light gradient or solid light color
   - Optional: divide into 2-3 color zones for narrative structure
   - Keep very subtle (10-20% color saturation maximum)

2. **Position Main Content Areas**:
   - Identify 2-3 main content sections
   - Place larger cards/shapes in foreground
   - Smaller supporting elements in background
   - Gentle z-axis separation (50-100px conceptual distance)

3. **Add Cards and Content Containers**:
   - White or very light cards with rounded corners
   - Minimal depth (2-6px visible side if needed)
   - Soft shadows for floating effect
   - Clear hierarchy through size and position

4. **Place Organic Decorative Shapes**:
   - Soft pastel blobs in background
   - Behind or beside main content (not covering)
   - Gentle gradient fills
   - Complement the overall composition

5. **Add Symbolic Illustrations** (if theme-appropriate):
   - Simplified iconic objects matching content
   - Friendly, approachable style
   - 2.5D with minimal depth
   - Pastel colors from palette

6. **Apply Typography**:
   - Friendly sans-serif fonts
   - Dark text on light backgrounds
   - Generous spacing and hierarchy
   - Ensure excellent readability

7. **Add Subtle Shadows**:
   - Single soft shadow per element
   - Small offset (0-4px), wide blur (15-20px)
   - 20-30% opacity
   - Consistent direction (down-right)

8. **Final Polish**:
   - Check color harmony (all pastels work together)
   - Verify readability (sufficient contrast)
   - Ensure friendly, inviting feel
   - Balance between elements (not too busy)

#### Key Principles:
✓ Light backgrounds (white to light blue)
✓ Soft pastel colors
✓ Simplified 2.5D (relaxed angles, minimal strictness)
✓ Friendly typography (rounded, warm)
✓ Generous white space
✓ Approachable and inviting mood
✓ Single soft shadows
✓ Optional narrative color zones
✓ Symbolic theme-based illustrations

#### Avoid:
- ❌ Dark backgrounds
- ❌ Strict geometric 3D with exact angle requirements
- ❌ Harsh shadows or dramatic lighting
- ❌ Technical or cold aesthetic
- ❌ Cluttered composition
- ❌ Corporate stiffness
- ❌ Overly saturated colors
- ❌ Complex multi-shadow systems

#### Validation:
✓ Does it feel friendly and inviting?
✓ Are colors soft and harmonious?
✓ Is typography warm and readable?
✓ Does it have gentle depth (not flat, not overly 3D)?
✓ Would it fit on a Notion or Slack marketing page?
✓ Is the overall mood optimistic and approachable?

This style adapts to any layout (TIMELINE, FLOW, HERO-FOCUS, etc.) by applying these visual characteristics 
to whatever structural organization the content requires.";

    // ==================== History Domain Variants ====================

    private string GetHistoryRenderingInstructions(string variant)
    {
        return variant switch
        {
            "timeline-classic" => "Classic historical timeline with vintage textures and period-appropriate typography. Use sepia tones and archival aesthetics.",
            "documentary-style" => "Documentary film aesthetic with cinematic framing. Bold historical imagery with modern clean typography for contrast.",
            "cultural-heritage" => "Cultural preservation aesthetic with museum-quality presentation. Rich earth tones and traditional design elements.",
            _ => "Historical content with balanced modern-classic styling. Focus on accuracy and gravitas."
        };
    }

    // ==================== Science Domain Variants ====================

    private string GetScienceRenderingInstructions(string variant)
    {
        return variant switch
        {
            "academic-journal" => "Academic publication style with clean diagrams and scientific precision. Use neutral colors and clear data visualization.",
            "modern-research" => "Contemporary research aesthetic with bold colors and infographic elements. Balance scientific rigor with visual engagement.",
            "lab-visual" => "Laboratory and experimental aesthetic. Use clinical whites, lab equipment imagery, and precise technical illustrations.",
            _ => "Scientific content with clear visualization and educational focus."
        };
    }

    // ==================== Nature & Space Domain Variants ====================

    private string GetNatureSpaceRenderingInstructions(string variant)
    {
        return variant switch
        {
            "cosmic-wonder" => "Deep space aesthetic with nebula colors and astronomical imagery. Use dark backgrounds with vibrant cosmic phenomena.",
            "outdoor-adventure" => "Outdoor and nature adventure style with earthy tones and natural textures. Emphasize environmental beauty and exploration.",
            "nature-minimal" => "Minimalist nature aesthetic with organic shapes and natural color palettes. Clean, peaceful, and contemplative design.",
            _ => "Nature and space content with immersive visual storytelling."
        };
    }

    // ==================== Social Domain Variants ====================

    private string GetSocialRenderingInstructions(string variant)
    {
        return variant switch
        {
            "community-vibrant" => "Community-focused design with diverse representation and warm, inclusive colors. Emphasize connection and shared experiences.",
            "activism-bold" => "Bold activist aesthetic with strong contrasts and impactful messaging. Use energetic colors and powerful imagery.",
            "documentary-social" => "Social documentary style with authentic photography and empathetic storytelling. Focus on human connections.",
            _ => "Social content with engaging and inclusive visual language."
        };
    }

    // ==================== Family Domain Variants ====================

    private string GetFamilyRenderingInstructions(string variant)
    {
        return variant switch
        {
            "playful-learning" => GetPlayfulLearningStyle(),
            "educational-story" => GetEducationalStoryStyle(),
            _ => "Family-friendly content with warm, accessible design. Focus on clarity and engagement for all ages."
        };
    }

    // ==================== Technology Domain Variants ====================

    private string GetTechnologyRenderingInstructions(string variant)
    {
        return variant switch
        {
            "cyberpunk" => GetCyberpunkDarkStyle(),
            "academic" => GetAcademicCleanStyle(),
            "notion" => GetNotionIsometricStyle(),
            "cosmic" => GetSpaceCosmicStyle(),
            _ => GetCyberpunkDarkStyle()
        };
    }

    /// <summary>
    /// Default rendering instructions for cases where domain is not recognized.
    /// </summary>
    private string GetDefaultRenderingInstructions() => @"
### DEFAULT MODERN STYLE

#### General Guidelines:
- Use contemporary design trends (2024-2025 aesthetics)
- Balance visual appeal with information clarity
- Maintain clear hierarchy and readability
- Apply appropriate color psychology for the topic
- Ensure all text is properly rendered in all languages

#### Layout:
- Follow the layout type specified in the description (FLOW/TIMELINE/MOSAIC/etc.)
- Use asymmetry for visual interest but maintain balance
- Create clear visual flow to guide the reader's eye
- Generous whitespace for breathing room

#### Typography:
- Bold headlines for clear hierarchy
- Clean sans-serif for modern feel
- Mix font weights for visual rhythm
- Ensure proper line spacing and readability

#### Colors:
- Cohesive color palette (3-5 colors max)
- Consider color psychology for the topic
- High contrast for readability
- Use color to create focal points

#### Visual Elements:
- Modern flat or semi-flat design
- Consistent icon style
- Clean data visualizations if needed
- Subtle shadows and depth effects

#### Quality:
- Professional polish
- Current design trends
- Portfolio-ready execution";
}

public record StudioGeneration
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required string Title { get; init; }
    public required string Status { get; set; } // generating, completed, failed
    public string? Content { get; set; }
    public string? ImagePath { get; set; } // Relative path to generated image (for infographics)
    public string? ImagePrompt { get; set; } // Full prompt sent to image generation model (for infographics)
    public DateTime CreatedAt { get; init; }
    public DateTime? CompletedAt { get; set; }
}

public record GenerateRequest(string NotebookId, string Type, string? CustomPrompt = null);
public class MindmapPayload
{
    public List<MindmapNode> Nodes { get; set; } = new();
    public List<MindmapEdge>? Edges { get; set; }
}

public class MindmapNode
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public string? Summary { get; set; }
    public int? Depth { get; set; }
}

public class MindmapEdge
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string? Relation { get; set; }
}
