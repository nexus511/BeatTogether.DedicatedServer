using System.Net;
using System.Linq;
using System.Collections.Generic;
using BeatTogether.DedicatedServer.Kernel.Abstractions;
using BeatTogether.DedicatedServer.Kernel.Abstractions.Providers;
using BeatTogether.DedicatedServer.Kernel.Configuration;

namespace BeatTogether.DedicatedServer.Kernel.Implementations.Factories
{
    public class RelayServerFactory : IRelayServerFactory
    {
        private readonly RelayServerConfiguration _configuration;
        private LinkedList<IRelaySocket> _threads = new LinkedList<IRelaySocket>();
        private int _nextRelayThread = 0;

        public RelayServerFactory(RelayServerConfiguration configuration)
        {
            _configuration = configuration;
            IPAddress ipAddress = IPAddress.Parse(configuration.BindAddress);
            int workers = configuration.WorkersCount;
            int threads = configuration.ThreadCount;
            int timeout = configuration.InactivityTimeout;

            for (int i = 0; i < threads; ++i)
            {
                int startPort = (i * workers);
                _threads.AddLast(new RelaySocket(ipAddress, startPort, workers, timeout));
            }
        }

        public IPEndPoint GetRelayServer(IPEndPoint sourceEndPoint, IPEndPoint targetEndPoint)
        {
            int count = _threads.Count;
            for (int i = 0; i < _threads.Count; ++i)
            {
                IPEndPoint endPoint = _threads.ElementAt<IRelaySocket>(i)
                    .AddRelayFor(sourceEndPoint, targetEndPoint);
                _nextRelayThread = (_nextRelayThread + 1) % count;
                if (endPoint != null)
                {
                    return endPoint;
                }
            }
            return null;
        }
    }
}
