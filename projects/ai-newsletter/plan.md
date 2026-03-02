# AI Radar - 自动化情报系统

> 定时从 ChatGPT / Gemini / Genspark 拉取 AI 前沿信息，自动上传 NotebookLM 生成音频/PPT，帮助快速了解 AI 行业动态

## 📋 项目背景

### 需求概述
- **采集源**：ChatGPT（综合信息）、Gemini（Google 生态）、Genspark（社媒信号）
- **关注范围**：
  - AI 行业大事、国内外大厂动作
  - 大模型迭代（方向、新产品、能力、Arena 榜单）
  - AI 医疗垂直领域动向
  - Agent / 工具产品 / release notes
  - 社媒领军人物观点（Altman / Musk 等）、Reddit 讨论、论文/博客

### 输出要求
- 足够新、权威、覆盖全
- 固定模板（日报/周报）
- 可展示（可转发给 leader）
- 自动化（定时运行、失败告警、可追溯）

---

## 🎯 分步实现计划

### ✅ Step 0: ChatGPT MVP（已完成）
- [x] 实现 ChatGPT 单源抓取
- [x] 使用手动 cookies 认证
- [x] 切换 ArenaAtHome workspace
- [x] 提取「AI行业情报」对话
- [x] 保存为 `reports/report-2026-01-18.md`

**验证结果：** ✅ 成功提取 4258 字符 Newsletter

---

### 🔄 Step 1: Gemini 单源抓取（进行中）

**目标：** 复制 ChatGPT 模式实现 Gemini 抓取

**任务清单：**
- [ ] 手动打开 https://gemini.google.com 检查界面结构
- [ ] 录制 Gemini 登录态到 `auth/gemini-cookies.json`
- [ ] 创建 `scrape_gemini.js` 适配 Gemini 选择器
- [ ] 提取目标对话最新回复
- [ ] 保存为 `reports/report-gemini-2026-01-18.md`
- [ ] 手动运行验证成功

**验证标准：**
- [ ] `auth/gemini-cookies.json` 存在且有效
- [ ] 浏览器成功登录 Gemini
- [ ] 找到目标对话并提取 AI 回复（>100 字符）
- [ ] 生成报告文件且内容正确

---

### 🔄 Step 2: Genspark 单源抓取

**目标：** 实现 Genspark 平台抓取

**任务清单：**
- [ ] 调研 Genspark 界面（对话式/搜索式）
- [ ] 录制 Genspark 登录态
- [ ] 创建 `scrape_genspark.js`
- [ ] 适配选择器并提取内容
- [ ] 保存为 `reports/report-genspark-2026-01-18.md`
- [ ] 手动验证

---

### 🔄 Step 3: NotebookLM 上传验证

**目标：** 验证自动上传文件到 NotebookLM 可行性

**任务清单：**
- [ ] 录制 NotebookLM 登录态
- [ ] 创建 `test_notebooklm.js`
- [ ] 定位"添加来源"按钮并点击
- [ ] 自动上传测试 markdown 文件
- [ ] 验证文件在 NotebookLM 中可见

**本步骤不实现音频生成，仅验证上传流程**

---

### 🔄 Step 4: NotebookLM 音频生成

**目标：** 完成完整 NotebookLM 自动化流程

**任务清单：**
- [ ] 扩展 Step 3 脚本
- [ ] 定位"生成音频概览"按钮
- [ ] 点击并等待生成完成（轮询状态）
- [ ] 下载音频到 `outputs/audio/audio-2026-01-18.mp3`
- [ ] 手动验证音频质量

---

### 🔄 Step 5: 统一执行脚本

**目标：** 一键运行所有抓取 + NotebookLM 流程

**任务清单：**
- [ ] 创建 `run_daily.js` 编排器
- [ ] 依次调用三个抓取脚本
- [ ] 自动上传所有报告到 NotebookLM
- [ ] 添加错误处理（某步失败继续执行）
- [ ] 生成执行日志 `STATUS-2026-01-18.log`
- [ ] 手动运行完整流程验证

---

### 🔄 Step 6: 重构模块化 + 环境变量

