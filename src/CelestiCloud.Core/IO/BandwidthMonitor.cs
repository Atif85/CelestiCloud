using System;
using System.Threading;

namespace CelestiCloud.Core.IO;

public static class BandwidthMonitor
{
    private static long _totalBytesUploaded;
    private static long _bytesInLastInterval;
    private static double _currentSpeedBps;
    private static readonly Timer _speedTimer;

    static BandwidthMonitor()
    {
        // Fires every 1 second to calculate current speed and reset the interval bucket
        _speedTimer = new Timer(CalculateSpeed, null, 1000, 1000);
    }

    public static long TotalBytesUploaded => Volatile.Read(ref _totalBytesUploaded);
    public static double CurrentSpeedBps => Volatile.Read(ref _currentSpeedBps);

    /// <summary>
    /// Records bytes successfully read/written through the throttled stream.
    /// </summary>
    public static void RecordBytes(long bytes)
    {
        Interlocked.Add(ref _totalBytesUploaded, bytes);
        Interlocked.Add(ref _bytesInLastInterval, bytes);
    }

    private static void CalculateSpeed(object? state)
    {
        long bytesThisSecond = Interlocked.Exchange(ref _bytesInLastInterval, 0);
        Volatile.Write(ref _currentSpeedBps, bytesThisSecond); // Speed in Bytes/Sec
    }
}