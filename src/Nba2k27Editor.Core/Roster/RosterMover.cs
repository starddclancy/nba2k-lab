using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Memory;
using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.Core.Roster;

/// <summary>A placement request that the current roster cannot satisfy.</summary>
public sealed class RosterMoveException : Exception
{
    public RosterMoveException(string message) : base(message)
    {
    }
}

public sealed record Placement(long TeamAddress, int TeamIndex, string TeamLabel, int Slot);

public sealed class TeamSlots
{
    public TeamSlots(RecordListItem team, long[] values, IReadOnlyList<RosterBrowser.SlotEntry> entries)
    {
        Team = team;
        Values = values;
        Entries = entries;
    }

    public RecordListItem Team { get; }
    public long[] Values { get; }
    public IReadOnlyList<RosterBrowser.SlotEntry> Entries { get; }

    public IEnumerable<int> OpenSlots
    {
        get
        {
            for (int index = 0; index < Values.Length; index++)
            {
                if (Values[index] == 0)
                {
                    yield return Entries[index].Slot;
                }
            }
        }
    }

    public int Used => Values.Count(value => value != 0);
}

public sealed record RosterMovePlayer(
    string Player,
    int Index,
    long Address,
    string Identity,
    long FromTeam,
    string FromTeamLabel,
    int FromSlot,
    long ToTeam,
    string ToTeamLabel,
    long ContractTeamBefore,
    long CurrentTeamAddress,
    long ContractTeamAddress);

public sealed record RosterMoveTeam(long Address, int Index, string Label, long[] ValuesBefore, long[] ValuesAfter);

public sealed class MovePlan
{
    public MovePlan(
        string kind, string summary, IReadOnlyList<PatchRow> rows,
        IReadOnlyList<RosterMovePlayer> players, IReadOnlyList<RosterMoveTeam> teams,
        IReadOnlyList<string>? details = null)
    {
        Kind = kind;
        Summary = summary;
        Rows = rows;
        Players = players;
        Teams = teams;
        Details = details ?? Array.Empty<string>();
    }

    /// <summary>Human-readable linked changes: payroll updates and draft pick transfers.</summary>
    public IReadOnlyList<string> Details { get; }

    public string Kind { get; }
    public string Summary { get; }
    public IReadOnlyList<PatchRow> Rows { get; }
    public IReadOnlyList<RosterMovePlayer> Players { get; }
    public IReadOnlyList<RosterMoveTeam> Teams { get; }
}

public sealed class RosterMoveTransaction
{
    public const string Format = "nba2k27-roster-move/1";

    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Time { get; init; } = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
    public SessionEpoch Epoch { get; set; } = null!;
    public string Kind { get; set; } = "move";
    public string Summary { get; set; } = string.Empty;
    public List<RosterMovePlayer> Players { get; set; } = new();
    public List<RosterMoveTeam> Teams { get; set; } = new();
    public List<PatchRow> Patches { get; set; } = new();
    public List<string> Details { get; set; } = new();
    public string Status { get; set; } = "prepared";
    public string? Error { get; set; }
    public string? JournalPath { get; set; }
}

/// <summary>Plan, apply and undo roster placement transactions (players and PLAYER slots only).</summary>
public sealed class RosterMover
{
    private const string CurrentTeamField = "CURRENTTEAMINTERNAL";
    private const string ContractTeamField = "CONTRACTTEAMINTERNAL";

    private readonly List<(string Path, RosterMoveTransaction Transaction)> _undo = new();
    private readonly string _dataDirectory;

    public RosterMover(PlayerSession session)
    {
        Session = session;
        Model = session.Model;
        _dataDirectory = Path.Combine(session.DataDirectory, "roster-moves");
    }

    public PlayerSession Session { get; }
    public Schema Model { get; }
    public string LastWarning { get; set; } = string.Empty;
    public IReadOnlyList<(string Path, RosterMoveTransaction Transaction)> UndoStack => _undo;

