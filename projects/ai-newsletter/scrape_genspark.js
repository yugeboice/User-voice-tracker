/**
 * AI Radar - Genspark Newsletter 抓取脚本
 * 
 * 流程（全自动，无需按回车）：
 * 1. 打开 Genspark（持久化登录）
 * 2. 点击菜单展开对话历史
 * 3. 选择「AI行业情报」对话
 * 4. 检查历史对话是否有2天内的 AI Newsletter
 *    - 有：直接抓取最新内容
 *    - 没有：发送 prompt 生成新的（等待最长5分钟）
 * 5. 保存报告
 */

const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const readline = require('readline');
const { TitleMatcher, ProgressLogger } = require('./utils');

// ==================== 配置 ====================
const CONFIG = {
  GENSPARK_URL: 'https://www.genspark.ai/',
  TARGET_CHAT_TITLE: 'AI行业情报',  // 无空格版本
  TARGET_CHAT_TITLE_ALT: 'AI 行业情报',  // 有空格版本（备用）
  TRIGGER_PROMPT: '生成本周AI Newsletter',
  CHROME_USER_DATA_DIR: path.join(__dirname, 'chrome-data-genspark'),
  REPORTS_DIR: path.join(__dirname, 'reports'),
  DEBUG_DIR: path.join(__dirname, 'debug'),
  HEADLESS: false,
  SLOW_MO: 100,
  RESPONSE_TIMEOUT: 300000,  // 等待回复：5分钟
  NEWSLETTER_MAX_AGE_DAYS: 2,  // Newsletter 有效期：2天
};

// ==================== 工具函数 ====================

function setupDebugDir() {
  if (!fs.existsSync(CONFIG.DEBUG_DIR)) {
    fs.mkdirSync(CONFIG.DEBUG_DIR, { recursive: true });
  }
}

function getDateString() {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
}

function saveReport(content, dateStr, isNew = true) {
  if (!fs.existsSync(CONFIG.REPORTS_DIR)) {
    fs.mkdirSync(CONFIG.REPORTS_DIR, { recursive: true });
  }
  
  const filename = `report-genspark-${dateStr}.md`;
  const filepath = path.join(CONFIG.REPORTS_DIR, filename);
  
  const source = isNew ? '新生成' : '历史抓取';
  const now = new Date();
  const timestamp = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')} ${String(now.getHours()).padStart(2, '0')}:${String(now.getMinutes()).padStart(2, '0')}:${String(now.getSeconds()).padStart(2, '0')}`;
  const header = `# AI Radar 日报 - Genspark - ${dateStr}\n\n> 🕐 抓取时间：${timestamp}\n> 📍 来源：Genspark「${CONFIG.TARGET_CHAT_TITLE}」(${source})\n\n---\n\n`;
  
  fs.writeFileSync(filepath, header + content, 'utf-8');
  console.log(`✅ 报告已保存：${filepath}`);
  return filepath;
}

async function waitForUserInput(prompt) {
  console.log(prompt);
  const rl = readline.createInterface({
    input: process.stdin,
    output: process.stdout
  });
  await new Promise(resolve => {
    rl.question('', () => {
      rl.close();
      resolve();
    });
  });
}

/**
 * 检查内容是否包含2天内的 Newsletter
 */
function checkRecentNewsletter(pageText) {
  const now = new Date();
  const twoDaysAgo = new Date(now.getTime() - CONFIG.NEWSLETTER_MAX_AGE_DAYS * 24 * 60 * 60 * 1000);
  
  // 检查是否包含 Newsletter 标识
  const hasNewsletter = pageText.includes('AI Newsletter') || 
                        pageText.includes('核心Facts') || 
                        pageText.includes('核心情报汇总') ||
                        pageText.includes('📊') ||
                        pageText.includes('每日简报') ||
                        pageText.includes('AI Competitive') ||
                        pageText.includes('Core Facts') ||
                        pageText.includes('AI 行业') ||
                        pageText.includes('行业动态') ||
                        pageText.includes('行业情报') ||
                        pageText.includes('重要事件');
  
  if (!hasNewsletter) {
    return { hasRecent: false, content: null };
  }
  
  // 解析日期 - 模式: 2026.01.17 或 2026-01-17 或 2026/01/17
  const match = pageText.match(/(\d{4})[./-](\d{1,2})[./-](\d{1,2})/);
  if (match) {
    const year = parseInt(match[1]);
    const month = parseInt(match[2]) - 1;
    const day = parseInt(match[3]);
    const contentDate = new Date(year, month, day);
    
    if (contentDate >= twoDaysAgo) {
      console.log(`   找到日期: ${year}-${month+1}-${day} (${CONFIG.NEWSLETTER_MAX_AGE_DAYS}天内有效)`);
      return { hasRecent: true, content: pageText };
    } else {
      console.log(`   找到日期: ${year}-${month+1}-${day} (已过期)`);
    }
  }
  
  // 模式2: 1月17日（假设当前年份）
  const match2 = pageText.match(/(\d{1,2})月(\d{1,2})日/);
  if (match2) {
    const month = parseInt(match2[1]) - 1;
    const day = parseInt(match2[2]);
    const contentDate = new Date(now.getFullYear(), month, day);
    
    if (contentDate >= twoDaysAgo) {
      console.log(`   找到日期: ${now.getFullYear()}-${month+1}-${day} (${CONFIG.NEWSLETTER_MAX_AGE_DAYS}天内有效)`);
      return { hasRecent: true, content: pageText };
    }
  }
  
  return { hasRecent: false, content: null };
}

