using LawLms.Application;

namespace LawLms.Api;

public sealed class AttemptExpiryWorker(IServiceScopeFactory scopes,ILogger<AttemptExpiryWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(10));
        var ticks=0;
        while(await timer.WaitForNextTickAsync(stoppingToken))
        {
            try{using var scope=scopes.CreateScope();var operations=scope.ServiceProvider.GetRequiredService<IOperationsService>();await operations.ExpireAttemptsAsync(stoppingToken);if(ticks++%6==0)await operations.GenerateNotificationsAsync(stoppingToken);}
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception error){logger.LogError(error,"Không thể xử lý lượt hết hạn; sẽ thử lại ở chu kỳ sau.");}
        }
    }
}
