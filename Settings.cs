using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MouseMover;

/// <summary>
/// Persisted, user-tunable configuration. Edited via the plugin's settings
/// panel (see <see cref="SettingsControl"/>). Setters clamp to safe bounds so a
/// typo can never make the cursor unusable, and raise change notifications so
/// the panel reflects any clamping immediately.
/// </summary>
public class MouseMoverSettings : INotifyPropertyChanged
{
    // Safe bounds.
    public const int MinInterval = 250, MaxInterval = 3_600_000;
    public const int MinRadius = 5, MaxRadius = 4000;
    public const int MinGlide = 0, MaxGlide = 10_000;
    public const int MinStep = 1, MaxStep = 200;

    private int _intervalMs = 5000;
    private int _radius = 250;
    private int _glideMs = 600;
    private int _stepMs = 10;
    private bool _autoDisableOnUserMouseInput = true;
    private bool _restrictMovementToCurrentMonitor = true;

    /// <summary>Milliseconds between picking new random targets.</summary>
    public int IntervalMs
    {
        get => _intervalMs;
        set => Set(ref _intervalMs, Math.Clamp(value, MinInterval, MaxInterval));
    }

    /// <summary>Maximum distance (px) a new target may be from the current cursor.</summary>
    public int Radius
    {
        get => _radius;
        set => Set(ref _radius, Math.Clamp(value, MinRadius, MaxRadius));
    }

    /// <summary>How long (ms) each smooth glide to a target takes. 0 = instant jump.</summary>
    public int GlideMs
    {
        get => _glideMs;
        set => Set(ref _glideMs, Math.Clamp(value, MinGlide, MaxGlide));
    }

    /// <summary>Granularity (ms) of each glide step. Lower = smoother, more CPU.</summary>
    public int StepMs
    {
        get => _stepMs;
        set => Set(ref _stepMs, Math.Clamp(value, MinStep, MaxStep));
    }

    /// <summary>Stop the current run when physical mouse input is detected.</summary>
    public bool AutoDisableOnUserMouseInput
    {
        get => _autoDisableOnUserMouseInput;
        set => Set(ref _autoDisableOnUserMouseInput, value);
    }

    /// <summary>Keep random targets inside the monitor containing the cursor.</summary>
    public bool RestrictMovementToCurrentMonitor
    {
        get => _restrictMovementToCurrentMonitor;
        set => Set(ref _restrictMovementToCurrentMonitor, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set(ref int field, int value, [CallerMemberName] string? name = null)
    {
        if (field == value) 
            return;
            
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private void Set(ref bool field, bool value, [CallerMemberName] string? name = null)
    {
        if (field == value) 
            return;
            
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
