using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

using System.Net;
using System.Net.Sockets;
using System.Threading;

using Serilog;

using BeatTogether.DedicatedServer.Kernel.Abstractions;

namespace BeatTogether.DedicatedServer.Kernel.Implementations
{
    class RelaySocket : IRelaySocket
    {
        public class RelayPair
        {
            public IPEndPoint Source { get; set; }
            public IPEndPoint Target { get; set; }
            public long LastActive { get; set; }
        }
        private class UdpRelaySocket : Socket
        {
            public UdpRelaySocket(IPAddress address, int port)
                : base(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                this.Bind(new IPEndPoint(address, port));
                Port = port;
                Mappings = new Dictionary<IPEndPoint, RelayPair>();
                Pairs = new LinkedList<RelayPair>();
                Lock = new Mutex();
            }

            public int Port { get; set; }
            public Dictionary<IPEndPoint, RelayPair> Mappings { get; }
            public LinkedList<RelayPair> Pairs { get; }
            public Mutex Lock { get; }
        }

        private readonly ILogger _logger;
        private readonly IPAddress _address;
        private readonly int _startPort;
        private readonly int _endPort;
        private readonly int _selectTimeout;
        private readonly long _peerTimeout;

        private readonly LinkedList<UdpRelaySocket> _sockets = new LinkedList<UdpRelaySocket>();
        private int _nextSocketIndex = 0;

        private readonly Thread _thread;

        private Stopwatch _stopwatch;
        
        private bool _active;

        public RelaySocket(IPAddress address, int startPort, int workers)
        {
            _logger = Log.ForContext<RelaySocket>();

            _address = address;
            _startPort = startPort;
            _endPort = startPort + workers - 1;
            _active = true;
            _selectTimeout = 1000;
            _peerTimeout = 60;

            _thread = new Thread(new ThreadStart(SocketThread));
            _thread.IsBackground = true;

            _logger.Information("Opening sockets {_startPort} to {_endPort} " +
                $" with handler {_thread}"
            );

            for (int port = startPort; port <= _endPort; ++startPort)
            {
                _sockets.AddLast(new UdpRelaySocket(address, port));
            }

            _thread.Start();
        }

        public IPEndPoint AddRelayFor(IPEndPoint source, IPEndPoint target)
        {
            UdpRelaySocket socket = FindPossiblePort(source, target);
            if (socket == null)
            {
                return null;
            }
            int port = AddRelay(source, target, socket);
            return new IPEndPoint(_address, port);
        }

        private void SocketThread()
        {
            _logger.Verbose("Start listening on {_startPort} to {_endPort} " +
                $" with handler {_thread}"
            );

            _stopwatch = Stopwatch.StartNew();
            long nextTimeoutCheck = CurrentTimestamp() + _peerTimeout;

            while (_active)
            {
                List<Socket> readSockets = _sockets.ToList<Socket>();
                Socket.Select(readSockets, null, null, _selectTimeout);

                foreach (Socket socket in readSockets)
                {
                    RelayFromSocket((UdpRelaySocket) socket);
                }

                long timestamp = CurrentTimestamp();
                if (timestamp > nextTimeoutCheck)
                {
                    CheckTimeouts(timestamp);
                    nextTimeoutCheck = CurrentTimestamp() + _peerTimeout;
                }
            }
        }
        private void RelayFromSocket(UdpRelaySocket socket)
        {
            byte[] buffer = new byte[socket.Available];
            IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
            EndPoint endpoint = (EndPoint)sender;

            int len = socket.ReceiveFrom(buffer, 0, socket.Available, SocketFlags.None, ref endpoint);

            socket.Lock.WaitOne();
            try
            {
                if (socket.Mappings.ContainsKey(sender))
                {
                    _logger.Verbose("Deny relay attempt from invalid peer {sender}", sender);
                    return;
                }

                RelayPair pair = socket.Mappings[sender];
                pair.LastActive = CurrentTimestamp();
                IPEndPoint target = pair.Source;
                if (target.Equals(sender))
                {
                    target = pair.Target;
                }

                if (socket.SendTo(buffer, target) != len)
                {
                    _logger.Warning("Not all bytes delivered from {sender} to {target}", sender, target);
                }
            }
            finally
            {
                socket.Lock.ReleaseMutex();
            }
        }
        private UdpRelaySocket FindPossiblePort(IPEndPoint source, IPEndPoint target)
        {
            int count = _sockets.Count();
            for (int i = 0; i < count; ++i)
            {
                UdpRelaySocket socket = _sockets.ElementAt(_nextSocketIndex);
                socket.Lock.WaitOne();
                try
                {
                    if (socket.Mappings.ContainsKey(source) || socket.Mappings.ContainsKey(target))
                    {
                        continue;
                    }
                    else
                    {
                        return socket;
                    }
                }
                finally
                {
                    socket.Lock.ReleaseMutex();
                    _nextSocketIndex = (_nextSocketIndex + 1) % count;
                }
            }
            return null;
        }
        private int AddRelay(IPEndPoint source, IPEndPoint target, UdpRelaySocket socket)
        {
            _logger.Information($"Adding peers {source} <-> {target} to port {socket.Port} ");

            socket.Lock.WaitOne();
            try
            { 
                RelayPair pair = new RelayPair {
                    Source = source,
                    Target = target,
                    LastActive = CurrentTimestamp()
                };

                socket.Mappings[source] = pair;
                socket.Mappings[target] = pair;
                socket.Pairs.AddLast(pair);

                return socket.Port;
            }
            finally
            {
                socket.Lock.ReleaseMutex();
            }
        }

        private long CurrentTimestamp()
        {
            return _stopwatch.ElapsedMilliseconds / 1000;
        }
        private void CheckTimeouts(long timestamp)
        {
            foreach (UdpRelaySocket socket in _sockets)
            {
                CheckTimeouts(timestamp, socket);
            }
        }
        private void CheckTimeouts(long timestamp, UdpRelaySocket socket)
        {
            // can be optimized by using a priority queue instead of iterating all
            socket.Lock.WaitOne();
            try
            {
                var node = socket.Pairs.First;
                while (node != null)
                {
                    var next = node.Next;
                    if (node.Value.LastActive + _peerTimeout < timestamp)
                    {
                        _logger.Information("Removing peers {0} <-> {1} from port {2} ",
                            node.Value.Source,
                            node.Value.Target,
                            socket.Port
                        );
                        socket.Mappings.Remove(node.Value.Source);
                        socket.Mappings.Remove(node.Value.Target);
                        socket.Pairs.Remove(node);
                    }
                    node = next;
                }
            }
            finally
            {
                socket.Lock.ReleaseMutex();
            }
        }
    }
}
