namespace RinhaDeBackEnd_AOT.Infra.Entities
{
    public class Transaction
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public int Value { get; set; }
        public char Type { get; set; }
        public string Description { get; set; }
        public DateTime TransactionDate { get; set; }

        public Customer Customer { get; set; }
    }
}
