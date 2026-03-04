using System.Text;

namespace MinimalApiCall;

/// <summary>
/// 本地知识库服务 - 管理本地文件作为补充资料，并保存对话结果
/// </summary>
public class LocalKnowledgeService
{
    private string? _knowledgePath;
    private string _agentPath = @"C:\knowledge\agent"; // Agent文件路径
    private readonly List<string> _supportedExtensions = new() { ".md", ".txt", ".html", ".json", ".csv" };

    /// <summary>
    /// 设置知识库路径
    /// </summary>
    public void SetKnowledgePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _knowledgePath = null;
            return;
        }

        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        _knowledgePath = path;
    }

    /// <summary>
    /// 获取当前知识库路径
    /// </summary>
    public string? GetKnowledgePath() => _knowledgePath;

    /// <summary>
    /// 设置Agent路径
    /// </summary>
    public void SetAgentPath(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            _agentPath = path;
        }
    }

    /// <summary>
    /// 获取Agent路径
    /// </summary>
    public string GetAgentPath() => _agentPath;

    /// <summary>
    /// 读取指定Agent文件内容
    /// </summary>
    public async Task<string?> GetAgentContentAsync(string agentName)
    {
        if (string.IsNullOrEmpty(_agentPath) || !Directory.Exists(_agentPath))
            return null;

        var agentFile = Path.Combine(_agentPath, $"{agentName}.md");
        if (!File.Exists(agentFile))
            return null;

        try
        {
            return await File.ReadAllTextAsync(agentFile);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取所有可用的Agent列表
    /// </summary>
    public List<AgentInfo> GetAvailableAgents()
    {
        var agents = new List<AgentInfo>();
        
        if (string.IsNullOrEmpty(_agentPath) || !Directory.Exists(_agentPath))
            return agents;

        var files = Directory.GetFiles(_agentPath, "*.md");
        foreach (var file in files)
        {
            var fi = new System.IO.FileInfo(file);
            agents.Add(new AgentInfo
            {
                Name = Path.GetFileNameWithoutExtension(file),
                FilePath = file,
                Size = fi.Length,
                LastModified = fi.LastWriteTime
            });
        }

        return agents;
    }

    /// <summary>
    /// 根据用户问题智能选择合适的Agent
    /// </summary>
    public async Task<string?> SelectAgentByIntentAsync(string userQuestion)
    {
        // 关键词匹配策略
        var keywords = new Dictionary<string, List<string>>
        {
            { "CLAUDE", new List<string> { "产品", "设计", "开发", "项目", "需求", "前端", "后端" } },
            { "PPT", new List<string> { "PPT", "幻灯片", "演示文稿", "presentation", "slide", "汇报" } },
            { "RESEARCH", new List<string> { "调研", "研究", "分析", "报告", "市场" } },
            { "DATA", new List<string> { "数据", "统计", "图表", "可视化" } }
        };

        // 特别优先处理PPT相关
        if (userQuestion.Contains("PPT", StringComparison.OrdinalIgnoreCase) ||
            userQuestion.Contains("幻灯片") ||
            userQuestion.Contains("演示文稿") ||
            userQuestion.Contains("汇报"))
        {
            var pptAgent = await GetAgentContentAsync("PPT");
            if (pptAgent != null) return pptAgent;
        }

        // 遍历所有关键词匹配
        foreach (var kvp in keywords)
        {
            foreach (var keyword in kvp.Value)
            {
                if (userQuestion.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    var agentContent = await GetAgentContentAsync(kvp.Key);
                    if (agentContent != null) return agentContent;
                }
            }
        }

        // 默认使用CLAUDE agent（如果存在）
        return await GetAgentContentAsync("CLAUDE");
    }

    /// <summary>
    /// 读取知识库中的所有文件内容
    /// </summary>
    public async Task<string> GetKnowledgeContextAsync()
    {
        if (string.IsNullOrEmpty(_knowledgePath) || !Directory.Exists(_knowledgePath))
            return "";

        var sb = new StringBuilder();
        var files = Directory.GetFiles(_knowledgePath, "*.*", SearchOption.AllDirectories)
            .Where(f => _supportedExtensions.Contains(Path.GetExtension(f).ToLower()))
            .Take(10); // 最多读取10个文件

        foreach (var file in files)
        {
            try
            {
                var fileName = Path.GetFileName(file);
                var content = await File.ReadAllTextAsync(file);
                
                // 限制每个文件最多5000字符
                if (content.Length > 5000)
                    content = content.Substring(0, 5000) + "...[文件过长，已截断]";

                sb.AppendLine($"\n=== 文档: {fileName} ===");
                sb.AppendLine(content);
                sb.AppendLine("=== 文档结束 ===\n");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[读取文件 {file} 失败: {ex.Message}]");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 获取知识库文件列表
    /// </summary>
    public List<FileInfo> GetFileList()
    {
        if (string.IsNullOrEmpty(_knowledgePath) || !Directory.Exists(_knowledgePath))
            return new List<FileInfo>();

        var fileInfos = new List<FileInfo>();
        var files = Directory.GetFiles(_knowledgePath, "*.*", SearchOption.AllDirectories)
            .Where(f => _supportedExtensions.Contains(Path.GetExtension(f).ToLower()));

        foreach (var file in files)
        {
            var fi = new System.IO.FileInfo(file);
            fileInfos.Add(new FileInfo
            {
                Name = fi.Name,
                Path = fi.FullName,
                Size = fi.Length,
                LastModified = fi.LastWriteTime
            });
        }

        return fileInfos;
    }

    /// <summary>
    /// 保存对话结果到知识库
    /// </summary>
    public async Task<string> SaveConversationAsync(string userQuestion, string aiAnswer, string format = "md")
    {
        if (string.IsNullOrEmpty(_knowledgePath))
            throw new InvalidOperationException("知识库路径未设置");

        if (!Directory.Exists(_knowledgePath))
            Directory.CreateDirectory(_knowledgePath);

        // 生成文件名：使用时间戳和问题摘要
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var questionSummary = SanitizeFileName(userQuestion.Length > 30 ? userQuestion.Substring(0, 30) : userQuestion);
        var fileName = $"conversation_{timestamp}_{questionSummary}.{format}";
        var filePath = Path.Combine(_knowledgePath, fileName);

        // 根据格式生成内容
        string content = format.ToLower() switch
        {
            "md" => GenerateMarkdownContent(userQuestion, aiAnswer),
            "html" => GenerateHtmlContent(userQuestion, aiAnswer),
            "txt" => GenerateTextContent(userQuestion, aiAnswer),
            _ => GenerateMarkdownContent(userQuestion, aiAnswer)
        };

        await File.WriteAllTextAsync(filePath, content, Encoding.UTF8);
        return filePath;
    }

    /// <summary>
    /// 自动保存有价值的对话（包含代码、长回答等）
    /// </summary>
    public async Task<string?> AutoSaveIfValuableAsync(string userQuestion, string aiAnswer)
    {
        if (string.IsNullOrEmpty(_knowledgePath))
            return null;

        // 判断是否值得保存
        bool isValuable = aiAnswer.Length > 500 || // 长回答
                         aiAnswer.Contains("```") || // 包含代码
                         aiAnswer.Contains("[来源") || // 包含引用
                         userQuestion.Contains("总结") ||
                         userQuestion.Contains("分析") ||
                         userQuestion.Contains("对比");

        if (isValuable)
        {
            return await SaveConversationAsync(userQuestion, aiAnswer, "md");
        }

        return null;
    }

    private string GenerateMarkdownContent(string question, string answer)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# 对话记录");
        sb.AppendLine();
        sb.AppendLine($"**时间**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine("## 问题");
        sb.AppendLine();
        sb.AppendLine(question);
        sb.AppendLine();
        sb.AppendLine("## AI 回答");
        sb.AppendLine();
        sb.AppendLine(answer);
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("*由 Lumina API Demo 自动生成*");
        return sb.ToString();
    }

    private string GenerateHtmlContent(string question, string answer)
    {
        return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <title>对话记录 - {DateTime.Now:yyyy-MM-dd HH:mm:ss}</title>
    <style>
        body {{ font-family: 'Segoe UI', Arial, sans-serif; max-width: 900px; margin: 40px auto; padding: 20px; background: #f5f5f5; }}
        .container {{ background: white; padding: 30px; border-radius: 8px; box-shadow: 0 2px 8px rgba(0,0,0,0.1); }}
        h1 {{ color: #333; border-bottom: 3px solid #667eea; padding-bottom: 10px; }}
        .question {{ background: #e3f2fd; padding: 20px; border-radius: 8px; margin: 20px 0; border-left: 4px solid #2196f3; }}
        .answer {{ background: #f1f8e9; padding: 20px; border-radius: 8px; margin: 20px 0; border-left: 4px solid #4caf50; }}
        .meta {{ color: #666; font-size: 14px; margin-top: 20px; }}
        pre {{ background: #f5f5f5; padding: 15px; border-radius: 4px; overflow-x: auto; }}
    </style>
</head>
<body>
    <div class=""container"">
        <h1>📝 对话记录</h1>
        <div class=""meta"">⏰ 时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}</div>
        
        <h2>❓ 问题</h2>
        <div class=""question"">{System.Net.WebUtility.HtmlEncode(question)}</div>
        
        <h2>💬 AI 回答</h2>
        <div class=""answer"">{System.Net.WebUtility.HtmlEncode(answer).Replace("\n", "<br>")}</div>
        
        <div class=""meta"">✨ 由 Lumina API Demo 自动生成</div>
    </div>
</body>
</html>";
    }

    private string GenerateTextContent(string question, string answer)
    {
        var sb = new StringBuilder();
        sb.AppendLine("========================================");
        sb.AppendLine($"对话记录 - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("========================================");
        sb.AppendLine();
        sb.AppendLine("【问题】");
        sb.AppendLine(question);
        sb.AppendLine();
        sb.AppendLine("【AI回答】");
        sb.AppendLine(answer);
        sb.AppendLine();
        sb.AppendLine("========================================");
        return sb.ToString();
    }

    private string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", fileName.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        return sanitized.Replace(" ", "_");
    }
}

/// <summary>
/// 文件信息
/// </summary>
public class FileInfo
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
}

/// <summary>
/// Agent信息
/// </summary>
public class AgentInfo
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
}
