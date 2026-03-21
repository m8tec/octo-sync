namespace OctoSync.Core.Interfaces;

public interface ISyncOrchestrator
{
    Task RunCycleAsync(CancellationToken cancellationToken);
}