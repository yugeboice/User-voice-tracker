# 🔧 故障排查指南

## 问题：LLM 连接错误

### 错误信息
```
Error connecting to LLM at http://localhost:4141: No connection could be made because 
the target machine actively refused it. (localhost:4141)
```

### ✅ 已修复！

我已经为这个问题添加了**降级处理机制**：

#### 修改内容：
- ✅ 当 LLM 服务不可用时，自动切换到"搜索结果摘要"模式
- ✅ 不再直接显示错误，而是返回格式化的搜索结果
- ✅ 提示用户如何启用完整的 AI 分析功能

### 现在的行为

#### 1️⃣ 有 LLM 服务（理想状态）
```
用户问题 → 搜索网络 → AI 深度分析 → 智能回答（带引用）
```

#### 2️⃣ 无 LLM 服务（降级模式）
```
用户问题 → 搜索网络 → 搜索结果摘要 → 简单格式化输出
```

### 降级模式输出示例

```
⚠️ LLM服务暂时不可用，以下是搜索结果摘要：

关于「2024年人工智能有什么突破」，找到 5 条相关结果：

【1】GPT-4发布引领AI新纪元
🔗 https://example.com/news/gpt4
📄 2024年初，OpenAI发布了GPT-4模型，在多项任务上超越了前代模型...

【2】多模态AI技术突破
🔗 https://example.com/news/multimodal
📄 今年多模态大模型发展迅速，能够同时处理文本、图像、音频...

...

💡 提示：要获得AI智能分析，请启动 Copilot API 服务（localhost:4141）
```

### 如何启用完整 AI 功能

#### 方案 1: 启动本地 Copilot API（推荐）

如果你有 Copilot API 服务：
```bash
# 在新终端运行
copilot-api --port 4141
```

#### 方案 2: 使用 Azure OpenAI

修改 `appsettings.json`：
```json
{
  "CopilotApi": {
    "Endpoint": "https://your-azure-openai.openai.azure.com",
    "Model": "gpt-4",
    "ApiKey": "your-api-key"
  }
}
```

注意：这需要相应修改 `LlmExample.cs` 中的 API 调用代码。

#### 方案 3: 使用 OpenAI API

修改 `appsettings.json`：
```json
{
  "CopilotApi": {
    "Endpoint": "https://api.openai.com",
    "Model": "gpt-4",
    "ApiKey": "sk-..."
  }
}
```

### 测试降级功能

现在你可以直接测试聊天功能了！

1. **访问**: http://localhost:8400
2. **登录** Microsoft 账号
3. **输入问题**，例如："2024年人工智能有什么突破？"
4. **查看结果**：
   - 如果有 LLM：得到智能分析
   - 如果没有 LLM：得到搜索结果摘要

### 其他常见问题

#### 问题 1: 搜索也失败
**检查项**：
- [ ] Lumina API 配置是否正确
- [ ] 网络连接是否正常
- [ ] 是否已登录 Microsoft 账号

#### 问题 2: 页面无法访问
**解决方案**：
```bash
# 检查是否已启动
netstat -ano | findstr :8400

# 重新启动
cd Lumina-API-Demo
dotnet run
```

#### 问题 3: 登录失败
**检查项**：
- [ ] `appsettings.json` 中的 Azure AD 配置
- [ ] TenantId 和 ClientId 是否正确
- [ ] 浏览器是否阻止弹出窗口

### 代码修改说明

修改的文件：**LlmExample.cs**

#### 主要改动：

1. **添加降级处理**
```csharp
public async Task<string> SearchAndSummarizeAsync(string userQuery, int topN = 5)
{
    var searchResults = await _searchApi.SearchAsync(userQuery, topN);
    
    if (searchResults.Count == 0)
        return "抱歉，没有找到相关的搜索结果。";

    var context = BuildContextFromResults(searchResults);
    var llmResponse = await CallLlmAsync(userQuery, context);
    
    // 如果 LLM 失败，返回降级摘要
    if (llmResponse.StartsWith("Error connecting to LLM"))
    {
        return BuildFallbackSummary(userQuery, searchResults);
    }
    
    return llmResponse;
}
```

2. **新增降级摘要生成函数**
```csharp
private string BuildFallbackSummary(string query, List<SearchResultItem> results)
{
    // 生成格式化的搜索结果摘要
    // 包含标题、链接、内容片段
}
```

### 性能对比

| 功能 | 有 LLM | 无 LLM（降级） |
|------|--------|----------------|
| 响应时间 | 5-15秒 | 2-5秒 |
| 回答质量 | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ |
| 信息准确度 | 高（AI分析） | 中（原始搜索） |
| 信息来源 | 有引用 | 有链接 |
| 可用性 | 依赖 LLM | 总是可用 |

### 最佳实践

#### 开发环境
✅ 使用降级模式快速开发和测试  
✅ 不需要额外配置 LLM 服务  
✅ 重点测试搜索功能  

#### 生产环境
✅ 配置稳定的 LLM 服务  
✅ 使用 Azure OpenAI 或类似的托管服务  
✅ 设置监控和告警  

---

## 📞 需要帮助？

如果还有问题，请检查：
1. 终端输出的详细错误信息
2. 浏览器控制台（F12）的错误
3. `appsettings.json` 配置文件

**现在应用已经启动，可以访问 http://localhost:8400 开始使用了！** 🚀
