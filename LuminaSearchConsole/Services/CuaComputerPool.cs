using System.Collections.Concurrent;

namespace LuminaSearchConsole.Services
{
    /// <summary>
    /// Manages a pool of CUA virtual computers with delayed release
    /// Computers are kept alive for reuse within a 3-minute window
    /// </summary>
    public class CuaComputerPool
    {
        private readonly ConcurrentDictionary<string, ComputerInfo> _computers = new();
        private readonly TimeSpan _keepAliveTime = TimeSpan.FromMinutes(3);
        private readonly Timer _cleanupTimer;
        private readonly ILogger<CuaComputerPool> _logger;

        public CuaComputerPool(ILogger<CuaComputerPool> logger)
        {
            _logger = logger;
            // Run cleanup every 30 seconds
            _cleanupTimer = new Timer(CleanupExpiredComputers, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        /// <summary>
        /// Get or create a computer for the user session
        /// </summary>
        public async Task<string> GetOrCreateComputerAsync(
            string userId, 
            string tenantId,
            LuminaCuaService cuaService)
        {
            var userKey = $"{userId}_{tenantId}";

            // Try to get existing computer
            if (_computers.TryGetValue(userKey, out var computerInfo))
            {
                // Update last used time
                computerInfo.LastUsedTime = DateTime.UtcNow;
                _logger.LogInformation("Reusing existing computer {ComputerId} for user {UserId}", 
                    computerInfo.ComputerId, userId);
                return computerInfo.ComputerId;
            }

            // Create new computer
            var computerId = Guid.NewGuid().ToString("N");
            await cuaService.InitializeComputerAsync(computerId, userId, tenantId);

            var newComputer = new ComputerInfo
            {
                ComputerId = computerId,
                UserId = userId,
                TenantId = tenantId,
                CreatedTime = DateTime.UtcNow,
                LastUsedTime = DateTime.UtcNow,
                CuaService = cuaService
            };

            _computers.TryAdd(userKey, newComputer);
            _logger.LogInformation("Created new computer {ComputerId} for user {UserId}", computerId, userId);

            return computerId;
        }

        /// <summary>
        /// Mark computer as used (updates last used time)
        /// </summary>
        public void TouchComputer(string userId, string tenantId)
        {
            var userKey = $"{userId}_{tenantId}";
            if (_computers.TryGetValue(userKey, out var computerInfo))
            {
                computerInfo.LastUsedTime = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Manually release a computer (immediate cleanup)
        /// </summary>
        public async Task ReleaseComputerAsync(string userId, string tenantId)
        {
            var userKey = $"{userId}_{tenantId}";
            if (_computers.TryRemove(userKey, out var computerInfo))
            {
                try
                {
                    await computerInfo.CuaService.ReleaseComputerAsync(computerInfo.ComputerId);
                    _logger.LogInformation("Manually released computer {ComputerId}", computerInfo.ComputerId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to release computer {ComputerId}", computerInfo.ComputerId);
                }
            }
        }

        /// <summary>
        /// Background cleanup of expired computers
        /// </summary>
        private void CleanupExpiredComputers(object? state)
        {
            var now = DateTime.UtcNow;
            var expiredComputers = _computers
                .Where(kvp => now - kvp.Value.LastUsedTime > _keepAliveTime)
                .ToList();

            foreach (var kvp in expiredComputers)
            {
                if (_computers.TryRemove(kvp.Key, out var computerInfo))
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
        public PoolStatistics GetStatistics()
        {
            var now = DateTime.UtcNow;
            return new PoolStatistics
            {
                TotalComputers = _computers.Count,
                ActiveComputers = _computers.Count(kvp => now - kvp.Value.LastUsedTime < TimeSpan.FromMinutes(1)),
                IdleComputers = _computers.Count(kvp => now - kvp.Value.LastUsedTime >= TimeSpan.FromMinutes(1))
            };
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            
            // Release all computers on shutdown
            foreach (var kvp in _computers)
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
            _computers.Clear();
        }

        private class ComputerInfo
        {
            public string ComputerId { get; set; } = string.Empty;
            public string UserId { get; set; } = string.Empty;
            public string TenantId { get; set; } = string.Empty;
            public DateTime CreatedTime { get; set; }
            public DateTime LastUsedTime { get; set; }
            public LuminaCuaService CuaService { get; set; } = null!;
        }

        public class PoolStatistics
        {
            public int TotalComputers { get; set; }
            public int ActiveComputers { get; set; }
            public int IdleComputers { get; set; }
        }
    }
}
