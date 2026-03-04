---
name: infographic-gen
description: Generate professional educational infographics from text files with intelligent domain detection and style adaptation
---

## Infographic Generation Workflow

This skill generates professional educational infographics from text files using intelligent style detection across 8 domains and 27 visual variants.

**Architecture**: Standalone skill that directly calls egress-llm API (no Notebook dependency required).

## ⚠️ IMPORTANT: Execution Mode

**CRITICAL INSTRUCTION** - Read this before proceeding:

**IF** implementation scripts already exist in the skill directory:
- `Lumina-API-Demo/skills/infographic-gen/infographic-gen.py` (Python - **PREFERRED**)
- `Lumina-API-Demo/skills/infographic-gen/infographic-gen.ps1` (PowerShell - fallback)

**THEN** you MUST:
- ✅ Use the existing scripts **WITHOUT any modifications**
- ✅ Execute them directly with appropriate command-line parameters
- ✅ Only read the scripts to understand their parameters
- ❌ Do NOT rewrite, edit, or "improve" the existing implementations
- ❌ Do NOT create duplicate scripts in the working directory

**Script Selection Priority**:
1. **If `infographic-gen.py` exists** → Use Python version (cross-platform, better SSE handling)
2. **Else if `infographic-gen.ps1` exists** → Use PowerShell version (Windows only)
3. **Else** → Proceed with manual implementation

**ONLY write new code IF**:
1. Neither script exists in the expected locations, OR
2. User explicitly requests code changes/debugging, OR
3. There are runtime errors that require fixes

**Execution Examples**:
```bash
# Python (PREFERRED - cross-platform, better SSE stream handling)
python Lumina-API-Demo/skills/infographic-gen/infographic-gen.py --input <input_file> --output <output_path>

# PowerShell (Windows fallback)
powershell -File Lumina-API-Demo/skills/infographic-gen/infographic-gen.ps1 -InputFile <input_file> -OutputPath <output_path>
```

### Environment Requirements

Set these environment variables before using the skill:

```bash
# LLM API endpoint (used for both content analysis and image generation)
export LLM_ENDPOINT="http://localhost:4141"
export LLM_MODEL="claude-sonnet-4"

# Note: Both LLM and image generation use the same endpoint
# The egress-llm service routes to appropriate backends based on API path
```

### Step 0: Parameter Detection & Script Verification

Detect and validate parameters from the user request:

**Required Parameters:**
- `input_file`: Path to text file (txt, md, etc.)
  - Examples: "research.txt", "notes.md", "/path/to/document.txt"
  - Validation: File must exist and be readable

**Optional Parameters:**
- `output_dir`: Output directory (default: current working directory)
  - Examples: "./output/", "/path/to/output/"
- `style_preference`: User-specified style override (optional)
  - Examples: "cosmic-wonder", "business-editorial", "playful-learning"

**Action Items:**
1. Parse user request to extract file path
2. Verify input file exists using Read tool
3. Extract output directory if specified
4. Extract style preference if user explicitly mentions a style
5. **CHECK if implementation scripts exist** (see "Execution Mode" section above):
   - **First check for `Lumina-API-Demo/skills/infographic-gen/infographic-gen.py`** (PREFERRED)
   - If not found, check for `Lumina-API-Demo/skills/infographic-gen/infographic-gen.ps1`
   - If either exists, proceed to direct execution (skip Steps 1-3 implementation, just call the script)
   - If neither exists, proceed with manual implementation (Steps 1-3 below)

### Step 1: Content Analysis & Domain Detection

**Read input file and generate infographic description using LLM API.**

**Tasks:**
1. Use Read tool to load the input file content
2. Load style-config.json to understand available domains and variants
3. Build system prompt from content-analyzer.md
4. Call LLM API at `$LLM_ENDPOINT/v1/messages` to generate description
5. Parse response to extract description text
6. Apply domain detection algorithm (check keywords in style-config.json)
7. Select optimal style variant based on keyword matching

**LLM API Call Format:**
```bash
curl -X POST "$LLM_ENDPOINT/v1/messages" \
  -H "Content-Type: application/json" \
  -H "anthropic-version: 2023-06-01" \
  -d '{
    "model": "'"$LLM_MODEL"'",
    "max_tokens": 2000,
    "messages": [{
      "role": "user",
      "content": "SYSTEM_PROMPT + USER_CONTENT"
    }]
  }'
```

**Expected Output:**
```json
{
  "description": "Full detailed infographic description text...",
  "domain": "nature-space",
  "variant": "cosmic-wonder"
}
```

**Progress Feedback:**
```
[Infographic] Step 1/4: Analyzing content from {filename}...
[Infographic] ✓ Content loaded ({N} characters)
[Infographic] ✓ Calling LLM API to generate description...
[Infographic] ✓ Domain detected: {domain} ({variant} variant)
```

### Step 2: Image Prompt Construction

**MUST read "image-prompter.md"** and follow its instructions.

