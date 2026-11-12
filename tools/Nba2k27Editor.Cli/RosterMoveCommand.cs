using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Memory;
using Nba2k27Editor.Core.Model;
using Nba2k27Editor.Core.Roster;

namespace Nba2k27Editor.Cli;

/// <summary>
/// Live roster move / trade verifier: apply, prove the new placement from an independent
/// connection, undo, then prove every touched byte is restored from a third connection.
/// </summary>
internal static class RosterMoveCommand
{
    private sealed record State(long CurrentTeam, long ContractTeam, long[] FromSlots, long[] ToSlots);

    public static int Run(Dictionary<string, string> options)
    {
        string mode = options.GetValueOrDefault("mode") ?? "move";
        if (!options.TryGetValue("out", out var outPath))
        {
            Console.Error.WriteLine("roster-move requires --out");
            return 2;
        }
        var dataDirectory = options.GetValueOrDefault("data-dir");
        var report = new JsonObject
        {
            ["format"] = "nba2k27-csharp-roster-move/1",
            ["version"] = AppVersion.Value,
            ["time"] = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
            ["mode"] = mode,
        };
        PlayerSession? writer = null;
        PlayerSession? observer = null;
        PlayerSession? verifier = null;
        try
        {
            writer = new PlayerSession("NBA2K27.exe", dataDirectory);
            writer.Connect();
            var mover = new RosterMover(writer);

            RecordListItem first;
            RecordListItem? second = null;
            if (mode == "trade")
            {
                if (!options.TryGetValue("first", out var firstLabel)
                    || !options.TryGetValue("second", out var secondLabel))
                {
                    Console.Error.WriteLine("roster-trade requires --first and --second");
                    return 2;
                }
                first = Resolve(writer.Items, firstLabel);
                second = Resolve(writer.Items, secondLabel);
            }
            else
            {
                if (!options.TryGetValue("player", out var playerLabel))
                {
                    Console.Error.WriteLine("roster-move requires --player");
                    return 2;
                }
                first = Resolve(writer.Items, playerLabel);
            }

            RecordListItem? target = null;
            if (mode != "trade")
            {
                if (!options.TryGetValue("team", out var teamLabel))
                {
                    Console.Error.WriteLine("roster-move requires --team");
                    return 2;
                }
                int? teamIndex = options.TryGetValue("team-index", out var indexText)
                    ? int.Parse(indexText, CultureInfo.InvariantCulture)
                    : null;
                var teams = writer.Model.ScanRecords("Teams", 512)
                    .Where(team => team.Label == teamLabel)
                    .ToList();
                if (teamIndex is not null)
                {
                    teams = teams.Where(team => team.Index == teamIndex).ToList();
                }
                if (teams.Count != 1)
                {
                    Console.Error.WriteLine($"team label must match exactly once: {teamLabel} ({teams.Count})");
                    return 3;
                }
                target = teams[0];
                report["team"] = new JsonObject
                {
                    ["label"] = target.Label,
                    ["index"] = target.Index,
                    ["address"] = Program.Hex(target.Address),
                };
            }

            var firstPlacement = mover.Placement(first);
            report["players"] = new JsonArray(
                new JsonObject
                {
                    ["player"] = first.Label,
                    ["index"] = first.Index,
                    ["address"] = Program.Hex(first.Address),
                    ["from_team"] = firstPlacement.TeamLabel,
                    ["from_slot"] = firstPlacement.Slot,
                },
                second is null
                    ? null
                    : new JsonObject
                    {
                        ["player"] = second.Label,
                        ["index"] = second.Index,
                        ["address"] = Program.Hex(second.Address),
                        ["from_team"] = mover.Placement(second).TeamLabel,
                        ["from_slot"] = mover.Placement(second).Slot,
                    });
            var fromTeam = mover.TeamsByAddress()[firstPlacement.TeamAddress];
            long toTeamAddress = target?.Address
                ?? mover.Placement(second!).TeamAddress;
            var toTeam = mover.TeamsByAddress()[toTeamAddress];
            var before = Capture(mover, first, fromTeam, toTeam, second);

            int writesBefore = Writes(writer);
            MovePlan plan;
            try
            {
                plan = mode == "trade"
                    ? mover.PlanTrade(first, second!)
                    : mover.PlanMove(first, target!);
            }
            catch (RosterMoveException exc)
            {
                report["status"] = "rejected";
                report["error"] = exc.Message;
                File.WriteAllText(outPath, report.ToJsonString(Program.JsonOptions), new UTF8Encoding(false));
                Console.Error.WriteLine($"rejected: {exc.Message}");
                return 4;
            }
            report["summary"] = plan.Summary;
            report["plan_patches"] = plan.Rows.Count;
            var transaction = mover.Apply(plan);
            report["writer_writes"] = Writes(writer) - writesBefore;
            report["journal"] = transaction.JournalPath;
            report["transaction_status"] = transaction.Status;

            observer = new PlayerSession("NBA2K27.exe", dataDirectory);
            observer.Connect();
            var observerMover = new RosterMover(observer);
            var afterApply = Capture(
                observerMover,
                Resolve(observer.Items, first.Label, first.Address),
                observerMover.TeamsByAddress()[fromTeam.Address],
                observerMover.TeamsByAddress()[toTeam.Address],
                second is null ? null : Resolve(observer.Items, second.Label, second.Address));
            report["observer_writes"] = Writes(observer);
            report["observer_current_team"] = Program.Hex(afterApply.CurrentTeam);
            report["observer_contract_team"] = Program.Hex(afterApply.ContractTeam);
            report["observer_expected_team"] = Program.Hex(toTeam.Address);
            report["observer_placement_matches"] = afterApply.CurrentTeam == toTeam.Address
                && afterApply.ContractTeam == toTeam.Address;
            report["observer_target_slots"] = Numbers(afterApply.ToSlots);
            report["observer_source_slots"] = Numbers(afterApply.FromSlots);

            int holdSeconds = options.TryGetValue("hold-seconds", out var holdText)
                ? int.Parse(holdText, CultureInfo.InvariantCulture)
                : 0;
            if (holdSeconds > 0)
            {
                Thread.Sleep(holdSeconds * 1000);
            }

            int undoWritesBefore = Writes(writer);
            mover.Undo();
            report["undo_writes"] = Writes(writer) - undoWritesBefore;

            verifier = new PlayerSession("NBA2K27.exe", dataDirectory);
            verifier.Connect();
            var verifierMover = new RosterMover(verifier);
            var final = Capture(
                verifierMover,
                Resolve(verifier.Items, first.Label, first.Address),
                verifierMover.TeamsByAddress()[fromTeam.Address],
                verifierMover.TeamsByAddress()[toTeam.Address],
                second is null ? null : Resolve(verifier.Items, second.Label, second.Address));
            report["verifier_writes"] = Writes(verifier);
            report["verifier_current_team"] = Program.Hex(final.CurrentTeam);
            report["verifier_contract_team"] = Program.Hex(final.ContractTeam);
            report["verifier_from_slots"] = Numbers(final.FromSlots);
            report["verifier_to_slots"] = Numbers(final.ToSlots);
            bool restored = final.CurrentTeam == before.CurrentTeam
                && final.ContractTeam == before.ContractTeam
                && final.FromSlots.SequenceEqual(before.FromSlots)
                && final.ToSlots.SequenceEqual(before.ToSlots);
            report["slots_before"] = Numbers(before.FromSlots);
            report["slots_before_target"] = Numbers(before.ToSlots);
            report["records_byte_exact_restored"] = restored;
            bool observed = report["observer_placement_matches"]!.GetValue<bool>();
            report["status"] = restored && observed ? "restored" : "needs_review";
            File.WriteAllText(outPath, report.ToJsonString(Program.JsonOptions), new UTF8Encoding(false));
            Console.WriteLine(
                $"{plan.Summary}: patches {plan.Rows.Count}, writer {report["writer_writes"]}, "
                + $"observer {report["observer_writes"]}, status {report["status"]}");
            return report["status"]!.GetValue<string>() == "restored" ? 0 : 5;
        }
        finally
        {
            writer?.Close();
            observer?.Close();
            verifier?.Close();
        }
    }

    private static RecordListItem Resolve(IReadOnlyList<RecordListItem> items, string label, long? address = null)
    {
        var matches = items.Where(item => item.Label == label
            && (address is null || item.Address == address)).ToList();
        if (matches.Count != 1)
        {
            throw new InvalidOperationException($"player must match exactly once: {label} ({matches.Count})");
        }
        return matches[0];
    }

    private static State Capture(
        RosterMover mover, RecordListItem player, RecordListItem fromTeam, RecordListItem toTeam,
        RecordListItem? second)
    {
        long current = Conversions.ToLong(mover.Model.ReadField(mover.CurrentTeamEntry(), player).RawValue);
        long contract = Conversions.ToLong(mover.Model.ReadField(mover.ContractTeamEntry(), player).RawValue);
        return new State(current, contract, mover.TeamSlots(fromTeam).Values, mover.TeamSlots(toTeam).Values);
    }

    private static int Writes(PlayerSession session) =>
        session.Memory is Win32Memory memory ? memory.Writes : 0;

    private static JsonArray Numbers(IEnumerable<long> values) =>
        new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
}
