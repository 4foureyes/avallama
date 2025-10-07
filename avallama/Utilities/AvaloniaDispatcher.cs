using System;
using Avalonia.Threading;

namespace avallama.Utilities;

public interface IAvaloniaDispatcher
{
    void Post(Action action);
    bool CheckAccess();
}

public class AvaloniaDispatcher : IAvaloniaDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();
}
