/**
 * AI Radar - 智能摘要生成器 v2
 * 
 * 读取合并后的 md 文件，生成精炼的分类摘要：
 * 1. 模型动态 - Top Facts + Insights
 * 2. AI 应用与产品 - Top Facts + Insights
 * 3. 大厂动向 - Top Facts + Insights
 * 
 * 优先展示医疗健康相关内容
 * 输出麦肯锡风格 HTML 页面
 */

const fs = require('fs');
const path = require('path');

// ==================== 配置 ====================
const CONFIG = {
  REPORTS_DIR: path.join(__dirname, 'reports'),
  OUTPUT_DIR: path.join(__dirname, 'reports'),
  TOP_FACTS_COUNT: 5,  // 每个分类的关键必读数量
};

// 分类配置
const CATEGORIES = {
  models: {
    title: '🧠 模型动态',
    subtitle: 'Model Updates & Benchmarks',
    keywords: ['模型', 'model', 'GPT', 'Claude', 'Gemini', 'ERNIE', 'GLM', 'Llama', 'benchmark', 
               '评测', '榜单', 'leaderboard', '参数', 'token', '训练', 'training', '推理', 
               'inference', '微调', 'fine-tune', 'RLHF', '对齐', 'alignment', 'o1', 'o3',
               'Qwen', '通义', 'DeepSeek', '文心', 'LMArena', '多模态', 'multimodal', 
               'shut down', 'lifecycle', '版本', 'version', 'preview', 'API'],
    healthcare: ['医疗模型', 'clinical model', 'medical AI', 'healthcare model', 'PANDA', '医学大模型',
                 'BioGPT', 'Med-', 'Clinical-', '诊断模型', 'Claude for Healthcare']
  },
  applications: {
    title: '🚀 AI 应用与产品',
    subtitle: 'Tools, Products & Applications', 
    keywords: ['产品', 'product', '应用', 'app', '工具', 'tool', 'Agent', '助手', 'assistant',
               'API', 'SDK', '平台', 'platform', '发布', 'launch', '上线', '更新', 'update',
               'feature', '功能', 'Copilot', '插件', 'plugin', 'MCP', 'A2A', '协议', 'protocol',
               '开发者', 'developer', 'NotebookLM', 'Cursor', 'Devin', 'Codex', 'CLI',
               'SKILL', 'skill', 'toml', '元数据', 'metadata'],
    healthcare: ['医疗助手', '健康', 'health', '诊断', 'diagnosis', '临床', 'clinical', 
                 '医生', 'doctor', '患者', 'patient', '问诊', '分诊', '药物', 'drug',
                 'AI 医', '数字医疗', 'digital health', 'telemedicine', '远程医疗',
                 'AI京医', '蚂蚁阿福', 'ChatGPT Health', '医疗Agent', '医院', '医疗',
                 'Healthcare', 'hospital', '健客', '健康险', '理赔']
  },
  industry: {
    title: '🏢 大厂动向',
    subtitle: 'Industry Moves & Strategic Updates',
    keywords: ['OpenAI', 'Google', 'Microsoft', 'Anthropic', 'Meta', 'Apple', 'Amazon', 'NVIDIA',
               '阿里', '腾讯', '百度', '字节', '华为', '京东', '蚂蚁', '商汤', '科大讯飞',
               '融资', 'funding', '投资', 'investment', '收购', 'acquisition', '合作', 
               'partnership', '战略', 'strategy', 'CEO', '市值', '股价', '上市', 'IPO',
               '估值', 'valuation', '营收', 'revenue', 'Sam Altman', 'Satya', 'Sundar',
               '港交所', 'HKEX', '申请', '披露', 'Tencent', '方舟'],
    healthcare: ['阿里健康', '京东健康', '腾讯医疗', 'Tencent Healthcare', '平安好医生', '微医', 
                 'HIPAA', 'FDA', 'CE', '医疗合规', '健康险', 'Apple Health', 'Google Health',
                 '莲池', '镁信健康', 'MedTrust', '医院集团']
  }
};

// ==================== 提取函数 ====================