/**
 * 提取并清理内容 - 只提取最后一条 AI 回复
 * 核心改进：精确定位最后一个 AI 回复消息，避免抓取整个对话历史
 */
async function extractContent(page) {
  const content = await page.evaluate(() => {
    // 辅助函数：将HTML元素转换为Markdown
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
            const fullHref = href.startsWith('http') ? href : (href.startsWith('/') ? `https://genspark.ai${href}` : href);
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
        case 'table':
          return '\n' + children + '\n';
        case 'thead':
        case 'tbody':
          return children;
        case 'tr':
          return children + '\n';
        case 'th':
        case 'td':
          return children + ' | ';
        default:
          return children;
      }
    }
    
    // =========================================
    // 核心：精确定位最后一条 AI 回复
    // =========================================
    
    // 隐藏侧边栏避免干扰
    const sidebar = document.querySelector('[class*="sidebar"], nav, [class*="task-list"]');
    if (sidebar) {
      sidebar.style.display = 'none';
    }
    
    // Newsletter 内容特征标记
    const newsletterMarkers = [
      '# **AI Newsletter',
      '# AI Newsletter',
      '## 一、极简版',
      '## A) 极简版',
      '## 完整版',
      '## 🧠 完整版',
      '📊 核心情报汇总',
      'Core Facts',
      'Key Facts',
    ];
    
    const isNewsletterContent = (text) => {
      return newsletterMarkers.some(marker => text.includes(marker));
    };
    
    // 方法1：Genspark 特定的消息选择器
    const messageSelectors = [
      '[data-message-role="assistant"]',
      '[data-role="assistant"]',
      '[class*="assistant-message"]',
      '[class*="ai-response"]',
      '[class*="bot-message"]',
      '[class*="model-response"]',
      '[class*="message-content"]',
      '[class*="markdown-body"]',
      '[class*="prose"]',
      'article',
    ];
    
    let lastMessage = null;
    
    // 尝试消息选择器
    for (const selector of messageSelectors) {
      try {
        const messages = document.querySelectorAll(selector);
        if (messages.length === 0) continue;
        
        // 从后往前找，找到第一个包含 Newsletter 内容的消息
        for (let i = messages.length - 1; i >= 0; i--) {
          const msg = messages[i];
          const text = msg.innerText || '';
          
          // 必须有足够内容（>500字符）且包含 Newsletter 特征
          if (text.length > 500 && isNewsletterContent(text)) {
            lastMessage = msg;
            console.log(`Found AI message using ${selector}, index ${i}/${messages.length-1}, length ${text.length}`);
            break;
          }
        }
        
        if (lastMessage) break;
      } catch (e) {
        continue;
      }
    }
    
    // 方法2：基于页面内容分析定位最后一个 Newsletter 块
    if (!lastMessage) {
      console.log('Fallback: searching for Newsletter content block in main area...');
      
      const main = document.querySelector('main') || document.body;
      const bodyText = main.innerText || '';
      
      // 找到最后一个 Newsletter 开始标记的位置
      let latestStart = -1;
      let latestMarker = '';
      
      // 优先寻找结构化的 Newsletter 开头
      const strongMarkers = [
        '## 一、极简版',
        '## A) 极简版',
        '# **AI Newsletter',
        '# AI Newsletter',
        '📊 核心情报汇总',
      ];
      
      for (const marker of strongMarkers) {
        let pos = bodyText.lastIndexOf(marker);
        if (pos > latestStart) {
          latestStart = pos;
          latestMarker = marker;
        }
      }
      
      if (latestStart > 0) {
        console.log(`Found Newsletter at position ${latestStart} with marker: "${latestMarker}"`);
        
        // 提取从该位置开始的内容
        let extractedText = bodyText.substring(latestStart);
        
        // 限制最大长度（最后一条消息不应超过 30000 字符）
        if (extractedText.length > 30000) {
          // 寻找合理的结束点
          const possibleEnds = [
            extractedText.indexOf('\n\n---\n\n如需'),
            extractedText.indexOf('\n如需任何帮助'),
            extractedText.indexOf('\n\n说明：在当前对话'),
            extractedText.indexOf('\n\n**如需'),
          ].filter(idx => idx > 1000);
          
          if (possibleEnds.length > 0) {
            const endIdx = Math.min(...possibleEnds);
            extractedText = extractedText.substring(0, endIdx);
          } else {
            extractedText = extractedText.substring(0, 30000);
          }
        }
        
        // 清理多余空行
        return extractedText.replace(/\n{3,}/g, '\n\n').trim();
      }
    }
    
    // 方法3：如果找到了消息容器，提取其内容
    if (lastMessage) {
      let text = htmlToMarkdown(lastMessage);
      
      // 清理多余空行
      text = text.replace(/\n{3,}/g, '\n\n').trim();
      
      // 限制长度
      if (text.length > 30000) {
        const endMarkers = [
          '\n\n---\n\n如需',
          '\n如需任何帮助',
          '\n说明：在当前对话',
        ];
        
        let endIdx = text.length;
        for (const marker of endMarkers) {
          const idx = text.indexOf(marker);
          if (idx > 1000 && idx < endIdx) {
            endIdx = idx;
          }
        }
        text = text.substring(0, Math.min(endIdx, 30000));
      }
      
      return text;
    }
    
    // 方法4：最后的兜底 - 从页面底部提取有限内容
    console.log('Final fallback: extracting limited content from page bottom...');
    
    const main = document.querySelector('main') || document.body;
    let fullText = main.innerText || '';
    
    // 如果文本太长，只取最后部分
    if (fullText.length > 30000) {
      fullText = fullText.substring(fullText.length - 30000);
      // 找到第一个完整段落的开始
      const firstParagraph = fullText.indexOf('\n\n');
      if (firstParagraph > 0 && firstParagraph < 500) {
        fullText = fullText.substring(firstParagraph + 2);
      }
    }
    
    // 清理系统 UI 文本
    const cleanupPatterns = [
      /^Task List[\s\S]*?Search Chats/gm,
      /^任务列表[\s\S]*?搜索对话/gm,
      /^(New|Home|AI Inbox|Hub|AI Drive|Super Agent|Share)\s*$/gm,
      /Using Tool\s*\|\s*Search[^\n]*/g,
      /^搜索对话$/gm,
      /^Search Chats$/gm,
    ];
    
    for (const pattern of cleanupPatterns) {
      fullText = fullText.replace(pattern, '');
    }
    
    return fullText.replace(/\n{3,}/g, '\n\n').trim();
  });
  
  return content;
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

