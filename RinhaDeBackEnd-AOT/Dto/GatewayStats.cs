using System.Text.Json.Serialization;

namespace RinhaDeBackEnd_AOT.Dto
{
    public record GatewayStats(
       [property: JsonPropertyName("totalRequests")] long TotalRequests,
       [property: JsonPropertyName("totalAmount")] decimal TotalAmount
    );
}
