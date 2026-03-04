/**
 * 测试 ProgressLogger 效果
 */
const { ProgressLogger } = require('./utils');

async function demo() {
  const logger = new ProgressLogger('AI Radar 抓取演示');
  logger.initSteps(4);

  // 步骤 1
  logger.startStep(1, '初始化系统', '检查配置和依赖');
  logger.info('正在检查 Node.js 版本...');
  logger.detail(`Node.js: ${process.version}`);
  await new Promise(r => setTimeout(r, 800));
  logger.success('配置检查完成');
  logger.completeStep();
  
  // 步骤 2
  logger.startStep(2, '启动浏览器', '初始化 Chromium');
  logger.info('启动浏览器进程...');
  logger.detail('启动参数: --disable-blink-features=AutomationControlled');
  await new Promise(r => setTimeout(r, 800));
  logger.success('浏览器已启动');
  logger.completeStep();
  
  // 步骤 3
  logger.startStep(3, '导航到网页', '打开目标站点');
  logger.info('正在导航到 https://chatgpt.com');
  logger.detail('等待页面加载...');
  await new Promise(r => setTimeout(r, 800));
  logger.success('页面加载完成');
  logger.completeStep();
  
  // 步骤 4
  logger.startStep(4, '提取内容', '收集数据');
  logger.info('正在查找目标对话...');
  logger.detail('找到 "AI 行业情报" 对话 (匹配度: 100%, 方法: exact)');
  logger.info('等待对话加载...');
  logger.detail('找到 2543 条消息');
  logger.info('提取最后一条回复...');
  logger.success('成功提取 15842 字符');
  logger.detail('内容预览: AI 行业情报日报：本周AI行业重点新闻汇总...');
  logger.completeStep();
  
  logger.finish(true);
}

demo().catch(console.error);
