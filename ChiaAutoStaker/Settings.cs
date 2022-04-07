namespace ChiaAutoStaker
{
    internal class Settings
    {
        public int IntervalSeconds { get; set; }

        public string LogFile { get; set; }

        public Fork[] Forks { get; set; }
    }

    internal class Fork
    {
        public bool Enabled { get; set; }

        public string Name { get; set; }

        public string Folder { get; set; }

        public string Executable { get; set; }

        public string StakingAddress { get; set; }

        public string ExecutablePath { get; set; }
    }
}
