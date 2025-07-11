using RinhaDeBackEnd_AOT.Dto;

namespace RinhaDeBackEnd_AOT.Infra.Interfaces
{
    public interface IPaymentGatewayService
    {
        Task<int> ProcessAsync(ExternalGatewayRequest request, CancellationToken cancellationToken = default);
    }
}
