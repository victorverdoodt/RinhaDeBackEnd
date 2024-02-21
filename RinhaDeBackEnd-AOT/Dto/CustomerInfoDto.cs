namespace RinhaDeBackEnd_AOT.Dto
{
    public class CustomerInfoDto
    {
        public int Id { get; set; }
        public int Balance { get; set; }
        public int Limit { get; set; }
        public string? LastStatement { get; set; }
    }
}
