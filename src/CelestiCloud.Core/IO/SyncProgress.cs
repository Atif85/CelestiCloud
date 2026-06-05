using System;

namespace CelestiCloud.Core.IO;

public class SyncProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public SyncProgress(Action<T> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public void Report(T value)
    {
        _handler(value); // Invokes the delegate synchronously on the active thread [1.3.1]
    }
}