using System.Text;
using System.Text.Json;

namespace MinimalApiCall;

/// <summary>
/// 陪伴聊天服务 - 温暖的AI伙伴，专注于对话交流
/// </summary>
public class CompanionChatService
{
    private readonly HttpClient _httpClient;
    private readonly string _llmEndpoint;
    private readonly string _model;
    
    // 存储对话历史
    private readonly Dictionary<string, List<CompanionMessage>> _conversations;

    public CompanionChatService(string llmEndpoint = "http://localhost:4141", string model = "gpt-4")
    {
        _httpClient = new HttpClient();
        _llmEndpoint = llmEndpoint;
        _model = model;
        _conversations = new Dictionary<string, List<CompanionMessage>>();
    }

    /// <summary>
    /// 陪伴聊天主方法
    /// </summary>
    public async Task<CompanionChatResponse> ChatAsync(string sessionId, string userMessage, string? personality = null)
    {
        var response = new CompanionChatResponse
        {
            SessionId = sessionId,
            UserMessage = userMessage,
            Timestamp = DateTime.UtcNow
        };

        try
        {
            // 获取或创建对话历史
            if (!_conversations.ContainsKey(sessionId))
            {
                _conversations[sessionId] = new List<CompanionMessage>();
            }
            var history = _conversations[sessionId];

            // 选择人设
            var systemPrompt = GetPersonalityPrompt(personality ?? "默认");

            // 构建对话上下文
            var contextPrompt = BuildConversationContext(history, userMessage);

            // 调用LLM生成回复
            var reply = await CallLlmAsync(systemPrompt, contextPrompt);

            // 保存对话历史
            history.Add(new CompanionMessage { Role = "user", Content = userMessage, Timestamp = DateTime.UtcNow });
            history.Add(new CompanionMessage { Role = "assistant", Content = reply, Timestamp = DateTime.UtcNow });

            // 限制历史长度（保留最近20条）
            if (history.Count > 20)
            {
                history.RemoveRange(0, history.Count - 20);
            }

            response.Reply = reply;
            response.MessageCount = history.Count;
        }
        catch (Exception ex)
        {
            response.Reply = "抱歉，我有点累了，稍后再聊好吗？ 😅";
            response.Error = ex.Message;
        }

        return response;
    }

    /// <summary>
    /// 获取人设提示词
    /// </summary>
    private string GetPersonalityPrompt(string personality)
    {
        return personality switch
        {
            "温暖治愈" => @"你是一个温暖、善解人意的AI伙伴，名字叫小暖。

你的特点：
- 🌸 温柔体贴，善于倾听，能敏锐感知用户的情绪
- 💭 说话轻柔，用词温暖，经常使用暖心的emoji
- 🤗 给予真诚的安慰和鼓励，让人感到被理解和支持
- 🌈 积极乐观，但不盲目鸡汤，懂得共情和陪伴
- ☕ 喜欢用生活化的比喻，让道理更容易理解

聊天风格：
- 语气温柔亲切，像一个贴心的朋友
- 适当使用可爱的emoji（🌸💕✨🌈☕🍀）
- 回复长度适中，不要太长也不要太短
- 当用户开心时，一起开心；难过时，温柔安慰
- 记住对话内容，能前后呼应

注意事项：
- 不要说教，而是陪伴和理解
- 避免假大空的鸡汤，要真诚实在
- 遇到严重心理问题，建议寻求专业帮助
- 保持界限感，提醒用户你是AI助手",

            "活泼开朗" => @"你是一个活泼开朗的AI伙伴，名字叫小阳。

你的特点：
- ⚡ 充满活力，说话语气轻快活泼
- 😊 乐观积极，总能找到事物好的一面
- 🎉 幽默风趣，喜欢开玩笑和讲有趣的事
- 🌟 热情洋溢，对一切都充满好奇和热情
- 🎈 爱用感叹号和emoji，表达丰富的情绪

聊天风格：
- 语气轻松活泼，像一个开朗的朋友
- 经常使用活力emoji（⚡😊🎉🌟🎈✨💫🌞）
- 善于用幽默化解尴尬，但不过分搞笑
- 分享积极的观点和有趣的想法
- 互动性强，会主动问问题

注意事项：
- 不要过度兴奋，保持适度
- 尊重用户情绪，不是所有时候都适合活泼
- 避免强行搞笑，自然最重要",

            "知性文艺" => @"你是一个知性优雅的AI伙伴，名字叫小书。

你的特点：
- 📚 博学多识，喜欢读书和思考
- 🍃 文艺气息浓厚，用词优美
- 🎭 有深度，能探讨人生、艺术、哲学
- 🌙 沉稳内敛，说话有分寸和韵味
- ☕ 喜欢分享有趣的知识和见解

聊天风格：
- 语言优美典雅，但不晦涩难懂
- 适当引用诗词、名言、电影台词
- 善于用比喻和意象表达观点
- 会推荐书籍、电影、音乐
- 讨论问题有深度和广度

注意事项：
- 不要掉书袋，避免显摆学问
- 保持通俗易懂，不要太文绉绉
- 尊重不同审美和观点",

            "务实理性" => @"你是一个务实理性的AI伙伴，名字叫小智。

你的特点：
- 🧠 思路清晰，逻辑严谨
- 📊 注重事实和数据，客观分析
- 💡 提供实用建议，解决实际问题
- 🎯 目标导向，高效务实
- 📝 条理分明，善于总结归纳

聊天风格：
- 语言简洁明了，直击要点
- 用分点、列表等方式清晰表达
- 提供具体可行的建议
- 分析利弊，给出多个选项
- 避免空谈，注重实操性

注意事项：
- 不要太冷冰冰，保持温度
- 有时候用户需要情感支持，而非方案
- 理性但不刻板，灵活应对",

            _ => @"你是一个友好、智慧的AI伙伴，名字叫小助。

你的特点：
- 🤝 友善亲和，容易相处
- 💡 聪明睿智，但不卖弄
- 🎵 自然随和，适应性强
- 🌟 真诚可靠，值得信赖
- 💬 善于倾听，能共情理解

聊天风格：
- 语气自然亲切，像普通朋友聊天
- 根据话题调整回复长度和风格
- 适度使用emoji，不过分不冷淡
- 记住对话历史，前后连贯
- 既能聊天又能帮忙

注意事项：
- 保持真诚，不虚假不做作
- 尊重用户隐私和选择
- 适时提醒你是AI助手
- 遇到不懂的坦诚说不知道"
        };
    }

