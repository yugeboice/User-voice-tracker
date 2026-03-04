# Partner Context 合并指南

## 目录

- [背景](#背景)
  - [为什么需要 Partner Context？](#为什么需要-partner-context)
- [Git 基础知识](#git-基础知识)
  - [分支命名规范](#分支命名规范)
- [场景一：你基于 minimal-api-call 新建了分支](#场景一你基于-minimal-api-call-新建了分支)
- [场景二：你直接在 minimal-api-call 上做了本地修改（未提交）](#场景二你直接在-minimal-api-call-上做了本地修改未提交)
- [场景三：你直接在 minimal-api-call 上做了本地修改（已提交）](#场景三你直接在-minimal-api-call-上做了本地修改已提交)
- [⭐ 最佳实践建议](#-最佳实践建议)
- [Partner Context 相关的文件变更](#partner-context-相关的文件变更)
- [冲突解决指南](#冲突解决指南)
- [合并后验证](#合并后验证)
- [配置你自己的 Partner Context](#配置你自己的-partner-context)
- [遇到问题？](#遇到问题)

---

## 背景

`minimal-api-call` 分支新增了 **Partner Context** 功能。

### 为什么需要 Partner Context？

1. **遥测和监控**：Lumina 团队可以根据 Partner Context 追踪 API 调用来源，帮助分析使用情况和排查问题
2. **问题排查**：当 API 出现问题时，Partner Context 可以帮助快速定位是哪个团队、哪个场景遇到了问题
3. **使用分析**：了解哪些功能被哪些团队使用，帮助 Lumina 团队优化服务

### 本指南的目的

本指南帮助你将 Partner Context 代码合并到你的本地实现中。

---

## Git 基础知识

在开始之前，先了解几个基本概念：

| 术语 | 解释 |
|------|------|
| **分支 (branch)** | 代码的一个独立副本，可以在上面自由修改而不影响其他分支 |
| **远程 (origin)** | GitHub 上的代码仓库，是大家共享代码的地方 |
| **本地** | 你电脑上的代码副本 |
| **提交 (commit)** | 保存你的修改到 Git 历史记录中，类似于"存档" |
| **合并 (merge)** | 把一个分支的修改合并到另一个分支 |
| **冲突 (conflict)** | 当两个人修改了同一处代码，Git 无法自动决定用哪个版本 |

### 分支命名规范

创建自己的分支时，建议使用以下格式：

```
user/你的alias/功能名
```

例如：
- `user/yufanli/competitor-research`
- `user/zhangsan/news-summary`
- `user/lisi/stock-analysis`

---

## 场景一：你基于 minimal-api-call 新建了分支

如果你从 `minimal-api-call` 新建了自己的分支（例如 `user/yufanli/competitor-research`），可以通过 merge 获取更新：

```powershell
# 切换到你自己的分支
# 作用：告诉 Git "我现在要在这个分支上工作"
# 把下面的分支名换成你自己的分支名
git checkout user/yufanli/competitor-research

# 从 GitHub 下载最新的 minimal-api-call 分支代码
# 作用：把远程的更新下载到本地，但还没有合并到你的分支
git fetch origin minimal-api-call

# 把下载的更新合并到你当前的分支
# 作用：把 Partner Context 的新代码合并到你的 my-feature 分支
git merge origin/minimal-api-call
```

如果有冲突，Git 会提示你解决。主要关注 `Program.cs` 文件，因为 Partner Context 在那里做了修改。

---

## 场景二：你直接在 minimal-api-call 上做了本地修改（未提交）

> 💡 **建议**：完成合并后，建议将你的修改放到独立分支中管理，避免以后再次遇到类似的合并问题。

### 步骤 1：查看你修改了哪些文件

```powershell
# 查看当前状态
# 作用：显示你修改了哪些文件，哪些是新增的，哪些还没保存
git status
```

你会看到类似这样的输出：
```
Changes not staged for commit:
  modified:   Program.cs
  modified:   SearchApi.cs
```

### 步骤 2：基于当前修改创建新分支

```powershell
# 创建并切换到新分支，同时保留你的所有修改
# 作用：相当于把你的修改"搬"到一个新的分支上
# 注意：把 my-feature 换成你想要的分支名，常见格式为 user/你的alias/你的feature名 ，比如 "user/yufanli/competitor-research"
git checkout -b my-feature
```

现在你的本地修改已经在 `my-feature` 分支上了，原来的 `minimal-api-call` 分支不受影响。

### 步骤 3：保存你的修改

```powershell
# 把所有修改的文件添加到"待提交"列表
# 作用：告诉 Git "这些文件的修改我要保存"
git add .

# 正式保存（提交）你的修改
# 作用：创建一个存档点，记录你目前为止的所有修改
# 引号里的文字是对这次修改的描述，可以改成你自己的描述
git commit -m "我的功能实现"
```

### 步骤 4：合并最新的 Partner Context 代码

```powershell
# 从 GitHub 下载最新的 minimal-api-call 分支代码
# 作用：获取包含 Partner Context 的最新代码
git fetch origin minimal-api-call

# 把新代码合并到你当前的分支
# 作用：把 Partner Context 功能合并到你的代码中
git merge origin/minimal-api-call
```

**如果出现冲突怎么办？**

Git 会告诉你哪些文件有冲突。打开冲突文件，你会看到类似这样的标记：

```
<<<<<<< HEAD
你的代码
=======
Partner Context 的代码
>>>>>>> origin/minimal-api-call
```

你需要手动编辑文件，保留你需要的部分，删除 `<<<<<<<`、`=======`、`>>>>>>>` 这些标记。

> 💡 **小技巧**：你也可以让 Coding Agent 帮你解决冲突！只需要告诉它：
> 
> "请帮我解决这个文件的冲突，保留我的代码和 Partner Context 的代码"
> 
> Coding Agent 会帮你分析冲突内容，并给出合并后的代码。

解决完所有冲突后：

```powershell
# 把解决完冲突的文件添加到待提交列表
git add .

# 完成合并
git commit -m "合并 Partner Context 更新"
```

---

## 场景三：你直接在 minimal-api-call 上做了本地修改（已提交）

> 💡 **建议**：完成合并后，建议将你的修改放到独立分支中管理。

### 步骤 1：基于当前提交创建新分支

```powershell
# 创建并切换到新分支
# 作用：基于你当前的代码（包括你的提交）创建一个新分支
# 分支名格式：user/你的alias/功能名，例如 user/yufanli/competitor-research
git checkout -b user/你的alias/功能名
```

### 步骤 2：合并最新的 Partner Context 代码

```powershell
# 从 GitHub 下载最新代码
git fetch origin minimal-api-call

# 合并到你的分支
git merge origin/minimal-api-call
```

如果有冲突，参照场景二的冲突解决方法。

### 步骤 3（可选）：重置 minimal-api-call 到远程状态

如果你希望本地的 `minimal-api-call` 分支恢复到和 GitHub 上一样的状态：

```powershell
# 切换到 minimal-api-call 分支
git checkout minimal-api-call

# 强制重置到远程状态
# ⚠️ 警告：这会丢弃这个分支上所有本地修改！
# 但别担心，你的修改已经安全地保存在 my-feature 分支了
git reset --hard origin/minimal-api-call
```

---

## ⭐ 最佳实践建议

**以后请始终在独立分支上进行开发**，不要直接修改 `minimal-api-call` 分支。

好处：
- 随时可以从 `minimal-api-call` 拉取最新更新
- 你的修改不会与上游更新冲突
- 可以同时维护多个功能分支

推荐的工作流：

```powershell
# 1. 切换到 minimal-api-call 分支
git checkout minimal-api-call

# 2. 拉取最新代码（下载 + 合并一步完成）
git pull origin minimal-api-call

# 3. 基于最新代码创建你的功能分支
# 分支名格式：user/你的alias/功能名，例如 user/yufanli/competitor-research
git checkout -b user/你的alias/功能名

# 4. 在你的分支上开发...
# （修改代码、测试等）

# 5. 保存你的修改
git add .
git commit -m "描述你做了什么修改"

# 6. 当 minimal-api-call 有更新时，合并到你的分支
git fetch origin minimal-api-call
git merge origin/minimal-api-call
```

---

## Partner Context 相关的文件变更

以下是 Partner Context 功能涉及的文件，合并时请特别注意：

| 文件 | 变更说明 |
|------|----------|
| `Internal/PartnerContextConfiguration.cs` | **新增文件** - 配置类，无需手动合并 |
| `appsettings.Template.json` | 新增了 `PartnerContext` 配置节 |
| `Program.cs` | 加载配置 + 传递给 `LuminaApiOptions` 和 `CuaApi` |
| `CuaApi.cs` | 构造函数增加了 `partnerContext` 参数，添加了 HTTP 头 |
| `MinimalApiCall.csproj` | SDK 版本从 `2025.11.17.1741` 升级到 `2025.11.26.128` |
| `README.md` | 添加了 Partner Context 文档说明 |
| `.gitignore` | 添加了 `Directory.Build.props` |

---

## 冲突解决指南

### Program.cs 冲突

这是最可能发生冲突的文件。Partner Context 主要修改了两处：

**1. 配置加载部分（约第 26-42 行）**

新增了以下代码，放在 `var llmModel = ...` 之后：

```csharp
// Load Partner Context configuration
var partnerContext = new PartnerContextConfiguration();
config.GetSection("PartnerContext").Bind(partnerContext);

// Validate and log Partner Context
if (partnerContext.HasPartnerContext)
{
    if (!partnerContext.IsValid())
    {
        Console.WriteLine("[Warning] Partner Context has invalid hierarchy...");
    }
    Console.WriteLine($"[Config] {partnerContext.GetSummary()}");
}
else
{
    Console.WriteLine("[Config] Partner Context: Not configured (optional)");
}
```

**2. GetOrCreateProxy() 方法（约第 61-72 行）**

`LuminaApiOptions` 增加了 Partner Context 字段：

```csharp
var options = new LuminaApiOptions
{
    Endpoint = luminaEndpoint,
    LuminaApiTokenProvider = async () => await tokenProvider(),
    // 新增以下 5 行
    Partner = partnerContext.Partner,
    ScenarioGroup = partnerContext.ScenarioGroup,
    ScenarioName = partnerContext.ScenarioName,
    Application = partnerContext.Application,
    Component = partnerContext.Component
};
```

**3. CuaApi 初始化（约第 93 行）**

```csharp
// 旧代码
cuaApi = new CuaApi(cuaEndpoint, tokenProvider);

// 新代码
cuaApi = new CuaApi(cuaEndpoint, tokenProvider, partnerContext);
```

### CuaApi.cs 冲突

如果你修改过 `CuaApi` 的构造函数，需要添加 `partnerContext` 参数。

> 💡 **小技巧**：让 Coding Agent 帮你处理！告诉它：
> 
> "我修改过 CuaApi.cs，请帮我把 Partner Context 的修改合并进去，保留我的修改"
> 
> Coding Agent 会帮你正确地添加 `partnerContext` 参数和相关代码。

---

## 合并后验证

```powershell
# 1. 编译项目
dotnet build

# 2. 运行测试
dotnet run

# 3. 检查控制台输出，应该看到类似：
# [Config] Partner Context: Partner=PM playground, ScenarioGroup=APIDemo
```

---

## 配置你自己的 Partner Context

合并完成后，需要在 `appsettings.json` 中添加 `PartnerContext` 配置。
下面的配置是我们在做PM vibe coding时可以统一使用的信息。

**如果你的 `appsettings.json` 还没有 `PartnerContext` 部分**，直接复制下面的代码，粘贴到 `appsettings.json` 文件中（注意放在某个 `}` 后面，前面要加逗号）：

```json
"PartnerContext": {
  "Partner": "PM playground",
  "ScenarioGroup": "APIDemo",
  "ScenarioName": "",
  "Application": "",
  "Component": ""
}
```

完整示例（假设你原来的 `appsettings.json` 有 `AzureAd` 配置）：

```json
{
  "AzureAd": {
    "TenantId": "...",
    "ClientId": "..."
  },
  "PartnerContext": {
    "Partner": "PM playground",
    "ScenarioGroup": "APIDemo",
    "ScenarioName": "",
    "Application": "",
    "Component": ""
  }
}
```

> 💡 **注意**：JSON 格式要求每个配置项之间用逗号分隔，最后一项后面不能有逗号。如果不确定格式是否正确，可以让 Coding Agent 帮你检查。

---

## 遇到问题？

把错误信息发给 Coding Agent，它可以帮你解决！
