using Microsoft.EntityFrameworkCore;
using Onudhabon.Data;

namespace Onudhabon.Services;

public sealed class ExpiredStudyDocumentCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExpiredStudyDocumentCleanupService> _logger;

    public ExpiredStudyDocumentCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<ExpiredStudyDocumentCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var deleted = await dbContext.StudyDocuments
                    .Where(document => document.ExpiresAt <= DateTime.UtcNow)
                    .ExecuteDeleteAsync(stoppingToken);
                if (deleted > 0)
                    _logger.LogInformation("Removed {ExpiredDocumentCount} expired AI study documents.", deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not clean up expired AI study documents.");
            }
        }
    }
}