function extractFactsAndInsights(content) {
  const facts = [];
  const insights = [];
  const lines = content.split('\n');
  
  let inInsightBlock = false;
  
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i].trim();
    
    if (!line || line.startsWith('#') || line.startsWith('>') || 
        line.startsWith('---') || line.startsWith('===') ||
        line.match(/^[\d.)]+\s*(标题|Core Facts|今日必读)/i)) {
      inInsightBlock = false;
      continue;
    }
    
    // 检测 Insights 块
    if (line.includes('Insights') || line.startsWith('（推断）') || line.startsWith('推断')) {
      inInsightBlock = true;
      continue;
    }
    
    // 提取推断/洞察内容
    if (inInsightBlock || line.startsWith('（推断）')) {
      const insightText = line.replace(/^（推断）\s*/, '').trim();
      if (insightText.length > 20) {
        insights.push({
          content: insightText,
          source: '',
          isInsight: true
        });
      }
      continue;
    }
    
    // 检测 Signal 标题
    if (line.match(/^Signal\s*\d+\s*[—–-]/i)) {
      const text = line.replace(/^Signal\s*\d+\s*[—–-]\s*/i, '').trim();
      if (text.length > 20) {
        facts.push({
          category: 'Signal',
          content: text,
          source: '',
          importance: 10  // Signal 优先级最高
        });
      }
      continue;
    }
    
    // 制表符分隔的表格行
    if (line.includes('\t')) {
      const cells = line.split('\t').map(c => c.trim()).filter(c => c);
      if (cells[0]?.toLowerCase().includes('category') || cells[0]?.includes('分类')) {
        continue;
      }
      if (cells.length >= 2 && cells[1].length > 20) {
        facts.push({
          category: cells[0] || '',
          content: cells[1] || '',
          source: cells[2] || '',
          importance: 5
        });
        continue;
      }
    }
    
    // Markdown 表格
    if (line.includes('|') && !line.match(/^\|[-:]+\|/)) {
      const cells = line.split('|').map(c => c.trim()).filter(c => c);
      if (cells[0]?.toLowerCase().includes('category') || cells[0]?.includes('分类')) {
        continue;
      }
      if (cells.length >= 2 && cells[1].length > 20) {
        facts.push({
          category: cells[0] || '',
          content: cells[1] || '',
          source: cells[2] || '',
          importance: 5
        });
        continue;
      }
    }
  }
  
  // 去重
  const seenFacts = new Set();
  const seenInsights = new Set();
  
  const uniqueFacts = facts.filter(f => {
    const key = f.content.slice(0, 60);
    if (seenFacts.has(key)) return false;
    seenFacts.add(key);
    return true;
  });
  
  const uniqueInsights = insights.filter(f => {
    const key = f.content.slice(0, 60);
    if (seenInsights.has(key)) return false;
    seenInsights.add(key);
    return true;
  });
  
  return { facts: uniqueFacts, insights: uniqueInsights };
}

function categorizeFact(fact, categoryConfig) {
  const text = `${fact.category} ${fact.content} ${fact.source}`.toLowerCase();
  
  let isHealthcare = false;
  for (const keyword of categoryConfig.healthcare) {
    if (text.includes(keyword.toLowerCase())) {
      isHealthcare = true;
      break;
    }
  }
  
  let matches = false;
  let matchCount = 0;
  for (const keyword of categoryConfig.keywords) {
    if (text.includes(keyword.toLowerCase())) {
      matches = true;
      matchCount++;
    }
  }
  
  return { matches, isHealthcare, matchCount };
}

function processContent(mdContent) {
  const { facts, insights } = extractFactsAndInsights(mdContent);
  
  const result = {
    models: { topFacts: [], insights: [] },
    applications: { topFacts: [], insights: [] },
    industry: { topFacts: [], insights: [] }
  };
  
  const usedFacts = new Set();
  const usedInsights = new Set();
  
  // 分类 Facts
  for (const fact of facts) {
    if (usedFacts.has(fact.content)) continue;
    
    for (const [catKey, catConfig] of Object.entries(CATEGORIES)) {
      const { matches, isHealthcare, matchCount } = categorizeFact(fact, catConfig);
      
      if (matches) {
        fact.isHealthcare = isHealthcare;
        fact.score = (fact.importance || 0) + matchCount + (isHealthcare ? 5 : 0);
        result[catKey].topFacts.push(fact);
        usedFacts.add(fact.content);
        break;
      }
    }
  }
  
  // 分类 Insights
  for (const insight of insights) {
    if (usedInsights.has(insight.content)) continue;
    
    for (const [catKey, catConfig] of Object.entries(CATEGORIES)) {
      const { matches, isHealthcare } = categorizeFact(insight, catConfig);
      
      if (matches) {
        insight.isHealthcare = isHealthcare;
        result[catKey].insights.push(insight);
        usedInsights.add(insight.content);
        break;
      }
    }
  }
  
  // 排序并截取 Top Facts
  for (const catKey of Object.keys(result)) {
    // 医疗优先 + 得分排序
    result[catKey].topFacts.sort((a, b) => {
      if (a.isHealthcare && !b.isHealthcare) return -1;
      if (!a.isHealthcare && b.isHealthcare) return 1;
      return (b.score || 0) - (a.score || 0);
    });
    result[catKey].topFacts = result[catKey].topFacts.slice(0, CONFIG.TOP_FACTS_COUNT);
    
    // Insights 也按医疗优先排序
    result[catKey].insights.sort((a, b) => {
      if (a.isHealthcare && !b.isHealthcare) return -1;
      if (!a.isHealthcare && b.isHealthcare) return 1;
      return 0;
    });
    result[catKey].insights = result[catKey].insights.slice(0, 3);
  }
  
  return result;
}