// ==================== 主逻辑 ====================

async function main() {
  const logger = new ProgressLogger('Genspark Newsletter 抓取');
  logger.initSteps(5);
  
  setupDebugDir();
  
  // 步骤 1：检查运行状态
  logger.startStep(1, '检查运行状态', '检查是否首次运行');
  const isFirstRun = !fs.existsSync(CONFIG.CHROME_USER_DATA_DIR);
  if (isFirstRun) {
    logger.warn('首次运行，需要手动登录 Genspark');
    logger.info('浏览器打开后，请完成登录，然后按回车继续');
  } else {
    logger.success('使用已保存的登录状态');
    // 清理可能存在的锁定文件
    logger.detail('清理 Chrome 锁定文件...');
    cleanupChromeLocks();
  }
  logger.completeStep();
  
  // 步骤 2：启动浏览器
  logger.startStep(2, '启动浏览器', '初始化 Chromium 浏览器');
  const context = await chromium.launchPersistentContext(CONFIG.CHROME_USER_DATA_DIR, {
    headless: CONFIG.HEADLESS,
    slowMo: CONFIG.SLOW_MO,
    viewport: { width: 1400, height: 900 },
    args: ['--disable-blink-features=AutomationControlled', '--no-sandbox'],
  });
  
  const page = await context.newPage();
  logger.success('浏览器已启动');
  logger.completeStep();
  
  try {
    // 步骤 3：导航到 Genspark
    logger.startStep(3, '导航到 Genspark', '访问 ' + CONFIG.GENSPARK_URL);
    
    // 使用 domcontentloaded 而不是 networkidle，因为现代网页可能有持续的后台请求
    await page.goto(CONFIG.GENSPARK_URL, { waitUntil: 'domcontentloaded', timeout: 30000 });
    logger.detail('页面已加载');
    
    // 等待页面主要内容渲染
    await page.waitForTimeout(3000);
    
    const pageText = await page.textContent('body').catch(() => '');
    
    const isLoggedIn = pageText.includes('New') ||           
                       pageText.includes('Home') ||          
                       pageText.includes('AI Inbox') ||      
                       pageText.includes('Hub') ||           
                       pageText.includes('AI Drive') ||      
                       pageText.includes('Ask anything') ||  
                       pageText.includes('Super Agent') ||
                       pageText.includes('超级智能体');
    
    if (!isLoggedIn) {
      logger.warn('请在浏览器中登录 Genspark...');
      await waitForUserInput('   登录完成后按回车继续\n');
      await page.waitForTimeout(2000);
      logger.success('登录完成');
    } else {
      logger.success('已登录');
    }
    logger.completeStep();
    
    // 步骤 4：点击菜单展开对话历史
    logger.startStep(4, '展开对话历史', '点击菜单按钮');
    
    await page.waitForTimeout(2000);
    
    // 创建一个辅助函数来检查目标对话是否可见（同时检查有空格和无空格版本）
    const checkTargetChatVisible = async (timeout = 1000) => {
      // 使用 Genspark 特定的选择器：对话列表中的链接
      const gensparkChatSelector = page.locator('a[href*="/agents?id="]').filter({ hasText: /AI.*行业.*情报|AI行业情报/ });
      if (await gensparkChatSelector.first().isVisible({ timeout }).catch(() => false)) {
        return { visible: true, locator: gensparkChatSelector.first(), method: 'genspark-agents-link' };
      }
      
      // 检查无空格版本
      const noSpaceLocator = page.locator(`text="${CONFIG.TARGET_CHAT_TITLE}"`);
      if (await noSpaceLocator.first().isVisible({ timeout }).catch(() => false)) {
        return { visible: true, locator: noSpaceLocator.first(), method: 'exact-no-space' };
      }
      
      // 检查有空格版本
      const withSpaceLocator = page.locator(`text="${CONFIG.TARGET_CHAT_TITLE_ALT}"`);
      if (await withSpaceLocator.first().isVisible({ timeout }).catch(() => false)) {
        return { visible: true, locator: withSpaceLocator.first(), method: 'exact-with-space' };
      }
      
      // 使用正则匹配
      const regexLocator = page.locator('text=/AI\\s*行业\\s*情报/i');
      if (await regexLocator.first().isVisible({ timeout }).catch(() => false)) {
        return { visible: true, locator: regexLocator.first(), method: 'regex-match' };
      }
      
      return { visible: false, locator: null, method: null };
    };
    
    // 先检查对话列表是否已经可见
    let chatCheck = await checkTargetChatVisible(2000);
    
    if (!chatCheck.visible) {
      logger.info('对话列表未可见，尝试展开菜单...');
      
      // 尝试多种方法找到并点击菜单按钮
      let menuOpened = false;
      
      // 方法1：查找汉堡菜单图标（三条横线）或常见的菜单按钮
      const menuSelectors = [
        'button[aria-label*="menu" i]',
        'button[aria-label*="菜单" i]',
        'button[class*="menu" i]',
        '[role="button"][aria-label*="menu" i]',
        'svg[class*="menu" i]',
        'div.cursor-pointer.text-black',
        '[class*="hamburger" i]',
        '[class*="sidebar" i] button:first-child',
        // Genspark 特定选择器
        'button[class*="toggle"]',
        '[class*="nav"] button',
      ];
      
      for (const selector of menuSelectors) {
        try {
          const menuButton = page.locator(selector).first();
          if (await menuButton.isVisible({ timeout: 500 }).catch(() => false)) {
            await menuButton.click();
            logger.detail(`已点击菜单按钮 (${selector})`);
            await page.waitForTimeout(2500);
            
            // 检查是否成功展开 - 使用改进的检查函数
            chatCheck = await checkTargetChatVisible(1500);
            if (chatCheck.visible) {
              menuOpened = true;
              logger.detail(`菜单展开成功，找到目标对话 (${chatCheck.method})`);
              break;
            }
          }
        } catch (err) {
          // 继续尝试下一个选择器
        }
      }
      
      // 方法2：如果所有选择器都失败，尝试坐标点击（左上角区域）
      if (!menuOpened) {
        logger.info('使用坐标点击左上角菜单区域...');
        await page.mouse.click(50, 30);
        await page.waitForTimeout(1500);
        await page.mouse.click(98, 30);
        await page.waitForTimeout(2500);
        
        // 再次检查
        chatCheck = await checkTargetChatVisible(1500);
        if (chatCheck.visible) {
          menuOpened = true;
        }
      }
      
      logger.success('菜单已展开');
    } else {
      logger.success(`对话列表已可见 (${chatCheck.method})`);
    }
    logger.completeStep();
    
    // 步骤 5：进入对话并提取内容
    logger.startStep(5, '进入对话并提取', '查找并点击对话，提取内容');
    
    await page.waitForTimeout(2000);
    
    const titleMatcher = new TitleMatcher();
    let chatFound = false;
    
    // 方法0：如果在步骤4中已经找到了目标对话，直接点击
    if (chatCheck && chatCheck.visible && chatCheck.locator) {
      logger.detail(`使用步骤4中找到的对话元素 (${chatCheck.method})`);
      try {
        await chatCheck.locator.click();
        logger.success(`✓ 点击对话成功 (${chatCheck.method})`);
        chatFound = true;
      } catch (err) {
        logger.warn(`点击失败: ${err.message}，尝试其他方法...`);
      }
    }
    
    // 方法1：使用 Genspark 特定选择器 - 对话链接
    if (!chatFound) {
      logger.detail(`查找对话: "${CONFIG.TARGET_CHAT_TITLE}" 或 "${CONFIG.TARGET_CHAT_TITLE_ALT}"`);
      
      // 优先使用 Genspark 特定的选择器：对话列表中的链接
      const gensparkChatSelectors = [
        // 精确匹配 agents 链接中包含目标标题的元素
        page.locator('a[href*="/agents?id="]').filter({ hasText: /AI.*行业.*情报|AI行业情报/ }),
        // 无空格版本
        page.locator(`a[href*="/agents?id="]:has-text("${CONFIG.TARGET_CHAT_TITLE}")`),
        // 有空格版本
        page.locator(`a[href*="/agents?id="]:has-text("${CONFIG.TARGET_CHAT_TITLE_ALT}")`),
        // 文本匹配
        page.locator(`text="${CONFIG.TARGET_CHAT_TITLE}"`),
        page.locator(`text="${CONFIG.TARGET_CHAT_TITLE_ALT}"`),
        // 正则匹配
        page.locator('text=/AI\\s*行业\\s*情报/i'),
      ];
      
      for (const locator of gensparkChatSelectors) {
        try {
          if (await locator.first().isVisible({ timeout: 1500 }).catch(() => false)) {
            await locator.first().click();
            const matchText = await locator.first().textContent({ timeout: 500 }).catch(() => '目标对话');
            logger.success(`✓ 找到并点击对话：「${matchText.trim().substring(0, 30)}」`);
            chatFound = true;
            break;
          }
        } catch (err) {
          // 继续尝试下一个选择器
        }
      }
    }
    
    // 方法2：遍历对话列表，使用标题匹配工具模糊匹配
    if (!chatFound) {
      logger.info('精确匹配未找到，尝试模糊匹配...');
      
      // 尝试多种选择器来收集对话列表
      const selectorGroups = [
        // Genspark 特定选择器 - 优先
        'a[href*="/agents?id="]',
        'a[href*="/task/"]',
        // 常见的对话列表选择器
        'div[class*="task"] div[class*="title"]',
        'div[class*="conversation"] span',
        'div[class*="chat"] div[class*="name"]',
        '[role="listitem"]',
        'div[class*="item"] span',
        'div[role="button"] span',
        // 其他可能的选择器
        'div.cursor-pointer span',
        'button span',
      ];
      
      let titles = [];
      
      for (const selector of selectorGroups) {
        const elements = page.locator(selector);
        const count = await elements.count();
        
        if (count > 0) {
          logger.detail(`使用选择器 "${selector}" 找到 ${count} 个元素`);
          
          for (let i = 0; i < Math.min(count, 30); i++) {
            try {
              const text = await elements.nth(i).textContent({ timeout: 500 }).catch(() => '');
              const trimmed = text.trim().replace(/\s+/g, ' ');
              
              // 过滤掉太短、太长、或明显不是标题的文本
              if (trimmed.length >= 3 &&
                  trimmed.length <= 100 &&
                  !trimmed.includes('http') &&
                  !trimmed.match(/^\d+$/)) {
                
                // 避免重复添加相同的标题
                if (!titles.some(t => t.text === trimmed)) {
                  titles.push({
                    text: trimmed,
                    index: i,
                    element: elements.nth(i),
                    selector: selector
                  });
                  logger.detail(`  [${titles.length}] "${trimmed.substring(0, 50)}${trimmed.length > 50 ? '...' : ''}"`);
                }
              }
            } catch (e) {
              // 继续
            }
          }
          
          // 如果找到了足够多的候选项，就不再尝试其他选择器
          if (titles.length >= 5) {
            break;
          }
        }
      }
      
      // 使用标题匹配工具查找最佳匹配
      if (titles.length > 0) {
        logger.info(`共收集到 ${titles.length} 个候选标题`);
        const titleTexts = titles.map(t => t.text);
        
        // 同时尝试匹配有空格和无空格版本
        let bestMatch = titleMatcher.getBestMatch(titleTexts, CONFIG.TARGET_CHAT_TITLE, 0.5);
        if (!bestMatch) {
          bestMatch = titleMatcher.getBestMatch(titleTexts, CONFIG.TARGET_CHAT_TITLE_ALT, 0.5);
        }
        
        if (bestMatch) {
          const matchedTitle = titles.find(t => t.text === bestMatch.title);
          if (matchedTitle) {
            await matchedTitle.element.click();
            logger.success(`✓ 点击匹配对话: "${bestMatch.title}" (匹配度: ${(bestMatch.score * 100).toFixed(0)}%)`);
            chatFound = true;
          }
        } else {
          logger.warn(`未找到匹配度 >= 50% 的对话`);
        }
      } else {
        logger.warn('未找到任何对话标题');
      }
    }
    
    // 方法3：点击任务列表中第一个对话项（备用 - 不推荐）
    if (!chatFound) {
      logger.warn('❌ 未找到匹配的对话！');
      logger.warn('⚠️  将尝试点击第一个对话项作为备用方案');
      logger.warn('⚠️  这可能导致抓取到错误的内容！');
      
      const firstChatSelectors = [
        // Genspark 特定选择器
        'a[href*="/agents?id="]:first-of-type',
        'a[href*="/task/"]:first-of-type',
        '[class*="task"]:first-of-type',
        '[class*="conversation"]:first-of-type',
        '[class*="chat-item"]:first-of-type',
        'div[class*="cursor-pointer"]:has(svg)',
      ];
      
      for (const selector of firstChatSelectors) {
        try {
          const item = page.locator(selector).first();
          if (await item.isVisible({ timeout: 1000 }).catch(() => false)) {
            // 尝试获取这个元素的文本，看看是什么
            const itemText = await item.textContent({ timeout: 500 }).catch(() => '');
            logger.detail(`尝试点击: "${itemText.trim().substring(0, 50)}..."`);
            
            await item.click();
            logger.info(`✓ 已点击第一个对话项 (${selector})`);
            chatFound = true;
            break;
          }
        } catch (e) {
          continue;
        }
      }
    }
    
    // 方法4：通过"搜索对话"或"Search Chats"下方的第一个元素定位
    if (!chatFound) {
      const searchLabels = [page.locator('text="搜索对话"'), page.locator('text="Search Chats"')];
      for (const searchLabel of searchLabels) {
        if (await searchLabel.isVisible({ timeout: 500 }).catch(() => false)) {
          const firstChat = searchLabel.locator('.. >> .. >> a[href*="/agents"]').first();
          if (await firstChat.isVisible({ timeout: 500 }).catch(() => false)) {
            await firstChat.click();
            logger.info('✓ 点击了搜索框下方第一个对话');
            chatFound = true;
            break;
          }
        }
      }
    }
    
    // 方法5：坐标点击（任务列表中第一个对话的大致位置）
    if (!chatFound) {
      logger.info('使用坐标点击第一个对话位置...');
      await page.mouse.click(180, 210);
      chatFound = true;
    }
    
    await page.waitForTimeout(3000);  // 等待对话内容加载
    
    // 验证导航是否成功 - 检查 URL 是否包含 agents?id=
    const currentUrl = page.url();
    logger.detail(`点击后 URL: ${currentUrl}`);
    
    if (currentUrl.includes('/agents?id=') || currentUrl.includes('/task/')) {
      logger.success('✓ 成功进入对话页面');
    } else {
      logger.warn(`⚠️  URL 未变化，可能未成功进入对话页面`);
      logger.detail('尝试等待更长时间...');
      
      // 额外等待并检查页面变化
      await page.waitForTimeout(3000);
      
      // 尝试等待 URL 变化
      try {
        await page.waitForURL(/.*\/(agents\?id=|task\/).*/, { timeout: 5000 });
        logger.success('✓ URL 已更新，成功进入对话页面');
      } catch (urlErr) {
        logger.warn('URL 检查超时，继续尝试提取内容...');
      }
    }
    
    // 等待右侧内容区域加载（检测 "Key Facts" 或其他内容特征）
    logger.detail('等待右侧对话内容加载...');
    let contentLoaded = false;
    for (let retry = 0; retry < 10; retry++) {
      const rightContent = await page.evaluate(() => {
        // 查找右侧区域的内容（排除左侧任务列表）
        const body = document.body.innerText;
        // 检查是否有 Newsletter 内容特征
        return body.includes('Key Facts') || 
               body.includes('Source & Time') || 
               body.includes('核心Facts') ||
               body.includes('AI Newsletter') ||
               body.includes('完整版');
      });
      if (rightContent) {
        console.log('   ✓ 右侧对话内容已加载');
        contentLoaded = true;
        break;
      }
      await page.waitForTimeout(1000);
    }
    
    if (!contentLoaded) {
      console.log('   ⚠️  内容加载超时，继续尝试抓取...');
    }
    
    await page.waitForTimeout(2000);
    
    console.log(`   当前URL: ${page.url()}`);
    
    // 截图以便调试
    await page.screenshot({ path: path.join(__dirname, 'reports', 'genspark_debug.png'), fullPage: false });
    console.log('   📸 已保存调试截图：reports/genspark_debug.png');
    
    // === 步骤 4：检查是否有2天内的 Newsletter ===
    console.log('🔎 [步骤 4/5] 检查历史对话中是否有2天内的 Newsletter...');
    
    // 先滚动到底部看最新内容
    console.log('   向下滚动到最新内容...');
    for (let i = 0; i < 5; i++) {
      await page.evaluate(() => window.scrollTo(0, document.body.scrollHeight));
      await page.waitForTimeout(1000);
    }
    
    // 再滚动回顶部，加载完整对话
    console.log('   向上滚动加载完整对话...');
    for (let i = 0; i < 3; i++) {
      await page.evaluate(() => window.scrollTo(0, 0));
      await page.waitForTimeout(1000);
    }
    
    await page.waitForTimeout(2000);
    
    // 获取当前页面内容
    let currentContent = await extractContent(page);
    console.log(`   页面内容长度: ${currentContent.length} 字符`);
    
    // 检查是否有最近的 Newsletter
    const checkResult = checkRecentNewsletter(currentContent);
    
    let finalContent = '';
    let isNewGenerated = false;
    
    if (checkResult.hasRecent && currentContent.length > 1000) {
      console.log('   ✓ 找到2天内的 Newsletter，直接抓取');
      finalContent = currentContent;
    } else if (currentContent.length > 500) {
      // 即使没有检测到2天内的标记，如果内容足够长也直接使用
      console.log('   ⚠️  未检测到2天内标记，但内容长度足够，使用现有内容');
      finalContent = currentContent;
    } else {
      // 检查是否 credits 用完
      const pageText = await page.evaluate(() => document.body.innerText);
      if (pageText.includes("You've used all your credits") || pageText.includes("credits")) {
        console.log('   ⚠️  Genspark credits 已用完，无法生成新内容');
        console.log('   ⚠️  尝试使用现有内容...');
        
        // 再次滚动尝试获取更多内容
        for (let i = 0; i < 5; i++) {
          await page.keyboard.press('End');
          await page.waitForTimeout(500);
        }
        await page.waitForTimeout(2000);
        
        currentContent = await extractContent(page);
        if (currentContent.length > 200) {
          finalContent = currentContent;
          console.log(`   ✓ 获取到 ${currentContent.length} 字符内容`);
        } else {
          throw new Error('Genspark credits 已用完，且无法获取历史内容');
        }
      } else {
        console.log('   ✗ 未找到2天内的有效 Newsletter，需要重新生成');
        isNewGenerated = true;
      }
    }
    
    // 只有在需要生成新内容时才发送 prompt
    if (isNewGenerated) {
      // 发送 prompt
      console.log(`💬 发送：「${CONFIG.TRIGGER_PROMPT}」...`);
      
      // 查找输入框
      let inputBox = null;
      const inputSelectors = [
        'textarea[placeholder*="询问"]',
        'textarea[placeholder*="Ask"]',
        'textarea[placeholder*="anything"]',
      ];
      
      for (const selector of inputSelectors) {
        const input = page.locator(selector).first();
        if (await input.isVisible({ timeout: 1000 }).catch(() => false)) {
          inputBox = input;
          break;
        }
      }
      
      // 备用：查找可见的 textarea
      if (!inputBox) {
        const allTextareas = await page.locator('textarea').all();
        for (const ta of allTextareas) {
          const isVisible = await ta.isVisible().catch(() => false);
          const className = await ta.getAttribute('class').catch(() => '');
          if (isVisible && !className.includes('g-recaptcha')) {
            inputBox = ta;
            break;
          }
        }
      }
      
      if (!inputBox) {
        throw new Error('未找到输入框');
      }
      
      // 输入并发送
      await inputBox.fill(CONFIG.TRIGGER_PROMPT);
      await page.waitForTimeout(500);
      
      const sendBtn = page.locator('button[type="submit"], button:has-text("发送")').first();
      if (await sendBtn.isVisible({ timeout: 500 }).catch(() => false)) {
        await sendBtn.click();
      } else {
        await inputBox.press('Enter');
      }
      
      console.log('   ✓ 消息已发送');
      
      // 检测并关闭可能出现的弹窗（如 credit 不足提示）
      await page.waitForTimeout(2000);
      const closePopup = async () => {
        const closeSelectors = [
          'button[aria-label="Close"]',
          'button[aria-label="关闭"]',
          '[class*="modal"] button[class*="close"]',
          '[class*="dialog"] button[class*="close"]',
          '[class*="popup"] button[class*="close"]',
          '[class*="modal"] svg[class*="close"]',
          '[role="dialog"] button:has(svg)',
          'button:has-text("×")',
          'button:has-text("✕")',
          'button:has-text("X")',
        ];
        
        for (const selector of closeSelectors) {
          try {
            const closeBtn = page.locator(selector).first();
            if (await closeBtn.isVisible({ timeout: 300 }).catch(() => false)) {
              await closeBtn.click();
              console.log('   ✓ 关闭了弹窗');
              await page.waitForTimeout(500);
              return true;
            }
          } catch (e) {
            continue;
          }
        }
        return false;
      };
      
      // 尝试关闭弹窗（可能多次出现）
      await closePopup();
      await page.waitForTimeout(1000);
      await closePopup();
      
      // 等待回复完成
      console.log(`⏳ 等待 AI 回复（最长 ${CONFIG.RESPONSE_TIMEOUT / 1000 / 60} 分钟）...`);
      
      await page.waitForTimeout(5000);
      
      const startTime = Date.now();
      while (Date.now() - startTime < CONFIG.RESPONSE_TIMEOUT) {
        // 检测并关闭可能的弹窗
        await closePopup();
        
        const isGenerating = await page.locator(
          '[class*="loading"], [class*="generating"], [class*="typing"], ' +
          'button:has-text("停止"), button:has-text("Stop"), ' +
          '[class*="spinner"], [class*="pulse"]'
        ).first().isVisible({ timeout: 500 }).catch(() => false);
        
        if (!isGenerating) {
          await page.waitForTimeout(3000);
          const stillGenerating = await page.locator(
            '[class*="loading"], [class*="generating"], button:has-text("停止")'
          ).first().isVisible({ timeout: 500 }).catch(() => false);
          
          if (!stillGenerating) {
            break;
          }
        }
        
        const elapsed = Math.round((Date.now() - startTime) / 1000);
        process.stdout.write(`\r   已等待 ${elapsed} 秒...`);
        await page.waitForTimeout(3000);
      }
      
      console.log('\n   ✓ 回复完成');
      
      // 提取新生成的内容
      await page.waitForTimeout(2000);
      finalContent = await extractContent(page);
    }
    
    // === 步骤 5：保存报告 ===
    if (finalContent && finalContent.length > 100) {
      logger.success(`提取到 ${finalContent.length} 字符`);
      logger.detail(`内容预览: ${finalContent.substring(0, 100).replace(/\n/g, ' ')}...`);
      
      saveReport(finalContent, getDateString(), isNewGenerated);
    } else {
      logger.warn('提取内容较少，保存完整页面文本');
      const fullText = await page.textContent('body');
      saveReport(fullText, getDateString(), isNewGenerated);
    }
    
    logger.completeStep();
    logger.finish(true);
    
  } catch (error) {
    logger.finish(false);
    logger.error(error.message);
    
    const errorScreenshot = path.join(CONFIG.DEBUG_DIR, `error-genspark-${Date.now()}.png`);
    await page.screenshot({ path: errorScreenshot, fullPage: true });
    logger.detail(`错误截图已保存: ${errorScreenshot}`);
    
  } finally {
    console.log('\n🔒 关闭浏览器...');
    await context.close();
  }
}

main().then(() => {
  process.exit(0);
}).catch(err => {
  console.error('致命错误:', err);
  process.exit(1);
});
