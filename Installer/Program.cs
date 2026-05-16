using Microsoft.Win32;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;

const string AppName    = "QuickInputAssistant";
const string AppDisplay = "QuickInputAssistant 快捷输入助手";
const string AppVersion = "1.0.0";
const string ExeName    = "QuickInputAssistant.exe";
const string MutexName  = "Global\\QuickInputAssistant_Installer_v1";

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.Title = $"{AppDisplay} 安装向导 v{AppVersion}";

// ── 全局单实例（防多窗口）─────────────────────────────────────
using var mutex = new Mutex(true, MutexName, out bool isFirst);
if (!isFirst)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine();
    Console.WriteLine("⚠️  安装程序已经在运行，请勿重复打开。");
    Console.ResetColor();
    Console.WriteLine();
    Console.WriteLine("按任意键关闭此窗口...");
    Console.ReadKey(true);
    return;
}

// ── 管理员提权 ────────────────────────────────────────────────
if (!IsAdmin())
{
    Console.WriteLine("需要管理员权限，请在弹出的 UAC 对话框中点'是'...");
    mutex.ReleaseMutex();  // 提前释放，让子进程能拿到 Mutex
    var psi = new ProcessStartInfo(Environment.ProcessPath!, "")
    {
        Verb = "runas",
        UseShellExecute = true
    };
    try { Process.Start(psi); }
    catch { Console.WriteLine("已取消。按任意键退出..."); Console.ReadKey(true); }
    return;
}

// ── 横幅 ─────────────────────────────────────────────────────
Console.Clear();
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine();
Console.WriteLine("  ╔══════════════════════════════════════════════╗");
Console.WriteLine("  ║      QuickInputAssistant 快捷输入助手        ║");
Console.WriteLine($"  ║      版本 {AppVersion}   安装向导                    ║");
Console.WriteLine("  ╚══════════════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine();

string defaultDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);

Console.WriteLine($"  默认安装目录：{defaultDir}");
Console.Write("  回车使用默认，或输入其他路径（如 D:\\Apps\\QuickInputAssistant）：");
string userInput = (Console.ReadLine() ?? "").Trim().Trim('"');

string installDir;
if (string.IsNullOrWhiteSpace(userInput))
{
    installDir = defaultDir;
}
else
{
    try
    {
        installDir = Path.GetFullPath(userInput);
        // 校验路径可写
        var parent = Path.GetDirectoryName(installDir);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            Directory.CreateDirectory(parent ?? installDir);
    }
    catch (Exception ex)
    {
        Fail($"路径无效：{ex.Message}");
        return;
    }
}

bool isUpgrade = Directory.Exists(installDir);
Console.WriteLine();
Console.WriteLine($"  目标目录：{installDir}");
Console.WriteLine($"  操作类型：{(isUpgrade ? "覆盖升级（保留用户数据）" : "全新安装")}");
Console.WriteLine();
Console.WriteLine("  开始安装...");
Console.WriteLine();

// ── 停止旧进程 ───────────────────────────────────────────────
Step("停止运行中的实例");
StopProcess(ExeName.Replace(".exe", ""));
Thread.Sleep(500);

// ── 解压到安装目录 ────────────────────────────────────────────
Step("解压文件");
if (Directory.Exists(installDir))
{
    try { Directory.Delete(installDir, true); }
    catch (Exception ex)
    {
        Fail($"无法清理旧目录：{ex.Message}\n   请确认应用已关闭后重试。");
        return;
    }
}
Directory.CreateDirectory(installDir);

using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Installer.Resources.app.zip"))
{
    if (stream == null) { Fail("嵌入资源 app.zip 未找到"); return; }
    using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
    int total = zip.Entries.Count, done = 0;
    foreach (var entry in zip.Entries)
    {
        if (string.IsNullOrEmpty(entry.Name)) continue;
        string destPath = Path.Combine(installDir, entry.FullName.Replace('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        entry.ExtractToFile(destPath, overwrite: true);
        done++;
        if (done % 50 == 0)
            Console.Write($"\r       已解压 {done}/{total} ...   ");
    }
    Console.WriteLine($"\r       已解压 {total}/{total} 完成     ");
}

// ── 写卸载脚本 ───────────────────────────────────────────────
Step("写入卸载脚本");
File.WriteAllText(Path.Combine(installDir, "uninstall.ps1"), UninstallScript(installDir));

// ── 桌面快捷方式 + 开始菜单 ─────────────────────────────────────
Step("创建桌面快捷方式");
CreateShortcut(
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), $"{AppName}.lnk"),
    Path.Combine(installDir, ExeName), installDir);

Step("创建开始菜单项");
CreateShortcut(
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), $"{AppName}.lnk"),
    Path.Combine(installDir, ExeName), installDir);

// ── 控制面板卸载项 ────────────────────────────────────────────
// 注：开机自启默认关闭，用户可在应用齿轮菜单 → 开机自启动 自助开启。
Step("注册卸载项");
using (var uninstKey = Registry.LocalMachine.CreateSubKey(
    $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{AppName}"))
{
    uninstKey.SetValue("DisplayName",     AppDisplay);
    uninstKey.SetValue("DisplayVersion",  AppVersion);
    uninstKey.SetValue("Publisher",       "QuickInputAssistant");
    uninstKey.SetValue("InstallLocation", installDir);
    uninstKey.SetValue("UninstallString",
        $"powershell -ExecutionPolicy Bypass -File \"{Path.Combine(installDir, "uninstall.ps1")}\"");
    uninstKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
    uninstKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
}

// ── 完成 ─────────────────────────────────────────────────────
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("  ╔══════════════════════════════════════════════╗");
Console.WriteLine("  ║              ✅  安装成功  ✅                ║");
Console.WriteLine("  ╚══════════════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine();
Console.WriteLine($"  • 程序位置：{Path.Combine(installDir, ExeName)}");
Console.WriteLine("  • 桌面已创建快捷方式");
Console.WriteLine("  • 开始菜单已加入");
Console.WriteLine("  • 开机自启：未启用（可在应用齿轮菜单中开启）");
Console.WriteLine();
Console.Write("  是否立即启动程序？[Y/n] ");
var ans = (Console.ReadLine() ?? "").Trim().ToUpper();
if (ans == "" || ans == "Y")
{
    Process.Start(new ProcessStartInfo(Path.Combine(installDir, ExeName)) { UseShellExecute = true });
    Console.WriteLine("  程序已启动。");
}
Console.WriteLine();
Console.WriteLine("  按任意键关闭安装程序...");
Console.ReadKey(true);

// ── 辅助函数 ─────────────────────────────────────────────────

static void Step(string msg)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.Write("  >>> ");
    Console.ResetColor();
    Console.WriteLine(msg);
}

static void Fail(string msg)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine();
    Console.WriteLine("  ❌ 安装失败：" + msg);
    Console.ResetColor();
    Console.WriteLine();
    Console.WriteLine("  按任意键关闭...");
    Console.ReadKey(true);
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
