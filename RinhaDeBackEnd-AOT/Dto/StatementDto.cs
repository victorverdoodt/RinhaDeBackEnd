namespace RinhaDeBackEnd_AOT.Dto
{
    public class StatementDto
    {
        public BalanceDto Saldo { get; set; } = new BalanceDto();
        public List<TransactionDto> Ultimas_transacoes { get; set; } = new List<TransactionDto>();
    }
}
