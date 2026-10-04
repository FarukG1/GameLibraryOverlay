using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace GameLibrary.App;

internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly string name;
    private readonly CancellationTokenSource lifetime = new();
    public bool IsFirst { get; }
    public SingleInstance()
    {
        name = "GameLibrary-" + WindowsIdentity.GetCurrent().User!.Value;
        mutex = new Mutex(true, @"Local\" + name, out var first);
        IsFirst = first;
    }
    public async Task NotifyAsync(byte command = 1)
    {
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(3000);
        try { await pipe.ConnectAsync(timeout.Token); await pipe.WriteAsync(new byte[] { command }, timeout.Token); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or TimeoutException) { }
    }
    public Task ListenAsync(Action open, Action exit) => Task.Run(async () =>
    {
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(lifetime.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                timeout.CancelAfter(2000);
                var buffer = new byte[1];
                if (await pipe.ReadAsync(buffer, timeout.Token) == 1) { if (buffer[0] == 1) open(); else if (buffer[0] == 2) exit(); }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        }
    });
    public void Dispose() { lifetime.Cancel(); if (IsFirst) mutex.ReleaseMutex(); mutex.Dispose(); }
}

internal sealed class HotkeyService : IDisposable
{
    public static string Format(uint modifiers, uint key) => key == 0 ? "Disabled" :
        ((modifiers & 2) != 0 ? "Ctrl + " : "") + ((modifiers & 1) != 0 ? "Alt + " : "") +
        ((modifiers & 4) != 0 ? "Shift + " : "") + ((modifiers & 8) != 0 ? "Win + " : "") + System.Windows.Input.KeyInterop.KeyFromVirtualKey((int)key);
    private readonly HwndSource source;
    public event Action? Pressed;
    public HotkeyService()
    {
        source = new HwndSource(new HwndSourceParameters("GameLibrary.Hotkey") { ParentWindow = new IntPtr(-3) });
        source.AddHook(Hook);
    }
    public bool Register(uint modifiers, uint key)
    {
        UnregisterHotKey(source.Handle, 1);
        return key == 0 || RegisterHotKey(source.Handle, 1, modifiers | 0x4000, key);
    }
    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    { if (msg == 0x0312) { handled = true; Pressed?.Invoke(); } return IntPtr.Zero; }
    public void Dispose() { UnregisterHotKey(source.Handle, 1); source.Dispose(); }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}

internal static class NativeWindow
{
    public static void Place(Window window, string? monitor)
    {
        var screen = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == monitor) ?? Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
        var bounds = screen.WorkingArea;
        // Auto-hidden taskbars may report the entire screen as work area. Leave their edge accessible.
        if (bounds == screen.Bounds) bounds.Inflate(-1, -1);
        SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x0014);
    }
    public static bool DesktopHasFocus()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == GetShellWindow()) return true;
        var name = new System.Text.StringBuilder(256);
        GetClassName(foreground, name, name.Capacity);
        return name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd";
    }
    internal static bool FitsWorkArea(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var work = Forms.Screen.FromHandle(handle).WorkingArea;
        return GetWindowRect(handle, out var rect) && rect.Left >= work.Left && rect.Top >= work.Top && rect.Right <= work.Right && rect.Bottom <= work.Bottom;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder value, int count);
}

internal sealed class TrayService : IDisposable
{
    private const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly Forms.NotifyIcon icon;
    private readonly System.Drawing.Icon trayIcon;
    private readonly Forms.ToolStripMenuItem startup;
    public static bool StartsWithWindows
    { get { using var key = Registry.CurrentUser.OpenSubKey(StartupKey); return key?.GetValue("GameLibrary") is string; } }
    public TrayService(Action open, Action settings, Action scan, Action exit, Action<string> error, Action<bool> filter)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Library", null, (_, _) => open());
        var categories = new Forms.ToolStripMenuItem("Library view");
        categories.DropDownItems.Add("All games", null, (_, _) => filter(false));
        categories.DropDownItems.Add("Favorites", null, (_, _) => filter(true));
        menu.Items.Add(categories);
        menu.Items.Add("Settings", null, (_, _) => settings());
        menu.Items.Add("Rescan Steam", null, (_, _) => scan());
        menu.Items.Add(new Forms.ToolStripSeparator());
        startup = new Forms.ToolStripMenuItem("Start with Windows") { Checked = StartsWithWindows, CheckOnClick = false };
        startup.Click += (_, _) =>
        {
            try { SetStartup(!StartsWithWindows); startup.Checked = StartsWithWindows; }
            catch (Exception ex) { error("Could not change startup: " + ex.Message); }
        };
        menu.Items.Add(startup);
        menu.Items.Add("Exit", null, (_, _) => exit());
        using var resource = typeof(App).Assembly.GetManifestResourceStream("GameLibrary.App.Assets.library.ico");
        trayIcon = resource is null ? (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone() : new System.Drawing.Icon(resource);
        icon = new Forms.NotifyIcon { Text = "Game Library", Icon = trayIcon, ContextMenuStrip = menu, Visible = true };
        icon.DoubleClick += (_, _) => open();
        icon.BalloonTipClicked += (_, _) => open();
    }
    public static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(StartupKey);
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Application path is unavailable.");
            if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Launch GameLibrary.exe before enabling startup.");
            key.SetValue("GameLibrary", $"\"{executable}\" --background");
        }
        else key.DeleteValue("GameLibrary", false);
    }
    public void RefreshStartup() => startup.Checked = StartsWithWindows;
    public void Notify(string text) => icon.ShowBalloonTip(5000, "Game Library", text, Forms.ToolTipIcon.Info);
    public void Dispose() { icon.Visible = false; icon.ContextMenuStrip?.Dispose(); icon.Dispose(); trayIcon.Dispose(); }
}
