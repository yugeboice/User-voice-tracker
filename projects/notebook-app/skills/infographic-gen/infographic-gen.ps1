<#
.SYNOPSIS
    Standalone infographic generation skill - No Notebook required!

.DESCRIPTION
    Generates professional educational infographics from text files using intelligent
    domain detection and style adaptation. Directly calls egress-llm API.

.PARAMETER InputFile
    Path to input text file (txt, md, etc.)

.PARAMETER OutputDir
    Output directory for generated infographic (default: current directory)

.PARAMETER StylePreference
    Optional style override (e.g., "cosmic-wonder", "business-editorial")

.PARAMETER LlmEndpoint
    LLM API endpoint (default: http://localhost:4141)

.PARAMETER LlmModel
    LLM model to use (default: claude-sonnet-4)

.EXAMPLE
    .\infographic-gen.ps1 -InputFile "solar_system.txt"

.EXAMPLE
    .\infographic-gen.ps1 -InputFile "business_report.md" -OutputDir "./output" -StylePreference "business-editorial"
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$InputFile,

    [Parameter(Mandatory=$false)]
    [string]$OutputDir = ".",

    [Parameter(Mandatory=$false)]
    [string]$StylePreference = "",

    [Parameter(Mandatory=$false)]
    [string]$LlmEndpoint = "http://localhost:4141",

    [Parameter(Mandatory=$false)]
    [string]$LlmModel = "claude-sonnet-4"
)

# Script directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Color output functions
function Write-Step {
    param([string]$Message)
    Write-Host "[Infographic] $Message" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "[Infographic] ✓ $Message" -ForegroundColor Green
}

function Write-Error-Custom {
    param([string]$Message)
    Write-Host "[Infographic] ✗ $Message" -ForegroundColor Red
}

function Write-Progress-Custom {
    param([string]$Message)
    Write-Host "[Infographic]   $Message" -ForegroundColor Gray
}

# === Step 0: Parameter Detection and Validation ===
Write-Step "Step 0/4: Validating parameters..."

# Validate input file
if (-not (Test-Path $InputFile)) {
    Write-Error-Custom "Input file not found: $InputFile"
    exit 1
}

$InputFile = Resolve-Path $InputFile
Write-Success "Input file: $InputFile"

# Validate output directory
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    Write-Success "Created output directory: $OutputDir"
}
$OutputDir = Resolve-Path $OutputDir
Write-Success "Output directory: $OutputDir"

# Check egress-llm connectivity
try {
    $healthCheck = Invoke-WebRequest -Uri "$LlmEndpoint/health" -TimeoutSec 5 -UseBasicParsing -ErrorAction Stop
    Write-Success "egress-llm is running at $LlmEndpoint"
} catch {
    Write-Error-Custom "Cannot connect to egress-llm at $LlmEndpoint"
    Write-Error-Custom "Please start egress-llm first. See EgressGuide.md for instructions."
    exit 1
}

# === Step 1: Content Analysis & Domain Detection ===
Write-Step "Step 1/4: Analyzing content and detecting domain..."

# Read input file
$inputContent = Get-Content -Path $InputFile -Raw -Encoding UTF8
$contentLength = $inputContent.Length
Write-Success "Content loaded: $contentLength characters"

# Load style config
$styleConfigPath = Join-Path $scriptDir "style-config.json"
$styleConfig = Get-Content -Path $styleConfigPath -Raw | ConvertFrom-Json
Write-Success "Style configuration loaded: 8 domains, 27 variants"

# Load content analyzer system prompt
$contentAnalyzerPath = Join-Path $scriptDir "content-analyzer.md"
$systemPrompt = Get-Content -Path $contentAnalyzerPath -Raw

# Build LLM request
$userPrompt = @"
Analyze the following content and generate a detailed infographic description.

INPUT CONTENT:
$inputContent

Generate a comprehensive infographic specification following the system prompt guidelines.
Focus on creating an engaging, educational, and visually compelling design.
"@

$fullPrompt = -join ($systemPrompt, [System.Environment]::NewLine, [System.Environment]::NewLine, $userPrompt)
$llmRequest = @{
    model = $LlmModel
    max_tokens = 4000
    messages = @(
        @{
            role = 'user'
            content = $fullPrompt
        }
    )
} | ConvertTo-Json -Depth 10

Write-Progress-Custom "Calling LLM API to generate description..."

