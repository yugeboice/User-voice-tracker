using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Lumina.Client.Models.Sonicberry;

namespace MinimalApiCall;

/// <summary>
    /// Lumina技术支持Agent - 协助客户onboard Lumina基础设施，解决初期基础问题
    /// </summary>
    public class CustomerServiceAgent
    {
        private readonly SearchApi _searchApi;
        private readonly LlmExample _llmExample;
        private readonly string _llmEndpoint;
        private readonly string _llmModel;
        private readonly ConversationMemory _memory;
        private readonly ConversationHistoryService _historyService;

        // 知识库 - Lumina常见问题和答案（区分 Search 和 CUA 场景）
        private readonly Dictionary<string, KnowledgeItem> _knowledgeBase;

        // 用户需求收集状态
        private static readonly Dictionary<string, UserOnboardingState> _onboardingStates = new();
    public CustomerServiceAgent(
        SearchApi searchApi,
        LlmExample llmExample,
        string llmEndpoint,
        string llmModel)
    {
        _searchApi = searchApi;
        _llmExample = llmExample;
        _llmEndpoint = llmEndpoint;
        _llmModel = llmModel;
        _memory = new ConversationMemory();
        _historyService = new ConversationHistoryService();
        _knowledgeBase = InitializeKnowledgeBase();
    }

    /// <summary>
    /// 初始化Lumina知识库（区分 Search 和 CUA 场景）
    /// </summary>
    private Dictionary<string, KnowledgeItem> InitializeKnowledgeBase()
    {
        return new Dictionary<string, KnowledgeItem>
        {
            // ========== Search 场景知识库 ==========
            ["Search入门"] = new KnowledgeItem
            {
                Title = "Search API 快速入门",
                Scenario = "Search",
                Content = "欢迎使用 Lumina Search API！\n\n**Search Onboarding 步骤：**\n1. 申请 Bing API App ID\n2. 授权 App ID 使用 Lumina\n3. 配置 appsettings.json\n4. 安装 SDK 并运行第一个搜索\n\n**前置要求：**\n• Azure AD 认证配置\n• Bing Search API App ID\n• .NET 6.0+ 或其他支持的语言\n\n📖 **完整文档**: [快速入门指南](https://eng.ms/docs/experiences-devices/m365-core-msai/platform/msai-stca/copilot-lumina/copilot-lumina/quick-start/step-by-step)\n\n**下一步：** 了解如何申请 Bing API",
                Keywords = new[] { "search", "搜索", "入门", "开始", "get started", "快速", "新手" },
                QuickReplies = new[] { "如何申请 Bing API？", "Search API 配置示例", "第一个搜索调用" }
            },
            ["Search-Bing申请"] = new KnowledgeItem
            {
                Title = "Bing API 申请与授权（Search 场景）",
                Scenario = "Search",
                Content = "**Search API 需要 Bing App ID：**\n\n**1. 申请 Bing Search API：**\n• TryItOut: https://aka.ms/AISPSearchv7TryitOutAPPID\n• 文档: Bing Search API documentation\n\n**2. 授权给 Lumina：**\n• 发送邮件至: spbsupp@microsoft.com\n• 邮件内容: 提供您的 Bing App ID 和使用场景\n• 等待审批（通常 1-3 个工作日）\n\n**3. 配置到 Lumina：**\n```json\n{\n  \"LuminaConfiguration\": {\n    \"ApiEndpoint\": \"https://your-endpoint\",\n    \"BingAppId\": \"your-app-id\"\n  }\n}\n```\n\n📖 **详细申请指南**: [Bing API Request Guide](https://eng.ms/docs/experiences-devices/m365-core-msai/platform/msai-stca/copilot-lumina/copilot-lumina/quick-start/bing-api-request-guide)",
                Keywords = new[] { "bing", "app id", "申请", "授权", "search", "搜索" },
                QuickReplies = new[] { "授权需要多久？", "测试 Bing API", "Search 配置示例" }
            },
            ["Search-API使用"] = new KnowledgeItem
            {
                Title = "Search API 使用方法",
                Scenario = "Search",
                Content = "**Search API 核心功能：**\n\n**1. 基础搜索：**\n```csharp\nvar results = await searchApi.SearchAsync(\"query\", topN: 10);\n```\n\n**2. 高级过滤：**\n• 支持 Bing 搜索语法\n• 可按时间、域名过滤\n• 支持多语言搜索\n\n**3. 结果处理：**\n• Title: 搜索结果标题\n• Url: 结果链接\n• SemanticDocument: 结果摘要\n\n**常见用例：**\n• 网络信息检索\n• 内容聚合\n• 知识增强（Grounding）",
                Keywords = new[] { "search api", "搜索", "使用", "调用", "how to use" },
                QuickReplies = new[] { "Search 示例代码", "搜索结果格式", "高级搜索技巧" }
            },

            // ========== CUA 场景知识库 ==========
            ["CUA入门"] = new KnowledgeItem
            {
                Title = "CUA 浏览器自动化快速入门",
                Scenario = "CUA",
                Content = "欢迎使用 Lumina CUA（浏览器自动化）！\n\n**CUA Onboarding 步骤：**\n1. 评估并发需求，计算所需机器数量\n2. 提交 VM 申请（提供上线时间和数量）\n3. 等待审批（建议提前规划）\n4. 配置 CUA Endpoint 并测试\n\n**前置要求：**\n• Azure AD 认证配置\n• 明确的并发需求评估\n• 预算审批（CUA 涉及 VM 成本）\n\n**机器数量估算公式：**\n机器数量 ≈ 峰值并发用户数 × 每用户并发数\n\n📖 **完整文档**: [快速入门指南](https://eng.ms/docs/experiences-devices/m365-core-msai/platform/msai-stca/copilot-lumina/copilot-lumina/quick-start/step-by-step)",
                Keywords = new[] { "cua", "浏览器", "自动化", "入门", "开始", "cdp", "sandbox" },
                QuickReplies = new[] { "如何申请 CUA 机器？", "机器数量怎么估算？", "CUA 成本计算" }
            },
            ["CUA-机器申请"] = new KnowledgeItem
            {
                Title = "CUA 机器申请流程",
                Scenario = "CUA",
                Content = "**CUA VM 申请详细流程：**\n\n**1. 准备信息：**\n• 产品上线时间\n• 预估峰值并发用户数\n• 每用户最大并发会话数\n• 业务场景描述\n\n**2. 提交申请：**\n• 联系 Lumina 团队\n• 发送邮件至: lumina-support@microsoft.com\n• 包含以上所有信息\n\n**3. 审批流程：**\n• Lumina 团队协助推进\n• 审批时间无法精确预估\n• 建议根据项目进度尽早提交\n\n**4. 沙盒环境：**\n暂不提供共享 VM 试用，客户可申请自己的沙盒环境测试。",
                Keywords = new[] { "cua", "机器", "vm", "申请", "sandbox", "沙盒", "审批" },
                QuickReplies = new[] { "申请需要多久？", "如何估算机器数量？", "CUA 成本是多少？" }
            },
            ["CUA-成本"] = new KnowledgeItem
            {
                Title = "CUA 机器成本计算",
                Scenario = "CUA",
                Content = "**8-vCPU VM 24×7 运行月度成本：**\n\n• 存储/磁盘：$3.15\n• 网络/带宽：$1.10\n• 计算资源：$44.00\n**每台 VM 总计：约 $48.25/月**\n\n**示例计算：**\n• 10 个用户 × 3 个并发 = 30 台机器\n• 30 × $48.25 = **$1,447.50/月**\n\n**成本优化建议：**\n1. 根据实际使用模式调整数量\n2. 用完及时释放 CUA 会话\n3. 监控实际并发，避免过度配置\n4. 非高峰期考虑缩减机器数\n\n实际成本可能因区域、配置有所不同。",
                Keywords = new[] { "cua", "成本", "费用", "价格", "cost", "billing", "多少钱" },
                QuickReplies = new[] { "如何降低成本？", "按需付费吗？", "机器数量优化" }
            },
            ["CUA-API使用"] = new KnowledgeItem
            {
                Title = "CUA API 使用方法",
                Scenario = "CUA",
                Content = "**CUA API 核心功能：**\n\n**1. 截图功能：**\n```csharp\nvar screenshot = await cuaApi.CaptureScreenshotAsync(url);\n// 返回 Base64 编码的图片\n```\n\n**2. 浏览器交互：**\n• 支持点击、输入、滚动等操作\n• 可执行 JavaScript\n• 云端浏览器执行，无需本地浏览器\n\n**3. 会话管理：**\n• 最多 5 个同时活动会话（默认配额）\n• 用完需及时释放\n• 超时自动释放\n\n**常见用例：**\n• 网页截图\n• 表单自动填写\n• 动态内容提取",
                Keywords = new[] { "cua api", "截图", "使用", "调用", "浏览器", "交互" },
                QuickReplies = new[] { "CUA 示例代码", "如何释放会话？", "会话超时时间" }
            },

            // ========== 通用知识库 ==========
            ["入门"] = new KnowledgeItem
            {
                Title = "Lumina 服务概览",
                Scenario = "General",
                Content = "欢迎使用 Lumina Agent Infrastructure！\n\n**Lumina 提供两大核心服务：**\n\n**1. Search（搜索服务）**\n• 基于 Bing 的网络搜索\n• 支持内容提取和页面解析\n• 适合信息检索和知识增强\n\n**2. CUA（浏览器自动化）**\n• 云端浏览器自动化\n• 支持截图、交互、数据提取\n• 适合动态内容处理\n\n**请选择您的使用场景：**",
                Keywords = new[] { "入门", "开始", "概览", "介绍", "是什么" },
                QuickReplies = new[] { "我要使用 Search", "我要使用 CUA", "两者有什么区别？" }
            },
            ["认证"] = new KnowledgeItem
            {
                Title = "身份认证配置",
                Scenario = "General",
                Content = "Lumina使用Azure AD进行身份认证：\n\n**配置步骤：**\n1. 在Azure Portal创建应用注册\n2. 配置应用权限（Lumina.Read等）\n3. 获取Client ID和Tenant ID\n4. 在代码中配置TokenProvider\n\n**示例配置：**\n```json\n{\n  \"LuminaConfiguration\": {\n    \"ApiEndpoint\": \"https://...\",\n    \"TenantId\": \"your-tenant-id\",\n    \"ClientId\": \"your-client-id\"\n  }\n}\n```\n\n认证token会自动刷新，无需手动管理。",
                Keywords = new[] { "认证", "auth", "token", "登录", "权限", "credential", "密钥" },
                QuickReplies = new[] { "Token过期怎么办？", "如何配置应用权限？", "支持服务主体吗？" }
            },
            ["配置"] = new KnowledgeItem
            {
                Title = "配置文件说明",
                Scenario = "General",
                Content = "Lumina支持多种配置方式：\n\n**appsettings.json配置：**\n```json\n{\n  \"LuminaConfiguration\": {\n    \"ApiEndpoint\": \"https://your-endpoint\",\n    \"CuaEndpoint\": \"https://cua-endpoint\"\n  },\n  \"PartnerContext\": {\n    \"Partner\": \"YourCompany\",\n    \"ScenarioGroup\": \"YourScenario\",\n    \"ScenarioName\": \"SpecificUseCase\"\n  }\n}\n```\n\n**环境变量：**\n可以通过环境变量覆盖配置，使用前缀`Lumina__`\n\n**Partner Context（可选）：**\n用于追踪和分析使用情况，建议配置。",
                Keywords = new[] { "配置", "config", "设置", "appsettings", "环境变量", "endpoint" },
                QuickReplies = new[] { "必填配置有哪些？", "如何配置多环境？", "Partner Context是什么？" }
            },
            ["错误"] = new KnowledgeItem
            {
                Title = "常见错误处理",
                Scenario = "General",
                Content = "常见错误及解决方案：\n\n**401 Unauthorized**\n• 检查token是否有效\n• 确认应用权限配置正确\n• 验证租户ID和客户端ID\n\n**404 Not Found**\n• 检查API端点是否正确\n• 确认服务在该区域可用\n\n**429 Too Many Requests**\n• 实现请求重试机制\n• 考虑增加请求间隔\n• 联系我们提升配额\n\n**超时错误**\n• 增加HttpClient超时设置\n• 检查网络连接\n• 某些操作（如CUA）可能需要更长时间\n\n建议在生产环境实现完善的错误处理和重试逻辑。",
                Keywords = new[] { "错误", "error", "异常", "失败", "401", "404", "429", "timeout", "bug" },
                QuickReplies = new[] { "如何实现重试？", "错误日志在哪里？", "联系技术支持" }
            },
            ["SDK"] = new KnowledgeItem
            {
                Title = "SDK使用指南",
                Scenario = "General",
                Content = "Lumina提供官方.NET SDK：\n\n**安装NuGet包：**\n```bash\ndotnet add package Microsoft.Lumina.Client.ApiProxy\n```\n\n**基本使用：**\n```csharp\nvar proxy = new LuminaServiceApiProxy(options);\nvar searchApi = new SearchApi(proxy);\nvar results = await searchApi.SearchAsync(\"query\");\n```\n\n**SDK优势：**\n• 自动处理认证和token刷新\n• 强类型API接口\n• 内置错误处理\n• 支持依赖注入\n\n**其他语言：**\nPython和JavaScript SDK正在开发中，当前可使用REST API。\n\n📖 **代码示例**: [Code Sample Library](https://eng.ms/docs/experiences-devices/m365-core-msai/platform/msai-stca/copilot-lumina/copilot-lumina/quick-start/code-sample/)",
                Keywords = new[] { "sdk", "client", "库", "package", "nuget", "安装" },
                QuickReplies = new[] { "有Python SDK吗？", "如何更新SDK？", "SDK源码在哪？" }
            },
            ["限制"] = new KnowledgeItem
            {
                Title = "配额和限制",
                Scenario = "General",
                Content = "Lumina服务的配额信息：\n\n**默认限制：**\n• 请求速率：60次/分钟\n• 并发请求：10个\n• Search API：10,000次/天\n• CUA会话：最多5个同时活动\n\n**最佳实践：**\n• 实现请求池控制并发\n• 使用缓存减少重复请求\n• CUA会话用完及时释放\n• 批量操作时添加延迟\n\n**提升配额：**\n如需更高配额，请联系我们提供：\n• 使用场景描述\n• 预期请求量\n• 业务联系方式",
                Keywords = new[] { "限制", "quota", "配额", "速率", "limit", "并发", "次数" },
                QuickReplies = new[] { "如何申请更高配额？", "超过限制会怎样？", "怎么监控使用量？" }
            },
            ["最佳实践"] = new KnowledgeItem
            {
                Title = "开发最佳实践",
                Scenario = "General",
                Content = "推荐的开发实践：\n\n**1. 错误处理**\n• 捕获并记录所有异常\n• 实现指数退避重试\n• 区分可重试和不可重试错误\n\n**2. 性能优化**\n• 复用HttpClient实例\n• 使用连接池\n• 合理设置超时时间\n• 缓存不变的查询结果\n\n**3. 安全性**\n• 不要在代码中硬编码密钥\n• 使用Key Vault存储敏感信息\n• 定期轮换credentials\n\n**4. 监控**\n• 记录API调用日志\n• 监控响应时间\n• 设置告警机制\n\n**5. 测试**\n• 编写单元测试\n• 使用mock数据\n• 测试错误场景",
                Keywords = new[] { "最佳实践", "best practice", "建议", "推荐", "优化", "性能" },
                QuickReplies = new[] { "如何提升性能？", "安全性建议", "如何做测试？" }
            },

            // ========== 文档链接 (Doc-Site Links) ==========
            ["文档-快速入门"] = new KnowledgeItem
            {
                Title = "Lumina 快速入门文档",
                Scenario = "General",
                Content = "📖 **官方快速入门指南**\n\n" +
                          "详细的分步教程，帮助您快速上手 Lumina：\n" +
                          "👉 [Step by Step Guide](https://eng.ms/docs/experiences-devices/m365-core-msai/platform/msai-stca/copilot-lumina/copilot-lumina/quick-start/step-by-step)\n\n" +
                          "涵盖内容：\n" +
                          "• 环境准备和前置要求\n" +
                          "• SDK 安装和配置\n" +
                          "• 第一个 API 调用示例\n" +
                          "• 常见问题和故障排查",
                Keywords = new[] { "快速入门", "step by step", "开始", "教程", "tutorial", "getting started", "guide", "文档" },
                QuickReplies = new[] { "代码示例", "Partner Context 配置", "API 参考文档" }
            },
            ["文档-Partner-Context"] = new KnowledgeItem
            {
                Title = "Partner Context 配置文档",
                Scenario = "General",
                Content = "📖 **Partner Context 配置指南**\n\n" +
                          "了解如何配置 Partner Context 以追踪和优化 API 使用：\n" +
                          "👉 [Partner Context Guide](https://eng.ms/docs/experiences-devices/m365-core-msai/platform/msai-stca/copilot-lumina/copilot-lumina/quick-start/partner-context)\n\n" +
                          "关键信息：\n" +
                          "• Partner Context 的作用和重要性\n" +
                          "• 如何在 appsettings.json 中配置\n" +
                          "• 使用建议和最佳实践\n" +
                          "• 监控和分析数据",
                Keywords = new[] { "partner context", "partner", "追踪", "tracking", "monitoring", "配置" },
                QuickReplies = new[] { "配置示例", "快速入门", "API 参考" }
            },
            ["文档-Bing申请"] = new KnowledgeItem
            {
                Title = "Bing API 申请指南文档",
                Scenario = "Search",
                Content = "📖 **Bing API App ID 申请详细指南**\n\n" +
                          "完整的 Bing API 申请和授权流程：\n" +
                          "👉 [Bing API Request Guide](https://eng.ms/docs/experiences-devices/m365-core-msai/platform/msai-stca/copilot-lumina/copilot-lumina/quick-start/bing-api-request-guide)\n\n" +
                          "指南包含：\n" +
                          "• 如何申请 Bing Search API App ID\n" +
                          "• 授权 App ID 给 Lumina 使用\n" +
                          "• 审批流程和时间线\n" +
                          "• 常见问题解答",
                Keywords = new[] { "bing 文档", "app id 文档", "申请指南", "request guide", "authorization guide", "bing guide" },
                QuickReplies = new[] { "代码示例", "Search API 使用", "快速入门" }
            },
            ["文档-代码示例"] = new KnowledgeItem
            {
                Title = "代码示例文档",
                Scenario = "General",
                Content = "📖 **Lumina API 代码示例库**\n\n" +
                          "查看完整的代码示例，涵盖各种使用场景：\n" +
                          "👉 [Code Sample Library](https://eng.ms/docs/experiences-devices/m365-core-msai/platform/msai-stca/copilot-lumina/copilot-lumina/quick-start/code-sample/)\n\n" +
                          "包含示例：\n" +
                          "• Search API 完整调用示例\n" +
                          "• CUA API 截图和交互示例\n" +
                          "• 认证配置代码\n" +
                          "• 错误处理和重试逻辑\n" +
                          "• 多种编程语言实现",
                Keywords = new[] { "代码", "code", "示例", "example", "sample", "demo", "如何", "怎么用" },
                QuickReplies = new[] { "快速入门", "API 参考", "最佳实践" }
            },
            ["文档-沙盒集成"] = new KnowledgeItem
            {
                Title = "多租户和沙盒集成文档",
                Scenario = "CUA",
                Content = "📖 **多租户和沙盒环境集成指南**\n\n" +
                          "了解如何在沙盒环境中测试和集成 Lumina：\n" +
                          "👉 [Sandbox Integration Guide](https://eng.ms/docs/experiences-devices/m365-core-msai/platform/msai-stca/copilot-lumina/copilot-lumina/multi-tenant/)\n\n" +
                          "涵盖内容：\n" +
                          "• 沙盒环境申请流程\n" +
                          "• 多租户配置和隔离\n" +
                          "• 测试环境最佳实践\n" +
                          "• 生产环境迁移指南",
                Keywords = new[] { "沙盒", "sandbox", "测试", "test", "multi-tenant", "多租户", "环境" },
                QuickReplies = new[] { "CUA 机器申请", "快速入门", "代码示例" }
            },
            ["文档-API参考"] = new KnowledgeItem
            {
                Title = "API 参考文档",
                Scenario = "General",
                Content = "📖 **Lumina API 完整参考文档**\n\n" +
                          "查看所有 API 端点、参数和返回值的详细说明：\n" +
                          "👉 [API Reference](https://eng.ms/docs/experiences-devices/m365-core-msai/platform/msai-stca/copilot-lumina/copilot-lumina/reference/api-ref)\n\n" +
                          "文档包含：\n" +
                          "• Search API 完整接口定义\n" +
                          "• CUA API 完整接口定义\n" +
                          "• 请求/响应数据模型\n" +
                          "• 错误码和故障排查\n" +
                          "• 速率限制和配额说明",
                Keywords = new[] { "api", "reference", "参考", "文档", "接口", "endpoint", "方法", "详细" },
                QuickReplies = new[] { "代码示例", "快速入门", "最佳实践" }
            }
        };
    }

    /// <summary>
    /// 处理客户消息（支持 Search/CUA 场景和记忆功能）
    /// </summary>
    public async Task<CustomerServiceResponse> HandleMessageAsync(string message, string conversationId)
    {
        try
        {
            // 初始化会话状态（如果不存在）
            if (!_onboardingStates.ContainsKey(conversationId))
            {
                _onboardingStates[conversationId] = new UserOnboardingState
                {
                    InfoCollected = true  // 允许用户直接提问，不强制 onboarding
                };
            }

            var state = _onboardingStates[conversationId];

            // 添加到对话历史（内存 + 持久化）
            state.ConversationHistory.Add($"User: {message}");
            _historyService.AddEntry(conversationId, "User", message);

            // 1. 先尝试从记忆系统查找
            var memoryMatch = _memory.FindSimilarQuestion(message);
            if (memoryMatch != null)
            {
                Console.WriteLine($"[CustomerServiceAgent] Found answer in memory: {memoryMatch.Question}");
                state.ConversationHistory.Add($"Assistant (Memory): {memoryMatch.Answer}");
                _historyService.AddEntry(conversationId, "Assistant", memoryMatch.Answer, source: "memory");

                return new CustomerServiceResponse
                {
                    Response = $"💾 **从知识库找到答案：**\n\n{memoryMatch.Answer}\n\n" +
                              $"_（此答案来自之前的对话记录，使用次数：{memoryMatch.UsageCount}）_",
                    QuickReplies = memoryMatch.Category switch
                    {
                        "Search入门" or "Search-Bing申请" or "Search-API使用" => ["Search 示例代码", "如何测试？", "更多 Search 问题"],
                        "CUA入门" or "CUA-机器申请" or "CUA-成本" => ["机器数量估算", "成本优化", "更多 CUA 问题"],
                        _ => ["查看常见问题", "还有其他问题"]
                    },
                    Source = "memory"
                };
            }

            // 2. 从知识库匹配（优先匹配当前场景）
            var knowledgeMatch = FindKnowledgeMatch(message, state.Scenario);
            if (knowledgeMatch != null)
            {
                // 使用Search API搜索相关文档来增强回答
                var searchResults = await SearchRelevantDocsAsync(message);
                var enhancedContent = knowledgeMatch.Content;

                if (searchResults?.Count > 0)
                {
                    enhancedContent += "\n\n**📚 相关资源：**\n";
                    foreach (var result in searchResults.Take(3))
                    {
                        enhancedContent += $"• [{result.Title}]({result.Url})\n";
                        if (!string.IsNullOrEmpty(result.SemanticDocument))
                        {
                            var snippet = result.SemanticDocument.Length > 100
                                ? result.SemanticDocument.Substring(0, 100) + "..."
                                : result.SemanticDocument;
                            enhancedContent += $"  {snippet}\n";
                        }
                    }
                }

                state.ConversationHistory.Add($"Assistant (Knowledge): {enhancedContent}");
                _historyService.AddEntry(conversationId, "Assistant", enhancedContent, source: "knowledge");

                return new CustomerServiceResponse
                {
                    Response = enhancedContent,
                    KnowledgeCard = new KnowledgeCard
                    {
                        Title = knowledgeMatch.Title,
                        Content = "💡 还有其他问题吗？我随时为您解答！"
                    },
                    QuickReplies = knowledgeMatch.QuickReplies?.ToList(),
                    Source = "knowledge_base_with_search"
                };
            }

            // 3. 判断是否需要技术支持
            if (NeedsTechnicalSupport(message))
            {
                return new CustomerServiceResponse
                {
                    Response = "这个问题比较复杂，我为您转接技术支持团队。\n\n" +
                              "**在等待期间，您可以：**\n" +
                              "• 查看技术文档：https://docs.microsoft.com/lumina\n" +
                              "• 浏览常见问题\n" +
                              "• 加入我们的技术社区\n\n" +
                              "技术支持邮箱：lumina-support@microsoft.com",
                    QuickReplies = ["查看文档", "常见问题列表", "社区论坛"],
                    Source = "transfer_to_technical"
                };
            }

            // 4. 使用AI智能回答（对于一般问题，先尝试Web搜索）
            List<SearchResultItem>? webSearchResults = null;
            bool isLuminaRelated = await IsLuminaRelatedQueryAsync(message);
            if (!isLuminaRelated)
            {
                // 非Lumina相关问题，使用Web搜索
                webSearchResults = await SearchGeneralWebAsync(message);
            }

            var aiResponse = await GetAIResponseAsync(message, conversationId, state, webSearchResults);
            state.ConversationHistory.Add($"Assistant (AI): {aiResponse}");

            // 保存到持久化历史（区分有无搜索结果）
            var source = webSearchResults?.Count > 0 ? "ai_with_web_search" : "ai_generated";
            _historyService.AddEntry(conversationId, "Assistant", aiResponse, source: source);

            // 如果有Web搜索结果，添加到响应中
            string finalResponse = aiResponse;
            if (webSearchResults?.Count > 0)
            {
                finalResponse += "\n\n**📚 相关资源：**\n";
                foreach (var result in webSearchResults.Take(3))
                {
                    finalResponse += $"• [{result.Title}]({result.Url})\n";
                }
            }

            // 5. 保存到记忆系统（待审核）
            try
            {
                var category = DetermineCategoryFromMessage(message, state.Scenario);
                _memory.AddMemory(
                    question: message,
                    answer: aiResponse,
                    category: category,
                    scenario: state.Scenario ?? "General"
                );
                Console.WriteLine($"[CustomerServiceAgent] Saved new Q&A to memory (pending approval)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CustomerServiceAgent] Failed to save memory: {ex.Message}");
            }

            return new CustomerServiceResponse
            {
                Response = finalResponse + "\n\n_💾 此回答已保存到记忆库，经审核后将成为知识库的一部分。_",
                QuickReplies = GenerateContextualReplies(message, state.Scenario),
                Source = webSearchResults?.Count > 0 ? "ai_with_web_search" : "ai_generated"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CustomerServiceAgent] Error: {ex.Message}");
            return new CustomerServiceResponse
            {
                Response = "抱歉，系统暂时无法处理您的问题。\n\n" +
                          "**您可以：**\n" +
                          "• 稍后再试\n" +
                          "• 查看技术文档\n" +
                          "• 发邮件至：lumina-support@microsoft.com",
                QuickReplies = ["查看文档", "常见问题", "联系支持"],
                Source = "error"
            };
        }
    }

    /// <summary>
    /// 欢迎消息并开始收集信息（区分 Search/CUA 场景）
    /// </summary>
    private CustomerServiceResponse GetWelcomeAndCollectInfo()
    {
        return new CustomerServiceResponse
        {
            Response = "👋 您好！我是 Lumina 技术支持助手。\n\n" +
                      "Lumina 提供两大核心服务：\n\n" +
                      "🔍 **Search** - 网络搜索和内容提取\n" +
                      "🖥️ **CUA** - 云端浏览器自动化\n\n" +
                      "**请选择您要使用的服务：**",
            QuickReplies =
            [
                "🔍 Search 服务",
                "🖥️ CUA 服务",
                "还不确定，想了解区别"
            ],
            Source = "onboarding"
        };
    }

    /// <summary>
    /// 收集用户信息（根据 Search/CUA 场景区分）
    /// </summary>
    private async Task<CustomerServiceResponse> CollectUserInfoAsync(string message, UserOnboardingState state)
    {
        // Step 1: 选择场景 (Search or CUA)
        if (string.IsNullOrEmpty(state.Scenario))
        {
            var lowerMessage = message.ToLower();

            // 判断用户选择的场景
            if (lowerMessage.Contains("search") || lowerMessage.Contains("搜索"))
            {
                state.Scenario = "Search";
                return new CustomerServiceResponse
                {
                    Response = "太好了！您选择了 **Search 服务** 🔍\n\n" +
                              "**Search 场景 Onboarding 步骤：**\n" +
                              "1. 申请 Bing API App ID\n" +
                              "2. 授权 App ID 使用 Lumina\n" +
                              "3. 配置 Azure AD 认证\n" +
                              "4. 运行第一个搜索调用\n\n" +
                              "**请问您使用什么开发语言？**",
                    QuickReplies = [".NET/C#", "Python", "JavaScript", "其他"],
                    Source = "onboarding"
                };
            }
            else if (lowerMessage.Contains("cua") || lowerMessage.Contains("浏览器"))
            {
                state.Scenario = "CUA";
                return new CustomerServiceResponse
                {
                    Response = "太好了！您选择了 **CUA 服务** 🖥️\n\n" +
                              "**CUA 场景 Onboarding 步骤：**\n" +
                              "1. 评估并发需求并计算机器数量\n" +
                              "2. 提交 VM 申请（提供上线时间和数量）\n" +
                              "3. 等待审批（建议提前规划）\n" +
                              "4. 配置 CUA Endpoint 并测试\n\n" +
                              "**请问您使用什么开发语言？**",
                    QuickReplies = [".NET/C#", "Python", "JavaScript", "其他"],
                    Source = "onboarding"
                };
            }
            else
            {
                // 用户想了解区别
                return new CustomerServiceResponse
                {
                    Response = "**Search vs CUA 服务对比：**\n\n" +
                              "🔍 **Search 适合：**\n" +
                              "• 网络信息检索\n" +
                              "• 内容聚合和摘要\n" +
                              "• 知识增强（Grounding）\n" +
                              "• 静态网页内容提取\n\n" +
                              "🖥️ **CUA 适合：**\n" +
                              "• 网页截图\n" +
                              "• 动态内容交互\n" +
                              "• 表单自动填写\n" +
                              "• 需要 JavaScript 渲染的页面\n\n" +
                              "**请选择您要使用的服务：**",
                    QuickReplies = ["🔍 Search 服务", "🖥️ CUA 服务"],
                    Source = "onboarding"
                };
            }
        }

        // Step 2: 记录开发语言
        if (string.IsNullOrEmpty(state.DevLanguage))
        {
            state.DevLanguage = message;
            state.InfoCollected = true;

            // 根据场景返回不同的下一步建议
            if (state.Scenario == "Search")
            {
                return new CustomerServiceResponse
                {
                    Response = $"完美！我已了解您的需求：\n\n" +
                              $"• 服务类型：**Search** 🔍\n" +
                              $"• 开发语言：**{state.DevLanguage}**\n\n" +
                              "**Search Onboarding 下一步：**\n" +
                              "1. 🔑 申请 Bing API App ID\n" +
                              "2. ✅ 授权 App ID 给 Lumina\n" +
                              "3. 🔐 配置 Azure AD 认证\n" +
                              "4. 💻 运行第一个搜索示例\n\n" +
                              "有什么问题可以随时问我！",
                    QuickReplies =
                    [
                        "如何申请 Bing API？",
                        "Search 配置示例",
                        "第一个搜索调用",
                        "常见问题"
                    ],
                    Source = "onboarding_complete"
                };
            }
            else // CUA
            {
                return new CustomerServiceResponse
                {
                    Response = $"完美！我已了解您的需求：\n\n" +
                              $"• 服务类型：**CUA** 🖥️\n" +
                              $"• 开发语言：**{state.DevLanguage}**\n\n" +
                              "**CUA Onboarding 下一步：**\n" +
                              "1. 📊 评估并发需求（峰值用户数 × 并发数）\n" +
                              "2. 📝 提交 VM 申请（上线时间 + 机器数量）\n" +
                              "3. ⏳ 等待审批（建议提前规划）\n" +
                              "4. 💻 配置 CUA Endpoint 并测试\n\n" +
                              "有什么问题可以随时问我！",
                    QuickReplies =
                    [
                        "如何申请 CUA 机器？",
                        "机器数量怎么估算？",
                        "CUA 成本是多少？",
                        "常见问题"
                    ],
                    Source = "onboarding_complete"
                };
            }
        }

        return new CustomerServiceResponse
        {
            Response = "感谢您的信息！现在您可以开始提问了。",
            QuickReplies = ["查看常见问题", "如何开始？"],
            Source = "onboarding_complete"
        };
    }

    /// <summary>
    /// 从知识库中查找匹配项（优先匹配当前场景）
    /// </summary>
    private KnowledgeItem? FindKnowledgeMatch(string message, string? currentScenario = null)
    {
        var lowerMessage = message.ToLower();
        Console.WriteLine($"[FindKnowledgeMatch] Searching for: '{lowerMessage}', Scenario: {currentScenario ?? "None"}");

        // 优先匹配当前场景的知识
        if (!string.IsNullOrEmpty(currentScenario))
        {
            foreach (var item in _knowledgeBase.Values.Where(k => k.Scenario == currentScenario))
            {
                if (item.Keywords != null)
                {
                    foreach (var keyword in item.Keywords)
                    {
                        if (lowerMessage.Contains(keyword.ToLower()))
                        {
                            Console.WriteLine($"[FindKnowledgeMatch] Matched '{item.Title}' via keyword '{keyword}' in scenario {currentScenario}");
                            return item;
                        }
                    }
                }
            }
        }

        // 如果当前场景没有匹配，再查找通用知识库
        foreach (var item in _knowledgeBase.Values)
        {
            if (item.Keywords != null)
            {
                foreach (var keyword in item.Keywords)
                {
                    if (lowerMessage.Contains(keyword.ToLower()))
                    {
                        Console.WriteLine($"[FindKnowledgeMatch] Matched '{item.Title}' via keyword '{keyword}'");
                        return item;
                    }
                }
            }
        }

        Console.WriteLine($"[FindKnowledgeMatch] No match found");
        return null;
    }

    /// <summary>
    /// 根据消息内容确定类别
    /// </summary>
    private string DetermineCategoryFromMessage(string message, string? scenario)
    {
        var lowerMessage = message.ToLower();

        // Search 场景分类
        if (scenario == "Search")
        {
            if (lowerMessage.Contains("bing") || lowerMessage.Contains("app id") || lowerMessage.Contains("申请"))
                return "Search-Bing申请";
            if (lowerMessage.Contains("api") || lowerMessage.Contains("调用") || lowerMessage.Contains("使用"))
                return "Search-API使用";
            return "Search入门";
        }

        // CUA 场景分类
        if (scenario == "CUA")
        {
            if (lowerMessage.Contains("机器") || lowerMessage.Contains("vm") || lowerMessage.Contains("申请"))
                return "CUA-机器申请";
            if (lowerMessage.Contains("成本") || lowerMessage.Contains("费用") || lowerMessage.Contains("价格"))
                return "CUA-成本";
            if (lowerMessage.Contains("api") || lowerMessage.Contains("调用") || lowerMessage.Contains("使用"))
                return "CUA-API使用";
            return "CUA入门";
        }

        // 通用分类
        if (lowerMessage.Contains("认证") || lowerMessage.Contains("auth") || lowerMessage.Contains("token"))
            return "认证";
        if (lowerMessage.Contains("配置") || lowerMessage.Contains("config"))
            return "配置";
        if (lowerMessage.Contains("错误") || lowerMessage.Contains("error"))
            return "错误";
        if (lowerMessage.Contains("sdk"))
            return "SDK";

        return "入门";
    }

    /// <summary>
    /// 使用Lumina Search API搜索相关文档来增强回答
    /// </summary>
    private async Task<List<SearchResultItem>?> SearchRelevantDocsAsync(string userMessage)
    {
        try
        {
            // 构建搜索查询，聚焦于Lumina官方文档（包括 eng.ms doc-site）
            var searchQuery = $"Lumina {userMessage} site:eng.ms OR site:docs.microsoft.com OR site:learn.microsoft.com";

            Console.WriteLine($"[CustomerServiceAgent] Searching for: {searchQuery}");

            // 调用Lumina Search API
            var searchResults = await _searchApi.SearchAsync(searchQuery, topN: 5);

            if (searchResults != null && searchResults.Count > 0)
            {
                Console.WriteLine($"[CustomerServiceAgent] Found {searchResults.Count} search results");
                return searchResults;
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CustomerServiceAgent] Search failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 搜索一般网络内容（不限于Lumina文档）
    /// </summary>
    private async Task<List<SearchResultItem>?> SearchGeneralWebAsync(string userMessage)
    {
        try
        {
            // 直接搜索用户问题，不添加"Lumina"前缀，不限制网站
            Console.WriteLine($"[CustomerServiceAgent] General web search for: {userMessage}");

            var searchResults = await _searchApi.SearchAsync(userMessage, topN: 5);

            if (searchResults != null && searchResults.Count > 0)
            {
                Console.WriteLine($"[CustomerServiceAgent] Found {searchResults.Count} web search results");
                return searchResults;
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CustomerServiceAgent] General web search failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 判断查询是否与Lumina相关（使用AI判断，更智能且节省搜索资源）
    /// </summary>
    private async Task<bool> IsLuminaRelatedQueryAsync(string message)
    {
        try
        {
            // 首先使用快速关键词匹配（作为快速路径）
            var lowerMessage = message.ToLower();
            var strongLuminaKeywords = new[] { "lumina", "search api", "cua api", "bing api", "app id" };

            if (strongLuminaKeywords.Any(keyword => lowerMessage.Contains(keyword)))
            {
                Console.WriteLine($"[IsLuminaRelated] Quick match: true (keyword found)");
                return true;
            }

            // 使用LLM进行智能判断（避免误判）
            var prompt = @"你是一个查询分类器。判断用户的问题是否与 Lumina 基础设施相关。

Lumina 是微软的搜索和浏览器自动化API平台，包括：
- Search API（网页搜索）
- CUA API（浏览器截图和自动化）
- 认证配置（Azure AD）
- SDK 和代码示例
- Partner Context 配置
- Bing API 申请

请判断以下问题是否与 Lumina 相关。只回答 'yes' 或 'no'，不要其他内容。

用户问题：" + message;

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5); // 快速判断，5秒超时

            var requestBody = new
            {
                model = _llmModel,
                messages = new[]
                {
                    new { role = "system", content = "You are a query classifier. Answer only 'yes' or 'no'." },
                    new { role = "user", content = prompt }
                },
                temperature = 0.1,
                max_tokens = 10
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var llmUrl = _llmEndpoint.TrimEnd('/') + "/chat/completions";
            var response = await client.PostAsync(llmUrl, content);

            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                var responseObj = JsonSerializer.Deserialize<JsonElement>(responseJson);
                var answer = responseObj.GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString()?.Trim().ToLower() ?? "no";

                bool isLuminaRelated = answer.Contains("yes");
                Console.WriteLine($"[IsLuminaRelated] AI判断: {isLuminaRelated} (answer: {answer})");
                return isLuminaRelated;
            }

            // LLM调用失败，默认为非Lumina相关（触发Web搜索）
            Console.WriteLine($"[IsLuminaRelated] LLM failed, default to false (trigger web search)");
            return false;
        }
        catch (Exception ex)
        {
            // 异常情况下，默认为非Lumina相关（触发Web搜索，确保用户能得到答案）
            Console.WriteLine($"[IsLuminaRelated] Exception: {ex.Message}, default to false");
            return false;
        }
    }

    /// <summary>
    /// 使用Search增强用户意图理解
    /// </summary>
    private async Task<string> EnhanceIntentUnderstandingAsync(string userMessage)
    {
        try
        {
            // 使用Search API搜索相似问题和解决方案
            var searchQuery = $"Lumina FAQ {userMessage}";
            var searchResults = await _searchApi.SearchAsync(searchQuery, topN: 3);
            
            if (searchResults != null && searchResults.Count > 0)
            {
                var context = new StringBuilder();
                context.AppendLine("基于搜索结果的上下文信息：");
                
                foreach (var result in searchResults)
                {
                    var content = result.SemanticDocument ?? string.Empty;
                    var snippet = content.Length > 150 ? content.Substring(0, 150) + "..." : content;
                    context.AppendLine($"- {result.Title}: {snippet}");
                }
                
                return context.ToString();
            }
            
            return string.Empty;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CustomerServiceAgent] Intent enhancement failed: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// 判断是否需要技术支持
    /// </summary>
    private bool NeedsTechnicalSupport(string message)
    {
        var lowerMessage = message.ToLower();
        var technicalKeywords = new[] { 
            "bug", "崩溃", "crash", "性能问题", "很慢", "卡住",
            "无法连接", "连接失败", "超时", "不工作", "报错",
            "生产环境", "紧急", "urgent", "critical"
        };

        return technicalKeywords.Any(keyword => lowerMessage.Contains(keyword));
    }

    /// <summary>
    /// 使用AI生成回答
    /// </summary>
    private async Task<string> GetAIResponseAsync(string message, string conversationId, UserOnboardingState? state = null, List<SearchResultItem>? webSearchResults = null)
    {
        try
        {
            // 如果提供了Web搜索结果，使用它们作为上下文
            string searchContext;
            if (webSearchResults?.Count > 0)
            {
                var context = new StringBuilder();
                context.AppendLine("基于Web搜索的最新信息：");
                foreach (var result in webSearchResults.Take(5))
                {
                    var content = result.SemanticDocument ?? string.Empty;
                    var snippet = content.Length > 300 ? content.Substring(0, 300) + "..." : content;
                    context.AppendLine($"- {result.Title}: {snippet}");
                    context.AppendLine($"  来源: {result.Url}");
                }
                searchContext = context.ToString();
            }
            else
            {
                // 否则使用Search增强意图理解（Lumina文档搜索）
                searchContext = await EnhanceIntentUnderstandingAsync(message);
            }

            var systemPrompt = BuildSystemPrompt(state, searchContext);
            var response = await CallLlmAsync(systemPrompt, message);
            return response;
        }
        catch (Exception ex)
        {
            // AI调用失败，记录错误并返回友好提示
            Console.WriteLine($"[CustomerServiceAgent] LLM call failed: {ex.Message}");
            Console.WriteLine($"[CustomerServiceAgent] Stack trace: {ex.StackTrace}");

            return "我理解您的问题。以下是一些可能有帮助的资源：\n\n" +
                   "**📚 常见问题：**\n" +
                   "• Lumina快速入门\n" +
                   "• 身份认证配置\n" +
                   "• 核心API功能\n" +
                   "• 常见错误处理\n\n" +
                   "**📖 技术文档：**\n" +
                   "https://docs.microsoft.com/lumina\n\n" +
                   "请告诉我您具体想了解什么，我会尽力解答。";
        }
    }

    /// <summary>
    /// 构建系统提示词
    /// </summary>
    private string BuildSystemPrompt(UserOnboardingState? state = null, string? searchContext = null)
    {
        var sb = new StringBuilder();

        // 根据是否有 Web 搜索结果，调整角色定位
        if (!string.IsNullOrEmpty(searchContext) && searchContext.Contains("基于Web搜索的最新信息"))
        {
            // 一般问题：通用 AI 助手
            sb.AppendLine("你是一个有帮助的 AI 助手。请根据提供的搜索结果回答用户的问题。");
            sb.AppendLine();
            sb.AppendLine("**搜索到的相关信息：**");
            sb.AppendLine(searchContext);
            sb.AppendLine();
            sb.AppendLine("**回答要求：**");
            sb.AppendLine("1. 基于搜索结果提供准确、全面的回答");
            sb.AppendLine("2. 用清晰、简洁的语言表达");
            sb.AppendLine("3. 如果搜索结果不足以回答问题，诚实说明");
            sb.AppendLine("4. 保持客观，不要编造信息");
            sb.AppendLine("5. 使用用户提问的语言回答（中文问题用中文回答，英文问题用英文回答）");
        }
        else
        {
            // Lumina 相关问题：技术支持助手
            sb.AppendLine("你是Lumina基础设施的技术支持助手，专门帮助新用户onboarding和解决初期问题。");
            sb.AppendLine();

            // 如果有搜索上下文，先添加
            if (!string.IsNullOrEmpty(searchContext))
            {
                sb.AppendLine("**搜索到的相关信息（可参考但不要直接引用）：**");
                sb.AppendLine(searchContext);
                sb.AppendLine();
            }

            sb.AppendLine("**你的职责：**");
            sb.AppendLine("1. 帮助用户理解Lumina的功能和使用方法");
            sb.AppendLine("2. 指导用户完成认证配置和环境搭建");
            sb.AppendLine("3. 解答关于API使用的基础问题");
            sb.AppendLine("4. 提供示例代码和最佳实践建议");
            sb.AppendLine("5. 对于复杂问题，建议联系技术支持团队");
            sb.AppendLine();
            sb.AppendLine("**沟通风格：**");
            sb.AppendLine("- 专业、友好、耐心");
            sb.AppendLine("- 使用清晰的技术语言");
            sb.AppendLine("- 提供具体的步骤和示例");
            sb.AppendLine("- 主动提供相关文档链接");
            sb.AppendLine();
            sb.AppendLine("**知识范围：**");
            sb.AppendLine("- Lumina API（Search, Open, Find, CUA）");
            sb.AppendLine("- Azure AD认证配置");
            sb.AppendLine("- .NET SDK使用");
            sb.AppendLine("- 配置文件和环境变量");
            sb.AppendLine("- 常见错误和排查方法");
            sb.AppendLine("- 配额和限制说明");
            sb.AppendLine();

            if (state != null && state.InfoCollected)
            {
                sb.AppendLine("**用户背景：**");
                sb.AppendLine($"- 使用场景：{state.UseCase}");
                sb.AppendLine($"- 开发语言：{state.DevLanguage}");
                sb.AppendLine("请根据用户背景提供定制化的建议。");
                sb.AppendLine();
            }

            sb.AppendLine("**重要原则：**");
            sb.AppendLine("- 如果不确定答案，诚实告知并提供文档链接");
            sb.AppendLine("- 对于Bug或紧急问题，建议联系技术支持");
            sb.AppendLine("- 鼓励用户查看官方文档获取最新信息");
            sb.AppendLine("- 回答要简洁明了，避免过于冗长");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 调用LLM API
    /// </summary>
    private async Task<string> CallLlmAsync(string systemPrompt, string userMessage)
    {
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        var requestBody = new
        {
            model = _llmModel,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            },
            temperature = 0.7,
            max_tokens = 500
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync($"{_llmEndpoint}/chat/completions", content);
        response.EnsureSuccessStatusCode();

        var responseText = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<LlmApiResponse>(responseText);

        return result?.Choices?[0]?.Message?.Content ?? 
               "抱歉，我暂时无法处理您的问题。请联系人工客服获取帮助。";
    }

    /// <summary>
    /// 生成上下文相关的快速回复（根据场景定制）
    /// </summary>
    private List<string> GenerateContextualReplies(string message, string? scenario = null)
    {
        var lowerMessage = message.ToLower();

        // 根据场景返回定制化回复
        if (scenario == "Search")
        {
            if (lowerMessage.Contains("bing") || lowerMessage.Contains("app id"))
                return ["如何测试 Bing API？", "授权需要多久？", "Search 配置示例"];
            if (lowerMessage.Contains("api") || lowerMessage.Contains("调用"))
                return ["Search 示例代码", "搜索结果格式", "高级搜索技巧"];
            return ["如何申请 Bing API？", "Search 配置示例", "常见问题"];
        }

        if (scenario == "CUA")
        {
            if (lowerMessage.Contains("机器") || lowerMessage.Contains("vm"))
                return ["机器数量估算", "申请需要多久？", "成本优化"];
            if (lowerMessage.Contains("成本") || lowerMessage.Contains("费用"))
                return ["如何降低成本？", "按需付费吗？", "机器数量优化"];
            if (lowerMessage.Contains("api") || lowerMessage.Contains("截图"))
                return ["CUA 示例代码", "如何释放会话？", "会话超时时间"];
            return ["如何申请 CUA 机器？", "机器数量怎么估算？", "常见问题"];
        }

        // 通用回复
        if (lowerMessage.Contains("认证") || lowerMessage.Contains("auth") || lowerMessage.Contains("token"))
        {
            return ["如何配置Azure AD？", "Token过期怎么办？", "需要什么权限？"];
        }
        else if (lowerMessage.Contains("配置") || lowerMessage.Contains("config") || lowerMessage.Contains("设置"))
        {
            return ["配置文件示例", "环境变量怎么设置？", "必填配置有哪些？"];
        }
        else if (lowerMessage.Contains("sdk") || lowerMessage.Contains("client") || lowerMessage.Contains("示例"))
        {
            return ["SDK安装方法", "有示例代码吗？", "如何更新SDK？"];
        }
        else if (lowerMessage.Contains("错误") || lowerMessage.Contains("error") || lowerMessage.Contains("问题"))
        {
            return ["常见错误处理", "如何查看日志？", "联系技术支持"];
        }
        else
        {
            return ["查看快速入门", "常见问题列表", "查看技术文档"];
        }
    }

    // ========== 对话历史管理方法 ==========

    /// <summary>
    /// 获取指定对话的完整历史
    /// </summary>
    public List<ConversationHistoryEntry> GetConversationHistory(string conversationId)
    {
        return _historyService.GetConversationHistory(conversationId);
    }

    /// <summary>
    /// 获取最近的对话列表
    /// </summary>
    public List<ConversationSummary> GetRecentConversations(int limit = 10)
    {
        return _historyService.GetRecentConversations(limit);
    }

    /// <summary>
    /// 获取对话统计信息
    /// </summary>
    public ConversationStats GetConversationStats()
    {
        return _historyService.GetStats();
    }

    /// <summary>
    /// 搜索历史对话
    /// </summary>
    public List<ConversationHistoryEntry> SearchHistory(string keyword, int maxResults = 50)
    {
        return _historyService.SearchHistory(keyword, maxResults);
    }

    /// <summary>
    /// 删除指定对话的历史
    /// </summary>
    public bool DeleteConversation(string conversationId)
    {
        return _historyService.DeleteConversation(conversationId);
    }
}

// ========== 数据模型 ==========

public class UserOnboardingState
{
    public string? Scenario { get; set; } // "Search" or "CUA"
    public string? UseCase { get; set; }
    public string? DevLanguage { get; set; }
    public bool InfoCollected { get; set; }
    public List<string> ConversationHistory { get; set; } = [];
}

// ========== 数据模型 ==========

public class CustomerServiceResponse
{
    [JsonPropertyName("response")]
    public string Response { get; set; } = "";

    [JsonPropertyName("quickReplies")]
    public List<string>? QuickReplies { get; set; }

    [JsonPropertyName("knowledgeCard")]
    public KnowledgeCard? KnowledgeCard { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = ""; // knowledge_base, ai_generated, transfer_to_human
}

public class KnowledgeCard
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}

public class KnowledgeItem
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string Scenario { get; set; } = "General"; // "Search", "CUA", or "General"
    public string[]? Keywords { get; set; }
    public string[]? QuickReplies { get; set; }
}

public class CustomerServiceRequest
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("conversationId")]
    public string ConversationId { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }
}

public class LlmApiResponse
{
    [JsonPropertyName("choices")]
    public List<LlmApiChoice>? Choices { get; set; }
}

public class LlmApiChoice
{
    [JsonPropertyName("message")]
    public LlmApiMessage? Message { get; set; }
}

public class LlmApiMessage
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}