    // ---- reads -----------------------------------------------------------
    public IReadOnlyList<RosterBrowser.SlotEntry> SlotEntries()
    {
        var rows = RosterBrowser.TeamSlotEntries(Model)
            .Where(entry => !entry.IsTwoWay)
            .OrderBy(entry => entry.Slot)
            .ToList();
        if (rows.Count == 0)
        {
            throw new RosterMoveException("The team PLAYER slot definitions are unavailable");
        }
        return rows;
    }

    public long EntryAddress(FieldEntry entry, RecordListItem item)
    {
        var payload = Model.FieldVersionPayload(entry.Field);
        return FieldIo.FieldAddress(Model.Memory, item.Address, payload, Model.ParentPayload("Teams", payload));
    }

    private long Read(FieldEntry entry, RecordListItem item) =>
        Conversions.ToLong(Model.ReadField(entry, item).RawValue);

    private FieldEntry PlayerEntry(string name) =>
        Model.FieldByName("Players", name)
        ?? throw new RosterMoveException($"The Players {name} field is unavailable");

    public FieldEntry CurrentTeamEntry() => PlayerEntry(CurrentTeamField);

    public FieldEntry ContractTeamEntry() => PlayerEntry(ContractTeamField);

    public TeamSlots TeamSlots(RecordListItem team)
    {
        var entries = SlotEntries();
        var values = new long[entries.Count];
        for (int index = 0; index < entries.Count; index++)
        {
            values[index] = Read(entries[index].Entry, team);
        }
        return new TeamSlots(team, values, entries);
    }

    public IReadOnlyDictionary<long, RecordListItem> TeamsByAddress()
    {
        var result = new Dictionary<long, RecordListItem>();
        foreach (var team in Model.ScanRecords("Teams", 512))
        {
            result[team.Address] = team;
        }
        return result;
    }

    public Placement Placement(RecordListItem player)
    {
        var teams = TeamsByAddress();
        long pointer = Read(CurrentTeamEntry(), player);
        if (pointer == 0)
        {
            throw new RosterMoveException($"{player.Label} is not assigned to a team");
        }
        if (!teams.TryGetValue(pointer, out var team))
        {
            throw new RosterMoveException(
                $"{player.Label}: current team address is not in the loaded Teams table");
        }
        var slots = TeamSlots(team);
        var matching = new List<int>();
        for (int index = 0; index < slots.Values.Length; index++)
        {
            if (slots.Values[index] == player.Address)
            {
                matching.Add(slots.Entries[index].Slot);
            }
        }
        if (matching.Count != 1)
        {
            throw new RosterMoveException(
                $"{player.Label} must appear in exactly one {team.Label} PLAYER slot; found {matching.Count}");
        }
        return new Placement(team.Address, team.Index, team.Label, matching[0]);
    }

    public void CheckPlayer(RecordListItem player)
    {
        if (!Session.Items.Contains(player))
        {
            throw new RosterMoveException("The player is not part of the connected roster");
        }
        if (Session.Epoch is null)
        {
            throw new RosterMoveException("Connect to NBA 2K27 first");
        }
    }

    public IReadOnlyList<(RecordListItem Team, TeamSlots Slots)> TeamOptions()
    {
        var rows = new List<(RecordListItem, TeamSlots)>();
        foreach (var team in Model.ScanRecords("Teams", 512))
        {
            rows.Add((team, TeamSlots(team)));
        }
        return rows;
    }

    // ---- planning --------------------------------------------------------
    private long PointerRow(Overlay overlay, RecordListItem item, FieldEntry entry, long value)
    {
        long address = EntryAddress(entry, item);
        FieldIo.WriteAuthoredValue(overlay, address, Model.FieldVersionPayload(entry.Field), value);
        return address;
    }

