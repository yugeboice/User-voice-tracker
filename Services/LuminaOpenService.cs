using Microsoft.Lumina.Client.ApiProxy;
using Microsoft.Lumina.Client.Models.Sonicberry;
// Models like OpenContentResult, PageLink are in SharedModels.cs (same namespace)

namespace LuminaSearchConsole.Services
{
    /// <summary>
    /// Lumina Open & Click API Service
    /// 
    /// Demonstrates:
    /// - POST /api/sonicberry/open - Extract full content from URLs
    /// - POST /api/sonicberry/click - Navigate through links in opened pages
    /// 
    /// Common Use Case:
    /// 1. Open a URL to get full page content and available links
    /// 2. Click on links to navigate deeper into related pages
    /// 3. Maintain session context for multi-step navigation
    /// </summary>
    public class LuminaOpenService
    {
        private readonly string _luminaEndpoint;
        private readonly string _accessToken;
        private readonly LuminaServiceApiProxy _proxy;

        public LuminaOpenService(string accessToken, LuminaConfiguration luminaConfig)
        {
            _accessToken = accessToken;
            _luminaEndpoint = luminaConfig.ApiEndpoint;

            var options = new LuminaApiOptions
            {
                Endpoint = _luminaEndpoint,
                LuminaApiTokenProvider = async () => await Task.FromResult(accessToken)
            };

            var httpClientFactory = new DefaultHttpClientFactory();
            _proxy = new LuminaServiceApiProxy(options, httpClientFactory);
        }

        /// <summary>
        /// Open and retrieve full content from a URL
        /// 
        /// Returns: Full page text, clickable links, and session ID for navigation
        /// </summary>
        public async Task<OpenContentResult> OpenContentWithLinksAsync(string url, string? sessionId = null)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("URL cannot be empty", nameof(url));
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                throw new ArgumentException($"Invalid URL format: {url}", nameof(url));
            }

            try
            {
                var openRequest = new OpenRequest
                {
                    Requests = new List<OpenRequestItem>
                    {
                        new OpenRequestItem
                        {
                            RefId = url
                        }
                    },
                    ToolState = string.IsNullOrEmpty(sessionId) ? null : new ToolState { SessionId = sessionId }
                };

                var response = await _proxy.OpenAsync(openRequest);
                
                if (response != null && response.Pages != null && response.Pages.Count > 0)
                {
                    var page = response.Pages[0];
                    string content = page.Content ?? "";
                    string newSessionId = response.ToolState?.SessionId ?? "";
                    
                    // Extract links
                    var linksList = new List<dynamic>();
                    if (page.Doc?.Links != null)
                    {
                        foreach (var link in page.Doc.Links)
                        {
                            linksList.Add(link);
                            if (linksList.Count >= 20) break;
                        }
                    }
                    
                    var pageContext = page.PageContext;
                    
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        throw new Exception($"No content available from the URL: {url}");
                    }
                    
                    return new OpenContentResult
                    {
                        Content = content,
                        SessionId = newSessionId,
                        Links = linksList,
                        PageContext = pageContext,
                        Url = page.Url ?? url,
                        Title = page.Title ?? ""
                    };
                }
                else
                {
                    throw new Exception($"No content available from the URL: {url}");
                }
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Network error occurred while accessing '{url}'. Please check your internet connection.", ex);
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to open content from '{url}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Click a link within an opened page
        /// 
        /// Requires: Session ID and page context from previous Open operation
        /// Returns: Content of the clicked page with new navigation context
        /// </summary>
        public async Task<OpenContentResult> ClickLinkAsync(string sessionId, string linkId, dynamic pageContext)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("Session ID cannot be empty", nameof(sessionId));
            }

            if (string.IsNullOrWhiteSpace(linkId))
            {
                throw new ArgumentException("Link ID cannot be empty", nameof(linkId));
            }

            try
            {
                var clickRequest = new ClickRequest
                {
                    Requests = new List<ClickRequestItem>
                    {
                        new ClickRequestItem
                        {
                            RefId = linkId,
                            PageContext = new PageContextInfo
                            {
                                Turn = (int)(pageContext.Turn ?? 0),
                                Action = pageContext.Action?.ToString() ?? "view",
                                Id = (int)(pageContext.Id ?? 0)
                            }
                        }
                    },
                    ToolState = new ToolState
                    {
                        SessionId = sessionId
                    }
                };

                var clickResponse = await _proxy.ClickAsync(clickRequest);
                
                if (clickResponse != null && clickResponse.Pages != null && clickResponse.Pages.Count > 0)
                {
                    var page = clickResponse.Pages[0];
                    string content = page.Content ?? "";
                    
                    // Extract links
                    var linksList = new List<dynamic>();
                    if (page.Doc?.Links != null)
                    {
                        foreach (var link in page.Doc.Links)
                        {
                            linksList.Add(link);
                            if (linksList.Count >= 20) break;
                        }
                    }
                    
                    var newPageContext = page.PageContext;
                    
                    return new OpenContentResult
                    {
                        Content = content,
                        SessionId = sessionId,
                        Links = linksList,
                        PageContext = newPageContext,
                        Url = page.Url ?? "",
                        Title = page.Title ?? ""
                    };
                }
                else
                {
                    throw new Exception($"No content available from clicked link");
                }
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Network error occurred while clicking link. Please check your internet connection.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to click link '{linkId}': {ex.Message}", ex);
            }
        }
    }
}
