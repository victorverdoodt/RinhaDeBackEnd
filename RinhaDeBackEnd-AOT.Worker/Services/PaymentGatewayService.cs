using Polly;
using Polly.Registry;
using RinhaDeBackEnd_AOT.Domain.Interfaces;
using RinhaDeBackEnd_AOT.Domain.Models;
using System.Net.Http.Json;


namespace RinhaDeBackEnd_AOT.Worker.Services
{
    public class PaymentGatewayService : IPaymentGatewayService
    {
        private readonly HttpClient _httpClient;
        private readonly string _primaryUrl;
        private readonly ResiliencePipeline<int> _pipeline;

        private static readonly ResiliencePropertyKey<ExternalGatewayRequest> RequestKey = new("gateway_request");


        public PaymentGatewayService(
            HttpClient httpClient,
            IConfiguration config,
            ResiliencePipelineProvider<string> pipelineProvider)
        {
            _httpClient = httpClient;
            _primaryUrl = config["Gateways:Primary"] + "/payments";
            _pipeline = pipelineProvider.GetPipeline<int>("gateway-pipeline");
        }

        public async Task<int> ProcessAsync(ExternalGatewayRequest request, CancellationToken cancellationToken = default)
        {
            var context = ResilienceContextPool.Shared.Get(cancellationToken);

            context.Properties.Set(RequestKey, request);

            try
            {
                return await _pipeline.ExecuteAsync(
                    async ctx =>
                    {
                        using var jsonContent = JsonContent.Create(request);

                        var response = await _httpClient.PostAsync(_primaryUrl, jsonContent, ctx.CancellationToken);
                        response.EnsureSuccessStatusCode();

                        return 0;
                    },
                    context);
            }
            finally
            {
                ResilienceContextPool.Shared.Return(context);
            }
        }
    }
}