    private void SlotRows(Overlay overlay, TeamSlots slots, long[] values)
    {
        for (int index = 0; index < slots.Entries.Count; index++)
        {
            long before = slots.Values[index];
            long after = values[index];
            if (before == after)
            {
                continue;
            }
            var entry = slots.Entries[index].Entry;
            long address = EntryAddress(entry, slots.Team);
            FieldIo.WriteAuthoredValue(overlay, address, Model.FieldVersionPayload(entry.Field), after);
        }
    }

    public static long[] Removed(long[] values, long playerAddress)
    {
        int index = Array.IndexOf(values, playerAddress);
        if (index < 0)
        {
            throw new RosterMoveException("The player is not in the expected PLAYER slot");
        }
        var remaining = new List<long>();
        for (int position = 0; position < values.Length; position++)
        {
            if (position != index && values[position] != 0)
            {
                remaining.Add(values[position]);
            }
        }
        while (remaining.Count < values.Length)
        {
            remaining.Add(0);
        }
        return remaining.ToArray();
    }

    public static long[] Added(long[] values, long playerAddress)
    {
        if (values.Contains(playerAddress))
        {
            throw new RosterMoveException("The player already occupies a slot on the target team");
        }
        int index = Array.IndexOf(values, 0L);
        if (index < 0)
        {
            throw new RosterMoveException("The target team has no open player slot");
        }
        var result = (long[])values.Clone();
        result[index] = playerAddress;
        return result;
    }

    // ---- linked changes: payroll and draft picks -------------------------
    private const string TeamSalaryField = "SALARY";
    private const string PlayerSalaryField = "SALARYYEAR1";

    /// <summary>
    /// Team SALARY equals the summed SALARYYEAR1 of players whose current team is that team
    /// (32/32 teams on 2026-11-08). Shift it by the salaries that leave and arrive.
    /// </summary>
    private void PayrollRows(
        Overlay overlay, RecordListItem team, long outgoing, long incoming, List<string> details)
    {
        if (outgoing == incoming)
        {
            return;
        }
        var entry = Model.FieldByName("Teams", TeamSalaryField)
            ?? throw new RosterMoveException("The team SALARY field is unavailable");
        long before = Read(entry, team);
        long after = before - outgoing + incoming;
        if (after < 0 || after > uint.MaxValue)
        {
            throw new RosterMoveException($"{team.Label}: payroll would leave the valid range");
        }
        FieldIo.WriteAuthoredValue(overlay, EntryAddress(entry, team), Model.FieldVersionPayload(entry.Field), after);
        details.Add($"payroll {team.Label}: {before} -> {after}");
    }

    private long Salary(RecordListItem player) => Read(PlayerEntry(PlayerSalaryField), player);

    /// <summary>Move picks owned by from to to by rewriting only the current-owner pointer.</summary>
    private void PickRows(
        Overlay overlay, IReadOnlyList<int> indices, RecordListItem from, RecordListItem to, List<string> details)
    {
        if (indices.Count == 0)
        {
            return;
        }
        if (indices.Distinct().Count() != indices.Count)
        {
            throw new RosterMoveException("A draft pick was selected more than once");
        }
        var picks = DraftPickTable.Read(Model);
        foreach (int index in indices)
        {
            if (index < 0 || index >= picks.Count)
            {
                throw new RosterMoveException($"Draft pick {index} does not exist");
            }
            var pick = picks[index];
            if (pick.Owner != from.Address)
            {
                throw new RosterMoveException($"Draft pick {index} is not owned by {from.Label}");
            }
            if (pick.Used)
            {
                throw new RosterMoveException($"Draft pick {index} has already been used");
            }
            overlay.WriteBytes(pick.Address, BitConverter.GetBytes(to.Address));
            details.Add($"pick {index} (round {pick.Round}): {from.Label} -> {to.Label}");
        }
    }

    public MovePlan PlanMove(RecordListItem player, RecordListItem targetTeam) =>
        PlanMove(player, targetTeam, syncPayroll: false);

