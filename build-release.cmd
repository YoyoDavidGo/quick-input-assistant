@echo off
setlocal enabledelayedexpansion
chcp 65001 >nul

REM One-shot pack: self-contained + locale strip + single-file installer
REM Must use cmd (not pwsh): pwsh causes MSBuild AppxPackage task load failure

set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"

REM Kill running instance (file lock)
taskkill /IM QuickInputAssistant.exe /F >nul 2>&1

REM 1. Clean publish/
if exist "%ROOT%\publish" rmdir /s /q "%ROOT%\publish"
mkdir "%ROOT%\publish"

REM 2. publish main app
echo [1/4] dotnet publish (self-contained)...
dotnet publish "%ROOT%\QuickInputAssistant\QuickInputAssistant.csproj" -c Release -p:Platform=x64 -r win-x64 --self-contained true -o "%ROOT%\publish"
if errorlevel 1 (
    echo publish failed
    exit /b 1
)

REM 3a. Copy XAML artifacts (XBF/resources.pri) from bin to publish
REM     csproj's GenerateResourcesPri runs AfterTargets=Build only, publish doesn't pull them
set "BIN=%ROOT%\QuickInputAssistant\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64"
copy /Y "%BIN%\App.xbf"        "%ROOT%\publish\" >nul
copy /Y "%BIN%\MainWindow.xbf" "%ROOT%\publish\" >nul
copy /Y "%BIN%\resources.pri"  "%ROOT%\publish\" >nul

REM 3b. Strip non-CN/EN locale subdirs
echo [2/4] strip locale dirs...
for /d %%D in ("%ROOT%\publish\*-*") do (
    set "NAME=%%~nxD"
    set "KEEP=0"
    if /I "!NAME!"=="zh-CN" set "KEEP=1"
    if /I "!NAME!"=="zh-TW" set "KEEP=1"
    if /I "!NAME!"=="en-us" set "KEEP=1"
    if /I "!NAME!"=="en-US" set "KEEP=1"
    if /I "!NAME!"=="en-GB" set "KEEP=1"
    if "!KEEP!"=="0" (
        echo   - remove !NAME!
        rmdir /s /q "%%D"
    )
)

REM 4. Copy install scripts from publish-scripts
if exist "%ROOT%\publish-scripts" (
    for %%F in (install.bat install.ps1 uninstall.bat uninstall.ps1) do (
        if exist "%ROOT%\publish-scripts\%%F" copy /Y "%ROOT%\publish-scripts\%%F" "%ROOT%\publish\" >nul
    )
)

REM 5. Compress to app.zip
echo [3/4] compress app.zip...
powershell -NoProfile -Command "Compress-Archive -Path '%ROOT%\publish\*' -DestinationPath '%ROOT%\Installer\Resources\app.zip' -Force"

REM 6. Build single-file installer
echo [4/4] build installer...
dotnet publish "%ROOT%\Installer\Installer.csproj" -c Release -r win-x64 -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -o "%ROOT%\dist"
if errorlevel 1 (
    echo installer publish failed
    exit /b 1
)

REM Report sizes
echo.
echo ============================================================
powershell -NoProfile -Command "$pub = [math]::Round((Get-ChildItem '%ROOT%\publish' -Recurse | Measure-Object Length -Sum).Sum/1MB,1); $zip=[math]::Round((Get-Item '%ROOT%\Installer\Resources\app.zip').Length/1MB,1); $exe=[math]::Round((Get-Item '%ROOT%\dist\QuickInputAssistant_Setup.exe').Length/1MB,1); Write-Host ('publish/  ' + $pub + ' MB'); Write-Host ('app.zip   ' + $zip + ' MB'); Write-Host ('installer ' + $exe + ' MB')"
echo.
echo Done: %ROOT%\dist\QuickInputAssistant_Setup.exe
endlocal
