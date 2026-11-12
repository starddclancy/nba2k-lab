using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Memory;
using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.Core.Editing;

public sealed class FieldSpec
{
    public FieldSpec(string key, string section, string group, FieldEntry entry, JsonObject payload)
    {
        Key = key;
        Section = section;
        Group = group;
        Entry = entry;
        Payload = payload;
    }

    public string Key { get; }
    public string Section { get; }
    public string Group { get; }
    public FieldEntry Entry { get; }
    public JsonObject Payload { get; }
    public string Name => Entry.NormalizedName;
    public string Label => Entry.DisplayName;
    public bool IsSeasonBound => Payload["selected_record_source"]?["selector_field"] is not null;
    public bool Writable => !IsTrue(Payload["readonly"]) && FieldIo.ImplementedPayload(Payload);

    public IReadOnlyList<string> Options
    {
        get
        {
            var node = Payload["values"] ?? Payload["dropdown"];
            if (node is JsonArray array)
            {
                return array.Select(item => FieldIo.NodeText(item)).ToList();
            }
            return Array.Empty<string>();
        }
    }

    private static bool IsTrue(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == System.Text.Json.JsonValueKind.True;
}

public sealed record FieldValue(object Raw, object Display, long Address, bool Writable);

public sealed class PlayerSnapshot
{
    public PlayerSnapshot(
        RecordListItem item, SessionEpoch epoch, byte[] identity,
        Dictionary<string, FieldValue> values, Dictionary<string, string> errors,
        string source = "roster", (long Body, int Count)? signature = null)
    {
        Item = item;
        Epoch = epoch;
        Identity = identity;
        Values = values;
        Errors = errors;
        Source = source;
        Signature = signature;
    }

    public RecordListItem Item { get; }
    public SessionEpoch Epoch { get; }
    public byte[] Identity { get; }
    public Dictionary<string, FieldValue> Values { get; }
    public Dictionary<string, string> Errors { get; }
    public string Source { get; }
    public (long Body, int Count)? Signature { get; }
}

public sealed record SessionEpoch(int Pid, long CreateTicks, long Base, long Store, long Roster, long Table, int Count)
{
    public long[] ToJsonArray() => new long[] { Pid, CreateTicks, Base, Store, Roster, Table, Count };

    public static SessionEpoch FromJson(JsonArray array)
    {
        if (array.Count < 7)
        {
            throw new InvalidOperationException("Transaction epoch is incomplete");
        }
        return new SessionEpoch(
            (int)Conversions.ToLong(FieldIo.NodeText(array[0])),
            Conversions.ToLong(FieldIo.NodeText(array[1])),
            Conversions.ToLong(FieldIo.NodeText(array[2])),
            Conversions.ToLong(FieldIo.NodeText(array[3])),
            Conversions.ToLong(FieldIo.NodeText(array[4])),
            Conversions.ToLong(FieldIo.NodeText(array[5])),
            (int)Conversions.ToLong(FieldIo.NodeText(array[6])));
    }
}