    public MovePlan PlanMove(RecordListItem player, RecordListItem targetTeam, bool syncPayroll)
    {
        CheckPlayer(player);
        var current = Placement(player);
        if (targetTeam.Address == current.TeamAddress)
        {
            throw new RosterMoveException($"{player.Label} is already on {current.TeamLabel}");
        }
        var source = TeamSlots(TeamByAddress(current.TeamAddress));
        var target = TeamSlots(targetTeam);
        if (!target.Values.Contains(0))
        {
            throw new RosterMoveException($"{targetTeam.Label} has no open player slot");
        }
        var currentTeam = CurrentTeamEntry();
        var contractTeam = ContractTeamEntry();
        long contractBefore = Read(contractTeam, player);
        var sourceValues = Removed(source.Values, player.Address);
        var targetValues = Added(target.Values, player.Address);
        var overlay = new Overlay(Model.Memory);
        PointerRow(overlay, player, currentTeam, targetTeam.Address);
        PointerRow(overlay, player, contractTeam, targetTeam.Address);
        SlotRows(overlay, source, sourceValues);
        SlotRows(overlay, target, targetValues);
        var details = new List<string>();
        if (syncPayroll)
        {
            long salary = Salary(player);
            PayrollRows(overlay, source.Team, salary, 0, details);
            PayrollRows(overlay, targetTeam, 0, salary, details);
        }
        var rows = overlay.Patches();
        if (rows.Count == 0)
        {
            throw new RosterMoveException("The planned move would not change any byte");
        }
        return new MovePlan(
            "move",
            $"{player.Label}: {current.TeamLabel} slot {current.Slot} -> {targetTeam.Label}",
            rows,
            new[]
            {
                new RosterMovePlayer(
                    player.Label, player.Index, player.Address,
                    Convert.ToHexString(Session.Identity(player)).ToLowerInvariant(),
                    current.TeamAddress, current.TeamLabel, current.Slot,
                    targetTeam.Address, targetTeam.Label, contractBefore,
                    EntryAddress(currentTeam, player), EntryAddress(contractTeam, player)),
            },
            new[]
            {
                new RosterMoveTeam(source.Team.Address, source.Team.Index, source.Team.Label,
                    (long[])source.Values.Clone(), sourceValues),
                new RosterMoveTeam(target.Team.Address, target.Team.Index, target.Team.Label,
                    (long[])target.Values.Clone(), targetValues),
            },
            details);
    }

    public MovePlan PlanTrade(RecordListItem first, RecordListItem second) =>
        PlanTrade(first, second, Array.Empty<int>(), Array.Empty<int>(), syncPayroll: false);

