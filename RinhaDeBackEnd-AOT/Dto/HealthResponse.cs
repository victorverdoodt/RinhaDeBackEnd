using System.Text.Json.Serialization;

namespace RinhaDeBackEnd_AOT.Dto
{
    public record HealthResponse(
        [property: JsonPropertyName("failing")] bool Failing,
        [property: JsonPropertyName("minResponseTime")] int MinResponseTime
    );
}
