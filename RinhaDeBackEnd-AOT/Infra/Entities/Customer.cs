namespace RinhaDeBackEnd_AOT.Infra.Entities
{
    public class Customer
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public int Balance { get; set; }
        public int Limit { get; set; }
        public string? LastStatement { get; set; } = null;
    }
}