    /// <param name="firstPicks">Picks owned by the first player's team that go to the second team.</param>
    /// <param name="secondPicks">Picks owned by the second player's team that go to the first team.</param>
    public MovePlan PlanTrade(
        RecordListItem first, RecordListItem second,
        IReadOnlyList<int> firstPicks, IReadOnlyList<int> secondPicks, bool syncPayroll)
    {
        CheckPlayer(first);
        CheckPlayer(second);
        if (first.Address == second.Address)
        {
            throw new RosterMoveException("Select two different players");
        }
        var firstPlace = Placement(first);
        var secondPlace = Placement(second);
        if (firstPlace.TeamAddress == secondPlace.TeamAddress)
        {
            throw new RosterMoveException("Both players are already on the same team");
        }
        var firstTeam = TeamByAddress(firstPlace.TeamAddress);
        var secondTeam = TeamByAddress(secondPlace.TeamAddress);
        var firstSlots = TeamSlots(firstTeam);
        var secondSlots = TeamSlots(secondTeam);
        var currentTeam = CurrentTeamEntry();
        var contractTeam = ContractTeamEntry();
        long firstContract = Read(contractTeam, first);
        long secondContract = Read(contractTeam, second);
        // Remove both, then place each on the other team's first open slot.
        var firstTeamValues = Added(Removed(firstSlots.Values, first.Address), second.Address);
        var secondTeamValues = Added(Removed(secondSlots.Values, second.Address), first.Address);
        var overlay = new Overlay(Model.Memory);
        PointerRow(overlay, first, currentTeam, secondTeam.Address);
        PointerRow(overlay, first, contractTeam, secondTeam.Address);
        PointerRow(overlay, second, currentTeam, firstTeam.Address);
        PointerRow(overlay, second, contractTeam, firstTeam.Address);
        SlotRows(overlay, firstSlots, firstTeamValues);
        SlotRows(overlay, secondSlots, secondTeamValues);
        var details = new List<string>();
        if (syncPayroll)
        {
            long firstSalary = Salary(first), secondSalary = Salary(second);
            PayrollRows(overlay, firstTeam, firstSalary, secondSalary, details);
            PayrollRows(overlay, secondTeam, secondSalary, firstSalary, details);
        }
        PickRows(overlay, firstPicks, firstTeam, secondTeam, details);
        PickRows(overlay, secondPicks, secondTeam, firstTeam, details);
        var rows = overlay.Patches();
        if (rows.Count == 0)
        {
            throw new RosterMoveException("The planned trade would not change any byte");
        }
        return new MovePlan(
            "trade",
            $"{first.Label} ({firstPlace.TeamLabel}) <-> {second.Label} ({secondPlace.TeamLabel})",
            rows,
            new[]
            {
                new RosterMovePlayer(
                    first.Label, first.Index, first.Address,
                    Convert.ToHexString(Session.Identity(first)).ToLowerInvariant(),
                    firstPlace.TeamAddress, firstPlace.TeamLabel, firstPlace.Slot,
                    secondTeam.Address, secondTeam.Label, firstContract,
                    EntryAddress(currentTeam, first), EntryAddress(contractTeam, first)),
                new RosterMovePlayer(
                    second.Label, second.Index, second.Address,
                    Convert.ToHexString(Session.Identity(second)).ToLowerInvariant(),
                    secondPlace.TeamAddress, secondPlace.TeamLabel, secondPlace.Slot,
                    firstTeam.Address, firstTeam.Label, secondContract,
                    EntryAddress(currentTeam, second), EntryAddress(contractTeam, second)),
            },
            new[]
            {
                new RosterMoveTeam(firstTeam.Address, firstTeam.Index, firstTeam.Label,
                    (long[])firstSlots.Values.Clone(), firstTeamValues),
                new RosterMoveTeam(secondTeam.Address, secondTeam.Index, secondTeam.Label,
                    (long[])secondSlots.Values.Clone(), secondTeamValues),
            },
            details);
    }

