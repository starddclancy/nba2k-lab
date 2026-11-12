namespace Nba2k27Editor.Core.Match;

/// <summary>
/// The game's own pause state, read from two bytes in the main module's static data.
/// Found 2026-11-08 by live read-only comparison on the verified NBA2K27.exe build
/// (see artifacts/claude-next/pause-scan/FINDINGS.md):
/// <list type="bullet">
/// <item><c>+0x899C244</c>: 1 while the pause menu holds the match; 0 in play, dead balls and
/// in-game timeouts (re-checked after a game restart).</item>
/// <item><c>+0xAC67BEC</c>: non-zero only while the pause menu is open. The value is not stable
/// across launches (90/91 in one, 218 in the next), so only "non-zero" is meaningful.</item>
/// </list>
/// Both are required. One early sample showed the first byte set with the second at 0 outside the
/// pause menu, so neither byte alone is trusted. A false "not paused" only refuses a write.
/// The offsets belong to that exact executable; the connection verifies its SHA-256 before use.
/// </summary>
public static class MatchPauseState
{
    public const long SimulationStoppedOffset = 0x899C244;
    public const long PauseMenuOffset = 0xAC67BEC;

    public sealed record Reading(byte SimulationStopped, byte PauseMenu)
    {
        public bool Paused => SimulationStopped == 1 && PauseMenu != 0;
    }

    public static Reading Read(long moduleBase, Func<long, int, byte[]> read) =>
        new(read(moduleBase + SimulationStoppedOffset, 1)[0], read(moduleBase + PauseMenuOffset, 1)[0]);

    /// <summary>Throw unless the game is in its pause menu; nothing has been written when this throws.</summary>
    public static Reading Require(long moduleBase, Func<long, int, byte[]> read, string action)
    {
        var state = Read(moduleBase, read);
        if (!state.Paused)
        {
            throw new InvalidOperationException(state.SimulationStopped == 1
                ? $"比赛处于暂停（timeout）或死球等非菜单状态，请按暂停键打开暂停菜单后再{action}"
                : $"比赛正在进行，请先在游戏内打开暂停菜单后再{action}");
        }
        return state;
    }
}
