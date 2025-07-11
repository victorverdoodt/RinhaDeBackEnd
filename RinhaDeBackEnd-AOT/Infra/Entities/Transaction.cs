namespace RinhaDeBackEnd_AOT.Infra.Entities
{
    public class Transaction
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public decimal Amout { get; set; }
        public DateTime requestedAt { get; set; } = DateTime.UtcNow;
        public int Gateway { get; set; }
    }
}
