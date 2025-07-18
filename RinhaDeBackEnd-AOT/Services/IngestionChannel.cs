using System.Threading.Channels;

namespace RinhaDeBackEnd_AOT.Services
{
    public class IngestionChannel
    {
        public Channel<string> Channel { get; }

        public IngestionChannel()
        {
            Channel = System.Threading.Channels.Channel.CreateUnbounded<string>(
                new UnboundedChannelOptions { SingleReader = true });
        }
    }
}
