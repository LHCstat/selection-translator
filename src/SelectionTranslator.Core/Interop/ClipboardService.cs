using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace SelectionTranslator.Core.Interop;

public readonly record struct ClipboardSnapshot(bool Success, bool HasData, object? Data);

public interface IClipboardService
{
    uint GetSequenceNumber();
    bool TryGetText(out string text);
    ClipboardSnapshot Snapshot();
    void Restore(object? snapshot);
    bool TrySetText(string text);
}

public static class ClipboardCloner
{
    /// <summary>
    /// 把剪贴板数据复制到一个脱离的数据对象里。直接把 GetDataObject 返回的对象
    /// 再 SetDataObject 回去会造成自引用，查询格式时无限递归。
    /// </summary>
    public static DataObject Clone(IDataObject source)
    {
        var copy = new DataObject();

        foreach (var format in source.GetFormats())
        {
            try
            {
                var data = source.GetData(format);
                if (data is not null)
                    copy.SetData(format, data);
            }
            catch (Exception)
            {
                // 延迟渲染或跨进程格式可能取不到，跳过该格式即可。
            }
        }

        return copy;
    }
}

public sealed class ClipboardService : IClipboardService
{
    private const int Retries = 5;
    private const int RetryDelayMs = 20;
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMilliseconds(1000);

    // 剪贴板对象必须在同一个 STA 线程上快照和恢复。
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
        }, (false, string.Empty));

        text = result.Item2;
        return result.Item1;
    }

    public ClipboardSnapshot Snapshot() => Invoke(() =>
    {
        for (var i = 0; i < Retries; i++)
        {
            try
            {
                var source = Clipboard.GetDataObject();
                if (source is null)
                    return new ClipboardSnapshot(true, false, null);

                return new ClipboardSnapshot(true, true, ClipboardCloner.Clone(source));
            }
            catch (COMException)
            {
                Thread.Sleep(RetryDelayMs);
            }
        }

        return new ClipboardSnapshot(false, false, null);
    }, new ClipboardSnapshot(false, false, null));

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
        }, false);
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
    }, false);

    private static T Invoke<T>(Func<T> action, T fallback)
    {
        DispatcherOperation<T> operation;

        try
        {
            operation = ClipboardDispatcher.Value.InvokeAsync(action);
        }
        catch (Exception)
        {
            return fallback;
        }

        if (operation.Wait(OperationTimeout) != DispatcherOperationStatus.Completed)
        {
            operation.Abort();
            return fallback;
        }

        try
        {
            return operation.Result;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

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