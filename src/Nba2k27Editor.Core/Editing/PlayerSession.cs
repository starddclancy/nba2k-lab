using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Memory;
using Nba2k27Editor.Core.Model;
using Nba2k27Editor.Core.Roster;

namespace Nba2k27Editor.Core.Editing;

public sealed record ChangeRecord(string Field, object Before, object After, object Display, object? ExpectedRaw);

public sealed class PlayerTransactionRecord
{
    public string Player { get; init; } = string.Empty;
    public int Index { get; init; }
    public long Address { get; init; }
    public string Identity { get; init; } = string.Empty;
    public string IdentityAfter { get; set; } = string.Empty;
    public List<ChangeRecord> Changes { get; } = new();
    public Dictionary<string, long> FieldAddresses { get; } = new(StringComparer.Ordinal);
    public List<long> PatchAddresses { get; } = new();
    public RecordListItem Item { get; set; } = null!;
}

public sealed class PlayerTransaction
{
    public string Format { get; set; } = "nba2k27-player-edit/1";
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Time { get; set; } = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
    public SessionEpoch Epoch { get; set; } = null!;
    public string Source { get; set; } = "roster";
    public (long Body, int Count)? SourceSignature { get; set; }
    public string Mode { get; set; } = "native_roster";
    public List<PatchRow> Patches { get; set; } = new();
    public string Status { get; set; } = "prepared";
    public string? Error { get; set; }
    public List<PlayerTransactionRecord> Players { get; set; } = new();
    public string? JournalPath { get; set; }
}

/// <summary>Player editing session: connect, read, transactional apply and undo.</summary>
public sealed class PlayerSession
{
    /// <summary>
    /// UI tab order. Every name here is a value the <c>target</c> computation below can produce:
    /// a metadata section for most fields, or one of the groups under Vitals, which is split so
    /// identity, health, team, type, awards, personality and roster data get their own tabs.
    /// </summary>
    public static readonly string[] Sections =
    {
        "Vitals", "ID", "Body", "Health", "Team", "Type", "Awards", "Personality", "Roster",
        "Attributes", "Tendencies", "Badges", "Signature",
        "Gear", "Contract", "Stats", "Advanced",
        "Appearance", "Scanned Morph Data", "Skin Data",
        "College", "Base Tattoo Data", "Player Accessory Data", "Player Relationships",
    };

    /// <summary>Metadata sections that are exposed at all; everything else stays unread.</summary>
    private static readonly HashSet<string> SourceSections = new(StringComparer.Ordinal)
    {
        "Vitals", "Gear", "Attributes", "Tendencies", "Signature", "Contract", "Badges",
        "Stats", "Advanced", "Appearance", "Base Tattoo Data", "College",
        "Player Accessory Data", "Player Relationships", "Scanned Morph Data", "Skin Data",
    };

    private static readonly HashSet<string> BodyFields = new(StringComparer.Ordinal)
    {
        "HEIGHT", "WINGSPANINTENTHSOFMM", "WEIGHT", "BODYSHAPE",
    };

    private readonly List<(string Path, PlayerTransaction Transaction, IReadOnlyList<RecordListItem> Items)> _undo = new();

    public PlayerSession(string targetExecutable = "NBA2K27.exe", string? dataDirectory = null, IMemory? memory = null)
    {
        Memory = memory ?? Win32Memory.Open(targetExecutable);
        Offsets = new Offsets(targetExecutable);
        DataDirectory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NBA2K27PlayerEditor");
        Model = new Schema(Memory, Offsets, targetExecutable);
        if (memory is null)
        {
            // Only a real attachment gets the root cache; injected memory stays filesystem-free.
            Model.RootCache = new BaseRootCache(Path.Combine(DataDirectory, "base-roots.json"));
        }
        BuildSpecs();
    }

    public IMemory Memory { get; }
    public Offsets Offsets { get; }
    public Schema Model { get; }
    public string DataDirectory { get; }
    public Dictionary<string, FieldSpec> Specs { get; } = new(StringComparer.Ordinal);
    public List<RecordListItem> Items { get; private set; } = new();
    public SessionEpoch? Epoch { get; private set; }
    public RosterIndex RosterIndex { get; private set; } = new();
    public string LastWarning { get; set; } = string.Empty;
    public IReadOnlyList<(string Path, PlayerTransaction Transaction, IReadOnlyList<RecordListItem> Items)> UndoStack => _undo;

