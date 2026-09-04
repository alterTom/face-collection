namespace FaceCaptureAgent.Camera;

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
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                manager.Release(ownerId);
            }
        }
    }
}
