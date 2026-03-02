using System.Diagnostics;
using System.Text;

namespace MinimalApiCall;

/// <summary>
/// 文字转语音服务 - 使用Edge TTS
/// </summary>
public class TextToSpeechService
{
    private readonly string _outputPath;
    private readonly Dictionary<string, VoiceProfile> _voiceProfiles;

    public TextToSpeechService(string outputPath)
    {
        _outputPath = outputPath;
        if (!Directory.Exists(_outputPath))
        {
            Directory.CreateDirectory(_outputPath);
        }

        // 定义声音配置
        _voiceProfiles = new Dictionary<string, VoiceProfile>
        {
            { "甜美女生", new VoiceProfile 
                { 
                    Name = "zh-CN-XiaoxiaoNeural", 
                    DisplayName = "晓晓 - 甜美温柔",
                    Gender = "女",
                    Description = "年轻女性，声音甜美温柔，适合讲故事和聊天",
                    Rate = "+0%",
                    Pitch = "+0Hz"
                } 
            },
            { "活力女生", new VoiceProfile 
                { 
                    Name = "zh-CN-XiaoyiNeural", 
                    DisplayName = "晓依 - 活泼开朗",
                    Gender = "女",
                    Description = "充满活力的女性声音，语气活泼",
                    Rate = "+10%",
                    Pitch = "+5Hz"
                } 
            },
            { "知性女声", new VoiceProfile 
                { 
                    Name = "zh-CN-XiaochenNeural", 
                    DisplayName = "晓辰 - 知性优雅",
                    Gender = "女",
                    Description = "成熟女性声音，知性优雅",
                    Rate = "-5%",
                    Pitch = "-2Hz"
                } 
            },
            { "磁性男声", new VoiceProfile 
                { 
                    Name = "zh-CN-YunxiNeural", 
                    DisplayName = "云希 - 磁性温暖",
                    Gender = "男",
                    Description = "年轻男性，声音温暖有磁性",
                    Rate = "+0%",
                    Pitch = "-5Hz"
                } 
            },
            { "稳重男声", new VoiceProfile 
                { 
                    Name = "zh-CN-YunjianNeural", 
                    DisplayName = "云健 - 沉稳大气",
                    Gender = "男",
                    Description = "成熟男性，声音沉稳有力",
                    Rate = "-5%",
                    Pitch = "-10Hz"
                } 
            },
            { "少年音", new VoiceProfile 
                { 
                    Name = "zh-CN-YunyangNeural", 
                    DisplayName = "云扬 - 青春阳光",
                    Gender = "男",
                    Description = "少年声音，阳光活力",
                    Rate = "+15%",
                    Pitch = "+10Hz"
                } 
            }
        };
    }

    /// <summary>
    /// 文字转语音（使用Python edge-tts）
    /// </summary>
    public async Task<string> GenerateSpeechAsync(string text, string voiceStyle = "甜美女生")
    {
        try
        {
            if (!_voiceProfiles.ContainsKey(voiceStyle))
            {
                voiceStyle = "甜美女生";
            }

            var profile = _voiceProfiles[voiceStyle];
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"tts_{timestamp}.mp3";
            var outputFile = Path.Combine(_outputPath, fileName);

            // 检查edge-tts是否安装
            var checkResult = await RunCommandAsync("edge-tts", "--version");
            if (!checkResult.success)
            {
                // 尝试使用内置TTS方案
                return await GenerateWithWebSpeechAPI(text, voiceStyle);
            }

            // 使用edge-tts生成语音
            var command = $"edge-tts --voice {profile.Name} --rate={profile.Rate} --pitch={profile.Pitch} --text \"{EscapeText(text)}\" --write-media \"{outputFile}\"";
            var result = await RunCommandAsync("powershell", $"-Command \"{command}\"");

            if (result.success && File.Exists(outputFile))
            {
                return fileName;
            }

            throw new Exception($"TTS生成失败: {result.error}");
        }
        catch (Exception ex)
        {
            throw new Exception($"语音生成错误: {ex.Message}");
        }
    }

    /// <summary>
    /// 使用浏览器内置语音（降级方案）
    /// </summary>
    private async Task<string> GenerateWithWebSpeechAPI(string text, string voiceStyle)
    {
        // 返回特殊标记，前端使用Web Speech API
        await Task.CompletedTask;
        return $"web_speech:{voiceStyle}";
    }

    /// <summary>
    /// 获取所有可用的声音配置
    /// </summary>
    public List<VoiceProfile> GetAvailableVoices()
    {
        return _voiceProfiles.Values.ToList();
    }

    /// <summary>
    /// 获取声音配置
    /// </summary>
    public VoiceProfile? GetVoiceProfile(string voiceStyle)
    {
        return _voiceProfiles.TryGetValue(voiceStyle, out var profile) ? profile : null;
    }

    /// <summary>
    /// 运行命令行工具
    /// </summary>
    private async Task<(bool success, string output, string error)> RunCommandAsync(string command, string arguments)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            return (process.ExitCode == 0, output, error);
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }

    /// <summary>
    /// 转义文本中的特殊字符
    /// </summary>
    private string EscapeText(string text)
    {
        return text.Replace("\"", "\\\"")
                   .Replace("\n", " ")
                   .Replace("\r", " ")
                   .Replace("  ", " ");
    }
}

/// <summary>
/// 声音配置
/// </summary>
public class VoiceProfile
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Gender { get; set; } = "";
    public string Description { get; set; } = "";
    public string Rate { get; set; } = "+0%";
    public string Pitch { get; set; } = "+0Hz";
}
