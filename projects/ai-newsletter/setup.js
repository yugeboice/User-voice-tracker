#!/usr/bin/env node
/**
 * AI Radar - 安装引导脚本
 * 
 * 一键完成：
 * 1. 安装依赖
 * 2. 安装 Playwright 浏览器
 * 3. 引导登录各平台
 * 4. 配置定时任务（可选）
 */

const { execSync, spawn } = require('child_process');
const fs = require('fs');
const path = require('path');
const readline = require('readline');
const os = require('os');

const PROJECT_DIR = __dirname;

// 颜色输出
const colors = {
  reset: '\x1b[0m',
  green: '\x1b[32m',
  yellow: '\x1b[33m',
  blue: '\x1b[34m',
  red: '\x1b[31m',
  cyan: '\x1b[36m',
};

function log(msg, color = 'reset') {
  console.log(`${colors[color]}${msg}${colors.reset}`);
}

function createReadlineInterface() {
  return readline.createInterface({
    input: process.stdin,
    output: process.stdout
  });
}

async function question(rl, prompt) {
  return new Promise(resolve => {
    rl.question(prompt, answer => resolve(answer.trim()));
  });
}

async function runCommand(cmd, description) {
  log(`\n▶ ${description}...`, 'blue');
  try {
    execSync(cmd, { cwd: PROJECT_DIR, stdio: 'inherit' });
    log(`✓ ${description} 完成`, 'green');
    return true;
  } catch (err) {
    log(`✗ ${description} 失败: ${err.message}`, 'red');
    return false;
  }
}

async function runScript(scriptPath) {
  return new Promise((resolve) => {
    const nodePath = process.execPath;
    const child = spawn(nodePath, [scriptPath], {
      cwd: PROJECT_DIR,
      stdio: 'inherit',
    });
    child.on('close', (code) => {
      resolve(code === 0);
    });
    child.on('error', () => {
      resolve(false);
    });
  });
}

function getNodePath() {
  try {
    return execSync('which node', { encoding: 'utf-8' }).trim();
  } catch {
    return '/usr/local/bin/node';
  }
}

function generateLaunchdPlist(hour = 9, minute = 0) {
  const nodePath = getNodePath();
  return `<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.airadar.daily</string>
    
    <key>ProgramArguments</key>
    <array>
        <string>${nodePath}</string>
        <string>${PROJECT_DIR}/run_daily.js</string>
    </array>
    
    <key>WorkingDirectory</key>
    <string>${PROJECT_DIR}</string>
    
    <key>StartCalendarInterval</key>
    <dict>
        <key>Hour</key>
        <integer>${hour}</integer>
        <key>Minute</key>
        <integer>${minute}</integer>
    </dict>
    
    <key>StandardOutPath</key>
    <string>${PROJECT_DIR}/logs/launchd.log</string>
    
    <key>StandardErrorPath</key>
    <string>${PROJECT_DIR}/logs/launchd-error.log</string>
    
    <key>RunAtLoad</key>
    <false/>
</dict>
</plist>`;
}

