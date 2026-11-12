namespace Nba2k27Editor.WebHost;

/// <summary>
/// Opt-in diagnostics. A normal run never touches the filesystem; setting
/// NBA2K27_WEBHOST_LOG to a path records connect timings and bridge traffic.
/// </summary>
internal static class Diagnostics
{
    private static readonly string? Path =
        Environment.GetEnvironmentVariable("NBA2K27_WEBHOST_LOG");

    internal static void Note(string line)
    {
        if (string.IsNullOrEmpty(Path))
        {
            return;
        }
        try
        {
            File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }
}