// ==================== HTML 生成 ====================

function generateHTML(data, dateStr) {
  const formatTopFacts = (facts) => {
    if (facts.length === 0) return '<p class="empty">暂无关键事实</p>';
    
    return facts.map((f, idx) => {
      const badge = f.isHealthcare ? '<span class="badge healthcare">🏥 医疗</span>' : '';
      const source = f.source ? `<span class="source">${f.source}</span>` : '';
      return `
        <div class="fact-item ${f.isHealthcare ? 'healthcare-item' : ''}">
          <span class="fact-num">${idx + 1}</span>
          <div class="fact-body">
            ${badge}
            <p class="fact-content">${f.content}</p>
            ${source}
          </div>
        </div>
      `;
    }).join('');
  };
  
  const formatInsights = (insights) => {
    if (insights.length === 0) return '<p class="empty">暂无洞察</p>';
    
    return insights.map(f => {
      const badge = f.isHealthcare ? '<span class="badge healthcare">🏥</span>' : '';
      return `
        <div class="insight-item ${f.isHealthcare ? 'healthcare-insight' : ''}">
          <span class="insight-icon">💡</span>
          <p>${badge} ${f.content}</p>
        </div>
      `;
    }).join('');
  };

  const html = `<!DOCTYPE html>
<html lang="zh-CN">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>AI Radar 每日简报 - ${dateStr}</title>
  <style>
    :root {
      --primary: #0066CC;
      --primary-light: #E8F4FD;
      --healthcare: #00875A;
      --healthcare-light: #E3FCEF;
      --insight-bg: #FFF8E6;
      --insight-border: #FFD666;
      --text-primary: #1A1A1A;
      --text-secondary: #666666;
      --border: #E5E5E5;
      --bg-gray: #F8F9FA;
      --white: #FFFFFF;
    }
    
    * { margin: 0; padding: 0; box-sizing: border-box; }
    
    body {
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', 'PingFang SC', 'Hiragino Sans GB', 'Microsoft YaHei', sans-serif;
      background: var(--bg-gray);
      color: var(--text-primary);
      line-height: 1.7;
    }
    
    .container {
      max-width: 900px;
      margin: 0 auto;
      padding: 48px 24px;
    }
    
    /* Header */
    .header {
      text-align: center;
      margin-bottom: 48px;
    }
    
    .header h1 {
      font-size: 28px;
      font-weight: 600;
      color: var(--text-primary);
      margin-bottom: 8px;
    }
    
    .header .date {
      font-size: 15px;
      color: var(--text-secondary);
    }
    
    .header .tagline {
      font-size: 13px;
      color: var(--primary);
      margin-top: 12px;
      font-weight: 500;
    }
    
    /* Section */
    .section {
      background: var(--white);
      border-radius: 12px;
      padding: 32px;
      margin-bottom: 24px;
      border: 1px solid var(--border);
    }
    
    .section-header {
      display: flex;
      align-items: baseline;
      gap: 12px;
      margin-bottom: 24px;
    }
    
    .section-header h2 {
      font-size: 18px;
      font-weight: 600;
    }
    
    .section-header .subtitle {
      font-size: 13px;
      color: var(--text-secondary);
    }
    
    /* Subsection */
    .subsection {
      margin-bottom: 24px;
    }
    
    .subsection:last-child {
      margin-bottom: 0;
    }
    
    .subsection-title {
      font-size: 13px;
      font-weight: 600;
      color: var(--text-secondary);
      text-transform: uppercase;
      letter-spacing: 0.5px;
      margin-bottom: 16px;
      padding-bottom: 8px;
      border-bottom: 1px solid var(--border);
    }
    
    /* Fact Items */
    .fact-item {
      display: flex;
      gap: 12px;
      padding: 14px 0;
      border-bottom: 1px solid var(--border);
    }
    
    .fact-item:last-child {
      border-bottom: none;
    }
    
    .fact-num {
      flex-shrink: 0;
      width: 24px;
      height: 24px;
      background: var(--primary-light);
      color: var(--primary);
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 12px;
      font-weight: 600;
    }
    
    .healthcare-item .fact-num {
      background: var(--healthcare-light);
      color: var(--healthcare);
    }
    
    .fact-body {
      flex: 1;
    }
    
    .fact-content {
      font-size: 14px;
      line-height: 1.7;
      color: var(--text-primary);
    }
    
    .badge {
      display: inline-block;
      font-size: 11px;
      padding: 2px 6px;
      border-radius: 3px;
      margin-bottom: 6px;
      font-weight: 500;
    }
    
    .badge.healthcare {
      background: var(--healthcare);
      color: white;
    }
    
    .source {
      display: block;
      font-size: 12px;
      color: var(--text-secondary);
      margin-top: 6px;
    }
    
    /* Insight Items */
    .insight-item {
      display: flex;
      gap: 10px;
      padding: 12px 14px;
      background: var(--insight-bg);
      border-left: 3px solid var(--insight-border);
      border-radius: 0 6px 6px 0;
      margin-bottom: 10px;
    }
    
    .insight-item:last-child {
      margin-bottom: 0;
    }
    
    .healthcare-insight {
      background: var(--healthcare-light);
      border-left-color: var(--healthcare);
    }
    
    .insight-icon {
      flex-shrink: 0;
      font-size: 14px;
    }
    
    .insight-item p {
      font-size: 13px;
      line-height: 1.6;
      color: var(--text-primary);
    }
    
    .insight-item .badge {
      margin-right: 4px;
      padding: 1px 4px;
      font-size: 10px;
    }
    
    /* Empty State */
    .empty {
      color: var(--text-secondary);
      font-size: 13px;
      padding: 12px 0;
    }
    
    /* Footer */
    .footer {
      text-align: center;
      padding: 32px 0;
      color: var(--text-secondary);
      font-size: 12px;
    }
    
    /* Print */
    @media print {
      body { background: white; }
      .section { break-inside: avoid; box-shadow: none; }
    }
    
    @media (max-width: 640px) {
      .container { padding: 24px 16px; }
      .section { padding: 20px; }
      .header h1 { font-size: 22px; }
    }
  </style>
</head>
<body>
  <div class="container">
    <header class="header">
      <h1>AI Radar 每日简报</h1>
      <p class="date">${dateStr}</p>
      <p class="tagline">聚焦 AI 医疗健康 · 精选 Top Facts & Insights</p>
    </header>
    
    <section class="section">
      <div class="section-header">
        <h2>${CATEGORIES.models.title}</h2>
        <span class="subtitle">${CATEGORIES.models.subtitle}</span>
      </div>
      
      <div class="subsection">
        <div class="subsection-title">📌 关键必读 Top Facts</div>
        ${formatTopFacts(data.models.topFacts)}
      </div>
      
      <div class="subsection">
        <div class="subsection-title">💡 洞察 Insights</div>
        ${formatInsights(data.models.insights)}
      </div>
    </section>
    
    <section class="section">
      <div class="section-header">
        <h2>${CATEGORIES.applications.title}</h2>
        <span class="subtitle">${CATEGORIES.applications.subtitle}</span>
      </div>
      
      <div class="subsection">
        <div class="subsection-title">📌 关键必读 Top Facts</div>
        ${formatTopFacts(data.applications.topFacts)}
      </div>
      
      <div class="subsection">
        <div class="subsection-title">💡 洞察 Insights</div>
        ${formatInsights(data.applications.insights)}
      </div>
    </section>
    
    <section class="section">
      <div class="section-header">
        <h2>${CATEGORIES.industry.title}</h2>
        <span class="subtitle">${CATEGORIES.industry.subtitle}</span>
      </div>
      
      <div class="subsection">
        <div class="subsection-title">📌 关键必读 Top Facts</div>
        ${formatTopFacts(data.industry.topFacts)}
      </div>
      
      <div class="subsection">
        <div class="subsection-title">💡 洞察 Insights</div>
        ${formatInsights(data.industry.insights)}
      </div>
    </section>
    
    <footer class="footer">
      <p>由 AI Radar 自动生成 · ${new Date().toLocaleString('zh-CN')}</p>
      <p>数据来源：ChatGPT · Gemini · Genspark</p>
    </footer>
  </div>
</body>
</html>`;

  return html;
}

