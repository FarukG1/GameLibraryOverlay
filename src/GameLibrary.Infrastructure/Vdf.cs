using System.Text;

namespace GameLibrary.Infrastructure;

/// <summary>Bounded Valve KeyValues text reader. Does not interpret directives or load other files.</summary>
public sealed class Vdf
{
    public Dictionary<string, Vdf> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Value { get; private init; }
    public Vdf? this[string key] => Children.GetValueOrDefault(key);
    public string? Text(string key) => this[key]?.Value;

    public static Vdf Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > 32 * 1024 * 1024) throw new InvalidDataException("Metadata file is too large.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static Vdf Parse(string text)
    {
        var offset = 0;
        string? Token()
        {
            while (offset < text.Length)
            {
                if (char.IsWhiteSpace(text[offset]) || text[offset] == '\uFEFF') { offset++; continue; }
                if (text[offset] == '/' && offset + 1 < text.Length && text[offset + 1] == '/')
                { while (offset < text.Length && text[offset] != '\n') offset++; continue; }
                break;
            }
            if (offset == text.Length) return null;
            var first = text[offset++];
            if (first is '{' or '}') return first.ToString();
            if (first == '"')
            {
                var token = new StringBuilder();
                while (offset < text.Length)
                {
                    var c = text[offset++];
                    if (c == '"') return token.ToString();
                    if (c == '\\' && offset < text.Length && text[offset] is '\\' or '"') c = text[offset++];
                    token.Append(c);
                }
                throw new InvalidDataException("Unterminated VDF string.");
            }
            var start = offset - 1;
            while (offset < text.Length && !char.IsWhiteSpace(text[offset]) && text[offset] is not '{' and not '}') offset++;
            return text[start..offset];
        }
        Vdf Object(int depth, bool nested)
        {
            if (depth > 64) throw new InvalidDataException("VDF nesting exceeds limit.");
            var node = new Vdf();
            while (Token() is { } key)
            {
                if (key == "}") return nested ? node : throw new InvalidDataException("Unexpected closing brace.");
                if (key == "{") throw new InvalidDataException("Missing VDF key.");
                var value = Token() ?? throw new InvalidDataException("Missing VDF value.");
                if (value == "}") throw new InvalidDataException("Missing VDF value.");
                node.Children[key] = value == "{" ? Object(depth + 1, true) : new Vdf { Value = value };
            }
            return nested ? throw new InvalidDataException("Unclosed VDF object.") : node;
        }
        return Object(0, false);
    }
}