    private void BuildSpecs()
    {
        foreach (var entry in Model.GroupedFields("Players"))
        {
            var name = entry.NormalizedName;
            if (name.Contains("NEVERCALL", StringComparison.Ordinal))
            {
                continue;
            }
            string target = entry.Section;
            if (entry.Section == "Vitals")
            {
                // Vitals carries nine unrelated groups; each becomes its own tab instead of
                // crowding two hundred fields into one list.
                target = entry.Group == "Body" || BodyFields.Contains(name) ? "Body" : entry.Group;
            }
            else if (!SourceSections.Contains(entry.Section))
            {
                continue;
            }
            var payload = Model.FieldVersionPayload(entry.Field);
            if (payload.ContainsKey("record_source"))
            {
                continue;
            }
            if (FieldIo.NodeText(payload["stat_role"]) == "season_id_selector")
            {
                payload["readonly"] = true;
            }
            var typeKey = FieldIo.TypeKey(payload);
            if (typeKey is "pointer" or "address" or "uint64" or "ulonglong")
            {
                continue;
            }
            var key = $"{entry.Section}/{entry.Group}/{name}";
            Specs[key] = new FieldSpec(key, target, entry.Group, entry, payload);
        }
        PlayerStatistics.RegisterSpecs(this);
    }

    /// <summary>
    /// Read the player table and optionally build the team/draft index. The index costs most of
    /// the connect time, so callers that only need names can skip it and call
    /// <see cref="RefreshRosterIndex"/> later.
    /// </summary>
    public IReadOnlyList<RecordListItem> Connect(bool buildRosterIndex = true)
    {
        Items = new List<RecordListItem>();
        Epoch = null;
        _undo.Clear();
        RosterIndex = new RosterIndex();
        if (!Model.RecoverBasePointers())
        {
            throw new InvalidOperationException(Model.LastStatus);
        }
        var epoch = CurrentEpoch();
        int stride = Model.DomainStride("Players");
        if (stride != 1272)
        {
            throw new InvalidOperationException($"Unsupported player record size: {stride}");
        }
        var data = Memory.ReadBytes(epoch.Table, stride * epoch.Count);
        for (int index = 0; index < epoch.Count; index++)
        {
            int offset = index * stride;
            var last = DecodeName(data, offset, 40);
            var first = DecodeName(data, offset + 40, 40);
            var label = $"{first} {last}".Trim();
            if (label.Length == 0 || label.Any(character => character < 32))
            {
                continue;
            }
            Items.Add(new RecordListItem("Players", index, epoch.Table + (long)index * stride, label));
        }
        Epoch = epoch;
        if (!buildRosterIndex)
        {
            return Items;
        }
        try
        {
            Guard();
            var catalog = RosterBrowser.BuildRosterIndex(Model, Items);
            Guard();
            RosterIndex = catalog;
        }
        catch (Exception)
        {
            Items = new List<RecordListItem>();
            Epoch = null;
            throw;
        }
        return Items;
    }

    public RosterIndex RefreshRosterIndex()
    {
        Guard();
        var catalog = RosterBrowser.BuildRosterIndex(Model, Items);
        Guard();
        RosterIndex = catalog;
        return catalog;
    }

