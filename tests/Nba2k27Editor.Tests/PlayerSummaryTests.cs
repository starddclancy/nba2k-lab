using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Memory;
using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.Tests;

internal static class PlayerSummaryTests
{
    public static void Run(Harness harness)
    {
        var memory = new SimulatedMemory(0x5000);
        var session = new PlayerSession(memory: memory);
        var item = new RecordListItem("Players", 12, 0x100, "Offline Summary Player");
        FieldSpec height = session.Specs["Vitals/Body/HEIGHT"];
        var appearance = session.Model.ParentPayload("Players", height.Payload)!;
        long appearanceSlot = item.Address + FieldIo.FieldOffset(appearance);
        memory.Load(appearanceSlot, BitConverter.GetBytes(0x2000L));
        void Set(string key, object value)
        {
            FieldSpec spec = session.Specs[key];
            long address = FieldIo.FieldAddress(memory, item.Address, spec.Payload,
                session.Model.ParentPayload("Players", spec.Payload));
            FieldIo.WriteAuthoredValue(memory, address, spec.Payload, session.Encode(spec, value));
        }
        Set("Vitals/Vitals/POSITION", "PG");
        Set("Vitals/Vitals/SECONDARYPOSITION", "SG");
        Set("Vitals/Vitals/BIRTHYEAR", 1988);
        Set("Vitals/Body/HEIGHT", 198.12);
        Set("Vitals/Body/WEIGHT", 200 * Conversions.PoundsToKilograms);
        Set("Attributes/Offense/3POINT", 85);
        Set("Vitals/Vitals/BIRTHMONTH", 9);
        Set("Vitals/Vitals/BIRTHDAY", 29);
        Set("Vitals/Roster/ROSTERYEAR", 2002);
        Set("Vitals/Vitals/DRAFTPROSPECTINDEX", 1);
        memory.Load(item.Address + 0x370, new byte[] { 0x40 });
        memory.Load(item.Address + 0x1ac, BitConverter.GetBytes(80.408615f));
        var context = new PlayerAgeContext(new DateOnly(2007, 10, 16), 2002, true, true);
        int writes = memory.Writes;
        PlayerSummary summary = PlayerSummary.Read(session, item, context);
        harness.Equal("summary primary position uses authored enum", "PG", summary.Position);
        harness.Equal("summary secondary position uses authored enum", "SG", summary.SecondaryPosition);
        harness.Equal("summary preserves birth year instead of computer-date age", 1988, summary.BirthYear);
        harness.Equal("summary carries birth month for photo matching", 9, summary.BirthMonth);
        harness.Equal("summary carries birth day for photo matching", 29, summary.BirthDay);
        harness.Equal("summary height is centimeters", 198.12, summary.HeightCm!.Value, 0.001);
        harness.Equal("summary weight is kilograms", 200 * Conversions.PoundsToKilograms,
            summary.WeightKg!.Value, 0.001);
        harness.Equal("summary three-point value uses rating decoder", 85, summary.ThreePoint);
        harness.Equal("summary age uses game date and birthday", 19, summary.Age);
        harness.Equal("summary overall rounds native float", 80, summary.Overall);
        harness.Equal("summary reads perform no writes", writes, memory.Writes);

        var fields = new Dictionary<string, FieldValue>(StringComparer.Ordinal);
        foreach (string key in new[] { "Vitals/Vitals/POSITION", "Vitals/Vitals/SECONDARYPOSITION",
            "Vitals/Vitals/BIRTHYEAR", "Vitals/Body/HEIGHT", "Vitals/Body/WEIGHT", "Attributes/Offense/3POINT",
            "Vitals/Vitals/BIRTHMONTH", "Vitals/Vitals/BIRTHDAY", "Vitals/Roster/ROSTERYEAR",
            "Vitals/Vitals/DRAFTPROSPECTINDEX", "Vitals/Type/PLAYERTYPE", "Vitals/Type/OFFENSIVEPLAYERTYPE" })
        {
            FieldSpec spec = session.Specs[key];
            FieldRead value = session.Model.ReadField(spec.Entry, item);
            fields[key] = new FieldValue(value.RawValue, session.Decode(spec, value.RawValue), value.Address, false);
        }
        var snapshot = new PlayerSnapshot(item, new SessionEpoch(1, 2, 3, 4, 5, 6, 7),
            Array.Empty<byte>(), fields, new Dictionary<string, string>());
        harness.Equal("list and detail summaries decode identically", summary,
            PlayerSummary.FromSnapshot(snapshot, context, PlayerSummary.ReadOverall(session, item)));
        harness.Equal("missing calendar never uses computer year", null, PlayerSummary.Read(session, item).Age);
        memory.Load(item.Address + 0x370, new byte[1]);
        harness.Equal("invalidated overall cache stays unknown", null, PlayerSummary.ReadOverall(session, item));
        memory.Load(item.Address + 0x370, new byte[] { 0x40 });
        memory.Load(item.Address + 0x1ac, BitConverter.GetBytes(float.NaN));
        harness.Equal("nonfinite overall cache stays unknown", null, PlayerSummary.ReadOverall(session, item));
        memory.Load(item.Address + 0x1ac, BitConverter.GetBytes(80.408615f));

        memory.Load(appearanceSlot, new byte[8]);
        PlayerSummary unloaded = PlayerSummary.Read(session, item);
        harness.Equal("missing appearance leaves height unknown", null, unloaded.HeightCm);
        harness.Equal("missing appearance preserves direct weight", summary.WeightKg, unloaded.WeightKg);
        harness.Equal("missing appearance preserves direct position", "PG", unloaded.Position);
        harness.Equal("missing appearance preserves direct birth year", 1988, unloaded.BirthYear);
        harness.Equal("missing appearance preserves direct rating", 85, unloaded.ThreePoint);
        harness.Equal("missing appearance summary never writes", writes, memory.Writes);
        memory.Load(appearanceSlot, BitConverter.GetBytes(0x3000L));
        memory.Load(0x3000, BitConverter.GetBytes((ushort)21000));
        harness.Equal("summary follows the current appearance pointer", 210.0,
            PlayerSummary.Read(session, item).HeightCm!.Value, 0.001);
        memory.Load(appearanceSlot, BitConverter.GetBytes(0x2000L));

        fields["Vitals/Vitals/POSITION"] = fields["Vitals/Vitals/POSITION"] with { Display = "7" };
        fields["Vitals/Vitals/SECONDARYPOSITION"] = fields["Vitals/Vitals/SECONDARYPOSITION"] with { Display = "N/A" };
        fields["Vitals/Vitals/BIRTHYEAR"] = fields["Vitals/Vitals/BIRTHYEAR"] with { Display = 0 };
        fields["Vitals/Body/HEIGHT"] = fields["Vitals/Body/HEIGHT"] with { Display = double.NaN };
        fields["Vitals/Body/WEIGHT"] = fields["Vitals/Body/WEIGHT"] with { Display = double.PositiveInfinity };
        fields["Attributes/Offense/3POINT"] = fields["Attributes/Offense/3POINT"] with { Display = 110 };
        PlayerSummary invalid = PlayerSummary.FromSnapshot(snapshot);
        harness.Equal("unknown primary position stays unknown", null, invalid.Position);
        harness.Equal("absent secondary position stays unknown", null, invalid.SecondaryPosition);
        harness.Equal("zero birth year stays unknown", null, invalid.BirthYear);
        harness.Equal("nonfinite height stays unknown", null, invalid.HeightCm);
        harness.Equal("nonfinite weight stays unknown", null, invalid.WeightKg);
        harness.Equal("invalid displayed rating stays unknown", null, invalid.ThreePoint);
        PlayerSummary missing = PlayerSummary.Read(session, item with { Address = 0x5000 });
        harness.Equal("unreadable record does not become point guard", null, missing.Position);
        harness.Equal("unreadable record does not become a zero rating", null, missing.ThreePoint);
        harness.Equal("invalid reads perform no writes", writes, memory.Writes);
        fields.Clear();
        harness.Equal("empty snapshot summary keeps all fields unknown",
            new PlayerSummary(null, null, null, null, null, null), PlayerSummary.FromSnapshot(snapshot));
        fields["Vitals/Vitals/BIRTHYEAR"] = new FieldValue(1988.5, 1988.5, 0, false);
        harness.Equal("fractional birth year is not rounded into a false year",
            null, PlayerSummary.FromSnapshot(snapshot).BirthYear);

        harness.Equal("packed native date decodes year month day", new DateOnly(2007, 10, 16),
            PlayerAgeContext.DecodeDate(0x47b00001));
        harness.Equal("age before birthday", 18, context.Age(1988, 10, 17, 2002, 1));
        harness.Equal("age on birthday", 19, context.Age(1988, 10, 16, 2002, 1));
        harness.Equal("roster uses native prior year rule", 31, context.Age(1975, 6, 7, 2002, 255));
        harness.Equal("historic roster year shifts effective birth year", 25, context.Age(1975, 6, 7, 1996, 255));
        harness.Equal("zero roster year does not shift birthday", 31, context.Age(1975, 6, 7, 0, 255));
        harness.Equal("unavailable calendar leaves age unknown", null,
            PlayerAgeContext.Unknown("missing").Age(1988, 9, 29, 2002, 1));
        harness.Equal("missing birthday leaves age unknown", null, context.Age(1988, null, 29, 2002, 1));
        harness.Equal("missing prospect type leaves age unknown", null, context.Age(1988, 9, 29, 2002, null));
        harness.Equal("future birth year does not become negative age", null, context.Age(2010, 9, 29, 2002, 1));
        harness.Equal("invalid month leaves age unknown", null, context.Age(1988, 13, 29, 2002, 1));
        harness.Equal("malformed birth year 18 stays unknown", null, context.Age(18, 6, 19, 0, 255));
        var leap = new PlayerAgeContext(new DateOnly(2007, 2, 28), 2002, false, true);
        harness.Equal("native leap birthday clamp", 24, leap.Age(1988, 2, 29, 2007, 1));
        harness.Equal("overall midpoint follows native nearest-even rounding", 80, PlayerSummary.DecodeOverall(80.5));
        harness.Equal("overall upper midpoint follows native nearest-even rounding", 82, PlayerSummary.DecodeOverall(81.5));
        harness.Equal("unset overall is not a zero rating", null, PlayerSummary.DecodeOverall(0));
        harness.Equal("out of range overall stays unknown", null, PlayerSummary.DecodeOverall(110));
    }
}
