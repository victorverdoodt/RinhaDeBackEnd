using System.ComponentModel.DataAnnotations;

namespace RinhaDeBackEnd.Domain.Entities
{
    public class Customer
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public int Balance { get; set; }
        public int Limit { get; set; }
        public string? LastTransactions { get; set; } = null;

        [ConcurrencyCheck]
        public uint xmin { get; set; }
    }
}
