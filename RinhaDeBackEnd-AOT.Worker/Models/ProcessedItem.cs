using RinhaDeBackEnd_AOT.Domain.Models;

namespace RinhaDeBackEnd_AOT.Worker.Models
{
    public class ProcessedItem
    {
        public Transaction Transaction { get; init; }
        public DateTime GatewayCompletionTime { get; init; }
    }
}
