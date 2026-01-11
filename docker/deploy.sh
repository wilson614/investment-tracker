#!/bin/bash
# =============================================================================
# InvestmentTracker 自動部署腳本
# 由 webhook 觸發執行
# =============================================================================

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
LOG_FILE="/var/log/investmenttracker-deploy.log"

log() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] $1" | tee -a "$LOG_FILE"
}

log "========== 開始部署 =========="

cd "$SCRIPT_DIR"

# 登入 GHCR (使用環境變數或預設 token)
if [ -n "$GHCR_TOKEN" ]; then
    log "登入 GitHub Container Registry..."
    echo "$GHCR_TOKEN" | docker login ghcr.io -u "$GHCR_USER" --password-stdin
fi

# Pull 最新 images
log "拉取最新 images..."
docker compose -f docker-compose.prod.yml pull backend frontend

# 重啟服務 (不中斷 postgres)
log "重啟服務..."
docker compose -f docker-compose.prod.yml up -d backend frontend

# 清理舊 images
log "清理舊 images..."
docker image prune -f

# 檢查服務狀態
log "檢查服務狀態..."
sleep 5
docker compose -f docker-compose.prod.yml ps

log "========== 部署完成 =========="
