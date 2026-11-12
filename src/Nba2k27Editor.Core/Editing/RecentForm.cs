namespace Nba2k27Editor.Core.Editing;

/// <summary>One of the last five games the game keeps per player (Stats/History/GAMESCOREHISTORY*).</summary>
public sealed record FormGame(int Slot, DateTimeOffset Date, int Minutes, double GameScore);

/// <summary>A team's recent results as stored on the team record (Vitals LAST10*, *WINLOSSSTREAK).</summary>
public sealed record TeamForm(string Last10, int Wins, int Losses, string Last10Home, string Last10Away,
    int Streak, int HomeStreak, int AwayStreak, int PreviousStreak, int LongestWin, int LongestLoss, int HomeWins, int HomeLosses);

/// <summary>
/// Decoders for the recent-form fields. Verified on the live roster (2026-11-08): GAMESCOREHISTORYVALUE
/// is an IEEE float (raw 1078355556 = 3.1), GAMESCOREHISTORYDATE is Unix seconds (1213437443 =
/// 2008-06-14), slot 0 is the newest game; LAST10 is a 10-bit mask with bit 0 = most recent game and
/// 1 = win (Cleveland 647 = WWWLLLLWLW with WINLOSSSTREAK 3); streaks are signed bytes (255 = -1).
/// </summary>
public static class RecentForm
{
    public static double Float(long raw) => Math.Round(BitConverter.Int32BitsToSingle(unchecked((int)(uint)raw)), 2);

    public static int SignedByte(long raw) => raw > 127 ? (int)raw - 256 : (int)raw;

    /// <summary>Most recent game first, W for a win and L for a loss.</summary>
    public static string Results(long mask, int count = 10)
    {
        var text = new char[Math.Clamp(count, 0, 10)];
        for (int i = 0; i < text.Length; i++)
        {
            text[i] = ((mask >> i) & 1) == 1 ? 'W' : 'L';
        }
        return new string(text);
    }

    /// <summary>Games with a date, newest first; empty slots (date 0) are skipped.</summary>
    public static IReadOnlyList<FormGame> Games(IReadOnlyList<long> dates, IReadOnlyList<long> minutes, IReadOnlyList<long> values)
    {
        var games = new List<FormGame>();
        for (int slot = 0; slot < Math.Min(dates.Count, Math.Min(minutes.Count, values.Count)); slot++)
        {
            if (dates[slot] <= 0)
            {
                continue;
            }
            games.Add(new FormGame(slot, DateTimeOffset.FromUnixTimeSeconds(dates[slot]), (int)minutes[slot], Float(values[slot])));
        }
        return games;
    }

    public static TeamForm Team(Func<string, long> field)
    {
        int count = (int)Math.Clamp(field("LAST10COUNT") is var c and > 0 ? c : 10, 0, 10);
        string last10 = Results(field("LAST10"), count);
        return new TeamForm(last10, last10.Count(ch => ch == 'W'), last10.Count(ch => ch == 'L'),
            Results(field("LAST10HOME"), (int)Math.Clamp(field("LAST10HOMECOUNT"), 0, 10)),
            Results(field("LAST10AWAY"), (int)Math.Clamp(field("LAST10AWAYCOUNT"), 0, 10)),
            SignedByte(field("WINLOSSSTREAK")), SignedByte(field("HOMEWINLOSSSTREAK")), SignedByte(field("AWAYWINLOSSSTREAK")),
            SignedByte(field("PREVIOUSWINLOSSSTREAK")), (int)field("LONGESTWINSTREAK"), (int)field("LONGESTLOSSSTREAK"),
            (int)field("SEASONHOMEWINS"), (int)field("SEASONHOMELOSSES"));
    }
}
