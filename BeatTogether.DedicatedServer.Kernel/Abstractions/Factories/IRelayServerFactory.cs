using System.Net;

namespace BeatTogether.DedicatedServer.Kernel.Abstractions.Providers
{
    public interface IRelayServerFactory
    {
        IPEndPoint GetRelayServer(IPEndPoint sourceEndPoint, IPEndPoint targetEndPoint);
    }
}
