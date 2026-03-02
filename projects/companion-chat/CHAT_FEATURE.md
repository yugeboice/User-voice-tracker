# 💬 AI 智能聊天功能说明

## 功能概述

新增的AI智能聊天功能可以让用户在聊天框中提问，系统会自动：
1. 🔍 搜索网络获取最新信息
2. 📖 提取搜索结果的内容
3. 🤖 使用LLM（大语言模型）基于搜索结果生成准确的回答

## 使用方法

### 1. 启动应用
```bash
dotnet run
```
然后打开浏览器访问: http://localhost:8400

### 2. 登录
- 点击"登录 Microsoft 账号"按钮
- 在弹出的浏览器窗口中完成 Microsoft 账号认证

### 3. 使用聊天功能
- 登录后会看到"💬 AI 智能聊天"模块（位于页面顶部）
- 在输入框中输入任何问题
- 点击"发送"按钮或按回车键
- AI会自动搜索网络最新信息并生成回答

### 示例问题
- "2024年人工智能领域有什么重大突破？"
- "最新的GPT模型有哪些特点？"
- "今天天气怎么样？"
- "最近有什么科技新闻？"

## 技术实现

### 后端 API
- **端点**: `POST /api/chat`
- **请求参数**:
  ```json
  {
    "question": "用户的问题",
    "topN": 5  // 搜索结果数量，默认5
  }
  ```
- **响应**:
  ```json
  {
    "answer": "基于搜索结果的回答",
    "timestamp": "2024-01-29T10:30:00Z"
  }
  ```

### 前端实现
- 现代化的聊天UI界面
- 消息气泡样式（用户消息蓝色，AI回复灰色）
- 打字动画指示器
- 自动滚动到最新消息
- 支持回车键发送

### 核心流程
1. 用户输入问题 → 前端发送到 `/api/chat`
2. 后端调用 `SearchApi.SearchAsync()` 搜索网络
3. 搜索结果传递给 `LlmExample.SearchAndSummarizeAsync()`
4. LLM基于搜索内容生成回答
5. 回答返回前端并显示在聊天界面

## 配置要求

确保 `appsettings.json` 中配置了以下内容：

```json
{
  "LuminaConfiguration": {
    "ApiEndpoint": "你的Lumina API端点",
    "CuaEndpoint": "你的CUA端点"
  },
  "CopilotApi": {
    "Endpoint": "http://localhost:4141",
    "Model": "gpt-4"
  }
}
```

## 功能特点

✅ **实时搜索**: 每次提问都会搜索网络获取最新信息  
✅ **智能回答**: LLM基于搜索结果生成准确、相关的回答  
✅ **引用来源**: 回答中会引用搜索结果来源（[Source N]格式）  
✅ **友好界面**: 现代化的聊天UI，类似主流聊天应用  
✅ **错误处理**: 优雅处理网络错误和API异常  

## 相关文件

- **后端API**: [Program.cs](Program.cs) - `/api/chat` 端点实现
- **前端UI**: [wwwroot/index.html](wwwroot/index.html) - 聊天界面
- **搜索逻辑**: [SearchApi.cs](SearchApi.cs) - 网络搜索功能
- **LLM集成**: [LlmExample.cs](LlmExample.cs) - 搜索结果处理和LLM调用

## 注意事项

⚠️ 需要有效的 Lumina API 凭证  
⚠️ 需要 Copilot API 服务运行在 localhost:4141  
⚠️ 搜索和LLM生成可能需要几秒钟时间  
⚠️ 请确保网络连接正常  
