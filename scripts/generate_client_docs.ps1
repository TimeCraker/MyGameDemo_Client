# ===== 路径配置 =====
$ProjectRoot = "C:\Users\TimeCraker\Desktop\unity-MyGameDemo"
$DocsDir = Join-Path -Path $ProjectRoot -ChildPath "docs"
# 将 Wiki 仓库放在 docs 目录下
$WikiDir = Join-Path -Path $DocsDir -ChildPath ".wiki.git"
$WikiRepoUrl = "https://github.com/TimeCraker/game-backend-demo.wiki.git"

# 确保 docs 目录存在
if (-not (Test-Path -Path $DocsDir)) {
    New-Item -ItemType Directory -Path $DocsDir | Out-Null
    Write-Host "[OK] Created docs directory." -ForegroundColor Green
}

Write-Host ">>> Starting Client document generation..." -ForegroundColor Cyan

# --- 1. 生成项目树 (仅 Assets) ---
$TreeOutputPath = Join-Path -Path $DocsDir -ChildPath "project_tree.txt"
Write-Host "-> [1/3] Generating Project Tree (Assets only)..."
Push-Location -Path $ProjectRoot
# 仅扫描 Assets，避免 Library/Logs 等冗余文件夹
cmd /c "tree Assets /f /a" | Out-File -FilePath $TreeOutputPath -Encoding utf8
Pop-Location

# --- 2. 合并 C# 源代码 ---
$CodeOutputPath = Join-Path -Path $DocsDir -ChildPath "all_code_merged.txt"
Write-Host "-> [2/3] Merging C# source files..."
$AssetsPath = Join-Path -Path $ProjectRoot -ChildPath "Assets"

Get-ChildItem -Path $AssetsPath -Filter *.cs -Recurse | 
    Where-Object { 
        $_.FullName -notmatch "Plugins" -and 
        $_.FullName -notmatch "TextMesh Pro" 
    } | 
    ForEach-Object {
        $relativeName = $_.FullName.Replace($ProjectRoot, "")
        "`n--- FILE: $relativeName ---`n"
        Get-Content $_.FullName -Encoding UTF8
    } | Out-File -FilePath $CodeOutputPath -Encoding UTF8

# --- 3. 同步并合并 GitHub Wiki ---
Write-Host "-> [3/3] Syncing shared Wiki notes..."
if (-not (Test-Path -Path $WikiDir)) {
    Write-Host "   Wiki cache not found. Cloning to docs/.wiki.git..." -ForegroundColor Yellow
    # 直接在 docs 目录下 clone
    git clone $WikiRepoUrl $WikiDir
} else {
    Write-Host "   Wiki cache exists. Pulling latest changes..." -ForegroundColor Yellow
    Push-Location -Path $WikiDir
    git pull
    Pop-Location
}

# 合并 Wiki 内容
$WikiOutputPath = Join-Path -Path $DocsDir -ChildPath "all_wiki_merged.txt"
if (Test-Path -Path $WikiDir) {
    Get-ChildItem -Path $WikiDir -Filter *.md -Recurse | 
        ForEach-Object {
            "`n--- WIKI PAGE: $($_.Name) ---`n"
            Get-Content $_.FullName -Encoding UTF8
        } | Out-File -FilePath $WikiOutputPath -Encoding UTF8
    Write-Host "   Wiki notes successfully merged." -ForegroundColor Green
}

Write-Host "`nDONE! All files are ready in: $DocsDir" -ForegroundColor Green