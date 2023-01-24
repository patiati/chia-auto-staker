using Newtonsoft.Json;

namespace ChiaAutoStaker
{
    internal class Settings
    {
        public int? IntervalSeconds { get; set; }

        public string? LogFile { get; set; }

        public Fork[]? Forks { get; set; }
    }

    internal class Fork
    {
        [JsonIgnore]
        public bool Enabled { get; set; } = true;

        public string? Name { get; set; }

        public string? Folder { get; set; }

        public string? Executable { get; set; }

        public string? StakingAddress { get; set; }

        [JsonIgnore]
        public string? ExecutablePath { get; set; }

        public string? StakingAddressCommand { get; set; }

        public string? StakingAddressRegex { get; set; }

        public string? WalletCommand { get; set; }

        public string? WalletRegex { get; set; }

        public string? StakingCommand { get; set; }

        public string? AllTheBlocksForkPath { get; set; }

        public byte? BalanceDecimalPlaces { get; set; }
    }
}
