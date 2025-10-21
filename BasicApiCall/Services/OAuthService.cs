using Microsoft.Identity.Client;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

public class OAuthService
{
    // 配置常量 - 根据您提供的信息
    private const string TenantId = "72f988bf-86f1-41af-91ab-2d7cd011db47";
    private const string ClientId = "63696678-8070-4259-91d9-292979db05c4";
    private const string Scope = "67f912ef-f692-43d3-9b97-3702aa2fd840/.default";
    private const string RedirectUri = "http://localhost:8400";
    
    private IPublicClientApplication? _app;
    private IAccount? _currentAccount;
    private string? _accessToken;
    private HttpListener? _httpListener;
    
    /// <summary>
    /// 获取当前访问令牌
    /// </summary>
    public string? AccessToken => _accessToken;
    
    /// <summary>
    /// 检查是否已认证
    /// </summary>
    public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken) && _currentAccount != null;
    
    /// <summary>
    /// 获取当前用户账户信息
    /// </summary>
    public IAccount? CurrentAccount => _currentAccount;
    
    /// <summary>
    /// 初始化认证流程
    /// </summary>
    public async Task<bool> InitializeAuthenticationAsync()
    {
        try
        {
            Console.WriteLine("🔐 Starting OAuth authentication flow...");
            
            // 初始化MSAL公共客户端应用程序
            await InitializeMsalApp();
            
            // 首先尝试静默获取令牌
            if (await TryAcquireTokenSilentlyAsync())
            {
                Console.WriteLine("✅ Authentication successful via cached token");
                return true;
            }
            
            // 如果静默获取失败，进行交互式认证
            return await AcquireTokenInteractivelyAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Authentication initialization failed: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// 初始化MSAL应用程序
    /// </summary>
    private async Task InitializeMsalApp()
    {
        var builder = PublicClientApplicationBuilder
            .Create(ClientId)
            .WithAuthority($"https://login.microsoftonline.com/{TenantId}")
            .WithRedirectUri(RedirectUri)
            .WithLogging((level, message, containsPii) =>
            {
                if (level <= Microsoft.Identity.Client.LogLevel.Warning)
                {
                    Console.WriteLine($"MSAL [{level}]: {message}");
                }
            }, Microsoft.Identity.Client.LogLevel.Warning, enablePiiLogging: false, enableDefaultPlatformLogging: true);
        
        _app = builder.Build();
        
        // 尝试从缓存加载账户
        var accounts = await _app.GetAccountsAsync();
        _currentAccount = accounts.FirstOrDefault();
        
        Console.WriteLine($"📋 Found {accounts.Count()} cached accounts");
    }
    
    /// <summary>
    /// 尝试静默获取令牌（从缓存）
    /// </summary>
    private async Task<bool> TryAcquireTokenSilentlyAsync()
    {
        if (_app == null || _currentAccount == null)
        {
            Console.WriteLine("⚠️ No cached account found, interactive authentication required");
            return false;
        }
        
        try
        {
            Console.WriteLine("🔄 Attempting silent token acquisition...");
            
            var result = await _app
                .AcquireTokenSilent(new[] { Scope }, _currentAccount)
                .ExecuteAsync();
            
            _accessToken = result.AccessToken;
            _currentAccount = result.Account;
            
            Console.WriteLine($"✅ Silent token acquisition successful");
            Console.WriteLine($"👤 User: {_currentAccount.Username}");
            Console.WriteLine($"🔑 Token expires: {result.ExpiresOn:yyyy-MM-dd HH:mm:ss}");
            
            return true;
        }
        catch (MsalUiRequiredException)
        {
            Console.WriteLine("⚠️ Silent token acquisition failed - user interaction required");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Silent token acquisition failed: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// 交互式获取令牌（弹出浏览器）
    /// </summary>
    private async Task<bool> AcquireTokenInteractivelyAsync()
    {
        if (_app == null)
        {
            throw new InvalidOperationException("MSAL application not initialized");
        }
        
        try
        {
            Console.WriteLine("🌐 Starting interactive authentication...");
            Console.WriteLine("📱 Browser window will open for login...");
            
            var result = await _app
                .AcquireTokenInteractive(new[] { Scope })
                .WithPrompt(Prompt.SelectAccount) // 允许用户选择账户
                .WithExtraQueryParameters("response_mode=query") // 确保使用查询参数返回
                .ExecuteAsync();
            
            _accessToken = result.AccessToken;
            _currentAccount = result.Account;
            
            Console.WriteLine($"✅ Interactive authentication successful!");
            Console.WriteLine($"👤 User: {_currentAccount.Username}");
            Console.WriteLine($"🔑 Token expires: {result.ExpiresOn:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"🔧 Scopes: {string.Join(", ", result.Scopes)}");
            
            return true;
        }
        catch (MsalClientException ex) when (ex.ErrorCode == MsalError.AuthenticationCanceledError)
        {
            Console.WriteLine("⚠️ Authentication was cancelled by user");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Interactive authentication failed: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner exception: {ex.InnerException.Message}");
            }
            return false;
        }
        finally
        {
            StopCallbackListener();
        }
    }
    
    /// <summary>
    /// 启动HTTP监听器处理OAuth回调
    /// </summary>
    private Task StartCallbackListener()
    {
        try
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add("http://localhost:8400/");
            _httpListener.Start();
            
            Console.WriteLine("🎧 Started callback listener on http://localhost:8400");
            
            // 在后台处理请求
            _ = Task.Run(HandleCallbackRequests);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Failed to start callback listener: {ex.Message}");
            return Task.CompletedTask;
        }
    }
    
    /// <summary>
    /// 处理OAuth回调请求
    /// </summary>
    private async Task HandleCallbackRequests()
    {
        if (_httpListener == null) return;
        
        try
        {
            while (_httpListener.IsListening)
            {
                var context = await _httpListener.GetContextAsync();
                var request = context.Request;
                var response = context.Response;
                
                Console.WriteLine($"📨 Received callback: {request.Url}");
                
                // Check if this is an OAuth callback (contains code or error parameters)
                var query = request.Url?.Query;
                if (!string.IsNullOrEmpty(query) && (query.Contains("code=") || query.Contains("error=")))
                {
                    // 返回成功页面
                    var successHtml = GetCallbackSuccessHtml();
                    var buffer = Encoding.UTF8.GetBytes(successHtml);
                    
                    response.ContentType = "text/html; charset=utf-8";
                    response.ContentLength64 = buffer.Length;
                    response.StatusCode = 200;
                    
                    await response.OutputStream.WriteAsync(buffer);
                    response.OutputStream.Close();
                    
                    Console.WriteLine("✅ Callback handled successfully");
                    break; // 成功处理回调后退出循环
                }
                else
                {
                    // 返回404
                    response.StatusCode = 404;
                    response.OutputStream.Close();
                }
            }
        }
        catch (HttpListenerException)
        {
            // 监听器被停止，正常情况
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error handling callback: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 停止HTTP监听器
    /// </summary>
    private void StopCallbackListener()
    {
        try
        {
            _httpListener?.Stop();
            _httpListener?.Close();
            _httpListener = null;
            Console.WriteLine("🛑 Callback listener stopped");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Error stopping callback listener: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 刷新访问令牌
    /// </summary>
    public async Task<bool> RefreshTokenAsync()
    {
        if (_app == null || _currentAccount == null)
        {
            Console.WriteLine("⚠️ Cannot refresh token - no account available");
            return false;
        }
        
        try
        {
            Console.WriteLine("🔄 Refreshing access token...");
            
            var result = await _app
                .AcquireTokenSilent(new[] { Scope }, _currentAccount)
                .ExecuteAsync();
            
            _accessToken = result.AccessToken;
            
            Console.WriteLine($"✅ Token refreshed successfully");
            Console.WriteLine($"🔑 New token expires: {result.ExpiresOn:yyyy-MM-dd HH:mm:ss}");
            
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Token refresh failed: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// 退出登录
    /// </summary>
    public async Task<bool> SignOutAsync()
    {
        if (_app == null || _currentAccount == null)
        {
            Console.WriteLine("⚠️ No active session to sign out");
            return false;
        }
        
        try
        {
            Console.WriteLine("🚪 Signing out...");
            
            await _app.RemoveAsync(_currentAccount);
            
            _accessToken = null;
            _currentAccount = null;
            
            Console.WriteLine("✅ Successfully signed out");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Sign out failed: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// 获取用户信息
    /// </summary>
    public UserInfo? GetUserInfo()
    {
        if (_currentAccount == null)
        {
            return null;
        }
        
        return new UserInfo
        {
            Username = _currentAccount.Username,
            DisplayName = _currentAccount.Username, // MSAL可能不提供DisplayName
            HomeAccountId = _currentAccount.HomeAccountId?.Identifier,
            Environment = _currentAccount.Environment
        };
    }
    
    /// <summary>
    /// 异步获取当前用户信息（为了兼容性）
    /// </summary>
    public async Task<UserInfo?> GetCurrentUserAsync()
    {
        // 如果没有令牌，尝试刷新
        if (!IsTokenValid())
        {
            await RefreshTokenAsync();
        }
        
        return GetUserInfo();
    }
    
    /// <summary>
    /// 验证令牌是否有效
    /// </summary>
    public bool IsTokenValid()
    {
        return !string.IsNullOrEmpty(_accessToken) && _currentAccount != null;
    }
    
    /// <summary>
    /// 获取回调成功页面HTML
    /// </summary>
    private string GetCallbackSuccessHtml()
    {
        return """
<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <title>Authentication Successful</title>
    <style>
        body { 
            font-family: 'Segoe UI', Arial, sans-serif; 
            text-align: center; 
            margin: 0;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            height: 100vh;
            display: flex;
            align-items: center;
            justify-content: center;
        }
        .container {
            background: rgba(255,255,255,0.1);
            padding: 60px 40px;
            border-radius: 20px;
            backdrop-filter: blur(20px);
            box-shadow: 0 8px 32px rgba(0,0,0,0.3);
            max-width: 500px;
        }
        .success-icon {
            font-size: 4em;
            margin-bottom: 20px;
            animation: bounce 0.6s ease-in-out;
        }
        h1 { 
            font-size: 2.5em; 
            margin: 0 0 20px 0;
            font-weight: 300;
        }
        p { 
            font-size: 1.3em; 
            margin-bottom: 30px;
            opacity: 0.9;
        }
        .progress {
            width: 100%;
            height: 4px;
            background: rgba(255,255,255,0.3);
            border-radius: 2px;
            overflow: hidden;
            margin-top: 20px;
        }
        .progress-bar {
            width: 0%;
            height: 100%;
            background: linear-gradient(90deg, #4CAF50, #45a049);
            animation: progress 2s ease-in-out forwards;
        }
        @keyframes bounce {
            0%, 20%, 50%, 80%, 100% { transform: translateY(0); }
            40% { transform: translateY(-10px); }
            60% { transform: translateY(-5px); }
        }
        @keyframes progress {
            from { width: 0%; }
            to { width: 100%; }
        }
    </style>
</head>
<body>
    <div class="container">
        <div class="success-icon">✅</div>
        <h1>Authentication Successful!</h1>
        <p>You have successfully authenticated with Microsoft Azure AD.</p>
        <p>You can now close this window and return to the application.</p>
        <div class="progress">
            <div class="progress-bar"></div>
        </div>
        <script>
            // 自动关闭窗口
            setTimeout(() => {
                try {
                    window.close();
                } catch (e) {
                    // 如果无法关闭窗口，显示提示
                    document.body.innerHTML = `
                        <div class="container">
                            <div class="success-icon">✅</div>
                            <h1>Authentication Complete!</h1>
                            <p>Please manually close this window and return to the application.</p>
                        </div>
                    `;
                }
            }, 3000);
        </script>
    </div>
</body>
</html>
""";
    }
    
    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        StopCallbackListener();
    }
}

/// <summary>
/// 用户信息类
/// </summary>
public class UserInfo
{
    public string? Username { get; set; }
    public string? DisplayName { get; set; }
    public string? HomeAccountId { get; set; }
    public string? Environment { get; set; }
}
