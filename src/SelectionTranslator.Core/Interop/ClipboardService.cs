using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace SelectionTranslator.Core.Interop;

public interface IClipboardService
{
    uint GetSequenceNumber();
    bool TryGetText(out string text);
    object? Snapshot();
    void Restore(object? snapshot);
    bool TrySetText(string text);
}

public sealed class ClipboardService : IClipboardService
{
    private const int Retries = 5;
    private const int RetryDelayMs = 20;

    // 剪贴板对象必须在同一个 STA 线程上快照和恢复，线程池线程会因跨套间而失败。
    private static readonly Lazy<Dispatcher> ClipboardDispatcher = new(CreateDispatcher);

    public uint GetSequenceNumber() => NativeMethods.GetClipboardSequenceNumber();

    public bool TryGetText(out string text)
    {
        var result = Invoke(() =>
        {
            for (var i = 0; i < Retries; i++)
            {
                try
                {
                    if (Clipboard.ContainsText())
                        return (true, Clipboard.GetText());
                    return (false, string.Empty);
                }
                catch (COMException)
                {
                    Thread.Sleep(RetryDelayMs);
                }
            }
            return (false, string.Empty);
        });

        text = result.Item2;
        return result.Item1;
    }

    public object? Snapshot() => Invoke<object?>(() =>
    {
        for (var i = 0; i < Retries; i++)
        {
            try { return Clipboard.GetDataObject(); }
            catch (COMException) { Thread.Sleep(RetryDelayMs); }
        }
        return null;
    });

    public void Restore(object? snapshot)
    {
        if (snapshot is null)
            return;

        Invoke(() =>
        {
            for (var i = 0; i < Retries; i++)
            {
                try
                {
                    Clipboard.SetDataObject(snapshot, true);
                    return true;
                }
                catch (COMException)
                {
                    Thread.Sleep(RetryDelayMs);
                }
            }
            return false;
        });
    }

    public bool TrySetText(string text) => Invoke(() =>
    {
        for (var i = 0; i < Retries; i++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(RetryDelayMs);
            }
        }
        return false;
    });

    private static T Invoke<T>(Func<T> action) => ClipboardDispatcher.Value.Invoke(action);

    private static Dispatcher CreateDispatcher()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();

        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "ClipboardSta"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();

        return dispatcher!;
    }
}