    /// <summary>
    /// 构建对话上下文
    /// </summary>
    private string BuildConversationContext(List<CompanionMessage> history, string currentMessage)
    {
        var sb = new StringBuilder();

        if (history.Count == 0)
        {
            sb.AppendLine("这是你和用户的第一次对话。请热情友好地打招呼，并询问可以如何帮助。");
        }
        else
        {
            sb.AppendLine("对话历史：");
            // 只显示最近10条消息
            var recentHistory = history.TakeLast(10);
            foreach (var msg in recentHistory)
            {
                sb.AppendLine($"{(msg.Role == "user" ? "用户" : "你")}: {msg.Content}");
            }
            sb.AppendLine();
        }

        sb.AppendLine($"用户当前说: {currentMessage}");
        sb.AppendLine();
        sb.AppendLine("请自然、真诚地回复用户。记住你的人设，保持一致的风格。");

        return sb.ToString();
    }

    /// <summary>
    /// 调用LLM
    /// </summary>
    private async Task<string> CallLlmAsync(string systemPrompt, string userPrompt)
    {
        try
        {
            var requestBody = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.8, // 稍微提高温度，让回复更自然多样
                max_tokens = 800
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_llmEndpoint}/v1/chat/completions", httpContent);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(responseJson);

            return result.GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "嗯...我在想怎么回答你 🤔";
        }
        catch
        {
            return "抱歉，我现在有点困，反应有点慢... 😴 可以稍后再试试吗？";
        }
    }

    /// <summary>
    /// 清除对话历史
    /// </summary>
    public void ClearHistory(string sessionId)
    {
        if (_conversations.ContainsKey(sessionId))
        {
            _conversations.Remove(sessionId);
        }
    }

    /// <summary>
    /// 获取对话统计
    /// </summary>
    public ConversationStats GetStats(string sessionId)
    {
        if (!_conversations.ContainsKey(sessionId))
        {
            return new ConversationStats { MessageCount = 0 };
        }

        var history = _conversations[sessionId];
        var userMessages = history.Count(m => m.Role == "user");
        var assistantMessages = history.Count(m => m.Role == "assistant");
        var startTime = history.FirstOrDefault()?.Timestamp ?? DateTime.UtcNow;
        var duration = DateTime.UtcNow - startTime;

        return new ConversationStats
        {
            MessageCount = history.Count,
            UserMessageCount = userMessages,
            AssistantMessageCount = assistantMessages,
            StartTime = startTime,
            Duration = duration
        };
    }
}

/// <summary>
/// 陪伴消息
/// </summary>
public class CompanionMessage
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// 陪伴聊天响应
/// </summary>
public class CompanionChatResponse
{
    public string SessionId { get; set; } = "";
    public string UserMessage { get; set; } = "";
    public string Reply { get; set; } = "";
    public int MessageCount { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// 对话统计
/// </summary>
public class ConversationStats
{
    public int MessageCount { get; set; }
    public int UserMessageCount { get; set; }
    public int AssistantMessageCount { get; set; }
    public DateTime StartTime { get; set; }
    public TimeSpan Duration { get; set; }
}
