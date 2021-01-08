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
        private readonly ILogger _logger;
        private readonly IPAddress _address;
        private readonly int _startPort;
        private readonly int _endPort;
        private readonly int _selectTimeout;
        private readonly long _peerTimeout;
        private readonly Thread _thread;
        private readonly LinkedList<Socket> _sockets;

        private readonly Dictionary<IPEndPoint, RelayPair> _mappings;
        private readonly LinkedList<RelayPair> _relayPairs;

        private Stopwatch _stopwatch;

        private bool _active;

        public RelaySocket(IPAddress address, int startPort, int workers)
        {
            _logger = Log.ForContext<RelayServer>();
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
                Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _sockets.AddLast(socket);
                socket.Bind(new IPEndPoint(address, port));
            }

            _thread.Start();
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
                List<Socket> readSockets = _sockets.ToList();
                Socket.Select(readSockets, null, null, _selectTimeout);

                foreach (Socket socket in readSockets)
                {
                    RelayFromSocket(socket);
                }

                long timestamp = CurrentTimestamp();
                if (timestamp > nextTimeoutCheck)
                {
                    CheckTimeouts(timestamp);
                    nextTimeoutCheck = CurrentTimestamp() + _peerTimeout;
                }
            }
        }

        private void RelayFromSocket(Socket socket)
        {
            byte[] buffer = new byte[socket.Available];
            IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
            EndPoint endpoint = (EndPoint)sender;

            int len = socket.ReceiveFrom(buffer, 0, socket.Available, SocketFlags.None, ref endpoint);
            if (!_mappings.ContainsKey(sender))
            {
                _logger.Verbose("Deny relay attempt from invalid peer {sender}", sender);
                return;
            }

            RelayPair pair = _mappings[sender];
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

        private void AddRelay(IPEndPoint source, IPEndPoint target, int port)
        {
            _logger.Information($"Adding peers {source} <-> {target} to port {port} ");

            RelayPair pair = new RelayPair {
                Source = source,
                Target = target,
                LastActive = CurrentTimestamp(),
                Port = port
            };

            _mappings[source] = pair;
            _mappings[target] = pair;
            _relayPairs.AddLast(pair);
        }

        private long CurrentTimestamp()
        {
            return _stopwatch.ElapsedMilliseconds / 1000;
        }

        private void CheckTimeouts(long timestamp)
        {
            // can be optimized by using a priority queue instead of iterating all
            var node = _relayPairs.First;
            while (node != null)
            {
                var next = node.Next;
                if (node.Value.LastActive + _peerTimeout < timestamp)
                {
                    _logger.Information("Removing peers {0} <-> {1} from port {2} ",
                        node.Value.Source,
                        node.Value.Target,
                        node.Value.Port
                    );
                    _mappings.Remove(node.Value.Source);
                    _mappings.Remove(node.Value.Target);
                    _relayPairs.Remove(node);
                }
                node = next;
            }
        }
    }
}
