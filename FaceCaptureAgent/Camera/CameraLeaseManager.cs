namespace FaceCaptureAgent.Camera;

/// <summary>进程内共享的独占租约：同一时刻只允许一个会话使用摄像头，不是操作系统设备锁。</summary>
public sealed class CameraLeaseManager
{
    private readonly object _gate = new();
    private Guid? _ownerId;

    public IDisposable? TryAcquire(Guid ownerId)
    {
        lock (_gate)
        {
            if (_ownerId is not null)
            {
                return null;
            }

            _ownerId = ownerId;
            return new Lease(this, ownerId);
        }
    }

    private void Release(Guid ownerId)
    {
        lock (_gate)
        {
            if (_ownerId == ownerId)
            {
                _ownerId = null;
            }
        }
    }

    private sealed class Lease(CameraLeaseManager manager, Guid ownerId) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            // 正常关闭和异常清理可能重复释放，同一租约只归还一次。
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                manager.Release(ownerId);
            }
        }
    }
}
