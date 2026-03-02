using System.Text;
using System.Text.Json;
using Microsoft.Lumina.Client.Models.Sonicberry;

namespace MinimalApiCall;

/// <summary>
/// 智能对话服务 - 实现两步走策略：对话理解 → 搜索词生成 → 并行搜索 → 生成回答
/// </summary>
public class SmartChatService
{
    private readonly SearchApi _searchApi;
    private readonly HttpClient _httpClient;
    private readonly string _llmEndpoint;
    private readonly string _model;
    private readonly LocalKnowledgeService? _knowledgeService;
    private PptGeneratorService? _pptGenerator;
    
    // 存储对话历史（每个会话ID对应一个历史记录）
    private readonly Dictionary<string, List<ChatMessage>> _conversationHistory;
    
    // 存储Agent会话状态（每个会话ID对应一个Agent状态）
    private readonly Dictionary<string, AgentSession> _agentSessions;

    public SmartChatService(SearchApi searchApi, string llmEndpoint = "http://localhost:4141", string model = "gpt-4", LocalKnowledgeService? knowledgeService = null)
    {
        _searchApi = searchApi;
        _httpClient = new HttpClient();
        _llmEndpoint = llmEndpoint;
        _model = model;
        _knowledgeService = knowledgeService;
        
        // 初始化PPT生成器
        if (knowledgeService != null)
        {
            var pptPath = knowledgeService.GetKnowledgePath() ?? Path.Combine(Path.GetTempPath(), "LuminaPPT");
            _pptGenerator = new PptGeneratorService(pptPath);
        }
        _conversationHistory = new Dictionary<string, List<ChatMessage>>();
        _agentSessions = new Dictionary<string, AgentSession>();
    }

    /// <summary>
    /// 智能对话处理 - Agent驱动的完整流程
    /// </summary>
    public async Task<SmartChatResponse> ChatAsync(string sessionId, string userMessage, int topN = 5)
    {
        var response = new SmartChatResponse
        {
            SessionId = sessionId,
            UserMessage = userMessage,
            Timestamp = DateTime.UtcNow
        };

        try
        {
            // ===== 新增：Agent驱动流程 =====
            // 1. 检查或初始化Agent会话
            if (!_agentSessions.ContainsKey(sessionId) && _knowledgeService != null)
            {
                var agentPrompt = await _knowledgeService.SelectAgentByIntentAsync(userMessage);
                if (agentPrompt != null)
                {
                    _agentSessions[sessionId] = new AgentSession
                    {
                        AgentPrompt = agentPrompt,
                        IsActive = true,
                        StartTime = DateTime.UtcNow
                    };
                    response.AgentMode = true;
                }
            }

            // 2. 如果Agent会话激活，让Agent先处理
            if (_agentSessions.ContainsKey(sessionId) && _agentSessions[sessionId].IsActive)
            {
                var agentResponse = await ProcessWithAgentAsync(sessionId, userMessage, topN);
                response.Intent = agentResponse.Intent;
                response.SearchQueries = agentResponse.SearchQueries;
                response.SearchResultsCount = agentResponse.SearchResultsCount;
                response.Answer = agentResponse.Answer;
                response.AgentMode = true;
                
                SaveToHistory(sessionId, userMessage, agentResponse.Answer);
                return response;
            }

            // ===== 原有流程：非Agent模式 =====
            // 步骤1: 理解对话 + 提取搜索意图
            var intent = await UnderstandIntentAsync(sessionId, userMessage);
            response.Intent = intent;

            // 步骤2: 生成搜索词
            var queries = await GenerateSearchQueriesAsync(intent, userMessage);
            response.SearchQueries = queries;

            if (queries.Count == 0)
            {
                response.Answer = "抱歉，我无法理解你的问题。请尝试换一种方式表达。";
                return response;
            }

            // 步骤3: 并行搜索多个query
            var allResults = await ParallelSearchAsync(queries, topN);
            response.SearchResultsCount = allResults.Count;

            if (allResults.Count == 0)
            {
                response.Answer = "抱歉，没有找到相关的搜索结果。请尝试使用不同的关键词。";
                return response;
            }

            // 步骤4: 基于搜索结果生成回答
            var answer = await GenerateAnswerAsync(sessionId, userMessage, intent, allResults);
            response.Answer = answer;

            // 保存对话历史
            SaveToHistory(sessionId, userMessage, answer);
        }
        catch (Exception ex)
        {
            response.Answer = $"处理过程中出现错误: {ex.Message}";
            response.Error = ex.Message;
        }

        return response;
    }

