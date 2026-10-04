using GameLibrary.Core;

namespace GameLibrary.Infrastructure;

public sealed class CustomGameProvider : IGameProvider
{
    public Task<ScanResult> ScanAsync(Settings settings, CancellationToken cancellationToken)
    {
        var games = settings.CustomGames.Select(g => new Game
        {
            Id = g.Id, Name = g.Name, Source = GameSource.Custom, LaunchTarget = g.Target,
            Arguments = g.Arguments, LauncherExecutable = g.LauncherExecutable,
            ProcessExecutable = g.ProcessExecutable, CoverPath = g.CoverPath,
            Installed = IsUri(g.Target) || File.Exists(g.Target),
            InstallDirectory = IsUri(g.Target) ? null : Path.GetDirectoryName(g.Target)
        }).ToList();
        return Task.FromResult(new ScanResult(games, [], [], []));
    }
    public static bool IsUri(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && !uri.IsFile;
    public static string? Validate(CustomGame game)
    {
        if (string.IsNullOrWhiteSpace(game.Name)) return "Enter a game name.";
        if (string.IsNullOrWhiteSpace(game.Target)) return "Choose an executable or enter a launch URI.";
        if (!IsUri(game.Target) && (!Path.IsPathFullyQualified(game.Target) || !File.Exists(game.Target))) return "The game executable does not exist.";
        if (!IsUri(game.Target) && !game.Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return "Choose an .exe file.";
        if (IsUri(game.Target) && !string.IsNullOrWhiteSpace(game.Arguments)) return "For URI launches, put launch parameters in the URI and leave Arguments empty.";
        foreach (var path in new[] { game.LauncherExecutable, game.ProcessExecutable })
            if (!string.IsNullOrWhiteSpace(path) && (!Path.IsPathFullyQualified(path) || !File.Exists(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                return "Launcher and process overrides must point to existing .exe files.";
        if (!string.IsNullOrWhiteSpace(game.CoverPath) && !File.Exists(game.CoverPath)) return "The cover image does not exist.";
        return null;
    }
}
