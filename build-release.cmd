@echo off
setlocal enabledelayedexpansion

REM One-click packaging script for QuickInputAssistant.

cd /d "%~dp0"
set "ROOT=%CD%"

echo ============================================================
echo  QuickInputAssistant - Release Build
echo  Working dir: %ROOT%
echo ============================================================
echo.

REM Kill any running instance to avoid file lock
taskkill /IM QuickInputAssistant.exe /F 1>NUL 2>&1

REM Shutdown stale MSBuild/dotnet daemons (may lock NuGet stub DLLs)
dotnet build-server shutdown 1>NUL 2>&1

REM Clean previous publish output
if exist "%ROOT%\publish" rmdir /s /q "%ROOT%\publish"
mkdir "%ROOT%\publish"

echo [1/4] dotnet publish main app (self-contained)...
dotnet publish "%ROOT%\QuickInputAssistant\QuickInputAssistant.csproj" -c Release -p:Platform=x64 -r win-x64 --self-contained true -o "%ROOT%\publish"
if errorlevel 1 (
    echo.
    echo ###### Main app publish FAILED ######
    echo  Tip: restart your PC or run "dotnet build-server shutdown" and retry.
    pause
    exit /b 1
)

REM Copy XAML artifacts (XBF + resources.pri) from bin to publish.
REM dotnet publish does NOT include them automatically because GenerateResourcesPri
REM only runs AfterTargets=Build and Publish has its own staging.
set "BIN=%ROOT%\QuickInputAssistant\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64"
echo Copying XAML artifacts from bin...
copy /Y "%BIN%\App.xbf"        "%ROOT%\publish\"
copy /Y "%BIN%\MainWindow.xbf" "%ROOT%\publish\"
copy /Y "%BIN%\resources.pri"  "%ROOT%\publish\"

echo [2/4] Strip unused locale dirs...
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

REM Copy install / uninstall scripts
if exist "%ROOT%\publish-scripts" (
    for %%F in (install.bat install.ps1 uninstall.bat uninstall.ps1) do (
        if exist "%ROOT%\publish-scripts\%%F" copy /Y "%ROOT%\publish-scripts\%%F" "%ROOT%\publish\" 1>NUL
    )
)

echo [3/4] Compress app.zip...
powershell -NoProfile -Command "Compress-Archive -Path '%ROOT%\publish\*' -DestinationPath '%ROOT%\Installer\Resources\app.zip' -Force"
if errorlevel 1 (
    echo.
    echo ###### Compress app.zip FAILED ######
    pause
    exit /b 1
)

echo [4/4] Build single-file installer...
dotnet publish "%ROOT%\Installer\Installer.csproj" -c Release -r win-x64 -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -o "%ROOT%\dist"
if errorlevel 1 (
    echo.
    echo ###### Installer publish FAILED ######
    pause
    exit /b 1
)

echo.
echo ============================================================
powershell -NoProfile -Command "$pub = [math]::Round((Get-ChildItem '%ROOT%\publish' -Recurse | Measure-Object Length -Sum).Sum/1MB,1); $zip=[math]::Round((Get-Item '%ROOT%\Installer\Resources\app.zip').Length/1MB,1); $exe=[math]::Round((Get-Item '%ROOT%\dist\QuickInputAssistant_Setup.exe').Length/1MB,1); Write-Host ('publish/   ' + $pub + ' MB'); Write-Host ('app.zip    ' + $zip + ' MB'); Write-Host ('installer  ' + $exe + ' MB')"
echo ============================================================
echo.
echo  Build success! Installer at:
echo    %ROOT%\dist\QuickInputAssistant_Setup.exe
echo.
echo  Double click to install.
echo ============================================================
echo.
pause
endlocal
