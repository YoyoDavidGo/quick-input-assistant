using Microsoft.Win32;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;

const string AppName    = "QuickInputAssistant";
const string AppDisplay = "QuickInputAssistant 快捷输入助手";
const string AppVersion = "1.0.0";
const string ExeName    = "QuickInputAssistant.exe";

Console.OutputEncoding = System.Text.Encoding.UTF8;

// ── 管理员提权 ────────────────────────────────────────────────
if (!IsAdmin())
{
    Console.WriteLine("需要管理员权限，正在提权...");
    var psi = new ProcessStartInfo(Environment.ProcessPath!, "")
    {
        Verb = "runas",
        UseShellExecute = true
    };
    try { Process.Start(psi); }
    catch { Console.WriteLine("已取消。"); }
    return;
}

// ── 欢迎界面 ─────────────────────────────────────────────────
Console.Clear();
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("╔══════════════════════════════════════════╗");
Console.WriteLine("║   QuickInputAssistant 快捷输入助手         ║");
Console.WriteLine($"║   版本 {AppVersion}  安装向导                  ║");
Console.WriteLine("╚══════════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine();

string installDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);

Console.WriteLine($"安装目录: {installDir}");
Console.Write("按 Enter 开始安装，或 Ctrl+C 取消...");
Console.ReadLine();

// ── 停止旧进程 ───────────────────────────────────────────────
StopProcess(ExeName.Replace(".exe", ""));

// ── 解压到安装目录 ────────────────────────────────────────────
Console.WriteLine();
Step("解压文件...");
if (Directory.Exists(installDir))
    Directory.Delete(installDir, true);
Directory.CreateDirectory(installDir);

using var stream = Assembly.GetExecutingAssembly()
    .GetManifestResourceStream("Installer.Resources.app.zip")
    ?? throw new Exception("嵌入资源 app.zip 未找到");

using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
int total = zip.Entries.Count, done = 0;
foreach (var entry in zip.Entries)
{
    if (string.IsNullOrEmpty(entry.Name)) continue; // 目录项
    string destPath = Path.Combine(installDir, entry.FullName.Replace('/', '\\'));
    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
    entry.ExtractToFile(destPath, overwrite: true);
    done++;
    if (done % 30 == 0)
        Console.Write($"\r  已解压 {done}/{total} 个文件...   ");
}
Console.WriteLine($"\r  已解压 {done}/{total} 个文件    ");

// 把 uninstall.ps1 也写进安装目录
Step("写入卸载脚本...");
File.WriteAllText(Path.Combine(installDir, "uninstall.ps1"), UninstallScript(installDir));

// ── 桌面快捷方式 ──────────────────────────────────────────────
Step("创建桌面快捷方式...");
CreateShortcut(
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), $"{AppName}.lnk"),
    Path.Combine(installDir, ExeName), installDir);

// ── 开始菜单 ─────────────────────────────────────────────────
Step("创建开始菜单...");
CreateShortcut(
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), $"{AppName}.lnk"),
    Path.Combine(installDir, ExeName), installDir);

// ── 开机自启（HKCU）────────────────────────────────────────────
Step("设置开机自启...");
using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)!;
runKey.SetValue(AppName, $"\"{Path.Combine(installDir, ExeName)}\"");

// ── 控制面板卸载项 ────────────────────────────────────────────
Step("注册卸载信息...");
using var uninstKey = Registry.LocalMachine.CreateSubKey(
    $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{AppName}");
uninstKey.SetValue("DisplayName",     AppDisplay);
uninstKey.SetValue("DisplayVersion",  AppVersion);
uninstKey.SetValue("Publisher",       "QuickInputAssistant");
uninstKey.SetValue("InstallLocation", installDir);
uninstKey.SetValue("UninstallString",
    $"powershell -ExecutionPolicy Bypass -File \"{Path.Combine(installDir, "uninstall.ps1")}\"");
uninstKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
uninstKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);

// ── 完成 ─────────────────────────────────────────────────────
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine();
Console.WriteLine("✅ 安装完成！");
Console.ResetColor();
Console.WriteLine($"   程序: {Path.Combine(installDir, ExeName)}");
Console.WriteLine("   桌面快捷方式已创建");
Console.WriteLine("   已加入开机自启（可在设置中关闭）");
Console.WriteLine();
Console.Write("是否立即启动程序？[Y/N] ");
var ans = Console.ReadLine();
if (ans?.Trim().ToUpper() == "Y")
    Process.Start(new ProcessStartInfo(Path.Combine(installDir, ExeName)) { UseShellExecute = true });

Console.WriteLine("按任意键退出...");
Console.ReadKey();

// ── 辅助函数 ─────────────────────────────────────────────────

static void Step(string msg)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.Write(">>> ");
    Console.ResetColor();
    Console.WriteLine(msg);
}

static bool IsAdmin()
{
    using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
    return new System.Security.Principal.WindowsPrincipal(id)
        .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
}

static void StopProcess(string name)
{
    foreach (var p in Process.GetProcessesByName(name))
    {
        try { p.Kill(); p.WaitForExit(2000); } catch { }
    }
}

static void CreateShortcut(string lnkPath, string targetPath, string workDir)
{
    // 用 WScript.Shell COM 创建快捷方式
    Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
    if (shellType is null) return;
    dynamic shell = Activator.CreateInstance(shellType)!;
    dynamic lnk   = shell.CreateShortcut(lnkPath);
    lnk.TargetPath       = targetPath;
    lnk.WorkingDirectory = workDir;
    lnk.Description      = "快捷输入助手";
    lnk.Save();
}

static string UninstallScript(string installDir)
{
    var dir = installDir.Replace(@"\", @"\\");
    return
        "#Requires -RunAsAdministrator\r\n" +
        $"$AppName    = \"{AppName}\"\r\n" +
        $"$InstallDir = \"{dir}\"\r\n" +
        "$proc = Get-Process -Name $AppName -ErrorAction SilentlyContinue\r\n" +
        "if ($proc) { $proc | Stop-Process -Force; Start-Sleep -Milliseconds 500 }\r\n" +
        "Remove-Item $InstallDir -Recurse -Force -ErrorAction SilentlyContinue\r\n" +
        "$desktop   = [Environment]::GetFolderPath('CommonDesktopDirectory')\r\n" +
        "$startMenu = \"$env:ProgramData\\Microsoft\\Windows\\Start Menu\\Programs\"\r\n" +
        "Remove-Item \"$desktop\\$AppName.lnk\"   -Force -ErrorAction SilentlyContinue\r\n" +
        "Remove-Item \"$startMenu\\$AppName.lnk\" -Force -ErrorAction SilentlyContinue\r\n" +
        "Remove-ItemProperty 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Run' $AppName -ErrorAction SilentlyContinue\r\n" +
        "Remove-Item \"HKLM:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\$AppName\" -Recurse -Force -ErrorAction SilentlyContinue\r\n" +
        "Write-Host '✅ 卸载完成'\r\n";
}
