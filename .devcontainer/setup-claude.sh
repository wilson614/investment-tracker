#!/bin/bash

# 1. 安裝工具
echo "📦 Installing global tools..."
npm install -g @anthropic-ai/claude-code @augmentcode/auggie

# 2. 定義新設定 (JSON 格式)
export CLAUDE_CONFIG_Update='{
  "hasCompletedOnboarding": true,
  "installMethod": "npm",
  "mcpServers": {
    "desktop-commander": {
      "type": "stdio",
      "command": "npx",
      "args": [
        "-y",
        "@wonderwhy-er/desktop-commander"
      ],
      "env": {}
    },
    "context7": {
      "type": "stdio",
      "command": "npx",
      "args": [
        "-y",
        "@upstash/context7-mcp@latest"
      ],
      "env": {}
    },
    "sequential-thinking": {
      "type": "stdio",
      "command": "npx",
      "args": [
        "-y",
        "@modelcontextprotocol/server-sequential-thinking"
      ],
      "env": {}
    },
    "playwright": {
      "type": "stdio",
      "command": "npx",
      "args": [
        "-y",
        "@playwright/mcp@latest"
      ],
      "env": {}
    },
    "ace": {
      "type": "stdio",
      "command": "auggie",
      "args": [
        "--mcp"
      ],
      "env": {
        "AUGMENT_API_TOKEN": "PLACEHOLDER_TOKEN",
        "AUGMENT_API_URL": "https://d1.api.augmentcode.com/"
      }
    }
  }
}'

# 把真實的 Token 塞進去 (取代上面的 PLACEHOLDER)
export CLAUDE_CONFIG_Update="${CLAUDE_CONFIG_Update/PLACEHOLDER_TOKEN/$AUGMENT_API_TOKEN}"

echo "⚙️ Merging configuration into .claude.json..."

# 3. 使用 Node.js 執行「智慧合併」
# 這段腳本會：
# a. 檢查檔案是否存在，存在就讀取，不存在就開新的
# b. 保留原本所有的設定
# c. 只更新/加入我們指定的 hasCompletedOnboarding 和 mcpServers
node -e "
const fs = require('fs');
const targetPath = '/home/vscode/.claude.json';
const newConfig = JSON.parse(process.env.CLAUDE_CONFIG_Update);

let currentConfig = {};

// 嘗試讀取舊設定
if (fs.existsSync(targetPath)) {
  try {
    currentConfig = JSON.parse(fs.readFileSync(targetPath, 'utf8'));
    console.log('Found existing config, merging...');
  } catch (e) {
    console.log('Existing config is invalid, starting fresh...');
  }
} else {
  console.log('No existing config found, creating new one...');
}

// 合併邏輯 (Deep Merge for mcpServers)
const finalConfig = { ...currentConfig, ...newConfig };

// 特別處理 mcpServers，以免覆蓋掉您原本可能有的其他 MCP 工具
if (currentConfig.mcpServers && newConfig.mcpServers) {
  finalConfig.mcpServers = { ...currentConfig.mcpServers, ...newConfig.mcpServers };
}

// 寫回檔案
fs.writeFileSync(targetPath, JSON.stringify(finalConfig, null, 2));
"

# 4. 處理專案依賴
echo "🚀 Restoring project dependencies..."
cd frontend && npm install
cd ..
dotnet restore backend

echo "✅ Setup complete!"