    /// <summary>
    /// Team-to-team package: any number of players and unused draft picks in each direction, in
    /// one transaction. Both teams' slots are recompacted, every moved player's current and
    /// contract team point at the receiver, and payroll moves by the salaries exchanged.
    /// </summary>
    public MovePlan PlanPackage(
        RecordListItem firstTeam, RecordListItem secondTeam,
        IReadOnlyList<RecordListItem> firstPlayers, IReadOnlyList<RecordListItem> secondPlayers,
        IReadOnlyList<int> firstPicks, IReadOnlyList<int> secondPicks, bool syncPayroll)
    {
        if (firstTeam.Address == secondTeam.Address)
        {
            throw new RosterMoveException("Select two different teams");
        }
        if (firstPlayers.Count + secondPlayers.Count + firstPicks.Count + secondPicks.Count == 0)
        {
            throw new RosterMoveException("Add at least one player or draft pick to the trade");
        }
        var all = firstPlayers.Concat(secondPlayers).ToList();
        if (all.Select(player => player.Address).Distinct().Count() != all.Count)
        {
            throw new RosterMoveException("A player was selected more than once");
        }
        var firstSlots = TeamSlots(firstTeam);
        var secondSlots = TeamSlots(secondTeam);
        var moved = new List<RosterMovePlayer>();
        var currentTeam = CurrentTeamEntry();
        var contractTeam = ContractTeamEntry();

        long[] Recompose(TeamSlots slots, IReadOnlyList<RecordListItem> outgoing, IReadOnlyList<RecordListItem> incoming) =>
            Repacked(slots.Values, outgoing.Select(player => player.Address).ToArray(),
                incoming.Select(player => player.Address).ToArray(), slots.Team.Label);

        void Describe(IReadOnlyList<RecordListItem> players, RecordListItem from, RecordListItem to)
        {
            foreach (var player in players)
            {
                CheckPlayer(player);
                var place = Placement(player);
                if (place.TeamAddress != from.Address)
                {
                    throw new RosterMoveException($"{player.Label} is not on {from.Label}");
                }
                moved.Add(new RosterMovePlayer(
                    player.Label, player.Index, player.Address,
                    Convert.ToHexString(Session.Identity(player)).ToLowerInvariant(),
                    place.TeamAddress, place.TeamLabel, place.Slot, to.Address, to.Label,
                    Read(contractTeam, player), EntryAddress(currentTeam, player), EntryAddress(contractTeam, player)));
            }
        }

        Describe(firstPlayers, firstTeam, secondTeam);
        Describe(secondPlayers, secondTeam, firstTeam);
        var firstValues = Recompose(firstSlots, firstPlayers, secondPlayers);
        var secondValues = Recompose(secondSlots, secondPlayers, firstPlayers);
        var overlay = new Overlay(Model.Memory);
        foreach (var player in firstPlayers)
        {
            PointerRow(overlay, player, currentTeam, secondTeam.Address);
            PointerRow(overlay, player, contractTeam, secondTeam.Address);
        }
        foreach (var player in secondPlayers)
        {
            PointerRow(overlay, player, currentTeam, firstTeam.Address);
            PointerRow(overlay, player, contractTeam, firstTeam.Address);
        }
        SlotRows(overlay, firstSlots, firstValues);
        SlotRows(overlay, secondSlots, secondValues);
        var details = new List<string>();
        if (syncPayroll)
        {
            long firstOut = firstPlayers.Sum(Salary), secondOut = secondPlayers.Sum(Salary);
            PayrollRows(overlay, firstTeam, firstOut, secondOut, details);
            PayrollRows(overlay, secondTeam, secondOut, firstOut, details);
        }
        PickRows(overlay, firstPicks, firstTeam, secondTeam, details);
        PickRows(overlay, secondPicks, secondTeam, firstTeam, details);
        var rows = overlay.Patches();
        if (rows.Count == 0)
        {
            throw new RosterMoveException("The planned trade would not change any byte");
        }
        string Names(IReadOnlyList<RecordListItem> players, int picks) => string.Join(", ",
            players.Select(player => player.Label).Concat(picks > 0 ? new[] { $"{picks} pick(s)" } : Array.Empty<string>()));
        return new MovePlan(
            "trade",
            $"{firstTeam.Label} sends {Names(firstPlayers, firstPicks.Count)}; {secondTeam.Label} sends {Names(secondPlayers, secondPicks.Count)}",
            rows, moved,
            new[]
            {
                new RosterMoveTeam(firstTeam.Address, firstTeam.Index, firstTeam.Label, (long[])firstSlots.Values.Clone(), firstValues),
                new RosterMoveTeam(secondTeam.Address, secondTeam.Index, secondTeam.Label, (long[])secondSlots.Values.Clone(), secondValues),
            },
            details);
    }

