using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ChiaAutoStaker
{
    internal class Worker
    {
        private static readonly Regex SpendableRegex = new(@"-Spendable: (\d*\.\d*) (\w*)");
        private static readonly Regex StakingRegex = new(@"Staking addresses:(?:\n|\r|\r\n)  (.*) \(balance: (\d*\.?\d*)\, plots: (\d*)\)");
        private static readonly Regex NetworkSpaceRegex = new(@"Estimated network space: (.*)");
        private static readonly Regex StakingFactorRegex = new(@"Estimated staking factor: (.*)");
        private static readonly Regex ExpectedTimeToWinRegex = new(@"Expected time to win: (.*)");

        private readonly IConfiguration configuration;
        private readonly Log log;

        private DateTime lastRun = DateTime.MinValue;

        public Worker(Log log, IConfiguration configuration)
        {
            this.log = log;
            this.configuration = configuration;
        }

        public void DoWork()
        {
            var settings = this.GetSettings();

            log.Write("Looking for forks ...");

            var enabledForks = settings.Forks.Where(f => f.Enabled);

            if (enabledForks.Any())
            {
                foreach (var fork in enabledForks)
                {
                    log.Write($"{fork.Name} found!", ConsoleColor.Green);
                    log.Write($"{fork.ExecutablePath}");
                }
            }
            else
            {
                log.Write("No fork found!", ConsoleColor.Red);
                return;
            }

            log.Write($"Interval: {settings.IntervalSeconds} seconds");

            while (true)
            {
                var now = DateTime.Now;
                if (lastRun.AddSeconds(settings.IntervalSeconds) <= now)
                {
                    lastRun = now;

                    foreach (var fork in enabledForks)
                    {
                        log.Write($"Checking {fork.Name} ...");
                        log.Write($"  wallet show:");
                        var wallet = GetWallet(fork);
                        log.Write($"    Spendable: {wallet.Spendable} {wallet.Symbol}");
                        log.Write($"  farm summary:");
                        var farm = GetFarm(fork);
                        log.Write($"    Balance: {farm.Balance} {wallet.Symbol}");
                        log.Write($"    Plots: {farm.Plots}");
                        log.Write($"    NetworkSpace: {farm.NetworkSpace}");
                        if (!string.IsNullOrEmpty(farm.StakingFactor))
                            log.Write($"    StakingFactor: {farm.StakingFactor}");
                        log.Write($"    ETW: {farm.ExpectedTimeToWin}");

                        if (float.Parse(wallet.Spendable, CultureInfo.GetCultureInfo("en-US").NumberFormat) > 0)
                        {
                            log.Write("  Staking ...");
                            if (SendStake(fork, wallet, farm))
                            {
                                log.Write("    Succeded!", ConsoleColor.Green);
                            }
                            else
                            {
                                log.Write("    Failed!", ConsoleColor.Red);
                            }
                        }
                    }
                }
                else
                {
                    Thread.Sleep(1000);
                }
            }
        }

        private Settings GetSettings()
        {
            var settings = configuration.GetRequiredSection("Settings").Get<Settings>();

            var localAppData = configuration["LOCALAPPDATA"];

            foreach (var fork in settings.Forks.Where(f => f.Enabled))
            {
                try
                {
                    var appFolder = Directory.GetDirectories($"{localAppData}\\{fork.Folder}", "app-*").FirstOrDefault();

                    if (!string.IsNullOrEmpty(appFolder))
                    {
                        var exePath = $"{appFolder}\\resources\\app.asar.unpacked\\daemon\\{fork.Executable}";

                        if (File.Exists(exePath))
                        {
                            fork.ExecutablePath = exePath;
                        }
                        else
                        {
                            fork.Enabled = false;
                        }
                    }
                }
                catch (Exception)
                {
                    fork.Enabled = false;
                }
            }

            return settings;
        }

        private Wallet GetWallet(Fork fork)
        {
            var process = new Process {
                StartInfo = new ProcessStartInfo
                {
                    FileName = $"{fork.ExecutablePath}",
                    Arguments = "wallet show",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };           
            process.Start();
            
            var output = process.StandardOutput.ReadToEnd();

            var spendableMatch = SpendableRegex.Match(output);
            var spendable = spendableMatch.Success && spendableMatch.Groups.Count > 1 ? spendableMatch.Groups[1].Value : "0.0";
            var symbol = spendableMatch.Success && spendableMatch.Groups.Count > 2 ? spendableMatch.Groups[2].Value : string.Empty;

            process.WaitForExit();

            return new Wallet 
            { 
                Spendable = spendable,
                Symbol = symbol,
            };
        }

        private Farm GetFarm(Fork fork)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = $"{fork.ExecutablePath}",
                    Arguments = "farm summary",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.Start();

            var output = process.StandardOutput.ReadToEnd();

            var stakingMatch = StakingRegex.Match(output);
            var stakingAddress = stakingMatch.Success && stakingMatch.Groups.Count > 1 ? stakingMatch.Groups[1].Value : string.Empty;
            var balance = stakingMatch.Success && stakingMatch.Groups.Count > 2 ? stakingMatch.Groups[2].Value : "0";
            var plots = stakingMatch.Success && stakingMatch.Groups.Count > 3 ? stakingMatch.Groups[3].Value : "0";

            var networkSpaceMatch = NetworkSpaceRegex.Match(output);
            var networkSpace = networkSpaceMatch.Success && networkSpaceMatch.Groups.Count > 1 ? networkSpaceMatch.Groups[1].Value.TrimEnd() : string.Empty;

            var stakingFactorMatch = StakingFactorRegex.Match(output);
            var stakingFactor = stakingFactorMatch.Success && stakingFactorMatch.Groups.Count > 1 ? stakingFactorMatch.Groups[1].Value.TrimEnd() : string.Empty;

            var expectedTimeToWinMatch = ExpectedTimeToWinRegex.Match(output);
            var expectedTimeToWin = expectedTimeToWinMatch.Success && expectedTimeToWinMatch.Groups.Count > 1 ? expectedTimeToWinMatch.Groups[1].Value.TrimEnd() : string.Empty;

            process.WaitForExit();

            return new Farm
            {
                StakingAddress = stakingAddress,
                Balance = balance,
                Plots = plots,
                NetworkSpace = networkSpace,
                StakingFactor = stakingFactor,
                ExpectedTimeToWin = expectedTimeToWin,
            };
        }

        private bool SendStake(Fork fork, Wallet wallet, Farm farm)
        {
            if (!string.IsNullOrEmpty(fork?.ExecutablePath) &&
                !string.IsNullOrEmpty(farm?.StakingAddress) &&
                !string.IsNullOrEmpty(wallet?.Spendable) && float.Parse(wallet.Spendable, CultureInfo.GetCultureInfo("en-US").NumberFormat) > 0)
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = $"{fork.ExecutablePath}",
                        Arguments = $"wallet send -t {farm.StakingAddress} -a {wallet.Spendable}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                process.Start();
                process.WaitForExit();
                return true;
            }

            return false;
        }
    }
}
