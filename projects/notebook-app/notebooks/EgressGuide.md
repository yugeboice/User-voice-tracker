# Egress-LLM 启动指南 (Lumina-API-Demo 项目)

## 概述

egress-llm 是一个 LLM API 代理服务，运行在本地端口 4141，为本项目的 Notebook 图像生成功能提供图像生成接口 (`/chatgpt/convo2im`)。

**重要提示**：本项目的图像生成功能依赖 egress-llm，而不是 copilot-api。copilot-api 只支持文本 LLM，不支持图像生成。

## 本项目服务架构

运行 Lumina-API-Demo Notebook 功能需要启动两个服务：

1. **egress-llm** (端口 4141) - 提供 LLM 和图像生成 API
2. **MinimalApiCall.exe** (端口 8400) - 后端服务，提供 Notebook Web UI

## 服务配置

- **端口**: 4141
- **模型**: dev-anthropic-claude-sonnet-4-0
- **API 版本**: 2023-06-01
- **提供商**: LuminaPlayground
- **环境**: local
- **认证方式**: MSAL (Microsoft Authentication Library)

## 快速启动

### 方式 1: 一键启动 egress-llm

在 VS Code 终端或独立的 PowerShell 窗口中运行：

```powershell
Get-Process -Name bun -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep -Seconds 2; cd C:\ws\ado\CopilotLumina\sources\dev\SandboxService\AIAgents\ts-agents\egress-llm; bun src/index.ts
```

### 方式 2: 分步启动（推荐用于调试）

```powershell
# 1. 停止现有的 bun 进程
Get-Process -Name bun -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

# 2. 切换到 egress-llm 目录
cd C:\ws\ado\CopilotLumina\sources\dev\SandboxService\AIAgents\ts-agents\egress-llm

# 3. 启动服务
bun src/index.ts
```

### 方式 3: 在独立窗口中启动（避免被 VS Code 终端中断）

```powershell
Start-Process pwsh -ArgumentList "-NoExit", "-Command", "cd C:\ws\ado\CopilotLumina\sources\dev\SandboxService\AIAgents\ts-agents\egress-llm; bun src/index.ts"
```

## 启动后端服务 (MinimalApiCall)

egress-llm 启动后，需要启动本项目的后端服务：

### 推荐方式：直接运行编译后的 exe

```powershell
# 1. 确保项目已编译
cd C:\PMVibeCoding\PM_Playground\Lumina-API-Demo
dotnet build

# 2. 运行编译后的可执行文件
cd bin\Debug\net8.0
Start-Process -FilePath ".\MinimalApiCall.exe" -WorkingDirectory (Get-Location)
```

**为什么不用 `dotnet run`？**  
在 VS Code 终端中使用 `dotnet run` 会导致服务自动退出。直接运行 exe 文件更稳定。

### 验证两个服务都已启动

```powershell
# 检查 egress-llm (端口 4141)
Test-NetConnection -ComputerName localhost -Port 4141 -InformationLevel Quiet

# 检查后端服务 (端口 8400)
Test-NetConnection -ComputerName localhost -Port 8400 -InformationLevel Quiet
```

两个命令都应返回 `True`。

### 访问 Notebook Web UI

浏览器打开：`http://localhost:8400/notebook-home.html`

## 详细启动步骤

### 1. 清理现有进程（可选但推荐）

```powershell
# 停止所有 bun 进程
Get-Process -Name bun -ErrorAction SilentlyContinue | Stop-Process -Force

# 等待进程完全终止
Start-Sleep -Seconds 2
```

### 2. 检查端口占用情况

```powershell
# 检查端口 4141 是否被占用
Get-NetTCPConnection -LocalPort 4141 -ErrorAction SilentlyContinue
```

### 3. 释放端口（如果被占用）

```powershell
# 强制终止占用端口 4141 的进程
Get-NetTCPConnection -LocalPort 4141 -ErrorAction SilentlyContinue | ForEach-Object { 
    Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue 
}
Start-Sleep -Seconds 2
```

