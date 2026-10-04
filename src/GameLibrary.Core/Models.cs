namespace GameLibrary.Core;

public enum GameSource { Steam, Custom, SteamShortcut }

public sealed record Game
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public GameSource Source { get; init; }
    public uint? SteamAppId { get; init; }
    public string? InstallDirectory { get; init; }
    public string? LibraryDirectory { get; init; }
    public bool Installed { get; init; }
    public string? CoverPath { get; init; }
    public string? WideCoverPath { get; init; }
    public string? BackgroundPath { get; init; }
    public string? LogoPath { get; init; }
    public string LaunchTarget { get; init; } = "";
    public string Arguments { get; init; } = "";
    public string? LauncherExecutable { get; init; }
    public string? ProcessExecutable { get; init; }
    public DateTimeOffset? LastPlayed { get; init; }
    public double? SteamMinutes { get; init; }
}

public sealed class GamePreferences
{
    public bool Favorite { get; set; }
    public List<string> Categories { get; set; } = [];
    public string? CoverPath { get; set; }
    public DateTimeOffset? LastPlayed { get; set; }
    public double ObservedMinutes { get; set; }
    // Baseline plus subsequent local sessions, reconciled with Steam using max, never sum.
    public double EstimatedTotalMinutes { get; set; }
}

public sealed class CustomGame
{
    public string Id { get; set; } = "custom:" + Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Target { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string? LauncherExecutable { get; set; }
    public string? ProcessExecutable { get; set; }
    public string? CoverPath { get; set; }
}

public sealed class Settings
{
    public int SchemaVersion { get; set; } = 1;
    public string? SteamDirectory { get; set; }
    public string? SteamUserId { get; set; }
    public List<string> ExtraLibraries { get; set; } = [];
    public string? MonitorDevice { get; set; }
    public bool Translucent { get; set; } = true;
    public double BackgroundOpacity { get; set; } = 0.87;
    public double CardWidth { get; set; } = 156;
    public double CoverCornerRadius { get; set; } = 12;
    public double SelectionBorderThickness { get; set; } = 2;
    public bool UseWindowsAccentColor { get; set; } = true;
    public string SelectionColor { get; set; } = "#CCF578";
    public bool RestoreAfterExit { get; set; } = true;
    public uint HotkeyModifiers { get; set; } = 3; // Control + Alt
    public uint HotkeyKey { get; set; } = 0x47; // G
    public List<string> Categories { get; set; } = ["Backlog", "Couch games"];
    public List<CustomGame> CustomGames { get; set; } = [];
    public Dictionary<string, GamePreferences> Games { get; set; } = new(StringComparer.Ordinal);

    public GamePreferences For(string id)
    {
        if (!Games.TryGetValue(id, out var value)) Games[id] = value = new();
        return value;
    }
}

public sealed class LibraryCache
{
    public int SchemaVersion { get; set; } = 1;
    public List<Game> Games { get; set; } = [];
}

public sealed record ScanResult(IReadOnlyList<Game> Games, IReadOnlyList<string> WatchDirectories,
    IReadOnlyList<string> Warnings, IReadOnlyList<string> UnavailableLibraries);

public interface IGameProvider
{
    Task<ScanResult> ScanAsync(Settings settings, CancellationToken cancellationToken);
}

public enum SessionState { Starting, Running, Exited, Failed, Unknown }
public sealed record SessionUpdate(Game Game, SessionState State, string Message, double Minutes = 0);

public static class GameStatistics
{
    public static double? Minutes(Game game, GamePreferences? preferences)
    {
        var local = preferences?.EstimatedTotalMinutes ?? 0;
        return game.SteamMinutes is { } steam ? Math.Max(steam, local) : local > 0 ? local : null;
    }

    public static DateTimeOffset? LastPlayed(Game game, GamePreferences? preferences)
        => game.LastPlayed > preferences?.LastPlayed ? game.LastPlayed : preferences?.LastPlayed ?? game.LastPlayed;

    public static void RecordSession(Game game, GamePreferences preferences, double minutes, DateTimeOffset now)
    {
        preferences.EstimatedTotalMinutes = Math.Max(game.SteamMinutes ?? 0, preferences.EstimatedTotalMinutes) + Math.Max(0, minutes);
        preferences.ObservedMinutes += Math.Max(0, minutes);
        preferences.LastPlayed = now;
    }
}
