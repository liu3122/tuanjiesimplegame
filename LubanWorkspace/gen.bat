@echo off
rem ============================================================
rem  Luban 配置生成脚本
rem  - C# 代码  -> ../Assets/HotUpdateScripts/Gen (热更程序集内)
rem  - json 数据 -> ../Assets/AssetRaw/Config    (被 YooAsset 收集)
rem ============================================================
setlocal
set LUBAN_DLL=%~dp0Tools\Luban\Luban.dll
set WORKSPACE=%~dp0

dotnet %LUBAN_DLL% ^
    -t client ^
    -c cs-simple-json ^
    -d json ^
    --conf %WORKSPACE%luban.conf ^
    -x outputCodeDir=..\Assets\HotUpdateScripts\Gen ^
    -x outputDataDir=..\Assets\AssetRaw\Config ^
    -x pathValidator.rootDir=..\

if %errorlevel% neq 0 (
    echo.
    echo [Luban] 生成失败！
    pause
    exit /b 1
)

echo.
echo [Luban] 生成完成：C# 代码 -> Assets/HotUpdateScripts/Gen , json 数据 -> Assets/AssetRaw/Config
pause
