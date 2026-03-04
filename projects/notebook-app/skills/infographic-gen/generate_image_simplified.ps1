# Simplified Image Generation Script
$cvId = [guid]::NewGuid().ToString()
$interactionId = [guid]::NewGuid().ToString()
$messageId = [guid]::NewGuid().ToString()
$scenarioGuid = "347061d6-d666-4e7e-a34e-38bb75bd7a38"
$endpoint = "http://localhost:4141"
$promptPath = "C:\PMVibeCoding\PM_Playground\Lumina-API-Demo\skills\infographic-gen\C4_AutonomousVehicleMarketOutlook_simplified_prompt.txt"
$outputPath = "C:\PMVibeCoding\PM_Playground\Lumina-API-Demo\skills\infographic-gen\C4_AutonomousVehicleMarketOutlook_infographic_20260106_105000.png"

Write-Host "[Infographic] Generating with simplified prompt..."
Write-Host "[Infographic] This may take 30-90 seconds..."

$prompt = Get-Content $promptPath -Raw

$payloadObj = @{
    messages = @(
        @{
            id = $messageId
            author = @{ role = 'user' }
            content = @{
                content_type = 'multimodal_text'
                parts = @($prompt)
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
}

$payload = $payloadObj | ConvertTo-Json -Depth 10 -Compress

$headers = @{
    'Content-Type' = 'application/json'
    'Accept' = 'text/event-stream'
    'X-cv' = $cvId
    'X-ChatGPT-User-Email' = 'infographic-skill@example.com'
    'X-ChatGPT-User-Id' = 'infographic-skill-001'
    'X-ModelType' = 'dev-gpt-image-1-5'
    'X-ScenarioGUID' = $scenarioGuid
    'x-imagegen-api-use-mainline' = 'true'
    'X-InteractionId' = $interactionId
    'X-Tag' = '{"Client":"InfographicSkill"}'
}

try {
    $response = Invoke-WebRequest -Uri "$endpoint/chatgpt/convo2im" -Method POST -Headers $headers -Body $payload -TimeoutSec 120

    $lines = $response.Content -split "`n"
    $imageFound = $false

    foreach ($line in $lines) {
        if ($line.StartsWith("data: ")) {
            $dataContent = $line.Substring(6).Trim()
            if ($dataContent -eq "[DONE]" -or $dataContent -eq "DONE") {
                break
            }

            try {
                $data = $dataContent | ConvertFrom-Json
                if ($data.content -and $data.content.parts) {
                    foreach ($part in $data.content.parts) {
                        if ($part.content_type -eq "image" -and $part.payload) {
                            $imageBytes = [System.Convert]::FromBase64String($part.payload)
                            [System.IO.File]::WriteAllBytes($outputPath, $imageBytes)
                            $fileSizeKB = [math]::Round($imageBytes.Length / 1024, 2)
                            Write-Host "[Infographic] SUCCESS! Image saved: $outputPath"
                            Write-Host "[Infographic] File size: $fileSizeKB KB"
                            $imageFound = $true
                            break
                        }
                    }
                }
            }
            catch {
            }
        }
    }

    if (-not $imageFound) {
        Write-Host "[Infographic] ERROR: No image data found in response"
        exit 1
    }
}
catch {
    Write-Host "[Infographic] ERROR: $($_.Exception.Message)"
    Write-Host "[Infographic] Check egress-llm logs for details"
    exit 1
}