using RinhaDeBackEnd_AOT.Domain.Models;

namespace RinhaDeBackEnd_AOT.Domain.Interfaces
{
    public interface IPaymentGatewayService
    {
        Task<int> ProcessAsync(ExternalGatewayRequest request, CancellationToken cancellationToken = default);
    }
}
