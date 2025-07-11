namespace RinhaDeBackEnd_AOT.Dto
{
    public record QueuedPaymentRequest(
        Guid CorrelationId,
        decimal Amount,
        DateTime RequestedAt
    );
}
