using Nba2k27Editor.Core.Memory;
using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.Core.Editing;

/// <summary>Read-only equivalent of the verified MyNBA age getter; no game code is called.</summary>
public sealed record PlayerAgeContext(DateOnly? Date, int BaseYear, bool PreviousYearForRoster,
    bool UsesRosterYear, string? Error = null, int? SeasonEndYear = null)
{
    public static PlayerAgeContext Unknown(string error) => new(null, 0, false, false, error);

    public static PlayerAgeContext Read(IMemory memory)
    {
        try
        {
            long At(long rva) => memory.BaseAddress + rva;
            long Pointer(long rva) => FieldIo.ReadPointerValue(memory, At(rva));
            void Check(long rva, string hex)
            {
                byte[] expected = Convert.FromHexString(hex);
                if (!memory.ReadBytes(At(rva), expected.Length).SequenceEqual(expected))
                {
                    throw new InvalidOperationException("Unsupported native age getter");
                }
            }
            // These instruction bytes and routes belong to the editor's pinned 2K27 build.
            Check(0x21e9c40, "488B05398286080FB74018C3");
            Check(0x2ca3fc0, "4883EC28B8FF070000238180030000");
            long global = Pointer(0xaa51e80);
            byte[] season = memory.ReadBytes(global, 0x41);
            int mode = BitConverter.ToInt32(season, 8);
            // Both verified league modes dispatch to the same native calendar and roster-year routes.
            if (mode is not (13 or 25))
            {
                throw new InvalidOperationException("Native age date is unverified for this game mode");
            }
            int baseYear = BitConverter.ToUInt16(season, 0x18);
            int offset = (sbyte)season[0x1c];
            long dispatch = At(0xaa84c58);
            int active = memory.ReadBytes(dispatch + 0x20, 1)[0];
            if (active > 1)
            {
                throw new InvalidOperationException("Unsupported game context selector");
            }
            long context = FieldIo.ReadPointerValue(memory, dispatch + 0x10 + active * 8);
            long table = FieldIo.ReadPointerValue(memory, context);
            if (FieldIo.ReadPointerValue(memory, table + 0x468) != At(0x1cbc310))
            {
                throw new InvalidOperationException("Unsupported native player date route");
            }
            long settings = Pointer(0xaa51ee0);
            if (memory.ReadBytes(settings + 0x37, 1)[0] != 0)
            {
                throw new InvalidOperationException("Alternate native calendar is not verified");
            }
            long state = Pointer(0xaa51e88);
            int count = BitConverter.ToUInt16(memory.ReadBytes(state + 0x7c4c94, 2));
            if (count > 3000)
            {
                throw new InvalidOperationException("Invalid game calendar count");
            }
            uint lower = BitConverter.ToUInt32(memory.ReadBytes(At(0x7bfcce0), 4));
            byte[] events = memory.ReadBytes(state + 0x7c4c98, count * 12);
            DateOnly? date = null;
            for (int index = 0; index < count; index++)
            {
                int start = index * 12;
                uint packed = BitConverter.ToUInt32(events, start);
                if (BitConverter.ToInt32(events, start + 8) == 121 && packed >= lower && packed != 0)
                {
                    date = DecodeDate(packed);
                    break;
                }
            }
            if (date is null)
            {
                // Native fallback: calendar row 25 supplies the month/day for this season.
                byte[] rows = memory.ReadBytes(state + 0x79af70, 33 * 32);
                for (int index = 0; index < 33; index++)
                {
                    int start = index * 32;
                    int kind = BitConverter.ToInt32(rows, start);
                    if (kind == 0)
                    {
                        break;
                    }
                    if (kind == 25)
                    {
                        uint packed = BitConverter.ToUInt32(rows, start + 4);
                        date = new DateOnly(baseYear + offset + 1,
                            (int)((packed >> 3) & 15) + 1, (int)((packed >> 7) & 31));
                        break;
                    }
                }
            }
            if (date is null || baseYear is < 1900 or > 2200
                || date.Value.Year < 1900 || date.Value.Year > 2300)
            {
                throw new InvalidOperationException("Game calendar is unavailable");
            }
            long rules = FieldIo.ReadPointerValue(memory, context + 0xd0);
            long rosterYearMethod = FieldIo.ReadPointerValue(memory, rules + 0xb0);
            byte[] rosterYearCode = memory.ReadBytes(rosterYearMethod, 3);
            bool useRosterYear = rosterYearCode.SequenceEqual(new byte[] { 0xb0, 1, 0xc3 });
            if (!useRosterYear && !rosterYearCode.SequenceEqual(new byte[] { 0x31, 0xc0, 0xc3 }))
            {
                throw new InvalidOperationException("Native roster year rule is unverified");
            }
            int seasonEndYear = baseYear + offset + 1;
            if (seasonEndYear is < 1900 or > 2300)
                throw new InvalidOperationException("Game season year is unavailable");
            return new PlayerAgeContext(date, baseYear, true, useRosterYear, SeasonEndYear: seasonEndYear);
        }
        catch (Exception exc)
        {
            return Unknown(exc.Message);
        }
    }

    public static DateOnly DecodeDate(uint packed) =>
        new((int)(packed / 0x1800000) + 1960,
            (int)((packed >> 21) % 12) + 1, (int)((packed >> 16) & 31));

    public int? Age(int? year, int? month, int? day, int? rosterYear, int? prospectIndex)
    {
        if (Date is not DateOnly date || year is null || year < 1900
            || month is null or < 1 or > 12 || day is null or < 1 or > 31
            || rosterYear is null || prospectIndex is null)
        {
            return null;
        }
        try
        {
            if (PreviousYearForRoster && prospectIndex == 255)
            {
                date = date.AddYears(-1);
            }
            int birthYear = year.Value;
            if (UsesRosterYear && rosterYear > 0)
            {
                birthYear += BaseYear - rosterYear.Value;
            }
            // The native getter clamps February 29 when the effective birth year is not leap.
            int birthDay = Math.Min(day.Value, DateTime.DaysInMonth(birthYear, month.Value));
            int age = date.Year - birthYear;
            if (date.Month < month || date.Month == month && date.Day < birthDay)
            {
                age--;
            }
            return age is >= 0 and <= 120 ? age : null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
