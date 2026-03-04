/**
 * AI Radar - ChatGPT Newsletter 抓取脚本（Chrome 用户目录版）
 * 
 * 使用系统 Chrome 的用户数据目录，复用已登录的 session
 */

const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const os = require('os');
const readline = require('readline');
const { TitleMatcher, ProgressLogger } = require('./utils');

// ==================== 配置 ====================
const CONFIG = {
  // 目标对话标题（文字匹配）
  TARGET_CHAT_TITLE: 'AI 行业情报',
  
  // 是否需要切换 workspace（如果对话在默认 Chats 中，设为 null）
  TARGET_WORKSPACE: 'ArenaAtHome',  // 需要切换到 ArenaAtHome workspace
  
  // 独立的 Chrome 用户数据目录（避免与正在运行的 Chrome 冲突）
  CHROME_USER_DATA_DIR: path.join(__dirname, 'chrome-data-chatgpt'),
  
  // 报告输出目录
  REPORTS_DIR: path.join(__dirname, 'reports'),
  
  // 浏览器配置
  HEADLESS: false,
  SLOW_MO: 500,
  
  // 超时配置（毫秒）
  NAVIGATION_TIMEOUT: 60000,
  CONTENT_LOAD_TIMEOUT: 30000,
};

// ==================== 工具函数 ====================

