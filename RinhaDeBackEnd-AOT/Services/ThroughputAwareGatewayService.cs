using RinhaDeBackEnd_AOT.Dto;
using RinhaDeBackEnd_AOT.Infra.Interfaces;
using StackExchange.Redis;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace RinhaDeBackEnd_AOT.Services
{
    public class ThroughputAwareGatewayService : IPaymentGatewayService
    {
        // --- CONSTANTES ---
        private const double RequiredRpsForTarget = 14980.0 / 90.0; // ~166.44 RPS
        private const int HardLatencyLimitMs = 1000; // Limite máximo de segurança
        private const int RpsSlidingWindowSeconds = 10;
        private static readonly TimeSpan DegradedStateDuration = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan RecoveryCheckInterval = TimeSpan.FromSeconds(5);

        // --- CHAVES DO REDIS ---
        private const string RedisPrimaryIsDegradedKey = "gateway:primary:is_degraded";
        private const string RedisPrimaryLatencyKey = "gateway:latency:primary";
        private const string RedisPrimaryRequestsKey = "gateway:primary:requests_zset"; // Para calcular RPS
        private const string RedisHealthCheckLockKey = "gateway:health_check:lock";

        // --- CAMPOS ---
        private readonly HttpClient _httpClient;
        private readonly IDatabase _redis;
        private readonly ILogger<ThroughputAwareGatewayService> _logger;
        private readonly JsonSerializerOptions _jsonSerializerOptions;
        private readonly string _primaryUrl;
        private readonly string _fallbackUrl;
        private readonly string _healthUrlPrimary;
        private readonly string _healthUrlFallback;

        public ThroughputAwareGatewayService(HttpClient httpClient, IConfiguration config, IConnectionMultiplexer redisMux, ILogger<ThroughputAwareGatewayService> logger)
        {
            _httpClient = httpClient; _redis = redisMux.GetDatabase(); _logger = logger;
            _primaryUrl = config["Gateways:Primary"] + "/payments";
            _fallbackUrl = config["Gateways:Fallback"] + "/payments";
            _healthUrlPrimary = config["Gateways:Primary"] + "/payments/service-health";
            _healthUrlFallback = config["Gateways:Fallback"] + "/payments/service-health";
            _jsonSerializerOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, TypeInfoResolver = AppJsonSerializerContext.Default };
        }

        public async Task<int> ProcessAsync(ExternalGatewayRequest request, CancellationToken cancellationToken = default)
        {
            bool usePrimary = true;
            string reason = "default";

            // 1. O primário já está marcado como degradado?
            if (await _redis.KeyExistsAsync(RedisPrimaryIsDegradedKey))
            {
                reason = "primary_is_marked_degraded";
                usePrimary = false;
            }
            else
            {
                // 2. O ritmo ou a latência estão indicando um problema?
                (bool isSlow, string slowReason) = await IsPrimarySlowBasedOnPacingAndLatencyAsync();
                if (isSlow)
                {
                    reason = slowReason;
                    usePrimary = false;
                }
            }

            // 3. Se a decisão for usar o fallback, vamos verificar se isso ainda é a melhor escolha.
            if (!usePrimary)
            {
                bool primaryRecovered = await PerformComparativeHealthCheckAsync(cancellationToken);
                if (primaryRecovered)
                {
                    _logger.LogWarning("RECUPERAÇÃO: O primário se recuperou e é a melhor opção. Usando o primário.");
                    usePrimary = true;
                }
            }

            // 4. Executa a Ação Final
            if (usePrimary)
            {
                try { return await ProcessWithPrimaryAsync(request, cancellationToken); }
                catch (Exception primaryEx)
                {
                    await MarkPrimaryAsDegradedAsync();
                    return await ProcessWithFallbackAsync(request, "primary_failover", cancellationToken);
                }
            }
            else
            {
                return await ProcessWithFallbackAsync(request, reason, cancellationToken);
            }
        }

        private async Task<(bool isSlow, string reason)> IsPrimarySlowBasedOnPacingAndLatencyAsync()
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var windowStart = now - (RpsSlidingWindowSeconds * 1000);

            var tran = _redis.CreateTransaction();
            var latencyTask = tran.ListRangeAsync(RedisPrimaryLatencyKey);
            tran.SortedSetRemoveRangeByScoreAsync(RedisPrimaryRequestsKey, 0, windowStart);
            var rpsTask = tran.SortedSetLengthAsync(RedisPrimaryRequestsKey);
            await tran.ExecuteAsync();

            var latencies = await latencyTask;
            if (latencies.Length > 20)
            {
                var avgLatency = latencies.Select(l => (double)l).Average();
                if (avgLatency > HardLatencyLimitMs)
                {
                    _logger.LogWarning("LENTIDÃO (LATÊNCIA): Média de {AvgLatency:F0}ms excedeu o limite de {Limit}ms", avgLatency, HardLatencyLimitMs);
                    return (true, "hard_latency_limit_exceeded");
                }
            }

            var requestCountInWindow = await rpsTask;
            if (requestCountInWindow > 20) // Apenas avalia RPS com dados suficientes
            {
                var currentRps = requestCountInWindow / (double)RpsSlidingWindowSeconds;
                if (currentRps < RequiredRpsForTarget)
                {
                    _logger.LogWarning("LENTIDÃO (RITMO): RPS atual de {CurrentRps:F1} está abaixo do necessário de {RequiredRps:F1}", currentRps, RequiredRpsForTarget);
                    return (true, "pacing_below_target");
                }
            }

            return (false, "healthy");
        }

        private async Task<bool> PerformComparativeHealthCheckAsync(CancellationToken cancellationToken)
        {
            // Bloqueio distribuído para garantir que apenas um worker faça a verificação
            bool gotLock = await _redis.StringSetAsync(RedisHealthCheckLockKey, "1", RecoveryCheckInterval, When.NotExists);
            if (!gotLock) return false; // Outro worker está verificando, então não faça nada.

            _logger.LogInformation("Worker obteve o bloqueio para a verificação comparativa de saúde.");
            try
            {
                var primaryHealthTask = GetHealthFromEndpointAsync(_healthUrlPrimary, cancellationToken);
                var fallbackHealthTask = GetHealthFromEndpointAsync(_healthUrlFallback, cancellationToken);
                await Task.WhenAll(primaryHealthTask, fallbackHealthTask);

                var primaryHealth = primaryHealthTask.Result;
                var fallbackHealth = fallbackHealthTask.Result;

                if (primaryHealth is null || primaryHealth.Failing || fallbackHealth is null)
                {
                    _logger.LogWarning("Não foi possível obter um ou ambos os status de saúde. Nenhuma ação será tomada.");
                    return false; // Não foi possível decidir, então não há recuperação.
                }

                _logger.LogInformation("Health Check Comparativo -> Primário: {PrimaryTime}ms | Fallback: {FallbackTime}ms",
                    primaryHealth.MinResponseTime, fallbackHealth.MinResponseTime);

                if (primaryHealth.MinResponseTime <= fallbackHealth.MinResponseTime)
                {
                    _logger.LogWarning("RECUPERAÇÃO CONCLUÍDA: Primário é a melhor opção. Removendo o estado DEGRADED.");
                    await _redis.KeyDeleteAsync(RedisPrimaryIsDegradedKey);
                    return true; // O primário se recuperou!
                }
                else
                {
                    _logger.LogWarning("MANUTENÇÃO NO FALLBACK: Fallback ({FallbackTime}ms) ainda é mais rápido que o Primário ({PrimaryTime}ms).", fallbackHealth.MinResponseTime, primaryHealth.MinResponseTime);
                    // Garante que o estado degradado continue ativo.
                    await MarkPrimaryAsDegradedAsync();
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exceção durante a verificação comparativa de saúde.");
                return false;
            }
        }

        private async Task<HealthResponse?> GetHealthFromEndpointAsync(string url, CancellationToken cancellationToken)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
                return await _httpClient.GetFromJsonAsync<HealthResponse>(url, _jsonSerializerOptions, linkedCts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao obter health do endpoint {Url}", url);
                return null;
            }
        }

        private async Task<int> ProcessWithPrimaryAsync(ExternalGatewayRequest request, CancellationToken cancellationToken)
        {
            var content = new StringContent(JsonSerializer.Serialize(request, _jsonSerializerOptions), Encoding.UTF8, "application/json");
            var stopwatch = Stopwatch.StartNew();

            var response = await _httpClient.PostAsync(_primaryUrl, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            stopwatch.Stop();
            var latency = stopwatch.ElapsedMilliseconds;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // ATUALIZAÇÃO: Agora rastreia latência E o evento de requisição para o RPS
            var tran = _redis.CreateTransaction();
            // Latency tracking
            tran.ListLeftPushAsync(RedisPrimaryLatencyKey, latency);
            tran.ListTrimAsync(RedisPrimaryLatencyKey, 0, 99);
            // RPS tracking
            tran.SortedSetAddAsync(RedisPrimaryRequestsKey, now.ToString(), now);
            await tran.ExecuteAsync();

            _logger.LogInformation("Sucesso no Primário em {ElapsedMilliseconds}ms.", latency);
            return 0;
        }

        private async Task<int> ProcessWithFallbackAsync(ExternalGatewayRequest request, string reason, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Enviando para Fallback. Motivo: {Reason}", reason);
            try
            {
                var content = new StringContent(JsonSerializer.Serialize(request, _jsonSerializerOptions), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(_fallbackUrl, content, cancellationToken);
                response.EnsureSuccessStatusCode();
                _logger.LogInformation("Sucesso no Fallback.");
                return 1;
            }
            catch (Exception fallbackEx)
            {
                _logger.LogCritical(fallbackEx, "FALHA CRÍTICA: Fallback também falhou! Requisição perdida.");
                throw;
            }
        }

        private Task MarkPrimaryAsDegradedAsync()
        {
            return _redis.StringSetAsync(RedisPrimaryIsDegradedKey, "1", DegradedStateDuration);
        }
    }
}