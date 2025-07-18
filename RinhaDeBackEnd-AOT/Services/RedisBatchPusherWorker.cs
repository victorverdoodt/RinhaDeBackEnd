using StackExchange.Redis;

namespace RinhaDeBackEnd_AOT.Services
{
    public class RedisBatchPusherWorker : BackgroundService
    {
        private readonly ILogger<RedisBatchPusherWorker> _logger;
        private readonly IngestionChannel _ingestionChannel;
        private readonly IDatabase _redisDb;
        private readonly int _batchSize;
        private readonly TimeSpan _maxDelay;
        private const string QueueName = "payments:queue";

        public RedisBatchPusherWorker(
            ILogger<RedisBatchPusherWorker> logger,
            IConnectionMultiplexer redis,
            IngestionChannel ingestionChannel,
            IConfiguration configuration)
        {
            _logger = logger;
            _ingestionChannel = ingestionChannel;
            _redisDb = redis.GetDatabase();
            _batchSize = configuration.GetValue("RedisPusher:BatchSize", 1000); // Lotes de 1000 por padrão
            _maxDelay = TimeSpan.FromMilliseconds(configuration.GetValue("RedisPusher:MaxDelayMs", 100)); // Envia a cada 100ms no máximo
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Redis Batch Pusher iniciado. BatchSize={size}, MaxDelay={delay}", _batchSize, _maxDelay);

            var batch = new List<RedisValue>(_batchSize);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Espera pelo primeiro item com um timeout para garantir o flush
                    var payload = await _ingestionChannel.Channel.Reader.ReadAsync(stoppingToken);
                    batch.Add(payload);

                    // Tenta coletar mais itens até o tamanho do lote, sem esperar muito
                    while (batch.Count < _batchSize && _ingestionChannel.Channel.Reader.TryRead(out var nextPayload))
                    {
                        batch.Add(nextPayload);
                    }

                    if (batch.Any())
                    {
                        // Envia o lote inteiro em um único comando
                        await _redisDb.ListRightPushAsync(QueueName, batch.ToArray(), CommandFlags.FireAndForget);
                        _logger.LogDebug("{Count} itens enviados para a fila do Redis.", batch.Count);
                        batch.Clear();
                    }
                }
                catch (OperationCanceledException)
                {
                    // Operação normal durante o shutdown
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro no Redis Batch Pusher Worker.");
                    // Pausa para evitar loop de erro rápido
                    await Task.Delay(1000, stoppingToken);
                }
            }
        }
    }
}
