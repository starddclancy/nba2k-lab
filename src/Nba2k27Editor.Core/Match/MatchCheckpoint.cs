using System.Text;
using System.Text.Json.Nodes;

namespace Nba2k27Editor.Core.Match;

/// <summary>Persist debug progress before process access and replace completed evidence atomically.</summary>
public sealed class MatchCheckpoint
{
    private readonly string _directory;

    public MatchCheckpoint(string directory)
    {
        _directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(_directory);
    }

    public void Record(string phase, string command, JsonObject detail)
    {
        var item = new JsonObject
        {
            ["utc"] = DateTime.UtcNow, ["phase"] = phase,
            ["command"] = command, ["detail"] = detail.DeepClone(),
        };
        byte[] bytes = Encoding.UTF8.GetBytes(item.ToJsonString() + "\n");
        using var stream = new FileStream(Path.Combine(_directory, "events.jsonl"), FileMode.Append,
            FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(true);
    }

    public void Save(string filename, JsonNode value)
    {
        if (string.IsNullOrWhiteSpace(filename) || Path.GetFileName(filename) != filename
            || filename is "." or ".." || Path.IsPathRooted(filename))
        {
            throw new ArgumentException("Checkpoint filename must stay in its output directory", nameof(filename));
        }
        string destination = Path.Combine(_directory, filename);
        string temporary = destination + ".tmp";
        byte[] bytes = Encoding.UTF8.GetBytes(value.ToJsonString() + "\n");
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write,
            FileShare.None, 4096, FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(true);
        }
        File.Move(temporary, destination, true);
    }
}
