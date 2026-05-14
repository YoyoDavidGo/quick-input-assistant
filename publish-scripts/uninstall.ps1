#Requires -RunAsAdministrator
$AppName    = "QuickInputAssistant"
$InstallDir = "$env:ProgramFiles\$AppName"
$ExeName    = "QuickInputAssistant.exe"

Write-Host ">>> 卸载 $AppName..."

# 停止进程
$proc = Get-Process -Name ($ExeName -replace '\.exe','') -ErrorAction SilentlyContinue
if ($proc) {
    $proc | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

# 删除文件
if (Test-Path $InstallDir) {
    Remove-Item $InstallDir -Recurse -Force
    Write-Host ">>> 已删除安装目录"
}

# 删除快捷方式
$desktop   = [System.Environment]::GetFolderPath("CommonDesktopDirectory")
$startMenu = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs"
Remove-Item "$desktop\$AppName.lnk"   -Force -ErrorAction SilentlyContinue
Remove-Item "$startMenu\$AppName.lnk" -Force -ErrorAction SilentlyContinue

# 删除开机自启
$regPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
Remove-ItemProperty -Path $regPath -Name $AppName -ErrorAction SilentlyContinue

# 删除卸载注册表项
Remove-Item "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$AppName" -Force -ErrorAction SilentlyContinue

Write-Host "✅ 卸载完成"
