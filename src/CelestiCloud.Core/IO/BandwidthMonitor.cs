using System;
using System.Threading;

namespace CelestiCloud.Core.IO;

public static class BandwidthMonitor
{
    private static long _totalBytesUploaded;
    private static readonly long[] _speedHistory = new long[5];
    private static int _historyIndex;
    private static long _bytesInCurrentInterval;
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
        Interlocked.Add(ref _bytesInCurrentInterval, bytes);
    }

    private static void CalculateSpeed(object? state)
    {
        // Swap out the bytes recorded in the last 1 second
        long bytesThisSecond = Interlocked.Exchange(ref _bytesInCurrentInterval, 0);

        // Store it in our rolling history array
        _speedHistory[_historyIndex] = bytesThisSecond;
        _historyIndex = (_historyIndex + 1) % _speedHistory.Length;

        // Calculate the moving average
        long sum = 0;
        for (int i = 0; i < _speedHistory.Length; i++)
        {
            sum += _speedHistory[i];
        }

        double movingAverageBps = (double)sum / _speedHistory.Length;

        // Update the volatile speed variable safely
        Volatile.Write(ref _currentSpeedBps, movingAverageBps);
    }
}