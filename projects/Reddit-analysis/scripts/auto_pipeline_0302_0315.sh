#!/bin/bash
# Auto pipeline for 03-02 ~ 03-15 analysis
# This script waits for scrape to finish, then runs analyze → translate → deploy
# Progress is reported via Telegram bot

BOT_TOKEN="8279468924:AAFxUsZ-drwZEtYH7kgAxJVZJWbXTsKlZy0"
CHAT_ID="8754454930"
PROJECT_DIR="/c/Users/dorisrao/vibeprojects/lab/projects/Reddit-analysis"
VENV_PYTHON="$PROJECT_DIR/scripts/venv/Scripts/python.exe"

send_telegram() {
    curl -s -X POST "https://api.telegram.org/bot$BOT_TOKEN/sendMessage" \
        -d chat_id="$CHAT_ID" \
        -d text="$1" > /dev/null 2>&1
}

cd "$PROJECT_DIR"

# ============================================================
# Step 1: Wait for existing scrape process to finish
# ============================================================
send_telegram "🤖 Auto Pipeline 已启动 (03-02 ~ 03-15)
等待当前 scrape 进程完成..."

while true; do
    # Check if any scrape.py process is running
    SCRAPE_PID=$(tasklist 2>/dev/null | grep -i python | head -1)
    SCRAPE_RUNNING=$(wmic process where "name like '%python%'" get commandline 2>/dev/null | grep -c "scrape.py")
    if [ "$SCRAPE_RUNNING" -eq 0 ]; then
        break
    fi
    sleep 30
done

send_telegram "✅ Scrape 完成！开始 LLM 分析..."

# ============================================================
# Step 2: Run analyze with custom date range
# ============================================================
send_telegram "🔄 Step 2/4: 启动 LLM 分析 (03-02 ~ 03-15)
预计耗时: 3-4 小时"

"$VENV_PYTHON" scripts/analyze.py \
    --start-date 2026-03-02 \
    --end-date 2026-03-15 \
    --llm-endpoint http://localhost:4141 \
    --model gpt-4 2>&1 | tee "$PROJECT_DIR/data/analyze_0302_0315.log"

ANALYZE_EXIT=$?
if [ $ANALYZE_EXIT -ne 0 ]; then
    send_telegram "❌ LLM 分析失败 (exit code: $ANALYZE_EXIT)
查看日志: data/analyze_0302_0315.log"
    exit 1
fi

send_telegram "✅ LLM 分析完成！开始翻译..."

# ============================================================
# Step 3: Translate
# ============================================================
send_telegram "🔄 Step 3/4: 翻译中..."

"$VENV_PYTHON" scripts/translate.py \
    --llm-endpoint http://localhost:4141 \
    --model gpt-4 2>&1 | tee "$PROJECT_DIR/data/translate_0302_0315.log"

TRANSLATE_EXIT=$?
if [ $TRANSLATE_EXIT -ne 0 ]; then
    send_telegram "⚠️ 翻译失败 (exit code: $TRANSLATE_EXIT)，继续部署..."
fi

# ============================================================
# Step 4: Deploy
# ============================================================
send_telegram "🔄 Step 4/4: 部署中..."

"$VENV_PYTHON" scripts/deploy.py 2>&1 | tee "$PROJECT_DIR/data/deploy_0302_0315.log"

DEPLOY_EXIT=$?
if [ $DEPLOY_EXIT -ne 0 ]; then
    send_telegram "⚠️ 部署失败 (exit code: $DEPLOY_EXIT)"
else
    send_telegram "🎉 Pipeline 全部完成！
✅ Scrape → ✅ Analyze → ✅ Translate → ✅ Deploy

03-02 ~ 03-15 分析报告已生成。
报告位置: data/reports/latest.json"
fi
