namespace SmartXApi.Services
{
    public class StreamWorker : BackgroundService
    {
        private readonly ITelemetryStreamProcessor _processor;
        private readonly IConfiguration _configuration;
        private readonly ILogger<StreamWorker> _logger;

        public StreamWorker(ITelemetryStreamProcessor processor, IConfiguration configuration, ILogger<StreamWorker> logger)
        {
            _processor = processor;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await _processor.ReloadAllAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Initial device registry load failed.");
            }

            var timeout = TimeSpan.FromSeconds(Math.Max(5, _configuration.GetValue("Stream:DisconnectAfterSeconds", 30)));
            var tick = 0;

            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    try
                    {
                        _processor.ProcessPending(500);

                        tick++;
                        if (tick % 20 == 0)
                        {
                            _processor.CheckDisconnected(timeout);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Telemetry stream processing cycle failed.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
