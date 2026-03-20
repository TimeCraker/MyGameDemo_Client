@echo off
chcp 65001 > nul
setlocal enabledelayedexpansion

:: 设置脚本名称
set SCRIPT_NAME=generate_client_docs.ps1

echo ======================================================
echo   AsterNova - 客户端文档自动化生成工具
echo ======================================================
echo.

:: 检查当前目录下是否存在脚本
if not exist "%~dp0%SCRIPT_NAME%" (
    echo [错误] 找不到脚本文件: %SCRIPT_NAME%
    pause
    exit /b
)

echo 正在调用 PowerShell 执行任务...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0%SCRIPT_NAME%"

echo.
echo ------------------------------------------------------
echo 任务已完成！
pause