using System.Text.Json.Serialization;

namespace RinhaDeBackEnd_AOT.Dto
{
    public record ApiPaymentRequest(
        [property: JsonPropertyName("correlationId")] Guid CorrelationId,
        [property: JsonPropertyName("amount")] decimal Amount
    );
}