    /// <summary>
    /// Agent驱动的对话处理流程
    /// </summary>
    private async Task<SmartChatResponse> ProcessWithAgentAsync(string sessionId, string userMessage, int topN)
    {
        var agentSession = _agentSessions[sessionId];
        var response = new SmartChatResponse
        {
            SessionId = sessionId,
            UserMessage = userMessage,
            Timestamp = DateTime.UtcNow,
            AgentMode = true
        };

        // 1. Agent分析用户输入，判断是否需要搜索
        var agentDecision = await AgentAnalyzeAsync(sessionId, userMessage, agentSession.AgentPrompt);
        response.Intent = agentDecision.Intent;

        // 2. 如果Agent决定需要搜索，执行搜索流程
        List<SearchResultItem> searchResults = new List<SearchResultItem>();
        if (agentDecision.NeedSearch && agentDecision.SearchQueries.Count > 0)
        {
            response.SearchQueries = agentDecision.SearchQueries;
            searchResults = await ParallelSearchAsync(agentDecision.SearchQueries, topN);
            response.SearchResultsCount = searchResults.Count;
        }

        // 3. Agent基于搜索结果（如果有）生成最终回答
        var answer = await AgentGenerateResponseAsync(sessionId, userMessage, agentSession.AgentPrompt, searchResults);
        response.Answer = answer;

        // 4. 检查是否完成任务（Agent可以在回答中标记完成）
        if (answer.Contains("任务完成") || answer.Contains("流程结束"))
        {
            agentSession.IsActive = false;
        }

        return response;
    }

