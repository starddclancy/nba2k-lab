using System.Text.Json.Nodes;

namespace Nba2k27Editor.Tests;

/// <summary>Minimal dependency-free assertion harness; the suite must run without NuGet restore.</summary>
internal sealed class Harness
{
    private readonly List<string> _failures = new();
    private int _passed;

    public int Passed => _passed;
    public IReadOnlyList<string> Failures => _failures;

    public void Check(string name, bool condition, string? detail = null)
    {
        if (condition)
        {
            _passed++;
            return;
        }
        _failures.Add(detail is null ? name : $"{name}: {detail}");
    }

    public void Equal<T>(string name, T expected, T actual)
    {
        Check(name, EqualityComparer<T>.Default.Equals(expected, actual), $"expected {expected}, got {actual}");
    }

    /// <summary>Floating point comparison for values reconstructed from process start times.</summary>
    public void Equal(string name, double expected, double actual, double tolerance)
    {
        Check(name, Math.Abs(expected - actual) <= tolerance, $"expected {expected} +/- {tolerance}, got {actual}");
    }

    public void Sequence(string name, IReadOnlyList<byte> expected, IReadOnlyList<byte> actual)
    {
        bool same = expected.Count == actual.Count;
        for (int index = 0; same && index < expected.Count; index++)
        {
            same = expected[index] == actual[index];
        }
        Check(name, same, $"expected {Hex(expected)}, got {Hex(actual)}");
    }

    /// <summary>Run an action that must throw; returns the message for exact comparison.</summary>
    public string Throws(string name, Action action, string? expected = null)
    {
        try
        {
            action();
        }
        catch (Exception exc)
        {
            string message = exc.Message;
            if (expected is not null)
            {
                Check(name, message == expected, $"expected \"{expected}\", got \"{message}\"");
            }
            else
            {
                _passed++;
            }
            return message;
        }
        _failures.Add($"{name}: expected an exception, nothing was thrown");
        return string.Empty;
    }

    public static string Hex(IReadOnlyList<byte> raw)
    {
        var builder = new System.Text.StringBuilder(raw.Count * 2);
        foreach (byte value in raw)
        {
            builder.Append(value.ToString("x2"));
        }
        return builder.ToString();
    }

    public static JsonObject Payload(string json) => (JsonObject)JsonNode.Parse(json)!;
}