    public SessionEpoch CurrentEpoch()
    {
        var (store, roster, table, count) = ReadRoster();
        Process process;
        try
        {
            process = Process.GetProcessById(Memory.Pid);
        }
        catch (Exception exc)
        {
            throw new InvalidOperationException("NBA 2K27 has exited; reconnect before editing", exc);
        }
        if (!string.Equals(process.ProcessName + ".exe", "NBA2K27.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("NBA 2K27 has exited; reconnect before editing");
        }
        long createTicks;
        try
        {
            createTicks = process.StartTime.ToUniversalTime().Ticks;
        }
        catch (Exception exc)
        {
            throw new InvalidOperationException("Cannot read the game start time; reconnect before editing", exc);
        }
        return new SessionEpoch(Memory.Pid, createTicks, Memory.BaseAddress, store, roster, table, count);
    }

    private (long Store, long Roster, long Table, int Count) ReadRoster()
    {
        if (!Memory.Attached)
        {
            throw new InvalidOperationException("Please connect to NBA 2K27 first");
        }
        long rootRva;
        if (Model.RecoveredRoots.Count > 0)
        {
            rootRva = Model.RecoveredRoots["GameDataStore"];
        }
        else
        {
            var config = Model.ActiveConfig();
            rootRva = Conversions.ToLong(FieldIo.NodeText(config["base_pointers"]?["GameDataStore"]?["address"]));
        }
        long root = Memory.BaseAddress + rootRva;
        long store = ReadUInt64(root);
        long roster = ReadUInt64(store + 0xA8);
        int count = (int)ReadUInt32(roster + 0x10);
        long table = ReadUInt64(roster + 0x18);
        if (table == 0 || count < 1 || count > 30000)
        {
            throw new InvalidOperationException("The active roster is not initialized");
        }
        return (store, roster, table, count);
    }

    public void Guard(SessionEpoch? epoch = null)
    {
        var expected = epoch ?? Epoch
            ?? throw new InvalidOperationException("Please connect to NBA 2K27 first");
        if (!CurrentEpoch().Equals(expected))
        {
            throw new InvalidOperationException("The game or roster changed; reconnect and read the player again");
        }
    }

    public byte[] Identity(RecordListItem item) => Memory.ReadBytes(item.Address, 80);

    public IReadOnlyList<RecordListItem> MemberItems(string source) => source switch
    {
        "roster" => Items,
        "draft" => RosterIndex.DraftItems,
        _ => throw new ArgumentException("Unsupported player source"),
    };

    public void GuardEdit(SessionEpoch epoch, string source, (long Body, int Count)? signature)
    {
        Guard(epoch);
        if (source == "draft" && RosterBrowser.DraftSignature(Model) != signature)
        {
            throw new InvalidOperationException("Draft class changed; reconnect before editing");
        }
    }

    public long AddressOf(FieldSpec spec, RecordListItem item) =>
        Model.FieldAddress("Players", item.Address, spec.Payload);

    private long WriteAddressOf(FieldSpec spec, RecordListItem item)
    {
        PlayerStatistics.CheckWritableRecord(this, spec, item);
        return AddressOf(spec, item);
    }

    public string ConversionSection(FieldSpec spec) => spec.Group == "Progression" ? "Vitals" : spec.Entry.Section;

    public PlayerSnapshot ReadPlayer(RecordListItem item, IReadOnlyCollection<string>? fields = null)
    {
        Guard();
        string source = Items.Contains(item) ? "roster" : "draft";
        if (source == "draft" && !RosterIndex.DraftItems.Contains(item))
        {
            throw new InvalidOperationException("Player is not from the connected roster");
        }
        if (source == "draft" && RosterBrowser.DraftSignature(Model) != RosterIndex.DraftSignature)
        {
            throw new InvalidOperationException("Draft class changed; reconnect before reading");
        }
        var identity = Identity(item);
        var values = new Dictionary<string, FieldValue>(StringComparer.Ordinal);
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        IEnumerable<string> keys = fields ?? Specs.Values.Where(spec => !spec.IsSeasonBound)
            .Select(spec => spec.Key).ToArray();
        foreach (string key in keys)
        {
            if (!Specs.TryGetValue(key, out FieldSpec? spec))
            {
                throw new ArgumentException($"Unknown player field: {key}");
            }
            try
            {
                var read = Model.ReadField(spec.Entry, item);
                object value = Decode(spec, read.RawValue);
                values[key] = new FieldValue(read.RawValue, value, read.Address, read.Writeable && spec.Writable);
            }
            catch (Exception exc)
            {
                errors[key] = exc.Message;
            }
        }
        Guard();
        if (!Identity(item).SequenceEqual(identity))
        {
            throw new InvalidOperationException("Player changed while reading; select it again");
        }
        if (source == "draft" && RosterBrowser.DraftSignature(Model) != RosterIndex.DraftSignature)
        {
            throw new InvalidOperationException("Draft class changed while reading; reconnect");
        }
        return new PlayerSnapshot(item, Epoch!, identity, values, errors, source,
            source == "draft" ? RosterIndex.DraftSignature : null);
    }

    public object Decode(FieldSpec spec, object raw)
    {
        if (spec.Name is "HEIGHT" or "WINGSPANINTENTHSOFMM")
        {
            return Conversions.ToInt(raw) / 100.0;
        }
        if (spec.Name == "WEIGHT")
        {
            return Conversions.ToDouble(raw) * 0.45359237;
        }
        return FieldIo.RawToDisplayValue(ConversionSection(spec), spec.Entry.Field, spec.Payload, raw);
    }

    public object Encode(FieldSpec spec, object? value)
    {
        var payload = spec.Payload;
        if (!spec.Writable)
        {
            throw new ArgumentException($"{spec.Label} is read-only");
        }
        var options = spec.Options;
        if (options.Count > 0)
        {
            if (!options.Contains(value?.ToString() ?? string.Empty))
            {
                throw new ArgumentException($"{spec.Label}: select an existing 2K27 option");
            }
        }
        else if (payload["enum"] is JsonValue enumFlag
            && enumFlag.GetValueKind() == System.Text.Json.JsonValueKind.True)
        {
            throw new ArgumentException($"{spec.Label}: 2K27 enum labels are unavailable; field is read-only");
        }
        var typeKey = FieldIo.TypeKey(payload);
        if (typeKey is "wstring" or "utf16le_vtable" or "string")
        {
            var text = value?.ToString() ?? string.Empty;
            int length = Conversions.ToInt(FieldIo.NodeText(payload["length"]));
            int capacity = length * (typeKey == "string" ? 1 : 2);
            bool blankName = spec.Name is "FIRSTNAME" or "LASTNAME" && text.Trim().Length == 0;
            if (text.Any(character => character < 32) || blankName)
            {
                throw new ArgumentException(
                    $"{spec.Label}: player names must not be empty; control characters are invalid");
            }
            int encoded = typeKey == "string"
                ? Encoding.ASCII.GetByteCount(text)
                : Encoding.Unicode.GetByteCount(text);
            int terminator = typeKey == "string" ? 1 : 2;
            if (encoded + terminator > capacity)
            {
                throw new ArgumentException($"{spec.Label}: text is too long");
            }
            return text;
        }
        if (options.Count == 0)
        {
            double number = Conversions.ToDouble(value, double.NaN);
            if (double.IsNaN(number) || double.IsInfinity(number))
            {
                throw new ArgumentException($"{spec.Label}: a finite value is required");
            }
            var (low, high) = Bounds(spec);
            if (number < low || number > high)
            {
                throw new ArgumentException($"{spec.Label}: expected {low:g} to {high:g}");
            }
            if (typeKey != "float" && spec.Section != "Body" && number != Math.Floor(number))
            {
                throw new ArgumentException($"{spec.Label}: an integer is required");
            }
        }
        object raw;
        if (spec.Name is "HEIGHT" or "WINGSPANINTENTHSOFMM")
        {
            raw = (long)Math.Round(Conversions.ToDouble(value) * 100);
        }
        else if (spec.Name == "WEIGHT")
        {
            raw = Conversions.ToDouble(value) / 0.45359237;
        }
        else
        {
            raw = FieldIo.DisplayToRawValue(ConversionSection(spec), spec.Entry.Field, payload, value) ?? value!;
        }
        if (raw is double floating && floating == Math.Floor(floating) && typeKey != "float")
        {
            raw = (long)floating;
        }
        if (FieldIo.UsesBitfieldIo(payload))
        {
            int bits = FieldIo.BitWindow(payload).BitLength;
            long numeric = Conversions.ToLong(raw);
            if (numeric < 0 || numeric >= 1L << bits)
            {
                throw new ArgumentException($"{spec.Label}: value exceeds storage capacity");
            }
        }
        else if (typeKey != "float")
        {
            long numeric = Conversions.ToLong(raw);
            if (numeric < 0 || numeric >= 1L << (8 * FieldIo.NumericWidth(payload)))
            {
                throw new ArgumentException($"{spec.Label}: value exceeds storage capacity");
            }
        }
        return raw;
    }

    public (double Low, double High) Bounds(FieldSpec spec)
    {
        if (spec.Name is "HEIGHT" or "WINGSPANINTENTHSOFMM")
        {
            return (120.0, 300.0);
        }
        if (spec.Name == "WEIGHT")
        {
            return (40.0, 180.0);
        }
        if (spec.Name is "ANIMATIONBLENDING" or "FINANCIALSECURITY" or "LOYALTY" or "PLAYFORWINNER")
        {
            return (0, 100);
        }
        if (spec.Name == "YEARSPRO")
        {
            return (0, 31);
        }
        if (spec.Name == "POTENTIAL")
        {
            return (40, 99);
        }
        if (spec.Section == "Attributes")
        {
            if (spec.Name is "MINPOTENTIAL" or "MAXPOTENTIAL" or "BUSTPERCENT" or "BOOMPERCENT"
                or "AVGPERCENT" or "MINIMUMPOTENTIAL" or "MAXIMUMPOTENTIAL" or "BOOMPERCENTAGE"
                or "BUSTPERCENTAGE")
            {
                return (0, 100);
            }
            return (25, 99);
        }
        if (spec.Section == "Tendencies")
        {
            return (0, 100);
        }
        if (spec.Name == "BIRTHMONTH")
        {
            return (1, 12);
        }
        if (spec.Name == "BIRTHDAY")
        {
            return (1, 31);
        }
        if (spec.Name == "BIRTHYEAR")
        {
            return (1900, 2100);
        }
        if (spec.Payload.ContainsKey("year_map_base")
            || Conversions.IsYearOffsetField(spec.Label))
        {
            int baseYear = Conversions.ToInt(FieldIo.NodeText(spec.Payload["year_map_base"]));
            baseYear = baseYear > 0 ? baseYear : Conversions.YearBase;
            int bits = FieldIo.ResolvedLengthBits(spec.Payload);
            return (baseYear, baseYear + (1L << bits) - 1);
        }
        if (spec.Name == "NUMBER")
        {
            return (0, 99);
        }
        if (FieldIo.UsesBitfieldIo(spec.Payload))
        {
            return (0, (1L << FieldIo.BitWindow(spec.Payload).BitLength) - 1);
        }
        if (FieldIo.TypeKey(spec.Payload) == "float")
        {
            return (0, 100);
        }
        return (0, (1L << (8 * FieldIo.NumericWidth(spec.Payload))) - 1);
    }

    public PlayerTransaction? Apply(PlayerSnapshot snapshot, IReadOnlyDictionary<string, object?> changes) =>
        changes.Count == 0 ? null : ApplyPlayerEdits(new[] { (snapshot, changes) }, batch: false);

    public PlayerTransaction? ApplyBatch(
        IReadOnlyList<(PlayerSnapshot Snapshot, IReadOnlyDictionary<string, object?> Changes)> edits) =>
        ApplyPlayerEdits(edits, batch: true);

    public void ValidateSnapshot(PlayerSnapshot snapshot, IEnumerable<string> fields) =>
        CheckPreparedPlayer(snapshot, fields);

    private List<ChangeRecord> PreparePlayerChanges(
        PlayerSnapshot snapshot, IReadOnlyDictionary<string, object?> changes, Overlay overlay)
    {
        CheckSource(snapshot);
        if (!Identity(snapshot.Item).SequenceEqual(snapshot.Identity))
        {
            throw new InvalidOperationException("Player identity changed; read this player again");
        }
        PlayerStatistics.ValidateChanges(this, snapshot, changes);
        var details = new List<ChangeRecord>();
        foreach (var pair in changes)
        {
            if (!Specs.TryGetValue(pair.Key, out var spec) || !snapshot.Values.ContainsKey(pair.Key))
            {
                throw new ArgumentException($"Field was not read in this snapshot: {pair.Key}");
            }
            var previous = snapshot.Values[pair.Key];
            if (!previous.Writable)
            {
                throw new ArgumentException($"{spec.Label} is read-only");
            }
            var current = Model.ReadField(spec.Entry, snapshot.Item);
            if (!current.Writeable)
            {
                throw new ArgumentException($"{spec.Label} is read-only");
            }
            if (current.Address != previous.Address
                || !PlayerPatchGuard.SameValue(current.RawValue, previous.Raw))
            {
                throw new InvalidOperationException($"{spec.Label} changed in the game; read the player again");
            }
            // Ratings convert lossily (raw 56 -> 44 -> raw 57). Submitting the value that is already
            // displayed must not rewrite the raw byte, so keep the stored raw when the display matches.
            var raw = PlayerPatchGuard.SameValue(pair.Value, previous.Display) ? previous.Raw : Encode(spec, pair.Value);
            FieldIo.WriteAuthoredValue(overlay, previous.Address, spec.Payload, raw);
            details.Add(new ChangeRecord(pair.Key, previous.Raw, raw, pair.Value ?? string.Empty, null));
        }
        return details;
    }

    private void CheckPreparedPlayer(PlayerSnapshot snapshot, IEnumerable<string> changes)
    {
        CheckSource(snapshot);
        if (!Identity(snapshot.Item).SequenceEqual(snapshot.Identity))
        {
            throw new InvalidOperationException("Player identity changed during preparation");
        }
        foreach (var key in changes)
        {
            var spec = Specs[key];
            var previous = snapshot.Values[key];
            if (WriteAddressOf(spec, snapshot.Item) != previous.Address)
            {
                throw new InvalidOperationException("Player data address changed during preparation");
            }
            var current = Model.ReadField(spec.Entry, snapshot.Item);
            if (!current.Writeable)
            {
                throw new ArgumentException($"{spec.Label} is read-only");
            }
            if (current.Address != previous.Address
                || !PlayerPatchGuard.SameValue(current.RawValue, previous.Raw))
            {
                throw new InvalidOperationException($"{spec.Label} changed in the game; read the player again");
            }
        }
    }

    private void CheckSource(PlayerSnapshot snapshot)
    {
        if (snapshot.Source is not ("roster" or "draft"))
        {
            throw new ArgumentException("Unsupported player source");
        }
        if (!MemberItems(snapshot.Source).Contains(snapshot.Item))
        {
            throw new ArgumentException("This player is not part of the connected source");
        }
        GuardEdit(snapshot.Epoch, snapshot.Source, snapshot.Signature);
    }

    private PlayerTransaction? ApplyPlayerEdits(
        IReadOnlyList<(PlayerSnapshot Snapshot, IReadOnlyDictionary<string, object?> Changes)> edits, bool batch)
    {
        LastWarning = string.Empty;
        if (edits.Count == 0)
        {
            return null;
        }
        if (edits.Any(edit => !Equals(edit.Snapshot.Epoch, Epoch)))
        {
            throw new InvalidOperationException("The game or roster changed; reconnect and read the players again");
        }
        var sources = edits.Select(edit => edit.Snapshot.Source).Distinct().ToList();
        if (sources.Count != 1)
        {
            throw new ArgumentException("Roster and draft players cannot be edited in one batch");
        }
        var source = sources[0];
        var signature = source == "draft" ? edits[0].Snapshot.Signature : null;
        if (source == "draft" && edits.Any(edit => edit.Snapshot.Signature != signature))
        {
            throw new InvalidOperationException("Draft class changed; reconnect and read the players again");
        }
        var seenAddresses = new HashSet<long>();
        var seenIndices = new HashSet<int>();
        foreach (var edit in edits)
        {
            if (!seenAddresses.Add(edit.Snapshot.Item.Address) || !seenIndices.Add(edit.Snapshot.Item.Index))
            {
                throw new ArgumentException("A player was selected more than once in this batch");
            }
        }
        var memory = Memory;
        var overlay = new Overlay(memory);
        var players = new List<PlayerTransactionRecord>();
        foreach (var edit in edits)
        {
            var authored = new Overlay(memory);
            var details = PreparePlayerChanges(edit.Snapshot, edit.Changes, authored);
            if (overlay.After.Keys.Intersect(authored.After.Keys).Any())
            {
                throw new ArgumentException("Selected players share field storage; batch edit was stopped");
            }
            foreach (var pair in authored.Before)
            {
                overlay.Before.TryAdd(pair.Key, pair.Value);
            }
            foreach (var pair in authored.After)
            {
                overlay.After[pair.Key] = pair.Value;
            }
            for (int index = 0; index < details.Count; index++)
            {
                var detail = details[index];
                var expected = FieldIo.ReadAuthoredValue(
                    authored, edit.Snapshot.Values[detail.Field].Address, Specs[detail.Field].Payload);
                details[index] = detail with { ExpectedRaw = expected };
            }
            var identityAfter = new byte[edit.Snapshot.Identity.Length];
            for (int offset = 0; offset < identityAfter.Length; offset++)
            {
                identityAfter[offset] = authored.After.TryGetValue(edit.Snapshot.Item.Address + offset, out byte value)
                    ? value
                    : edit.Snapshot.Identity[offset];
            }
            var record = new PlayerTransactionRecord
            {
                Player = edit.Snapshot.Item.Label,
                Index = edit.Snapshot.Item.Index,
                Address = edit.Snapshot.Item.Address,
                Identity = Convert.ToHexString(edit.Snapshot.Identity).ToLowerInvariant(),
                IdentityAfter = Convert.ToHexString(identityAfter).ToLowerInvariant(),
                Item = edit.Snapshot.Item,
            };
            record.Changes.AddRange(details);
            foreach (var key in edit.Changes.Keys)
            {
                record.FieldAddresses[key] = edit.Snapshot.Values[key].Address;
            }
            record.PatchAddresses.AddRange(authored.Patches().Select(row => row.Address));
            players.Add(record);
        }
        var rows = overlay.Patches();
        if (rows.Count == 0)
        {
            return null;
        }
        var transaction = new PlayerTransaction
        {
            Format = batch ? "nba2k27-player-batch-edit/1" : "nba2k27-player-edit/1",
            Epoch = edits[0].Snapshot.Epoch,
            Source = source,
            SourceSignature = signature,
            Mode = memory.Simulated ? "isolated_simulation" : "native_roster",
            Patches = rows.ToList(),
            Players = players,
        };
        var items = edits.Select(edit => edit.Snapshot.Item).ToList();
        var guard = new PlayerPatchGuard(
            players.Select(player => (player.Item, player)),
            () => MemberItems(source),
            Identity,
            key => Specs[key],
            WriteAddressOf)
        {
            Memory = memory,
        };
        Directory.CreateDirectory(DataDirectory);
        var path = Path.Combine(DataDirectory, $"{DateTime.Now:yyyyMMdd-HHmmss}-{transaction.Id[..8]}.json");
        transaction.JournalPath = path;
        SaveJournal(path, transaction);
        try
        {
            foreach (var edit in edits)
            {
                CheckPreparedPlayer(edit.Snapshot, edit.Changes.Keys);
            }
            PatchApplier.ApplyPatches(
                memory, rows, () => GuardEdit(transaction.Epoch, source, signature), guard);
        }
        catch (Exception exc)
        {
            transaction.Status = exc.Message.Contains("rollback incomplete:", StringComparison.Ordinal)
                ? "needs_review"
                : "failed";
            transaction.Error = exc.Message;
            try
            {
                SaveJournal(path, transaction);
            }
            catch (IOException logError)
            {
                throw new InvalidOperationException(
                    $"{exc.Message}; failed to update transaction log: {logError.Message}", exc);
            }
            throw;
        }
        transaction.Status = "applied";
        _undo.Add((path, transaction, items));
        try
        {
            SaveJournal(path, transaction);
        }
        catch (IOException exc)
        {
            LastWarning = $"Memory changes succeeded, but the transaction log update failed: {exc.Message}";
        }
        return transaction;
    }

    public IReadOnlyList<RecordListItem> UndoBatch()
    {
        LastWarning = string.Empty;
        if (_undo.Count == 0)
        {
            throw new InvalidOperationException("There is no edit to undo in this connection");
        }
        var (path, transaction, items) = _undo[^1];
        if (transaction.Status == "needs_review")
        {
            throw new InvalidOperationException("The incomplete transaction needs review before another undo");
        }
        GuardEdit(transaction.Epoch, transaction.Source, transaction.SourceSignature);
        var members = MemberItems(transaction.Source);
        for (int index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var player = transaction.Players[index];
            if (!members.Contains(item)
                || !Convert.ToHexString(Identity(item)).ToLowerInvariant()
                    .Equals(player.IdentityAfter, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Player identity changed; undo was stopped");
            }
            foreach (var pair in player.FieldAddresses)
            {
                if (WriteAddressOf(Specs[pair.Key], item) != pair.Value)
                {
                    throw new InvalidOperationException("Player data address changed; undo was stopped");
                }
            }
            foreach (var change in player.Changes)
            {
                var current = Model.ReadField(Specs[change.Field].Entry, item);
                if (current.Address != player.FieldAddresses[change.Field]
                    || !PlayerPatchGuard.SameValue(current.RawValue, change.ExpectedRaw))
                {
                    throw new InvalidOperationException("Edited values changed in the game; undo was stopped");
                }
            }
        }
        var memory = Memory;
        foreach (var row in transaction.Patches)
        {
            byte current = memory.ReadBytes(row.Address, 1)[0];
            if ((current & row.Mask) != (row.After & row.Mask))
            {
                throw new InvalidOperationException("Edited values changed in the game; undo was stopped");
            }
        }
        var reverse = new List<PatchRow>();
        foreach (var row in transaction.Patches)
        {
            byte current = memory.ReadBytes(row.Address, 1)[0];
            byte restored = (byte)((current & ~row.Mask) | (row.Before & row.Mask));
            reverse.Add(new PatchRow(row.Address, current, restored, row.Mask));
        }
        var guard = new PlayerPatchGuard(
            items.Select((item, index) => (item, transaction.Players[index])),
            () => MemberItems(transaction.Source),
            Identity,
            key => Specs[key],
            WriteAddressOf,
            undo: true)
        {
            Memory = memory,
        };
        try
        {
            PatchApplier.ApplyPatches(
                memory, reverse,
                () => GuardEdit(transaction.Epoch, transaction.Source, transaction.SourceSignature), guard);
        }
        catch (Exception exc)
        {
            if (exc.Message.Contains("rollback incomplete:", StringComparison.Ordinal))
            {
                transaction.Status = "needs_review";
                transaction.Error = exc.Message;
                try
                {
                    SaveJournal(path, transaction);
                }
                catch (IOException logError)
                {
                    throw new InvalidOperationException(
                        $"{exc.Message}; failed to update transaction log: {logError.Message}", exc);
                }
            }
            throw;
        }
        transaction.Status = "undone";
        _undo.RemoveAt(_undo.Count - 1);
        try
        {
            SaveJournal(path, transaction);
        }
        catch (IOException exc)
        {
            LastWarning = $"Undo succeeded, but the transaction log update failed: {exc.Message}";
        }
        return items;
    }

    public void SaveJournal(string path, PlayerTransaction transaction)
    {
        var temporary = Path.ChangeExtension(path, ".tmp");
        File.WriteAllText(temporary, JournalJson.Serialize(transaction), new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }

    public void ExportPlayer(PlayerSnapshot snapshot, string path)
    {
        var fields = new JsonObject();
        foreach (var pair in snapshot.Values)
        {
            fields[pair.Key] = JsonValue.Create(pair.Value.Display.ToString());
        }
        var document = new JsonObject
        {
            ["format"] = "nba2k27-player-preset/1",
            ["editor_version"] = AppVersion.Value,
            ["player"] = snapshot.Item.Label,
            ["fields"] = fields,
        };
        File.WriteAllText(path, document.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }), new UTF8Encoding(false));
    }

    public Dictionary<string, object?> ImportPreset(string path)
    {
        var document = JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) as JsonObject
            ?? throw new ArgumentException("This file is not a 2K27 player preset");
        if (FieldIo.NodeText(document["format"]) != "nba2k27-player-preset/1"
            || document["fields"] is not JsonObject fields)
        {
            throw new ArgumentException("This file is not a 2K27 player preset");
        }
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in fields)
        {
            if (!Specs.TryGetValue(pair.Key, out var spec))
            {
                continue;
            }
            var text = FieldIo.NodeText(pair.Value);
            if (spec.Writable && !(spec.Payload["enum"] is not null && spec.Options.Count == 0))
            {
                Encode(spec, text);
                values[pair.Key] = text;
            }
        }
        return values;
    }

    private long ReadUInt64(long address)
    {
        var raw = Memory.ReadBytes(address, 8);
        ulong value = 0;
        for (int index = 7; index >= 0; index--)
        {
            value = (value << 8) | raw[index];
        }
        return (long)value;
    }

    private uint ReadUInt32(long address)
    {
        var raw = Memory.ReadBytes(address, 4);
        return (uint)(raw[0] | (raw[1] << 8) | (raw[2] << 16) | (raw[3] << 24));
    }

    private static string DecodeName(byte[] data, int offset, int length)
    {
        var builder = new StringBuilder(length / 2);
        for (int index = 0; index + 1 < length; index += 2)
        {
            char value = (char)(data[offset + index] | (data[offset + index + 1] << 8));
            if (value == '\0')
            {
                break;
            }
            builder.Append(value);
        }
        return builder.ToString().Trim();
    }

    public void Close() => Memory.Close();
}

public static class AppVersion
{
    public const string Value = "1.0.0";
}
