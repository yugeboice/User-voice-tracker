namespace LuminaSearchConsole
{
    /// <summary>
    /// Lumina API Demo Application
    /// Demonstrates OAuth authentication, Search API, and Open API integration
    /// </summary>
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Load configuration from appsettings.json
            // Partners can customize these values for their environment
            var luminaConfig = builder.Configuration.GetSection("LuminaConfiguration").Get<Configuration.LuminaConfiguration>() 
                ?? new Configuration.LuminaConfiguration();
            var azureAdConfig = builder.Configuration.GetSection("AzureAd").Get<Configuration.AzureAdConfiguration>() 
                ?? new Configuration.AzureAdConfiguration();
            var webServerConfig = builder.Configuration.GetSection("WebServer").Get<Configuration.WebServerConfiguration>() 
                ?? new Configuration.WebServerConfiguration();

            // Register configurations as singletons for dependency injection
            builder.Services.AddSingleton(luminaConfig);
            builder.Services.AddSingleton(azureAdConfig);
            builder.Services.AddSingleton(webServerConfig);

            // Configure server URLs from configuration
            // Partners can change port in appsettings.json
            if (!string.IsNullOrEmpty(webServerConfig.Urls))
            {
                builder.WebHost.UseUrls(webServerConfig.Urls);
            }

            // MVC support for web interface
            builder.Services.AddControllersWithViews();
            
            // Session management for storing access tokens
            // Token is stored in session after successful login
            builder.Services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromMinutes(30);
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
            });

            // Register OBO authentication service
            // This service handles OAuth 2.0 token acquisition
            builder.Services.AddScoped<OboTokenService>();

            // Register ApiLogService as singleton to track API calls across all requests
            // This helps partners understand API usage patterns
            builder.Services.AddSingleton<LuminaSearchConsole.Services.ApiLogService>();

            var app = builder.Build();

            // Configure error handling
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            if (!app.Environment.IsDevelopment())
            {
                app.UseHttpsRedirection();
            }
            
            app.UseStaticFiles();
            app.UseRouting();
            app.UseSession();  // Enable session before authorization
            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            app.Run();
        }
    }
}