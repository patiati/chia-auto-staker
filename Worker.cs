using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace ChiaAutoStaker
{
    internal class Worker
    {
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
            {
                log.WriteLine($"Log file enabled: {logFile}");
            }
        }

        public void DoWork()
        {
            log.WriteLine("Looking for forks ...");

            var settings = this.GetSettings();

            var enabledForks = settings?.Forks?.Where(f => f.Enabled) ?? new Fork[] { };

            log.WriteLine($"Interval: {settings?.IntervalSeconds ?? 300} seconds");

            while (true)
            {
                var now = DateTime.Now;
                if (lastRun.AddSeconds(settings?.IntervalSeconds ?? 300) <= now)
                {
                    lastRun = now;

                    log.WriteLine($"Checking wallets.");

                    foreach (var fork in enabledForks)
                    {
                        log.Write($"- {fork.Name} ...");
                        var wallet = GetWallet(fork) ?? new Wallet();
                        log.Write($"{wallet.Spendable} {wallet.Symbol}");

                        if (float.Parse(wallet.Spendable ?? "0", CultureInfo.GetCultureInfo("en-US").NumberFormat) > 0)
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
            var settings = configuration.Get<Settings>() ?? new Settings();

            var localAppData = configuration["LOCALAPPDATA"];

            var forks = settings.Forks?.Where(f => f.Enabled) ?? new Fork[] { };

            foreach (var fork in forks)
            {
                try
                {
                    // ExecutablePath

                    var appFolder = 
                        Directory.Exists($"{localAppData}\\{fork.Folder}")
                            ? Directory.GetDirectories($"{localAppData}\\{fork.Folder}", "app-*").FirstOrDefault()
                            : string.Empty;

                    if (string.IsNullOrEmpty(appFolder))
                    {
                        appFolder = $"{localAppData}\\Programs\\{fork.Folder}";
                    }

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
                            log.WriteLine($"{fork.Name} not found!", ConsoleColor.Red);
                        }
                    }

                    // StakingAddress

                    if (fork.Enabled)
                    {
                        if (string.IsNullOrEmpty(fork.StakingAddress) &&
                            !string.IsNullOrEmpty(fork.StakingAddressRegex))
                        {
                            var process = new Process
                            {
                                StartInfo = new ProcessStartInfo
                                {
                                    FileName = $"{fork.ExecutablePath}",
                                    Arguments = fork.StakingAddressCommand,
                                    UseShellExecute = false,
                                    RedirectStandardOutput = true,
                                    RedirectStandardError = true
                                }
                            };
                            process.Start();

                            var output = process.StandardOutput.ReadToEnd();

                            var stakingAddressMatch = new Regex(fork.StakingAddressRegex).Match(output);
                            var address = 
                                stakingAddressMatch.Success && stakingAddressMatch.Groups.Count > 1 
                                    ? stakingAddressMatch.Groups[1].Value 
                                    : string.Empty;

                            process.WaitForExit();

                            if (!string.IsNullOrEmpty(address))
                            {
                                fork.StakingAddress = address;
                            }
                            else
                            {
                                fork.Enabled = false;
                            }
                        }

                        if (!string.IsNullOrEmpty(fork.StakingAddress))
                        {
                            log.WriteLine($"Staking address: {fork.StakingAddress}", ConsoleColor.Yellow);
                        }
                        else
                        {
                            log.WriteLine(string.Empty);
                        }
                    }
                }
                catch (Exception)
                {
                    fork.Enabled = false;
                }
            }

            SaveConfigFile(settings);
            return settings;
        }

        private void SaveConfigFile(Settings settings)
        {
            var json = JsonConvert.SerializeObject(settings, Formatting.Indented);

            File.WriteAllText("ChiaAutoStaker.config.json", json);

            log.WriteLine("Settings saved.");
        }

        private Wallet? GetWallet(Fork fork)
        {
            if (string.IsNullOrEmpty(fork.WalletRegex))
            {
                return null;
            }

            var process = new Process {
                StartInfo = new ProcessStartInfo
                {
                    FileName = $"{fork.ExecutablePath}",
                    Arguments = fork.WalletCommand,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };           
            process.Start();
            
            var output = process.StandardOutput.ReadToEnd();

            var spendableMatch = new Regex(fork.WalletRegex).Match(output);
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
                !string.IsNullOrEmpty(fork.StakingCommand) &&
                !string.IsNullOrEmpty(wallet?.Spendable) && 
                float.Parse(wallet.Spendable, CultureInfo.GetCultureInfo("en-US").NumberFormat) > 0)
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = $"{fork.ExecutablePath}",
                        Arguments = fork.StakingCommand.Replace("{address}", fork.StakingAddress).Replace("{amount}", wallet.Spendable),
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
