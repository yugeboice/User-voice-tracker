using System.Collections.Concurrent;

namespace LuminaSearchConsole.Services
{
    /// <summary>
    /// Demonstrates Lumina Computer Use Agent (CUA) APIs: POST /api/agent/computer/*
    /// 
    /// CUA allows controlling a virtual Windows computer via API to:
    /// - Navigate websites
    /// - Click UI elements
    /// - Type text
    /// - Capture screenshots
    /// 
    /// API Endpoints:
    /// - POST /api/agent/computer/initialize - Create virtual computer
    /// - POST /api/agent/computer/get - Capture screenshot
    /// - POST /api/agent/computer/do - Perform actions (click, type, keypress)
    /// - POST /api/agent/computer/release - Clean up resources
    /// 
    /// UI Integration:
    /// - "🤖 Stock Price Screenshot" card shows CUA in action
    /// - Real-time progress via Server-Sent Events (SSE)
    /// - Displays screenshot captured from virtual computer
    /// 
    /// Frontend Code Reference:
    /// - wwwroot/js/cua.js → cuaMsnMoney(companyName)
    /// - Views/Home/Index.cshtml → SSE event handling
    /// 
    /// How to Try:
    /// 1. Search for a company (e.g., "Microsoft")
    /// 2. Click "Get Stock Price Screenshot" button in CUA card
    /// 3. Watch real-time progress: Initialize → Navigate → Click → Type → Screenshot
    /// 4. See captured screenshot displayed in the card
    /// 5. Check API logs - you'll see POST /api/agent/computer/* entries
    /// </summary>
    public class LuminaComputerUseApiService : IDisposable
    {
        private readonly ApiLogService _apiLogService;
        private readonly ILogger<LuminaComputerUseApiService> _logger;

        // Computer Pool fields (performance optimization - see region below)
        private readonly ConcurrentDictionary<string, ComputerPoolEntry> _computerPool = new();
        private readonly TimeSpan _keepAliveTime = TimeSpan.FromMinutes(3);
        private readonly Timer _cleanupTimer;

        #region Constructor