### 4. 启动服务

```powershell
# 切换到 egress-llm 目录
cd C:\ws\ado\CopilotLumina\sources\dev\SandboxService\AIAgents\ts-agents\egress-llm

# 启动服务
bun src/index.ts
```

## 验证服务状态

### egress-llm 健康检查

```powershell
# 检查服务是否正常运行
Invoke-WebRequest -Uri http://localhost:4141/health -TimeoutSec 5
```

成功返回示例：
```
StatusCode        : 200
StatusDescription : OK
```

### 后端服务健康检查

```powershell
# 检查 8400 端口
Test-NetConnection -ComputerName localhost -Port 8400 -InformationLevel Quiet

# 或在浏览器访问
# http://localhost:8400/notebook-home.html
```

### 查看进程状态

```powershell
# 查看 bun 进程 (egress-llm)
Get-Process -Name bun -ErrorAction SilentlyContinue

# 查看后端服务进程
Get-Process -Name MinimalApiCall -ErrorAction SilentlyContinue

# 查看端口监听状态
Get-NetTCPConnection -LocalPort 4141,8400 -ErrorAction SilentlyContinue | Select-Object LocalPort, State, OwningProcess
```

## 可用端点

服务启动后提供以下 API 端点：

- `POST http://localhost:4141/v1/messages` - Claude 消息 API
- `POST http://localhost:4141/v1/messages/count_tokens` - Token 计数
- `POST http://localhost:4141/app/register/agent` - Agent 注册
- `POST http://localhost:4141/chatgpt/convo2im` - 图像生成
- `GET http://localhost:4141/health` - 健康检查

## 环境配置

服务会自动加载以下配置文件：

1. `.env` - 基础环境变量
2. `.env.local` - 本地覆盖配置

关键环境变量：
```env
ENVIRONMENT=local
LLMAPI_PROVIDER=LuminaPlayground
LLMAPI_PROXY_PORT=4141
LLMAPI_PROXY_MODEL=dev-anthropic-claude-sonnet-4-0
LLMAPI_PROXY_ANTHROPIC_VERSION=2023-06-01
LLMAPI_PROXY_STREAM_MODE=false
```

## 认证机制

egress-llm 使用三层认证回退机制：

1. **LUMINA_TOKEN 环境变量**（优先级最高）
2. **MSAL 自动认证**（本地开发推荐）
   - 自动使用 Microsoft 账号认证
   - 无需手动配置 token
   - 支持 tiantianguo@microsoft.com 等企业账号
3. **文件 token**（回退选项）

## Docker 环境中使用

如果在 Docker 容器中需要访问 egress-llm：

```yaml
environment:
  - EGRESS_LLM_API_ENDPOINT=http://host.docker.internal:4141
```

或在容器启动命令中：
```bash
docker run -e EGRESS_LLM_API_ENDPOINT=http://host.docker.internal:4141 ...
```

## 完整启动流程示例

### 第一次启动（完整流程）

```powershell
# 1. 停止可能存在的旧进程
Get-Process | Where-Object {$_.ProcessName -in @("bun","MinimalApiCall","node")} | Stop-Process -Force
Start-Sleep -Seconds 2

# 2. 启动 egress-llm（在独立窗口，避免被中断）
Start-Process pwsh -ArgumentList "-NoExit", "-Command", "cd C:\ws\ado\CopilotLumina\sources\dev\SandboxService\AIAgents\ts-agents\egress-llm; bun src/index.ts"
Start-Sleep -Seconds 5

# 3. 验证 egress-llm 已启动
Test-NetConnection -ComputerName localhost -Port 4141 -InformationLevel Quiet

# 4. 编译项目（如果未编译）
cd C:\PMVibeCoding\PM_Playground\Lumina-API-Demo
dotnet build

# 5. 启动后端服务
cd bin\Debug\net8.0
Start-Process -FilePath ".\MinimalApiCall.exe" -WorkingDirectory (Get-Location)
Start-Sleep -Seconds 5

# 6. 验证后端服务已启动
Test-NetConnection -ComputerName localhost -Port 8400 -InformationLevel Quiet

# 7. 打开浏览器
Start-Process "http://localhost:8400/notebook-home.html"
```

