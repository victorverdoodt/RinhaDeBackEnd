using System.Text.Json.Serialization;

namespace RinhaDeBackEnd_AOT.Dto
{
    public record MetricsResponse(
       [property: JsonPropertyName("default")] GatewayStats Default,
       [property: JsonPropertyName("fallback")] GatewayStats Fallback
   );
}
