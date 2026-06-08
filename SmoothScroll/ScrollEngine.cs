namespace SmoothScroll;

/// <summary>
/// Physics core of the smoother. Maintains a single scalar "velocity" that
/// incoming wheel events add to, and a high-frequency timer (~8ms / ~120fps)
/// that drains it.
///
/// Per frame:
///   emit      = velocity / StepsPerEvent      (a fraction posted this frame)
///   velocity *= Friction                      (exponential inertia decay)
///
/// Inertia is directional: a wheel event in the opposite direction of the
/// current glide cancels it and starts fresh in the new direction, rather than
/// summing against the existing velocity.
///
/// Fractional emit is accumulated and only whole wheel deltas are posted.
/// Because the geometric series sums to 1/(1-Friction), choosing
/// StepsPerEvent ≈ 1/(1-Friction) makes total emitted scroll roughly conserve
/// the raw (sensitivity-scaled) input, while larger StepsPerEvent values spread
/// it over more, smaller messages (smoother) and Friction controls the inertia
/// tail length.
///
/// The timer runs on a background thread; PostMessage (used by
/// <see cref="InjectionService"/>) is thread-safe, so this is safe. We use
/// System.Threading.Timer rather than a WinForms timer because the latter is
/// clamped to the ~15ms system tick and cannot reliably hit 8ms.
/// </summary>
public sealed class ScrollEngine : IDisposable
{
    // Below this magnitude the inertia tail is considered finished.
    private const double MinVelocity = 1.0;

    private readonly InjectionService _injection;
    private readonly AppSettings _settings;
    private readonly object _lock = new();

    private System.Threading.Timer? _timer;
    private bool _running;

    private double _velocity;        // current scroll velocity (wheel-delta units)
    private double _emitRemainder;   // fractional carry between frames
    private IntPtr _targetHwnd;      // most recent delivery target

    public ScrollEngine(InjectionService injection, AppSettings settings)
    {
        _injection = injection;
        _settings = settings;
    }

    /// <summary>
    /// Adds a captured wheel event to the velocity and ensures the frame timer
    /// is running. Called from the hook callback thread.
    /// </summary>
    public void AddScroll(int rawDelta, IntPtr targetHwnd)
    {
        lock (_lock)
        {
            _targetHwnd = targetHwnd;
            double incoming = rawDelta * _settings.Sensitivity;

            // Inertia only carries the active direction of movement: if the new
            // scroll opposes the current glide, cancel it and start fresh in the
            // new direction (instead of partially summing against it).
            if (incoming != 0 && _velocity != 0 &&
                Math.Sign(incoming) != Math.Sign(_velocity))
            {
                _velocity = 0;
                _emitRemainder = 0; // drop the fractional carry from the old direction
            }

            _velocity += incoming;

            if (!_running)
            {
                _running = true;
                int interval = Math.Max(1, _settings.FrameIntervalMs);
                _timer = new System.Threading.Timer(Tick, null, 0, interval);
            }
        }
    }

    /// <summary>Immediately cancels any in-progress inertia.</summary>
    public void Stop()
    {
        lock (_lock)
        {
            StopInternalLocked();
        }
    }

    private void Tick(object? state)
    {
        IntPtr hwnd;
        int emit;

        lock (_lock)
        {
            if (Math.Abs(_velocity) < MinVelocity)
            {
                StopInternalLocked();
                return;
            }

            int steps = Math.Max(1, _settings.StepsPerEvent);
            double portion = _velocity / steps;

            _emitRemainder += portion;
            emit = (int)_emitRemainder;       // truncate toward zero
            _emitRemainder -= emit;           // keep the fractional remainder

            _velocity *= _settings.Friction;  // exponential decay (inertia)
            hwnd = _targetHwnd;
        }

        if (emit != 0)
            _injection.PostWheel(hwnd, emit);
    }

    private void StopInternalLocked()
    {
        _running = false;
        _velocity = 0;
        _emitRemainder = 0;
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose() => Stop();
}
