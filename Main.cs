using System.Runtime.InteropServices;
using System.Windows.Controls;
using Flow.Launcher.Plugin;

namespace MouseMover;

public class Main : IPlugin, ISettingProvider
{
    private PluginInitContext _context = null!;
    private MouseMoverSettings _settings = null!;

    private readonly Random _rng = new();
    private System.Threading.Timer? _timer;
    private volatile bool _running;
    private int _gliding; // 0 = idle, 1 = a glide is in progress (overlap guard)

    private string IconPath => _context.CurrentPluginMetadata.IcoPath;

    public void Init(PluginInitContext context)
    {
        _context = context;
        _settings = context.API.LoadSettingJsonStorage<MouseMoverSettings>();
    }

    // The settings panel lives in Flow Launcher's plugin settings page.
    public Control CreateSettingPanel() => new SettingsControl(_context, _settings);

    public List<Result> Query(Query query)
    {
        // A single toggle. All configuration is in the settings panel.
        if (_running)
        {
            return new List<Result>
            {
                new()
                {
                    Title = "Disable mouse mover",
                    SubTitle = $"Running — gliding within {_settings.Radius}px every {_settings.IntervalMs} ms. Select to stop and return control.",
                    IcoPath = IconPath,
                    Score = 100,
                    Action = _ => { Stop(); return true; }
                }
            };
        }

        return new List<Result>
        {
            new()
            {
                Title = "Enable mouse mover",
                SubTitle = $"Stopped — will glide within {_settings.Radius}px every {_settings.IntervalMs} ms. Select to start.",
                IcoPath = IconPath,
                Score = 100,
                Action = _ => { Start(); return true; }
            }
        };
    }

    // ---------------------------------------------------------------- engine

    private void Start()
    {
        if (_running) return;
        _running = true;
        var period = Math.Max(MouseMoverSettings.MinInterval, _settings.IntervalMs);
        _timer = new System.Threading.Timer(OnTick, null, 250, period);
        _context.API.ShowMsg("Mouse Mover", "Enabled — cursor will move automatically.", IconPath);
    }

    private void Stop()
    {
        if (!_running) return;
        _running = false;
        _timer?.Dispose();
        _timer = null;
        _context.API.ShowMsg("Mouse Mover", "Disabled — you have control again.", IconPath);
    }

    private void OnTick(object? state)
    {
        if (!_running) return;
        // Skip this tick if a previous glide is still running.
        if (Interlocked.CompareExchange(ref _gliding, 1, 0) != 0) return;
        try
        {
            // Keep cadence in sync if the user changed the interval in settings.
            var period = Math.Max(MouseMoverSettings.MinInterval, _settings.IntervalMs);
            _timer?.Change(period, period);

            if (!GetCursorPos(out var start)) return;

            var (vx, vy, vw, vh) = VirtualScreen();
            var target = PickTarget(start, vx, vy, vw, vh);
            GlideTo(start, target);
        }
        finally
        {
            Interlocked.Exchange(ref _gliding, 0);
        }
    }

    private POINT PickTarget(POINT from, int vx, int vy, int vw, int vh)
    {
        // Uniform point inside a disc of the configured radius.
        var angle = _rng.NextDouble() * Math.PI * 2;
        var dist = Math.Sqrt(_rng.NextDouble()) * _settings.Radius;
        var tx = (int)Math.Round(from.X + Math.Cos(angle) * dist);
        var ty = (int)Math.Round(from.Y + Math.Sin(angle) * dist);
        // Clamp inside the full virtual desktop (all monitors).
        tx = Math.Clamp(tx, vx, vx + vw - 1);
        ty = Math.Clamp(ty, vy, vy + vh - 1);
        return new POINT { X = tx, Y = ty };
    }

    private void GlideTo(POINT from, POINT to)
    {
        var glide = _settings.GlideMs;
        var step = Math.Max(MouseMoverSettings.MinStep, _settings.StepMs);

        if (glide <= 0)
        {
            SetCursorPos(to.X, to.Y);
            return;
        }

        var steps = Math.Max(1, glide / step);
        for (var i = 1; i <= steps; i++)
        {
            if (!_running) return; // bail out instantly when disabled mid-glide
            var t = (double)i / steps;
            var e = EaseInOut(t);
            var x = (int)Math.Round(from.X + (to.X - from.X) * e);
            var y = (int)Math.Round(from.Y + (to.Y - from.Y) * e);
            SetCursorPos(x, y);
            Thread.Sleep(step);
        }
    }

    private static double EaseInOut(double t) =>
        t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;

    // ---------------------------------------------------------------- Win32

    private static (int x, int y, int w, int h) VirtualScreen()
    {
        int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int w = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int h = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (w <= 0 || h <= 0) // fallback to primary monitor
        {
            x = 0; y = 0;
            w = GetSystemMetrics(SM_CXSCREEN);
            h = GetSystemMetrics(SM_CYSCREEN);
        }
        return (x, y, w, h);
    }

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
