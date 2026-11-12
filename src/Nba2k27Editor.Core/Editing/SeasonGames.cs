using Nba2k27Editor.Core.Memory;
using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.Core.Editing;

/// <summary>One player's line in a game, as the game stores it (24-byte row).</summary>
public sealed record GameLine(int PlayerUid, double Minutes, int PlusMinus, int Points, int FgMade, int FgAttempted,
    int ThreeMade, int ThreeAttempted, int FtMade, int FtAttempted, int OffensiveRebounds, int DefensiveRebounds,
    int Assists, int Steals, int Blocks, int Turnovers, int Fouls);

/// <summary>One scheduled or played game of the current season (1356-byte record).</summary>
public sealed record SeasonGame(int Id, long Address, uint DateCode, DateOnly Date, bool Final, int HomeUid, int AwayUid,
    int[] HomeQuarters, int[] AwayQuarters, IReadOnlyList<GameLine> Home, IReadOnlyList<GameLine> Away)
{
    public bool Played => Final;
    public int HomePoints => Home.Sum(line => line.Points);
    public int AwayPoints => Away.Sum(line => line.Points);
}

/// <summary>
/// The season's game table: every game of the schedule, played games with full box scores.
/// Decoded on the live 2008-09 save (2026-11-08) and checked against the user's in-game
/// box scores: Iverson 12/19 vs DEN (25 min, 14 pts, 4/12, 0/2, 6/8, 1 OREB, 4 AST, 2 TOV,
/// 2 PF, -10) and 12/17 vs SAC (28 min, 28 pts, 11/19, 1/2, 5/5, +22), Game Score reproduced
/// exactly for all 37 player lines of both games, and the Rockets schedule 12/22-1/3.
/// Layout: +0 packed date (twice), +8 game id, +12 mutable metadata, +16 home team UNIQUEID, +18 away;
/// +24 home lines and +648 away lines (26 rows of 24 bytes each); +1296 home quarters,
/// +1300 away quarters (one byte per quarter), +1304/+1305 first overtime home/away (later periods add
/// two bytes each, so total = lines; checked on all 22 overtime games). Read-only.
/// </summary>
public static class SeasonGames
{
    public const int Stride = 1356;
    private const int SideOffset = 24;
    private const int SideSize = 624;
    private const int RowSize = 24;

    /// <summary>
    /// Packed date: bits 21-31 months since January 1960, bits 16-20 day of month; the low
    /// 16 bits are the tip-off slot. 0x49739602 = 2008-12-19, 0x4982A8C1 = 2009-01-02.
    /// The same encoding is used by the player GAMESCOREHISTORYDATE fields.
    /// </summary>
    public static DateOnly? Date(uint code)
    {
        int months = (int)(code >> 21);
        int day = (int)((code >> 16) & 31);
        int year = 1960 + months / 12;
        int month = months % 12 + 1;
        return day >= 1 && day <= DateTime.DaysInMonth(Math.Clamp(year, 1, 9999), month) && year is >= 1960 and <= 2200
            ? new DateOnly(year, month, day) : null;
    }

    public static GameLine? Line(byte[] row, int at)
    {
        if (at < 0 || at > row.Length - RowSize)
        {
            throw new InvalidOperationException("单场球员记录不足 24 字节");
        }
        int uid = BitConverter.ToUInt16(row, at);
        if (row[at + 2] == 0 && row.Skip(at + 4).Take(8).All(b => b == 0))
        {
            return null;
        }
        ulong bits = BitConverter.ToUInt64(row, at + 16);
        int Bits(int start, int width) => (int)((bits >> start) & ((1UL << width) - 1));
        int fgm = row[at + 4], tpm = row[at + 6] >> 1, ftm = Bits(7, 7);
        var line = new GameLine(uid, row[at + 2] / 2.0, unchecked((sbyte)row[at + 3]), 2 * fgm + tpm + ftm,
            fgm, row[at + 5], tpm, row[at + 7], ftm, row[at + 8], Bits(14, 8), row[at + 11],
            Bits(0, 7), row[at + 9], row[at + 10], Bits(22, 8), Bits(40, 3));
        if (line.FgMade > line.FgAttempted || line.ThreeMade > line.ThreeAttempted
            || line.ThreeMade > line.FgMade || line.FtMade > line.FtAttempted)
        {
            throw new InvalidOperationException("单场球员投篮统计不一致，请重新读取");
        }
        return line;
    }

