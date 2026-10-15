using System;
using System.IO;
using System.Threading;

namespace Ohrbob.App.Services;

public class HttpBufferingStream : Stream
{
    private readonly FileStream _fileStream;
    private readonly long _contentLength;
    private bool _isDownloadComplete;
    private readonly object _lock = new object();
    private readonly Action<bool>? _onBufferingChanged;
    private bool _isBufferingEventFired;

    public HttpBufferingStream(string tempFile, long contentLength, Action<bool>? onBufferingChanged = null)
    {
        _fileStream = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        _contentLength = contentLength;
        _onBufferingChanged = onBufferingChanged;
    }

    public void MarkComplete() => _isDownloadComplete = true;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _contentLength;

    public override long Position
    {
        get { lock (_lock) return _fileStream.Position; }
        set { lock (_lock) _fileStream.Position = value; }
    }

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int readBytes = 0;
        while (true)
        {
            lock (_lock)
            {
                if (_fileStream.Position < _fileStream.Length)
                {
                    readBytes = _fileStream.Read(buffer, offset, count);
                    break;
                }
            }

            if (_isDownloadComplete)
                return 0;

            if (!_isBufferingEventFired)
            {
                _isBufferingEventFired = true;
                _onBufferingChanged?.Invoke(true);
            }

            Thread.Sleep(50); // Buffer underrun, wait for download task
        }

        if (_isBufferingEventFired)
        {
            _isBufferingEventFired = false;
            _onBufferingChanged?.Invoke(false);
        }

        return readBytes;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        lock (_lock)
        {
            long newPos = offset;
            if (origin == SeekOrigin.Current) newPos = _fileStream.Position + offset;
            else if (origin == SeekOrigin.End) newPos = _contentLength + offset;
            
            // Miniaudio may seek to the end of the file to parse ID3 tags.
            // If the UI seeks to a point not downloaded yet, block briefly to wait for it.
            int maxWait = 100; // 5 seconds maximum wait during seek
            int waited = 0;
            while (newPos > _fileStream.Length && !_isDownloadComplete && waited < maxWait) {
                Monitor.Exit(_lock);
                Thread.Sleep(50);
                Monitor.Enter(_lock);
                waited++;
            }
            
            if (newPos > _fileStream.Length) {
                // Time out reached, fallback to end of downloaded chunk to avoid UI freeze.
                return _fileStream.Seek(_fileStream.Length, SeekOrigin.Begin);
            }

            return _fileStream.Seek(offset, origin);
        }
    }

    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    
    protected override void Dispose(bool disposing)
    {
        _fileStream.Dispose();
        base.Dispose(disposing);
    }
}