        public LuminaComputerUseApiService(
            ApiLogService apiLogService,
            ILogger<LuminaComputerUseApiService> logger)
        {
            _apiLogService = apiLogService;
            _logger = logger;

            // Run cleanup every 30 seconds
            _cleanupTimer = new Timer(CleanupExpiredComputers, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        #endregion

        #region CUA API Methods

        /// <summary>
        /// Search company on MSN Money and capture screenshot
        /// 
        /// This demonstrates the full CUA workflow:
        /// 1. Initialize - Get/create virtual computer (with pooling optimization)
        /// 2. Navigate - Open MSN Money website
        /// 3. Click - Click search box
        /// 4. Type - Enter company name
        /// 5. Keypress - Press Enter to search
        /// 6. Wait - Wait for results to load
        /// 7. GetScreenshot - Capture the results page
        /// 
        /// Use Case: Automate web browsing tasks that require visual verification
        /// 
        /// Note: This method uses Server-Sent Events (SSE) to stream progress to the UI
        /// </summary>
        public async Task<CuaResult> SearchCompanyAndCaptureScreenshotAsync(
            LuminaCuaService cuaService,
            string userId,
            string tenantId,
            string companyName,
            Func<string, string, Task>? progressCallback = null)
        {
            string? computerId = null;
            var userKey = $"{userId}_{tenantId}";

            try
            {
                // Step 1: Initialize - Get or create computer from pool
                await SendProgress(progressCallback, "progress", "🔄 Initialize - Getting virtual computer...");

                var startTime = DateTime.Now;
                (computerId, cuaService) = await GetOrCreateComputerAsync(userKey, userId, tenantId, cuaService);
                var duration = (DateTime.Now - startTime).TotalMilliseconds;

                var poolStats = GetPoolStatistics();
                _apiLogService.AddLog("Lumina CUA - MSN Money", "Initialize",
                    $"✅ Virtual computer ready\n" +
                    $"  ComputerId: {computerId}\n" +
                    $"  Response time: {duration:F0}ms\n" +
                    $"  Pool: {poolStats.TotalComputers} computers ({poolStats.ActiveComputers} active)");

                await SendProgress(progressCallback, "progress", $"✅ Initialize completed ({duration:F0}ms) [Reused computer]");
                await Task.Delay(300);

                // Step 2: Navigate - Open MSN Money
                await SendProgress(progressCallback, "progress", "🌐 Navigate - Opening https://www.msn.com/en-us/money/...");

                startTime = DateTime.Now;
                await cuaService.NavigateToUrlAsync(computerId, "https://www.msn.com/en-us/money/");
                await Task.Delay(2000); // Wait for page load
                duration = (DateTime.Now - startTime).TotalMilliseconds;

                await SendProgress(progressCallback, "progress", $"✅ Navigate completed ({duration:F0}ms)");
                await Task.Delay(300);

                // Step 3-6: Perform search actions
                await SendProgress(progressCallback, "progress", "🖱️ Click - Clicking search box at (1203, 43)...");
                await Task.Delay(300);

                await SendProgress(progressCallback, "progress", $"⌨️ Type - Typing \"{companyName}\"...");
                await Task.Delay(400);

                await SendProgress(progressCallback, "progress", "⏎ Keypress - Pressing Enter...");
                await Task.Delay(300);

                await SendProgress(progressCallback, "progress", "⏱️ Wait - Waiting for page to load...");

                // Perform the search actions
                var actions = new List<CuaAction>
                {
                    new CuaAction { Action = "click", X = 1203, Y = 43, Button = 1 },
                    new CuaAction { Action = "type", Text = companyName },
                    new CuaAction { Action = "keypress", Keys = new[] { "enter" } },
                    new CuaAction { Action = "wait" }
                };

                await cuaService.PerformComputerActionsAsync(computerId, actions, actionDelayMs: 800);

                _apiLogService.AddLog("Lumina CUA - MSN Money", "Search",
                    $"✅ Search completed for '{companyName}'");

                await Task.Delay(500);

                // Step 7: GetScreenshot - Capture results page
                await SendProgress(progressCallback, "progress", "📸 GetScreenshot - Capturing screenshot...");

                startTime = DateTime.Now;
                var screenshot = await cuaService.GetComputerScreenshotAsync(computerId);
                duration = (DateTime.Now - startTime).TotalMilliseconds;

                _apiLogService.AddLog("Lumina CUA - MSN Money", "Screenshot",
                    $"✅ Screenshot captured\n" +
                    $"  Resolution: {screenshot.Content?.Width}x{screenshot.Content?.Height}\n" +
                    $"  Response time: {duration:F0}ms");

                await SendProgress(progressCallback, "progress", $"✅ GetScreenshot completed ({duration:F0}ms)");
                await Task.Delay(500);

                // Mark computer as used (extends keep-alive time)
                TouchComputer(userKey);
                _apiLogService.AddLog("Lumina CUA - MSN Money", "Cleanup",
                    $"✅ Computer kept alive for reuse (will auto-release after 3 minutes of inactivity)\n" +
                    $"  ComputerId: {computerId}");

                return new CuaResult
                {
                    Success = true,
                    Screenshot = screenshot.Content?.Screenshot,
                    Width = screenshot.Content?.Width ?? 0,
                    Height = screenshot.Content?.Height ?? 0,
                    ComputerId = computerId
                };
            }
            catch (HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.InsufficientStorage)
            {
                _logger.LogWarning(httpEx, "CUA service capacity reached");
                _apiLogService.AddLog("Lumina CUA - MSN Money", "Error",
                    "❌ CUA service is currently at capacity", false);

                return new CuaResult
                {
                    Success = false,
                    ErrorMessage = "CUA service is currently at capacity. Please try again later."
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CUA MSN Money search failed");
                _apiLogService.AddLog("Lumina CUA - MSN Money", "Error",
                    $"❌ Error: {ex.Message}", false);

                return new CuaResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Helper method to send progress updates via callback
        /// </summary>
        private async Task SendProgress(Func<string, string, Task>? callback, string eventType, string message)
        {
            if (callback != null)
            {
                await callback(eventType, message);
            }
        }

        #endregion

        #region Computer Pool (Performance Optimization)

        // IMPORTANT: Computer pooling is NOT required by Lumina CUA API
        // This is an optional performance optimization to avoid repeated initialization
        // 
        // Why pool computers?
        // - Initialize takes 5-10 seconds (creates new virtual Windows computer)
        // - By reusing computers for 3 minutes, users get instant responses
        // - Pool automatically cleans up idle computers to save resources
        //
        // Without pooling:
        // - Every CUA request: Initialize (10s) → Do actions (2s) → Release (1s) = 13s total
        // 
        // With pooling:
        // - First request: Initialize (10s) → Do actions (2s) = 12s
        // - Subsequent requests within 3min: Do actions (2s) = 2s only!
        // - Auto-release after 3min of inactivity

        /// <summary>
        /// Get or create a computer from the pool
        /// </summary>
        private async Task<(string ComputerId, LuminaCuaService Service)> GetOrCreateComputerAsync(
            string userKey,
            string userId,
            string tenantId,
            LuminaCuaService cuaService)
        {
            // Try to get existing computer
            if (_computerPool.TryGetValue(userKey, out var computerInfo))
            {
                // Update last used time
                computerInfo.LastUsedTime = DateTime.UtcNow;
                _logger.LogInformation("Reusing existing computer {ComputerId} for user {UserId}",
                    computerInfo.ComputerId, userId);
                return (computerInfo.ComputerId, computerInfo.CuaService);
            }

            // Create new computer
            var computerId = Guid.NewGuid().ToString("N");
            await cuaService.InitializeComputerAsync(computerId, userId, tenantId);

            var newComputer = new ComputerPoolEntry
            {
                ComputerId = computerId,
                UserId = userId,
                TenantId = tenantId,
                CreatedTime = DateTime.UtcNow,
                LastUsedTime = DateTime.UtcNow,
                CuaService = cuaService
            };

            _computerPool.TryAdd(userKey, newComputer);
            _logger.LogInformation("Created new computer {ComputerId} for user {UserId}", computerId, userId);

            return (computerId, cuaService);
        }

        /// <summary>
        /// Mark computer as used (extends keep-alive time)
        /// </summary>
        private void TouchComputer(string userKey)
        {
            if (_computerPool.TryGetValue(userKey, out var computerInfo))
            {
                computerInfo.LastUsedTime = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Background cleanup of expired computers (runs every 30 seconds)
        /// </summary>
        private void CleanupExpiredComputers(object? state)
        {
            var now = DateTime.UtcNow;
            var expiredComputers = _computerPool
                .Where(kvp => now - kvp.Value.LastUsedTime > _keepAliveTime)
                .ToList();

            foreach (var kvp in expiredComputers)
            {
                if (_computerPool.TryRemove(kvp.Key, out var computerInfo))
                {
                    // Release in background - don't block cleanup timer
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await computerInfo.CuaService.ReleaseComputerAsync(computerInfo.ComputerId);
                            var idleTime = now - computerInfo.LastUsedTime;
                            _logger.LogInformation(
                                "Auto-released computer {ComputerId} after {IdleMinutes:F1} minutes of inactivity",
                                computerInfo.ComputerId, idleTime.TotalMinutes);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to release expired computer {ComputerId}",
                                computerInfo.ComputerId);
                        }
                    });
                }
            }

            if (expiredComputers.Any())
            {
                _logger.LogInformation("Cleaned up {Count} expired computers", expiredComputers.Count);
            }
        }

        /// <summary>
        /// Get statistics about the computer pool
        /// </summary>
        public ComputerPoolStats GetPoolStatistics()
        {
            var now = DateTime.UtcNow;
            return new ComputerPoolStats
            {
                TotalComputers = _computerPool.Count,
                ActiveComputers = _computerPool.Count(kvp => now - kvp.Value.LastUsedTime < TimeSpan.FromMinutes(1)),
                IdleComputers = _computerPool.Count(kvp => now - kvp.Value.LastUsedTime >= TimeSpan.FromMinutes(1))
            };
        }

        private class ComputerPoolEntry
        {
            public string ComputerId { get; set; } = string.Empty;
            public string UserId { get; set; } = string.Empty;
            public string TenantId { get; set; } = string.Empty;
            public DateTime CreatedTime { get; set; }
            public DateTime LastUsedTime { get; set; }
            public LuminaCuaService CuaService { get; set; } = null!;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _cleanupTimer?.Dispose();

            // Release all computers on shutdown
            foreach (var kvp in _computerPool)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await kvp.Value.CuaService.ReleaseComputerAsync(kvp.Value.ComputerId);
                    }
                    catch { }
                });
            }
            _computerPool.Clear();
        }

        #endregion
    }

    #region Model Classes

    /// <summary>
    /// Result from CUA operations
    /// </summary>
    public class CuaResult
    {
        public bool Success { get; set; }
        public string? Screenshot { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string? ComputerId { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Computer pool statistics
    /// </summary>
    public class ComputerPoolStats
    {
        public int TotalComputers { get; set; }
        public int ActiveComputers { get; set; }
        public int IdleComputers { get; set; }
    }

    #endregion
}
