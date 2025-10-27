using Microsoft.AspNetCore.Mvc;
using LuminaSearchConsole.Models;
using System.Diagnostics;

namespace LuminaSearchConsole.Controllers
{
    public class HomeController : Controller
    {
        private readonly OboTokenService _oboTokenService;
        private readonly ILogger<HomeController> _logger;

        public HomeController(OboTokenService oboTokenService, ILogger<HomeController> logger)
        {
            _oboTokenService = oboTokenService;
            _logger = logger;
        }

        public IActionResult Index()
        {
            var model = new SearchViewModel
            {
                IsAuthenticated = !string.IsNullOrEmpty(HttpContext.Session.GetString("AccessToken"))
            };
            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> Login()
        {
            try
            {
                var token = await _oboTokenService.GetUserTokenAsync();
                HttpContext.Session.SetString("AccessToken", token);
                
                TempData["Message"] = "Successfully logged in!";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login failed");
                TempData["Error"] = $"Login failed: {ex.Message}";
                return RedirectToAction("Index");
            }
        }

        [HttpPost]
        public async Task<IActionResult> Search(SearchViewModel model)
        {
            if (string.IsNullOrEmpty(model.Query))
            {
                TempData["Error"] = "Please enter a search query";
                return RedirectToAction("Index");
            }

            var token = HttpContext.Session.GetString("AccessToken");
            if (string.IsNullOrEmpty(token))
            {
                TempData["Error"] = "Please log in first";
                return RedirectToAction("Index");
            }

            try
            {
                var searchService = new LuminaSearchService(token);
                var results = await searchService.ExecuteWebSearchAsync(model.Query, model.TopResults);
                
                model.SearchResults = results;
                model.IsAuthenticated = true;
                model.HasSearched = true;
                
                return View("Index", model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Search failed for query: {Query}", model.Query);
                TempData["Error"] = $"Search failed: {ex.Message}";
                model.IsAuthenticated = true;
                return View("Index", model);
            }
        }

        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            try
            {
                await _oboTokenService.SignOutAsync();
                HttpContext.Session.Clear();
                TempData["Message"] = "Successfully logged out!";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Logout failed");
                TempData["Error"] = $"Logout failed: {ex.Message}";
            }
            
            return RedirectToAction("Index");
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}