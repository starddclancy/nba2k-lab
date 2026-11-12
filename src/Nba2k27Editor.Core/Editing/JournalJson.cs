using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nba2k27Editor.Core.Editing;

/// <summary>Stable journal serialization so a crashed write can be reviewed by hand.</summary>
public static class JournalJson
{
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    /// <summary>Shared indented options; requires a resolver for object-typed nodes.</summary>
    public static JsonSerializerOptions Options => Indented;

    public static string Serialize(PlayerTransaction transaction)
    {
        var document = new JsonObject
        {
            ["format"] = transaction.Format,
            ["id"] = transaction.Id,
            ["time"] = transaction.Time,
            ["epoch"] = new JsonArray(transaction.Epoch.ToJsonArray().Select(value => JsonValue.Create(value)).ToArray()),
            ["source"] = transaction.Source,
            ["source_signature"] = transaction.SourceSignature is { } signature
                ? new JsonArray(JsonValue.Create(signature.Body), JsonValue.Create(signature.Count))
                : null,
            ["mode"] = transaction.Mode,
            ["status"] = transaction.Status,
            ["error"] = transaction.Error,
        };
        var patches = new JsonArray();
        foreach (var row in transaction.Patches)
        {
            patches.Add(new JsonObject
            {
                ["address"] = row.Address,
                ["before"] = row.Before,
                ["after"] = row.After,
                ["mask"] = row.Mask,
            });
        }
        document["patches"] = patches;
        var players = new JsonArray();
        foreach (var player in transaction.Players)
        {
            var changes = new JsonArray();
            foreach (var change in player.Changes)
            {
                changes.Add(new JsonObject
                {
                    ["field"] = change.Field,
                    ["before"] = ValueNode(change.Before),
                    ["after"] = ValueNode(change.After),
                    ["display"] = ValueNode(change.Display),
                    ["expected_raw"] = ValueNode(change.ExpectedRaw),
                });
            }
            var addresses = new JsonObject();
            foreach (var pair in player.FieldAddresses)
            {
                addresses[pair.Key] = pair.Value;
            }
            var patchAddresses = new JsonArray();
            foreach (var address in player.PatchAddresses)
            {
                patchAddresses.Add(address);
            }
            players.Add(new JsonObject
            {
                ["player"] = player.Player,
                ["index"] = player.Index,
                ["address"] = player.Address,
                ["identity"] = player.Identity,
                ["identity_after"] = player.IdentityAfter,
                ["changes"] = changes,
                ["field_addresses"] = addresses,
                ["patch_addresses"] = patchAddresses,
            });
        }
        document["players"] = players;
        return document.ToJsonString(Indented);
    }

    private static JsonNode? ValueNode(object? value)
    {
        return value switch
        {
            null => null,
            string text => JsonValue.Create(text),
            bool flag => JsonValue.Create(flag),
            byte[] bytes => JsonValue.Create(Convert.ToHexString(bytes).ToLowerInvariant()),
            ValueTuple<double, double> pair => new JsonArray(JsonValue.Create(pair.Item1), JsonValue.Create(pair.Item2)),
            sbyte or byte or short or ushort or int or uint or long or ulong => JsonValue.Create(Convert.ToInt64(value)),
            float single => JsonValue.Create((double)single),
            double number => JsonValue.Create(number),
            decimal money => JsonValue.Create((double)money),
            _ => JsonValue.Create(value.ToString()),
        };
    }
}
