using System.Diagnostics;
using GTMP.Server.Core.Configuration;

namespace GTMP.Server.Core.Runtime;

/// <summary>Anything that needs to run once per simulation tick.</summary>
public interface IServerTick
{
    /// <summary>
    /// Called at a fixed cadence. Implementations must not block; the tick loop
    /// compensates for overruns rather than letting them accumulate.
    /// </summary>
    void Tick(long tickNumber);
}

/// <summary>
/// Fixed cadence tick loop.
/// </summary>
/// <remarks>
/// Deliberately single threaded. Ordering between subsystems - replication, scripting,
/// persistence - is part of the contract, and parallel ticks would make the outcome
/// depend on scheduling.
/// </remarks>
public sealed class TickLoop
{
    private readonly List<IServerTick> _ticks = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public long TickNumber { get; private set; }

    public bool IsRunning { get; private set; }

    /// <summary>Set to false by the host to request a clean shutdown.</summary>
    public Func<bool>? ShouldContinue { get; set; }

    public int TickRate => Settings.TickRate;

    public required ServerSettings Settings { get; init; }

    public void Register(IServerTick tick) => _ticks.Add(tick);

    public void Run()
    {
        IsRunning = true;

        var targetInterval = TimeSpan.FromMilliseconds(Settings.TickRate);
        var nextTickAt = _clock.Elapsed;

        while (ShouldContinue?.Invoke() ?? true)
        {
            nextTickAt += targetInterval;

            RunSingleTick();

            var sleepTime = nextTickAt - _clock.Elapsed;

            if (sleepTime > TimeSpan.Zero)
                Thread.Sleep(sleepTime);
            else
            {
                // The tick took longer than the interval. Skip ahead instead of
                // spiralling, but keep the loop cadence anchored to the schedule.
                nextTickAt = _clock.Elapsed;
            }
        }

        IsRunning = false;
    }

    private void RunSingleTick()
    {
        TickNumber++;

        foreach (var tick in _ticks)
        {
            try
            {
                tick.Tick(TickNumber);
            }
            catch (Exception)
            {
                // One misbehaving subsystem must not take the whole server down. The
                // exception is surfaced by the logger that the subsystem holds.
                continue;
            }
        }
    }
}