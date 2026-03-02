/**
 * AI Radar - 每日自动化编排器
 * 
 * 一键运行所有抓取 + 合并 + NotebookLM 上传流程
 * 
 * 流程：
 * 1. 抓取 ChatGPT Newsletter
 * 2. 抓取 Gemini Newsletter  
 * 3. 抓取 Genspark Newsletter
 * 4. 合并三份报告
 * 5. 上传到 NotebookLM 并生成音频/演示文稿
 * 
 * 用法：
 *   node run_daily.js           # 运行完整流程
 *   node run_daily.js --skip-scrape  # 跳过抓取，仅合并+上传
 *   node run_daily.js --scrape-only  # 仅抓取，不上传
 */

const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');

// ==================== 配置 ====================
const CONFIG = {
  LOGS_DIR: path.join(__dirname, 'logs'),
  REPORTS_DIR: path.join(__dirname, 'reports'),
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

// 日志类
class Logger {
  constructor(dateStr) {
    if (!fs.existsSync(CONFIG.LOGS_DIR)) {
      fs.mkdirSync(CONFIG.LOGS_DIR, { recursive: true });
    }
    this.logFile = path.join(CONFIG.LOGS_DIR, `STATUS-${dateStr}.log`);
    this.logs = [];
    this.startTime = Date.now();
  }

  log(level, message) {
    const timestamp = getTimestamp();
    const logLine = `[${timestamp}] [${level}] ${message}`;
    console.log(logLine);
    this.logs.push(logLine);
  }

  info(message) { this.log('INFO', message); }
  success(message) { this.log('SUCCESS', message); }
  warn(message) { this.log('WARN', message); }
  error(message) { this.log('ERROR', message); }

  save() {
    const duration = ((Date.now() - this.startTime) / 1000 / 60).toFixed(2);
    this.logs.push('');
    this.logs.push(`========== 执行摘要 ==========`);
    this.logs.push(`总耗时: ${duration} 分钟`);
    this.logs.push(`日志文件: ${this.logFile}`);
    
    fs.writeFileSync(this.logFile, this.logs.join('\n'), 'utf-8');
    console.log(`\n📝 日志已保存: ${this.logFile}`);
  }
}

// Node.js 可执行文件路径（兼容 Shortcuts 环境）
const NODE_PATH = process.execPath || '/opt/homebrew/bin/node';

// 运行子脚本
function runScript(scriptPath, args = []) {
  return new Promise((resolve, reject) => {
    const scriptName = path.basename(scriptPath);
    console.log(`\n${'─'.repeat(50)}`);
    console.log(`▶ 运行: ${scriptName}`);
    console.log(`${'─'.repeat(50)}\n`);

    const child = spawn(NODE_PATH, [scriptPath, ...args], {
      cwd: __dirname,
      stdio: 'inherit', // 继承父进程的 stdio，实时显示输出
      env: { ...process.env, FORCE_COLOR: '1' },
    });

    child.on('close', (code) => {
      if (code === 0) {
        resolve({ success: true, code });
      } else {
        resolve({ success: false, code });
      }
    });

    child.on('error', (err) => {
      reject(err);
    });
  });
}

// ==================== 主流程 ====================

async function runDaily() {
  const dateStr = getDateString();
  const logger = new Logger(dateStr);
  
  console.log('\n');
  console.log('╔════════════════════════════════════════════════════════════╗');
  console.log('║                                                            ║');
  console.log('║           🚀 AI Radar - 每日自动化情报系统                   ║');
  console.log('║                                                            ║');
  console.log('╚════════════════════════════════════════════════════════════╝');
  console.log('');
  
  logger.info(`开始执行每日流程 - ${dateStr}`);
  
  // 解析命令行参数
  const skipScrape = process.argv.includes('--skip-scrape');
  const scrapeOnly = process.argv.includes('--scrape-only');
  
  if (skipScrape) {
    logger.info('模式: 跳过抓取，仅合并+上传');
  } else if (scrapeOnly) {
    logger.info('模式: 仅抓取，不上传');
  } else {
    logger.info('模式: 完整流程');
  }
  
  const results = {
    chatgpt: null,
    gemini: null,
    genspark: null,
    merge: null,
    notebooklm: null,
  };
  
  try {
    // ==================== Step 1-3: 抓取 ====================
    if (!skipScrape) {
      console.log('\n');
      console.log('┌────────────────────────────────────────────────────────────┐');
      console.log('│  📡 Step 1-3: 抓取各平台 Newsletter                         │');
      console.log('└────────────────────────────────────────────────────────────┘');
      
      // ChatGPT
      logger.info('Step 1: 开始抓取 ChatGPT...');
      try {
        const result = await runScript(path.join(__dirname, 'scrape_chatgpt_chrome.js'));
        results.chatgpt = result.success;
        if (result.success) {
          logger.success('ChatGPT 抓取完成 ✓');
        } else {
          logger.error(`ChatGPT 抓取失败 (exit code: ${result.code})`);
        }
      } catch (err) {
        results.chatgpt = false;
        logger.error(`ChatGPT 抓取异常: ${err.message}`);
      }
      
      // Gemini
      logger.info('Step 2: 开始抓取 Gemini...');
      try {
        const result = await runScript(path.join(__dirname, 'scrape_gemini_debug.js'));
        results.gemini = result.success;
        if (result.success) {
          logger.success('Gemini 抓取完成 ✓');
        } else {
          logger.error(`Gemini 抓取失败 (exit code: ${result.code})`);
        }
      } catch (err) {
        results.gemini = false;
        logger.error(`Gemini 抓取异常: ${err.message}`);
      }
      
      // Genspark
      logger.info('Step 3: 开始抓取 Genspark...');
      try {
        const result = await runScript(path.join(__dirname, 'scrape_genspark.js'));
        results.genspark = result.success;
        if (result.success) {
          logger.success('Genspark 抓取完成 ✓');
        } else {
          logger.error(`Genspark 抓取失败 (exit code: ${result.code})`);
        }
      } catch (err) {
        results.genspark = false;
        logger.error(`Genspark 抓取异常: ${err.message}`);
      }
      
      // 检查是否至少有一个成功
      const scrapeSuccessCount = [results.chatgpt, results.gemini, results.genspark].filter(Boolean).length;
      logger.info(`抓取完成: ${scrapeSuccessCount}/3 成功`);
      
      if (scrapeSuccessCount === 0) {
        logger.error('所有抓取都失败，终止流程');
        logger.save();
        process.exit(1);
      }
    }
    
    if (scrapeOnly) {
      logger.info('仅抓取模式，跳过后续步骤');
      logger.save();
      printSummary(results, dateStr);
      return;
    }
    
    // ==================== Step 4: 合并报告 ====================
    console.log('\n');
    console.log('┌────────────────────────────────────────────────────────────┐');
    console.log('│  📋 Step 4: 合并报告                                        │');
    console.log('└────────────────────────────────────────────────────────────┘');
    
    logger.info('Step 4: 开始合并报告...');
    try {
      const result = await runScript(path.join(__dirname, 'merge_reports.js'));
      results.merge = result.success;
      if (result.success) {
        logger.success('报告合并完成 ✓');
      } else {
        logger.error(`报告合并失败 (exit code: ${result.code})`);
      }
    } catch (err) {
      results.merge = false;
      logger.error(`报告合并异常: ${err.message}`);
    }
    
    // 检查合并文件是否存在
    const combinedFile = path.join(CONFIG.REPORTS_DIR, `combined-${dateStr}.md`);
    if (!fs.existsSync(combinedFile)) {
      logger.error(`合并文件不存在: ${combinedFile}`);
      logger.save();
      process.exit(1);
    }
    
    // ==================== Step 5: 上传 NotebookLM ====================
    console.log('\n');
    console.log('┌────────────────────────────────────────────────────────────┐');
    console.log('│  📤 Step 5: 上传到 NotebookLM 并生成音频/演示文稿            │');
    console.log('└────────────────────────────────────────────────────────────┘');
    
    logger.info('Step 5: 开始上传到 NotebookLM...');
    try {
      const result = await runScript(path.join(__dirname, 'upload_notebooklm.js'), [combinedFile]);
      results.notebooklm = result.success;
      if (result.success) {
        logger.success('NotebookLM 上传完成 ✓');
      } else {
        logger.error(`NotebookLM 上传失败 (exit code: ${result.code})`);
      }
    } catch (err) {
      results.notebooklm = false;
      logger.error(`NotebookLM 上传异常: ${err.message}`);
    }
    
    // ==================== Step 6: 生成 HTML 摘要 ====================
    console.log('\n');
    console.log('┌────────────────────────────────────────────────────────────┐');
    console.log('│  📊 Step 6: 生成智能摘要 HTML                               │');
    console.log('└────────────────────────────────────────────────────────────┘');
    
    logger.info('Step 6: 开始生成智能摘要...');
    try {
      const result = await runScript(path.join(__dirname, 'generate_summary.js'));
      results.summary = result.success;
      if (result.success) {
        logger.success('HTML 摘要生成完成 ✓');
        console.log(`\n   📁 HTML 报告：reports/summary-${dateStr}.html`);
      } else {
        logger.error(`摘要生成失败 (exit code: ${result.code})`);
      }
    } catch (err) {
      results.summary = false;
      logger.error(`摘要生成异常: ${err.message}`);
    }
    
    // ==================== 完成 ====================
    logger.info('每日流程执行完毕');
    logger.save();
    printSummary(results, dateStr);
    
  } catch (err) {
    logger.error(`流程异常中断: ${err.message}`);
    logger.save();
    throw err;
  }
}

// 打印执行摘要
function printSummary(results, dateStr) {
  console.log('\n');
  console.log('╔════════════════════════════════════════════════════════════╗');
  console.log('║                      📊 执行摘要                            ║');
  console.log('╚════════════════════════════════════════════════════════════╝');
  console.log('');
  
  const statusIcon = (status) => {
    if (status === null) return '⏭️  跳过';
    return status ? '✅ 成功' : '❌ 失败';
  };
  
  console.log(`  📅 日期: ${dateStr}`);
  console.log('');
  console.log('  ┌─────────────────────────────────────────┐');
  console.log(`  │ ChatGPT 抓取     ${statusIcon(results.chatgpt).padEnd(20)} │`);
  console.log(`  │ Gemini 抓取      ${statusIcon(results.gemini).padEnd(20)} │`);
  console.log(`  │ Genspark 抓取    ${statusIcon(results.genspark).padEnd(20)} │`);
  console.log(`  │ 报告合并         ${statusIcon(results.merge).padEnd(20)} │`);
  console.log(`  │ NotebookLM 上传  ${statusIcon(results.notebooklm).padEnd(20)} │`);
  console.log(`  │ HTML 摘要生成    ${statusIcon(results.summary).padEnd(20)} │`);
  console.log('  └─────────────────────────────────────────┘');
  console.log('');
  
  // 计算成功率
  const allResults = Object.values(results).filter(r => r !== null);
  const successCount = allResults.filter(Boolean).length;
  const totalCount = allResults.length;
  
  if (successCount === totalCount) {
    console.log('  🎉 全部成功！');
  } else if (successCount > 0) {
    console.log(`  ⚠️  部分成功 (${successCount}/${totalCount})`);
  } else {
    console.log('  ❌ 全部失败');
  }
  
  console.log('');
  console.log(`  📁 报告目录: ${CONFIG.REPORTS_DIR}`);
  console.log(`  📝 日志目录: ${CONFIG.LOGS_DIR}`);
  console.log('');
}

// ==================== 入口 ====================

runDaily().catch((err) => {
  console.error('\n❌ 致命错误:', err.message);
  process.exit(1);
});
