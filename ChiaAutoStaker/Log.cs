using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace ChiaAutoStaker
{
    internal class Log
    {
        private readonly object lockObject = new object();
        private readonly string logFile;

        public Log(IConfiguration configuration)
        {
            logFile = configuration["Settings:LogFile"];

            if (!string.IsNullOrEmpty(logFile))
                Write($"Log file enabled: {logFile}");
        }

        public void Write(string message, ConsoleColor? consoleColor = null)
        {
            lock (lockObject)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"{DateTime.Now.ToString(CultureInfo.InvariantCulture)} ");

                if (consoleColor.HasValue)
                {
                    Console.ForegroundColor = consoleColor.Value;
                }
                else
                {
                    Console.ResetColor();
                }

                Console.WriteLine(message);
                Console.ResetColor();

                //file
                if (!string.IsNullOrEmpty(this.logFile))
                {
                    var line = $"{DateTime.Now.ToString(CultureInfo.InvariantCulture)} {message}{Environment.NewLine}";

                    File.AppendAllText(this.logFile, line);
                }
            }
        }
    }
}
