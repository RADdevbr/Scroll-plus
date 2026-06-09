namespace SmoothScroll;

/// <summary>
/// Physics core of the smoother. A single accumulator collects incoming wheel
/// input and a high-frequency timer (~8ms / ~120fps) drains it into posted wheel
/// messages. Two models are supported (see <see cref="ScrollMode"/>):
///
///   Inertia  — the accumulator is a velocity: each frame emits velocity/Steps
///              and then velocity *= Friction (exponential decay tail).
///
///   Momentum — the accumulator is the remaining distance to a target: each
///              frame eases out a fraction (remaining *= 1-EaseFactor) toward 0,
///              i.e. a web-like ease-out that conserves the total scroll.
///
/// Both models are directional: a wheel event opposite to the current glide
/// cancels it and starts fresh in the new direction instead of summing against
/// the accumulator. Fractional emission is carried between frames so only whole
/// wheel deltas are posted.
///
/// The timer runs on a background thread; PostMessage (used by
/// <see cref="InjectionService"/>) is thread-safe, so this is safe. We use
/// System.Threading.Timer rather than a WinForms timer because the latter is
/// clamped to the ~15ms system tick and cannot reliably hit 8ms.
/// </summary>
public sealed class ScrollEngine : IDisposable
{
    // Inertia: below this velocity the tail is finished.
    private const double MinVelocity = 1.0;
    // Momentum: below this remaining distance the glide is finished.
    private const double MomentumEpsilon = 0.5;

    private readonly InjectionService _injection;
    private readonly AppSettings _settings;
    private readonly object _lock = new();

    private System.Threading.Timer? _timer;
    private bool _running;

    // Velocity (Inertia) or remaining distance to target (Momentum), in wheel-delta units.
    private double _accum;
    private double _emitRemainder;   // fractional carry between frames
    private IntPtr _targetHwnd;      // most recent delivery target

    public ScrollEngine(InjectionService injection, AppSettings settings)
    {
        _injection = injection;
        _settings = settings;
    }

    /// <summary>
    /// Adds a captured wheel event to the accumulator and ensures the frame timer
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
            if (incoming != 0 && _accum != 0 &&
                Math.Sign(incoming) != Math.Sign(_accum))
            {
                _accum = 0;
                _emitRemainder = 0; // drop the fractional carry from the old direction
            }

            _accum += incoming;

            if (!_running)
            {
                _running = true;
                int interval = Math.Max(1, _settings.FrameIntervalMs);
                _timer = new System.Threading.Timer(Tick, null, 0, interval);
            }
        }
    }

    /// <summary>Immediately cancels any in-progress glide.</summary>
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
            emit = _settings.Mode == ScrollMode.Momentum
                ? StepMomentumLocked()
                : StepInertiaLocked();

            if (!_running)
                return; // a step decided the glide is finished

            hwnd = _targetHwnd;
        }

        if (emit != 0)
            _injection.PostWheel(hwnd, emit);
    }

    /// <summary>Inertia model: emit velocity/Steps, then decay velocity by Friction.</summary>
    private int StepInertiaLocked()
    {
        if (Math.Abs(_accum) < MinVelocity)
        {
            StopInternalLocked();
            return 0;
        }

        int steps = Math.Max(1, _settings.StepsPerEvent);
        double portion = _accum / steps;

        _emitRemainder += portion;
        int emit = (int)_emitRemainder; // truncate toward zero
        _emitRemainder -= emit;         // keep the fractional remainder

        _accum *= _settings.Friction;   // exponential decay (inertia)
        return emit;
    }

    /// <summary>Momentum model: ease out a fraction of the remaining distance each frame.</summary>
    private int StepMomentumLocked()
    {
        if (Math.Abs(_accum) < MomentumEpsilon)
        {
            StopInternalLocked();
            return 0;
        }

        double ease = Math.Clamp(_settings.EaseFactor, 0.02, 0.9);
        double step = _accum * ease;

        // Floor the per-frame progress to at least one unit so the tail finishes
        // promptly, and never overshoot the remaining distance.
        if (Math.Abs(step) < 1.0)
            step = Math.Sign(_accum);
        if (Math.Abs(step) > Math.Abs(_accum))
            step = _accum;

        _accum -= step;

        _emitRemainder += step;
        int emit = (int)_emitRemainder; // truncate toward zero
        _emitRemainder -= emit;         // keep the fractional remainder
        return emit;
    }

    private void StopInternalLocked()
    {
        _running = false;
        _accum = 0;
        _emitRemainder = 0;
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose() => Stop();
}
