using System.Diagnostics;
using GameLibrary.Core;
using GameLibrary.Infrastructure;

try { return await RunAsync(args); }
catch (Exception ex) { Console.Error.WriteLine("Test runner failed: " + ex); return 1; }

static async Task<int> RunAsync(string[] args)
{
var failures = new List<string>();
var count = 0;
void Check(bool condition, string message) { count++; if (!condition) failures.Add(message); }
void Throws(Action action, string message)
{
    try { action(); Check(false, message); }
    catch (InvalidDataException) { Check(true, message); }
}
var root = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/test-data");
Directory.CreateDirectory(root);
var run = Path.Combine(root, Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(run);

var parsed = Vdf.Parse("// comment\n\"root\" { \"Path\" \"D:\\\\Steam Library\" \"title\" \"A \\\"quoted\\\" name\" bare value }");
Check(parsed["ROOT"]?.Text("path") == @"D:\Steam Library", "VDF handles escaped paths and case-insensitive keys");
Check(parsed["root"]?.Text("title") == "A \"quoted\" name", "VDF handles quoted names");
Check(parsed["root"]?.Text("bare") == "value", "VDF handles unquoted tokens");
Throws(() => Vdf.Parse("\"root\" { \"key\" \"unfinished"), "VDF rejects partial writes");
Throws(() => Vdf.Parse("}"), "VDF rejects unmatched closing braces");
Throws(() => Vdf.Parse(string.Concat(Enumerable.Repeat("a { ", 70))), "VDF limits nesting");

var store = new SettingsService(Path.Combine(run, "state"));
var settings = store.LoadSettings();
settings.For("steam:730").Favorite = true;
store.SaveSettings(settings);
settings.Categories.Add("Test category");
store.SaveSettings(settings);
Check(store.LoadSettings().For("steam:730").Favorite, "Settings round trip preserves favorites");
File.WriteAllText(Path.Combine(store.Root, "settings.json"), "{invalid");
Check(store.LoadSettings().For("steam:730").Favorite, "Corrupt settings recover from backup");
Check(Directory.EnumerateFiles(store.Root, "*.corrupt-*").Any(), "Recovery preserves corrupt source");
File.WriteAllText(Path.Combine(store.Root, "settings.json"), "{\"SchemaVersion\":2}");
Throws(() => store.LoadSettings(), "Future schemas are not silently overwritten");

var steam = Path.Combine(run, "Steam");
var secondary = Path.Combine(run, "Second library");
var missing = Path.Combine(run, "Offline library");
Directory.CreateDirectory(Path.Combine(steam, "config"));
Directory.CreateDirectory(Path.Combine(steam, "steamapps", "common", "Sample"));
Directory.CreateDirectory(Path.Combine(secondary, "steamapps", "common", "Other"));
Directory.CreateDirectory(Path.Combine(steam, "userdata", "123", "config"));
Directory.CreateDirectory(Path.Combine(steam, "appcache", "librarycache", "42"));
string Escape(string value) => value.Replace("\\", "\\\\");
File.WriteAllText(Path.Combine(steam, "config", "libraryfolders.vdf"), $"\"libraryfolders\" {{ \"0\" {{ \"path\" \"{Escape(steam)}\" }} \"1\" {{ \"path\" \"{Escape(secondary)}\" }} \"2\" \"{Escape(missing)}\" }}");
File.WriteAllText(Path.Combine(steam, "steamapps", "appmanifest_42.acf"), "\"AppState\" { \"appid\" \"42\" \"name\" \"Sample Game\" \"installdir\" \"Sample\" \"StateFlags\" \"4\" }");
File.WriteAllText(Path.Combine(secondary, "steamapps", "appmanifest_43.acf"), "\"AppState\" { \"appid\" \"43\" \"name\" \"Other Game\" \"installdir\" \"Other\" \"StateFlags\" \"1026\" }");
File.WriteAllText(Path.Combine(secondary, "steamapps", "appmanifest_44.acf"), "\"AppState\" { \"appid\" \"44\" \"name\" \"Unsafe\" \"installdir\" \"..\\\\..\\\\escape\" }");
File.WriteAllText(Path.Combine(steam, "userdata", "123", "config", "localconfig.vdf"), "\"UserLocalConfigStore\" { \"Software\" { \"Valve\" { \"Steam\" { \"apps\" { \"42\" { \"LastPlayed\" \"1700000000\" \"Playtime\" \"600\" } } } } } }");
File.WriteAllBytes(Path.Combine(steam, "appcache", "librarycache", "42", "library_600x900.jpg"), []);
var provider = new SteamProvider();
var scan = await provider.ScanAsync(new Settings { SteamDirectory = steam, SteamUserId = "123" }, CancellationToken.None);
Check(scan.Games.Count == 2, "Discovery supports multiple libraries and rejects path traversal");
var sample = scan.Games.Single(g => g.SteamAppId == 42);
Check(sample.Installed && sample.SteamMinutes == 600 && sample.LastPlayed is not null, "Installed state and account-local playtime are imported");
Check(!scan.Games.Single(g => g.SteamAppId == 43).Installed, "Incomplete downloads are not marked installed");
Check(sample.CoverPath?.EndsWith("library_600x900.jpg") == true, "Per-app Steam artwork is found");
File.Delete(Path.Combine(steam, "appcache", "librarycache", "42", "library_600x900.jpg"));
var hashedArtwork = Path.Combine(steam, "appcache", "librarycache", "42", "asset-hash");
Directory.CreateDirectory(hashedArtwork);
File.WriteAllBytes(Path.Combine(hashedArtwork, "library_capsule.jpg"), []);
var newArtwork = await provider.ScanAsync(new Settings { SteamDirectory = steam, SteamUserId = "123" }, CancellationToken.None);
Check(newArtwork.Games.Single(g => g.SteamAppId == 42).CoverPath?.EndsWith("library_capsule.jpg") == true, "Hashed Steam capsule artwork is found");
Check(scan.UnavailableLibraries.Contains(missing), "Offline libraries are reported for cache preservation");
Check(scan.Warnings.Any(w => w.Contains("Invalid install directory")), "Invalid manifest paths produce a diagnostic");
store.SaveCache(scan.Games);
Check(store.LoadCache().Games.Count == 2, "Library cache round trips");

// Steam shortcuts are binary KeyValues, not text manifests. Use the canonical stored unsigned ID.
var shortcutExe = Path.Combine(secondary, "steamapps", "common", "Other", "external-game.exe");
File.WriteAllBytes(shortcutExe, []);
const uint shortcutId = 0xABCDEF01;
using (var file = File.Create(Path.Combine(steam, "userdata", "123", "config", "shortcuts.vdf")))
using (var writer = new BinaryWriter(file, System.Text.Encoding.UTF8))
{
    void Text(string text) { writer.Write(System.Text.Encoding.UTF8.GetBytes(text)); writer.Write((byte)0); }
    void StringField(string key, string value) { writer.Write((byte)1); Text(key); Text(value); }
    void IntField(string key, uint value) { writer.Write((byte)2); Text(key); writer.Write(value); }
    writer.Write((byte)0); Text("shortcuts"); writer.Write((byte)0); Text("0");
    IntField("appid", shortcutId); StringField("AppName", "Non-Steam game ü"); StringField("Exe", "\"" + shortcutExe + "\"");
    StringField("LaunchOptions", "-test"); IntField("LastPlayTime", 1700000000);
    writer.Write((byte)0); Text("tags"); StringField("0", "External"); writer.Write((byte)8);
    writer.Write((byte)8); writer.Write((byte)8); writer.Write((byte)8);
}
var shortcutGrid = Path.Combine(steam, "userdata", "123", "config", "grid");
Directory.CreateDirectory(shortcutGrid);
File.WriteAllBytes(Path.Combine(shortcutGrid, shortcutId + ".png"), []);
File.WriteAllBytes(Path.Combine(shortcutGrid, shortcutId + "_hero.jpg"), []);
File.WriteAllBytes(Path.Combine(shortcutGrid, shortcutId + "_logo.png"), []);
var shortcutScan = await provider.ScanAsync(new Settings { SteamDirectory = steam, SteamUserId = "123" }, CancellationToken.None);
var shortcut = shortcutScan.Games.Single(g => g.Source == GameSource.SteamShortcut);
Check(shortcut.Name == "Non-Steam game ü" && shortcut.SteamAppId == shortcutId, "Binary shortcut UTF-8 names and unsigned IDs are preserved");
Check(shortcut.ProcessExecutable == shortcutExe && shortcut.Installed && shortcut.LastPlayed is not null, "Shortcut target and recent activity are imported");
Check(shortcut.WideCoverPath?.EndsWith(shortcutId + ".png") == true && shortcut.BackgroundPath is not null && shortcut.LogoPath is not null, "Shortcut wide artwork, background, and logo are found");
Check(GameLauncher.ShortcutGameId(shortcutId) == 0xABCDEF0102000000UL, "Shortcut launch ID uses the stored app ID, not a recomputed hash");
Check(GameLauncher.SteamUri(shortcut) == "steam://rungameid/12379813738318921728", "Non-Steam shortcuts launch through Steam with the full 64-bit ID");
Throws(() => BinaryVdf.Parse(new MemoryStream([0, (byte)'x', 0])), "Truncated binary shortcut files are rejected");
Throws(() => BinaryVdf.Parse(new MemoryStream([99, (byte)'x', 0])), "Unknown binary types are rejected");

var pad = new GamepadNavigation();
Check(pad.Read(0, 1000, 1000, 0).Count == 0, "Controller deadzone ignores stick drift");
Check(pad.Read(8, 0, 0, 0).SequenceEqual([LibraryAction.Right]), "D-pad press moves once immediately");
Check(pad.Read(8, 0, 0, 100).Count == 0, "Held D-pad waits before repeat");
Check(pad.Read(8, 0, 0, 350).SequenceEqual([LibraryAction.Right]), "Held D-pad repeats after delay");
pad.Reset();
Check(pad.Read(0x1000, 0, 0, 0).SequenceEqual([LibraryAction.Launch]), "Xbox A launches");
Check(pad.Read(0x1000, 0, 0, 1000).Count == 0, "Holding A cannot repeatedly launch");
pad.Reset();
Check(pad.Read(0, -24000, 0, 0).SequenceEqual([LibraryAction.Left]), "Left stick navigates");
pad.Reset();
Check(pad.Read(0x2000, 0, 0, 0).SequenceEqual([LibraryAction.Close]), "Xbox B closes the overlay");
pad.Reset();
Check(pad.Read(0xC000, 0, 0, 0).SequenceEqual([LibraryAction.Favorite, LibraryAction.Settings]), "Xbox X and Y map to favorite and settings");

var prefs = new GamePreferences();
GameStatistics.RecordSession(sample, prefs, 10, DateTimeOffset.UtcNow);
Check(GameStatistics.Minutes(sample, prefs) == 610, "Local sessions extend the imported baseline");
Check(GameStatistics.Minutes(sample with { SteamMinutes = 610 }, prefs) == 610, "Updated Steam totals are not double counted");
Check(GameStatistics.Minutes(sample with { SteamMinutes = null }, null) is null, "Missing playtime remains unknown");
Check(GameProcessMonitor.Matches(sample, Path.Combine(sample.InstallDirectory!, "bin", "game.exe")), "Game executable matches its install directory");
Check(!GameProcessMonitor.Matches(sample, sample.InstallDirectory + "-other\\game.exe"), "Process matching checks directory boundaries");
Check(!GameProcessMonitor.Matches(sample, Path.Combine(sample.InstallDirectory!, "REDlauncher.exe")), "Launcher bootstrapper is excluded");
Check(CustomGameProvider.Validate(new CustomGame { Name = "URI game", Target = "com.epicgames.launcher://apps/example?action=launch" }) is null, "Custom launcher URIs are accepted");
Check(CustomGameProvider.Validate(new CustomGame { Name = "Missing", Target = Path.Combine(run, "missing.exe") }) is not null, "Missing custom executables are rejected");
Check(!GameProcessMonitor.Matches(new Game { Id = "custom:x", Name = "URI", Source = GameSource.Custom, LaunchTarget = "launcher://game" }, "C:\\launcher.exe"), "URI-only entries do not guess an unrelated process");

var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
using (var watcher = new LibraryWatcher(() => changed.TrySetResult()))
{
    watcher.Reset([Path.Combine(steam, "steamapps")]);
    File.AppendAllText(Path.Combine(steam, "steamapps", "appmanifest_42.acf"), "\n");
    await Task.WhenAny(changed.Task, Task.Delay(4000));
    Check(changed.Task.IsCompleted, "Filesystem events trigger reconciliation");
}

if (args.Length > 1 && File.Exists(args[1]))
{
    var events = new List<SessionUpdate>();
    using var monitor = new GameProcessMonitor(Console.WriteLine);
    monitor.Changed += update => { lock (events) events.Add(update); };
    var game = new Game { Id = "custom:fixture", Name = "Test process", Source = GameSource.Custom, LaunchTarget = Path.GetFullPath(args[1]), Arguments = "2500", Installed = true };
    var task = monitor.LaunchAsync(game);
    var completed = await Task.WhenAny(task, Task.Delay(20000));
    Check(completed == task, "A launched process is detected and its exit completes tracking");
    if (completed == task) await task;
    lock (events)
    {
        Check(events.Select(e => e.State).SequenceEqual([SessionState.Starting, SessionState.Running, SessionState.Exited]), "Session transitions are Starting → Running → Exited");
        Check(events.LastOrDefault()?.Minutes is > 0 and < 0.15, "Observed session duration is bounded");
    }
}
Console.WriteLine($"{count - failures.Count}/{count} checks passed.");
foreach (var failure in failures) Console.Error.WriteLine("FAIL: " + failure);
return failures.Count == 0 ? 0 : 1;
}
