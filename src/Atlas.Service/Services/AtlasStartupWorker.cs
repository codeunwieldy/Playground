using Atlas.Core.Contracts;
using Atlas.Storage;

namespace Atlas.Service.Services;

public sealed class AtlasStartupWorker(
    ILogger<AtlasStartupWorker> logger,
    AtlasDatabaseBootstrapper databaseBootstrapper,
    PolicyProfile profile) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Atlas service starting. Preparing storage and default policy profile {ProfileName}.", profile.ProfileName);
        await databaseBootstrapper.InitializeAsync(cancellationToken);
        logger.LogInformation("Atlas storage initialized.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Atlas service stopping.");
        return Task.CompletedTask;
    }
}