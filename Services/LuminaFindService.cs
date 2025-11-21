using Microsoft.Lumina.Client.ApiProxy;
using Microsoft.Lumina.Client.Models.Sonicberry;
// Models like CompanyInfo, InfoField are in SharedModels.cs (same namespace)

namespace LuminaSearchConsole.Services
{
    /// <summary>
    /// Lumina Find API Service
    /// 
    /// Demonstrates: POST /api/sonicberry/find
    /// Purpose: Search for specific patterns within opened web page content
    /// 
    /// Common Use Case:
    /// After opening a page with Open API, use Find API to extract structured information
    /// like company details from Wikipedia infoboxes.
    /// </summary>
    public class LuminaFindService
    {
        private readonly string _luminaEndpoint;
        private readonly string _accessToken;
        private readonly LuminaServiceApiProxy _proxy;

        public LuminaFindService(string accessToken, LuminaConfiguration luminaConfig)
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
        /// Find specific patterns within opened page content
        /// 
        /// Example: Search for "Founded" in a Wikipedia company page
        /// Returns: Matching text with line numbers
        /// </summary>
        public async Task<FindResponse> FindContentAsync(string pattern, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                throw new ArgumentException("Pattern cannot be empty", nameof(pattern));
            }

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new ArgumentException("Session ID is required for Find operation", nameof(sessionId));
            }

            try
            {
                var findRequest = new FindRequest
                {
                    Requests = new List<FindRequestItem>
                    {
                        new FindRequestItem
                        {
                            Pattern = pattern,
                            PageContext = new PageContextInfo
                            {
                                Turn = 0,
                                Action = "view",
                                Id = 0
                            }
                        }
                    },
                    ToolState = new ToolState
                    {
                        SessionId = sessionId
                    }
                };

                var response = await _proxy.FindAsync(findRequest);
                return response!;
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Network error occurred during Find operation. Please check your internet connection.", ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to find pattern '{pattern}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Extract company information from Wikipedia using Find API
        /// 
        /// Process:
        /// 1. Takes a Wikipedia URL
        /// 2. Searches for common infobox fields (Founded, Headquarters, Revenue, etc.)
        /// 3. Returns structured company information
        /// </summary>
        public async Task<CompanyInfo?> ExtractCompanyInfoAsync(string url, string sessionId)
        {
            try
            {
                // Search for key-value pairs commonly found in Wikipedia company infoboxes
                string[] patterns = { "Founded", "Headquarters", "Revenue", "Industry", "Type" };
                
                var companyInfo = new CompanyInfo
                {
                    Url = url,
                    Fields = new List<InfoField>()
                };
                
                foreach (var pattern in patterns)
                {
                    try
                    {
                        var findResponse = await FindContentAsync(pattern, sessionId);
                        
                        if (findResponse?.Results != null && findResponse.Results.Count > 0)
                        {
                            var firstMatch = findResponse.Results.FirstOrDefault();
                            if (firstMatch != null)
                            {
                                var matchContent = firstMatch.Template ?? "";
                                var cleanContent = ExtractInfoboxValue(matchContent, pattern);
                                
                                companyInfo.Fields.Add(new InfoField
                                {
                                    LineNumber = firstMatch.LineIdx ?? 0,
                                    Content = cleanContent,
                                    FieldName = pattern
                                });
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Skip fields that can't be found
                    }
                }
                
                return companyInfo.Fields.Any() ? companyInfo : null;
            }
            catch
            {
                throw;
            }
        }

        /// <summary>
        /// Extract clean value from Wikipedia infobox content
        /// </summary>
        private string ExtractInfoboxValue(string rawContent, string fieldName)
        {
            // Remove link markers like [[[link_0]]]
            var content = System.Text.RegularExpressions.Regex.Replace(rawContent, @"\[\[\[link_\d+\]\]\]", "");
            
            // Strategy 1: Look for "FieldName:" pattern
            var colonPattern = $@"{fieldName}\s*:\s*([^\n\|]+)";
            var colonMatch = System.Text.RegularExpressions.Regex.Match(content, colonPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (colonMatch.Success && colonMatch.Groups.Count > 1)
            {
                var value = colonMatch.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(value) && value.Length > 3)
                {
                    return CleanAndLimit(value, 150);
                }
            }
            
            // Strategy 2: Look for pipe-delimited table format "| FieldName | Value"
            var lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("|") && line.Contains(fieldName, StringComparison.OrdinalIgnoreCase))
                {
                    var cells = line.Split('|', StringSplitOptions.RemoveEmptyEntries);
                    if (cells.Length >= 2)
                    {
                        var fieldCell = cells[0].Trim();
                        if (fieldCell.Equals(fieldName, StringComparison.OrdinalIgnoreCase) && cells.Length > 1)
                        {
                            var valueCell = cells[1].Trim();
                            if (!string.IsNullOrWhiteSpace(valueCell) && valueCell.Length > 3)
                            {
                                return CleanAndLimit(valueCell, 150);
                            }
                        }
                    }
                }
            }
            
            // Strategy 3: Simple extraction
            var fieldIndex = content.IndexOf(fieldName, StringComparison.OrdinalIgnoreCase);
            if (fieldIndex >= 0)
            {
                var afterField = content.Substring(fieldIndex + fieldName.Length);
                afterField = System.Text.RegularExpressions.Regex.Replace(afterField, @"^[\s\|:\-]+", "");
                
                var match = System.Text.RegularExpressions.Regex.Match(afterField, @"^([^\n\|]{5,150})");
                if (match.Success)
                {
                    return CleanAndLimit(match.Groups[1].Value, 150);
                }
            }
            
            return "N/A";
        }
        
        private string CleanAndLimit(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "N/A";
            
            value = System.Text.RegularExpressions.Regex.Replace(value, @"\s+", " ").Trim();
            value = value.Replace("**", "").Replace("__", "");
            
            if (value.Length > maxLength)
            {
                value = value.Substring(0, maxLength).Trim() + "...";
            }
            
            return value;
        }
    }
}
