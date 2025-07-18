using RinhaDeBackEnd_AOT.Dto;

namespace RinhaDeBackEnd_AOT.Dto
{
 public class CachedSummary
{
    public DateTime LastToDate { get; set; }
    public List<GatewayStatsResult> Stats { get; set; }
}
}
