using System.Runtime.InteropServices;
using System.Windows.Threading;
using GameLibrary.Core;

namespace GameLibrary.App;

/// <summary>XInput exposes state reads, not button events. Poll only while the overlay is active.</summary>
internal sealed class GamepadService : IDisposable
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly GamepadNavigation navigation = new();
    private readonly Action<LibraryAction> action;
    private int connected = -1;
    private long nextDiscovery;
    private bool suppressHeldButtons = true;
    public bool IsRunning => timer.IsEnabled;
    public GamepadService(Action<LibraryAction> action) { this.action = action; timer.Tick += Tick; }
    public void Start() { navigation.Reset(); suppressHeldButtons = true; nextDiscovery = 0; timer.Start(); }
    public void Stop() { timer.Stop(); navigation.Reset(); }
    private void Tick(object? sender, EventArgs e)
    {
        try
        {
            var now = Environment.TickCount64;
            if (connected < 0)
            {
                if (now < nextDiscovery) return;
                nextDiscovery = now + 2000;
                for (uint i = 0; i < 4; i++) if (XInputGetState(i, out _) == 0) { connected = (int)i; break; }
                if (connected < 0) return;
            }
            if (XInputGetState((uint)connected, out var state) != 0) { connected = -1; navigation.Reset(); suppressHeldButtons = true; return; }
            // A held launch button must not launch a second game when focus returns.
            if (suppressHeldButtons)
            { if (state.Gamepad.Buttons != 0 || Math.Abs((int)state.Gamepad.X) > 16000 || Math.Abs((int)state.Gamepad.Y) > 16000) return; suppressHeldButtons = false; }
            foreach (var value in navigation.Read(state.Gamepad.Buttons, state.Gamepad.X, state.Gamepad.Y, now))
            { if (!timer.IsEnabled) break; action(value); }
        }
        catch (DllNotFoundException) { Stop(); }
        catch (EntryPointNotFoundException) { Stop(); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Gamepad
    { public ushort Buttons; public byte LeftTrigger; public byte RightTrigger; public short X; public short Y; public short RightX; public short RightY; }
    [StructLayout(LayoutKind.Sequential)] private struct State { public uint Packet; public Gamepad Gamepad; }
    [DllImport("xinput1_4.dll")] private static extern uint XInputGetState(uint index, out State state);
    public void Dispose() { Stop(); timer.Tick -= Tick; }
}
