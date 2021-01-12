namespace BeatTogether.DedicatedServer.Kernel.Configuration
{
    public class RelayServerConfiguration
    {
        public int ThreadCount { get; set; } = 8;        //< number of threads to be started for socket handling
        public int WorkersCount { get; set; } = 16;      //< number of sockets to be opened per thread
        public int InactivityTimeout { get; set; } = 60000;
        public string BindAddress { get; set; } = "0.0.0.0";
        public int BasePort { get; set; } = 30000;
    }
}
