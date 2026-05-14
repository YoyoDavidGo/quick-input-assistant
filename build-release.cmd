@echo off
setlocal enabledelayedexpansion
chcp 65001 >nul

REM 一键打包脚本：自含发布 + 多余语言精简 + 单文件安装器
REM 双击运行时通过 pause 防止窗口闪退

REM 强制切到脚本所在目录（双击时尤其重要）
cd /d "%~dp0"
set "ROOT=%CD%"

echo ============================================================
echo  快捷输入助手 - 打包脚本
echo  工作目录: %ROOT%
echo ============================================================
echo.

REM 杀掉正在运行的实例（避免文件锁）
taskkill /IM QuickInputAssistant.exe /F >nul 2>&1

REM 1. 清空 publish/
if exist "%ROOT%\publish" rmdir /s /q "%ROOT%\publish"
mkdir "%ROOT%\publish"

REM 2. 发布主程序（自含）
echo [1/4] dotnet publish 主程序（自含 .NET + WinAppSDK）...
dotnet publish "%ROOT%\QuickInputAssistant\QuickInputAssistant.csproj" -c Release -p:Platform=x64 -r win-x64 --self-contained true -o "%ROOT%\publish"
if errorlevel 1 (
    echo.
    echo ###### 主程序 publish 失败 ######
    pause
    exit /b 1
)

REM 3a. 复制 XAML 产物（XBF / resources.pri）
set "BIN=%ROOT%\QuickInputAssistant\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64"
copy /Y "%BIN%\App.xbf"        "%ROOT%\publish\" >nul
copy /Y "%BIN%\MainWindow.xbf" "%ROOT%\publish\" >nul
copy /Y "%BIN%\resources.pri"  "%ROOT%\publish\" >nul

REM 3b. 删除非 CN/EN 语言子目录
echo [2/4] 精简多余语言资源...
for /d %%D in ("%ROOT%\publish\*-*") do (
    set "NAME=%%~nxD"
    set "KEEP=0"
    if /I "!NAME!"=="zh-CN" set "KEEP=1"
    if /I "!NAME!"=="zh-TW" set "KEEP=1"
    if /I "!NAME!"=="en-us" set "KEEP=1"
    if /I "!NAME!"=="en-US" set "KEEP=1"
    if /I "!NAME!"=="en-GB" set "KEEP=1"
    if "!KEEP!"=="0" rmdir /s /q "%%D"
)

REM 4. 复制安装/卸载脚本
if exist "%ROOT%\publish-scripts" (
    for %%F in (install.bat install.ps1 uninstall.bat uninstall.ps1) do (
        if exist "%ROOT%\publish-scripts\%%F" copy /Y "%ROOT%\publish-scripts\%%F" "%ROOT%\publish\" >nul
    )
)

REM 5. 打包为 app.zip
echo [3/4] 压缩 app.zip...
powershell -NoProfile -Command "Compress-Archive -Path '%ROOT%\publish\*' -DestinationPath '%ROOT%\Installer\Resources\app.zip' -Force"
if errorlevel 1 (
    echo.
    echo ###### 压缩 app.zip 失败 ######
    pause
    exit /b 1
)

REM 6. 编译单文件安装器
echo [4/4] 生成单文件安装器...
dotnet publish "%ROOT%\Installer\Installer.csproj" -c Release -r win-x64 -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -o "%ROOT%\dist"
if errorlevel 1 (
    echo.
    echo ###### Installer publish 失败 ######
    pause
    exit /b 1
)

REM 输出大小与产物路径
echo.
echo ============================================================
powershell -NoProfile -Command "$pub = [math]::Round((Get-ChildItem '%ROOT%\publish' -Recurse | Measure-Object Length -Sum).Sum/1MB,1); $zip=[math]::Round((Get-Item '%ROOT%\Installer\Resources\app.zip').Length/1MB,1); $exe=[math]::Round((Get-Item '%ROOT%\dist\QuickInputAssistant_Setup.exe').Length/1MB,1); Write-Host ('publish/   ' + $pub + ' MB'); Write-Host ('app.zip    ' + $zip + ' MB'); Write-Host ('installer  ' + $exe + ' MB')"
echo ============================================================
echo.
echo  打包成功！安装器位于：
echo    %ROOT%\dist\QuickInputAssistant_Setup.exe
echo.
echo  双击即可安装。
echo ============================================================
echo.
pause
endlocal
