using Nba2k27Editor.Core.Match;

namespace Nba2k27Editor.Tests;

internal static class MatchPauseStateTests
{
    public static void Run(Harness harness)
    {
        const long moduleBase = 0x7FF600000000;
        Func<long, int, byte[]> Memory(byte simulation, byte menu) => (address, size) =>
        {
            harness.Check("reads exactly one byte", size == 1);
            return address == moduleBase + MatchPauseState.SimulationStoppedOffset ? new[] { simulation }
                : address == moduleBase + MatchPauseState.PauseMenuOffset ? new[] { menu }
                : throw new InvalidOperationException("unexpected address");
        };

        harness.Check("pause menu open is paused", MatchPauseState.Read(moduleBase, Memory(1, 91)).Paused);
        harness.Check("pause sub-menu value is paused", MatchPauseState.Read(moduleBase, Memory(1, 90)).Paused);
        harness.Check("menu value after a restart is paused", MatchPauseState.Read(moduleBase, Memory(1, 218)).Paused);
        harness.Check("timeout is not a pause", !MatchPauseState.Read(moduleBase, Memory(1, 0)).Paused);
        harness.Check("live play is not a pause", !MatchPauseState.Read(moduleBase, Memory(0, 0)).Paused);
        harness.Check("menu byte without stopped simulation is not a pause",
            !MatchPauseState.Read(moduleBase, Memory(0, 91)).Paused);
        harness.Check("unexpected simulation value is not a pause",
            !MatchPauseState.Read(moduleBase, Memory(2, 91)).Paused);

        string timeout = harness.Throws("timeout blocks writing", () =>
            MatchPauseState.Require(moduleBase, Memory(1, 0), "应用"));
        harness.Check("timeout message asks for the pause menu", timeout.Contains("暂停菜单"), timeout);
        string live = harness.Throws("live play blocks writing", () =>
            MatchPauseState.Require(moduleBase, Memory(0, 0), "撤销"));
        harness.Check("live play message names the action", live.Contains("撤销"), live);
        harness.Equal("pause menu passes", (byte)91, MatchPauseState.Require(moduleBase, Memory(1, 91), "应用").PauseMenu);
    }
}
