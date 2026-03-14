# ===== 新增代码 START =====
$ProjectRoot = "C:\Users\TimeCraker\Desktop\MyGameDemo_Client"
$DocsDir = Join-Path -Path $ProjectRoot -ChildPath "docs"
$WikiDir = Join-Path -Path $ProjectRoot -ChildPath ".wiki.git"
$WikiRepoUrl = "https://github.com/TimeCraker/game-backend-demo.wiki.git"

if (-not (Test-Path -Path $DocsDir)) {
    New-Item -ItemType Directory -Path $DocsDir | Out-Null
    Write-Host "Success: Created output directory." -ForegroundColor Green
}

Write-Host "Starting Client document generation task..." -ForegroundColor Cyan

# 1. Project Tree (仅针对 Assets 目录)
$TreeOutputPath = Join-Path -Path $DocsDir -ChildPath "project_tree.txt"
Write-Host "-> [1/3] Generating Project Tree for Assets..."
Push-Location -Path $ProjectRoot
# 这里加上了 Assets 目录限制，避免把庞大的 Library 和 Temp 导出来
cmd /c "tree Assets /f /a" | Out-File -FilePath $TreeOutputPath -Encoding utf8
Pop-Location

# 2. Merge C# Code
$CodeOutputPath = Join-Path -Path $DocsDir -ChildPath "all_code_merged.txt"
Write-Host "-> [2/3] Merging C# source files..."
$AssetsPath = Join-Path -Path $ProjectRoot -ChildPath "Assets"
Get-ChildItem -Path $AssetsPath -Filter *.cs -Recurse | 
    Where-Object { 
        $_.FullName -notmatch "Plugins" -and 
        $_.FullName -notmatch "TextMesh Pro" 
    } | 
    ForEach-Object {
        $content = Get-Content $_.FullName -Encoding UTF8
        "`n--- FILE: $($_.FullName) ---`n"
        $content
    } | Out-File -FilePath $CodeOutputPath -Encoding UTF8

# 3. Sync GitHub Wiki (前后端共用一个 Wiki)
Write-Host "-> [3/3] Syncing and Merging shared Wiki notes..."
if (-not (Test-Path -Path $WikiDir)) {
    Write-Host "   Wiki directory not found. Cloning from GitHub..." -ForegroundColor Yellow
    Push-Location -Path $ProjectRoot
    git clone $WikiRepoUrl ".wiki.git"
    Pop-Location
} else {
    Write-Host "   Wiki directory exists. Pulling latest changes..." -ForegroundColor Yellow
    Push-Location -Path $WikiDir
    git pull
    Pop-Location
}

# 4. Merge Wiki
$WikiOutputPath = Join-Path -Path $DocsDir -ChildPath "all_wiki_merged.txt"
if (Test-Path -Path $WikiDir) {
    Get-ChildItem -Path $WikiDir -Filter *.md -Recurse | 
        ForEach-Object {
            $header = "`n--- WIKI PAGE: $($_.Name) ---`n"
            Write-Output $header
            Get-Content $_.FullName -Encoding UTF8
        } | Out-File -FilePath $WikiOutputPath -Encoding UTF8
    Write-Host "   Wiki notes successfully merged." -ForegroundColor Green
} else {
    Write-Host "   Warning: Failed to clone Wiki directory, skipping merge." -ForegroundColor Red
}

Write-Host "DONE! All files saved to $DocsDir" -ForegroundColor Green
# ===== 新增代码 END =====