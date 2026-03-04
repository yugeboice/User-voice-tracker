using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MinimalApiCall;

/// <summary>
/// Invokes Python-based skills with proper process management and error handling.
/// Supports async execution, JSON argument passing, and stdout/stderr capture.
/// </summary>
public class SkillInvoker
{
    private readonly string _pythonExecutable;
    private readonly string _skillsBasePath;

    /// <summary>
    /// Initialize skill invoker with Python executable and skills directory.
    /// </summary>
    /// <param name="pythonExecutable">Path to python.exe or just "python" if in PATH</param>
    /// <param name="skillsBasePath">Base directory containing skill folders</param>
    public SkillInvoker(string pythonExecutable = "python", string? skillsBasePath = null)
    {
        _pythonExecutable = pythonExecutable;
        _skillsBasePath = skillsBasePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "skills");
    }

    /// <summary>
    /// Execute a Python skill script with arguments.
    /// </summary>
    /// <param name="skillName">Name of the skill folder (e.g., "infographic-gen")</param>
    /// <param name="scriptName">Name of the Python script (e.g., "generate_image.py")</param>
    /// <param name="arguments">Command-line arguments to pass to the script</param>
    /// <param name="timeoutMs">Timeout in milliseconds (default: 5 minutes)</param>
    /// <returns>Result containing success status, stdout, stderr, and exit code</returns>
    public async Task<SkillExecutionResult> ExecuteSkillAsync(
        string skillName,
        string scriptName,
        string arguments = "",
        int timeoutMs = 300_000)
    {
        var skillPath = Path.Combine(_skillsBasePath, skillName);
        var scriptPath = Path.Combine(skillPath, scriptName);

        if (!File.Exists(scriptPath))
        {
            return new SkillExecutionResult
            {
                Success = false,
                ErrorMessage = $"Script not found: {scriptPath}",
                ExitCode = -1
            };
        }

        var processStartInfo = new ProcessStartInfo
        {
            FileName = _pythonExecutable,
            Arguments = $"\"{scriptPath}\" {arguments}",
            WorkingDirectory = skillPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        using var process = new Process { StartInfo = processStartInfo };

        // Capture stdout and stderr asynchronously
        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                stdoutBuilder.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                stderrBuilder.AppendLine(e.Data);
            }
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait for exit or timeout
            var exited = await Task.Run(() => process.WaitForExit(timeoutMs));

            if (!exited)
            {
                // Timeout - kill process
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch { /* Ignore if already exited */ }

                return new SkillExecutionResult
                {
                    Success = false,
                    ErrorMessage = $"Skill execution timed out after {timeoutMs}ms",
                    StandardOutput = stdoutBuilder.ToString(),
                    StandardError = stderrBuilder.ToString(),
                    ExitCode = -1
                };
            }

            var stdout = stdoutBuilder.ToString();
            var stderr = stderrBuilder.ToString();
            var exitCode = process.ExitCode;

            return new SkillExecutionResult
            {
                Success = exitCode == 0,
                StandardOutput = stdout,
                StandardError = stderr,
                ExitCode = exitCode,
                ErrorMessage = exitCode != 0 ? $"Script exited with code {exitCode}" : null
            };
        }
        catch (Exception ex)
        {
            return new SkillExecutionResult
            {
                Success = false,
                ErrorMessage = $"Failed to execute skill: {ex.Message}",
                StandardOutput = stdoutBuilder.ToString(),
                StandardError = stderrBuilder.ToString(),
                ExitCode = -1
            };
        }
    }

    /// <summary>
    /// Execute infographic generation skill specifically.
    /// Convenience method for the most common use case.
    /// </summary>
    /// <param name="inputFilePath">Path to input text file</param>
    /// <param name="outputImagePath">Path where PNG image will be saved</param>
    /// <param name="llmEndpoint">LLM endpoint URL (default: http://localhost:4141)</param>
    /// <param name="llmModel">LLM model name (default: claude-sonnet-4)</param>
    /// <param name="customStylePrompt">Optional custom style/content prompt from user</param>
    /// <param name="timeoutMs">Timeout in milliseconds (default: 5 minutes)</param>
    /// <returns>Result of the execution</returns>
    public async Task<SkillExecutionResult> ExecuteInfographicGenerationAsync(
        string inputFilePath,
        string outputImagePath,
        string llmEndpoint = "http://localhost:4141",
        string llmModel = "claude-sonnet-4",
        string? customStylePrompt = null,
        int timeoutMs = 300_000)
    {
        var arguments = $"--input \"{inputFilePath}\" --output \"{outputImagePath}\" --llm-endpoint {llmEndpoint} --llm-model {llmModel}";
        
        // Add custom style argument if provided
        if (!string.IsNullOrWhiteSpace(customStylePrompt))
        {
            // Escape quotes and special characters for command line
            var escapedPrompt = customStylePrompt
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
            arguments += $" --custom-style \"{escapedPrompt}\"";
        }

        return await ExecuteSkillAsync(
            "infographic-gen",
            "infographic-gen.py",
            arguments,
            timeoutMs
        );
    }
}

/// <summary>
/// Result of a skill execution.
/// </summary>
public record SkillExecutionResult
{
    /// <summary>
    /// Whether the execution was successful (exit code 0).
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Standard output from the script.
    /// </summary>
    public string StandardOutput { get; init; } = string.Empty;

    /// <summary>
    /// Standard error from the script.
    /// </summary>
    public string StandardError { get; init; } = string.Empty;

    /// <summary>
    /// Process exit code.
    /// </summary>
    public int ExitCode { get; init; }

    /// <summary>
    /// Error message if execution failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Get combined output (stdout + stderr) for debugging.
    /// </summary>
    public string GetCombinedOutput()
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(StandardOutput))
        {
            sb.AppendLine("=== STDOUT ===");
            sb.AppendLine(StandardOutput);
        }
        if (!string.IsNullOrEmpty(StandardError))
        {
            sb.AppendLine("=== STDERR ===");
            sb.AppendLine(StandardError);
        }
        return sb.ToString();
    }
}
