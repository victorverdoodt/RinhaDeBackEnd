namespace RinhaDeBackEnd_AOT.Domain.Models
{
    public record QueuedPaymentRequest(
        Guid CorrelationId,
        decimal Amount,
        DateTime RequestedAt
    );
}
