using System.Windows;
using System.Windows.Input;
using Forms = System.Windows.Forms;

namespace GameLibrary.App;

/// <summary>Application-wide input mode; stationary pointers do not override controller navigation.</summary>
public sealed class InputMode : DependencyObject
{
    public static InputMode Current { get; } = new();
    public static readonly DependencyProperty IsGamepadProperty = DependencyProperty.Register(nameof(IsGamepad), typeof(bool), typeof(InputMode), new PropertyMetadata(false));
    public bool IsGamepad { get => (bool)GetValue(IsGamepadProperty); private set => SetValue(IsGamepadProperty, value); }
    private System.Drawing.Point lastPointer;
    public static void Initialize()
    {
        Current.lastPointer = Forms.Cursor.Position;
        EventManager.RegisterClassHandler(typeof(Window), Mouse.PreviewMouseMoveEvent, new MouseEventHandler((_, _) => Current.PointerMoved()), true);
        EventManager.RegisterClassHandler(typeof(Window), Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler((_, _) => Current.UseMouse()), true);
        EventManager.RegisterClassHandler(typeof(Window), Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler((_, _) => Current.UseMouse()), true);
    }
    private void PointerMoved()
    {
        var position = Forms.Cursor.Position;
        if (position == lastPointer) return;
        lastPointer = position;
        IsGamepad = false;
    }
    internal void UseGamepad() { lastPointer = Forms.Cursor.Position; IsGamepad = true; }
    internal void UseMouse() { lastPointer = Forms.Cursor.Position; IsGamepad = false; }
}