    public static SeasonGame Decode(byte[] bytes, int offset, long address)
    {
        if (offset < 0 || offset > bytes.Length - Stride || !IsRecord(bytes, offset))
        {
            throw new InvalidOperationException("比赛记录的大小、日期或身份无效");
        }
        uint code = BitConverter.ToUInt32(bytes, offset);
        List<GameLine> Side(int side)
        {
            var lines = new List<GameLine>();
            for (int slot = 0; slot < SideSize / RowSize; slot++)
            {
                if (Line(bytes, offset + SideOffset + side * SideSize + slot * RowSize) is { } line)
                {
                    lines.Add(line);
                }
            }
            return lines;
        }
        int[] Quarters(int at) => Enumerable.Range(0, 4).Select(q => (int)bytes[offset + at + q]).ToArray();
        // Overtime periods follow the quarters as home/away byte pairs; keep those the lines need.
        var home = Side(0);
        var away = Side(1);
        var hq = Quarters(1296).ToList();
        var aq = Quarters(1300).ToList();
        for (int ot = 0; ot < 6 && (hq.Sum() < home.Sum(l => l.Points) || aq.Sum() < away.Sum(l => l.Points)); ot++)
        {
            hq.Add(bytes[offset + 1304 + ot * 2]);
            aq.Add(bytes[offset + 1305 + ot * 2]);
        }
        return new SeasonGame(BitConverter.ToInt32(bytes, offset + 8), address, code, Date(code) ?? default,
            BitConverter.ToUInt32(bytes, offset + 4) == code,
            BitConverter.ToUInt16(bytes, offset + 16), BitConverter.ToUInt16(bytes, offset + 18),
            hq.ToArray(), aq.ToArray(), home, away);
    }

    private static bool IsRecord(byte[] bytes, int at) =>
        at >= 0 && at <= bytes.Length - Stride && IsHeader(bytes, at);

    private static bool IsHeader(byte[] bytes, int at) =>
        at >= 0 && at <= bytes.Length - 24
        // +4 repeats the date once the game is played and is 0 before that.
        && BitConverter.ToUInt32(bytes, at + 4) is var played && (played == 0 || played == BitConverter.ToUInt32(bytes, at))
        && Date(BitConverter.ToUInt32(bytes, at)) is not null
        && BitConverter.ToInt32(bytes, at + 8) is > 0 and < 10000;

    /// <summary>
    /// Follow the registered native-game manager: module slot -> manager +160 -> container +40.
    /// The host resolves the module slot for the current executable; addresses are never reused.
    /// </summary>
    public static (long Address, int Count) Resolve(IMemory memory, long managerSlot)
    {
        long Pointer(long at)
        {
            long value = BitConverter.ToInt64(memory.ReadBytes(at, 8));
            if (value <= 0 || (value & 7) != 0)
                throw new InvalidOperationException("原生比赛表根指针无效，请确认已载入赛季存档");
            return value;
        }
        long manager = Pointer(managerSlot);
        long container = Pointer(checked(manager + 160));
        long address = checked(container + 40);
        int count = 0;
        while (count < 10000)
        {
            byte[] header;
            try { header = memory.ReadBytes(checked(address + (long)count * Stride), 24); }
            catch (InvalidOperationException) { break; }
            if (!IsHeader(header, 0)) break;
            count++;
        }
        if (count is < 32 or >= 10000)
            throw new InvalidOperationException("原生比赛表数量无效，请确认已载入赛季存档");
        if (Pointer(managerSlot) != manager || Pointer(manager + 160) != container)
            throw new InvalidOperationException("原生比赛表在读取期间变化，请刷新");
        return (address, count);
    }

    /// <summary>Find a contiguous run of complete 1356-byte game records. Cached by caller.</summary>
    public static (long Address, int Count)? Locate(Win32Memory memory, long hint = 0)
    {
        var regions = memory.PrivateRegions().OrderByDescending(r => hint >= r.Base && hint < r.Base + r.Size).ToArray();
        foreach (var (regionBase, size) in regions)
        {
            if (size < Stride * 32 || size > 256L << 20)
            {
                continue;
            }
            byte[] bytes;
            try { bytes = memory.ReadBytes(regionBase, (int)size); }
            catch (InvalidOperationException) { continue; }
            if (Locate(bytes, regionBase) is { } found) return found;
        }
        return null;
    }

    public static (long Address, int Count)? Locate(byte[] bytes, long regionBase)
    {
        for (int i = 0; i + Stride * 3 <= bytes.Length; i += 4)
        {
            if (!IsRecord(bytes, i) || !IsRecord(bytes, i + Stride) || !IsRecord(bytes, i + 2 * Stride))
            {
                continue;
            }
            int start = i;
            while (IsRecord(bytes, start - Stride)) start -= Stride;
            int count = 0;
            // +12 changes for the loaded match; it cannot be used as a table-wide identity key.
            while (IsRecord(bytes, start + count * Stride)) count++;
            if (count >= 32)
            {
                return (regionBase + start, count);
            }
            i = start + count * Stride;
        }
        return null;
    }

    public static IReadOnlyList<SeasonGame> Read(IMemory memory, long address, int count)
    {
        if (address <= 0 || count is < 1 or > 10000)
        {
            throw new InvalidOperationException("比赛表地址或数量无效");
        }
        byte[] bytes = memory.ReadBytes(address, checked(count * Stride));
        var games = new List<SeasonGame>(count);
        for (int i = 0; i < count; i++)
        {
            if (!IsRecord(bytes, i * Stride))
            {
                throw new InvalidOperationException("比赛记录表已变化，请重新读取");
            }
            games.Add(Decode(bytes, i * Stride, address + (long)i * Stride));
        }
        return games;
    }
}
