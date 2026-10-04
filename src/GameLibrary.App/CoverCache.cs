using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameLibrary.App;

public sealed class CoverCache
{
    public static CoverCache Shared { get; } = new();
    private readonly SemaphoreSlim decodeSlots = new(2, 2);
    private readonly object gate = new();
    private readonly Dictionary<string, (BitmapSource Image, long Bytes, LinkedListNode<string> Node)> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> order = new();
    private long usedBytes;
    public async Task<BitmapSource?> LoadAsync(string path, int targetWidth, int targetHeight, bool crop, CancellationToken token)
    {
        string key;
        try { key = $"{path}|{File.GetLastWriteTimeUtc(path).Ticks}|{targetWidth}x{targetHeight}|{crop}"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
        lock (gate)
        {
            if (cache.TryGetValue(key, out var cached)) { order.Remove(cached.Node); order.AddLast(cached.Node); return cached.Image; }
        }
        await decodeSlots.WaitAsync(token);
        try
        {
            token.ThrowIfCancellationRequested();
            var image = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > 24 * 1024 * 1024) return null;
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                var frame = decoder.Frames[0];
                var sourceWidth = frame.PixelWidth;
                var sourceHeight = frame.PixelHeight;
                if (sourceWidth <= 0 || sourceHeight <= 0) return null;
                var x = (double)targetWidth / sourceWidth;
                var y = (double)targetHeight / sourceHeight;
                var scale = Math.Min(1, crop ? Math.Max(x, y) : Math.Min(x, y));
                // Decode enough pixels for the cropped area at the monitor's DPI, never upscale the source in the cache.
                var decodeWidth = Math.Clamp((int)Math.Ceiling(sourceWidth * scale), 1, 2048);
                var decodeHeight = (double)sourceHeight * decodeWidth / sourceWidth;
                if (decodeHeight * decodeWidth * 4 > 64 * 1024 * 1024) return null;
                stream.Position = 0;
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = decodeWidth;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }, token);
            token.ThrowIfCancellationRequested();
            if (image is null) return null;
            lock (gate)
            {
                if (cache.TryGetValue(key, out var existing)) return existing.Image;
                var bytes = (long)image.PixelWidth * image.PixelHeight * 4;
                if (bytes > 64 * 1024 * 1024) return null;
                cache[key] = (image, bytes, order.AddLast(key));
                usedBytes += bytes;
                Trim(64 * 1024 * 1024);
            }
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException) { return null; }
        finally { decodeSlots.Release(); }
    }
    public void Trim(long maximum)
    {
        lock (gate)
            while (usedBytes > maximum && order.First is { } first)
            { usedBytes -= cache[first.Value].Bytes; cache.Remove(first.Value); order.RemoveFirst(); }
    }
}

public sealed class CoverImage : Image
{
    public static readonly DependencyProperty CoverPathProperty = DependencyProperty.Register(nameof(CoverPath), typeof(string), typeof(CoverImage),
        new PropertyMetadata(null, (value, _) => ((CoverImage)value).Refresh(true)));
    private CancellationTokenSource? pending;
    private string? requested;
    public string? CoverPath { get => (string?)GetValue(CoverPathProperty); set => SetValue(CoverPathProperty, value); }
    public CoverImage()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        Loaded += (_, _) => Refresh();
        SizeChanged += (_, _) => Refresh();
        Unloaded += (_, _) => { pending?.Cancel(); requested = null; Source = null; };
    }
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) { base.OnDpiChanged(oldDpi, newDpi); Refresh(); }
    private async void Refresh(bool pathChanged = false)
    {
        if (pathChanged) { requested = null; Source = null; }
        if (!IsLoaded || string.IsNullOrWhiteSpace(CoverPath)) { pending?.Cancel(); return; }
        var dpi = VisualTreeHelper.GetDpi(this);
        static int Bucket(double value) => Math.Clamp((int)Math.Ceiling(value / 64) * 64, 64, 2048);
        var width = Bucket(ActualWidth * dpi.DpiScaleX);
        var height = Bucket(ActualHeight * dpi.DpiScaleY);
        var crop = Stretch == Stretch.UniformToFill;
        var requestKey = $"{CoverPath}|{width}x{height}|{crop}";
        if (requested == requestKey) return;
        requested = requestKey;
        pending?.Cancel();
        var request = new CancellationTokenSource();
        pending = request;
        try
        {
            var image = await CoverCache.Shared.LoadAsync(CoverPath, width, height, crop, request.Token);
            if (!request.IsCancellationRequested && IsLoaded) Source = image;
        }
        catch (OperationCanceledException) { }
        finally { if (pending == request) pending = null; request.Dispose(); }
    }
}
