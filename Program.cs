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

            var app = builder.Build();

            // Configure error handling
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            // Configure ports:
            // - Web app: http://localhost:5000
            // - MSAL auth callback: http://localhost:8400 (configured in OboTokenService)
            app.Urls.Add("http://localhost:5000");

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