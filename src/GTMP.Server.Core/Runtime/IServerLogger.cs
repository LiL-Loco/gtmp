namespace GTMP.Server.Core.Runtime;

/// <summary>Minimal logging sink. Replaced by a file/console implementation in the host.</summary>
public interface IServerLogger
{
    void Info(string message);

    void Warn(string message);

    void Error(string message, Exception? exception = null);
}

/// <summary>In-memory logger used by tests and by the core when no logger was supplied.</summary>
public sealed class InMemoryLogger : IServerLogger
{
    private readonly List<string> _lines = new();
    private readonly object _gate = new();

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_gate) return _lines.ToList();
        }
    }

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message} :: {exception}");

    private void Write(string level, string message)
    {
        lock (_gate) _lines.Add($"[{DateTime.Now:HH:mm:ss}] [{level}] {message}");
    }
}