function getDateString() {
  const now = new Date();
  const year = now.getFullYear();
  const month = String(now.getMonth() + 1).padStart(2, '0');
  const day = String(now.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

function saveReport(content, dateStr) {
  if (!fs.existsSync(CONFIG.REPORTS_DIR)) {
    fs.mkdirSync(CONFIG.REPORTS_DIR, { recursive: true });
  }
  
  const filename = `report-chatgpt-${dateStr}.md`;
  const filepath = path.join(CONFIG.REPORTS_DIR, filename);
  
  const now = new Date();
  const timestamp = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')} ${String(now.getHours()).padStart(2, '0')}:${String(now.getMinutes()).padStart(2, '0')}:${String(now.getSeconds()).padStart(2, '0')}`;
  const header = `# AI Radar 日报 - ChatGPT - ${dateStr}\n\n> 🕐 抓取时间：${timestamp}\n> 📍 来源：ChatGPT「${CONFIG.TARGET_CHAT_TITLE}」\n\n---\n\n`;
  
  fs.writeFileSync(filepath, header + content, 'utf-8');
  console.log(`✅ 报告已保存：${filepath}`);
  return filepath;
}

function logStatus(status, message) {
  const logFile = path.join(__dirname, 'STATUS.log');
  const timestamp = new Date().toISOString();
  const logLine = `[${timestamp}] ${status}: ${message}\n`;
  fs.appendFileSync(logFile, logLine);
}

// ==================== 清理函数 ====================

function cleanupChromeLocks() {
  // 删除可能导致启动失败的锁定文件
  const lockFiles = [
    path.join(CONFIG.CHROME_USER_DATA_DIR, 'SingletonLock'),
    path.join(CONFIG.CHROME_USER_DATA_DIR, 'SingletonSocket'),
    path.join(CONFIG.CHROME_USER_DATA_DIR, 'SingletonCookie'),
  ];
  
  lockFiles.forEach(file => {
    try {
      if (fs.existsSync(file)) {
        fs.unlinkSync(file);
      }
    } catch (err) {
      // 忽略删除失败的错误
    }
  });
}

// ==================== 主抓取逻辑 ====================

async function scrapeChatGPT() {
  const logger = new ProgressLogger('ChatGPT Newsletter 抓取');
  logger.initSteps(6);
  
  let browser;
  try {
    // 步骤 1：检查首次运行
    logger.startStep(1, '检查运行状态', '检查是否首次运行');
    const isFirstRun = !fs.existsSync(CONFIG.CHROME_USER_DATA_DIR);
    if (isFirstRun) {
      logger.warn('首次运行，需要手动登录 ChatGPT');
      logger.info('浏览器打开后，请完成登录，然后关闭浏览器重新运行脚本');
    } else {
      logger.success('使用已保存的登录状态');
      // 清理可能存在的锁定文件
      logger.detail('清理 Chrome 锁定文件...');
      cleanupChromeLocks();
    }
    logger.completeStep();
    
    // 步骤 2：启动浏览器
    logger.startStep(2, '启动浏览器', '初始化 Chromium 浏览器');
    
    browser = await chromium.launchPersistentContext(CONFIG.CHROME_USER_DATA_DIR, {
      headless: false,
      slowMo: CONFIG.SLOW_MO,
      viewport: { width: 1400, height: 900 },
      args: [
        '--disable-blink-features=AutomationControlled',
        '--no-sandbox',
      ],
    });
    
    const page = browser.pages()[0] || await browser.newPage();
    page.setDefaultTimeout(CONFIG.NAVIGATION_TIMEOUT);
    logger.success('浏览器已启动');
    logger.completeStep();
    
    // 步骤 3：导航到 ChatGPT
    logger.startStep(3, '导航到 ChatGPT', '访问 https://chatgpt.com');
    await page.goto('https://chatgpt.com', { 
      waitUntil: 'domcontentloaded',
      timeout: CONFIG.NAVIGATION_TIMEOUT 
    });
    
    await page.waitForTimeout(5000);
    logger.info('页面加载完成，检查登录状态...');
    
    // 检查是否登录
    let currentUrl = page.url();
    const needLogin = currentUrl.includes('auth') || currentUrl.includes('login') || isFirstRun;
    
    if (needLogin) {
      logger.warn('检测到需要登录');
      logger.info('请在浏览器中完成 ChatGPT 登录...');
      
      const rl = readline.createInterface({
        input: process.stdin,
        output: process.stdout
      });
      await new Promise(resolve => {
        rl.question('   登录完成后按回车继续: ', () => {
          rl.close();
          resolve();
        });
      });
      
      await page.waitForTimeout(3000);
      logger.success('登录完成');
    } else {
      logger.success('已登录');
    }
    logger.completeStep();
    
    // 步骤 4：等待侧边栏加载
    logger.startStep(4, '等待侧边栏', '等待页面完全加载');
    await page.waitForTimeout(5000);
    
    if (CONFIG.TARGET_WORKSPACE) {
      logger.info(`检查 workspace: ${CONFIG.TARGET_WORKSPACE}...`);
      try {
        await page.waitForTimeout(2000);
        
        let alreadyInWorkspace = false;
        const pageText = await page.evaluate(() => document.body.innerText);
        
        const activeWorkspaceSelectors = [
          `[aria-selected="true"]:has-text("${CONFIG.TARGET_WORKSPACE}")`,
          `[data-state="active"]:has-text("${CONFIG.TARGET_WORKSPACE}")`,
          `.active:has-text("${CONFIG.TARGET_WORKSPACE}")`,
          `[class*="selected"]:has-text("${CONFIG.TARGET_WORKSPACE}")`,
        ];
        
        for (const selector of activeWorkspaceSelectors) {
          try {
            const activeWs = page.locator(selector).first();
            if (await activeWs.isVisible({ timeout: 1000 })) {
              alreadyInWorkspace = true;
              logger.success(`已在 ${CONFIG.TARGET_WORKSPACE} workspace`);
              break;
            }
          } catch (e) {
            // 继续
          }
        }
        
        if (!alreadyInWorkspace) {
          logger.info(`尝试切换到 ${CONFIG.TARGET_WORKSPACE}...`);
          const workspaceSelectors = [
            `text="${CONFIG.TARGET_WORKSPACE}"`,
            `[role="button"]:has-text("${CONFIG.TARGET_WORKSPACE}")`,
            `button:has-text("${CONFIG.TARGET_WORKSPACE}")`,
          ];
          
          let clicked = false;
          for (const selector of workspaceSelectors) {
            try {
              const workspaceButton = page.locator(selector).first();
              if (await workspaceButton.isVisible({ timeout: 2000 })) {
                const ariaSelected = await workspaceButton.getAttribute('aria-selected');
                if (ariaSelected === 'true') {
                  logger.success(`已在 ${CONFIG.TARGET_WORKSPACE} workspace`);
                  alreadyInWorkspace = true;
                  break;
                }
                
                await workspaceButton.click();
                logger.success(`已切换到 ${CONFIG.TARGET_WORKSPACE} workspace`);
                logger.info('等待历史记录加载...');
                await page.waitForTimeout(8000);
                clicked = true;
                break;
              }
            } catch (e) {
              continue;
            }
          }
          
          if (!clicked && !alreadyInWorkspace) {
            logger.warn(`未找到 ${CONFIG.TARGET_WORKSPACE}，继续在当前 workspace 查找`);
          }
        }
        
        try {
          await page.keyboard.press('Escape');
          await page.waitForTimeout(500);
        } catch (e) {
          // 忽略
        }
        
      } catch (error) {
        logger.warn(`切换 workspace 出错: ${error.message}`);
      }
    } else {
      logger.info('使用默认 Chats（不切换 workspace）');
      await page.waitForTimeout(3000);
    }
    logger.completeStep();
    
    // 步骤 5：查找目标对话
    logger.startStep(5, '查找对话', `查找「${CONFIG.TARGET_CHAT_TITLE}」对话`);
    
    const titleMatcher = new TitleMatcher();
    
    logger.info('滚动侧边栏加载历史记录...');
    try {
      const sidebarSelectors = [
        'nav[aria-label="Chat history"]',
        'nav',
        '[role="navigation"]',
        'aside',
        '[class*="sidebar"]',
      ];
      
      let sidebar = null;
      for (const sel of sidebarSelectors) {
        const s = page.locator(sel).first();
        if (await s.isVisible({ timeout: 1000 }).catch(() => false)) {
          sidebar = s;
          logger.detail(`找到侧边栏: ${sel}`);
          break;
        }
      }
      
      if (sidebar) {
        // 向上滚动到顶部
        await sidebar.evaluate(el => el.scrollTop = 0);
        await page.waitForTimeout(1000);
        
        // 然后向下滚动加载更多
        for (let i = 0; i < 5; i++) {
          await sidebar.evaluate(el => el.scrollBy(0, 300));
          await page.waitForTimeout(500);
        }
      }
    } catch (e) {
      console.log('   ⚠️  无法滚动侧边栏:', e.message);
    }
    
    // 等待内容加载
    await page.waitForTimeout(2000);
    
    // 尝试多种选择器
    let targetChat = null;
    let found = false;
    
    // 策略1：在侧边栏中查找包含目标文字的元素（使用模糊匹配）
    const selectors = [
      `a:has-text("${CONFIG.TARGET_CHAT_TITLE}")`,
      `li:has-text("${CONFIG.TARGET_CHAT_TITLE}")`,
      `[data-testid="history-item"]:has-text("${CONFIG.TARGET_CHAT_TITLE}")`,
      `[role="listitem"]:has-text("${CONFIG.TARGET_CHAT_TITLE}")`,
      `[class*="conversation"]:has-text("${CONFIG.TARGET_CHAT_TITLE}")`,
    ];
    
    for (const selector of selectors) {
      try {
        targetChat = page.locator(selector).first();
        if (await targetChat.isVisible({ timeout: 2000 })) {
          found = true;
          console.log(`   ✓ 使用选择器找到: ${selector}`);
          break;
        }
      } catch (e) {
        // 继续尝试下一个选择器
      }
    }
    
    if (!found) {
      // 策略2：遍历所有可点击元素并打印对话列表，使用标题匹配工具
      console.log('   尝试遍历侧边栏...');
      const allLinks = page.locator('nav a, nav li, [role="listitem"], [class*="conversation-item"]');
      const count = await allLinks.count();
      console.log(`   共 ${count} 个可点击元素`);
      
      // 收集所有对话标题
      const titles = [];
      console.log('   --- 侧边栏对话列表 ---');
      for (let i = 0; i < Math.min(count, 30); i++) {
        const text = await allLinks.nth(i).textContent().catch(() => '');
        const trimmed = text.trim().replace(/\s+/g, ' ');
        if (trimmed.length > 2 && trimmed.length < 100) {
          titles.push({ text: trimmed, index: i, element: allLinks.nth(i) });
          console.log(`   [${i}] "${trimmed}"`);
        }
      }
      console.log('   --- 列表结束 ---');
      
      // 使用标题匹配工具查找最佳匹配
      if (titles.length > 0) {
        const titleTexts = titles.map(t => t.text);
        const bestMatch = titleMatcher.getBestMatch(titleTexts, CONFIG.TARGET_CHAT_TITLE, 0.6);
        
        if (bestMatch) {
          const matchedTitle = titles.find(t => t.text === bestMatch.title);
          if (matchedTitle) {
            targetChat = matchedTitle.element;
            found = true;
            console.log(`   ✓ 找到匹配！ "${bestMatch.title}" (匹配度: ${(bestMatch.score * 100).toFixed(0)}%, 方法: ${bestMatch.method})`);
          }
        }
      }
    }
    
    if (!found) {
      // 截图帮助调试
      const screenshotPath = path.join(__dirname, 'debug-screenshot.png');
      await page.screenshot({ path: screenshotPath, fullPage: true });
      console.log(`   📸 已保存截图：${screenshotPath}`);
      throw new Error(`未找到对话「${CONFIG.TARGET_CHAT_TITLE}」，请检查对话名称是否正确`);
    }
    logger.completeStep();
    
    // 步骤 6：进入对话并提取内容
    logger.startStep(6, '提取内容', '进入对话并获取最后一条回复');
    
    logger.info('点击进入目标对话...');
    await targetChat.click();
    await page.waitForTimeout(5000);
    logger.success('对话已打开');
    
    logger.info('等待消息加载...');
    await page.waitForSelector('[data-message-author-role="assistant"]', {
      timeout: CONFIG.CONTENT_LOAD_TIMEOUT
    }).catch(() => {
      logger.warn('未找到标准消息选择器，尝试备用方案...');
    });
    
    const assistantMessages = page.locator('[data-message-author-role="assistant"]');
    let messageCount = await assistantMessages.count();
    
    if (messageCount === 0) {
      logger.info('尝试备用消息选择器...');
      const altSelectors = [
        '.markdown',
        '[class*="message"]',
        '[class*="Message"]',
        '.prose',
      ];
      
      for (const selector of altSelectors) {
        const msgs = page.locator(selector);
        messageCount = await msgs.count();
        if (messageCount > 0) {
          logger.detail(`找到 ${messageCount} 条消息 (使用选择器: ${selector})`);
          break;
        }
      }
    }
    
    if (messageCount === 0) {
      logger.error('未找到任何消息');
      throw new Error('未找到任何消息');
    }
    
    logger.info(`找到 ${messageCount} 条消息，提取最后一条...`);
    
    // ========================================
    // 核心改进：精确提取最后一条 AI 回复
    // ========================================
    const lastMessage = assistantMessages.last();
    
    // 记录消息数量用于调试
    logger.detail(`消息总数: ${messageCount}，只提取最后一条`);
    
    // 提取HTML并转换为Markdown以保留链接
    const content = await lastMessage.evaluate(element => {
      function htmlToMarkdown(node) {
        if (node.nodeType === Node.TEXT_NODE) {
          return node.textContent;
        }
        
        if (node.nodeType !== Node.ELEMENT_NODE) {
          return '';
        }
        
        const tag = node.tagName.toLowerCase();
        const children = Array.from(node.childNodes).map(htmlToMarkdown).join('');
        
        switch (tag) {
          case 'a':
            const href = node.getAttribute('href');
            if (href && !href.startsWith('javascript:')) {
              const fullHref = href.startsWith('http') ? href : (href.startsWith('/') ? `https://chatgpt.com${href}` : href);
              return `[${children}](${fullHref})`;
            }
            return children;
          case 'strong':
          case 'b':
            return `**${children}**`;
          case 'em':
          case 'i':
            return `*${children}*`;
          case 'code':
            return `\`${children}\``;
          case 'pre':
            return `\n\`\`\`\n${children}\n\`\`\`\n`;
          case 'h1':
            return `\n# ${children}\n`;
          case 'h2':
            return `\n## ${children}\n`;
          case 'h3':
            return `\n### ${children}\n`;
          case 'h4':
            return `\n#### ${children}\n`;
          case 'p':
          case 'div':
            return children + '\n';
          case 'br':
            return '\n';
          case 'ul':
          case 'ol':
            return '\n' + children + '\n';
          case 'li':
            return `- ${children}\n`;
          default:
            return children;
        }
      }
      
      return htmlToMarkdown(element).trim();
    });
    
    if (!content || content.trim().length === 0) {
      logger.error('提取内容为空');
      throw new Error('提取内容为空');
    }
    
    logger.success(`成功提取 (${content.length} 字符)`);
    logger.detail(`内容预览: ${content.substring(0, 100).replace(/\n/g, ' ')}...`);
    logger.completeStep();
    
    // 保存报告
    const dateStr = getDateString();
    const reportPath = saveReport(content, dateStr);
    
    logger.finish(true);
    logStatus('SUCCESS', `报告已保存：${reportPath}`);
    
    await page.waitForTimeout(2000);
    
  } catch (error) {
    logger.finish(false);
    logger.error(error.message);
    logStatus('FAILED', error.message);
    throw error;
    
  } finally {
    if (browser) {
      console.log('\n🔒 关闭浏览器...');
      await browser.close();
    }
  }
}

// ==================== 入口 ====================
scrapeChatGPT().catch((error) => {
  process.exit(1);
});