    /// <summary>
    /// Agent分析用户输入并决定下一步行动
    /// </summary>
    private async Task<AgentDecision> AgentAnalyzeAsync(string sessionId, string userMessage, string agentPrompt)
    {
        var history = GetHistory(sessionId);
        var historyContext = BuildHistoryContext(history, maxMessages: 10); // Agent需要更多历史

        var systemPrompt = $@"{agentPrompt}

**当前角色**: 你作为团队协调者/专业Agent，需要分析用户输入并决定下一步行动。

**决策规则**:
1. 如果用户刚开始提需求，先收集详细信息（多轮对话）
2. 如果信息足够，判断是否需要网络搜索（市场调研、竞品分析、技术资料等）
3. 如果需要搜索，生成1-3个精准的搜索关键词
4. 如果不需要搜索，直接基于已有信息提供专业建议

**输出格式** (JSON):
{{
  ""needSearch"": true/false,
  ""intent"": ""用户意图描述"",
  ""searchQueries"": [""query1"", ""query2""],
  ""reasoning"": ""决策理由""
}}";

        var userPrompt = $@"对话历史:
{historyContext}

用户最新消息: {userMessage}

请分析用户输入并返回JSON格式的决策。";

        try
        {
            var llmResponse = await CallLlmAsync(systemPrompt, userPrompt);
            
            // 尝试解析JSON
            var jsonStart = llmResponse.IndexOf('{');
            var jsonEnd = llmResponse.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonStr = llmResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var decision = JsonSerializer.Deserialize<AgentDecision>(jsonStr, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });
                
                if (decision != null)
                {
                    return decision;
                }
            }
        }
        catch { /* JSON解析失败，使用默认策略 */ }

        // 默认策略：包含特定关键词就搜索
        var needSearch = userMessage.Contains("市场") || userMessage.Contains("调研") || 
                        userMessage.Contains("竞品") || userMessage.Contains("分析") ||
                        userMessage.Contains("数据") || userMessage.Contains("趋势");

        return new AgentDecision
        {
            NeedSearch = needSearch,
            Intent = userMessage,
            SearchQueries = needSearch ? new List<string> { userMessage } : new List<string>(),
            Reasoning = "使用关键词匹配策略"
        };
    }

    /// <summary>
    /// Agent基于搜索结果生成回答
    /// </summary>
    private async Task<string> AgentGenerateResponseAsync(string sessionId, string userMessage, string agentPrompt, List<SearchResultItem> searchResults)
    {
        var history = GetHistory(sessionId);
        var historyContext = BuildHistoryContext(history, maxMessages: 10);

        // 读取本地知识库
        var knowledgeContext = "";
        if (_knowledgeService != null)
        {
            knowledgeContext = await _knowledgeService.GetKnowledgeContextAsync();
        }

        var systemPrompt = $@"{agentPrompt}

**当前任务**: 基于对话历史、本地知识库和搜索结果，为用户提供专业回答。

**回答要求**:
1. 保持Agent角色（团队协调者/产品经理/设计师等）
2. 如果是多步骤任务，引导用户完成下一步
3. 使用搜索结果中的最新信息
4. 如果任务完成，明确说明'任务完成'";

        var userPrompt = new StringBuilder();
        userPrompt.AppendLine("对话历史:");
        userPrompt.AppendLine(historyContext);
        userPrompt.AppendLine();

        if (!string.IsNullOrEmpty(knowledgeContext))
        {
            userPrompt.AppendLine("本地知识库:");
            userPrompt.AppendLine(knowledgeContext);
            userPrompt.AppendLine();
        }

        if (searchResults.Count > 0)
        {
            userPrompt.AppendLine("网络搜索结果:");
            userPrompt.AppendLine(BuildSearchContext(searchResults));
            userPrompt.AppendLine();
        }

        userPrompt.AppendLine($"用户消息: {userMessage}");
        userPrompt.AppendLine();
        userPrompt.AppendLine("请作为专业Agent回答用户。");

        var answer = await CallLlmAsync(systemPrompt, userPrompt.ToString());
        
        // 检测是否需要生成PPT
        bool shouldGeneratePpt = (userMessage.Contains("生成PPT") || userMessage.Contains("做PPT") || 
                                 userMessage.Contains("生成幻灯片") || userMessage.Contains("PPT")) &&
                                 searchResults.Count > 0;
        
        if (shouldGeneratePpt && _pptGenerator != null)
        {
            try
            {
                // 从答案中提取标题和内容生成PPT
                var title = ExtractPptTitle(userMessage, answer);
                var pptPath = _pptGenerator.GenerateFromMarkdown(title, answer);
                var fileName = Path.GetFileName(pptPath);
                
                answer += $"\n\n📊 **PPT已生成！**\n";
                answer += $"文件名: {fileName}\n";
                answer += $"位置: {pptPath}\n";
                answer += $"💡 提示: PPT已保存到知识库目录，可以直接打开查看！";
            }
            catch (Exception ex)
            {
                answer += $"\n\n⚠️ PPT生成失败: {ex.Message}";
            }
        }
        
        // 自动保存
        if (_knowledgeService != null && searchResults.Count > 0)
        {
            try
            {
                var savedPath = await _knowledgeService.AutoSaveIfValuableAsync(userMessage, answer);
                if (!string.IsNullOrEmpty(savedPath))
                {
                    answer += $"\n\n💾 *已保存到知识库: {Path.GetFileName(savedPath)}*";
                }
            }
            catch { }
        }

        return answer;
    }

    /// <summary>
    /// 从用户消息和答案中提取PPT标题
    /// </summary>
    private string ExtractPptTitle(string userMessage, string answer)
    {
        // 尝试从答案中提取标题（第一个 # 标题）
        var lines = answer.Split('\n');
        foreach (var line in lines)
        {
            if (line.Trim().StartsWith("# "))
            {
                return line.Trim().Substring(2).Trim();
            }
        }
        
        // 如果没有找到，从用户消息中提取关键词
        var keywords = userMessage.Replace("生成PPT", "")
                                 .Replace("做PPT", "")
                                 .Replace("PPT", "")
                                 .Replace("的", "")
                                 .Trim();
        
        return string.IsNullOrEmpty(keywords) ? "AI生成报告" : keywords;
    }

    /// <summary>
    /// 步骤1: 理解对话意图，处理上下文和指代问题
    /// </summary>
    private async Task<string> UnderstandIntentAsync(string sessionId, string userMessage)
    {
        var history = GetHistory(sessionId);
        var historyContext = BuildHistoryContext(history);

        var systemPrompt = @"你是一个对话理解专家。分析用户的问题和对话历史,理解真实的搜索意图。

任务:
1. 解决指代问题(如'它'、'这个'、'那个'等)
2. 补充缺失的上下文信息(时间、地点、主体等)
3. 理解用户的真实搜索需求
4. 用一句话总结用户想要搜索什么

输出格式:
只需要输出理解后的完整搜索意图,一句话,不要解释。

示例:
用户历史: GPT-4有什么新功能? -> AI回答了相关内容
用户当前: 它什么时候发布的?
输出: GPT-4的发布时间

用户历史: 无
用户当前: 2024年AI有什么突破?
输出: 2024年人工智能领域的重大突破和进展";

        var userPrompt = historyContext.Length > 0 
            ? $"对话历史:\n{historyContext}\n\n用户当前问题:{userMessage}" 
            : $"用户问题:{userMessage}";

        try
        {
            var intent = await CallLlmAsync(systemPrompt, userPrompt);
            return intent.Trim();
        }
        catch
        {
            // 如果LLM失败，直接返回用户原始消息
            return userMessage;
        }
    }

    /// <summary>
    /// 步骤2: 生成优化的搜索词，支持多个query
    /// </summary>
    private async Task<List<string>> GenerateSearchQueriesAsync(string intent, string originalMessage)
    {
        var systemPrompt = @"你是一个搜索词优化专家。根据用户的搜索意图,生成1-3个优化的英文搜索关键词。

要求:
1. 提取最关键的信息
2. 使用简洁的英文关键词
3. 可以生成多个角度的搜索词以获得更全面的结果
4. 每个搜索词用换行分隔
5. 只输出搜索词,不要解释

示例:
意图: 2024年人工智能领域的重大突破和进展
输出:
AI breakthroughs 2024
artificial intelligence advances 2024
major AI developments 2024

意图: GPT-4的发布时间
输出:
GPT-4 release date
when was GPT-4 launched

意图: 今天北京天气
输出:
Beijing weather today";

        var userPrompt = $"搜索意图:{intent}";

        try
        {
            var response = await CallLlmAsync(systemPrompt, userPrompt);
            var queries = response.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(q => q.Trim())
                .Where(q => !string.IsNullOrWhiteSpace(q))
                .Take(3)
                .ToList();

            return queries.Count > 0 ? queries : new List<string> { intent };
        }
        catch
        {
            // 如果LLM失败，使用原始消息
            return new List<string> { originalMessage };
        }
    }

    /// <summary>
    /// 步骤3: 并行搜索多个query
    /// </summary>
    private async Task<List<SearchResultItem>> ParallelSearchAsync(List<string> queries, int topNPerQuery)
    {
        var searchTasks = queries.Select(query => _searchApi.SearchAsync(query, topNPerQuery));
        var results = await Task.WhenAll(searchTasks);
        
        // 合并去重
        var allResults = new List<SearchResultItem>();
        var seenUrls = new HashSet<string>();
        
        foreach (var resultList in results)
        {
            foreach (var item in resultList)
            {
                if (!seenUrls.Contains(item.Url))
                {
                    seenUrls.Add(item.Url);
                    allResults.Add(item);
                }
            }
        }
        
        return allResults.Take(topNPerQuery * 2).ToList(); // 最多返回2倍的结果
    }

    /// <summary>
    /// 步骤4: 基于搜索结果和对话历史生成回答
    /// </summary>
    private async Task<string> GenerateAnswerAsync(string sessionId, string userMessage, string intent, List<SearchResultItem> searchResults)
    {
        var history = GetHistory(sessionId);
        var historyContext = BuildHistoryContext(history, maxMessages: 4); // 最近4条对话

        var searchContext = BuildSearchContext(searchResults);
        
        // 1. 优先检查并加载Agent（特别是PPT相关任务）
        string? agentPrompt = null;
        if (_knowledgeService != null)
        {
            agentPrompt = await _knowledgeService.SelectAgentByIntentAsync(userMessage);
        }
        
        // 2. 添加本地知识库内容
        var knowledgeContext = "";
        if (_knowledgeService != null)
        {
            knowledgeContext = await _knowledgeService.GetKnowledgeContextAsync();
        }

        // 3. 构建系统提示词（如果有Agent则使用Agent，否则使用默认）
        var systemPrompt = agentPrompt ?? @"你是一个专业的AI助手,基于搜索结果和本地知识库回答用户问题。

要求:
1. 优先使用本地知识库中的相关信息
2. 结合网络搜索结果生成准确、完整的回答
3. 引用信息来源(使用[来源N]格式表示网络搜索,[文档:文件名]格式表示本地文档)
4. 如果搜索结果中有多个观点,要客观呈现
5. 回答要简洁明了,重点突出
6. 如果信息不足以回答问题,请如实说明
7. 使用中文回答";

        var userPrompt = new StringBuilder();
        
        // 如果使用了Agent，添加提示
        if (agentPrompt != null)
        {
            userPrompt.AppendLine("🤖 已启用专业Agent模式");
            userPrompt.AppendLine();
        }
        
        if (historyContext.Length > 0)
        {
            userPrompt.AppendLine("对话历史:");
            userPrompt.AppendLine(historyContext);
            userPrompt.AppendLine();
        }
        
        if (!string.IsNullOrEmpty(knowledgeContext))
        {
            userPrompt.AppendLine("本地知识库:");
            userPrompt.AppendLine(knowledgeContext);
            userPrompt.AppendLine();
        }
        
        userPrompt.AppendLine($"用户问题:{userMessage}");
        userPrompt.AppendLine($"搜索意图:{intent}");
        userPrompt.AppendLine();
        userPrompt.AppendLine("网络搜索结果:");
        userPrompt.AppendLine(searchContext);
        userPrompt.AppendLine();
        userPrompt.AppendLine("请基于以上信息回答用户的问题。");

        var answer = await CallLlmAsync(systemPrompt, userPrompt.ToString());
        
        // 如果LLM失败，返回降级摘要
        if (answer.StartsWith("Error connecting to LLM"))
        {
            return BuildFallbackSummary(intent, searchResults);
        }
        
        // 自动保存有价值的对话
        if (_knowledgeService != null)
        {
            try
            {
                var savedPath = await _knowledgeService.AutoSaveIfValuableAsync(userMessage, answer);
                if (!string.IsNullOrEmpty(savedPath))
                {
                    answer += $"\n\n💾 此对话已自动保存到: {Path.GetFileName(savedPath)}";
                }
            }
            catch { /* 保存失败不影响主流程 */ }
        }
        
        return answer;
    }

    /// <summary>
    /// 构建对话历史上下文
    /// </summary>
    private string BuildHistoryContext(List<ChatMessage> history, int maxMessages = 6)
    {
        if (history.Count == 0) return "";

        var sb = new StringBuilder();
        var recentMessages = history.TakeLast(maxMessages).ToList();
        
        foreach (var msg in recentMessages)
        {
            sb.AppendLine($"{msg.Role}: {msg.Content}");
        }
        
        return sb.ToString().Trim();
    }

    /// <summary>
    /// 构建搜索结果上下文
    /// </summary>
    private string BuildSearchContext(List<SearchResultItem> results)
    {
        var sb = new StringBuilder();
        
        for (int i = 0; i < results.Count; i++)
        {
            var result = results[i];
            sb.AppendLine($"[来源{i + 1}]: {result.Title}");
            sb.AppendLine($"URL: {result.Url}");
            
            if (!string.IsNullOrEmpty(result.SemanticDocument))
            {
                var content = result.SemanticDocument.Length > 1500 
                    ? result.SemanticDocument.Substring(0, 1500) + "..." 
                    : result.SemanticDocument;
                sb.AppendLine($"内容: {content}");
            }
            sb.AppendLine();
        }
        
        return sb.ToString();
    }

    /// <summary>
    /// 降级模式：当LLM不可用时的简单摘要
    /// </summary>
    private string BuildFallbackSummary(string query, List<SearchResultItem> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("⚠️ LLM服务暂时不可用,以下是搜索结果摘要:");
        sb.AppendLine();
        sb.AppendLine($"关于「{query}」,找到 {results.Count} 条相关结果:");
        sb.AppendLine();
        
        for (int i = 0; i < Math.Min(results.Count, 5); i++)
        {
            var result = results[i];
            sb.AppendLine($"【{i + 1}】{result.Title}");
            sb.AppendLine($"🔗 {result.Url}");
            
            if (!string.IsNullOrEmpty(result.SemanticDocument))
            {
                var snippet = result.SemanticDocument.Length > 200 
                    ? result.SemanticDocument.Substring(0, 200) + "..." 
                    : result.SemanticDocument;
                sb.AppendLine($"📄 {snippet}");
            }
            sb.AppendLine();
        }
        
        return sb.ToString();
    }

    /// <summary>
    /// 调用LLM
    /// </summary>
    private async Task<string> CallLlmAsync(string systemPrompt, string userMessage)
    {
        var requestBody = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            },
            temperature = 0.7,
            max_tokens = 2000
        };

        try
        {
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_llmEndpoint}/v1/chat/completions", content);
            
            if (!response.IsSuccessStatusCode)
            {
                return $"Error: LLM API returned {response.StatusCode}";
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var responseDoc = JsonDocument.Parse(responseJson);
            
            var messageContent = responseDoc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return messageContent ?? "No response from LLM.";
        }
        catch (HttpRequestException)
        {
            return "Error connecting to LLM";
        }
    }

    /// <summary>
    /// 获取对话历史
    /// </summary>
    private List<ChatMessage> GetHistory(string sessionId)
    {
        if (!_conversationHistory.ContainsKey(sessionId))
        {
            _conversationHistory[sessionId] = new List<ChatMessage>();
        }
        return _conversationHistory[sessionId];
    }

    /// <summary>
    /// 保存到对话历史
    /// </summary>
    private void SaveToHistory(string sessionId, string userMessage, string assistantResponse)
    {
        var history = GetHistory(sessionId);
        history.Add(new ChatMessage { Role = "用户", Content = userMessage, Timestamp = DateTime.UtcNow });
        history.Add(new ChatMessage { Role = "AI", Content = assistantResponse, Timestamp = DateTime.UtcNow });
        
        // 保持历史记录在合理范围内（最多20条消息）
        if (history.Count > 20)
        {
            history.RemoveRange(0, history.Count - 20);
        }
    }

    /// <summary>
    /// 清除会话历史
    /// </summary>
    public void ClearHistory(string sessionId)
    {
        if (_conversationHistory.ContainsKey(sessionId))
        {
            _conversationHistory.Remove(sessionId);
        }
        if (_agentSessions.ContainsKey(sessionId))
        {
            _agentSessions.Remove(sessionId);
        }
    }
}

/// <summary>
/// 聊天消息
/// </summary>
public class ChatMessage
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Agent会话状态
/// </summary>
public class AgentSession
{
    public string AgentPrompt { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime StartTime { get; set; }
}

/// <summary>
/// Agent决策结果
/// </summary>
public class AgentDecision
{
    public bool NeedSearch { get; set; }
    public string Intent { get; set; } = "";
    public List<string> SearchQueries { get; set; } = new();
    public string Reasoning { get; set; } = "";
}

/// <summary>
/// 智能对话响应
/// </summary>
public class SmartChatResponse
{
    public string SessionId { get; set; } = "";
    public string UserMessage { get; set; } = "";
    public string Intent { get; set; } = "";
    public List<string> SearchQueries { get; set; } = new();
    public int SearchResultsCount { get; set; }
    public string Answer { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string? Error { get; set; }
    public bool AgentMode { get; set; } = false;
}
