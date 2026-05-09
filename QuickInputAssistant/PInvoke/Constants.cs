namespace QuickInputAssistant.PInvoke;

internal static class WM
{
    public const int KEYDOWN = 0x0100;
    public const int KEYUP   = 0x0101;
    public const int SYSKEYDOWN = 0x0104;
    public const int SYSKEYUP   = 0x0105;
}

internal static class WH
{
    public const int KEYBOARD_LL = 13;
}

internal static class VK
{
    public const int MENU   = 0x12; // Alt
    public const int BACK   = 0x08; // Backspace
    public const int CONTROL = 0x11;
    public const int KEY_C  = 0x43;

    public const int VK_1 = 0x31;
    public const int VK_2 = 0x32;
    public const int VK_3 = 0x33;
    public const int VK_4 = 0x34;
    public const int VK_5 = 0x35;
    public const int VK_6 = 0x36;
    public const int VK_Q = 0x51;
    public const int VK_W = 0x57;
    public const int VK_E = 0x45;
    public const int VK_R = 0x52;
    public const int VK_A = 0x41;
    public const int VK_S = 0x53;
    public const int VK_D = 0x44;
    public const int VK_F = 0x46;
}

internal static class KEYEVENTF
{
    public const uint KEYDOWN  = 0x0000;
    public const uint KEYUP    = 0x0002;
    public const uint UNICODE  = 0x0004;
    public const uint SCANCODE = 0x0008;
}

internal static class INPUT_TYPE
{
    public const uint KEYBOARD = 1;
}

internal static class WS_EX
{
    public const int NOACTIVATE  = 0x08000000;
    public const int TOOLWINDOW  = 0x00000080;
    public const int LAYERED     = 0x00080000;
    public const int TRANSPARENT = 0x00000020;
}

internal static class GWL
{
    public const int STYLE   = -16;
    public const int EXSTYLE = -20;
}

internal static class WS
{
    public const int CAPTION    = 0x00C00000; // WS_BORDER | WS_DLGFRAME
    public const int THICKFRAME = 0x00040000;
    public const int SYSMENU    = 0x00080000;
}
