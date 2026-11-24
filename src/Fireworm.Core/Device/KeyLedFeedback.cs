using System.ComponentModel;

namespace Fireworm.Core.Device;

/// <summary>
/// Lights the LED of the key that was just pressed, then turns it off again.
/// Only volatile writes are used (no AA A1 commit), so nothing reaches flash and the
/// stored LED mode stays as the user configured it.
/// </summary>
public sealed class KeyLedFeedback : IDisposable
{
    private const int HoldMilliseconds = 1000;

    private readonly Func<string?> _path;
    private readonly Action<string> _log;
    private readonly object _sync = new();
    private readonly HashSet<int> _lit = new();
    private readonly System.Threading.Timer _timer;
    private VendorConfigWriter? _writer;
    private bool _reportedFailure;
    private bool _disposed;

    public KeyLedFeedback(Func<string?> path, Action<string> log)
    {
        _path = path;
        _log = log;
        _timer = new System.Threading.Timer(_ => Clear(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Pulse(int slot)
    {
        if (slot < 1 || slot > 6) return;

        lock (_sync)
        {
            if (_disposed || !Write(slot, 1)) return;
            _lit.Add(slot);
            _timer.Change(HoldMilliseconds, Timeout.Infinite);
        }
    }

    private void Clear()
    {
        lock (_sync)
        {
            if (_disposed || _lit.Count == 0) return;
            foreach (var slot in _lit) Write(slot, 0);
            _lit.Clear();
        }
    }

    private bool Write(int slot, byte mode)
    {
        try
        {
            if (_writer == null)
            {
                var path = _path();
                if (string.IsNullOrEmpty(path)) return false;
                _writer = new VendorConfigWriter(path);
            }

            _writer.WriteSlotLed(slot, mode, commit: false);
            _reportedFailure = false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or UnauthorizedAccessException)
        {
            _writer?.Dispose();
            _writer = null;
            if (!_reportedFailure) _log($"LED 표시를 사용할 수 없습니다: {ex.Message}");
            _reportedFailure = true;
            return false;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Dispose();
            _writer?.Dispose();
            _writer = null;
        }
    }
}
