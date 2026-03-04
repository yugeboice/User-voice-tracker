# Infographic Custom Style 功能实现方案

## 概述

在 Infographic 卡片添加铅笔 icon 入口，用户可输入自定义 prompt。该 prompt 会在两个阶段注入：
1. **内容分析阶段** - 影响 LLM 生成的描述内容
2. **渲染指令阶段** - 影响视觉风格

实现对 **content** 和 **style** 双维度的定制。

---

## 数据流

```
┌──────────────────────────────────────────────────────────────────────────┐
│                              前端 (notebook.html)                         │
│                                                                          │
│  [Infographic 卡片] ──点击铅笔icon──> [弹框输入 custom prompt]              │
│                                              │                           │
│                                              ▼                           │
│                              generateWithCustomStyle()                   │
│                                              │                           │
│                    POST /api/notebook/studio/generate                    │
│                    { notebookId, type: "infographic", customPrompt }     │
└──────────────────────────────────────────────────────────────────────────┘
                                               │
                                               ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                            后端 (StudioApi.cs)                            │
│                                                                          │
│  GenerateRequest(NotebookId, Type, CustomPrompt)                         │
│                              │                                           │
│                              ▼                                           │
│                  GenerateInfographicAsync(notebookId, customPrompt)      │
│                              │                                           │
│                              ▼                                           │
│               NotebookStorage.ExecuteInfographicGenerationAsync(         │
│                   inputFile, outputPath, endpoint, model, customStyle)   │
└──────────────────────────────────────────────────────────────────────────┘
                                               │
                                               ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                      Python Skill (infographic-gen.py)                    │
│                                                                          │
│  命令行: python infographic-gen.py --input ... --output ...              │
│                                   --custom-style "用户输入"               │
│                                                                          │
│  ┌─────────────────────────────────────────────────────────────────┐     │
│  │ Step 1: 内容分析                                                 │     │
│  │                                                                  │     │
│  │ user_message = notebook_content                                  │     │
│  │ if custom_style:                                                 │     │
│  │     user_message += "\n\n用户额外要求:\n" + custom_style          │     │
│  │                                                                  │     │
│  │ → LLM 生成描述时会考虑用户对内容的要求                             │     │
│  └─────────────────────────────────────────────────────────────────┘     │
│                              │                                           │
│                              ▼                                           │
│  ┌─────────────────────────────────────────────────────────────────┐     │
│  │ Step 1b: 领域检测 (不变)                                         │     │
│  │ 描述文本 + style-config.json → domain/variant                    │     │
│  └─────────────────────────────────────────────────────────────────┘     │
│                              │                                           │
│                              ▼                                           │
│  ┌─────────────────────────────────────────────────────────────────┐     │
│  │ Step 2: 渲染指令组装                                             │     │
│  │                                                                  │     │
│  │ auto_rendering = extract_rendering_instructions(domain, variant) │     │
│  │                                                                  │     │
│  │ if custom_style:                                                 │     │
│  │     final_rendering = auto_rendering + """                       │     │
│  │                                                                  │     │
│  │     ## USER CUSTOMIZATION (Higher Priority)                      │     │
│  │     {custom_style}                                               │     │
│  │     """                                                          │     │
│  │ else:                                                            │     │
│  │     final_rendering = auto_rendering                             │     │
│  │                                                                  │     │
│  │ → 图片模型会优先遵循 "Higher Priority" 部分                       │     │
│  └─────────────────────────────────────────────────────────────────┘     │
│                              │                                           │
│                              ▼                                           │
│  ┌─────────────────────────────────────────────────────────────────┐     │
│  │ Step 3: 图片生成                                                 │     │
│  │ full_prompt = description + final_rendering → gpt-image-1-5      │     │
│  └─────────────────────────────────────────────────────────────────┘     │
└──────────────────────────────────────────────────────────────────────────┘
```

---

## 实现步骤

### Step 1: Python Skill 支持 custom-style 参数

