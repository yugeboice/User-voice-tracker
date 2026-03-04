using System.Diagnostics;
using System.Collections.Concurrent;
using Launcher.Internal;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:8400");

// Add CORS for child projects
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
                  "http://localhost:8401", 
                  "http://localhost:8402",
                  "http://localhost:8403",
                  "http://localhost:8404",
                  "http://localhost:8405",
                  "http://localhost:8406")
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Enable CORS
app.UseCors();

// Unified TokenService for all projects
TokenService? tokenService = null;
Func<Task<string>> launcherTokenProvider = async () =>
{
    tokenService ??= new TokenService(app.Configuration);
    return await tokenService.GetTokenAsync();
};

// Serve docs/index.html as static landing page
var docsPath = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "docs"));
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(docsPath),
    RequestPath = ""
});

// Project registry: name -> { port, relative path, stack, author, description }
var projects = new Dictionary<string, ProjectInfo>
{
    ["competitor-trending-news"] = new("projects/competitor-trending-news", 8401, "C# / .NET 8", "sunting", "竞争情报：追踪竞品动态和趋势新闻"),
    ["companion-chat"] = new("projects/companion-chat", 8402, "C# / .NET 8", "fangwu", "陪伴聊天、PPT生成、TTS、本地知识库"),
    ["notebook-app"] = new("projects/notebook-app", 8403, "C# / .NET 8", "tiantianguo", "交互式笔记本，RAG聊天+图片生成"),
    ["infographic-gen"] = new("projects/infographic-gen", 8404, "C# / .NET 8", "tiantianguo", "信息图生成（重构精简版）"),
    ["customer-service-agent"] = new("projects/customer-service-agent", 8405, "C# / .NET 8", "xinrangao", "智能客服助手（知识库+对话历史+记忆功能）"),
    ["arena-watch"] = new("projects/arena-watch", 8406, "C# / .NET 8", "Doris", "AI模型排名分析平台"),
    ["ai-newsletter"] = new("projects/ai-newsletter", 0, "Node.js", "Doris", "自动化AI新闻聚合系统"),
};

// Track running processes
var runningProcesses = new ConcurrentDictionary<string, Process>();
var projectsRoot = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, ".."));

// GET / -> serve landing page
app.MapGet("/", () => Results.Redirect("/index.html"));

// GET /api/projects -> list all projects with status
app.MapGet("/api/projects", () =>
{
    var result = projects.Select(p => new
    {
        name = p.Key,
        port = p.Value.Port,
        stack = p.Value.Stack,
        author = p.Value.Author,
        description = p.Value.Description,
        running = runningProcesses.ContainsKey(p.Key) && !runningProcesses[p.Key].HasExited,
        hasWebUi = p.Value.Port > 0,
        url = p.Value.Port > 0 ? $"http://localhost:{p.Value.Port}" : null
    });
    return Results.Ok(result);
});

// POST /api/launch/{projectName} -> start a project
app.MapPost("/api/launch/{projectName}", (string projectName) =>
{
    if (!projects.TryGetValue(projectName, out var info))
        return Results.NotFound(new { error = $"Project '{projectName}' not found" });

    if (info.Port == 0)
        return Results.BadRequest(new { error = $"Project '{projectName}' has no web server" });

    // Already running?
    if (runningProcesses.TryGetValue(projectName, out var existing) && !existing.HasExited)
        return Results.Ok(new { status = "already_running", port = info.Port, url = $"http://localhost:{info.Port}" });

    var workDir = Path.Combine(projectsRoot, info.RelativePath);
    if (!Directory.Exists(workDir))
        return Results.BadRequest(new { error = $"Project directory not found: {workDir}" });

    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "run --project MinimalApiCall.csproj",
            WorkingDirectory = workDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        // Inject shared configuration via environment variables.
        // ASP.NET Core reads these automatically (__ = section separator, overrides appsettings.json).
        var launcherConfig = app.Configuration;
        var sharedEnvVars = new Dictionary<string, string?>
        {
            ["LauncherUrl"] = "http://localhost:8400",
            ["LuminaConfiguration__ApiEndpoint"] = launcherConfig["LuminaConfiguration:ApiEndpoint"]
                ?? "https://luminaserviceapi-test-westus.copilotlumina.com",
            ["LuminaConfiguration__CuaEndpoint"] = launcherConfig["LuminaConfiguration:CuaEndpoint"]
                ?? "https://luminaserviceapi-test-westus.copilotlumina.com",
            ["LuminaConfiguration__ApiScopes"] = launcherConfig["LuminaConfiguration:ApiScopes"]
                ?? "67f912ef-f692-43d3-9b97-3702aa2fd840/.default",
            ["PartnerContext__Partner"] = launcherConfig["PartnerContext:Partner"] ?? "PM playground",
            ["PartnerContext__ScenarioGroup"] = launcherConfig["PartnerContext:ScenarioGroup"] ?? "APIDemo",
        };
        foreach (var (key, value) in sharedEnvVars)
        {
            if (!string.IsNullOrEmpty(value))
                psi.Environment[key] = value;
        }

        var process = Process.Start(psi);
        if (process == null)
            return Results.Json(new { error = "Failed to start process" }, statusCode: 500);

        // Consume output to prevent buffer deadlock
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        runningProcesses[projectName] = process;

        return Results.Ok(new { status = "started", port = info.Port, url = $"http://localhost:{info.Port}", pid = process.Id });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
});

// POST /api/stop/{projectName} -> stop a project
app.MapPost("/api/stop/{projectName}", (string projectName) =>
{
    if (!runningProcesses.TryGetValue(projectName, out var process))
        return Results.Ok(new { status = "not_running" });

    if (!process.HasExited)
    {
        try { process.Kill(entireProcessTree: true); } catch { }
    }

    runningProcesses.TryRemove(projectName, out _);
    return Results.Ok(new { status = "stopped" });
});

// === Authentication APIs for unified login ===

// POST /api/auth/login -> trigger user login (same as companion-chat)
app.MapPost("/api/auth/login", async () =>
{
    try
    {
        var token = await launcherTokenProvider();
        return Results.Ok(new { success = true, message = "Login successful" });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { success = false, message = ex.Message });
    }
});

// GET /api/auth/token -> child projects get token
app.MapGet("/api/auth/token", async () =>
{
    try
    {
        var token = await launcherTokenProvider();
        var expiresOn = tokenService?.GetTokenExpiry() ?? DateTimeOffset.UtcNow.AddHours(1);
        return Results.Ok(new { token, expiresOn });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// GET /api/auth/status -> check login status
app.MapGet("/api/auth/status", () =>
{
    try
    {
        var isLoggedIn = tokenService != null && tokenService.HasValidToken();
        return Results.Ok(new { isLoggedIn });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { isLoggedIn = false });
    }
});

// Cleanup on shutdown: kill all child processes
app.Lifetime.ApplicationStopping.Register(() =>
{
    foreach (var (name, process) in runningProcesses)
    {
        if (!process.HasExited)
        {
            Console.WriteLine($"[Shutdown] Stopping {name} (PID {process.Id})...");
            try { process.Kill(entireProcessTree: true); } catch { }
        }
    }
});

Console.WriteLine();
Console.WriteLine("╔══════════════════════════════════════════════════════╗");
Console.WriteLine("║        Lumina Lab Launcher                           ║");
Console.WriteLine("║        Open: http://localhost:8400                   ║");
Console.WriteLine("╚══════════════════════════════════════════════════════╝");
Console.WriteLine();

app.Run();

// Models
record ProjectInfo(string RelativePath, int Port, string Stack, string Author, string Description);
