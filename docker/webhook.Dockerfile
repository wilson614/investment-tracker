# 第一階段：來源
FROM soulteary/webhook:latest AS source

# 第二階段：實際執行的環境
FROM alpine:latest

# 1. 安裝 Docker 工具
RUN apk add --no-cache docker-cli docker-cli-compose curl bash gettext

# 2. [修正] 從 /usr/bin/webhook 複製 (這是大部分 Alpine based image 的預設位置)
COPY --from=source /usr/bin/webhook /usr/local/bin/webhook

# 3. 給予執行權限
RUN chmod +x /usr/local/bin/webhook

# 4. 設定工作目錄
WORKDIR /app

