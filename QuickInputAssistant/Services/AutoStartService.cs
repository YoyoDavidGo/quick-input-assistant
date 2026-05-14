using Microsoft.Win32;

namespace QuickInputAssistant.Services;

/// <summary>
/// 通过 HKCU\...\Run 注册表项管理开机自启（仅当前用户、无需管理员权限）。
/// </summary>
public static class AutoStartService
{
    private const string RegPath  = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueKey = "QuickInputAssistant";

    public static bool IsEnabled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RegPath);
            return k?.GetValue(ValueKey) is string;
        }
        catch { return false; }
    }

    public static void SetEnabled(bool enable)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RegPath, writable: true);
            if (k == null) return;
            if (enable)
            {
                string exe = Environment.ProcessPath ?? "";
                if (string.IsNullOrEmpty(exe)) return;
                k.SetValue(ValueKey, $"\"{exe}\"");
            }
            else
            {
                if (k.GetValue(ValueKey) != null) k.DeleteValue(ValueKey, throwOnMissingValue: false);
            }
        }
        catch { }
    }
}
