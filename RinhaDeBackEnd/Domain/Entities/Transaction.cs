namespace RinhaDeBackEnd.Domain.Entities
{
    public class Transaction
    {
        public int Value { get; set; }
        public char Type { get; set; }
        public string Description { get; set; } = null!;
        public DateTime TransactionDate { get; set; } = DateTime.UtcNow;
    }
}
