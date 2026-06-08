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
    private LowLevelMouseProc? _mouseHookCallback;
    private IntPtr _mouseHookHandle = IntPtr.Zero;
    private volatile bool _running;
    private int _gliding; // 0 = idle, 1 = a glide is in progress (overlap guard)
    private int _autoDisableQueued;
    private int _pendingProgrammaticMoveEvents;
    private int _lastProgrammaticX;
    private int _lastProgrammaticY;

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
        SynchronizeMouseHook();
        int intervalMilliseconds = Math.Max(MouseMoverSettings.MinInterval, _settings.IntervalMs);
        _timer = new Timer(OnTick, null, 250, intervalMilliseconds);
        _context.API.ShowMsg("Mouse Mover", "Mouse Mover Enabled", IconPath);
    }

    private void Stop(string notificationMessage = "Mouse Mover Disabled")
    {
        if (!_running) 
        {
            Interlocked.Exchange(ref _autoDisableQueued, 0);
            return;
        }

        _running = false;
        _timer?.Dispose();
        _timer = null;
        UninstallMouseHook();
        Interlocked.Exchange(ref _autoDisableQueued, 0);
        _context.API.ShowMsg("Mouse Mover", notificationMessage, IconPath);
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
            SynchronizeMouseHook();

            if (!GetCursorPos(out POINT startPosition)) 
                return;

            (int boundsX, int boundsY, int boundsWidth, int boundsHeight) = GetTargetBounds(startPosition);
            POINT targetPosition = PickRandomTarget(startPosition, boundsX, boundsY, boundsWidth, boundsHeight);
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
            SetCursorPosFromPlugin(toPosition.X, toPosition.Y);
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

            SetCursorPosFromPlugin(interpolatedX, interpolatedY);
            Thread.Sleep(stepMilliseconds);
        }
    }

    private (int x, int y, int width, int height) GetTargetBounds(POINT startPosition)
    {
        if (_settings.RestrictMovementToCurrentMonitor && TryGetMonitorBounds(startPosition, out (int x, int y, int width, int height) monitorBounds))
            return monitorBounds;

        return GetVirtualScreenBounds();
    }

    private void SetCursorPosFromPlugin(int x, int y)
    {
        Volatile.Write(ref _lastProgrammaticX, x);
        Volatile.Write(ref _lastProgrammaticY, y);
        Interlocked.Exchange(ref _pendingProgrammaticMoveEvents, 8);
        SetCursorPos(x, y);
    }

    private void SynchronizeMouseHook()
    {
        if (_running && _settings.AutoDisableOnUserMouseInput)
        {
            InstallMouseHook();
            return;
        }

        UninstallMouseHook();
    }

    private void InstallMouseHook()
    {
        if (_mouseHookHandle != IntPtr.Zero)
            return;

        _mouseHookCallback ??= OnMouseHook;
        IntPtr moduleHandle = GetModuleHandle(null);
        IntPtr mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, _mouseHookCallback, moduleHandle, 0);

        if (mouseHookHandle != IntPtr.Zero)
            _mouseHookHandle = mouseHookHandle;
    }

    private void UninstallMouseHook()
    {
        if (_mouseHookHandle == IntPtr.Zero)
            return;

        UnhookWindowsHookEx(_mouseHookHandle);
        _mouseHookHandle = IntPtr.Zero;
    }

    private IntPtr OnMouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _running && _settings.AutoDisableOnUserMouseInput)
        {
            MSLLHOOKSTRUCT mouseHookData = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

            if (IsTrackedMouseMessage(wParam) && !IsProgrammaticMouseInput(wParam, mouseHookData))
                QueueAutoDisable();
        }

        return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    private void QueueAutoDisable()
    {
        if (Interlocked.CompareExchange(ref _autoDisableQueued, 1, 0) != 0)
            return;

        ThreadPool.QueueUserWorkItem(_ => Stop("Mouse input detected. Mouse Mover disabled."));
    }

    private static bool IsTrackedMouseMessage(IntPtr wParam)
    {
        int message = wParam.ToInt32();

        return message is WM_MOUSEMOVE
            or WM_LBUTTONDOWN
            or WM_LBUTTONUP
            or WM_RBUTTONDOWN
            or WM_RBUTTONUP
            or WM_MBUTTONDOWN
            or WM_MBUTTONUP
            or WM_MOUSEWHEEL
            or WM_XBUTTONDOWN
            or WM_XBUTTONUP
            or WM_MOUSEHWHEEL;
    }

    private bool IsProgrammaticMouseInput(IntPtr wParam, MSLLHOOKSTRUCT mouseHookData)
    {
        if ((mouseHookData.flags & (LLMHF_INJECTED | LLMHF_LOWER_IL_INJECTED)) != 0)
            return true;

        if (wParam.ToInt32() != WM_MOUSEMOVE)
            return false;

        if (Volatile.Read(ref _pendingProgrammaticMoveEvents) <= 0)
            return false;

        int lastProgrammaticX = Volatile.Read(ref _lastProgrammaticX);
        int lastProgrammaticY = Volatile.Read(ref _lastProgrammaticY);

        if (mouseHookData.pt.X != lastProgrammaticX || mouseHookData.pt.Y != lastProgrammaticY)
            return false;

        Interlocked.Decrement(ref _pendingProgrammaticMoveEvents);
        return true;
    }

    private static double EaseInOut(double progress) => progress < 0.5 ? 2 * progress * progress : 1 - Math.Pow(-2 * progress + 2, 2) / 2;

    public void Dispose()
    {
        // Released on plugin unload/reload so a reload can't orphan a running timer
        // (which would keep moving the cursor with no UI left to stop it).
        _running = false;
        _timer?.Dispose();
        _timer = null;
        UninstallMouseHook();
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

    private static bool TryGetMonitorBounds(POINT point, out (int x, int y, int width, int height) bounds)
    {
        IntPtr monitorHandle = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);

        if (monitorHandle == IntPtr.Zero)
        {
            bounds = default;
            return false;
        }

        MONITORINFO monitorInfo = new()
        {
            cbSize = Marshal.SizeOf<MONITORINFO>()
        };

        if (!GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            bounds = default;
            return false;
        }

        int width = monitorInfo.rcMonitor.Right - monitorInfo.rcMonitor.Left;
        int height = monitorInfo.rcMonitor.Bottom - monitorInfo.rcMonitor.Top;

        if (width <= 0 || height <= 0)
        {
            bounds = default;
            return false;
        }

        bounds = (monitorInfo.rcMonitor.Left, monitorInfo.rcMonitor.Top, width, height);
        return true;
    }

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const int MONITOR_DEFAULTTONEAREST = 2;
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_XBUTTONUP = 0x020C;
    private const int WM_MOUSEHWHEEL = 0x020E;
    private const int LLMHF_INJECTED = 0x00000001;
    private const int LLMHF_LOWER_IL_INJECTED = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, int dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
