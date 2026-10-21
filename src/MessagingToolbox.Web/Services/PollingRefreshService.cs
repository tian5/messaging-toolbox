namespace MessagingToolbox.Web.Services;

/// <summary>
/// Scoped auto-refresh timer (FR-2.2): raises <see cref="Ticked"/> on an interval; Blazor
/// circuits subscribe and re-run their tree/count queries. AMQP pressure stays in the provider.
/// </summary>
public sealed class PollingRefreshService : IDisposable
{
    private readonly ILogger<PollingRefreshService> _logger;
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cancellationTokenSource;

    public PollingRefreshService(ILogger<PollingRefreshService> logger)
    {
        _logger = logger;
    }

    public event Func<Task>? Ticked;

    public bool IsRunning => _timer is not null;

    public void Start(TimeSpan interval)
    {
        Stop();

        _cancellationTokenSource = new CancellationTokenSource();
        _timer = new PeriodicTimer(interval);
        // Each loop owns its timer: after a restart the old loop must not pick up the new one.
        _ = RunLoopAsync(_timer, _cancellationTokenSource.Token);
    }

    public void Stop()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose() => Stop();

    private async Task RunLoopAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (Ticked is null)
                {
                    continue;
                }

                try
                {
                    await Ticked.Invoke();
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // One failed refresh must not end polling for the rest of the circuit.
                    _logger.LogWarning(exception, "Auto-refresh tick failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on Stop/dispose.
        }
    }
}
