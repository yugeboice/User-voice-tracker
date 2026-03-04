# Quick test script - simplified version
param(
    [Parameter(Mandatory=$true)]
    [string]$InputFile,

    [Parameter(Mandatory=$false)]
    [string]$OutputDir = ".",

    [Parameter(Mandatory=$false)]
    [string]$LlmEndpoint = "http://localhost:4141"
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "[Test] Starting infographic generation..." -ForegroundColor Cyan

# Step 1: Read file
if (-not (Test-Path $InputFile)) {
    Write-Host "[Error] File not found: $InputFile" -ForegroundColor Red
    exit 1
}

$content = Get-Content -Path $InputFile -Raw -Encoding UTF8
Write-Host "[OK] Loaded $($content.Length) characters" -ForegroundColor Green

# Step 2: Load config
$styleConfigPath = Join-Path $scriptDir "style-config.json"
$styleConfig = Get-Content -Path $styleConfigPath -Raw | ConvertFrom-Json
Write-Host "[OK] Loaded style config" -ForegroundColor Green

# Step 3: Call LLM to generate description
Write-Host "[Progress] Calling LLM API..." -ForegroundColor Yellow

$promptText = "Analyze this content and create a detailed infographic description:`n`n$content"

$requestBody = @{
    model = "claude-sonnet-4"
    max_tokens = 3000
    messages = @(
        @{
            role = "user"
            content = $promptText
        }
    )
}

# Use PowerShell's built-in JSON serialization which handles escaping properly
$requestBodyJson = $requestBody | ConvertTo-Json -Depth 10 -Compress

try {
    $response = Invoke-RestMethod -Uri "$LlmEndpoint/v1/messages" `
        -Method POST `
        -Body $requestBodyJson `
        -ContentType "application/json; charset=utf-8" `
        -Headers @{ "anthropic-version" = "2023-06-01" }

    $description = $response.content[0].text
    Write-Host "[OK] Generated description ($($description.Length) chars)" -ForegroundColor Green
} catch {
    Write-Host "[Error] LLM call failed: $_" -ForegroundColor Red
    exit 1
}

# Step 4: Detect domain (simplified)
$detectedDomain = $null
$maxScore = 0

foreach ($domain in $styleConfig) {
    $score = 0
    foreach ($keyword in $domain.keywords) {
        if ($description -match "\b$keyword\b") {
            $score += 5
        }
    }
    if ($score -gt $maxScore) {
        $maxScore = $score
        $detectedDomain = $domain
    }
}

if ($null -eq $detectedDomain) {
    $detectedDomain = $styleConfig[0]
}

$variant = $detectedDomain.variants[0]
Write-Host "[OK] Detected: $($detectedDomain.domain) - $($variant.name)" -ForegroundColor Green

# Step 5: Generate image
Write-Host "[Progress] Generating image (this may take 30-60 seconds)..." -ForegroundColor Yellow

$domainStyle = $detectedDomain.domain
$imagePrompt = "Create a professional infographic about:

$description

Style: $domainStyle. Use landscape format, English text only, high visual impact."

Write-Host "[Debug] Image prompt length: $($imagePrompt.Length)" -ForegroundColor Gray

# Use the correct payload format from ImageGenerationService.cs
$messageId = [guid]::NewGuid().ToString()
$cvId = [guid]::NewGuid().ToString()
$interactionId = [guid]::NewGuid().ToString()

Write-Host "[Debug] Message ID: $messageId" -ForegroundColor Gray

$imageRequestBody = @{
    messages = @(
        @{
            id = $messageId
            author = @{
                role = "user"
            }
            content = @{
                content_type = "multimodal_text"
                parts = @($imagePrompt)
            }
        }
    )
    virtual_model = "gpt-image-1-5"
    zdr_type = 1
    size = "image"
    orientation = "landscape"
    stream = $true
    image_format = "png"
    n = 1
    encode_user_images_as_vq = $true
}

Write-Host "[Debug] Converting to JSON..." -ForegroundColor Gray
$imageRequestJson = $imageRequestBody | ConvertTo-Json -Depth 10 -Compress
Write-Host "[Debug] JSON length: $($imageRequestJson.Length)" -ForegroundColor Gray

try {
    Write-Host "[Debug] Calling image API..." -ForegroundColor Gray
    Write-Host "[Debug] Endpoint: $LlmEndpoint/chatgpt/convo2im" -ForegroundColor Gray

    $uri = "$LlmEndpoint/chatgpt/convo2im"
    Write-Host "[Debug] Full URI: $uri" -ForegroundColor Gray

    $headers = @{
        "X-CV" = $cvId
        "X-ChatGPT-User-Email" = "ppt-generator@microsoft.com"
        "X-ChatGPT-User-Id" = "ppt-automation-001"
        "X-ModelType" = "dev-gpt-image-1-5"
        "X-ScenarioGUID" = "347061d6-d666-4e7e-a34e-38bb75bd7a38"
        "x-imagegen-api-use-mainline" = "true"
        "X-InteractionId" = $interactionId
        "X-Tag" = '{"Client":"InfographicSkill"}'
        "Accept" = "text/event-stream"
    }

    $imageResponse = Invoke-WebRequest -Uri $uri `
        -Method POST `
        -Body $imageRequestJson `
        -ContentType "application/json; charset=utf-8" `
        -Headers $headers `
        -TimeoutSec 120 `
        -ErrorAction Stop

    Write-Host "[OK] Image API response received" -ForegroundColor Green

    # Debug: save response to file
    try {
        $debugPath = Join-Path $OutputDir "last_response_debug.txt"
        $imageResponse.Content | Out-File -FilePath $debugPath -Encoding UTF8
        Write-Host "[Debug] Response saved to $debugPath" -ForegroundColor Gray
    } catch {
        Write-Host "[Debug] Could not save response: $_" -ForegroundColor Gray
    }

} catch {
    Write-Host "[Error] Image generation failed: $_" -ForegroundColor Red
    Write-Host "[Error] Exception: $($_.Exception.GetType().FullName)" -ForegroundColor Red
    Write-Host "[Error] Message: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Step 6: Extract and save
$responseText = $imageResponse.Content
$base64Image = $null

foreach ($line in $responseText -split "`n") {
    if ($line.Trim() -eq "data: DONE" -or $line.Trim() -eq "data: [DONE]") {
        break
    }

    if ($line.StartsWith("data: ")) {
        $jsonData = $line.Substring(6).Trim()
        if ($jsonData -eq "[DONE]" -or $jsonData -eq "DONE") {
            break
        }

        try {
            $data = $jsonData | ConvertFrom-Json
            # Look for content.parts with image payload (matching ImageGenerationService.cs)
            if ($data.content -and $data.content.parts) {
                foreach ($part in $data.content.parts) {
                    if ($part.content_type -eq "image" -and $part.payload) {
                        $base64Image = $part.payload
                        break
                    }
                }
                if ($base64Image) { break }
            }
        } catch {
            # Skip invalid JSON
        }
    }
}

if ($null -eq $base64Image) {
    Write-Host "[Error] Could not extract image data" -ForegroundColor Red
    exit 1
}

# Save image
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$inputFileName = [System.IO.Path]::GetFileNameWithoutExtension($InputFile)
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$outputFileName = "${inputFileName}_infographic_${timestamp}.png"
$outputPath = Join-Path $OutputDir $outputFileName

try {
    $imageBytes = [Convert]::FromBase64String($base64Image)
    [System.IO.File]::WriteAllBytes($outputPath, $imageBytes)
    $sizeKB = [math]::Round($imageBytes.Length / 1KB, 2)

    Write-Host "`n=== SUCCESS ===" -ForegroundColor Green
    Write-Host "Output: $outputPath" -ForegroundColor Yellow
    Write-Host "Size: $sizeKB KB" -ForegroundColor Yellow
    Write-Host "Domain: $($detectedDomain.domain)" -ForegroundColor Yellow
} catch {
    Write-Host "[Error] Failed to save image: $_" -ForegroundColor Red
    exit 1
}
