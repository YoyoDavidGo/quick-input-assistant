namespace QuickInputAssistant.Services;

public enum Lang { Zh, En }

/// <summary>
/// 应用文本资源（中英双语）。每个属性返回当前语言对应字符串。
/// 切换语言后触发 Changed 事件，UI 订阅以刷新。
/// </summary>
public static class Strings
{
    private static Lang _lang = Lang.Zh;
    public static event Action? Changed;

    public static Lang Lang
    {
        get => _lang;
        set
        {
            if (_lang == value) return;
            _lang = value;
            Changed?.Invoke();
        }
    }

    private static string S(string zh, string en) => _lang == Lang.Zh ? zh : en;

    // ── 窗口 / 状态栏 ───────────────────────────────────────────
    public static string WindowTitle    => S("快捷输入助手",            "Quick Input Assistant");
    public static string RunningStatus  => S("快捷输入助手运行中…",      "Quick Input Assistant running…");

    // ── 键帽 ────────────────────────────────────────────────────
    public static string TapToBind      => S("点击绑定",               "Click to bind");

    // ── 齿轮菜单 ───────────────────────────────────────────────
    public static string Help           => S("帮助",                  "Help");
    public static string SwitchTheme    => S("切换主题",               "Theme");
    public static string ThemeDark      => S("深色主题",               "Dark");
    public static string ThemeLight     => S("浅色主题",               "Light");
    public static string ThemeAuto      => S("跟随系统",               "Follow system");
    public static string ManagePresets  => S("预设管理",               "Presets");
    public static string RenamePreset   => S("重命名当前预设…",         "Rename current preset…");
    public static string ResetPreset    => S("重置当前为默认绑定…",      "Reset current to defaults…");
    public static string Language       => S("语言",                  "Language");
    public static string LangZh         => S("中文",                  "Chinese (中文)");
    public static string LangEn         => S("English",              "English");
    public static string AutoStart      => S("开机自启动",              "Run at startup");
    public static string Exit           => S("退出应用",               "Exit");

    // ── 状态消息 ───────────────────────────────────────────────
    public static string SwitchedDark   => S("已切换为深色主题",         "Switched to Dark theme");
    public static string SwitchedLight  => S("已切换为浅色主题",         "Switched to Light theme");
    public static string FollowSystemDark  => S("已跟随系统（当前为深色）", "Following system (currently Dark)");
    public static string FollowSystemLight => S("已跟随系统（当前为浅色）", "Following system (currently Light)");
    public static string AutoStartOn    => S("已开启开机自启动",         "Auto-start enabled");
    public static string AutoStartOff   => S("已关闭开机自启动",         "Auto-start disabled");
    public static string LangSwitchedZh => S("已切换为中文",            "Switched to Chinese");
    public static string LangSwitchedEn => S("已切换为英文",            "Switched to English");

    // ── 预设默认名 ─────────────────────────────────────────────
    public static string DefaultPresetName(int index) => S($"预设{index + 1}", $"Preset {index + 1}");
    public static string DefaultReimbursementName    => S("报销",             "Reimbursement");

    public static string SetKeyOk(char key, string val) =>
        S($"设置 ALT+{key} 为 \"{val}\" 成功", $"Set ALT+{key} to \"{val}\"");
    public static string ClearKeyOk(char key) =>
        S($"已清空 ALT+{key} 绑定", $"Cleared ALT+{key} binding");
    public static string PresetRenamed(string newName) =>
        S($"预设已重命名为 \"{newName}\"", $"Preset renamed to \"{newName}\"");
    public static string PresetReset(string name) =>
        S($"已重置「{name}」为默认绑定", $"Reset \"{name}\" to defaults");
    public static string DateFormatError =>
        S("ALT+Q 仅支持 YY/MM/DD 格式",  "ALT+Q only accepts YY/MM/DD format");

    // ── 重置确认对话框 ──────────────────────────────────────────
    public static string ResetTitle     => S("重置当前预设？",          "Reset current preset?");
    public static string ResetMessage(string name) =>
        S($"将把预设「{name}」的全部 14 个键位绑定恢复为出厂默认值，此操作不可撤销。",
          $"All 14 key bindings in preset \"{name}\" will be restored to factory defaults. This cannot be undone.");
    public static string Reset          => S("重置",                  "Reset");
    public static string Cancel         => S("取消",                  "Cancel");
    public static string Ok             => S("确定",                  "OK");

    // ── 重命名对话框 ───────────────────────────────────────────
    public static string RenameTitle    => S("重命名预设",             "Rename preset");

    // ── 帮助文本 ───────────────────────────────────────────────
    public static string HelpText => S(
        "【输出绑定文字】\n" +
        "• 按 Alt+1~6 / Q-R / A-F 任一组合键，输出绑定文字到当前焦点窗口\n" +
        "• 或左键单击 UI 上的按键\n\n" +
        "【日期键 Alt+Q】\n" +
        "• 单击：输出今日日期 (YY/MM/DD)\n" +
        "• 双击：撤销前一次并改为 +1 天\n\n" +
        "【修改绑定（内联编辑）】\n" +
        "• 右键单击 UI 上的按键 → 进入编辑态（键帽变蓝边、文字全选）\n" +
        "• 确认：回车 / 左键单击任意位置（键帽、状态栏、空白、桌面）\n" +
        "• 取消：Esc / 右键单击任意位置（键帽、空白、桌面）\n" +
        "• 或在外部应用先选中文字 → 按 Alt+键 自动绑定\n\n" +
        "【预设】\n" +
        "• 齿轮菜单 → 预设管理：4 套独立绑定，可命名 / 切换\n" +
        "• 状态栏齿轮左侧显示当前预设名\n\n" +
        "【主题】\n" +
        "• 齿轮菜单 → 切换主题：深色 / 浅色 / 跟随系统\n" +
        "• 「跟随系统」与系统主题反向，便于在反差背景下查看",

        "[Output bound text]\n" +
        "• Press Alt+1~6 / Q-R / A-F to type the bound text into the focused window\n" +
        "• Or left-click a key on the UI\n\n" +
        "[Date key  Alt+Q]\n" +
        "• Single click: type today's date (YY/MM/DD)\n" +
        "• Double click: undo previous output and step +1 day\n\n" +
        "[Edit binding (inline)]\n" +
        "• Right-click a UI key → enter edit mode (blue border, text selected)\n" +
        "• Confirm: Enter / Left-click anywhere (key, status bar, gap, desktop)\n" +
        "• Cancel:  Esc / Right-click anywhere\n" +
        "• Or select text in another app, then press Alt+key to auto-bind\n\n" +
        "[Presets]\n" +
        "• Gear → Presets: 4 independent slots, each with a custom name\n" +
        "• Active preset name is shown to the left of the gear\n\n" +
        "[Theme]\n" +
        "• Gear → Theme: Dark / Light / Follow system\n" +
        "• \"Follow system\" inverts the system theme to keep contrast.");
}
