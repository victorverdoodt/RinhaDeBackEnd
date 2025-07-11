using System.Text.Json.Serialization;

namespace RinhaDeBackEnd_AOT.Dto
{
    public record ExternalGatewayRequest(
        [property: JsonPropertyName("correlationId")] Guid CorrelationId,
        [property: JsonPropertyName("amount")] decimal Amount,
        [property: JsonPropertyName("requestedAt")] DateTime RequestedAt
    );
}
