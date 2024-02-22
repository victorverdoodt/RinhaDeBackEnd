namespace RinhaDeBackEnd_AOT.Dto
{
    public class BalanceDto
    {
        public int Total {  get; set; }
        public DateTime Data_extrato { get; set; } = DateTime.UtcNow;
        public int Limite { get; set; }
    }
}
