using System.Text.Json;
using GameLibrary.Core;

namespace GameLibrary.Infrastructure;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public string Root { get; }
    public string? RecoveryMessage { get; private set; }
    public SettingsService(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameLibrary");
        Directory.CreateDirectory(Root);
    }

    public Settings LoadSettings()
    {
        var value = Load<Settings>("settings.json") ?? new();
        if (value.SchemaVersion != 1) throw new InvalidDataException("Settings were created by an unsupported app version. File left unchanged.");
        // Validate before application code uses deserialized collections or dimensions.
        if (value.Games is null || value.Categories is null || value.CustomGames is null || value.ExtraLibraries is null ||
            value.Games.Values.Any(p => p is null || p.Categories is null) || value.CustomGames.Any(g => g is null || string.IsNullOrWhiteSpace(g.Id) || string.IsNullOrWhiteSpace(g.Name) || string.IsNullOrWhiteSpace(g.Target)) ||
            value.ExtraLibraries.Any(p => p is null) || value.Categories.Any(c => string.IsNullOrWhiteSpace(c)))
            throw new InvalidDataException("Settings contain invalid collections. Restore settings.json.bak before continuing.");
        if (value.CustomGames.Select(g => g.Id).Distinct().Count() != value.CustomGames.Count)
            throw new InvalidDataException("Settings contain duplicate custom game IDs. File left unchanged.");
        value.CardWidth = double.IsFinite(value.CardWidth) ? Math.Clamp(value.CardWidth, 110, 240) : 156;
        value.CoverCornerRadius = double.IsFinite(value.CoverCornerRadius) ? Math.Clamp(value.CoverCornerRadius, 0, 40) : 12;
        value.SelectionBorderThickness = double.IsFinite(value.SelectionBorderThickness) ? Math.Clamp(value.SelectionBorderThickness, 0, 10) : 2;
        if (value.SelectionColor is not { Length: 7 } color || color[0] != '#' ||
            !uint.TryParse(color.AsSpan(1), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out _))
            value.SelectionColor = "#CCF578";
        value.BackgroundOpacity = double.IsFinite(value.BackgroundOpacity) ? Math.Clamp(value.BackgroundOpacity, 0.25, 1) : 0.87;
        return value;
    }
    public LibraryCache LoadCache()
    {
        var value = Load<LibraryCache>("library.json");
        return value is { SchemaVersion: 1, Games: not null } && value.Games.All(g => g is not null && !string.IsNullOrWhiteSpace(g.Id) && !string.IsNullOrWhiteSpace(g.Name)) ? value : new();
    }
    public void SaveSettings(Settings value) => Save("settings.json", value);
    public void SaveCache(IEnumerable<Game> games) => Save("library.json", new LibraryCache { Games = games.ToList() });
    public Settings Clone(Settings value) => JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(value, Json), Json)!;

    private T? Load<T>(string name)
    {
        var path = Path.Combine(Root, name);
        if (!File.Exists(path)) return default;
        foreach (var candidate in new[] { path, path + ".bak" })
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                var result = JsonSerializer.Deserialize<T>(File.ReadAllText(candidate), Json);
                if (result is null) throw new JsonException("Empty document.");
                if (candidate != path)
                {
                    File.Copy(path, path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"), true);
                    File.Copy(candidate, path, true);
                    RecoveryMessage = $"Recovered {name} from its backup.";
                }
                return result;
            }
            catch (Exception ex) when (ex is JsonException or IOException) { Log($"Read {candidate}: {ex.Message}"); }
        }
        if (name == "settings.json") throw new InvalidDataException("Settings and their backup could not be read. Files left unchanged.");
        return default;
    }

    private void Save<T>(string name, T value)
    {
        var path = Path.Combine(Root, name);
        var temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, value, Json);
            stream.Flush(true);
        }
        if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
        else File.Move(temporary, path);
    }

    private readonly object logLock = new();
    public void Log(string message)
    {
        lock (logLock)
        {
            try
            {
                var path = Path.Combine(Root, "app.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.Move(path, path + ".1", true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
