using System;
using avallama.Utilities;

namespace avallama.Tests.Utilities;

public class SynchronousAvaloniaDispatcher : IAvaloniaDispatcher
{
    public void Post(Action action)
    {
        action();
    }

    public bool CheckAccess()
    {
        return true;
    }
}