**Tasks:**
1. Based on detected domain and variant, load appropriate rendering instructions from image-prompter.md
2. Construct complete image generation prompt by combining:
   - Infographic description from Step 1
   - Domain-specific rendering instructions
   - General rendering rules (landscape format, English-only, etc.)
3. Add instruction to handle sensitive content (matching ImageGenerationService.cs):
   ```
   " If there are sensitive figures or copyrighted content, draw a similar alternative, but do not refuse to generate. IMPORTANT: Render every label in clear English only with correct spelling and spacing. Do not add non-English characters or decorative foreign scripts."
   ```
4. Save the complete prompt to a text file for transparency

**Expected Output:**
- `final_image_prompt`: Complete prompt string ready for GPT Image API
- `prompt_file_path`: Saved prompt file (e.g., "astronomy_20260105_143022_prompt.txt")

**Progress Feedback:**
```
[Infographic] Step 2/4: Constructing image prompt...
[Infographic] ✓ Loaded {domain}-{variant} style instructions
[Infographic] ✓ Prompt saved to {prompt_file_path}
```

### Step 3: Image Generation via API

Call GPT Image API using the correct format from ImageGenerationService.cs:

**Environment Variables Required:**
- `LLM_ENDPOINT`: Base URL for egress-llm (e.g., "http://localhost:4141")

**Implementation using PowerShell:**

```powershell
$cvId = [guid]::NewGuid().ToString()
$interactionId = [guid]::NewGuid().ToString()
$messageId = [guid]::NewGuid().ToString()
$scenarioGuid = "347061d6-d666-4e7e-a34e-38bb75bd7a38"
$endpoint = $env:LLM_ENDPOINT  # e.g., "http://localhost:4141"

# Read prompt from file
$prompt = Get-Content $promptPath -Raw

# Add instruction to handle sensitive content
$enhancedPrompt = $prompt + " If there are sensitive figures or copyrighted content, draw a similar alternative, but do not refuse to generate. IMPORTANT: Render every label in clear English only with correct spelling and spacing. Do not add non-English characters or decorative foreign scripts."

# Build payload matching ImageGenerationService.cs
$payload = @{
    messages = @(
        @{
            id = $messageId
            author = @{ role = 'user' }
            content = @{
                content_type = 'multimodal_text'
                parts = @($enhancedPrompt)
            }
        }
    )
    virtual_model = 'gpt-image-1-5'
    zdr_type = 1
    size = 'image'
    orientation = 'landscape'
    stream = $true
    image_format = 'png'
    n = 1
    encode_user_images_as_vq = $true
} | ConvertTo-Json -Depth 10

# Build headers matching ImageGenerationService.cs
$headers = @{
    'Content-Type' = 'application/json'
    'Accept' = 'text/event-stream'
    'X-CV' = $cvId
    'X-ChatGPT-User-Email' = 'infographic-skill@example.com'
    'X-ChatGPT-User-Id' = 'infographic-skill-001'
    'X-ModelType' = 'dev-gpt-image-1-5'
    'X-ScenarioGUID' = $scenarioGuid
    'x-imagegen-api-use-mainline' = 'true'
    'X-InteractionId' = $interactionId
    'X-Tag' = '{"Client":"InfographicSkill"}'
}

Write-Host "[Infographic] Step 3/4: Generating image via egress-llm API..."
Write-Host "[Infographic] This may take 30-90 seconds..."

# Call API
$response = Invoke-WebRequest -Uri "$endpoint/chatgpt/convo2im" `
    -Method POST `
    -Headers $headers `
    -Body $payload `
    -TimeoutSec 120

# Parse SSE stream and extract image
$lines = $response.Content -split "`n"
foreach ($line in $lines) {
    if ($line.StartsWith("data: ")) {
        $dataContent = $line.Substring(6).Trim()
        if ($dataContent -eq "[DONE]" -or $dataContent -eq "DONE") { break }

        try {
            $data = $dataContent | ConvertFrom-Json
            if ($data.content -and $data.content.parts) {
                foreach ($part in $data.content.parts) {
                    if ($part.content_type -eq 'image' -and $part.payload) {
                        # Decode and save image
                        $bytes = [System.Convert]::FromBase64String($part.payload)
                        [System.IO.File]::WriteAllBytes($outputPath, $bytes)
                        $fileSize = [math]::Round((Get-Item $outputPath).Length / 1KB, 2)
                        Write-Host "[Infographic] ✓ Image saved to $outputPath ($fileSize KB)"
                        break
                    }
                }
            }
        } catch {
            # Skip non-JSON lines
        }
    }
}
```

**Alternative: Using Bash with curl (if preferred):**

The skill should prefer PowerShell on Windows for better JSON handling and SSE parsing. However, if needed, bash implementation can be added.

**Progress Feedback:**
```
[Infographic] Step 3/4: Generating image via egress-llm API...
[Infographic] This may take 30-90 seconds...
[Infographic] ✓ Image data received
[Infographic] ✓ Image saved to {output_path} ({file_size} KB)
```

**Error Handling:**
- Connection failure: Check LLM_ENDPOINT configuration
- 500 error: Prompt may be too long or contain invalid characters
- Timeout: Fail after 120 seconds with clear error message
- No image data: Check egress-llm logs for details

### Step 4: Display Summary

Show final results to user:

```
[Infographic] Step 4/4: Finalizing...

