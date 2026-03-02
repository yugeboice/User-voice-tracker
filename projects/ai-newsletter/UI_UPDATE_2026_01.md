# NotebookLM UI 更新总结 (2026-01)

## 概述
通过调试工具扫描发现，NotebookLM 的"添加来源"按钮在新版本中的 UI 选择器已更新。

## 关键发现

### "添加来源"按钮信息
- **文本**: `add 添加来源`
- **aria-label**: `添加来源` ✅（最可靠的选择器）
- **CSS类**: `mdc-button mat-mdc-button-base mat-mdc-tooltip-trigger add-source-button ...`
- **UI框架**: Material Design 3 (mdc-button)

### 推荐选择器（优先级）

1. **主选择器（最可靠）** ✅
   ```javascript
   '[aria-label="添加来源"]'
   ```

2. **备用选择器**
   ```javascript
   'button:has-text("add 添加来源")'
   'button[class*="add-source-button"]'
   'button[class*="mdc-button"][aria-label*="添加来源"]'
   ```

## 页面 URL 变化

点击"添加来源"后，URL 会自动变为：
```
https://notebooklm.google.com/notebook/{notebook-id}?addSource=true
```

这说明页面已进入上传模式。

## 页面上的其他相关按钮

| 序号 | 按钮 | aria-label | 用途 |
|------|------|-----------|------|
| 0 | dock_to_right | 收起来源面板 | 切换来源面板 |
| 1 | add 添加来源 | 添加来源 | **上传文件入口** |
| 4 | 分享 | 分享笔记本 | 分享 |
| 5 | 设置 | 设置 | 配置 |

## 脚本更新

已在 `upload_notebooklm.js` 中更新选择器：

```javascript
const addSourceSelectors = [
  // 新 UI 选择器（优先级最高）
  '[aria-label="添加来源"]',
  'button[aria-label="添加来源"]',
  'button:has-text("add 添加来源")',
  'button[class*="add-source-button"]',
  // ... 备用选择器
];
```

## 调试工具产出

- `debug-notebooklm-ui-report.txt` - 详细的 UI 信息报告
- `debug-notebooklm-ui-screenshot.png` - NotebookLM 添加来源页面的截图

## 后续步骤

1. ✅ 更新了"添加来源"按钮选择器
2. ⚠️ 需要测试文件上传对话框选择器（可能需要进一步调试）
3. ⚠️ 需要验证上传成功后的来源列表识别

## 测试命令

```bash
# 快速测试新选择器
node upload_notebooklm.js

# 如果仍有问题，重新运行调试工具
node debug_notebooklm_ui.js
```
