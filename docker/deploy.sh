#!/bin/bash
# =============================================================================
# InvestmentTracker 自動部署腳本
# 由 webhook 觸發執行
# =============================================================================

set -e

# [關鍵修改 1] 設定工作目錄為 /app (對應 docker-compose 裡的掛載點)
WORK_DIR="/app"

# [關鍵修改 2] 將 Log 寫在 /app 底下，這樣你在 VPS 宿主機的資料夾也看得到
LOG_FILE="$WORK_DIR/deploy.log"

log() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] $1" | tee -a "$LOG_FILE"
}

log "========== 開始部署 =========="

# [關鍵修改 3] 切換到正確的目錄，這裡才有 docker-compose.prod.yml
cd "$WORK_DIR"

# 檢查檔案是否存在 (Debug 用)
if [ ! -f "docker-compose.prod.yml" ]; then
    log "錯誤：在 $WORK_DIR 找不到 docker-compose.prod.yml"
    exit 1
fi

# 登入 GHCR (如果環境變數有設定的話)
if [ -n "$GHCR_TOKEN" ]; then
    log "登入 GitHub Container Registry..."
    echo "$GHCR_TOKEN" | docker login ghcr.io -u "$GHCR_USER" --password-stdin
fi

# Pull 最新 images
log "拉取最新 images (Backend & Frontend)..."
docker compose -f docker-compose.prod.yml pull backend frontend

# 重啟服務
# 注意：這裡只重啟 backend 和 frontend，不影響 db 和 webhook 本身
log "重啟服務..."
docker compose -f docker-compose.prod.yml up -d --force-recreate backend frontend

# 清理舊 images (移除 dangling images)
log "清理舊 images..."
docker image prune -f

# 檢查服務狀態
log "檢查服務狀態..."
sleep 2
docker compose -f docker-compose.prod.yml ps

log "========== 部署完成 =========="