**文件**: `skills/infographic-gen/infographic-gen.py`

#### 1.1 添加命令行参数 (~第 600-620 行)

```python
parser.add_argument(
    '--custom-style',
    default=None,
    help='Custom style/content prompt from user (optional). Will be injected into both content analysis and rendering instructions.'
)
```

#### 1.2 内容分析阶段注入 (~第 260-280 行)

找到构建 user_message 的位置，修改为：

```python
# 构建 user message
user_message = f"""以下是需要分析的内容:

{input_content}"""

# 如果有 custom style，追加到 user message
if args.custom_style:
    user_message += f"""

---
## 用户额外要求（请在分析内容时充分考虑以下要求）:
{args.custom_style}
"""
    write_success(f"Custom style injected into content analysis ({len(args.custom_style)} chars)")
```

#### 1.3 渲染指令阶段追加 (~第 420-450 行)

找到 prompt 构建的位置，修改为：

```python
# 获取自动检测的渲染指令
rendering_instructions = extract_rendering_instructions(domain, variant, style_config)

# 如果有 custom style，追加为高优先级覆盖
if args.custom_style:
    rendering_instructions += f"""

## USER CUSTOMIZATION (Higher Priority - Override any conflicts above)
{args.custom_style}
"""
    write_success(f"Custom style appended to rendering instructions")
```

#### 1.4 测试方法

```powershell
# 手动测试（需要先有 input 文件）
python infographic-gen.py `
    --input "C:\path\to\input.txt" `
    --output "C:\path\to\output.png" `
    --custom-style "极简风格，白色背景，只展示三个核心要点"
```

---

### Step 2: 后端传递参数到 Python

**文件**: 
- `notebooks/Backend/NotebookStorage.cs`
- `notebooks/Backend/StudioApi.cs`

#### 2.1 修改 ExecuteInfographicGenerationAsync (NotebookStorage.cs ~第 151-174 行)

```csharp
public async Task<SkillExecutionResult> ExecuteInfographicGenerationAsync(
    string inputFilePath,
    string outputImagePath,
    string llmEndpoint = "http://localhost:4141",
    string llmModel = "claude-sonnet-4",
    string? customStylePrompt = null,  // 新增参数
    int timeoutMs = 300_000)
{
    var skillPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", 
        "skills", "infographic-gen", "infographic-gen.py");
    
    // 构建命令行参数
    var arguments = $"--input \"{inputFilePath}\" --output \"{outputImagePath}\" " +
                   $"--endpoint \"{llmEndpoint}\" --model \"{llmModel}\"";
    
    // 如果有 custom style，添加参数
    if (!string.IsNullOrWhiteSpace(customStylePrompt))
    {
        // 转义双引号，避免命令行解析问题
        var escapedPrompt = customStylePrompt.Replace("\"", "\\\"");
        arguments += $" --custom-style \"{escapedPrompt}\"";
    }
    
    // ... 后续执行逻辑不变
}
```

#### 2.2 修改 GenerateInfographicAsync 调用 (StudioApi.cs ~第 500 行)

找到调用 `ExecuteInfographicGenerationAsync` 的位置，传递 customPrompt：

```csharp
var result = await _notebookStorage.ExecuteInfographicGenerationAsync(
    inputFilePath,
    outputImagePath,
    llmEndpoint,
    llmModel,
    customPrompt  // 传递 custom prompt
);
```

#### 2.3 确认 GenerateRequest 已支持 CustomPrompt

检查 `GenerateRequest` record 定义（应该已有）：

```csharp
public record GenerateRequest(string NotebookId, string Type, string? CustomPrompt = null);
```

#### 2.4 测试方法

```powershell
# 使用 Postman 或 curl 测试
curl -X POST http://localhost:8400/api/notebook/studio/generate `
    -H "Content-Type: application/json" `
    -d '{"notebookId": "xxx", "type": "infographic", "customPrompt": "极简风格"}'
```

---

