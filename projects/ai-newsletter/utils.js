/**
 * AI Radar - 工具函数库
 * 
 * 提供通用工具函数，包括标题匹配、文本相似度计算等
 */

/**
 * 进度跟踪和日志工具
 */
class ProgressLogger {
  constructor(name = 'Process') {
    this.name = name;
    this.steps = [];
    this.currentStep = 0;
    this.startTime = Date.now();
  }

  /**
   * 初始化步骤
   */
  initSteps(totalSteps) {
    this.totalSteps = totalSteps;
    console.log(`\n${'='.repeat(60)}`);
    console.log(`🚀 ${this.name} 开始执行 (共 ${totalSteps} 个步骤)`);
    console.log(`${'='.repeat(60)}\n`);
  }

  /**
   * 开始一个步骤
   */
  startStep(stepNum, title, description = '') {
    this.currentStep = stepNum;
    const progress = `[${stepNum}/${this.totalSteps}]`;
    const elapsed = this._formatTime(Date.now() - this.startTime);
    console.log(`\n⏱️  ${elapsed} | ${progress} 🔄 ${title}`);
    if (description) {
      console.log(`   ${description}`);
    }
  }

  /**
   * 完成一个步骤
   */
  completeStep(message = '✅ 完成') {
    const elapsed = this._formatTime(Date.now() - this.startTime);
    const progress = `[${this.currentStep}/${this.totalSteps}]`;
    console.log(`⏱️  ${elapsed} | ${progress} ${message}`);
  }

  /**
   * 记录信息
   */
  info(message) {
    const indent = '   ';
    console.log(`${indent}ℹ️  ${message}`);
  }

  /**
   * 记录成功
   */
  success(message) {
    const indent = '   ';
    console.log(`${indent}✅ ${message}`);
  }

  /**
   * 记录警告
   */
  warn(message) {
    const indent = '   ';
    console.log(`${indent}⚠️  ${message}`);
  }

  /**
   * 记录错误
   */
  error(message) {
    const indent = '   ';
    console.log(`${indent}❌ ${message}`);
  }

  /**
   * 记录详情
   */
  detail(message) {
    const indent = '      ';
    console.log(`${indent}→ ${message}`);
  }

  /**
   * 完成所有步骤
   */
  finish(success = true) {
    const elapsed = this._formatTime(Date.now() - this.startTime);
    console.log(`\n${'='.repeat(60)}`);
    if (success) {
      console.log(`✅ ${this.name} 执行完成！ (总耗时: ${elapsed})`);
    } else {
      console.log(`❌ ${this.name} 执行失败！ (总耗时: ${elapsed})`);
    }
    console.log(`${'='.repeat(60)}\n`);
  }

  /**
   * 格式化时间
   */
  _formatTime(ms) {
    const seconds = Math.floor(ms / 1000);
    const minutes = Math.floor(seconds / 60);
    if (minutes > 0) {
      return `${minutes}m${seconds % 60}s`;
    }
    return `${seconds}s`;
  }
}

/**
 * 标题匹配工具 - 支持空格、同义词等情况
 */
class TitleMatcher {
  constructor() {
    // 定义同义词映射
    this.synonyms = {
      '行业': ['产业', '领域', 'industry', 'sector'],
      '情报': ['资讯', '新闻', '简报', 'news', 'intelligence'],
      'AI': ['人工智能', '算法', 'artificial intelligence'],
    };
  }

  /**
   * 规范化文本：移除多余空格，转换为小写
   */
  normalize(text) {
    if (!text) return '';
    return text
      .trim()
      .replace(/\s+/g, ' ')  // 多个空格变为单个空格
      .toLowerCase();
  }

  /**
   * 生成所有可能的匹配模式
   */
  generatePatterns(target = 'AI 行业情报') {
    const variations = [
      'AI 行业情报',
      'AI行业情报',
      'AI 行业',
      'AI行业',
      '行业情报',
      '行业 情报',
      '人工智能行业情报',
      '人工智能 行业 情报',
      'AI Industry',
      'AI industry intelligence',
      'industry intelligence',
    ];
    
    return [...new Set([target, ...variations])];
  }

  /**
   * 计算字符串相似度（Levenshtein距离）
   */
  similarity(str1, str2) {
    if (!str1 || !str2) return 0;
    
    const s1 = this.normalize(str1);
    const s2 = this.normalize(str2);
    
    if (s1 === s2) return 1;
    
    const longer = s1.length > s2.length ? s1 : s2;
    const shorter = s1.length > s2.length ? s2 : s1;
    
    if (longer.length === 0) return 1;
    
    const editDistance = this._levenshteinDistance(longer, shorter);
    return (longer.length - editDistance) / longer.length;
  }

