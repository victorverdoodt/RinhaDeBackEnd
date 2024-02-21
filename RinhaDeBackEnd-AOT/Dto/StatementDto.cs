namespace RinhaDeBackEnd_AOT.Dto
{
    public class StatementDto
    {
        public BalanceDto Saldo { get; set; }
        public IEnumerable<TransactionDto> UltimasTransacoes { get; set; }
    }
}
