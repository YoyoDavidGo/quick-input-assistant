namespace QuickInputAssistant.Models;

public enum StatusTone { Idle, Success, Info, Warn }

public sealed class StatusMessage
{
    public StatusTone Tone { get; init; }
    public string Text { get; init; } = "";

    public static StatusMessage Idle    => new() { Tone = StatusTone.Idle,    Text = "💡 快捷输入助手运行中…" };
    public static StatusMessage Paused(string proc) => new() { Tone = StatusTone.Warn, Text = $"⏸ 在 {proc} 中已暂停" };
}
