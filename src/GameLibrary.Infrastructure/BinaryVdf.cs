using System.Text;

namespace GameLibrary.Infrastructure;

/// <summary>Read-only, bounded parser for Steam's binary KeyValues shortcuts file.</summary>
public sealed class BinaryVdf
{
    public Dictionary<string, BinaryVdf> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
    public object? Value { get; private init; }
    public BinaryVdf? this[string key] => Children.GetValueOrDefault(key);
    public string? Text(string key) => this[key]?.Value as string;
    public uint? UInt32(string key) => this[key]?.Value is uint number ? number : null;

    public static BinaryVdf Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Parse(stream);
    }
    public static BinaryVdf Parse(Stream stream)
    {
        if (stream.Length > 16 * 1024 * 1024) throw new InvalidDataException("Shortcut metadata exceeds the size limit.");
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        string CString()
        {
            using var buffer = new MemoryStream();
            while (true)
            {
                var value = reader.ReadByte();
                if (value == 0) return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
                if (buffer.Length >= 1024 * 1024) throw new InvalidDataException("Shortcut string is too large.");
                buffer.WriteByte(value);
            }
        }
        BinaryVdf Object(int depth)
        {
            if (depth > 32) throw new InvalidDataException("Shortcut nesting exceeds limit.");
            var node = new BinaryVdf();
            while (true)
            {
                var type = reader.ReadByte();
                if (type == 8) return node;
                var key = CString();
                node.Children[key] = type == 0 ? Object(depth + 1) : new BinaryVdf
                {
                    Value = type switch
                    {
                        1 => CString(), 2 => reader.ReadUInt32(), 3 => reader.ReadSingle(),
                        4 or 6 => reader.ReadUInt32(), 7 => reader.ReadUInt64(), 10 => reader.ReadInt64(),
                        _ => throw new InvalidDataException($"Unsupported shortcut value type {type}.")
                    }
                };
            }
        }
        try { return Object(0); }
        catch (EndOfStreamException ex) { throw new InvalidDataException("Steam is still writing its shortcuts file.", ex); }
    }
}
