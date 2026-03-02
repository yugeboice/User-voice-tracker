/**
 * AI Radar - Gemini Newsletter 抓取脚本 (调试版本)
 * 
 * 调试模式：每步保存截图，暂停等待确认
 */

const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const readline = require('readline');
const { TitleMatcher, ProgressLogger } = require('./utils');

// ==================== 配置 ====================
const CONFIG = {
  TARGET_CHAT_TITLE: 'AI 行业情报',
  CHROME_USER_DATA_DIR: path.join(__dirname, 'chrome-data'),  // 持久化登录数据
  REPORTS_DIR: path.join(__dirname, 'reports'),
  DEBUG_DIR: path.join(__dirname, 'debug'),
  HEADLESS: false,
  SLOW_MO: 100,   // 加快速度
  DEBUG_MODE: false,  // 关闭调试暂停
};

// ==================== 调试工具 ====================

function setupDebugDir() {
  if (!fs.existsSync(CONFIG.DEBUG_DIR)) {
    fs.mkdirSync(CONFIG.DEBUG_DIR, { recursive: true });
  }
}

async function debugPause(page, stepName, message) {
  if (!CONFIG.DEBUG_MODE) return;
  
  // 保存截图
  const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
  const screenshotPath = path.join(CONFIG.DEBUG_DIR, `${timestamp}-${stepName}.png`);
  await page.screenshot({ path: screenshotPath, fullPage: true });
  console.log(`📸 截图已保存：${screenshotPath}`);
  
  // 暂停等待用户确认
  console.log(`\n⏸️  ${message}`);
  console.log('   浏览器保持打开，你可以手动检查页面');
  console.log('   按回车继续...\n');
  
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
  
  const filename = `report-gemini-${dateStr}.md`;
  const filepath = path.join(CONFIG.REPORTS_DIR, filename);
  
  const now = new Date();
  const timestamp = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')} ${String(now.getHours()).padStart(2, '0')}:${String(now.getMinutes()).padStart(2, '0')}:${String(now.getSeconds()).padStart(2, '0')}`;
  const header = `# AI Radar 日报 - Gemini - ${dateStr}\n\n> 🕐 抓取时间：${timestamp}\n> 📍 来源：Gemini「${CONFIG.TARGET_CHAT_TITLE}」\n\n---\n\n`;
  
  fs.writeFileSync(filepath, header + content, 'utf-8');
  console.log(`✅ 报告已保存：${filepath}`);
  return filepath;
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

async function scrapeGemini() {
  const logger = new ProgressLogger('Gemini Newsletter 抓取');
  logger.initSteps(6);
  
  setupDebugDir();
  
  // 步骤 1：检查运行状态
  logger.startStep(1, '检查运行状态', '检查是否首次运行');
  const isFirstRun = !fs.existsSync(CONFIG.CHROME_USER_DATA_DIR);
  if (isFirstRun) {
    logger.warn('首次运行，需要手动登录 Gemini');
    logger.info('登录后，登录状态会自动保存，以后无需重复登录');
  } else {
    logger.success('使用已保存的登录状态');
    // 清理可能存在的锁定文件
    logger.detail('清理 Chrome 锁定文件...');
    cleanupChromeLocks();
  }
  logger.completeStep();
  
  let context;
  try {
    // 步骤 2：启动浏览器
    logger.startStep(2, '启动浏览器', '初始化 Chromium 浏览器');
    
    context = await chromium.launchPersistentContext(CONFIG.CHROME_USER_DATA_DIR, {
      headless: CONFIG.HEADLESS,
      slowMo: CONFIG.SLOW_MO,
      args: [
        '--disable-blink-features=AutomationControlled',
        '--no-first-run',
        '--no-default-browser-check',
      ],
      userAgent: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36'
    });
    
    const page = context.pages()[0] || await context.newPage();
    logger.success('浏览器已启动');
    logger.completeStep();
    
    // 步骤 3：导航到 Gemini
    logger.startStep(3, '导航到 Gemini', '访问 https://gemini.google.com');
    await page.goto('https://gemini.google.com', { waitUntil: 'domcontentloaded', timeout: 30000 });
    await page.waitForTimeout(5000);  // 增加等待时间，让页面充分加载
    
    // 检查是否需要登录 - 同时检查 URL 和页面内容
    const currentUrl = page.url();
    const hasSignInButton = await page.locator('text="Sign in"').first().isVisible({ timeout: 2000 }).catch(() => false);
    
    if (currentUrl.includes('accounts.google.com') || currentUrl.includes('signin') || hasSignInButton) {
      logger.warn('🔐 检测到需要登录 Google 账号');
      logger.info('');
      logger.info('╔════════════════════════════════════════════════════════════╗');
      logger.info('║  请在打开的浏览器中完成登录操作                              ║');
      logger.info('║                                                            ║');
      logger.info('║  操作步骤：                                                 ║');
      logger.info('║  1. 点击浏览器中的 "Sign in" 按钮                           ║');
      logger.info('║  2. 输入你的 Google 账号和密码                              ║');
      logger.info('║  2. 完成两步验证（如果需要）                                ║');
      logger.info('║  3. 等待页面跳转回 Gemini 主页                              ║');
      logger.info('║  4. 看到 Gemini 对话界面后，回到终端                         ║');
      logger.info('║  5. 按回车键继续脚本执行                                     ║');
      logger.info('║                                                            ║');
      logger.info('║  💡 提示：登录信息会被保存，下次运行无需重复登录              ║');
      logger.info('╚════════════════════════════════════════════════════════════╝');
      logger.info('');
      
      // 使用 readline 提供更友好的等待提示
      const rl = require('readline').createInterface({
        input: process.stdin,
        output: process.stdout
      });
      
      await new Promise(resolve => {
        rl.question('👉 完成登录后，按回车继续...', () => {
          rl.close();
          resolve();
        });
      });
      
      logger.info('验证登录状态...');
      await page.waitForTimeout(3000);
      
      // 验证是否已成功登录（检查 Sign in 按钮是否消失）
      const stillNeedsLogin = await page.locator('text="Sign in"').first().isVisible({ timeout: 2000 }).catch(() => false);
      if (stillNeedsLogin) {
        logger.warn('⚠️ 仍检测到 "Sign in" 按钮，请确认已完成登录');
        throw new Error('登录失败：请重新运行脚本并完成登录');
      }
      
      logger.success('✅ 登录成功！登录状态已保存');
    } else {
      logger.success('已登录');
    }
    
    await debugPause(page, '01-gemini-loaded', '已打开 Gemini，检查是否成功登录');
    logger.completeStep();
    
    // 步骤 4：确保侧边栏展开
    logger.startStep(4, '展开侧边栏', '确保对话列表可见');
    
    await page.waitForTimeout(2000);
    
    const checkSidebarOpen = async () => {
      const nav = page.locator('nav').first();
      if (await nav.isVisible({ timeout: 500 }).catch(() => false)) {
        const box = await nav.boundingBox().catch(() => null);
        if (box && box.width > 100) {
          return true;
        }
      }
      
      // 方法2：检查对话列表是否可见
      const chatList = page.locator('[class*="conversation"], [class*="chat-list"]').first();
      if (await chatList.isVisible({ timeout: 500 }).catch(() => false)) {
        return true;
      }
      
      return false;
    };
    
    let sidebarOpen = await checkSidebarOpen();
    console.log(`   侧边栏当前状态: ${sidebarOpen ? '已展开' : '未展开'}`);
    
    if (!sidebarOpen) {
      console.log('   点击☰菜单按钮...');
      
      const menuBtn = page.locator('button[aria-label="主菜单"], button[aria-label="Main menu"]').first();
      if (await menuBtn.isVisible({ timeout: 1000 }).catch(() => false)) {
        await menuBtn.click();
        console.log('   ✓ 点击了菜单按钮');
      } else {
        // 坐标点击备用
        console.log('   使用坐标点击...');
        await page.mouse.click(30, 30);
      }
      
      // 等待菜单动画完成
      await page.waitForTimeout(2000);
      
      // 再次检查
      sidebarOpen = await checkSidebarOpen();
      console.log(`   点击后侧边栏状态: ${sidebarOpen ? '已展开' : '仍未展开'}`);
    }
    
    await debugPause(page, '02-menu-expanded', '检查侧边栏是否展开并显示历史对话');
    
    // === 步骤 3：等待侧边栏 ===
    console.log('⏳ [步骤 3/6] 等待侧边栏加载...');
    await page.waitForTimeout(3000);  // 增加等待时间
    await debugPause(page, '03-sidebar-loaded', '侧边栏已加载，检查历史对话');
    
    // === 步骤 4：查找对话 ===
    console.log(`🔍 [步骤 4/6] 查找对话：「${CONFIG.TARGET_CHAT_TITLE}」...`);
    
    const titleMatcher = new TitleMatcher();
    
    let targetChat = null;
    let found = false;
    
    // 多次尝试查找（侧边栏可能还在加载）
    for (let attempt = 0; attempt < 3 && !found; attempt++) {
      if (attempt > 0) {
        console.log(`   第 ${attempt + 1} 次尝试...`);
        await page.waitForTimeout(2000);
      }
      
      // 策略1：直接文字匹配（精确）
      try {
        targetChat = page.locator(`text="${CONFIG.TARGET_CHAT_TITLE}"`).first();
        if (await targetChat.isVisible({ timeout: 2000 })) {
          found = true;
          console.log('   ✓ 找到对话（精确匹配）');
        }
      } catch (e) {
        // 继续尝试
      }
      
      // 策略2：模糊匹配（包含关键词）
      if (!found) {
        try {
          // 尝试部分匹配
          const keywords = ['AI 行业情报', 'AI行业情报', '行业情报', 'AI 行业'];
          for (const kw of keywords) {
            targetChat = page.locator(`text=${kw}`).first();
            if (await targetChat.isVisible({ timeout: 1000 })) {
              found = true;
              console.log(`   ✓ 找到对话（关键词: ${kw}）`);
              break;
            }
          }
        } catch (e) {
          // 继续
        }
      }
      
      // 策略3：遍历所有可点击元素，使用标题匹配工具
      if (!found) {
        const allElements = page.locator('a, button, [role="button"], [role="listitem"], [class*="item"]');
        const count = await allElements.count();
        
        if (count > 0) {
          // 显示前10个元素帮助调试
          if (attempt === 0) {
            console.log(`\n   📋 侧边栏元素样本 (共 ${count} 个):`);
            for (let i = 0; i < Math.min(count, 15); i++) {
              const text = await allElements.nth(i).textContent().catch(() => '');
              const trimmed = text.trim().replace(/\s+/g, ' ').substring(0, 60);
              if (trimmed.length > 5) {
                console.log(`      [${i}] "${trimmed}${text.length > 60 ? '...' : ''}"`);
              }
            }
            console.log('');
          }
          
          // 收集所有对话标题
          const titles = [];
          for (let i = 0; i < Math.min(count, 100); i++) {
            const text = await allElements.nth(i).textContent().catch(() => '');
            const trimmed = text.trim().replace(/\s+/g, ' ');
            if (trimmed.length > 5 && trimmed.length < 100) {
              titles.push({ text: trimmed, element: allElements.nth(i) });
            }
          }
          
          // 使用标题匹配工具查找最佳匹配
          if (titles.length > 0) {
            const titleTexts = titles.map(t => t.text);
            const bestMatch = titleMatcher.getBestMatch(titleTexts, CONFIG.TARGET_CHAT_TITLE, 0.6);
            
            if (bestMatch) {
              const matchedTitle = titles.find(t => t.text === bestMatch.title);
              if (matchedTitle) {
                targetChat = matchedTitle.element;
                found = true;
                console.log(`   ✓ 发现匹配: "${bestMatch.title}" (匹配度: ${(bestMatch.score * 100).toFixed(0)}%, 方法: ${bestMatch.method})`);
              }
            }
          }
        }
      }
    }
    
    if (!found) {
      await debugPause(page, '04-chat-not-found', '❌ 未找到目标对话，检查侧边栏');
      throw new Error('未找到对话');
    }
    
    await debugPause(page, '04-chat-found', '已定位到目标对话，准备点击');
    
    // === 步骤 5：进入对话 ===
    console.log('📂 [步骤 5/6] 点击进入对话...');
    
    // 打印目标元素信息帮助调试
    const tagName = await targetChat.evaluate(el => el.tagName);
    const className = await targetChat.evaluate(el => el.className);
    console.log(`   目标元素: <${tagName}> class="${className}"`);
    
    // 点击对话
    await targetChat.click({ force: true });
    console.log('⏳ 等待内容加载（10秒）...');
    await page.waitForTimeout(10000);
    
    // 检查URL是否变化（进入对话后URL通常会变）
    const conversationUrl = page.url();
    console.log(`   当前URL: ${conversationUrl}`);
    
    // 验证是否成功进入对话
    const pageContent = await page.evaluate(() => {
      // 尝试只获取main区域的内容
      const main = document.querySelector('main');
      return main ? main.innerText.length : document.body.innerText.length;
    });
    console.log(`   内容区域文本长度: ${pageContent} 字符`);
    
    // 如果内容太短，可能需要点击对话列表中的实际链接
    if (pageContent < 500) {
      console.log('   ⚠️ 内容太短，尝试查找并点击对话链接...');
      
      // 在侧边栏中找到对话项的实际可点击链接
      const conversationLink = page.locator(`a:has-text("${CONFIG.TARGET_CHAT_TITLE}"), [role="link"]:has-text("${CONFIG.TARGET_CHAT_TITLE}")`).first();
      if (await conversationLink.isVisible({ timeout: 2000 })) {
        await conversationLink.click({ force: true });
        console.log('   ✓ 点击了对话链接');
        await page.waitForTimeout(8000);
      }
    }
    
    await debugPause(page, '05-conversation-loaded', '对话已打开，检查是否看到完整内容');
    
    // === 步骤 6：滚动到底部并提取最新回复 ===
    console.log('📝 [步骤 6/6] 提取最新AI回复...');
    
    // 等待初始内容加载
    console.log('   等待对话内容加载...');
    await page.waitForTimeout(3000);
    
    // ========================================
    // 核心改进：滚动到底部，只提取最后一条 AI 回复
    // ========================================
    console.log('   滚动到页面底部查看最新回复...');
    
    // 多次滚动确保到底并加载完整内容
    for (let i = 0; i < 3; i++) {
      await page.keyboard.press('End');
      await page.evaluate(() => {
        window.scrollTo(0, document.body.scrollHeight);
        const main = document.querySelector('main');
        if (main) main.scrollTop = main.scrollHeight;
      });
      await page.waitForTimeout(1500);
    }
    
    console.log('   定位最后一条 AI 回复消息...');
    
    // 在浏览器中查找并提取完整的HTML内容，然后转换为Markdown
    const aiReport = await page.evaluate(() => {
      // ========================================
      // 核心改进：精确定位最后一条 AI 回复消息
      // ========================================
      
      // 辅助函数：将HTML元素转换为Markdown
      function htmlToMarkdown(element) {
        function processNode(node) {
          if (node.nodeType === Node.TEXT_NODE) {
            return node.textContent;
          }
          
          if (node.nodeType !== Node.ELEMENT_NODE) {
            return '';
          }
          
          const tag = node.tagName.toLowerCase();
          const children = Array.from(node.childNodes).map(processNode).join('');
          
          switch (tag) {
            case 'a':
              const href = node.getAttribute('href');
              if (href && !href.startsWith('javascript:')) {
                const fullHref = href.startsWith('http') ? href : (href.startsWith('/') ? `https://gemini.google.com${href}` : href);
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
            case 'tr':
              return children + '\n';
            case 'th':
            case 'td':
              return children + ' | ';
            default:
              return children;
          }
        }
        
        return processNode(element);
      }
      
      // 隐藏侧边栏避免干扰
      const elementsToHide = ['nav', 'aside', '[role="navigation"]', '[class*="sidebar"]'];
      elementsToHide.forEach(selector => {
        document.querySelectorAll(selector).forEach(el => { el.style.display = 'none'; });
      });
      
      const main = document.querySelector('main');
      if (!main) {
        return { found: false, content: '', debug: 'Main element not found' };
      }
      
      // Newsletter 内容特征（用于识别正确的消息）
      const targetIntro = '这是为您精心准备的';
      const targetAlt = 'The Agentic Stack';
      const startsWithIntro = (text) => {
        const trimmed = (text || '').replace(/^\s+/, '');
        return trimmed.startsWith(targetIntro) || trimmed.startsWith(targetAlt);
      };
      
      // ========================================
      // 方法1：使用 Gemini 特定消息选择器（优先）
      // ========================================
      const messageSelectors = [
        '[data-message-author-role="model"]',
        '[data-message-id]',
        '[data-turn-id]',
        '.model-response',
        '[class*="model-turn"]',
        '[role="article"]',
        'message-content',
      ];
      
      let lastMessage = null;
      
      for (const selector of messageSelectors) {
        const messages = main.querySelectorAll(selector);
        if (messages.length === 0) continue;
        
        // 从后往前找，找到第一个包含 Newsletter 内容的消息
        for (let i = messages.length - 1; i >= 0; i--) {
          const msg = messages[i];
          const text = msg.innerText || '';
          
          // 必须是有意义的内容（>500字符）且以目标开头
          if (text.length > 500 && startsWithIntro(text)) {
            lastMessage = msg;
            console.log(`Found last AI message using ${selector}, index ${i}/${messages.length-1}, length ${text.length}`);
            break;
          }
        }
        
        if (lastMessage) break;
      }
      
      // ========================================
      // 方法2：如果选择器失败，使用文本锚点定位
      // ========================================
      if (!lastMessage) {
        console.log('Fallback: searching by text anchor...');
        
        // 获取所有可能的内容容器
        const containers = main.querySelectorAll('div, article, section');
        const candidates = [];
        
        containers.forEach(el => {
          const text = el.innerText || '';
          if (text.length > 1000 && startsWithIntro(text)) {
            // 计算距离页面底部的位置
            const rect = el.getBoundingClientRect();
            candidates.push({
              element: el,
              length: text.length,
              bottom: rect.bottom
            });
          }
        });
        
        if (candidates.length > 0) {
          // 优先选择最靠近页面底部且内容最长的
          candidates.sort((a, b) => {
            // 先按位置排序（越靠近底部越好）
            if (Math.abs(a.bottom - b.bottom) > 100) {
              return b.bottom - a.bottom;
            }
            // 位置相近时选择内容最短的（避免选到父容器）
            return a.length - b.length;
          });
          
          lastMessage = candidates[0].element;
          console.log(`Found via text anchor, ${candidates.length} candidates, selected length ${candidates[0].length}`);
        }
      }
      
      if (!lastMessage) {
        return { found: false, content: '', debug: 'Could not find any AI message with Newsletter content' };
      }
      
      // 提取并清理内容
      let markdownContent = htmlToMarkdown(lastMessage);
      const textContent = lastMessage.innerText || '';
      
      // 验证内容以目标开头
      const normalizedText = textContent.replace(/^\s+/, '');
      if (!startsWithIntro(normalizedText)) {
        return { found: false, content: '', debug: 'Extracted content does not start with expected intro text' };
      }
      
      // 根据常见结束标记裁剪多余内容
      const endMarkers = [
        'Gemini 的回答未必正确无误',
        '本报告已同步发送至',
        '如果您对如何利用这些最新的 AI 工具提升个人或团队效率感兴趣',
        '如需任何帮助',
        '\n\n---\n\n如有'
      ];
      
      let endIndex = markdownContent.length;
      for (const marker of endMarkers) {
        const idx = markdownContent.indexOf(marker);
        if (idx !== -1 && idx < endIndex) {
          endIndex = idx;
        }
      }
      markdownContent = markdownContent.substring(0, endIndex).trim();
      
      // 清理多余空行
      markdownContent = markdownContent.replace(/\n{3,}/g, '\n\n');
      
      return {
        found: true,
        content: markdownContent,
        debug: `Extracted ${markdownContent.length} chars from last AI message`
      };
    });
    
    console.log(`   调试信息: ${aiReport.debug}`);
    
    const content = aiReport.content || '';
    
    console.log(`\n✅ 提取到 ${content.length} 字符`);
    console.log('\n--- 内容预览 (前 800 字符) ---');
    console.log(content.substring(0, 800));
    console.log('--- 预览结束 ---\n');
    
    await debugPause(page, '06-content-extracted', '内容已提取，检查是否为完整AI简报');
    
    // 保存报告
    if (content.length > 100) {
      const dateStr = getDateString();
      saveReport(content, dateStr);
      console.log('\n🎉 抓取完成！');
    } else {
      console.warn('\n⚠️  提取内容太短，可能不完整');
    }
    
    // 调试模式下不自动关闭浏览器
    if (CONFIG.DEBUG_MODE) {
      console.log('\n💡 调试模式：浏览器保持打开');
      console.log('   你可以手动检查页面，完成后按回车关闭...\n');
      
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
    
  } catch (error) {
    console.error('\n❌ 抓取失败：', error.message);
    
    if (CONFIG.DEBUG_MODE && error.message !== '未找到对话') {
      console.log('\n💡 调试模式：浏览器保持打开以便检查错误');
      console.log('   按回车关闭...\n');
      
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
    
    throw error;
    
  } finally {
    if (context) {
      await context.close();
      console.log('浏览器已关闭');
    }
  }
}

// ==================== 入口 ====================
scrapeGemini().catch(() => {
  process.exit(1);
});
