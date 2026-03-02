# Lumina API - 最小化实现 Demo

一个最小化的 Lumina API 调用示例。帮助你快速掌握 API 基本用法，并在简洁的代码基础上自由扩展、设计你自己的功能。

---

## 🚀 快速开始 (DevBox)

### 第一步：下载代码

在 VS Code 中按 `` Ctrl + ` `` 打开终端，复制粘贴以下命令：

```powershell
git clone https://github.com/ai-microsoft/Lumina-API-Demo.git
cd Lumina-API-Demo
git checkout minimal-api-call
```

---

### 第二步：一键安装环境

在 VS Code 终端中运行：

```powershell
.\Internal\setup.ps1
```

这个脚本会自动帮你安装：
- .NET 8 SDK（运行代码的环境）
- Node.js（运行 Copilot API 需要）
- Azure Artifacts Credential Provider（下载 Lumina 包需要）

> ⏱️ 首次安装可能需要 5-10 分钟，请耐心等待

---

### 第三步：登录并下载 Lumina 包

运行以下命令：

```powershell
dotnet restore --interactive
```

**会发生什么：**
1. 浏览器会自动弹出 Microsoft 登录页面
2. 用你的 **@microsoft.com** 账号登录
3. 登录成功后，回到 VS Code 终端，会看到包下载完成

> 💡 这一步只需要做一次，之后电脑会记住你的登录状态

---

### 第四步：配置文件

将你收到的 `appsettings.json` 文件复制粘贴到 `Lumina-API-Demo` 文件夹下。

---

### 第五步：启动 Copilot API（LLM 服务）

在 VS Code 终端右上角点击 **+** 新建一个终端（这个终端要保持运行），输入：

```powershell
npx copilot-api@0.5.14 start
```

**首次运行需要 GitHub 授权：**
1. 终端会显示类似：`Please enter the code "A1B2-C3D4" in https://github.com/login/device`
2. 打开浏览器，访问 https://github.com/login/device
3. 输入终端显示的代码（如 `A1B2-C3D4`）
4. 点击授权
5. 回到 VS Code 终端，会看到服务启动成功

> ⚠️ **保持这个终端运行，不要关闭！**

---

### 第六步：运行 Demo！

在 VS Code 终端右上角点击 **+** 再新建一个终端，运行：

```powershell
dotnet run
```

🎉 **大功告成！** 打开浏览器访问 **http://localhost:8401**，你会看到一个简洁的 Web 界面，可以尝试各种 Lumina API 功能：

- **Search + LLM**：Web 搜索，可选用 LLM 总结结果
- **Open + Find**：打开网页获取内容，可选在页面内查找
- **CUA**：浏览器自动化截图

---

## ❓ 遇到问题？

如果在安装或运行过程中遇到任何错误，**把错误信息复制给Coding Agent**，它可以帮你排查问题！

---

## 📚 让 Coding Agent 读官方文档

想让 Coding Agent 写出更专业的 Lumina 代码？可以把官方文档 clone 到本地，让它先学习再写代码：

```powershell
git clone https://o365exchange.visualstudio.com/DefaultCollection/O365%20Core/_git/CopilotLumina partner
```

然后告诉 Coding Agent：

> "请先读一下 partner 文件夹里的 Lumina 官方文档，了解 API 的详细用法，然后再帮我写代码。"

这样 Coding Agent 就能参考官方文档，写出更准确、更符合最佳实践的代码。

---

## 📖 Search API 详解

Search API 是最常用的功能，下面详细介绍它的工作原理。

### 调用流程

```
用户输入 "AI news"
       ↓
前端 (index.html) → fetch('/api/search', { query: "AI news", topN: 5 })
       ↓
后端 (Program.cs) → /api/search 路由
       ↓
SearchApi.cs → _proxy.SearchAsync(request)
       ↓
Lumina 云端服务 → 返回搜索结果
       ↓
前端显示结果
```

### 核心代码：SearchApi.cs

```csharp
public async Task<List<SearchResultItem>> SearchAsync(string query, int topN = 10)
{
    // 1. 构造请求
    var request = new SearchRequest
    {
        Requests = new List<SearchRequestItem>
        {
            new SearchRequestItem
            {
                Q = query,           // 搜什么query
                TopN = topN,         // 最多返回几条结果
                Source = "web_with_bing"  // 搜索的数据源
            }
        }
    };

    // 2. 调用 Lumina SDK（SDK 会把 request 发送给 Lumina 云端）
    var response = await _proxy.SearchAsync(request);
    
    // 3. 返回结果列表
    return response?.Results?.Take(topN).ToList() ?? new List<SearchResultItem>();
}
```

当执行 `_proxy.SearchAsync(request)` 时，SDK 会自动帮你：
1. 把请求发给 Lumina 云端
2. 等 Lumina 搜索完成
3. 把结果拿回来

### Lumina 云端收到的请求

