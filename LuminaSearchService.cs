using Microsoft.Lumina.Client.ApiProxy;
using Microsoft.Lumina.Client.Models.Sonicberry;
using Newtonsoft.Json;
using static Microsoft.Lumina.Common.Constants.ConstantStrings;

namespace LuminaSearchConsole
{
    /// <summary>
    /// Lumina 搜索服务 - 实现各种搜索功能
    /// </summary>
    public class LuminaSearchService
    {
        private static readonly string LuminaEndpoint = "https://luminaserviceapi-test-westus.copilotlumina.com";
        private readonly LuminaServiceApiProxy _proxy;

        public LuminaSearchService(string accessToken)
        {
            var options = new LuminaApiOptions
            {
                Endpoint = LuminaEndpoint,
                LuminaApiTokenProvider = async () => await Task.FromResult(accessToken)
            };

            var httpClientFactory = new DefaultHttpClientFactory();
            _proxy = new LuminaServiceApiProxy(options, httpClientFactory);
        }

        /// <summary>
        /// 执行基础搜索 (按照文档 Step 3 的示例)
        /// </summary>
        public async Task ExecuteBasicSearchAsync()
        {
            Console.WriteLine("创建搜索请求...");

            var searchRequest = new SearchRequest
            {
                Requests = new List<SearchRequestItem>
                {
                    new SearchRequestItem
                    {
                        Q = "Microsoft Azure Functions",
                        TopN = 3,
                        Source = SearchProviders.WebWithBing,
                        Language = "en",
                        Market = "en-US",
                        CountryCode = "us",
                        AdditionalConfig = new SearchRequestAdditionalConfig
                        {
                            MaxSemanticDocumentLength = 50
                        }
                    }
                }
            };

            await ExecuteSearchRequestAsync(searchRequest, "基础搜索");
        }

        /// <summary>
        /// 执行自定义搜索
        /// </summary>
        public async Task ExecuteCustomSearchAsync(string query, int topN = 5, string language = "en", string market = "en-US")
        {
            Console.WriteLine($"创建自定义搜索请求: {query}");

            var searchRequest = new SearchRequest
            {
                Requests = new List<SearchRequestItem>
                {
                    new SearchRequestItem
                    {
                        Q = query,
                        TopN = topN,
                        Source = SearchProviders.WebWithBing,
                        Language = language,
                        Market = market,
                        CountryCode = "us",
                        AdditionalConfig = new SearchRequestAdditionalConfig
                        {
                            MaxSemanticDocumentLength = 100
                        }
                    }
                }
            };

            await ExecuteSearchRequestAsync(searchRequest, "自定义搜索");
        }

        /// <summary>
        /// 执行搜索请求的通用方法
        /// </summary>
        private async Task ExecuteSearchRequestAsync(SearchRequest searchRequest, string searchType)
        {
            try
            {
                var request = searchRequest.Requests[0];
                Console.WriteLine($"搜索参数 ({searchType}):");
                Console.WriteLine($"  查询: {request.Q}");
                Console.WriteLine($"  结果数量: {request.TopN}");
                Console.WriteLine($"  语言: {request.Language}");
                Console.WriteLine($"  市场: {request.Market}");
                Console.WriteLine($"  搜索源: {request.Source}");
                Console.WriteLine();

                Console.WriteLine("🔍 正在执行搜索...");

                var searchResult = await _proxy.SearchAsync(searchRequest);

                Console.WriteLine($"✅ {searchType}完成!");
                Console.WriteLine();

                DisplaySearchResults(searchResult);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ {searchType}失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 显示搜索结果
        /// </summary>
        private void DisplaySearchResults(dynamic searchResult)
        {
            Console.WriteLine("📊 搜索结果:");
            Console.WriteLine("=============");

            if (searchResult?.Results != null && searchResult.Results.Count > 0)
            {
                Console.WriteLine($"找到 {searchResult.Results.Count} 个搜索结果:");
                for (int i = 0; i < searchResult.Results.Count; i++)
                {
                    var result = searchResult.Results[i];
                    Console.WriteLine($"{i + 1}. 标题: {result.Title ?? "无标题"}");
                    Console.WriteLine($"   URL: {result.Url ?? "无URL"}");
                    Console.WriteLine();
                }
            }
            else
            {
                Console.WriteLine("未找到搜索结果");
            }

            // 显示完整的JSON响应 (用于调试)
            Console.WriteLine("🔧 完整响应 (JSON):");
            Console.WriteLine("==================");
            var jsonResponse = JsonConvert.SerializeObject(searchResult, Formatting.Indented);
            Console.WriteLine(jsonResponse);
        }
    }

    /// <summary>
    /// 简单的HTTP客户端工厂实现
    /// </summary>
    public class DefaultHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient();
        }
    }
}