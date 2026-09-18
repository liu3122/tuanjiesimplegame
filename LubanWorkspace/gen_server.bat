@echo off
rem ============================================================
rem  Luban 服务端生成脚本
rem  - C# 代码 -> ../Server/LubanGen（服务端工程引用）
rem  - json 数据 -> ../Assets/AssetRaw/Config（与客户端共用同一份数据）
rem ============================================================
setlocal
set LUBAN_DLL=%~dp0Tools\Luban\Luban.dll
set WORKSPACE=%~dp0

dotnet %LUBAN_DLL% ^
    -t server ^
    -c cs-simple-json ^
    -d json ^
    --conf %WORKSPACE%luban.conf ^
    -x outputCodeDir=..\Server\LubanGen ^
    -x outputDataDir=..\Assets\AssetRaw\Config ^
    -x pathValidator.rootDir=..\

if %errorlevel% neq 0 (
    echo.
    echo [Luban] 服务端生成失败！
    pause
    exit /b 1
)
echo.
echo [Luban] 服务端生成完成：Server/LubanGen
pause
