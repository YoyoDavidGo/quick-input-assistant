using QuickInputAssistant.Models;

namespace QuickInputAssistant.Services;

/// <summary>
/// 消息栏状态管理：4 种 tone，3 秒防抖自动复位到 idle。
/// </summary>
public sealed class StatusService : IDisposable
{
    private System.Threading.Timer? _timer;
    private readonly object _lock = new();

    public event Action<StatusMessage>? StatusChanged;

    public void Set(StatusMessage msg)
    {
        StatusChanged?.Invoke(msg);
        if (msg.Tone == StatusTone.Idle) return;

        lock (_lock)
        {
            if (_timer == null)
                _timer = new System.Threading.Timer(_ => Reset(), null, 3000, Timeout.Infinite);
            else
                _timer.Change(3000, Timeout.Infinite);
        }
    }

    public void Reset() => Set(StatusMessage.Idle);

    public void Dispose()
    {
        lock (_lock) { _timer?.Dispose(); _timer = null; }
    }
}