```json
{
  "requests": [
    {
      "q": "AI news",
      "topN": 5,
      "source": "web_with_bing"
    }
  ]
}
```

### Lumina 返回的响应

```json
{
  "pageId": "29893556ad1845189e439459355201d3",
  "results": [
    {
      "answerType": "WebPages",
      "url": "https://example.com/ai-news-article",
      "title": "Latest AI News - Example",
      "semanticDocument": "This article discusses the latest developments in AI..."
    },
    {
      "answerType": "WebPages", 
      "url": "https://another-site.com/ai-update",
      "title": "AI Industry Update",
      "semanticDocument": "The AI industry saw significant changes this week..."
    }
  ],
  "toolState": {
    "sessionId": "995e40972e2c4cf0af8c50d5efd045e9"
  }
}
```

每个结果包含：
- `url` - 网页链接
- `title` - 标题
- `semanticDocument` - 内容摘要（可以喂给 LLM 做总结）

---

## 📁 项目文件说明

### 🎯 可扩展的文件（Vibe Coding可以尝试的）

| 文件 | 说明 | 扩展场景 |
|------|------------|--------------|
| `Program.cs` | 主程序，控制整个服务 | 想串联多个 API、加新接口 |
| `wwwroot/index.html` | 网页界面 | 想改界面样式、加按钮 |
| `SearchApi.cs` | 搜索功能 | 想改搜什么、返回几条、只要最近几天的 |
| `OpenApi.cs` | 打开网页读内容 | 想打开哪个网址、读多少行 |
| `FindApi.cs` | 在网页里找关键词 | 想找什么词、在哪个页面找 |
| `CuaApi.cs` | 控制浏览器自动操作 | 想打开什么网站、做什么操作（点击/输入/滚动） |
| `LlmExample.cs` | AI 总结功能 | 想改 AI 怎么回答、用什么语气、写system prompt|

### 🔒 Internal 文件夹（不需要修改）

| 文件 | 说明 |
|------|------|
| `Internal/TokenService.cs` | 身份认证服务 |
| `Internal/setup.ps1` | 一键安装脚本 |

---

## 🎨 Vibe Coding 示例：竞品分析工具

下面用一个完整的例子，展示如何通过 Vibe Coding 把这些 API 组合成一个实用工具。

### 场景：自动化竞品分析

假设你想分析几个竞品最近的动态，生成一份分析报告。

#### 第一步：智能生成搜索词并批量搜索

> 告诉 Coding Agent：
> 
> "我想做一个竞品分析功能。用户只需要输入几个竞品名称（比如 OpenAI、Google AI、Anthropic），系统先用 AI 根据竞品名称生成更精准的搜索词（比如把"OpenAI"扩展成"OpenAI 最新产品发布"、"OpenAI 融资新闻"等），然后自动搜索最近一周的新闻，每个竞品返回 5 条结果。"

Coding Agent 会先调用 LLM 生成搜索词（主要改 `LlmExample.cs`），然后用生成的搜索词进行 batch search（主要改 `SearchApi.cs`、`Program.cs`），Search API 的 `recency` 参数设为 7 表示只要最近 7 天的内容。

#### 第二步：获取详细内容

> "搜索结果只有标题和摘要，信息不够。我希望能自动打开每个竞品的前 2 条新闻链接，获取完整的文章内容。"

Coding Agent 会串联 Search 和 Open API（主要改 `OpenApi.cs`、`Program.cs`），先搜索再自动打开链接读取全文。

#### 第三步：LLM 生成分析报告

> "现在有了完整的新闻内容，我希望 AI 能帮我生成一份分析报告。
> 
> 分析维度：
> - 产品功能更新：最近发布了什么新功能？
> - 市场策略：有什么定价、合作、扩张动作？
> - 用户反馈：用户和媒体的评价如何？
> - 竞争洞察：这些动态对我们有什么启示？
> 
> 输出要求：
> - 每个竞品单独一节
> - 用表格对比关键信息
> - 最后给出总结和行动建议
> - 用中文输出，语气专业但易懂"

Coding Agent 会把这些要求写成 system prompt（主要改 `LlmExample.cs`），让 LLM 按照指定格式输出分析报告。

#### 第四步：截取产品页面截图

> "分析报告需要配图。我希望能自动打开各竞品的官网首页（比如 openai.com、anthropic.com），截图保存下来作为报告配图。"

Coding Agent 会用 CUA 的浏览器自动化功能（主要改 `CuaApi.cs`），依次打开网站并截图。

#### 第五步：整合成完整功能

> "把上面的功能串成一个完整流程：用户输入竞品名称和官网地址，一键生成包含新闻摘要、详细分析、官网截图的完整竞品报告。"

Coding Agent 会在 `Program.cs` 里把 Search → Open → LLM → CUA 串联起来，形成完整流程。

---

## 🌐 完整 Web Demo

如果你想看带界面的完整 Web 应用，请切换到 `main` 分支：

```powershell
git checkout main
```
