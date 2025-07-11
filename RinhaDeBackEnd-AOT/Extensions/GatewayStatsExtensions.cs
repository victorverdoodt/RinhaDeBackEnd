using RinhaDeBackEnd_AOT.Dto;

namespace RinhaDeBackEnd_AOT.Extensions
{
    public static class GatewayStatsExtensions
    {
        public static GatewayStats ToRecord(this GatewayStatsResult dto)
        => new(dto.TotalRequests, dto.TotalAmount);
    }
}
