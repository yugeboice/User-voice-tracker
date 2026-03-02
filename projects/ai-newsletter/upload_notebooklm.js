/**
 * AI Radar - NotebookLM 上传脚本
 * 
 * 使用持久化登录上传合并报告到 NotebookLM
 */

const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const readline = require('readline');
const { TitleMatcher, ProgressLogger } = require('./utils');

// ==================== 配置 ====================
const DEBUG_MODE = process.argv.includes('--debug'); // 调试模式：node upload_notebooklm.js --debug

const CONFIG = {
  // NotebookLM URL（直接进入目标笔记本）
  NOTEBOOKLM_URL: 'https://notebooklm.google.com',
  
  // 目标笔记本名称
  TARGET_NOTEBOOK: 'AI 行业情报',
  
  // 独立的 Chrome 用户数据目录
  CHROME_USER_DATA_DIR: path.join(__dirname, 'chrome-data-notebooklm'),
  
  // 报告目录
  REPORTS_DIR: path.join(__dirname, 'reports'),
  
  // 浏览器配置
  HEADLESS: false,
  SLOW_MO: 300,
  
  // 超时配置
  NAVIGATION_TIMEOUT: 60000,
};

// ==================== 工具函数 ====================

function getDateString() {
  const now = new Date();
  const year = now.getFullYear();
  const month = String(now.getMonth() + 1).padStart(2, '0');
  const day = String(now.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

function logStatus(status, message) {
  const logFile = path.join(__dirname, 'STATUS.log');
  const timestamp = new Date().toISOString();
  const logLine = `[${timestamp}] [NotebookLM] ${status}: ${message}\n`;
  fs.appendFileSync(logFile, logLine);
}

// ==================== 主逻辑 ====================

async function uploadToNotebookLM(filePath) {
  const logger = new ProgressLogger('NotebookLM 上传');
  logger.initSteps(5);
  
  // 步骤 1：检查文件
  logger.startStep(1, '检查报告文件', '验证合并报告是否存在');
  
  if (!filePath) {
    const dateStr = getDateString();
    filePath = path.join(CONFIG.REPORTS_DIR, `combined-${dateStr}.md`);
  }
  
  if (!fs.existsSync(filePath)) {
    logger.error(`文件不存在：${filePath}`);
    logger.info('请先运行 node merge_reports.js 合并报告');
    logger.finish(false);
    return false;
  }
  
  logger.success(`文件已找到`);
  logger.detail(`文件路径: ${filePath}`);
  const fileSize = fs.statSync(filePath).size;
  logger.detail(`文件大小: ${(fileSize / 1024).toFixed(2)} KB`);
  logger.completeStep();
  
  // 步骤 2：检查运行状态
  logger.startStep(2, '检查运行状态', '检查是否首次运行');
  const isFirstRun = !fs.existsSync(CONFIG.CHROME_USER_DATA_DIR);
  if (isFirstRun) {
    logger.warn('首次运行，需要手动登录 Google 账号');
  } else {
    logger.success('使用已保存的登录状态');
  }
  logger.completeStep();
  
  let browser;
  try {
    // 步骤 3：启动浏览器
    logger.startStep(3, '启动浏览器', '初始化 Chromium 浏览器');
    browser = await chromium.launchPersistentContext(CONFIG.CHROME_USER_DATA_DIR, {
      headless: CONFIG.HEADLESS,
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
    
    // 步骤 4：导航到 NotebookLM
    logger.startStep(4, '导航到 NotebookLM', '访问 ' + CONFIG.NOTEBOOKLM_URL);
    await page.goto(CONFIG.NOTEBOOKLM_URL, {
      waitUntil: 'domcontentloaded',
      timeout: CONFIG.NAVIGATION_TIMEOUT,
    });
    
    await page.waitForTimeout(3000);
    
    const currentUrl = page.url();
    const needLogin = currentUrl.includes('accounts.google') || isFirstRun;
    
    if (needLogin) {
      logger.warn('检测到需要登录');
      logger.info('请在浏览器中完成 Google 登录...');
      
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
      
      await page.waitForTimeout(2000);
      logger.success('登录完成');
    } else {
      logger.success('已登录');
    }
    logger.completeStep();
    
    logger.info('等待页面加载完成...');
    await page.waitForTimeout(10000);
    
    // 查找并进入目标笔记本
    console.log(`🔍 查找笔记本：「${CONFIG.TARGET_NOTEBOOK}」...`);
    
    // 检查当前页面 URL 或内容判断是否在笔记本列表页
    const currentPageUrl = page.url();
    const isNotebookList = currentPageUrl === 'https://notebooklm.google.com/' || 
                           currentPageUrl === 'https://notebooklm.google.com' ||
                           await page.locator('text="我的笔记本"').first().isVisible({ timeout: 2000 }).catch(() => false) ||
                           await page.locator('text="新建笔记本"').first().isVisible({ timeout: 2000 }).catch(() => false);
    
    if (isNotebookList) {
      console.log('   当前在笔记本列表页，需要点击进入笔记本...');
      
      let clicked = false;
      
      // 使用调试工具获取的选择器：project-button > mat-card > button
      // 找到包含目标笔记本名称的卡片
      try {
        const allCards = page.locator('project-button');
        const count = await allCards.count();
        console.log(`   找到 ${count} 个笔记本卡片`);
        
        // 收集所有笔记本标题
        const notebooks = [];
        for (let i = 0; i < count; i++) {
          const text = await allCards.nth(i).textContent().catch(() => '');
          const trimmed = text.trim().replace(/\s+/g, ' ');
          notebooks.push({ text: trimmed, index: i, element: allCards.nth(i) });
          console.log(`   卡片 ${i}: "${trimmed.substring(0, 40)}..."`);
        }
        
        // 使用模糊匹配查找目标笔记本
        if (notebooks.length > 0) {
          const titleMatcher = new TitleMatcher();
          const notebookTitles = notebooks.map(n => n.text);
          const bestMatch = titleMatcher.getBestMatch(notebookTitles, CONFIG.TARGET_NOTEBOOK, 0.6);
          
          if (bestMatch) {
            const matchedNotebook = notebooks.find(n => n.text === bestMatch.title);
            if (matchedNotebook) {
              console.log(`   ✓ 找到匹配笔记本: "${bestMatch.title}" (匹配度: ${(bestMatch.score * 100).toFixed(0)}%)`);
              // 点击卡片内的 button
              const cardButton = matchedNotebook.element.locator('button.primary-action-button').first();
              await cardButton.click();
              // 等待跳转到笔记本详情页（不增加固定等待）
              await Promise.race([
                page.waitForURL('**/project/**', { timeout: 10000 }).catch(() => {}),
                page.waitForSelector('button, [role="button"]', { timeout: 6000 }).catch(() => {})
              ]);
              clicked = true;
            }
          } else {
            console.log(`   ⚠️  未找到匹配度 >= 60% 的笔记本`);
          }
        }
      } catch (e) {
        console.log('   遍历卡片出错:', e.message);
      }
      
      if (!clicked) {
        await page.screenshot({ path: 'debug-notebook-list.png' });
        throw new Error(`未找到笔记本「${CONFIG.TARGET_NOTEBOOK}」，请检查名称`);
      }
    } else {
      console.log('   ✓ 已在笔记本内');
    }
    
    // 点击"添加来源"按钮 - 新 UI 已确认
    console.log('📤 点击"添加来源"...');
    
    // 新 UI (2026-01) 使用 Material Design 按钮
    // 调试结果确认：[aria-label="添加来源"] 是最可靠的选择器
    const addSourceSelectors = [
      // 新 UI 选择器（优先级最高）
      '[aria-label="添加来源"]',
      'button[aria-label="添加来源"]',
      'button:has-text("add 添加来源")',
      'button[class*="add-source-button"]',
      // 备用选择器
      '[aria-label*="添加来源"]',
      'button:has-text("添加来源")',
      'button:has-text("+ 添加来源")',
      // 通用 Material Design 按钮
      'button[class*="mdc-button"][class*="add-source"]',
      // 旧版本支持
      'role=button[name*="添加来源" i]',
      'text="添加来源"',
    ];
    
    let addSourceClicked = false;
    
    for (const selector of addSourceSelectors) {
      try {
        const btn = page.locator(selector).first();
        if (await btn.isVisible({ timeout: 2000 }).catch(() => false)) {
          await btn.click();
          console.log(`   ✓ 已点击添加来源`);
          await page.waitForTimeout(2000);
          addSourceClicked = true;
          break;
        }
      } catch (e) {
        continue;
      }
    }
    
    if (!addSourceClicked) {
      // 截图帮助调试
      const debugScreenshot = path.join(__dirname, 'debug-notebooklm-no-add-button.png');
      await page.screenshot({ path: debugScreenshot, fullPage: true });
      logger.warn(`未找到"添加来源"按钮，已保存调试截图`);
      logger.detail(`截图: ${debugScreenshot}`);
      
      // 收集页面信息供调试
      const pageInfo = await page.evaluate(() => {
        const buttons = Array.from(document.querySelectorAll('button')).map((btn, i) => ({
          i,
          text: btn.textContent.trim().substring(0, 40),
          ariaLabel: btn.getAttribute('aria-label') || '',
          class: btn.className.substring(0, 60),
        })).filter(b => b.text || b.ariaLabel);
        
        return {
          title: document.title,
          url: window.location.href,
          buttons: buttons.slice(0, 30),
        };
      });
      
      logger.warn(`页面信息：`);
      logger.detail(`URL: ${pageInfo.url}`);
      logger.detail(`可见按钮 (前30个)：`);
      pageInfo.buttons.forEach(b => {
        logger.detail(`  [${b.i}] "${b.text}" (aria-label: "${b.ariaLabel}")`);
      });
      
      throw new Error('未找到"添加来源"按钮。UI 可能已改变，请查看调试截图和按钮列表。');
    }
    
    // 等待上传对话框出现
    console.log('⏳ 等待上传对话框...');
    await page.waitForTimeout(2000);
    
    // 等待上传对话框或文件选择器出现
    console.log('⏳ 等待上传界面...');
    await page.waitForTimeout(1500);
    
    // 查找文件上传方式 - 新 UI 可能有不同的上传入口
    console.log('📎 上传文件...');
    
    let uploadSuccess = false;
    
    // 方法1：查找 input[type="file"] 并直接设置文件
    try {
      const fileInput = page.locator('input[type="file"]').first();
      if (await fileInput.isVisible({ timeout: 2000 }).catch(() => false)) {
        await fileInput.setInputFiles(filePath);
        console.log('   ✓ 文件已通过 input[type="file"] 上传');
        uploadSuccess = true;
      }
    } catch (e) {
      // 继续尝试其他方法
    }
    
    // 方法2：查找"选择文件"/"上传文件"链接或按钮
    if (!uploadSuccess) {
      const selectFileSelectors = [
        // 新 UI 发现的选择器
        'button:has-text("upload上传文件")',
        'button:has-text("上传文件")',
        'button:has-text("上传")',
        // 旧选择器
        'button:has-text("选择文件")',
        'a:has-text("选择文件")',
        'text="选择文件"',
        'text="上传文件"',
        'button[aria-label*="文件"]',
        'button[aria-label*="上传"]',
        'a[aria-label*="选择"]',
        'button[class*="upload"]',
      ];
      
      for (const selector of selectFileSelectors) {
        try {
          const link = page.locator(selector).first();
          if (await link.isVisible({ timeout: 1500 }).catch(() => false)) {
            // 使用 filechooser 事件处理
            const [fileChooser] = await Promise.all([
              page.waitForEvent('filechooser', { timeout: 5000 }),
              link.click(),
            ]);
            await fileChooser.setFiles(filePath);
            console.log('   ✓ 文件已通过选择器上传');
            uploadSuccess = true;
            break;
          }
        } catch (e) {
          continue;
        }
      }
    }
    
    if (!uploadSuccess) {
      // 尝试查找所有可交互的上传相关元素
      const allUploadElements = await page.evaluate(() => {
        const elements = [];
        document.querySelectorAll('button, a, input').forEach((el, i) => {
          const text = el.textContent?.toLowerCase() || '';
          const ariaLabel = el.getAttribute('aria-label')?.toLowerCase() || '';
          const type = el.getAttribute('type') || el.tagName;
          
          if (text.includes('file') || text.includes('upload') || 
              text.includes('选择') || text.includes('上传') ||
              ariaLabel.includes('file') || ariaLabel.includes('upload')) {
            elements.push({
              i,
              tag: el.tagName,
              text: el.textContent?.substring(0, 30) || '',
              ariaLabel: ariaLabel.substring(0, 40),
              type,
            });
          }
        });
        return elements;
      });
      
      if (allUploadElements.length > 0) {
        logger.warn('⚠️ 未找到直接的文件上传方式，但发现相关元素：');
        allUploadElements.forEach(el => {
          logger.detail(`  <${el.tag}> "${el.text}" (aria-label: "${el.ariaLabel}")`);
        });
      }
      
      throw new Error('未找到文件上传方式。UI 可能已改变。');
    }
    
    // 等待上传完成
    logger.info('等待上传处理...');
    await page.waitForTimeout(5000);
    
    // 验证上传成功（检查来源列表是否有新项目）
    const sourceUploaded = await page.locator('text="combined"').first().isVisible({ timeout: 10000 }).catch(() => false) ||
                           await page.locator('[class*="source"]').count() > 0;
    
    if (sourceUploaded) {
      logger.success('文件已添加到 NotebookLM');
      logStatus('SUCCESS', `文件已上传：${filePath}`);
    } else {
      logger.warn('未能验证上传成功，继续执行其他步骤...');
    }
    
    // 截图留证
    const screenshotPath = path.join(__dirname, 'notebooklm-uploaded.png');
    await page.screenshot({ path: screenshotPath });
    console.log(`📸 截图已保存：${screenshotPath}`);
    
    // ==================== 选择来源 ====================
    console.log('\n📋 [步骤4] 选择来源...');
    
    // 调试模式：扫描并打印所有复选框相关元素
    if (DEBUG_MODE) {
      console.log('\n🔍 [调试模式] 扫描复选框元素...\n');
      
      // 扫描所有 mat-checkbox 元素（Material Design 复选框）
      const checkboxes = page.locator('mat-checkbox, [role="checkbox"], input[type="checkbox"]');
      const cbCount = await checkboxes.count();
      console.log(`   找到 ${cbCount} 个复选框元素：\n`);
      
      for (let i = 0; i < cbCount; i++) {
        const cb = checkboxes.nth(i);
        const isVisible = await cb.isVisible().catch(() => false);
        if (isVisible) {
          const ariaLabel = await cb.getAttribute('aria-label').catch(() => '');
          const text = await cb.textContent().catch(() => '');
          const className = await cb.evaluate(el => el.className).catch(() => '');
          const id = await cb.getAttribute('id').catch(() => '');
          
          console.log(`   [${i}] 可见复选框:`);
          if (ariaLabel) console.log(`       aria-label: "${ariaLabel}"`);
          if (text) console.log(`       text: "${text.trim().substring(0, 50)}"`);
          if (id) console.log(`       id: "${id}"`);
          console.log(`       class: "${className.substring(0, 80)}"`);
          console.log('');
        }
      }
      
      // 扫描来源列表项
      console.log('\n   扫描来源列表项：\n');
      const sourceItems = page.locator('[class*="source-item"], [class*="corpus-item"]');
      const siCount = await sourceItems.count();
      console.log(`   找到 ${siCount} 个来源项：\n`);
      
      for (let i = 0; i < siCount; i++) {
        const si = sourceItems.nth(i);
        const isVisible = await si.isVisible().catch(() => false);
        if (isVisible) {
          const text = await si.textContent().catch(() => '');
          const className = await si.evaluate(el => el.className).catch(() => '');
          console.log(`   [${i}] "${text.trim().substring(0, 50)}"`);
          console.log(`       class: "${className}"`);
          console.log('');
        }
      }
      
      console.log('\n   ⌨️  请在浏览器中手动点击复选框测试，完成后按回车继续...');
      console.log('   （如果卡住请直接按回车跳过）\n');
      
      // 设置超时，避免无限等待
      await Promise.race([
        new Promise(resolve => {
          const rl2 = readline.createInterface({ input: process.stdin, output: process.stdout });
          rl2.question('', () => { rl2.close(); resolve(); });
        }),
        page.waitForTimeout(60000) // 60秒超时
      ]);
      
      console.log('   ✓ 继续执行...\n');
    }
    
    console.log('   [4.1] 尝试取消选择所有来源...');
    
    // 根据调试输出，复选框结构：
    // - mat-checkbox 容器: #mat-mdc-checkbox-0
    // - input 元素: #mat-mdc-checkbox-0-input, aria-label="选择所有来源"
    
    let selectAllHandled = false;
    
    try {
      // 方法1: 直接用 ID 选择器点击 "选择所有来源" 复选框容器
      const selectAllContainer = page.locator('#mat-mdc-checkbox-0').first();
      if (await selectAllContainer.isVisible({ timeout: 3000 })) {
        // 检查内部 input 的 checked 状态
        const selectAllInput = page.locator('#mat-mdc-checkbox-0-input');
        const isChecked = await selectAllInput.isChecked().catch(() => false);
        console.log(`   "选择所有来源" 当前状态: checked=${isChecked}`);
        
        if (isChecked) {
          // 已勾选，点击取消
          await selectAllContainer.click();
          console.log('   ✓ 已取消选择所有来源');
          await page.waitForTimeout(1000);
        } else {
          // 未勾选，先勾选再取消，确保全部取消
          await selectAllContainer.click();
          await page.waitForTimeout(500);
          await selectAllContainer.click();
          console.log('   ✓ 已取消选择所有来源（通过双击）');
          await page.waitForTimeout(1000);
        }
        selectAllHandled = true;
      }
    } catch (e) {
      console.log('   ⚠️  方法1(ID选择器)失败:', e.message);
    }
    
    if (!selectAllHandled) {
      try {
        // 方法2: 通过 aria-label 找 input，然后点击其父级 mat-checkbox
        const selectAllInput = page.locator('input[aria-label="选择所有来源"]').first();
        if (await selectAllInput.isVisible({ timeout: 2000 })) {
          // 点击 input 的父级 mat-checkbox 容器
          const parentCheckbox = page.locator('input[aria-label="选择所有来源"]').locator('xpath=ancestor::mat-checkbox');
          await parentCheckbox.click();
          await page.waitForTimeout(500);
          await parentCheckbox.click();
          console.log('   ✓ 已取消选择所有来源（通过aria-label）');
          await page.waitForTimeout(1000);
          selectAllHandled = true;
        }
      } catch (e) {
        console.log('   ⚠️  方法2(aria-label)失败:', e.message);
      }
    }
    
    if (!selectAllHandled) {
      try {
        // 方法3: 通过 class 选择器
        const selectAllByClass = page.locator('.select-checkbox-all-sources').first();
        if (await selectAllByClass.isVisible({ timeout: 2000 })) {
          await selectAllByClass.click();
          await page.waitForTimeout(500);
          await selectAllByClass.click();
          console.log('   ✓ 已取消选择所有来源（通过class）');
          await page.waitForTimeout(1000);
          selectAllHandled = true;
        }
      } catch (e) {
        console.log('   ⚠️  方法3(class)失败:', e.message);
      }
    }
    
    if (!selectAllHandled) {
      console.log('   ⚠️  未找到"选择所有来源"复选框，尝试直接选择文件');
    }
    
    console.log('   [4.2] 选择上传的文件...');
    
    // 选择刚上传的文件 - 根据调试输出，文件的 aria-label 在 input 元素上
    const dateStr = getDateString();
    const uploadedFileName = `combined-${dateStr}.md`;
    let fileSelected = false;
    
    try {
      // 方法1: 通过 aria-label 找文件的 input，然后点击其父级 mat-checkbox
      console.log(`   查找 input[aria-label="${uploadedFileName}"]...`);
      const fileInputs = page.locator(`input[aria-label="${uploadedFileName}"]`);
      const inputCount = await fileInputs.count();
      console.log(`   找到 ${inputCount} 个匹配的 input 元素`);
      
      if (inputCount > 0) {
        // 取最后一个（最新上传的）
        const lastInput = fileInputs.last();
        const isChecked = await lastInput.isChecked().catch(() => false);
        console.log(`   最新文件当前状态: checked=${isChecked}`);
        
        if (!isChecked) {
          // 点击父级 mat-checkbox 容器来切换选中状态
          const parentCheckbox = lastInput.locator('xpath=ancestor::mat-checkbox');
          await parentCheckbox.click();
          console.log(`   ✓ 已勾选文件: ${uploadedFileName}`);
          fileSelected = true;
        } else {
          console.log(`   ✓ 文件已经勾选: ${uploadedFileName}`);
          fileSelected = true;
        }
        await page.waitForTimeout(1000);
      }
    } catch (e) {
      console.log('   ⚠️  方法1(aria-label input)失败:', e.message);
    }
    
    if (!fileSelected) {
      try {
        // 方法2: 通过 class 选择器找所有 .select-checkbox，然后检查其 input 的 aria-label
        console.log('   尝试通过 .select-checkbox 选择...');
        const checkboxes = page.locator('.select-checkbox');
        const cbCount = await checkboxes.count();
        console.log(`   找到 ${cbCount} 个 .select-checkbox 元素`);
        
        for (let i = cbCount - 1; i >= 0; i--) {  // 从后往前遍历（最新的在后面）
          const cb = checkboxes.nth(i);
          const input = cb.locator('input');
          const ariaLabel = await input.getAttribute('aria-label').catch(() => '');
          
          if (ariaLabel && ariaLabel.includes('combined')) {
            const isChecked = await input.isChecked().catch(() => false);
            console.log(`   找到 combined 文件: "${ariaLabel}", checked=${isChecked}`);
            
            if (!isChecked) {
              await cb.click();
              console.log(`   ✓ 已勾选: ${ariaLabel}`);
            } else {
              console.log(`   ✓ 已经勾选: ${ariaLabel}`);
            }
            fileSelected = true;
            break;
          }
        }
      } catch (e) {
        console.log('   ⚠️  方法2(.select-checkbox)失败:', e.message);
      }
    }
    
    if (!fileSelected) {
      console.log('   ⚠️  未能选择上传的文件，将使用默认选中的来源');
    }
    
    console.log('   [4.3] 选择来源完成');
    await page.waitForTimeout(1000);
    
    // ==================== 同时生成音频和演示文稿 ====================
    console.log('\n🚀 [步骤5&6] 快速启动音频和演示文稿生成...');
    
    // 先点击音频概览并生成
    const audioSelectors = [
      '[aria-label="音频概览"]',
      'text="音频概览"',
      '.create-artifact-button-container:has-text("音频")',
    ];
    
    let audioClicked = false;
    for (const selector of audioSelectors) {
      try {
        const audioBtn = page.locator(selector).first();
        if (await audioBtn.isVisible({ timeout: 2000 })) {
          await audioBtn.click();
          console.log('   ✓ 已点击音频概览按钮');
          await page.waitForTimeout(1500); // 减少等待时间
          audioClicked = true;
          break;
        }
      } catch (e) {
        continue;
      }
    }
    
    if (audioClicked) {
      // 快速点击生成
      try {
        const genBtn = page.locator('text="生成"').first();
        if (await genBtn.isVisible({ timeout: 2000 })) {
          await genBtn.click();
          console.log('   ✓ 音频生成已启动');
        }
      } catch (e) {
        console.log('   ⚠️  未找到音频生成按钮');
      }
    } else {
      console.log('   ⚠️  未找到音频按钮');
    }
    
    // 立即点击演示文稿（不等待音频生成完成）
    await page.waitForTimeout(500); // 只等0.5秒
    
    const slideSelectors = [
      '[aria-label="演示文稿"]',
      'text="演示文稿"',
      '.create-artifact-button-container:has-text("演示")',
    ];
    
    let slideClicked = false;
    for (const selector of slideSelectors) {
      try {
        const slideBtn = page.locator(selector).first();
        if (await slideBtn.isVisible({ timeout: 2000 })) {
          await slideBtn.click();
          console.log('   ✓ 已点击演示文稿按钮');
          await page.waitForTimeout(1500);
          slideClicked = true;
          break;
        }
      } catch (e) {
        continue;
      }
    }
    
    if (slideClicked) {
      try {
        const genBtn = page.locator('text="生成"').first();
        if (await genBtn.isVisible({ timeout: 2000 })) {
          await genBtn.click();
          logger.detail('演示文稿生成已启动');
        }
      } catch (e) {
        logger.warn('未找到演示文稿生成按钮');
      }
    } else {
      logger.warn('未找到演示文稿按钮');
    }
    
    logger.info('音频和演示文稿都在后台生成中...');
    
    const finalScreenshot = path.join(__dirname, 'notebooklm-final.png');
    await page.screenshot({ path: finalScreenshot });
    logger.detail(`最终截图已保存: ${finalScreenshot}`);
    
    logger.completeStep();
    logger.finish(true);
    
    await page.waitForTimeout(2000);
    
    return true;
    
  } catch (error) {
    logger.finish(false);
    logger.error(error.message);
    logStatus('FAILED', error.message);
    throw error;
    
  } finally {
    if (browser) {
      await browser.close();
    }
  }
}

// ==================== 入口 ====================

// 支持命令行参数指定文件路径（忽略 --debug 等选项参数）
const fileArg = process.argv.find(arg => !arg.startsWith('-') && !arg.includes('node') && !arg.includes('upload_notebooklm'));
uploadToNotebookLM(fileArg).catch(() => {
  process.exit(1);
});