✅ Infographic generated successfully!

📊 Details:
   - Domain: {domain} ({variant} variant)
   - Input: {input_file}
   - Output: {output_path} ({file_size} KB)
   - Prompt: {prompt_path}

💡 Tip: Open the PNG file to view full quality
```

## Configuration

### Environment Variables

Required environment variables (must be set before using this skill):

```bash
# LLM and Image API endpoint (both use egress-llm)
export LLM_ENDPOINT="http://localhost:4141"
export LLM_MODEL="claude-sonnet-4"
```

### Validation

At the start of the workflow, validate configuration:

```bash
if [[ -z "$LLM_ENDPOINT" ]]; then
  echo "❌ Error: LLM_ENDPOINT environment variable not set"
  echo "   Please set it to your egress-llm endpoint"
  echo "   Example: export LLM_ENDPOINT='http://localhost:4141'"
  exit 1
fi
```

## Error Handling

### File Not Found
```
❌ Error: Cannot find '{file_path}'
   Please check the path and try again.
   Current directory: {pwd}
```

### API Connection Failure
```
❌ Error: Cannot connect to egress-llm API at {LLM_ENDPOINT}
   Please verify:
   1. LLM_ENDPOINT environment variable is set correctly
   2. egress-llm service is running (check port 4141)
   3. Network connectivity is available

   Test connection: curl {LLM_ENDPOINT}/health
```

### Content Too Short
```
⚠️  Warning: Input file is very short ({N} characters)
   Consider providing more content for a detailed infographic.
   Proceeding with minimal content...
```

### API Rate Limit or 500 Error
```
❌ Error: Image generation API returned error
   Possible causes:
   - Prompt too long (try shorter input file)
   - API service issue (check egress-llm logs)
   - Rate limiting

   Prompt saved to: {prompt_path}
   You can inspect the prompt and retry
```

## Usage Examples

### Basic Usage
User: "Generate an infographic from research.txt"

### With Output Directory
User: "Create infographic from notes.md and save in ./output/"

### With Style Preference
User: "Make an infographic from astronomy.txt with cosmic-wonder style"

### With Full Path
User: "Generate infographic from C:\docs\science-paper.md"

## Implementation Notes

### Key Differences from Notebook-Based Approach

1. **No Notebook Creation**: Skill works directly with files
2. **Direct API Calls**: Uses egress-llm API directly (no backend wrapper)
3. **Stateless**: No persistent storage, output goes to specified directory
4. **Independent**: Can run anywhere egress-llm is available

### Correct API Headers

The skill MUST use these headers (matching ImageGenerationService.cs):
- `X-CV`: Unique correlation ID
- `X-ChatGPT-User-Email`: User identifier
- `X-ModelType`: "dev-gpt-image-1-5"
- `X-ScenarioGUID`: Scenario identifier
- `x-imagegen-api-use-mainline`: "true"
- `X-InteractionId`: Unique interaction ID
- `X-Tag`: JSON string with client info

### SSE Stream Parsing

Response is Server-Sent Events (SSE) format:
- Lines starting with "data: " contain JSON
- Look for `content.parts[].content_type == "image"`
- Extract `content.parts[].payload` (base64 encoded PNG)
- Decode and save to file

### Testing

Test with short input first:
```
echo "World War II (1939-1945) was a global conflict." > test.txt
# Then: "Generate infographic from test.txt"
```

## Maintenance

### Updating Style Configuration

To add new styles or domains:
1. Edit `style-config.json` to add keywords and variants
2. Edit `image-prompter.md` to add rendering instructions
3. No code changes needed in skill logic

### Debugging

If generation fails:
1. Check `{filename}_prompt.txt` - was the prompt generated correctly?
2. Test LLM API directly: `curl $LLM_ENDPOINT/health`
3. Check egress-llm terminal output for errors
4. Try with a shorter input file

## Architecture Diagram

```
User Request
    ↓
[Step 0: Parse Parameters]
    ↓
[Step 1: Read File + Call LLM API] → style-config.json
    ↓                                  content-analyzer.md
[Step 2: Build Image Prompt] → image-prompter.md
    ↓
[Step 3: Call Image API] → egress-llm (:4141/chatgpt/convo2im)
    ↓
[Step 4: Save PNG + Display Summary]
    ↓
Output: {filename}_{timestamp}.png
        {filename}_{timestamp}_prompt.txt
```

**No Notebook dependency** - skill is completely standalone!