    /// <summary>
    /// Slot values after a package: outgoing players removed (gaps closed) first, then incoming
    /// players appended in order. Removing first lets a full team swap players one for one.
    /// </summary>
    public static long[] Repacked(long[] values, IReadOnlyList<long> outgoing, IReadOnlyList<long> incoming, string team)
    {
        foreach (long player in outgoing)
        {
            values = Removed(values, player);
        }
        foreach (long player in incoming)
        {
            if (!values.Contains(0L))
            {
                throw new RosterMoveException($"{team} would have more than {values.Length} players after this trade");
            }
            values = Added(values, player);
        }
        return values;
    }

    private RecordListItem TeamByAddress(long address) =>
        TeamsByAddress().TryGetValue(address, out var team)
            ? team
            : throw new RosterMoveException("The team record is no longer loaded");

    // ---- apply and undo --------------------------------------------------
    public RosterMoveTransaction Apply(MovePlan plan)
    {
        LastWarning = string.Empty;
        var epoch = Session.Epoch ?? throw new RosterMoveException("Connect to NBA 2K27 first");
        if (_undo.Count > 0 && !Equals(_undo[^1].Transaction.Epoch, epoch))
        {
            _undo.Clear();
        }
        var transaction = new RosterMoveTransaction
        {
            Epoch = epoch,
            Kind = plan.Kind,
            Summary = plan.Summary,
            Players = plan.Players.ToList(),
            Teams = plan.Teams.ToList(),
            Patches = plan.Rows.ToList(),
            Details = plan.Details.ToList(),
        };
        Directory.CreateDirectory(_dataDirectory);
        var path = Path.Combine(
            _dataDirectory, $"{DateTime.Now:yyyyMMdd-HHmmss}-{transaction.Id[..8]}.json");
        transaction.JournalPath = path;
        Save(path, transaction);
        try
        {
            Check(plan);
            PatchApplier.ApplyPatches(Model.Memory, plan.Rows, () => Session.Guard(epoch));
        }
        catch (Exception exc)
        {
            transaction.Status = exc.Message.Contains("rollback incomplete:", StringComparison.Ordinal)
                ? "needs_review"
                : "failed";
            transaction.Error = exc.Message;
            try
            {
                Save(path, transaction);
            }
            catch (IOException logError)
            {
                throw new InvalidOperationException(
                    $"{exc.Message}; failed to update the move log: {logError.Message}", exc);
            }
            throw;
        }
        transaction.Status = "applied";
        _undo.Add((path, transaction));
        try
        {
            Save(path, transaction);
        }
        catch (IOException exc)
        {
            LastWarning = $"Roster move succeeded, but the move log update failed: {exc.Message}";
        }
        return transaction;
    }

    /// <summary>Verify the process, identities and original bytes before writing.</summary>
    private void Check(MovePlan plan)
    {
        Session.Guard();
        foreach (var player in plan.Players)
        {
            var item = Session.Items.FirstOrDefault(row => row.Address == player.Address)
                ?? throw new RosterMoveException("The player is no longer in the connected roster");
            if (!Convert.ToHexString(Session.Identity(item)).ToLowerInvariant()
                .Equals(player.Identity, StringComparison.OrdinalIgnoreCase))
            {
                throw new RosterMoveException($"{player.Player}: identity changed; reload the roster");
            }
        }
        foreach (var team in plan.Teams)
        {
            var loaded = TeamByAddress(team.Address);
            var slots = TeamSlots(loaded);
            if (!slots.Values.SequenceEqual(team.ValuesBefore))
            {
                throw new RosterMoveException($"{team.Label}: PLAYER slots changed; reload the roster");
            }
        }
    }

