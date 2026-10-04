using System.Diagnostics;
using GameLibrary.Core;

namespace GameLibrary.Infrastructure;

public static class GameLauncher
{
    public static ulong ShortcutGameId(uint appId) => ((ulong)appId << 32) | 0x02000000UL;
    public static string SteamUri(Game game)
    {
        if (game.SteamAppId is not { } appId || appId == 0) throw new InvalidOperationException("Missing Steam app ID.");
        return $"steam://rungameid/{(game.Source == GameSource.SteamShortcut ? ShortcutGameId(appId) : appId)}";
    }
    public static void Launch(Game game)
    {
        if (game.Source is GameSource.Steam or GameSource.SteamShortcut)
        {
            using var process = Process.Start(new ProcessStartInfo(SteamUri(game)) { UseShellExecute = true });
            return;
        }
        if (!string.IsNullOrWhiteSpace(game.LauncherExecutable))
        {
            using var launcher = Process.Start(new ProcessStartInfo(game.LauncherExecutable)
            { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(game.LauncherExecutable)! });
        }
        var info = new ProcessStartInfo(game.LaunchTarget) { UseShellExecute = true };
        if (!CustomGameProvider.IsUri(game.LaunchTarget))
        {
            info.Arguments = game.Arguments;
            info.WorkingDirectory = Path.GetDirectoryName(game.LaunchTarget)!;
        }
        using var launched = Process.Start(info);
    }
}
