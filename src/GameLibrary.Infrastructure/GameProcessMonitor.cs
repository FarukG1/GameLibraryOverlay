using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using GameLibrary.Core;

namespace GameLibrary.Infrastructure;

/// <summary>WMI startup events exist only during a launch/session. Process handles provide exit events.</summary>
public sealed class GameProcessMonitor : IDisposable
{
    private readonly Action<string> log;
    private readonly CancellationTokenSource lifetime = new();
    private int active;
    public bool IsActive => Volatile.Read(ref active) != 0;
    public event Action<SessionUpdate>? Changed;
    public GameProcessMonitor(Action<string> log) => this.log = log;

    public async Task LaunchAsync(Game game)
    {
        if (Interlocked.CompareExchange(ref active, 1, 0) != 0) throw new InvalidOperationException("A launch or game session is already being tracked.");
        try { await Task.Run(() => TrackAsync(game, lifetime.Token)); }
        finally { Interlocked.Exchange(ref active, 0); }
    }

    private async Task TrackAsync(Game game, CancellationToken token)
    {
        var processes = new ConcurrentDictionary<int, Process>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new Stopwatch();
        var gate = new object();
        var closed = false;
        ManagementEventWatcher? watcher = null;
        var eventAvailable = false;
        async Task SettleExit()
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), token);
                // An exit-triggered snapshot catches launcher handoffs when WMI is unavailable.
                Snapshot();
                lock (gate) { if (!closed && started.Task.IsCompleted && processes.IsEmpty) stopped.TrySetResult(); }
            }
            catch (OperationCanceledException) { }
        }
        void Observe(int pid)
        {
            lock (gate)
            {
                if (closed || stopped.Task.IsCompleted || processes.ContainsKey(pid) || !Matches(game, ProcessPath(pid))) return;
                Process? process = null;
                try
                {
                    process = Process.GetProcessById(pid);
                    if (!processes.TryAdd(pid, process)) { process.Dispose(); return; }
                    process.Exited += (_, _) =>
                    {
                        lock (gate)
                        {
                            if (processes.TryRemove(pid, out var exited)) exited.Dispose();
                            if (processes.IsEmpty) clock.Stop();
                        }
                        _ = SettleExit();
                    };
                    process.EnableRaisingEvents = true;
                    clock.Start();
                    if (!started.Task.IsCompleted)
                    {
                        Changed?.Invoke(new(game, SessionState.Running, $"Playing {game.Name}"));
                        started.TrySetResult();
                    }
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                { processes.TryRemove(pid, out _); process?.Dispose(); }
            }
        }
        void Snapshot()
        {
            foreach (var process in Process.GetProcesses())
            {
                using (process) { try { Observe(process.Id); } catch (InvalidOperationException) { } }
            }
        }
        async Task RetryObserveAsync(int pid)
        {
            try { await Task.Delay(150, token); Observe(pid); }
            catch (OperationCanceledException) { }
        }
        try
        {
            Changed?.Invoke(new(game, SessionState.Starting, $"Starting {game.Name}…"));
            if ((game.Source == GameSource.Custom && CustomGameProvider.IsUri(game.LaunchTarget) || game.Source == GameSource.SteamShortcut) && string.IsNullOrWhiteSpace(game.ProcessExecutable))
            {
                GameLauncher.Launch(game);
                Changed?.Invoke(new(game, SessionState.Unknown, "Launch sent. Add a game process executable in Settings to enable session tracking for this URI."));
                return;
            }
            try
            {
                watcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
                watcher.EventArrived += (_, e) =>
                {
                    try
                    {
                        var pid = Convert.ToInt32(e.NewEvent["ProcessID"]);
                        Observe(pid);
                        _ = RetryObserveAsync(pid);
                    }
                    catch (Exception ex) { log("Process event: " + ex.Message); }
                    finally { e.NewEvent.Dispose(); }
                };
                watcher.Start();
                eventAvailable = true;
            }
            catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
            { log("Process events unavailable; bounded startup checks: " + ex.Message); }
            GameLauncher.Launch(game);
            Snapshot();
            if (!eventAvailable)
            {
                // Only during startup, never an idle or running-game polling loop.
                for (var i = 0; i < 90 && !started.Task.IsCompleted; i++)
                { await Task.Delay(1000, token); Snapshot(); }
            }
            else
            {
                // Event paths may not yet be queryable at process creation. A few bounded reconciliation passes cover that race.
                for (var i = 0; i < 3 && !started.Task.IsCompleted; i++)
                { await Task.WhenAny(started.Task, Task.Delay(1000, token)); token.ThrowIfCancellationRequested(); Snapshot(); }
            }
            if (!started.Task.IsCompleted)
            {
                if (eventAvailable) await Task.WhenAny(started.Task, Task.Delay(TimeSpan.FromSeconds(87), token));
                token.ThrowIfCancellationRequested();
                if (!started.Task.IsCompleted)
                {
                    Changed?.Invoke(new(game, SessionState.Unknown, "Launch sent, but the game process could not be identified. Set a process override for custom games."));
                    return;
                }
            }
            await stopped.Task.WaitAsync(token);
            Changed?.Invoke(new(game, SessionState.Exited, $"Finished {game.Name}", clock.Elapsed.TotalMinutes));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { log(ex.ToString()); Changed?.Invoke(new(game, SessionState.Failed, "Launch failed: " + ex.Message)); }
        finally
        {
            lock (gate) closed = true;
            if (watcher is not null)
            {
                try { watcher.Stop(); } catch (Exception ex) when (ex is ManagementException or COMException) { log(ex.Message); }
                watcher.Dispose();
            }
            foreach (var process in processes.Values) process.Dispose();
            processes.Clear();
        }
    }


    public static bool Matches(Game game, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (!string.IsNullOrWhiteSpace(game.ProcessExecutable)) return path.Equals(game.ProcessExecutable, StringComparison.OrdinalIgnoreCase);
        if (game.Source == GameSource.Custom)
            return !CustomGameProvider.IsUri(game.LaunchTarget) && path.Equals(game.LaunchTarget, StringComparison.OrdinalIgnoreCase);
        if (game.InstallDirectory is null) return false;
        var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        if (name.Contains("launcher") || name.Contains("crash") || name.Contains("anticheat") || name.Contains("easyanti") ||
            name is "eac" or "beservice" or "battleye" or "setup" or "unins000" or "dxsetup" || name.StartsWith("vc_redist")) return false;
        var root = Path.GetFullPath(game.InstallDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ProcessPath(int pid)
    {
        var handle = OpenProcess(0x1000, false, pid);
        if (handle == IntPtr.Zero) return null;
        try { var buffer = new StringBuilder(32768); var length = buffer.Capacity; return QueryFullProcessImageName(handle, 0, buffer, ref length) ? buffer.ToString() : null; }
        finally { CloseHandle(handle); }
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr handle, uint flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
    public void Dispose() { lifetime.Cancel(); }
}
