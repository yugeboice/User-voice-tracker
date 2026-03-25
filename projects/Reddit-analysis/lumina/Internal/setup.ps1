# Lumina API Demo - Windows 环境安装脚本
# 以管理员身份运行 PowerShell，然后执行: .\setup.ps1

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Lumina API Demo - 环境安装脚本" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 检查是否已安装 .NET 8
Write-Host "[1/4] 检查 .NET SDK..." -ForegroundColor Yellow
$dotnetVersion = dotnet --version 2>$null
if ($dotnetVersion -like "8.*") {
    Write-Host "  ✅ .NET SDK $dotnetVersion 已安装" -ForegroundColor Green
} else {
    Write-Host "  ⬇️  正在安装 .NET 8 SDK..." -ForegroundColor Yellow
    winget install Microsoft.DotNet.SDK.8 --silent --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  ✅ .NET 8 SDK 安装完成" -ForegroundColor Green
        Write-Host "  ⚠️  请重新打开 PowerShell 以使 dotnet 命令生效" -ForegroundColor Yellow
    } else {
        Write-Host "  ❌ .NET 8 SDK 安装失败，请手动安装: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Red
    }
}

# 检查是否已安装 Node.js
Write-Host ""
Write-Host "[2/4] 检查 Node.js..." -ForegroundColor Yellow
$nodeVersion = node --version 2>$null
if ($nodeVersion) {
    Write-Host "  ✅ Node.js $nodeVersion 已安装" -ForegroundColor Green
} else {
    Write-Host "  ⬇️  正在安装 Node.js..." -ForegroundColor Yellow
    winget install OpenJS.NodeJS.LTS --silent --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  ✅ Node.js 安装完成" -ForegroundColor Green
        Write-Host "  ⚠️  请重新打开 PowerShell 以使 node/npx 命令生效" -ForegroundColor Yellow
    } else {
        Write-Host "  ❌ Node.js 安装失败，请手动安装: https://nodejs.org/" -ForegroundColor Red
    }
}

# 安装 Azure Artifacts Credential Provider
Write-Host ""
Write-Host "[3/4] 安装 Azure Artifacts Credential Provider..." -ForegroundColor Yellow
try {
    iex "& { $(irm https://aka.ms/install-artifacts-credprovider.ps1) }"
    Write-Host "  ✅ Credential Provider 安装完成" -ForegroundColor Green
} catch {
    Write-Host "  ❌ Credential Provider 安装失败: $_" -ForegroundColor Red
}

# 创建配置文件
Write-Host ""
Write-Host "[4/4] 检查配置文件..." -ForegroundColor Yellow
if (Test-Path "appsettings.json") {
    Write-Host "  ✅ appsettings.json 已存在" -ForegroundColor Green
} else {
    if (Test-Path "appsettings.Template.json") {
        Copy-Item "appsettings.Template.json" "appsettings.json"
        Write-Host "  ✅ 已创建 appsettings.json (从模板复制)" -ForegroundColor Green
        Write-Host "  ⚠️  请编辑 appsettings.json 填入你的 Azure AD 配置" -ForegroundColor Yellow
    } else {
        Write-Host "  ❌ 找不到 appsettings.Template.json" -ForegroundColor Red
    }
}

# 完成
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  安装完成！" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "下一步:" -ForegroundColor White
Write-Host "  1. 编辑 appsettings.json 填入你的配置" -ForegroundColor White
Write-Host "  2. 运行: dotnet restore --interactive" -ForegroundColor White
Write-Host "     (首次需要浏览器登录 Azure DevOps)" -ForegroundColor Gray
Write-Host "  3. 终端1运行: npx copilot-api@0.5.14 start" -ForegroundColor White
Write-Host "  4. 终端2运行: dotnet run" -ForegroundColor White
Write-Host ""