// ==================== Markdown 生成 ====================

function generateMarkdownSummary(data, dateStr) {
  const formatFacts = (facts) => {
    if (facts.length === 0) return '_暂无_\n';
    return facts.map((f, idx) => {
      const prefix = f.isHealthcare ? '🏥 ' : '';
      const source = f.source ? ` _(${f.source})_` : '';
      return `${idx + 1}. ${prefix}${f.content}${source}`;
    }).join('\n') + '\n';
  };
  
  const formatInsights = (insights) => {
    if (insights.length === 0) return '_暂无_\n';
    return insights.map(f => {
      const prefix = f.isHealthcare ? '🏥 ' : '';
      return `💡 ${prefix}${f.content}`;
    }).join('\n') + '\n';
  };

  return `# AI Radar 每日简报 - ${dateStr}

> 聚焦 AI 医疗健康 · 精选 Top Facts & Insights

---

## 🧠 模型动态

### 📌 关键必读
${formatFacts(data.models.topFacts)}
### 💡 洞察
${formatInsights(data.models.insights)}

---

## 🚀 AI 应用与产品

### 📌 关键必读
${formatFacts(data.applications.topFacts)}
### 💡 洞察
${formatInsights(data.applications.insights)}

---

## 🏢 大厂动向

### 📌 关键必读
${formatFacts(data.industry.topFacts)}
### 💡 洞察
${formatInsights(data.industry.insights)}

---

_由 AI Radar 自动生成 · ${new Date().toLocaleString('zh-CN')}_
_数据来源：ChatGPT · Gemini · Genspark_
`;
}