  /**
   * Levenshtein 距离计算
   */
  _levenshteinDistance(str1, str2) {
    const matrix = Array(str2.length + 1).fill(null).map(() => Array(str1.length + 1).fill(0));
    
    for (let i = 0; i <= str1.length; i++) {
      matrix[0][i] = i;
    }
    
    for (let j = 0; j <= str2.length; j++) {
      matrix[j][0] = j;
    }
    
    for (let j = 1; j <= str2.length; j++) {
      for (let i = 1; i <= str1.length; i++) {
        const indicator = str1[i - 1] === str2[j - 1] ? 0 : 1;
        matrix[j][i] = Math.min(
          matrix[j][i - 1] + 1,
          matrix[j - 1][i] + 1,
          matrix[j - 1][i - 1] + indicator
        );
      }
    }
    
    return matrix[str2.length][str1.length];
  }

  /**
   * 检查标题是否匹配
   * @param {string} title - 要检查的标题
   * @param {string} target - 目标标题（默认为 'AI 行业情报'）
   * @param {number} threshold - 相似度阈值（0-1，默认0.7）
   * @returns {object} { isMatch: boolean, score: number, method: string }
   */
  isMatch(title, target = 'AI 行业情报', threshold = 0.7) {
    if (!title) return { isMatch: false, score: 0, method: 'empty' };

    const normalizedTitle = this.normalize(title);
    const normalizedTarget = this.normalize(target);

    // 方法1：完全匹配
    if (normalizedTitle === normalizedTarget) {
      return { isMatch: true, score: 1, method: 'exact' };
    }

    // 方法2：包含匹配
    if (normalizedTitle.includes(normalizedTarget) || normalizedTarget.includes(normalizedTitle)) {
      return { isMatch: true, score: 0.95, method: 'contains' };
    }

    // 方法3：移除空格后匹配
    const titleNoSpace = normalizedTitle.replace(/\s+/g, '');
    const targetNoSpace = normalizedTarget.replace(/\s+/g, '');
    if (titleNoSpace === targetNoSpace) {
      return { isMatch: true, score: 0.95, method: 'space-removed' };
    }

    // 方法4：模式匹配
    const patterns = this.generatePatterns(target);
    for (const pattern of patterns) {
      const normalizedPattern = this.normalize(pattern).replace(/\s+/g, '');
      if (titleNoSpace === normalizedPattern || titleNoSpace.includes(normalizedPattern)) {
        return { isMatch: true, score: 0.9, method: 'pattern' };
      }
    }

    // 方法5：关键词匹配（必须包含 AI、行业、情报）
    const hasAI = /\bai\b|人工智能|算法/.test(normalizedTitle);
    const hasIndustry = /行业|产业|领域|industry|sector/.test(normalizedTitle);
    const hasIntelligence = /情报|资讯|新闻|简报|intelligence|news/.test(normalizedTitle);

    if (hasAI && hasIndustry && hasIntelligence) {
      return { isMatch: true, score: 0.85, method: 'keywords' };
    }

    // 方法6：相似度计算
    const score = this.similarity(normalizedTitle, normalizedTarget);
    if (score >= threshold) {
      return { isMatch: true, score, method: 'similarity' };
    }

    return { isMatch: false, score, method: 'none' };
  }

  /**
   * 从标题列表中查找匹配项
   * @param {string[]} titles - 标题列表
   * @param {string} target - 目标标题
   * @param {number} threshold - 相似度阈值
   * @returns {object[]} 匹配结果数组
   */
  findMatches(titles, target = 'AI 行业情报', threshold = 0.7) {
    return titles
      .map((title, index) => {
        const result = this.isMatch(title, target, threshold);
        return {
          index,
          title,
          ...result
        };
      })
      .filter(result => result.isMatch)
      .sort((a, b) => b.score - a.score);
  }

  /**
   * 获取最佳匹配
   */
  getBestMatch(titles, target = 'AI 行业情报', threshold = 0.7) {
    const matches = this.findMatches(titles, target, threshold);
    return matches.length > 0 ? matches[0] : null;
  }
}

/**
 * 导出工具
 */
module.exports = {
  TitleMatcher,
  ProgressLogger,
};