try {
    $llmResponse = Invoke-WebRequest -Uri "$LlmEndpoint/v1/messages" `
        -Method POST `
        -Body $llmRequest `
        -ContentType "application/json; charset=utf-8" `
        -Headers @{
            "anthropic-version" = "2023-06-01"
        } `
        -UseBasicParsing

    $llmResult = $llmResponse.Content | ConvertFrom-Json
    $description = $llmResult.content[0].text
    $descLength = $description.Length
    Write-Success "Description generated: $descLength characters"
} catch {
    Write-Error-Custom "LLM API call failed: $($_.Exception.Message)"
    exit 1
}

# Domain detection algorithm
Write-Progress-Custom "Detecting domain and style variant..."

function Detect-Domain {
    param($Description, $StyleConfig)

    $descriptionLower = $Description.ToLower()
    $bestDomain = $null
    $bestScore = 0

    foreach ($domain in $StyleConfig) {
        $score = $domain.priority

        # Check anti-keywords (disqualifiers)
        $hasAntiKeyword = $false
        foreach ($antiKeyword in $domain.antiKeywords) {
            if ($descriptionLower -match "\b$($antiKeyword.ToLower())\b") {
                $hasAntiKeyword = $true
                break
            }
        }

        if ($hasAntiKeyword) {
            continue
        }

        # Count keyword matches
        $keywordMatches = 0
        foreach ($keyword in $domain.keywords) {
            if ($descriptionLower -match "\b$($keyword.ToLower())\b") {
                $keywordMatches++
            }
        }

        $score += ($keywordMatches * 5)

        if ($score -gt $bestScore) {
            $bestScore = $score
            $bestDomain = $domain
        }
    }

    return $bestDomain
}

function Select-Variant {
    param($Description, $Domain)

    $descriptionLower = $Description.ToLower()
    $bestVariant = $Domain.variants[0]  # Default to first variant
    $bestScore = 0

    foreach ($variant in $Domain.variants) {
        $score = 0

        # Count keyword matches
        foreach ($keyword in $variant.keywords) {
            if ($descriptionLower -match "\b$($keyword.ToLower())\b") {
                $score += 3
            }
        }

        # Check tone matches
        foreach ($tone in $variant.preferredTones) {
            if ($descriptionLower -match "\b$($tone.ToLower())\b") {
                $score += 2
            }
        }

        if ($score -gt $bestScore) {
            $bestScore = $score
            $bestVariant = $variant
        }
    }

    return $bestVariant
}

$detectedDomain = Detect-Domain -Description $description -StyleConfig $styleConfig

if ($null -eq $detectedDomain) {
    Write-Error-Custom "Could not detect appropriate domain"
    exit 1
}

$selectedVariant = Select-Variant -Description $description -Domain $detectedDomain

$domainName = $detectedDomain.domain
$variantName = $selectedVariant.name
Write-Success "Domain detected: $domainName - $variantName variant"

# === Step 2: Image Prompt Construction ===
Write-Step "Step 2/4: Building image prompt with style-specific rendering..."

# Load image prompter
$imagePrompterPath = Join-Path $scriptDir "image-prompter.md"
$imagePrompterContent = Get-Content -Path $imagePrompterPath -Raw

# Extract rendering instructions for detected style
$styleKey = "$($detectedDomain.domain)-$($selectedVariant.name)"
$renderingInstructions = ""

# Parse image-prompter.md to find the specific style section
$lines = $imagePrompterContent -split "`n"
$inTargetSection = $false
$sectionDepth = 0

for ($i = 0; $i -lt $lines.Length; $i++) {
    $line = $lines[$i]

    if ($line -match "^###\s+$($detectedDomain.domain)\s*-\s*$($selectedVariant.name)") {
        $inTargetSection = $true
        $sectionDepth = 3
        continue
    }

    if ($inTargetSection) {
        # Check if we hit another section at same or higher level
        if ($line -match "^#{1,$sectionDepth}\s+\w") {
            break
        }
        $renderingInstructions += $line + "`n"
    }
}

if ([string]::IsNullOrWhiteSpace($renderingInstructions)) {
    # Fallback: use generic rendering instructions
    Write-Progress-Custom "Using generic rendering instructions"
    $domainName = $detectedDomain.domain
    $variantName = $selectedVariant.name
    $domainDesc = $detectedDomain.description
    $tones = $selectedVariant.preferredTones -join ", "
    $renderingInstructions = @"
RENDERING STYLE: $domainName - $variantName
Description: $domainDesc
Visual Tones: $tones
"@
}

# Build final image prompt
$imagePrompt = @"
Create a professional educational infographic based on the following specification:

$description

STYLE-SPECIFIC RENDERING INSTRUCTIONS:
$renderingInstructions

CRITICAL REQUIREMENTS:
- Generate ALL text in ENGLISH ONLY (no Chinese, Japanese, Korean, or other languages)
- Use landscape orientation (16:9 or 4:3 aspect ratio)
- Create high visual impact with clear information hierarchy
- Balance text and visual elements for optimal comprehension
- Include proper spacing and breathing room
- Make text easily readable with good contrast
- Output as high-quality digital image suitable for web and print

If there are any sensitive figures or copyrighted content, draw a similar alternative, but do not refuse to generate.
"@

$promptLength = $imagePrompt.Length
Write-Success "Image prompt constructed: $promptLength characters"

# === Step 3: Image Generation ===
Write-Step "Step 3/4: Generating infographic image via egress-llm..."

# Build image generation request (matching ImageGenerationService.cs format)
$messageId = [guid]::NewGuid().ToString()
$cvId = [guid]::NewGuid().ToString()
$interactionId = [guid]::NewGuid().ToString()
$scenarioGuid = "347061d6-d666-4e7e-a34e-38bb75bd7a38"

$imageRequest = @{
    messages = @(
        @{
            id = $messageId
            author = @{ role = 'user' }
            content = @{
                content_type = 'multimodal_text'
                parts = @($imagePrompt)
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

Write-Progress-Custom "Calling image generation API..."
Write-Progress-Custom "This may take 30-60 seconds..."

try {
    $imageResponse = Invoke-WebRequest -Uri "$LlmEndpoint/chatgpt/convo2im" `
        -Method POST `
        -Body $imageRequest `
        -ContentType "application/json; charset=utf-8" `
        -Headers @{
            "X-CV" = $cvId
            "X-ChatGPT-User-Email" = "infographic-skill@microsoft.com"
            "X-ChatGPT-User-Id" = "infographic-skill-001"
            "X-ModelType" = "dev-gpt-image-1-5"
            "X-ScenarioGUID" = $scenarioGuid
            "x-imagegen-api-use-mainline" = "true"
            "X-InteractionId" = $interactionId
            "X-Tag" = '{"Client":"InfographicSkill"}'
            "Accept" = "text/event-stream"
        } `
        -UseBasicParsing `
        -TimeoutSec 120

    Write-Success "Image generated successfully"
} catch {
    Write-Error-Custom "Image generation failed: $($_.Exception.Message)"
    exit 1
}

# === Step 4: Parse SSE Stream and Save Image ===
Write-Step "Step 4/4: Saving infographic..."

# Parse SSE stream (matching ImageGenerationService.cs parsing logic)
$responseText = $imageResponse.Content
$lines = $responseText -split "`n"
$base64Image = $null

foreach ($line in $lines) {
    if ($line.StartsWith("data: ")) {
        $dataContent = $line.Substring(6).Trim()

        # Check for completion markers
        if ($dataContent -eq "[DONE]" -or $dataContent -eq "DONE") {
            break
        }

        try {
            $data = $dataContent | ConvertFrom-Json

            # Look for content.parts with image payload
            if ($data.content -and $data.content.parts) {
                foreach ($part in $data.content.parts) {
                    if ($part.content_type -eq "image" -and $part.payload) {
                        $base64Image = $part.payload
                        Write-Progress-Custom "Image data received"
                        break
                    }
                }
            }

            if ($base64Image) {
                break
            }
        } catch {
            # Skip invalid JSON lines
        }
    }
}

if ($null -eq $base64Image) {
    Write-Error-Custom "Could not extract image data from response"
    exit 1
}

# Decode base64 and save
$inputFileName = [System.IO.Path]::GetFileNameWithoutExtension($InputFile)
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$outputFileName = "${inputFileName}_infographic_${timestamp}.png"
$outputPath = Join-Path $OutputDir $outputFileName

try {
    $imageBytes = [Convert]::FromBase64String($base64Image)
    [System.IO.File]::WriteAllBytes($outputPath, $imageBytes)
    Write-Success "Infographic saved: $outputPath"
} catch {
    Write-Error-Custom "Failed to save image: $($_.Exception.Message)"
    exit 1
}

# === Summary ===
Write-Host ""
Write-Host "=== Generation Complete ===" -ForegroundColor Green
Write-Host "Input:    $InputFile" -ForegroundColor Yellow
Write-Host "Output:   $outputPath" -ForegroundColor Yellow
Write-Host "Domain:   $($detectedDomain.domain)" -ForegroundColor Yellow
Write-Host "Variant:  $($selectedVariant.name)" -ForegroundColor Yellow
Write-Host "Size:     $([math]::Round($imageBytes.Length / 1KB, 2)) KB" -ForegroundColor Yellow
