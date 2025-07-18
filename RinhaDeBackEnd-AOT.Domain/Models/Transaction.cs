namespace RinhaDeBackEnd_AOT.Domain.Models
{
    public class Transaction
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public decimal Amount { get; set; }
        public DateTime requestedAt { get; set; } = DateTime.UtcNow;
        public int Gateway { get; set; }
        public int Status { get; set; } = 0;
    }
}