### 日常启动（服务已编译）

```powershell
# 1. 清理旧进程
Get-Process | Where-Object {$_.ProcessName -in @("bun","MinimalApiCall")} | Stop-Process -Force
Start-Sleep -Seconds 2

# 2. 启动 egress-llm
Start-Process pwsh -ArgumentList "-NoExit", "-Command", "cd C:\ws\ado\CopilotLumina\sources\dev\SandboxService\AIAgents\ts-agents\egress-llm; bun src/index.ts"

# 3. 启动后端服务
Start-Process -FilePath "C:\PMVibeCoding\PM_Playground\Lumina-API-Demo\bin\Debug\net8.0\MinimalApiCall.exe" -WorkingDirectory "C:\PMVibeCoding\PM_Playground\Lumina-API-Demo\bin\Debug\net8.0"

# 4. 等待几秒后打开浏览器
Start-Sleep -Seconds 8
Start-Process "http://localhost:8400/notebook-home.html"
```

## 常见问题

### 问题 1: 端口 4141 被占用

**错误信息**:
```
ERROR: Failed to start server: Failed to start server. Is port 4141 in use?
```

**解决方案**:
```powershell
# 查找占用端口的进程
Get-NetTCPConnection -LocalPort 4141 -ErrorAction SilentlyContinue | ForEach-Object { 
    Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue 
} | Select-Object Id, ProcessName, Path

# 停止占用端口的进程
Get-NetTCPConnection -LocalPort 4141 | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }
```

### 问题 2: 端口 8400 被占用

**错误信息**:
```
Failed to bind to address http://127.0.0.1:8400: address already in use
```

**解决方案**:
```powershell
# 查找并停止占用 8400 端口的进程
Get-NetTCPConnection -LocalPort 8400 -ErrorAction SilentlyContinue | ForEach-Object {
    Stop-Process -Id $_.OwningProcess -Force
}
Start-Sleep -Seconds 2
```

### 问题 3: 图像生成失败，返回 404 Not Found

**错误信息**:
```
[ImageGen] HTTP Error NotFound: 404 Not Found
```

**原因**: 4141 端口运行的不是 egress-llm，而是 copilot-api（不支持图像生成）

**解决方案**:
1. 停止所有 node/bun 进程：
   ```powershell
   Get-Process | Where-Object {$_.ProcessName -in @("node","bun")} | Stop-Process -Force
   ```
2. 按照本文档启动 egress-llm
3. 验证端点可用：
   ```powershell
   Invoke-WebRequest -Uri http://localhost:4141/health
   ```

### 问题 4: VS Code 终端中 `dotnet run` 服务自动退出

**现象**: 服务启动后显示 "Now listening on: http://localhost:8400"，然后立即退出

**原因**: VS Code 终端的某种机制导致进程被中断

**解决方案**: 使用编译后的 exe 文件启动（见上文"启动后端服务"部分）

### 问题 5: bun 进程无法启动

**检查 bun 是否安装**:
```powershell
bun --version
```

**重新安装 bun**:
```powershell
npm install -g bun
```

### 问题 6: 认证失败

**检查 MSAL 认证状态**:
- 确保使用企业 Microsoft 账号登录
- 检查 `.env.local` 配置
- 查看服务日志中的认证信息

### 问题 7: Docker 构建导致服务中断

**问题描述**: 
Docker 构建过程可能会终止系统中运行的 bun 进程

**解决方案**:
1. 在独立的 PowerShell 窗口中启动 egress-llm（不通过 VS Code 终端）
2. 使用 `-sb` (skip build) 标志跳过 Docker 构建
3. 使用预构建的 Docker 镜像

## 停止服务

