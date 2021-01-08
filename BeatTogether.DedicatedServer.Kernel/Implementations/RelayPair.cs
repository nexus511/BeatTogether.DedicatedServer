using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Net;

namespace BeatTogether.DedicatedServer.Kernel.Implementations
{
    class RelayPair
    {
        public IPEndPoint Source { get; set; }
        public IPEndPoint Target { get; set; }
        public long LastActive { get; set; }
        public int Port { get; set; }
    }
}
