/**
 * NotebookLM 调试脚本 - 快速识别新 UI
 * 
 * 打开浏览器让用户手动操作，脚本在后台监听和记录所有事件
 * 用法：node debug_notebooklm_ui.js
 */

const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const readline = require('readline');

const CONFIG = {
  NOTEBOOKLM_URL: 'https://notebooklm.google.com',
  CHROME_USER_DATA_DIR: path.join(__dirname, 'chrome-data-notebooklm'),
  HEADLESS: false,
  SLOW_MO: 200,
};

async function debugUI() {
  console.log(`
╔════════════════════════════════════════════════════════════╗
║                                                            ║
║           🔍 NotebookLM UI 调试工具                        ║
║                                                            ║
║  操作说明：                                                 ║
║  1. 浏览器会自动打开 NotebookLM                            ║
║  2. 在浏览器中点击"添加来源"按钮                           ║
║  3. 脚本会记录你点击的元素信息                             ║
║  4. 按回车继续，脚本会输出UI选择器信息                    ║
║                                                            ║
╚════════════════════════════════════════════════════════════╝
  `);

  const browser = await chromium.launchPersistentContext(CONFIG.CHROME_USER_DATA_DIR, {
    headless: CONFIG.HEADLESS,
    slowMo: CONFIG.SLOW_MO,
  });

  const page = browser.pages()[0] || await browser.newPage();

  try {
    // 导航到 NotebookLM
    console.log('📍 导航到 NotebookLM...');
    await page.goto(CONFIG.NOTEBOOKLM_URL, { waitUntil: 'domcontentloaded', timeout: 30000 });
    await page.waitForTimeout(3000);

    console.log('\n✅ 已打开 NotebookLM');
    console.log('📌 请在浏览器中寻找并点击"添加来源"按钮...\n');

    // 注入事件监听
    const eventLog = [];
    await page.evaluate(() => {
      window.__clickedElement = null;
      
      document.addEventListener('click', (e) => {
        const target = e.target;
        const text = target.textContent?.trim().substring(0, 50) || '';
        const ariaLabel = target.getAttribute('aria-label') || '';
        const className = target.className || '';
        const id = target.id || '';
        const tagName = target.tagName;
        
        // 检查是否是"添加来源"相关的元素
        if (text.includes('添加') || text.includes('来源') || 
            text.includes('Add') || text.includes('source') ||
            text.includes('上传') || text.includes('Upload') ||
            ariaLabel.includes('添加') || ariaLabel.includes('来源')) {
          
          window.__clickedElement = {
            text,
            ariaLabel,
            className,
            id,
            tagName,
            selector: `${tagName}${id ? '#' + id : ''}${className ? '.' + className.split(' ')[0] : ''}`,
            timestamp: new Date().toISOString(),
          };
          
          console.log('✅ 检测到可能的"添加来源"按钮被点击');
        }
      }, true);
    });

    // 等待用户操作
    const rl = readline.createInterface({
      input: process.stdin,
      output: process.stdout
    });

    await new Promise(resolve => {
      rl.question('\n👆 点击了"添加来源"按钮后，按回车继续: ', () => {
        rl.close();
        resolve();
      });
    });

    // 获取点击的元素信息
    const clickedElement = await page.evaluate(() => window.__clickedElement);

    // 扫描所有按钮找到最接近的
    const allButtons = await page.evaluate(() => {
      return Array.from(document.querySelectorAll('button')).map((btn, i) => ({
        i,
        text: btn.textContent.trim().substring(0, 50),
        ariaLabel: btn.getAttribute('aria-label') || '',
        className: btn.className,
        id: btn.id,
        title: btn.getAttribute('title') || '',
        isVisible: btn.offsetParent !== null,
      })).filter(b => b.isVisible && (
        b.text.includes('添加') || b.text.includes('来源') ||
        b.text.includes('Add') || b.text.includes('source') ||
        b.ariaLabel.includes('添加') || b.ariaLabel.includes('来源')
      ));
    });

    // 生成调试报告
    const debugReport = `
╔════════════════════════════════════════════════════════════╗
║                     UI 调试报告                             ║
╚════════════════════════════════════════════════════════════╝

📸 页面信息：
  URL: ${page.url()}
  标题: ${await page.title()}

👆 检测到的点击元素：
  ${clickedElement ? `
  文本: "${clickedElement.text}"
  aria-label: "${clickedElement.ariaLabel}"
  标签: <${clickedElement.tagName}>
  class: "${clickedElement.className}"
  id: "${clickedElement.id}"
  ` : '  (未检测到点击)'}

🔍 找到的相关按钮：
${allButtons.map((btn, i) => `
  [${i}] 文本: "${btn.text}"
      aria-label: "${btn.ariaLabel}"
      class: "${btn.className}"
      id: "${btn.id}"
      title: "${btn.title}"
`).join('')}

💡 推荐的选择器：
${allButtons.map((btn, i) => {
  const selectors = [];
  if (btn.id) selectors.push(`'#${btn.id}'`);
  if (btn.ariaLabel) selectors.push(`'[aria-label="${btn.ariaLabel}"]'`);
  if (btn.text) selectors.push(`'button:has-text("${btn.text.substring(0, 20)}")'`);
  if (btn.className) {
    const classes = btn.className.split(' ').filter(c => c.length > 2);
    if (classes.length > 0) selectors.push(`'button[class*="${classes[0]}"]'`);
  }
  return `  [${i}] ${selectors.join(' OR ')}`;
}).join('\n')}

📋 所有可见按钮（包括非"添加来源"相关）：
`;

    const allVisibleButtons = await page.evaluate(() => {
      return Array.from(document.querySelectorAll('button')).map((btn, i) => ({
        i,
        text: btn.textContent.trim().substring(0, 40),
        ariaLabel: btn.getAttribute('aria-label') || '',
        className: btn.className.substring(0, 50),
        isVisible: btn.offsetParent !== null,
      })).filter(b => b.isVisible).slice(0, 30);
    });

    const debugReportFull = debugReport + allVisibleButtons.map((btn, i) => `
  [${i}] "${btn.text}" (aria-label: "${btn.ariaLabel}")`).join('');

    console.log(debugReportFull);

    // 保存报告到文件
    const reportPath = path.join(__dirname, 'debug-notebooklm-ui-report.txt');
    fs.writeFileSync(reportPath, debugReportFull);
    console.log(`\n💾 报告已保存到: ${reportPath}`);

    // 保存截图
    const screenshotPath = path.join(__dirname, 'debug-notebooklm-ui-screenshot.png');
    await page.screenshot({ path: screenshotPath, fullPage: true });
    console.log(`📸 截图已保存到: ${screenshotPath}`);

    console.log(`
✅ 调试完成！
请将上述信息提供给开发者，用于更新选择器。
    `);

  } catch (error) {
    console.error('❌ 错误:', error.message);
  } finally {
    await browser.close();
  }
}

debugUI().catch(console.error);