// ==================== 主函数 ====================

async function main() {
  console.log('📊 AI Radar - 智能摘要生成器 v2\n');
  
  const dateStr = getDateString();
  const combinedFile = path.join(CONFIG.REPORTS_DIR, `combined-${dateStr}.md`);
  
  if (!fs.existsSync(combinedFile)) {
    console.error(`❌ 找不到合并报告：${combinedFile}`);
    process.exit(1);
  }
  
  console.log(`📄 读取合并报告：combined-${dateStr}.md`);
  const mdContent = fs.readFileSync(combinedFile, 'utf-8');
  console.log(`   文件大小：${(mdContent.length / 1024).toFixed(1)} KB`);
  
  console.log('🔍 分析内容，提取 Top Facts & Insights...');
  const data = processContent(mdContent);
  
  // 统计
  console.log(`   模型动态：${data.models.topFacts.length} 条 Facts，${data.models.insights.length} 条 Insights`);
  console.log(`   应用产品：${data.applications.topFacts.length} 条 Facts，${data.applications.insights.length} 条 Insights`);
  console.log(`   大厂动向：${data.industry.topFacts.length} 条 Facts，${data.industry.insights.length} 条 Insights`);
  
  // 生成 HTML
  console.log('🎨 生成麦肯锡风格 HTML...');
  const html = generateHTML(data, dateStr);
  
  const outputFile = path.join(CONFIG.OUTPUT_DIR, `summary-${dateStr}.html`);
  fs.writeFileSync(outputFile, html, 'utf-8');
  
  console.log(`\n✅ 摘要已生成：${outputFile}`);
  console.log(`   文件大小：${(html.length / 1024).toFixed(1)} KB`);
  
  // Markdown 版本
  const mdSummary = generateMarkdownSummary(data, dateStr);
  const mdOutputFile = path.join(CONFIG.OUTPUT_DIR, `summary-${dateStr}.md`);
  fs.writeFileSync(mdOutputFile, mdSummary, 'utf-8');
  console.log(`   Markdown 版本：summary-${dateStr}.md`);
  
  return outputFile;
}

function getDateString() {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
}

main().catch(err => {
  console.error('❌ 生成失败:', err.message);
  process.exit(1);
});
