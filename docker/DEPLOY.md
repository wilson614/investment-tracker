# InvestmentTracker 部署指南

## CI/CD 架構

```
┌──────────┐     ┌─────────────┐     ┌────────┐     ┌─────────────┐
│ Git Push │ ──► │ GitHub      │ ──► │ GHCR   │ ──► │ VPS/NAS     │
│          │     │ Actions     │     │ Images │     │ Webhook     │
└──────────┘     └─────────────┘     └────────┘     └─────────────┘
```

## 首次部署 (VPS/NAS)

### 1. 安裝 Docker

```bash
# Ubuntu/Debian
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker $USER
```

### 2. 複製部署檔案

只需要 `docker/` 資料夾中的這些檔案：

```bash
mkdir -p /opt/investmenttracker
cd /opt/investmenttracker

# 從 repo 下載部署檔案 (或用 scp 上傳)
curl -O https://raw.githubusercontent.com/YOUR_REPO/main/docker/docker-compose.prod.yml
curl -O https://raw.githubusercontent.com/YOUR_REPO/main/docker/deploy.sh
curl -O https://raw.githubusercontent.com/YOUR_REPO/main/docker/webhook-hooks.json
curl -O https://raw.githubusercontent.com/YOUR_REPO/main/docker/webhook.Dockerfile
curl -O https://raw.githubusercontent.com/YOUR_REPO/main/docker/.env.example

chmod +x deploy.sh
```

### 3. 設定環境變數

```bash
cp .env.example .env
nano .env  # 填入實際值
```

必填項目：
- `GITHUB_REPO`: 你的 GitHub repo (例如: `username/investmenttracker`)
- `DB_PASSWORD`: 資料庫密碼
- `JWT_SECRET`: JWT 簽名金鑰 (至少 32 字元)
- `DEPLOY_WEBHOOK_SECRET`: Webhook 驗證密鑰

### 4. 設定 GitHub Secrets

到 GitHub Repo → Settings → Secrets and variables → Actions，新增：

| Secret 名稱 | 值 |
|-------------|-----|
| `DEPLOY_WEBHOOK_SECRET` | 與 .env 中相同的密鑰 |
| `DEPLOY_WEBHOOK_URL` | `http://YOUR_VPS_IP:9000/hooks/deploy` |

### 5. 啟動服務

```bash
docker compose -f docker-compose.prod.yml up -d
```

### 6. 檢查狀態

```bash
docker compose -f docker-compose.prod.yml ps
docker compose -f docker-compose.prod.yml logs -f
```

## 之後的更新

只需 `git push` 到 main branch，GitHub Actions 會自動：
1. Build Docker images
2. Push 到 GHCR
3. 呼叫 Webhook 觸發 VPS 更新

## 手動部署

如需手動部署：

```bash
cd /opt/investmenttracker
./deploy.sh
```

## 資料庫備份/還原

### 備份

```bash
docker exec investmenttracker-db pg_dump -U investmenttracker investmenttracker > backup_$(date +%Y%m%d).sql
```

### 還原

```bash
docker exec -i investmenttracker-db psql -U investmenttracker investmenttracker < backup.sql
```

## 遷移到其他機器

```bash
# 舊機器: 備份
docker exec investmenttracker-db pg_dump -U investmenttracker investmenttracker > backup.sql

# 新機器: 部署 + 還原
# 1. 執行上述「首次部署」步驟
# 2. 還原資料庫
docker exec -i investmenttracker-db psql -U investmenttracker investmenttracker < backup.sql

# 3. 更新 GitHub Secrets 的 DEPLOY_WEBHOOK_URL 為新機器 IP
```

## 端口說明

| 服務 | Port | 說明 |
|------|------|------|
| Frontend | 80 | Web UI |
| Webhook | 9000 | 部署觸發 |
| PostgreSQL | (內部) | 資料庫 (不對外) |
| Backend | (內部) | API (透過 nginx 反向代理) |

## 疑難排解

### 查看日誌

```bash
# 所有服務
docker compose -f docker-compose.prod.yml logs -f

# 特定服務
docker compose -f docker-compose.prod.yml logs -f backend
docker compose -f docker-compose.prod.yml logs -f webhook
```

### Webhook 沒有觸發

1. 檢查 VPS 防火牆是否開放 9000 port
2. 確認 GitHub Secrets 設定正確
3. 查看 webhook 容器日誌

### Image 拉取失敗

如果 repo 是 private，需要設定 GHCR 認證：

```bash
# .env 加入
GHCR_USER=your-github-username
GHCR_TOKEN=ghp_your_personal_access_token
```
