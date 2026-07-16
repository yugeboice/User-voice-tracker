#!/bin/bash
# Periodic progress reporter - checks every 10 min, sends Telegram update, reads user messages

BOT_TOKEN="8279468924:AAFxUsZ-drwZEtYH7kgAxJVZJWbXTsKlZy0"
CHAT_ID="8754454930"
PROJECT_DIR="/c/Users/dorisrao/vibeprojects/lab/projects/Reddit-analysis"
PIPELINE_LOG="/c/Users/dorisrao/AppData/Local/Temp/claude/C--Users-dorisrao--craft-agent-workspaces-1f04029a-2394-d6d0-c247-8d52626e52ca-workdirectory/tasks/brd99zqcv.output"

send_telegram() {
    curl -s -X POST "https://api.telegram.org/bot$BOT_TOKEN/sendMessage" \
        -d chat_id="$CHAT_ID" \
        -d text="$1" > /dev/null 2>&1
}

LAST_UPDATE_ID=0

for i in $(seq 1 30); do  # Run for up to 5 hours (30 x 10min)
    sleep 600

    # Check if pipeline is still running
    PIPELINE_RUNNING=$(ps aux 2>/dev/null | grep -c "auto_pipeline" || tasklist 2>/dev/null | grep -c "bash")

    # Get last few lines of pipeline log
    LAST_LOG=$(tail -5 "$PIPELINE_LOG" 2>/dev/null || echo "Log not available")

    # Check running python processes
    PY_PROCS=$(tasklist 2>/dev/null | grep -i python | wc -l)

    # Read new Telegram messages
    NEW_MSGS=$(curl -s "https://api.telegram.org/bot$BOT_TOKEN/getUpdates?limit=3&offset=-3" 2>/dev/null)

    # Build status message
    TIMESTAMP=$(date +"%H:%M")
    send_telegram "📊 定期汇报 ($TIMESTAMP) [#$i]

Python进程数: $PY_PROCS
最近日志:
$LAST_LOG

(每10分钟自动汇报)"

    # Check if pipeline finished
    if [ -f "$PROJECT_DIR/data/reports/latest.json" ]; then
        LATEST_MOD=$(stat -c %Y "$PROJECT_DIR/data/reports/latest.json" 2>/dev/null || echo "0")
        SCRIPT_START=$(date -d "10 minutes ago" +%s 2>/dev/null || echo "0")
        # If latest.json was modified recently, pipeline may be done
    fi
done
