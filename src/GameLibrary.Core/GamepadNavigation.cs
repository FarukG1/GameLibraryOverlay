namespace GameLibrary.Core;

public enum LibraryAction { Left, Right, Up, Down, Launch, Close, Favorite, Settings }

/// <summary>Pure XInput button/axis translation with edge triggering and bounded direction repeat.</summary>
public sealed class GamepadNavigation
{
    private ushort previous;
    private LibraryAction? held;
    private long nextRepeat;
    public void Reset() { previous = 0; held = null; nextRepeat = 0; }
    public IReadOnlyList<LibraryAction> Read(ushort buttons, short x, short y, long milliseconds)
    {
        if (buttons == 0 && previous == 0 && held is null && Math.Abs((int)x) <= 16000 && Math.Abs((int)y) <= 16000) return [];
        var actions = new List<LibraryAction>(2);
        var pressed = buttons & ~previous;
        previous = buttons;
        foreach (var (mask, action) in new (int, LibraryAction)[]
        { (0x1000, LibraryAction.Launch), (0x2000, LibraryAction.Close), (0x4000, LibraryAction.Favorite), (0x8000, LibraryAction.Settings) })
            if ((pressed & mask) != 0) actions.Add(action);
        LibraryAction? direction = (buttons & 0x0004) != 0 ? LibraryAction.Left : (buttons & 0x0008) != 0 ? LibraryAction.Right :
            (buttons & 0x0101) != 0 ? LibraryAction.Up : (buttons & 0x0202) != 0 ? LibraryAction.Down :
            Math.Abs((int)x) > 16000 && Math.Abs((int)x) > Math.Abs((int)y) ? x < 0 ? LibraryAction.Left : LibraryAction.Right :
            Math.Abs((int)y) > 16000 ? y > 0 ? LibraryAction.Up : LibraryAction.Down : null;
        if (direction != held)
        { held = direction; nextRepeat = milliseconds + 350; if (direction is { } first) actions.Add(first); }
        else if (direction is { } repeat && milliseconds >= nextRepeat)
        { actions.Add(repeat); nextRepeat = milliseconds + 120; }
        return actions;
    }
}