async function main() {
  console.log('\n');
  log('╔════════════════════════════════════════════════════════════╗', 'cyan');
  log('║                                                            ║', 'cyan');
  log('║           🚀 AI Radar - 安装引导程序                        ║', 'cyan');
  log('║                                                            ║', 'cyan');
  log('╚════════════════════════════════════════════════════════════╝', 'cyan');
  console.log('');

  const rl = createReadlineInterface();

  try {
    // ==================== Step 1: 安装依赖 ====================
    log('\n📦 [Step 1/4] 安装项目依赖\n', 'yellow');
    
    const hasNodeModules = fs.existsSync(path.join(PROJECT_DIR, 'node_modules'));
    if (hasNodeModules) {
      log('   ✓ node_modules 已存在，跳过 npm install', 'green');
    } else {
      await runCommand('npm install', '安装 npm 依赖');
    }

    // ==================== Step 2: 安装 Playwright 浏览器 ====================
    log('\n🌐 [Step 2/4] 安装 Playwright Chromium 浏览器\n', 'yellow');
    
    await runCommand('npx playwright install chromium', '安装 Chromium');

    // ==================== Step 3: 登录各平台 ====================
    log('\n🔐 [Step 3/4] 登录各平台\n', 'yellow');
    log('   接下来会依次打开浏览器窗口，请在每个窗口中登录对应账号。', 'reset');
    log('   登录完成后，关闭浏览器或按 Ctrl+C 继续下一个。\n', 'reset');

    const platforms = [
      { name: 'ChatGPT', script: 'scrape_chatgpt_chrome.js' },
      { name: 'Gemini', script: 'scrape_gemini_debug.js' },
      { name: 'Genspark', script: 'scrape_genspark.js' },
      { name: 'NotebookLM', script: 'upload_notebooklm.js' },
    ];

    for (const platform of platforms) {
      const answer = await question(rl, `   是否登录 ${platform.name}? (Y/n): `);
      if (answer.toLowerCase() !== 'n') {
        log(`\n   ▶ 启动 ${platform.name}...`, 'blue');
        log(`   提示：登录成功后，脚本会自动运行一次。完成后浏览器会关闭。\n`, 'reset');
        
        const scriptPath = path.join(PROJECT_DIR, platform.script);
        await runScript(scriptPath);
        
        log(`   ✓ ${platform.name} 登录/测试完成\n`, 'green');
      } else {
        log(`   ⏭ 跳过 ${platform.name}`, 'yellow');
      }
    }

    // ==================== Step 4: 配置定时任务 ====================
    log('\n⏰ [Step 4/4] 配置定时任务（macOS）\n', 'yellow');
    
    if (os.platform() === 'darwin') {
      const setupSchedule = await question(rl, '   是否设置每天自动运行? (Y/n): ');
      
      if (setupSchedule.toLowerCase() !== 'n') {
        const timeInput = await question(rl, '   运行时间（格式 HH:MM，默认 09:00）: ');
        let hour = 9, minute = 0;
        
        const timeMatch = timeInput.match(/^(\d{1,2}):(\d{2})$/);
        if (timeMatch) {
          hour = parseInt(timeMatch[1]);
          minute = parseInt(timeMatch[2]);
        }
        
        const plistContent = generateLaunchdPlist(hour, minute);
        const plistPath = path.join(os.homedir(), 'Library/LaunchAgents/com.airadar.daily.plist');
        
        // 先卸载旧的（如果存在）
        try {
          execSync(`launchctl unload "${plistPath}" 2>/dev/null`, { stdio: 'ignore' });
        } catch {}
        
        fs.writeFileSync(plistPath, plistContent);
        log(`   ✓ 配置文件已写入: ${plistPath}`, 'green');
        
        try {
          execSync(`launchctl load "${plistPath}"`);
          log(`   ✓ 定时任务已启用：每天 ${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')} 运行`, 'green');
        } catch (err) {
          log(`   ⚠ 加载定时任务失败，请手动运行: launchctl load "${plistPath}"`, 'yellow');
        }
      }
    } else {
      log('   ⚠ 定时任务配置仅支持 macOS', 'yellow');
      log('   在其他系统上，请使用 cron 或 Task Scheduler 配置定时运行:', 'reset');
      log(`   命令: node ${path.join(PROJECT_DIR, 'run_daily.js')}`, 'reset');
    }

    // ==================== 完成 ====================
    console.log('\n');
    log('╔════════════════════════════════════════════════════════════╗', 'green');
    log('║                   🎉 安装完成！                             ║', 'green');
    log('╚════════════════════════════════════════════════════════════╝', 'green');
    console.log('');
    log('📋 使用方法:', 'yellow');
    console.log('');
    log('   npm run daily      # 运行完整流程（抓取+合并+上传+摘要）', 'reset');
    log('   npm run scrape     # 仅抓取三个平台', 'reset');
    log('   npm run summary    # 仅生成 HTML 摘要', 'reset');
    console.log('');
    log('📁 输出目录:', 'yellow');
    console.log('');
    log(`   ${path.join(PROJECT_DIR, 'reports')}`, 'reset');
    console.log('');

  } catch (err) {
    log(`\n❌ 安装出错: ${err.message}`, 'red');
  } finally {
    rl.close();
  }
}

main();
