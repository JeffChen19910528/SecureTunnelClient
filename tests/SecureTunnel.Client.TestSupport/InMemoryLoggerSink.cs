namespace SecureTunnel.Client.TestSupport;

/// <summary>
/// Test double - not a real logging implementation. Captures every logged
/// line in memory so tests can assert that secret material never appears
/// in log output.
/// </summary>
public sealed class InMemoryLoggerSink
{
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines => _lines;

    public void Log(string line) => _lines.Add(line);

    public void Log(object?[] values) => _lines.Add(string.Join(' ', values.Select(v => v?.ToString() ?? "null")));

    public bool AnyLineContains(string fragment) =>
        _lines.Exists(l => l.Contains(fragment, StringComparison.Ordinal));
}
