using Microsoft.Extensions.Options;

namespace Atlas.Service.Services;

/// <summary>
/// Background worker that periodically runs conversation compaction
/// based on configured cadence and retention options.
/// </summary>
public sealed class ConversationCompactionWorker(
    IOptions<AtlasServiceOptions> options,
    ConversationCompactionService compactionService,
    ILogger<ConversationCompactionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var opts = options.Value;

            if (!opts.EnableConversationCompaction)
            {
                await Task.Delay(opts.CompactionInterval, stoppingToken);
                continue;
            }

            try
            {
                var result = await compactionService.CompactAsync(
                    opts.CompactionRetentionWindow,
                    opts.CompactionMinMessages,
                    opts.CompactionMaxCandidatesPerCycle,
                    stoppingToken);

                if (result.ConversationsCompacted > 0)
                {
                    logger.LogInformation(
                        "Conversation compaction cycle complete: {Evaluated} evaluated, {Compacted} compacted.",
                        result.ConversationsEvaluated,
                        result.ConversationsCompacted);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Conversation compaction cycle failed.");
            }

            await Task.Delay(opts.CompactionInterval, stoppingToken);
        }
    }
}
