@echo off
chcp 65001 > nul

echo 正在请求管理员权限并启动前端文档生成脚本...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0generate_client_docs.ps1"

echo.
echo 任务已完成！按任意键退出...
pause