### Step 3: 前端添加弹框和交互

**文件**: `notebooks/Frontend/notebook.html`

#### 3.1 添加 Modal HTML (~第 1279 行之后，参考 addSourceModal)

```html
<!-- Custom Style Modal -->
<div class="modal" id="customStyleModal">
    <div class="modal-content">
        <div class="modal-header">
            <div class="modal-title">🎨 Customize Infographic Style</div>
        </div>
        <div class="modal-body">
            <div class="form-group">
                <label class="form-label">Custom Style Prompt</label>
                <textarea 
                    class="form-textarea" 
                    id="customStylePrompt" 
                    rows="4"
                    maxlength="500"
                    placeholder="Describe your preferred style or content requirements...&#10;&#10;Examples:&#10;• 极简风格，白色背景，只展示三个核心要点&#10;• Cyberpunk style with neon colors&#10;• Focus on the data comparison in chapter 3"
                ></textarea>
                <div class="form-hint">
                    <span id="customStyleCharCount">0</span>/500 characters
                </div>
            </div>
        </div>
        <div class="modal-footer">
            <button class="btn btn-secondary" onclick="closeCustomStyleModal()">Cancel</button>
            <button class="btn btn-primary" onclick="generateWithCustomStyle()">Generate</button>
        </div>
    </div>
</div>
```

#### 3.2 添加 CSS 样式 (如果需要额外样式)

```css
#customStylePrompt {
    width: 100%;
    min-height: 100px;
    resize: vertical;
}

.form-hint {
    font-size: 12px;
    color: #666;
    margin-top: 4px;
    text-align: right;
}
```

#### 3.3 添加 JavaScript 函数 (~第 2012 行附近)

```javascript
// Custom Style Modal functions
function openCustomStyleModal() {
    document.getElementById('customStyleModal').classList.add('active');
    document.getElementById('customStylePrompt').value = '';
    updateCustomStyleCharCount();
}

function closeCustomStyleModal() {
    document.getElementById('customStyleModal').classList.remove('active');
}

function updateCustomStyleCharCount() {
    const textarea = document.getElementById('customStylePrompt');
    const count = document.getElementById('customStyleCharCount');
    count.textContent = textarea.value.length;
}

// Add event listener for character count
document.addEventListener('DOMContentLoaded', function() {
    const textarea = document.getElementById('customStylePrompt');
    if (textarea) {
        textarea.addEventListener('input', updateCustomStyleCharCount);
    }
});

async function generateWithCustomStyle() {
    const customPrompt = document.getElementById('customStylePrompt').value.trim();
    
    if (!customPrompt) {
        // 如果没有输入，走普通流程
        closeCustomStyleModal();
        generateStudio('infographic');
        return;
    }
    
    closeCustomStyleModal();
    
    // 显示 loading 状态
    showGenerationStatus('infographic', 'Generating with custom style...');
    
    try {
        const response = await fetch('/api/notebook/studio/generate', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ 
                notebookId: currentNotebookId, 
                type: 'infographic',
                customPrompt: customPrompt
            })
        });
        
        if (!response.ok) {
            throw new Error('Generation failed');
        }
        
        const result = await response.json();
        // 处理结果，刷新列表等
        await refreshGenerationList();
        showGenerationStatus('infographic', 'Complete!');
        
    } catch (error) {
        console.error('Custom style generation failed:', error);
        showGenerationStatus('infographic', 'Failed: ' + error.message);
    }
}
```

#### 3.4 测试方法

1. 打开浏览器 DevTools Console
2. 手动调用 `openCustomStyleModal()` 验证弹框显示
3. 输入内容，点击 Generate，观察 Network 请求是否包含 customPrompt

---

### Step 4: 前端添加铅笔 icon 入口

**文件**: `notebooks/Frontend/notebook.html`

#### 4.1 修改 Infographic 卡片 (~第 1170-1173 行)

找到现有的 Infographic 卡片：

