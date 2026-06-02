using System.Diagnostics;
using System.Threading.RateLimiting;

namespace CelestiCloud.Core.IO;

[DebuggerNonUserCode]
public class ThrottledStream : Stream
{
    private readonly Stream _baseStream;
    private readonly RateLimiter? _limiter;
    private readonly int _maxChunkSize;
    private readonly bool _leaveOpen;

    /// <summary>
    /// Initializes a new instance of the ThrottledStream.
    /// </summary>
    /// <param name="baseStream">The underlying stream to wrap.</param>
    /// <param name="limiter">The RateLimiter that controls byte-throughput. Pass null for unlimited speed.</param>
    /// <param name="maxChunkSize">The maximum bytes to request from the rate limiter per read/write cycle. Set this safely below the limiter's burst/token limit.</param>
    /// <param name="leaveOpen">True to keep the underlying stream open after disposing this stream.</param>
    public ThrottledStream(Stream baseStream, RateLimiter? limiter, int maxChunkSize = 32 * 1024, bool leaveOpen = false)
    {
        _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
        _limiter = limiter;
        _maxChunkSize = maxChunkSize;
        _leaveOpen = leaveOpen;
    }

    public override bool CanRead => _baseStream.CanRead;
    public override bool CanSeek => _baseStream.CanSeek;
    public override bool CanWrite => _baseStream.CanWrite;
    public override long Length => _baseStream.Length;

    public override long Position
    {
        get => _baseStream.Position;
        set => _baseStream.Position = value;
    }

    public override void Flush() => _baseStream.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        _baseStream.FlushAsync(cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) =>
        _baseStream.Seek(offset, origin);

    public override void SetLength(long value) =>
        _baseStream.SetLength(value);

    #region Synchronous Read / Write

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_limiter == null)
        {
            int readDirect = _baseStream.Read(buffer, offset, count);
            if (readDirect > 0) BandwidthMonitor.RecordBytes(readDirect);
            return readDirect;
        }

        int totalRead = 0;
        while (count > 0)
        {
            // Chunking the read to avoid acquiring more permits than the limiter's maximum capacity
            int toRead = Math.Min(count, _maxChunkSize);

            // Wait synchronously to acquire permits (1 permit = 1 byte)
            using var lease = _limiter.AcquireAsync(toRead).AsTask().GetAwaiter().GetResult();

            if (!lease.IsAcquired)
            {
                Thread.Sleep(10);
                continue;
            }

            int read = _baseStream.Read(buffer, offset, toRead);
            if (read <= 0) break;

            BandwidthMonitor.RecordBytes(read);

            totalRead += read;
            offset += read;
            count -= read;
        }

        return totalRead;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (_limiter == null)
        {
            _baseStream.Write(buffer, offset, count);
            return;
        }

        while (count > 0)
        {
            int toWrite = Math.Min(count, _maxChunkSize);

            using var lease = _limiter.AcquireAsync(toWrite).AsTask().GetAwaiter().GetResult();

            if (!lease.IsAcquired)
            {
                Thread.Sleep(10);
                continue;
            }

            _baseStream.Write(buffer, offset, toWrite);

            offset += toWrite;
            count -= toWrite;
        }
    }

    #endregion

    #region Legacy Asynchronous Read / Write

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_limiter == null)
        {
            int readDirect = await _baseStream.ReadAsync(buffer, offset, count, cancellationToken);
            if (readDirect > 0) BandwidthMonitor.RecordBytes(readDirect);
            return readDirect;
        }

        int totalRead = 0;
        while (count > 0)
        {
            int toRead = Math.Min(count, _maxChunkSize);

            using var lease = await _limiter.AcquireAsync(toRead, cancellationToken);

            if (!lease.IsAcquired)
            {
                await Task.Delay(10, cancellationToken);
                continue;
            }

            int read = await _baseStream.ReadAsync(buffer, offset, toRead, cancellationToken);
            if (read <= 0) break;

            BandwidthMonitor.RecordBytes(read);

            totalRead += read;
            offset += read;
            count -= read;
        }

        return totalRead;
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_limiter == null)
        {
            await _baseStream.WriteAsync(buffer, offset, count, cancellationToken);
            return;
        }

        while (count > 0)
        {
            int toWrite = Math.Min(count, _maxChunkSize);

            using var lease = await _limiter.AcquireAsync(toWrite, cancellationToken);

            if (!lease.IsAcquired)
            {
                await Task.Delay(10, cancellationToken);
                continue;
            }

            await _baseStream.WriteAsync(buffer, offset, toWrite, cancellationToken);

            offset += toWrite;
            count -= toWrite;
        }
    }

    #endregion

    #region Modern Memory-Based Asynchronous Read / Write (Optimized for .NET 10)

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_limiter == null)
        {
            int readDirect = await _baseStream.ReadAsync(buffer, cancellationToken);
            if (readDirect > 0) BandwidthMonitor.RecordBytes(readDirect);
            return readDirect;
        }

        int totalRead = 0;
        int remaining = buffer.Length;
        int offset = 0;

        while (remaining > 0)
        {
            int toRead = Math.Min(remaining, _maxChunkSize);

            using var lease = await _limiter.AcquireAsync(toRead, cancellationToken);

            if (!lease.IsAcquired)
            {
                await Task.Delay(10, cancellationToken); 
                continue;
            }

            var slice = buffer.Slice(offset, toRead);
            int read = await _baseStream.ReadAsync(slice, cancellationToken);
            if (read <= 0) break;

            BandwidthMonitor.RecordBytes(read);

            totalRead += read;
            offset += read;
            remaining -= read;
        }

        return totalRead;
    }



    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_limiter == null)
        {
            await _baseStream.WriteAsync(buffer, cancellationToken);
            return;
        }

        int remaining = buffer.Length;
        int offset = 0;

        while (remaining > 0)
        {
            int toWrite = Math.Min(remaining, _maxChunkSize);

            using var lease = await _limiter.AcquireAsync(toWrite, cancellationToken);

            if (!lease.IsAcquired)
            {
                await Task.Delay(10, cancellationToken);
                continue;
            }

            var slice = buffer.Slice(offset, toWrite);
            await _baseStream.WriteAsync(slice, cancellationToken);

            offset += toWrite;
            remaining -= toWrite;
        }
    }

    #endregion

    #region Cleanup

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_leaveOpen)
        {
            _baseStream.Dispose();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_leaveOpen)
        {
            await _baseStream.DisposeAsync();
        }
        await base.DisposeAsync();
    }

    #endregion
}