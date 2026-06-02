using System.Runtime.InteropServices;
using System.Windows.Controls;
using Flow.Launcher.Plugin;

namespace MouseMover;

public class Main : IPlugin, ISettingProvider, IDisposable
{
    private PluginInitContext _context = null!;
    private MouseMoverSettings _settings = null!;

    private readonly Random _random = new();
    private Timer? _timer;
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
            return
            [
                new()
                {
                    Title = "Disable Mouse Mover",
                    SubTitle = $"Running gliding within {_settings.Radius}px every {_settings.IntervalMs} ms. Select to stop and return control.",
                    IcoPath = IconPath,
                    Score = 100,
                    Action = _ => { Stop(); return true; }
                }
            ];
        }

        return
        [
            new()
            {
                Title = "Enable Mouse Mover",
                SubTitle = $"Stopped will glide within {_settings.Radius}px every {_settings.IntervalMs} ms. Select to start.",
                IcoPath = IconPath,
                Score = 100,
                Action = _ => { Start(); return true; }
            }
        ];
    }

    // ---------------------------------------------------------------- engine

    private void Start()
    {
        if (_running) 
            return;
            
        _running = true;
        int intervalMilliseconds = Math.Max(MouseMoverSettings.MinInterval, _settings.IntervalMs);
        _timer = new Timer(OnTick, null, 250, intervalMilliseconds);
        _context.API.ShowMsg("Mouse Mover", "Mouse Mover Enabled", IconPath);
    }

    private void Stop()
    {
        if (!_running) 
            return;

        _running = false;
        _timer?.Dispose();
        _timer = null;
        _context.API.ShowMsg("Mouse Mover", "Mouse Mover Disabled", IconPath);
    }

    private void OnTick(object? state)
    {
        if (!_running) 
            return;

        if (Interlocked.CompareExchange(ref _gliding, 1, 0) != 0) 
            return;

        try
        {
            int intervalMilliseconds = Math.Max(MouseMoverSettings.MinInterval, _settings.IntervalMs);

            _timer?.Change(intervalMilliseconds, intervalMilliseconds);

            if (!GetCursorPos(out POINT startPosition)) 
                return;

            (int virtualScreenX, int virtualScreenY, int virtualScreenWidth, int virtualScreenHeight) = GetVirtualScreenBounds();
            POINT targetPosition = PickRandomTarget(startPosition, virtualScreenX, virtualScreenY, virtualScreenWidth, virtualScreenHeight);
            GlideTo(startPosition, targetPosition);
        }
        finally
        {
            Interlocked.Exchange(ref _gliding, 0);
        }
    }

    private POINT PickRandomTarget(POINT fromPosition, int virtualScreenX, int virtualScreenY, int virtualScreenWidth, int virtualScreenHeight)
    {
        double angle = _random.NextDouble() * Math.PI * 2;
        double distance = Math.Sqrt(_random.NextDouble()) * _settings.Radius;

        int targetX = (int)Math.Round(fromPosition.X + Math.Cos(angle) * distance);
        int targetY = (int)Math.Round(fromPosition.Y + Math.Sin(angle) * distance);

        targetX = Math.Clamp(targetX, virtualScreenX, virtualScreenX + virtualScreenWidth - 1);
        targetY = Math.Clamp(targetY, virtualScreenY, virtualScreenY + virtualScreenHeight - 1);

        return new POINT { X = targetX, Y = targetY };
    }

    private void GlideTo(POINT fromPosition, POINT toPosition)
    {
        int glideMilliseconds = _settings.GlideMs;
        int stepMilliseconds = Math.Max(MouseMoverSettings.MinStep, _settings.StepMs);

        if (glideMilliseconds <= 0)
        {
            SetCursorPos(toPosition.X, toPosition.Y);
            return;
        }

        int stepCount = Math.Max(1, glideMilliseconds / stepMilliseconds);

        for (int stepIndex = 1; stepIndex <= stepCount; stepIndex++)
        {
            if (!_running) 
                return;

            double progress = (double)stepIndex / stepCount;
            double easedProgress = EaseInOut(progress);

            int interpolatedX = (int)Math.Round(fromPosition.X + (toPosition.X - fromPosition.X) * easedProgress);
            int interpolatedY = (int)Math.Round(fromPosition.Y + (toPosition.Y - fromPosition.Y) * easedProgress);

            SetCursorPos(interpolatedX, interpolatedY);
            Thread.Sleep(stepMilliseconds);
        }
    }

    private static double EaseInOut(double progress) => progress < 0.5 ? 2 * progress * progress : 1 - Math.Pow(-2 * progress + 2, 2) / 2;

    public void Dispose()
    {
        // Released on plugin unload/reload so a reload can't orphan a running timer
        // (which would keep moving the cursor with no UI left to stop it).
        _running = false;
        _timer?.Dispose();
        _timer = null;
    }

    // ---------------------------------------------------------------- Win32

    private static (int x, int y, int width, int height) GetVirtualScreenBounds()
    {
        int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        if (width <= 0 || height <= 0)
        {
            x = 0;
            y = 0;
            width = GetSystemMetrics(SM_CXSCREEN);
            height = GetSystemMetrics(SM_CYSCREEN);
        }
        
        return (x, y, width, height);
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
