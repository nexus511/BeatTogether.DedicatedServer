using System.Net;

namespace BeatTogether.DedicatedServer.Kernel.Abstractions
{
    public interface IRelaySocket
    {
        public abstract IPEndPoint AddRelayFor(IPEndPoint source, IPEndPoint target);
        public abstract bool Stop();
    }
}
