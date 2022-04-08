using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace ChiaAutoStaker
{
    internal class Worker
    {
        private static readonly Regex SpendableRegex = new(@"-Spendable: (\d*\.\d*) (\w*)");
        private static readonly Regex StakingAddressRegex = new(@"Staking addresses:(?:\n|\r|\r\n)  ([0-9a-z]*)");
        private readonly IConfiguration configuration;
        private readonly Log log;

        private DateTime lastRun = DateTime.MinValue;

        public Worker(Log log, IConfiguration configuration)
        {
            this.log = log;
            this.configuration = configuration;

            log.WriteLine($"ChiaAutoStaker v{Assembly.GetExecutingAssembly().GetName().Version}");

            var logFile = configuration["Settings:LogFile"];
            if (!string.IsNullOrEmpty(logFile))
                log.WriteLine($"Log file enabled: {logFile}");
        }

        public void DoWork()
        {
            log.WriteLine("Looking for forks ...");

            var settings = this.GetSettings();

            var enabledForks = settings.Forks.Where(f => f.Enabled);

            log.WriteLine($"Interval: {settings.IntervalSeconds} seconds");

            while (true)
            {
                var now = DateTime.Now;
                if (lastRun.AddSeconds(settings.IntervalSeconds) <= now)
                {
                    lastRun = now;

                    foreach (var fork in enabledForks)
                    {
                        log.Write($"Checking {fork.Name} ...");
                        var wallet = GetWallet(fork);
                        log.Write($"{wallet.Spendable} {wallet.Symbol}");

                        if (float.Parse(wallet.Spendable, CultureInfo.GetCultureInfo("en-US").NumberFormat) > 0)
                        {
                            log.Write("- Staking ...");
                            if (SendStake(fork, wallet))
                            {
                                log.Write("Succeded!", ConsoleColor.Green);
                            }
                            else
                            {
                                log.Write("Failed!", ConsoleColor.Red);
                            }
                        }

                        log.WriteLine(string.Empty);
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
                    // ExecutablePath

                    var appFolder = Directory.GetDirectories($"{localAppData}\\{fork.Folder}", "app-*").FirstOrDefault();

                    if (!string.IsNullOrEmpty(appFolder))
                    {
                        var exePath = $"{appFolder}\\resources\\app.asar.unpacked\\daemon\\{fork.Executable}";

                        if (File.Exists(exePath))
                        {
                            fork.ExecutablePath = exePath;
                            log.Write($"{fork.Name} found!", ConsoleColor.Green);
                        }
                        else
                        {
                            fork.Enabled = false;
                            log.Write($"{fork.Name} not found!", ConsoleColor.Red);
                        }
                    }

                    // StakingAddress

                    if (fork.Enabled && string.IsNullOrEmpty(fork.StakingAddress))
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

                        var stakingAddressMatch = StakingAddressRegex.Match(output);
                        var address = stakingAddressMatch.Success && stakingAddressMatch.Groups.Count > 1 ? stakingAddressMatch.Groups[1].Value : string.Empty;

                        process.WaitForExit();

                        if (!string.IsNullOrEmpty(address))
                        {
                            fork.StakingAddress = address;
                            log.WriteLine($"Staking address: {fork.StakingAddress}", ConsoleColor.Yellow);
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

        private bool SendStake(Fork fork, Wallet wallet)
        {
            if (!string.IsNullOrEmpty(fork?.ExecutablePath) &&
                !string.IsNullOrEmpty(fork?.StakingAddress) &&
                !string.IsNullOrEmpty(wallet?.Spendable) && float.Parse(wallet.Spendable, CultureInfo.GetCultureInfo("en-US").NumberFormat) > 0)
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = $"{fork.ExecutablePath}",
                        Arguments = $"wallet send -t {fork.StakingAddress} -a {wallet.Spendable}",
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
