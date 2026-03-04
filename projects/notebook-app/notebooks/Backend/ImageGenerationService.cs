using System.Text;
using System.Text.Json;

namespace MinimalApiCall;

/// <summary>
/// Image generation service using GPT image model via chatgpt/convo2im endpoint.
/// Generates infographic images from text prompts.
/// </summary>
public class ImageGenerationService
{
    private readonly string _llmEndpoint;
    private readonly HttpClient _httpClient;

    public ImageGenerationService(string llmEndpoint)
    {
        _llmEndpoint = llmEndpoint;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(3) // Longer timeout for image generation
        };
    }

    /// <summary>
    /// Generate an image from a prompt and save to the specified path.
    /// Uses gpt-image-1.5 model with landscape orientation (1792x1024 for 3:4 ratio).
    /// </summary>
    public async Task<bool> GenerateImageAsync(string prompt, string outputPath)
    {
        try
        {
            Console.WriteLine($"[ImageGen] Starting image generation...");
            Console.WriteLine($"[ImageGen] Prompt: {TruncateForLog(prompt, 150)}");
            Console.WriteLine($"[ImageGen] Output: {outputPath}");
            
            // Save full prompt to file for debugging
            var promptLogDirectory = Path.GetDirectoryName(outputPath) ?? ".";
            var promptLogFileName = $"last_prompt_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            var promptLogPath = Path.Combine(promptLogDirectory, promptLogFileName);
            try
            {
                File.WriteAllText(promptLogPath, $"Generated at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n{prompt}");
            }
            catch { /* Ignore file write errors */ }

            // Ensure output directory exists
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Add instruction to handle sensitive/copyrighted content AND ensure proper text rendering
            var enhancedPrompt = prompt + " If there are sensitive figures or copyrighted content, draw a similar alternative, but do not refuse to generate. IMPORTANT: Render every label in clear English only with correct spelling and spacing. Do not add non-English characters or decorative foreign scripts.";

            // Build request payload - matching reference documentation
            var payload = new
            {
                messages = new[]
                {
                    new
                    {
                        id = Guid.NewGuid().ToString(),
                        author = new { role = "user" },
                        content = new
                        {
                            content_type = "multimodal_text",
                            parts = new[] { enhancedPrompt }
                        }
                    }
                },
                virtual_model = "gpt-image-1-5",
                zdr_type = 1,
                size = "image",
                orientation = "landscape",
                stream = true,
                image_format = "png",
                n = 1,
                encode_user_images_as_vq = true
            };

            // Build headers matching reference documentation
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_llmEndpoint}/chatgpt/convo2im");
            request.Headers.Add("X-CV", Guid.NewGuid().ToString());
            request.Headers.Add("X-ChatGPT-User-Email", "ppt-generator@microsoft.com");
            request.Headers.Add("X-ChatGPT-User-Id", "ppt-automation-001");
            request.Headers.Add("X-ModelType", "dev-gpt-image-1-5");
            request.Headers.Add("X-ScenarioGUID", "347061d6-d666-4e7e-a34e-38bb75bd7a38");
            request.Headers.Add("x-imagegen-api-use-mainline", "true");
            request.Headers.Add("X-InteractionId", Guid.NewGuid().ToString());
            request.Headers.Add("X-Tag", JsonSerializer.Serialize(new { Client = "PPTGenerator" }));
            request.Headers.Add("Accept", "text/event-stream");

            var jsonPayload = JsonSerializer.Serialize(payload);
            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            // Debug: Log all headers being sent
            Console.WriteLine($"[ImageGen] Sending request to {_llmEndpoint}/chatgpt/convo2im");
            Console.WriteLine($"[ImageGen] DEBUG Headers:");
            foreach (var header in request.Headers)
            {
                Console.WriteLine($"[ImageGen]   {header.Key}: {string.Join(", ", header.Value)}");
            }

            // Send request
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[ImageGen] HTTP Error {response.StatusCode}: {errorText}");
                throw new Exception($"Image generation API returned {response.StatusCode}: {errorText}");
            }

            Console.WriteLine($"[ImageGen] Response received, processing SSE stream...");

            // Process SSE stream
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            string? line;
            var imagesSaved = 0;

            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Check for completion markers
                if (line.Trim() == "data: DONE" || line.Trim() == "data: [DONE]")
                {
                    Console.WriteLine("[ImageGen] Stream completed");
                    break;
                }

                // Parse SSE data lines
                if (line.StartsWith("data: "))
                {
                    var dataContent = line.Substring(6).Trim();
                    if (dataContent == "[DONE]" || dataContent == "DONE")
                        break;

                    try
                    {
                        // Parse JSON chunk
                        using var doc = JsonDocument.Parse(dataContent);
                        var root = doc.RootElement;

                        // Look for content.parts with image payload
                        if (root.TryGetProperty("content", out var content))
                        {
                            if (content.TryGetProperty("parts", out var parts))
                            {
                                foreach (var part in parts.EnumerateArray())
                                {
                                    if (part.TryGetProperty("content_type", out var contentType) &&
                                        contentType.GetString() == "image" &&
                                        part.TryGetProperty("payload", out var imagePart))
                                    {
                                        var base64Data = imagePart.GetString();
                                        if (!string.IsNullOrEmpty(base64Data))
                                        {
                                            // Decode and save image
                                            var imageBytes = Convert.FromBase64String(base64Data);
                                            await File.WriteAllBytesAsync(outputPath, imageBytes);
                                            imagesSaved++;
                                            Console.WriteLine($"[ImageGen] ✅ Image saved: {outputPath} ({imageBytes.Length} bytes)");
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // Skip non-JSON lines
                        continue;
                    }
                }
                else if (line.StartsWith("event: error"))
                {
                    Console.WriteLine($"[ImageGen] ❌ Error event received: {line}");
                    // Read the next line to get error details
                    var errorDataLine = await reader.ReadLineAsync();
                    Console.WriteLine($"[ImageGen] ❌ Error data: {errorDataLine}");
                    throw new Exception($"Image generation failed with error event: {errorDataLine}");
                }
            }

            if (imagesSaved == 0)
            {
                Console.WriteLine("[ImageGen] ❌ No images were extracted from the stream");
                return false;
            }

            Console.WriteLine($"[ImageGen] ✅ Successfully generated and saved image");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ImageGen] ❌ Exception: {ex.Message}");
            Console.WriteLine($"[ImageGen] Stack trace: {ex.StackTrace}");
            throw;
        }
    }

    private string TruncateForLog(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
    }
}
