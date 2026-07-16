#!/bin/bash
set -e

SCRIPTS_DIR="$(cd "$(dirname "$0")" && pwd)"
DATA_DIR="$SCRIPTS_DIR/../data"
VENV="$SCRIPTS_DIR/venv/Scripts/python"
LOG_PREFIX="$DATA_DIR/pipeline_$(date +%Y%m%d_%H%M%S)"

# ─────────────────────────────────────────────
# Date range: default to last 14 days (Monday-aligned)
# Override with: ./auto_pipeline.sh 2026-05-05 2026-05-18
# ─────────────────────────────────────────────
if [ -n "$1" ] && [ -n "$2" ]; then
    START_DATE="$1"
    END_DATE="$2"
else
    # Auto-calculate: this Monday minus 14 days
    END_DATE=$(date -d "last monday" +%Y-%m-%d 2>/dev/null || date -v-monday +%Y-%m-%d)
    START_DATE=$(date -d "$END_DATE - 14 days" +%Y-%m-%d 2>/dev/null || date -v-14d -j -f "%Y-%m-%d" "$END_DATE" +%Y-%m-%d)
fi

echo "=== AUTO PIPELINE STARTED at $(date) ==="
echo "Period: $START_DATE to $END_DATE"
echo "Log prefix: $LOG_PREFIX"

# ─────────────────────────────────────────────
# Phase 1: Backfill official subreddits
# ─────────────────────────────────────────────
echo ""
echo "=== Phase 1: Backfilling official subreddits ==="
"$VENV" "$SCRIPTS_DIR/backfill_arctic.py" \
    --start-date "$START_DATE" --end-date "$END_DATE" \
    --min-score 1 \
    2>&1 | tee "${LOG_PREFIX}_backfill.log"

echo "Backfill COMPLETE!"

# ─────────────────────────────────────────────
# Phase 2: Cross-subreddit keyword search (NEW)
# ─────────────────────────────────────────────
echo ""
echo "=== Phase 2: Cross-subreddit keyword search ==="
"$VENV" "$SCRIPTS_DIR/backfill_arctic.py" \
    --keyword \
    --start-date "$START_DATE" --end-date "$END_DATE" \
    --min-score 1 \
    2>&1 | tee "${LOG_PREFIX}_keyword.log"

echo "Keyword search COMPLETE!"

# ─────────────────────────────────────────────
# Phase 3: LLM analysis (includes scenario tagging)
# ─────────────────────────────────────────────
echo ""
echo "=== Phase 3: Running LLM analysis ==="
echo "Checking LLM endpoint..."
for i in $(seq 1 10); do
    if curl -s --max-time 5 "http://localhost:4141/v1/models" > /dev/null 2>&1; then
        echo "LLM endpoint ready!"
        break
    fi
    echo "LLM not ready, waiting 30s (attempt $i/10)..."
    sleep 30
done

"$VENV" "$SCRIPTS_DIR/analyze.py" \
    --start-date "$START_DATE" --end-date "$END_DATE" \
    --llm-endpoint http://localhost:4141 \
    --model gpt-4 \
    2>&1 | tee "${LOG_PREFIX}_analyze.log"

echo "Analysis COMPLETE!"

# Extract run_id and report filename from analyze log
RUN_ID=$(grep "Run ID:" "${LOG_PREFIX}_analyze.log" | tail -1 | awk '{print $NF}')
REPORT_FILE=$(grep "Report saved:" "${LOG_PREFIX}_analyze.log" | tail -1 | awk '{print $NF}')
REPORT_BASENAME=$(basename "$REPORT_FILE")
echo "Run ID: $RUN_ID"
echo "Report: $REPORT_BASENAME"

# ─────────────────────────────────────────────
# Phase 4: Scenario analysis (NEW, independent module)
# ─────────────────────────────────────────────
echo ""
echo "=== Phase 4: Scenario analysis ==="
"$VENV" "$SCRIPTS_DIR/scenario_analysis.py" \
    --run-id "$RUN_ID" \
    2>&1 | tee "${LOG_PREFIX}_scenario.log"

echo "Scenario analysis COMPLETE!"

# ─────────────────────────────────────────────
# Phase 5: Translate report
# ─────────────────────────────────────────────
echo ""
echo "=== Phase 5: Translating report ==="
"$VENV" "$SCRIPTS_DIR/translate.py" \
    --file "$REPORT_BASENAME" \
    --llm-endpoint http://localhost:4141 \
    2>&1 | tee "${LOG_PREFIX}_translate.log"

echo "Translation COMPLETE!"

# ─────────────────────────────────────────────
# Phase 6: Summary
# ─────────────────────────────────────────────
echo ""
echo "=== Phase 6: All processing done! ==="
echo "Pipeline completed at $(date)"
echo ""
echo "SUMMARY:"
echo "  Run ID: $RUN_ID"
echo "  Report: $REPORT_BASENAME"
echo "  Scenario: latest_scenario.json"
echo "  Period: $START_DATE to $END_DATE"
echo ""
echo ">>> WAITING FOR USER CONFIRMATION BEFORE DEPLOY <<<"
echo ">>> Run 'python deploy.py' after user approves <<<"

