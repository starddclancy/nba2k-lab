using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.Core.Editing;

/// <summary>Read-only roster facts decoded with the same rules as the player editor.</summary>
public sealed record PlayerSummary(string? Position, string? SecondaryPosition, int? BirthYear,
    double? HeightCm, double? WeightKg, int? ThreePoint, int? Age = null, int? Overall = null,
    int? BirthMonth = null, int? BirthDay = null, string? PlayerType = null, string? OffensiveType = null)
{
    private static readonly string[] Keys =
    {
        "Vitals/Vitals/POSITION", "Vitals/Vitals/SECONDARYPOSITION", "Vitals/Vitals/BIRTHYEAR",
        "Vitals/Body/HEIGHT", "Vitals/Body/WEIGHT", "Attributes/Offense/3POINT",
        "Vitals/Vitals/BIRTHMONTH", "Vitals/Vitals/BIRTHDAY", "Vitals/Roster/ROSTERYEAR",
        "Vitals/Vitals/DRAFTPROSPECTINDEX", "Vitals/Type/PLAYERTYPE", "Vitals/Type/OFFENSIVEPLAYERTYPE",
    };

    public static PlayerSummary Read(PlayerSession session, RecordListItem item, PlayerAgeContext? context = null)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (string key in Keys)
        {
            if (!session.Specs.TryGetValue(key, out FieldSpec? spec))
            {
                continue;
            }
            try
            {
                FieldRead value = session.Model.ReadField(spec.Entry, item);
                values[key] = session.Decode(spec, value.RawValue);
            }
            catch (Exception)
            {
                // Missing facts stay unknown; they must not become a zero or a guessed position.
            }
        }
        return FromValues(values, context ?? PlayerAgeContext.Read(session.Memory), ReadOverall(session, item));
    }

    public static PlayerSummary FromSnapshot(PlayerSnapshot snapshot, PlayerAgeContext? context = null,
        int? overall = null) =>
        FromValues(Keys.Where(snapshot.Values.ContainsKey)
            .ToDictionary(key => key, key => (object?)snapshot.Values[key].Display, StringComparer.Ordinal),
            context, overall);

    public static int? ReadOverall(PlayerSession session, RecordListItem item)
    {
        try
        {
            // Bit 6 is the native getter's cache-valid bit. Never trigger its recalculation.
            if ((session.Memory.ReadBytes(item.Address + 0x370, 1)[0] & 0x40) == 0)
            {
                return null;
            }
            var entry = session.Model.FieldByName("Players", "CACHEDOVERALLRATINGNEVERCALL");
            if (entry is null)
            {
                return null;
            }
            double raw = Convert.ToDouble(session.Model.ReadField(entry, item).RawValue);
            return DecodeOverall(raw);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static int? DecodeOverall(double raw) =>
        double.IsFinite(raw) && raw is >= 25 and <= 99
            ? (int)Math.Round(raw, MidpointRounding.ToEven) : null;

    private static PlayerSummary FromValues(IReadOnlyDictionary<string, object?> values,
        PlayerAgeContext? context, int? overall)
    {
        string? PositionAt(int index)
        {
            string? value = values.GetValueOrDefault(Keys[index])?.ToString();
            return value is "PG" or "SG" or "SF" or "PF" or "C" ? value : null;
        }
        double? NumberAt(int index, double low, double high)
        {
            double number = Conversions.ToDouble(values.GetValueOrDefault(Keys[index]), double.NaN);
            return double.IsFinite(number) && number >= low && number <= high ? number : null;
        }
        // The game's own player type (enum PLAYERDATA_PLAYERTYPE); unknown text stays null.
        string? TypeAt(int index) => values.GetValueOrDefault(Keys[index])?.ToString() is string type
            && type is "ALL_AROUND" or "TWO_WAY" or "OFFENSIVE" or "DEFENSIVE" or "ATHLETIC" or "REBOUNDING"
                or "PLAYMAKING" or "OUTSIDE" or "INSIDE" ? type : null;
        int? IntegerAt(int index, int low, int high) =>
            NumberAt(index, low, high) is double number && number == Math.Truncate(number)
                ? (int)number : null;

        return new PlayerSummary(PositionAt(0), PositionAt(1), IntegerAt(2, 1800, 9999),
            NumberAt(3, Conversions.HeightMinInches * 2.54, Conversions.HeightMaxInches * 2.54),
            NumberAt(4, Conversions.WeightMinPounds * Conversions.PoundsToKilograms,
                Conversions.WeightMaxPounds * Conversions.PoundsToKilograms),
            IntegerAt(5, Conversions.RatingMin, Conversions.RatingMaxDisplay),
            context?.Age(IntegerAt(2, 1900, 9999), IntegerAt(6, 1, 12), IntegerAt(7, 1, 31),
                IntegerAt(8, 0, 2047), IntegerAt(9, 0, 255)), overall, IntegerAt(6, 1, 12), IntegerAt(7, 1, 31),
            TypeAt(10), TypeAt(11));
    }
}
