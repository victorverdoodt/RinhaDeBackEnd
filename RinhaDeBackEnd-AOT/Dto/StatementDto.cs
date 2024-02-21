namespace RinhaDeBackEnd_AOT.Dto
{
    public class StatementDto
    {
        public BalanceDto Saldo { get; set; }
        public List<TransactionDto> UltimasTransacoes { get; set; } = new List<TransactionDto>();
    }
}