    public RosterMoveTransaction Undo()
    {
        LastWarning = string.Empty;
        if (_undo.Count == 0)
        {
            throw new RosterMoveException("There is no roster move to undo in this connection");
        }
        var (path, transaction) = _undo[^1];
        if (transaction.Status == "needs_review")
        {
            throw new RosterMoveException("The incomplete move needs review before another undo");
        }
        var epoch = transaction.Epoch;
        Session.Guard(epoch);
        foreach (var player in transaction.Players)
        {
            var item = Session.Items.FirstOrDefault(row => row.Address == player.Address)
                ?? throw new RosterMoveException("The player is no longer in the connected roster");
            if (!Convert.ToHexString(Session.Identity(item)).ToLowerInvariant()
                .Equals(player.Identity, StringComparison.OrdinalIgnoreCase))
            {
                throw new RosterMoveException("A player identity changed; undo was stopped");
            }
        }
        var memory = Model.Memory;
        foreach (var row in transaction.Patches)
        {
            byte current = memory.ReadBytes(row.Address, 1)[0];
            if ((current & row.Mask) != (row.After & row.Mask))
            {
                throw new RosterMoveException("Placement data changed in the game; undo was stopped");
            }
        }
        var reverse = new List<PatchRow>();
        foreach (var row in transaction.Patches)
        {
            byte current = memory.ReadBytes(row.Address, 1)[0];
            byte restored = (byte)((current & ~row.Mask) | (row.Before & row.Mask));
            reverse.Add(new PatchRow(row.Address, current, restored, row.Mask));
        }
        try
        {
            PatchApplier.ApplyPatches(memory, reverse, () => Session.Guard(epoch));
        }
        catch (Exception exc)
        {
            if (exc.Message.Contains("rollback incomplete:", StringComparison.Ordinal))
            {
                transaction.Status = "needs_review";
                transaction.Error = exc.Message;
                Save(path, transaction);
            }
            throw;
        }
        transaction.Status = "undone";
        _undo.RemoveAt(_undo.Count - 1);
        try
        {
            Save(path, transaction);
        }
        catch (IOException exc)
        {
            LastWarning = $"Undo succeeded, but the move log update failed: {exc.Message}";
        }
        return transaction;
    }

    public void Save(string path, RosterMoveTransaction transaction)
    {
        var document = new JsonObject
        {
            ["format"] = RosterMoveTransaction.Format,
            ["id"] = transaction.Id,
            ["time"] = transaction.Time,
            ["epoch"] = new JsonArray(
                transaction.Epoch.ToJsonArray().Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
            ["kind"] = transaction.Kind,
            ["summary"] = transaction.Summary,
            ["status"] = transaction.Status,
            ["error"] = transaction.Error,
        };
        var players = new JsonArray();
        foreach (var player in transaction.Players)
        {
            players.Add(new JsonObject
            {
                ["player"] = player.Player,
                ["index"] = player.Index,
                ["address"] = player.Address,
                ["identity"] = player.Identity,
                ["from_team"] = player.FromTeam,
                ["from_team_label"] = player.FromTeamLabel,
                ["from_slot"] = player.FromSlot,
                ["to_team"] = player.ToTeam,
                ["to_team_label"] = player.ToTeamLabel,
                ["contract_team_before"] = player.ContractTeamBefore,
                ["current_team_address"] = player.CurrentTeamAddress,
                ["contract_team_address"] = player.ContractTeamAddress,
            });
        }
        document["players"] = players;
        var teams = new JsonArray();
        foreach (var team in transaction.Teams)
        {
            teams.Add(new JsonObject
            {
                ["address"] = team.Address,
                ["index"] = team.Index,
                ["label"] = team.Label,
                ["values_before"] = Numbers(team.ValuesBefore),
                ["values_after"] = Numbers(team.ValuesAfter),
            });
        }
        document["teams"] = teams;
        document["details"] = new JsonArray(transaction.Details.Select(text => (JsonNode?)JsonValue.Create(text)).ToArray());
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
        var temporary = Path.ChangeExtension(path, ".tmp");
        File.WriteAllText(
            temporary,
            document.ToJsonString(JournalJson.Options),
            new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }

    private static JsonArray Numbers(IEnumerable<long> values) =>
        new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
}
