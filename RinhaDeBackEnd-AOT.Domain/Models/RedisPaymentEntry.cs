using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace RinhaDeBackEnd_AOT.Domain.Models
{
    public class RedisPaymentEntry
    {
        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        [JsonPropertyName("processor")]
        public string Processor { get; set; } = string.Empty;

        [JsonPropertyName("requestedAt")]
        public DateTime RequestedAt { get; set; }
    }
}
