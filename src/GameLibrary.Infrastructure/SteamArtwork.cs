namespace GameLibrary.Infrastructure;

public sealed record SteamArtwork(string? Portrait, string? Wide, string? Background, string? Logo)
{
    public static SteamArtwork Find(string? steam, string? user, uint appId, ulong? shortcutId = null)
    {
        var id = appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string? Custom(string suffix)
        {
            if (user is null) return null;
            foreach (var key in new[] { id, shortcutId?.ToString(System.Globalization.CultureInfo.InvariantCulture) }.Where(k => k is not null))
                foreach (var extension in new[] { "png", "jpg", "jpeg" })
                {
                    var path = Path.Combine(user, "config", "grid", key + suffix + "." + extension);
                    if (File.Exists(path)) return path;
                }
            return null;
        }
        var files = new List<string>();
        if (steam is not null)
        {
            var root = Path.Combine(steam, "appcache", "librarycache");
            foreach (var asset in new[] { "library_600x900", "library_capsule", "wide_cover", "library_header", "header", "library_hero", "logo" })
                foreach (var extension in new[] { "jpg", "png", "jpeg" })
                { var file = Path.Combine(root, id + "_" + asset + "." + extension); if (File.Exists(file)) files.Add(file); }
            var directory = Path.Combine(root, id);
            if (Directory.Exists(directory)) files.AddRange(Directory.EnumerateFiles(directory, "*", new EnumerationOptions
            { RecurseSubdirectories = true, MaxRecursionDepth = 1, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
                .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg"));
        }
        string? Asset(params string[] names)
        {
            foreach (var name in names)
            {
                var found = files.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileNameWithoutExtension(f).Equals(id + "_" + name, StringComparison.OrdinalIgnoreCase));
                if (found is not null) return found;
            }
            return null;
        }
        return new(Custom("p") ?? Asset("library_600x900", "library_capsule", "library_header", "header"),
            Custom("") ?? Asset("wide_cover", "library_header", "header"),
            Custom("_hero") ?? Asset("library_hero", "background"), Custom("_logo") ?? Asset("logo"));
    }
}