### 停止 egress-llm
```powershell
# 停止所有 bun 进程
Get-Process -Name bun -ErrorAction SilentlyContinue | Stop-Process -Force
```

### 停止后端服务
```powershell
# 停止 MinimalApiCall 进程
Get-Process -Name MinimalApiCall -ErrorAction SilentlyContinue | Stop-Process -Force
```

### 一键停止所有相关服务
```powershell
Get-Process | Where-Object {$_.ProcessName -in @("bun","MinimalApiCall")} | Stop-Process -Force
```

## 日志查看

服务启动时会输出详细日志，包括：

- OTEL 初始化状态
- 环境变量加载信息
- 配置详情
- 服务端点列表
- 启动状态

## 在其他项目中集成

如果需要在其他项目中使用 egress-llm：

1. 确保 egress-llm 服务已启动
2. 配置项目的 API 端点：
   - 文本 LLM：`http://localhost:4141/v1/messages`
   - 图像生成：`http://localhost:4141/chatgpt/convo2im`
3. 如果在 Docker 中：使用 `http://host.docker.internal:4141`
4. 验证连接：访问 `/health` 端点

### 本项目的配置位置

- **appsettings.json**: 
  ```json
  "CopilotApi": {
    "Endpoint": "http://localhost:4141",
    "Model": "gpt-5"
  }
  ```

- **ImageGenerationService.cs**: 
  - 默认使用 `CopilotApi:Endpoint` 配置
  - 调用 `/chatgpt/convo2im` 端点生成图像

### 环境变量配置（可选）

```powershell
# 如果需要覆盖配置文件中的端点
$env:EGRESS_LLM_API_ENDPOINT="http://localhost:4141"
```

## 相关文件

- `src/index.ts` - 服务入口文件
- `src/auth/auth-manager.ts` - 认证管理
- `src/auth/msal-auth.ts` - MSAL 认证实现
- `.env` - 环境配置
- `.env.local` - 本地配置覆盖

## 技术栈

- **运行时**: Bun
- **框架**: Hono
- **认证**: @azure/msal-node
- **SDK**: @anthropic-ai/sdk
- **遥测**: @copilot-lumina/telemetry

## 维护建议

1. **定期检查服务状态**
   - 使用 `Test-NetConnection` 验证端口
   - 检查进程是否正常运行

2. **监控端口占用情况**
   - 4141 (egress-llm) 和 8400 (后端服务) 不能被其他程序占用
   - 使用 `Get-NetTCPConnection -LocalPort 4141,8400` 检查

3. **避免使用错误的服务**
   - ❌ 不要使用 `npx copilot-api@0.5.14 start` - 它不支持图像生成
   - ✅ 必须使用 egress-llm 才能生成图像

4. **编译和部署**
   - 代码修改后重新编译：`dotnet build`
   - 重启后端服务以应用更改

5. **调试技巧**
   - 查看 egress-llm 终端输出了解请求日志
   - 检查 `notebooks/Data/{notebook-id}/images/` 目录中的日志文件
   - 后端服务日志会显示在启动的 PowerShell 窗口中

## 快速参考

| 服务 | 端口 | 进程名 | 检查命令 |
|------|------|--------|----------|
| egress-llm | 4141 | bun | `Test-NetConnection localhost -Port 4141 -InformationLevel Quiet` |
| 后端服务 | 8400 | MinimalApiCall | `Test-NetConnection localhost -Port 8400 -InformationLevel Quiet` |

| 端点 | 用途 |
|------|------|
| `http://localhost:4141/health` | egress-llm 健康检查 |
| `http://localhost:4141/v1/messages` | 文本 LLM API |
| `http://localhost:4141/chatgpt/convo2im` | 图像生成 API |
| `http://localhost:8400` | 后端服务主页 |
| `http://localhost:8400/notebook-home.html` | Notebook Web UI |

---

**最后更新**: 2026-01-05
**项目**: Lumina-API-Demo
**分支**: user/tiantianguo/inforgraphic-gen-claude-skill
