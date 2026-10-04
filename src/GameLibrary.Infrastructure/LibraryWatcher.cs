namespace GameLibrary.Infrastructure;

/// <summary>No polling: watched metadata changes are coalesced into a single reconciliation.</summary>
public sealed class LibraryWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> watchers = [];
    private readonly Timer debounce;
    private readonly Action changed;
    private readonly object gate = new();
    private bool disposed;
    public LibraryWatcher(Action changed)
    {
        this.changed = changed;
        debounce = new Timer(_ => changed(), null, Timeout.Infinite, Timeout.Infinite);
    }
    public void Reset(IEnumerable<string> directories)
    {
        lock (gate)
        {
            if (disposed) return;
            foreach (var watcher in watchers) watcher.Dispose();
            watchers.Clear();
            foreach (var directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(directory)) continue;
                try
                {
                    var watcher = new FileSystemWatcher(directory)
                    {
                        IncludeSubdirectories = false,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                        InternalBufferSize = 16 * 1024
                    };
                    watcher.Changed += OnChanged;
                    watcher.Created += OnChanged;
                    watcher.Deleted += OnChanged;
                    watcher.Renamed += OnChanged;
                    watcher.Error += (_, _) => Schedule();
                    watcher.EnableRaisingEvents = true;
                    watchers.Add(watcher);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
    private void OnChanged(object sender, FileSystemEventArgs args)
    {
        var extension = Path.GetExtension(args.FullPath);
        var name = Path.GetFileName(args.FullPath);
        if (extension.Equals(".acf", StringComparison.OrdinalIgnoreCase) || name.Equals("shortcuts.vdf", StringComparison.OrdinalIgnoreCase) || name.Equals("libraryfolders.vdf", StringComparison.OrdinalIgnoreCase) || name.Equals("localconfig.vdf", StringComparison.OrdinalIgnoreCase) || name.Equals("loginusers.vdf", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".png", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(extension)) Schedule();
    }
    private void Schedule() { lock (gate) { if (!disposed) debounce.Change(700, Timeout.Infinite); } }
    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            debounce.Dispose();
            foreach (var watcher in watchers) watcher.Dispose();
            watchers.Clear();
        }
    }
}
