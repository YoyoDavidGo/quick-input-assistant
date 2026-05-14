#Requires -RunAsAdministrator
$ErrorActionPreference = "Stop"
$AppName    = "QuickInputAssistant"
$InstallDir = "$env:ProgramFiles\$AppName"
$ExeName    = "QuickInputAssistant.exe"
$ScriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Definition

Write-Host ""
Write-Host ">>> 安装目录: $InstallDir"

# 1. 复制文件
if (Test-Path $InstallDir) {
    Write-Host ">>> 清理旧版本..."
    Remove-Item "$InstallDir\*" -Recurse -Force
} else {
    New-Item -ItemType Directory -Path $InstallDir | Out-Null
}

Write-Host ">>> 复制文件..."
$items = Get-ChildItem $ScriptDir -Exclude "install.bat","install.ps1","uninstall.ps1","uninstall.bat"
foreach ($item in $items) {
    Copy-Item $item.FullName $InstallDir -Recurse -Force
}

# 2. 桌面快捷方式
Write-Host ">>> 创建桌面快捷方式..."
$desktop = [System.Environment]::GetFolderPath("CommonDesktopDirectory")
$shell   = New-Object -ComObject WScript.Shell
$lnk     = $shell.CreateShortcut("$desktop\$AppName.lnk")
$lnk.TargetPath       = "$InstallDir\$ExeName"
$lnk.WorkingDirectory = $InstallDir
$lnk.Description      = "快捷输入助手"
$lnk.Save()

# 3. 开始菜单
Write-Host ">>> 创建开始菜单..."
$startMenu = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs"
$lnk2 = $shell.CreateShortcut("$startMenu\$AppName.lnk")
$lnk2.TargetPath       = "$InstallDir\$ExeName"
$lnk2.WorkingDirectory = $InstallDir
$lnk2.Description      = "快捷输入助手"
$lnk2.Save()

# 4. 开机自启（注册表 HKCU，不需要管理员也能自启）
Write-Host ">>> 设置开机自启..."
$regPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
Set-ItemProperty -Path $regPath -Name $AppName -Value "`"$InstallDir\$ExeName`""

# 5. 写卸载信息到注册表
$uninstKey = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$AppName"
New-Item -Path $uninstKey -Force | Out-Null
Set-ItemProperty $uninstKey "DisplayName"     -Value "QuickInputAssistant 快捷输入助手"
Set-ItemProperty $uninstKey "UninstallString" -Value "powershell -ExecutionPolicy Bypass -File `"$InstallDir\uninstall.ps1`""
Set-ItemProperty $uninstKey "InstallLocation" -Value $InstallDir
Set-ItemProperty $uninstKey "Publisher"       -Value "QuickInputAssistant"
Set-ItemProperty $uninstKey "DisplayVersion"  -Value "1.0.0"
Set-ItemProperty $uninstKey "NoModify"        -Value 1 -Type DWord
Set-ItemProperty $uninstKey "NoRepair"        -Value 1 -Type DWord

Write-Host ""
Write-Host "✅ 安装完成！"
Write-Host "   程序位置: $InstallDir\$ExeName"
Write-Host "   桌面快捷方式已创建"
Write-Host "   已加入开机自启"
Write-Host ""

$run = Read-Host "是否立即启动程序？(Y/N)"
if ($run -match "^[Yy]") {
    Start-Process "$InstallDir\$ExeName"
}