**目标：** 优化代码结构，支持配置化

**任务清单：**
- [ ] 创建 `.env.template`（对话名称、workspace 等）
- [ ] 抽取共享逻辑到 `utils/browser-helper.js`
- [ ] 重构各脚本使用环境变量
- [ ] 回归测试所有功能

**目录结构：**
```
Auto_AInewsletter/
├── scrapers/
│   ├── chatgpt.js
│   ├── gemini.js
│   └── genspark.js
├── integrations/
│   └── notebooklm.js
├── utils/
│   └── browser-helper.js
├── auth/
│   ├── chatgpt-cookies.json
│   ├── gemini-cookies.json
│   └── genspark-cookies.json
├── reports/
│   ├── report-chatgpt-2026-01-18.md
│   ├── report-gemini-2026-01-18.md
│   └── report-genspark-2026-01-18.md
├── outputs/
│   └── audio/
├── .env.template
└── run_daily.js
```

---

### 🔄 Step 7: 同事安装文档

**目标：** 编写详细文档支持同事快速部署

**任务清单：**
- [ ] 更新 README 包含安装步骤（截图）
- [ ] 录制登录态操作视频教程
- [ ] 创建 `scripts/setup-auth-guide.js` 打印录制命令
- [ ] 编写常见问题 FAQ
- [ ] 找同事试用并收集反馈
- [ ] 优化文档和脚本

**同事使用流程：**
```bash
git clone <repo>
cd Auto_AInewsletter
npm install
# 按 README 录制登录态
npm run test
npm run daily
```

---

## 📖 当前使用说明（Step 0 - ChatGPT）

### 快速开始

#### 1. 导出 ChatGPT Cookies

在 Chrome 打开 https://chatgpt.com 并登录：
1. 按 `⌘ + Option + I` 打开 DevTools
2. 切换到 **Console** 标签
3. 输入 `allow pasting` 并回车
4. 粘贴并运行：
```javascript
copy(JSON.stringify([
  ...document.cookie.split('; ').map(c => {
    const [name, value] = c.split('=');
    return { name, value, domain: '.chatgpt.com', path: '/', secure: true, httpOnly: false, sameSite: 'Lax' };
  })
], null, 2))
```
5. Cookies 已复制到剪贴板

#### 2. 保存 Cookies

打开 `auth/cookies.json`，粘贴刚才复制的内容并保存。

#### 3. 运行抓取

```bash
node scrape_manual_cookies.js
```

**脚本执行流程：**
1. 加载 `auth/cookies.json`
2. 启动浏览器并注入 cookies
3. 访问 ChatGPT
4. 切换到 ArenaAtHome workspace
5. 等待历史记录加载（8 秒）
6. 查找「AI行业情报」对话
7. 提取最后一条 AI 回复
8. 保存为 `reports/report-2026-01-18.md`

---

## 📝 项目日志

### 2026-01-18
**完成：**
- ✅ 项目初始化（npm + Playwright）
- ✅ ChatGPT 手动 cookies 认证方案
- ✅ ArenaAtHome workspace 切换
- ✅ 「AI行业情报」对话提取
- ✅ 首个 Newsletter 抓取成功（4258 字符）

**进行中：**
- 🔄 Step 1: Gemini 抓取实现

---

## ⚙️ 环境信息

- **Node.js**: v23.11.0
- **npm**: 11.7.0
- **Playwright**: ^1.57.0
- **OS**: macOS

---

## 🚨 故障排查

### Cookies 失效
**症状：** 脚本显示"登录态已失效"

**解决方案：**
1. 重新导出 cookies（按上述步骤）
2. 确保在导出前 ChatGPT 处于登录状态

### 找不到对话
**症状：** "未找到对话「AI行业情报」"

**解决方案：**
1. 检查对话名称是否正确
2. 确认对话在 ArenaAtHome workspace 中
3. 增加等待时间（脚本中修改 `waitForTimeout` 值）

### 浏览器打开后卡住
**症状：** 浏览器启动后无响应

**解决方案：**
1. 关闭所有 Chrome 进程
2. 重新运行脚本
3. 检查网络连接
