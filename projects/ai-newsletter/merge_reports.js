/**
 * AI Radar - 报告合并脚本
 * 
 * 将三个平台的报告合并为一个文件，用于上传 NotebookLM
 */

const fs = require('fs');
const path = require('path');

// ==================== 配置 ====================
const CONFIG = {
  REPORTS_DIR: path.join(__dirname, 'reports'),
  // 合并顺序：ChatGPT -> Gemini -> Genspark
  SOURCES: ['chatgpt', 'gemini', 'genspark'],
};

// ==================== 工具函数 ====================

function getDateString() {
  const now = new Date();
  const year = now.getFullYear();
  const month = String(now.getMonth() + 1).padStart(2, '0');
  const day = String(now.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

function getTimestamp() {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')} ${String(now.getHours()).padStart(2, '0')}:${String(now.getMinutes()).padStart(2, '0')}:${String(now.getSeconds()).padStart(2, '0')}`;
}

// ==================== 主逻辑 ====================

function mergeReports(dateStr) {
  console.log('📋 AI Radar - 报告合并\n');
  
  dateStr = dateStr || getDateString();
  console.log(`📅 合并日期：${dateStr}\n`);
  
  const parts = [];
  let foundCount = 0;
  
  // 添加合并报告头部
  parts.push(`# AI Radar 综合日报 - ${dateStr}`);
  parts.push('');
  parts.push(`> 🕐 合并时间：${getTimestamp()}`);
  parts.push('> 📍 来源：ChatGPT + Gemini + Genspark');
  parts.push('');
  parts.push('---');
  parts.push('');
  
  // 依次读取各平台报告
  for (const source of CONFIG.SOURCES) {
    const filename = `report-${source}-${dateStr}.md`;
    const filepath = path.join(CONFIG.REPORTS_DIR, filename);
    
    if (fs.existsSync(filepath)) {
      console.log(`✅ 找到 ${source} 报告：${filename}`);
      const content = fs.readFileSync(filepath, 'utf-8');
      
      // 添加分隔标记
      parts.push(`\n\n${'='.repeat(60)}`);
      parts.push(`## 📰 来源：${source.toUpperCase()}`);
      parts.push(`${'='.repeat(60)}\n`);
      parts.push(content);
      
      foundCount++;
    } else {
      console.log(`⚠️  未找到 ${source} 报告：${filename}`);
    }
  }
  
  if (foundCount === 0) {
    console.error('\n❌ 没有找到任何报告文件！');
    return null;
  }
  
  // 保存合并文件
  const outputFilename = `combined-${dateStr}.md`;
  const outputPath = path.join(CONFIG.REPORTS_DIR, outputFilename);
  
  fs.writeFileSync(outputPath, parts.join('\n'), 'utf-8');
  
  console.log(`\n✅ 合并完成！共 ${foundCount} 个来源`);
  console.log(`📄 输出文件：${outputPath}`);
  
  return outputPath;
}

// ==================== 入口 ====================

// 支持命令行参数指定日期
const dateArg = process.argv[2];
const result = mergeReports(dateArg);

if (!result) {
  process.exit(1);
}
