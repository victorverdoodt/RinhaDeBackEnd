using System.Text.Json.Serialization;

namespace RinhaDeBackEnd_AOT.Domain.Models
{
    public record ExternalGatewayRequest(
        [property: JsonPropertyName("correlationId")] Guid CorrelationId,
        [property: JsonPropertyName("amount")] decimal Amount,
        [property: JsonPropertyName("requestedAt")] DateTime RequestedAt
    );
}