```html
<div class="studio-option" onclick="generateStudio('infographic')" data-type="infographic">
    <div class="studio-option-title">📊 Infographic</div>
    <div class="studio-option-desc">Generate a detailed visual infographic explaining your sources (landscape format)</div>
</div>
```

修改为：

```html
<div class="studio-option" data-type="infographic">
    <div class="studio-option-main" onclick="generateStudio('infographic')">
        <div class="studio-option-title">📊 Infographic</div>
        <div class="studio-option-desc">Generate a detailed visual infographic explaining your sources (landscape format)</div>
    </div>
    <div class="studio-option-actions">
        <button class="btn-icon" onclick="event.stopPropagation(); openCustomStyleModal();" title="Customize Style">
            <svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor">
                <path d="M12.146.854a.5.5 0 0 1 .708 0l2.292 2.292a.5.5 0 0 1 0 .708l-9.793 9.793a.5.5 0 0 1-.168.11l-3.5 1.5a.5.5 0 0 1-.65-.65l1.5-3.5a.5.5 0 0 1 .11-.168l9.793-9.793zM11.207 2.5L13.5 4.793 14.793 3.5 12.5 1.207 11.207 2.5zm-1.414 1.414L3 10.707V12h1.293l6.793-6.793-1.5-1.5z"/>
            </svg>
        </button>
    </div>
</div>
```

#### 4.2 添加相关 CSS

```css
.studio-option {
    display: flex;
    align-items: center;
    justify-content: space-between;
}

.studio-option-main {
    flex: 1;
    cursor: pointer;
}

.studio-option-actions {
    display: flex;
    gap: 4px;
    margin-left: 8px;
}

.btn-icon {
    background: transparent;
    border: 1px solid #ddd;
    border-radius: 4px;
    padding: 6px;
    cursor: pointer;
    color: #666;
    transition: all 0.2s;
}

.btn-icon:hover {
    background: #f0f0f0;
    color: #333;
    border-color: #bbb;
}
```

#### 4.3 测试方法

1. 刷新页面，查看 Infographic 卡片是否显示铅笔 icon
2. 点击铅笔 icon，验证弹框打开
3. 点击卡片主体区域，验证走普通生成流程
4. 输入 custom prompt，点击 Generate，验证端到端流程

---

## 测试用例

### 场景 1: 纯风格定制
```
Custom Prompt: "赛博朋克风格，霓虹灯配色，深色背景"
期望: 内容不变，视觉风格变为赛博朋克
```

### 场景 2: 纯内容定制
```
Custom Prompt: "只展示前三个要点，忽略历史背景部分"
期望: 描述只包含 3 个要点，风格使用自动检测
```

### 场景 3: 混合定制
```
Custom Prompt: "极简白色背景，只保留三个核心结论，使用蓝色调"
期望: 内容精简为 3 个结论，风格为极简蓝白配色
```

### 场景 4: 空输入
```
Custom Prompt: "" (空)
期望: 完全走默认流程，与直接点击 Generate 一致
```

---

## 注意事项

1. **参数转义**: `--custom-style` 参数可能包含引号、换行等特殊字符，后端需要正确转义
2. **长度限制**: 前端限制 500 字符，避免 prompt 过长
3. **错误处理**: 各层都需要处理 custom prompt 为空/null 的情况
4. **日志记录**: Python skill 应记录是否使用了 custom style，便于调试

---

## 文件修改清单

| 文件 | 修改内容 |
|------|---------|
| `skills/infographic-gen/infographic-gen.py` | 添加 `--custom-style` 参数，两处注入逻辑 |
| `notebooks/Backend/NotebookStorage.cs` | `ExecuteInfographicGenerationAsync` 添加 `customStylePrompt` 参数 |
| `notebooks/Backend/StudioApi.cs` | 传递 `customPrompt` 到 skill 执行器 |
| `notebooks/Frontend/notebook.html` | 添加 Modal、CSS、JS 函数、修改卡片结构 |
