using System.Globalization;
using Microsoft.Win32;
using GameLibrary.Core;

namespace GameLibrary.Infrastructure;

public sealed class SteamProvider : IGameProvider
{
    public static string? FindSteam()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            foreach (var name in new[] { @"Software\Valve\Steam", @"Software\WOW6432Node\Valve\Steam" })
                using (var key = hive.OpenSubKey(name))
                    if ((key?.GetValue("SteamPath") ?? key?.GetValue("InstallPath")) is string path && Directory.Exists(path)) return path;
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        return Directory.Exists(fallback) ? fallback : null;
    }

    public Task<ScanResult> ScanAsync(Settings settings, CancellationToken cancellationToken) => Task.Run(() => Scan(settings, cancellationToken), cancellationToken);

    private static ScanResult Scan(Settings settings, CancellationToken token)
    {
        var warnings = new List<string>();
        var unavailable = new List<string>();
        var watches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var games = new Dictionary<string, Game>();
        var steam = string.IsNullOrWhiteSpace(settings.SteamDirectory) ? FindSteam() : settings.SteamDirectory;
        if (steam is not null)
        {
            libraries.Add(Path.GetFullPath(steam));
            foreach (var relative in new[] { "steamapps", "config" })
            {
                var folder = Path.Combine(steam, relative);
                watches.Add(folder);
                var file = Path.Combine(folder, "libraryfolders.vdf");
                if (!File.Exists(file)) continue;
                try
                {
                    var root = Vdf.Read(file)["libraryfolders"];
                    if (root is null) continue;
                    foreach (var (key, value) in root.Children)
                        if (int.TryParse(key, out _) && (value.Text("path") ?? value.Value) is { Length: > 0 } path)
                            libraries.Add(Path.GetFullPath(path));
                }
                catch (Exception ex) when (IsMetadataError(ex)) { warnings.Add($"Could not read {file}: {ex.Message}"); }
            }
        }
        else warnings.Add("Steam was not found. Set its installation folder in Settings.");
        foreach (var path in settings.ExtraLibraries.Where(p => !string.IsNullOrWhiteSpace(p))) libraries.Add(Path.GetFullPath(path));
        var user = steam is null ? null : FindUser(steam, settings.SteamUserId);
        var stats = new Dictionary<string, (DateTimeOffset? Last, double? Minutes)>();
        if (user is not null)
        {
            var config = Path.Combine(user, "config");
            watches.Add(config);
            watches.Add(Path.Combine(config, "grid"));
            try
            {
                var file = Path.Combine(config, "localconfig.vdf");
                if (File.Exists(file))
                {
                    var apps = Vdf.Read(file)["UserLocalConfigStore"]?["Software"]?["Valve"]?["Steam"]?["apps"];
                    if (apps is not null)
                        foreach (var (id, node) in apps.Children)
                            stats[id] = (UnixTime(node.Text("LastPlayed")), Number(node.Text("Playtime")));
                }
            }
            catch (Exception ex) when (IsMetadataError(ex)) { warnings.Add("Steam playtime unavailable: " + ex.Message); }
            var shortcutsFile = Path.Combine(config, "shortcuts.vdf");
            if (File.Exists(shortcutsFile))
            {
                try
                {
                    var shortcuts = BinaryVdf.Read(shortcutsFile)["shortcuts"];
                    foreach (var node in (IEnumerable<BinaryVdf>?)shortcuts?.Children.Values ?? [])
                    {
                        token.ThrowIfCancellationRequested();
                        if (node.UInt32("appid") is not { } appId || string.IsNullOrWhiteSpace(node.Text("AppName"))) continue;
                        var executable = (node.Text("Exe") ?? "").Trim().Trim('"');
                        var artwork = SteamArtwork.Find(steam, user, appId, GameLauncher.ShortcutGameId(appId));
                        var lastPlayed = node.UInt32("LastPlayTime");
                        games[$"steam-shortcut:{Path.GetFileName(user)}:{appId}"] = new Game
                        {
                            Id = $"steam-shortcut:{Path.GetFileName(user)}:{appId}", Name = node.Text("AppName")!,
                            Source = GameSource.SteamShortcut, SteamAppId = appId, LibraryDirectory = user,
                            Installed = File.Exists(executable) || CustomGameProvider.IsUri(executable),
                            InstallDirectory = Path.IsPathFullyQualified(executable) ? Path.GetDirectoryName(executable) : null,
                            ProcessExecutable = File.Exists(executable) && executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? executable : null,
                            LaunchTarget = $"steam://rungameid/{GameLauncher.ShortcutGameId(appId)}",
                            CoverPath = artwork.Portrait, WideCoverPath = artwork.Wide, BackgroundPath = artwork.Background, LogoPath = artwork.Logo,
                            LastPlayed = lastPlayed is > 0 ? DateTimeOffset.FromUnixTimeSeconds(lastPlayed.Value) : null
                        };
                    }
                }
                catch (Exception ex) when (IsMetadataError(ex)) { warnings.Add("Steam shortcuts unavailable: " + ex.Message); unavailable.Add(user); }
            }
        }
        foreach (var library in libraries)
        {
            token.ThrowIfCancellationRequested();
            var apps = Path.Combine(library, "steamapps");
            watches.Add(apps);
            if (!Directory.Exists(apps)) { unavailable.Add(library); warnings.Add("Library unavailable: " + library); continue; }
            try
            {
                foreach (var file in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var state = Vdf.Read(file)["AppState"] ?? throw new InvalidDataException("Missing AppState.");
                        if (!uint.TryParse(state.Text("appid"), out var appId) || appId == 0) continue;
                        if (appId == 228980) continue; // Steamworks shared redistributables, not a launchable game.
                        var installName = state.Text("installdir");
                        if (string.IsNullOrWhiteSpace(installName)) continue;
                        var common = Path.GetFullPath(Path.Combine(apps, "common")) + Path.DirectorySeparatorChar;
                        var install = Path.GetFullPath(Path.Combine(common, installName));
                        if (!install.StartsWith(common, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid install directory.");
                        var info = stats.GetValueOrDefault(appId.ToString(CultureInfo.InvariantCulture));
                        var flags = uint.TryParse(state.Text("StateFlags"), out var parsedFlags) ? parsedFlags : 0;
                        var id = "steam:" + appId;
                        var artwork = SteamArtwork.Find(steam, user, appId);
                        games[id] = new Game
                        {
                            Id = id, SteamAppId = appId, Source = GameSource.Steam,
                            Name = state.Text("name") ?? $"Steam game {appId}", LibraryDirectory = library,
                            InstallDirectory = install, Installed = (flags & 4) != 0 && Directory.Exists(install),
                            LaunchTarget = "steam://rungameid/" + appId,
                            CoverPath = artwork.Portrait, WideCoverPath = artwork.Wide, BackgroundPath = artwork.Background, LogoPath = artwork.Logo,
                            LastPlayed = info.Last ?? UnixTime(state.Text("LastPlayed")), SteamMinutes = info.Minutes
                        };
                    }
                    catch (Exception ex) when (IsMetadataError(ex))
                    {
                        warnings.Add($"Skipped {Path.GetFileName(file)}: {ex.Message}");
                        // Preserve cached entries while Steam is rewriting a manifest.
                        unavailable.Add(library);
                    }
                }
            }
            catch (Exception ex) when (IsMetadataError(ex)) { unavailable.Add(library); warnings.Add(ex.Message); }
        }
        return new(games.Values.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList(), watches.ToList(), warnings, unavailable);
    }

    private static string? FindUser(string steam, string? userId)
    {
        var users = Path.Combine(steam, "userdata");
        if (!Directory.Exists(users)) return null;
        if (!string.IsNullOrWhiteSpace(userId) && uint.TryParse(userId, out _))
        {
            var selected = Path.Combine(users, userId);
            return Directory.Exists(selected) ? selected : null;
        }
        try
        {
            var logins = Path.Combine(steam, "config", "loginusers.vdf");
            if (File.Exists(logins) && Vdf.Read(logins)["users"] is { } root)
                foreach (var (id, node) in root.Children)
                    if (node.Text("MostRecent") == "1" && ulong.TryParse(id, out var steamId))
                    {
                        var path = Path.Combine(users, ((uint)(steamId & 0xffffffff)).ToString(CultureInfo.InvariantCulture));
                        if (Directory.Exists(path)) return path;
                    }
            return Directory.EnumerateDirectories(users).Where(d => uint.TryParse(Path.GetFileName(d), out _))
                .OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d, "config", "localconfig.vdf"))).FirstOrDefault();
        }
        catch (Exception ex) when (IsMetadataError(ex)) { return null; }
    }

    private static bool IsMetadataError(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
    private static DateTimeOffset? UnixTime(string? text) => long.TryParse(text, out var value) && value > 0 && value < 253402300800 ? DateTimeOffset.FromUnixTimeSeconds(value) : null;
    private static double? Number(string? text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value >= 0 ? value : null;
}
