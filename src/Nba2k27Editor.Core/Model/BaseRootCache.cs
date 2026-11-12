using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nba2k27Editor.Core.Model;

/// <summary>
/// Persists the roots found by the base scan so a later session on the same game process can skip
/// the full memory scan, which costs about 45 seconds. Cached roots are never trusted blindly:
/// <see cref="Schema.RecoverBasePointers"/> still runs the normal validation pass, so a recycled
/// process id cannot make the editor resolve to a stale address.
/// </summary>
public sealed class BaseRootCache
{
    private const string Format = "nba2k27-base-roots/1";
    private const int MaxEntries = 8;

    private readonly string _path;

    public BaseRootCache(string path)
    {
        _path = path;
    }

    public string FilePath => _path;

    public string LastWarning { get; private set; } = string.Empty;

    public bool TryLoad(int pid, long baseAddress, out Dictionary<string, long> roots)
    {
        roots = new Dictionary<string, long>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(_path))
            {
                return false;
            }
            if (JsonNode.Parse(File.ReadAllText(_path, Encoding.UTF8)) is not JsonObject document
                || document["entries"] is not JsonArray entries)
            {
                return false;
            }
            foreach (JsonNode? node in entries)
            {
                if (node is not JsonObject entry || entry["roots"] is not JsonObject map)
                {
                    continue;
                }
                if (entry["pid"]?.GetValue<int>() != pid
                    || Conversions.ToLong(FieldIo.NodeText(entry["base"])) != baseAddress)
                {
                    continue;
                }
                foreach (KeyValuePair<string, JsonNode?> pair in map)
                {
                    roots[pair.Key] = Conversions.ToLong(FieldIo.NodeText(pair.Value));
                }
                return roots.Count > 0;
            }
        }
        catch (Exception exc) when (exc is IOException or JsonException
            or InvalidOperationException or FormatException or UnauthorizedAccessException)
        {
            LastWarning = "基址缓存不可读：" + exc.Message;
        }
        return false;
    }

    public void Save(int pid, long baseAddress, IReadOnlyDictionary<string, long> roots)
    {
        if (roots.Count == 0)
        {
            return;
        }
        try
        {
            // Kept as detached clones in a plain list: a JsonNode cannot belong to two parents,
            // so collecting them into a JsonArray and adding them again would throw.
            var kept = new List<JsonObject>();
            if (File.Exists(_path)
                && JsonNode.Parse(File.ReadAllText(_path, Encoding.UTF8)) is JsonObject existing
                && existing["entries"] is JsonArray previous)
            {
                foreach (JsonNode? node in previous)
                {
                    if (node is not JsonObject entry)
                    {
                        continue;
                    }
                    bool sameSlot = entry["pid"]?.GetValue<int>() == pid
                        && Conversions.ToLong(FieldIo.NodeText(entry["base"])) == baseAddress;
                    if (!sameSlot && kept.Count < MaxEntries - 1)
                    {
                        kept.Add((JsonObject)entry.DeepClone());
                    }
                }
            }
            var map = new JsonObject();
            foreach (KeyValuePair<string, long> pair in roots)
            {
                map[pair.Key] = pair.Value;
            }
            var record = new JsonObject
            {
                ["pid"] = pid,
                ["base"] = "0x" + baseAddress.ToString("x", CultureInfo.InvariantCulture),
                ["time"] = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
                ["roots"] = map,
            };
            var updated = new JsonArray { record };
            foreach (JsonObject entry in kept)
            {
                updated.Add(entry);
            }
            var document = new JsonObject { ["format"] = Format, ["entries"] = updated };
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            // Write beside the target and move, so an interrupted save cannot truncate the cache.
            string temporary = _path + ".tmp";
            File.WriteAllText(temporary, document.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
            }) + "\n", new UTF8Encoding(false));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception exc) when (exc is IOException or UnauthorizedAccessException
            or JsonException or InvalidOperationException)
        {
            LastWarning = "基址缓存写入失败：" + exc.Message;
        }
    }
}
