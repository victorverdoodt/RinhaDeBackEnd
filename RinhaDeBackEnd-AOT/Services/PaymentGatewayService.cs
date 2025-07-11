using Polly;
using Polly.Timeout;
using RinhaDeBackEnd_AOT.Dto;
using RinhaDeBackEnd_AOT.Infra.Interfaces;
using System.Text;
using System.Text.Json;

namespace RinhaDeBackEnd_AOT.Services
{
    public class PaymentGatewayService : IPaymentGatewayService
    {
        private readonly HttpClient _httpClient;
        private readonly string _primaryUrl;
        private readonly string _fallbackUrl;

        private readonly IAsyncPolicy<int> _policy;
        private ExternalGatewayRequest? _lastRequest;
        private readonly JsonSerializerOptions _jsonSerializerOptions;

        public PaymentGatewayService(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;

            _primaryUrl = config["Gateways:Primary"] + "/payments";
            _fallbackUrl = config["Gateways:Fallback"] + "/payments";

            _jsonSerializerOptions = new JsonSerializerOptions
            {
                TypeInfoResolver = AppJsonSerializerContext.Default
            };

            var timeoutPolicy = Policy.TimeoutAsync<int>(2, TimeoutStrategy.Pessimistic);

            // Retry 1 vez antes de fallback
            var retryPolicy = Policy
                .Handle<Exception>()
                .Or<TimeoutRejectedException>()
                .RetryAsync(1);

            var fallbackPolicy = Policy<int>
                .Handle<Exception>()
                .Or<TimeoutRejectedException>()
                .FallbackAsync(
                    fallbackAction: async (ct) =>
                    {
                        var content = new StringContent(JsonSerializer.Serialize(_lastRequest, _jsonSerializerOptions), Encoding.UTF8, "application/json");
                        var response = await _httpClient.PostAsync(_fallbackUrl, content, ct);
                        response.EnsureSuccessStatusCode();
                        return 1;
                    });

            _policy = Policy.WrapAsync(fallbackPolicy, timeoutPolicy);
        }

        public async Task<int> ProcessAsync(ExternalGatewayRequest request, CancellationToken cancellationToken = default)
        {
            _lastRequest = request;

            return await _policy.ExecuteAsync(async ct =>
            {
                var content = new StringContent(JsonSerializer.Serialize(request, _jsonSerializerOptions), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(_primaryUrl, content, ct);
                response.EnsureSuccessStatusCode();
                return 0;
            }, cancellationToken);
        }
    }
}
