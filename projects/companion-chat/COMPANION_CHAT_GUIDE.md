# 陪伴聊天功能使用指南

## 🎯 功能概述

陪伴聊天是一个独立的科幻风格AI对话界面，提供沉浸式的AI陪伴体验。

## 🚀 使用方法

### 方式1：从主页面进入（推荐）
1. 访问 `http://localhost:8400/index.html`
2. 登录Microsoft账号
3. 点击顶部的 **✨ 陪伴模式** 按钮
4. 将在新标签页打开科幻风格的陪伴聊天界面

### 方式2：直接访问
直接在浏览器打开：`http://localhost:8400/companion.html`

## ✨ 主要特性

### 1. 科幻视觉效果
- 暗黑渐变背景 + 动态浮动粒子
- 彩虹渐变旋转光环头像
- 玻璃拟态半透明设计
- 发光边框和脉冲动画

### 2. 5种AI人设
- **默认（小助）** - 友好的智能助手
- **温暖治愈（小暖）** - 温柔体贴的陪伴者 🌸
- **活泼开朗（小阳）** - 元气满满的活力源泉 ⚡
- **知性文艺（小书）** - 博学优雅的知心朋友 📚
- **务实理性（小智）** - 理性思考的智慧伙伴 🧠

### 3. 语音功能
- 6种语音风格（甜美女生、活力女生、知性女声、磁性男声、稳重男声、少年音）
- 自动播放开关
- 点击🔊按钮手动播放
- 智能降级（Edge TTS → 浏览器TTS）

### 4. 实时统计
- 对话轮次计数
- 对话时长统计
- 情感评分显示

## 🔧 技术修复说明

### 问题诊断
用户反馈"无法对话"，错误信息显示"抱歉，我遇到了一些问题...😢"

### 根本原因
1. **缺少认证** - companion.html没有集成MSAL认证
2. **字段名不匹配** - API期望`message`字段，前端发送的是`userMessage`
3. **响应解析错误** - API返回`reply`字段，前端期望`response`字段

### 修复内容

#### 1. 添加MSAL认证支持
```javascript
// 引入MSAL库
<script src="https://alcdn.msauth.net/browser/2.14.2/js/msal-browser.min.js"></script>

// 配置认证
const msalConfig = {
    auth: {
        clientId: "872e1bb6-4d36-4a66-812e-84f0a18e6e66",
        authority: "https://login.microsoftonline.com/72f988bf-86f1-41af-91ab-2d7cd011db47",
        redirectUri: window.location.origin + "/companion.html"
    }
};
```

#### 2. 修复API调用
```javascript
// 修复前
body: JSON.stringify({
    sessionId: sessionId,
    userMessage: text,  // ❌ 错误字段名
    personality: currentPersonality
})

// 修复后
body: JSON.stringify({
    sessionId: sessionId,
    message: text,  // ✅ 正确字段名
    personality: currentPersonality
})
```

#### 3. 修复响应解析
```javascript
// 修复前
if (data.success) {
    addMessage(data.response, 'assistant', true);  // ❌ 错误字段
}

// 修复后
if (data.reply || data.sessionId) {
    addMessage(data.reply || data.response || '收到！', 'assistant', true);  // ✅ 正确字段
}
```

#### 4. 增强错误处理
- 添加详细的错误日志
- 区分认证错误和网络错误
- 自动重新登录机制
- 友好的错误提示

### API端点检查
确认后端API配置正确：
```csharp
// Program.cs
app.MapPost("/api/companion-chat", async (WebCompanionChatRequest req) => { ... });

// 请求模型
record WebCompanionChatRequest(string SessionId, string Message, string? Personality = null);

// 响应模型
public class CompanionChatResponse {
    public string Reply { get; set; }  // 主要回复字段
    public string Error { get; set; }  // 错误信息
}
```

## 🎮 使用流程

1. **初次访问** → 自动检测登录状态
2. **未登录** → 显示登录提示，点击登录按钮
3. **登录成功** → 显示欢迎消息和人设选择
4. **选择人设** → 从下拉菜单选择AI性格
5. **选择语音** → 从下拉菜单选择语音风格
6. **开始对话** → 输入消息，按Enter发送
7. **语音播放** → 点击🔊或开启自动播放

## 📝 注意事项

1. **认证要求**：必须先登录Microsoft账号
2. **Token刷新**：Token过期会自动重新登录
3. **会话隔离**：每个标签页独立sessionId
4. **人设切换**：切换人设会清空当前对话
5. **语音质量**：建议安装edge-tts以获得最佳语音效果

## 🐛 故障排除

### 问题1：提示"需要登录"
**解决**：点击登录按钮，使用Microsoft账号登录

### 问题2：消息发送失败
**检查**：
1. 确认已登录（检查浏览器Console）
2. 确认后端服务运行在 localhost:8400
3. 查看Console错误日志

### 问题3：语音无法播放
**解决**：
1. 检查浏览器是否允许自动播放
2. 首次播放需要用户手动点击
3. 会自动降级到浏览器TTS

### 问题4：API返回401/403
**解决**：
1. Token已过期，刷新页面重新登录
2. 检查MSAL配置是否正确

## 🔍 调试技巧

### 开启浏览器Console
- Chrome/Edge: F12 → Console标签
- 查看详细错误日志：
  - `API错误: 401 ...` - 认证问题
  - `发送消息错误: ...` - 网络/代码问题
  - `响应错误: ...` - API返回格式问题

### 检查Network
- F12 → Network标签
- 筛选 `/api/companion-chat`
- 查看Request Payload和Response

### 测试API
```bash
# 使用curl测试（需要先获取token）
curl -X POST http://localhost:8400/api/companion-chat \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{"sessionId":"test-123","message":"你好","personality":"默认"}'
```

## 📚 相关文件

- `/wwwroot/companion.html` - 陪伴聊天前端页面
- `/CompanionChatService.cs` - 陪伴聊天服务
- `/Program.cs` - API端点定义（第186-228行）
- `/TextToSpeechService.cs` - 语音合成服务

## 🎨 自定义

### 修改人设
编辑 `CompanionChatService.cs` 的 `GetPersonalityPrompt()` 方法

### 修改语音
编辑 `TextToSpeechService.cs` 的 `GetVoiceCode()` 方法

### 修改样式
编辑 `companion.html` 的 `<style>` 部分

## ✅ 当前状态

- [x] MSAL认证集成
- [x] API字段名修复
- [x] 响应解析修复
- [x] 错误处理增强
- [x] 主页面入口添加
- [x] 详细日志记录

## 🚀 下一步

可以考虑添加：
1. 语音输入功能（语音识别）
2. 聊天记录持久化
3. 多轮对话上下文优化
4. 自定义头像上传
5. 对话分享功能
