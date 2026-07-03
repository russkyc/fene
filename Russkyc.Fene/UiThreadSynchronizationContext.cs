using System.Collections.Concurrent;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Russkyc.Fene;

// based on this very good Stephen Toub article: https://devblogs.microsoft.com/pfxteam/await-synchronizationcontext-and-console-apps/
internal sealed class UiThreadSynchronizationContext(HWND hwnd) : SynchronizationContext
{
    private readonly BlockingCollection<KeyValuePair<SendOrPostCallback, object>> _mQueue = new();

    public override void Post(SendOrPostCallback d, object? state)
    {
        if (state is null) return;
        _mQueue.Add(new KeyValuePair<SendOrPostCallback, object>(d, state));
        PInvoke.PostMessage(hwnd, WebViewWindow.WmSynchronizationcontextWorkAvailable, 0, 0);
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        if (state is null) return;
        _mQueue.Add(new KeyValuePair<SendOrPostCallback, object>(d, state));
        PInvoke.SendMessage(hwnd, WebViewWindow.WmSynchronizationcontextWorkAvailable, 0, 0);
    }

    public void RunAvailableWorkOnCurrentThread()
    {
        while (_mQueue.TryTake(out KeyValuePair<SendOrPostCallback, object> workItem))
        {
            workItem.Key(workItem.Value);
        }
    }

    public void Complete()
    {
        _mQueue.CompleteAdding();
    }
}