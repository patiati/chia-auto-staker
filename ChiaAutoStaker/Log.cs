using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace ChiaAutoStaker
{
    internal class Log
    {
        private readonly object lockObject = new object();
        private readonly string logFile;
        private bool openLine = false;

        public Log(IConfiguration configuration)
        {
            logFile = configuration["Settings:LogFile"];

            if (!string.IsNullOrEmpty(logFile))
                WriteLine($"Log file enabled: {logFile}");
        }

        public void Write(string message, ConsoleColor? consoleColor = null)
        {
            Write(message, consoleColor, false);
        }

        public void WriteLine(string message, ConsoleColor? consoleColor = null)
        {
            Write(message, consoleColor, true);
        }

        private void Write(string message, ConsoleColor? consoleColor, bool newLine)
        {
            lock (lockObject)
            {
                if (!openLine)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write($"[{DateTime.Now.ToString(CultureInfo.InvariantCulture)}]");
                }

                if (consoleColor.HasValue)
                {
                    Console.ForegroundColor = consoleColor.Value;
                }
                else
                {
                    Console.ResetColor();
                }

                Console.Write($" {message}");

                if (newLine)
                {
                    Console.WriteLine();
                }

                Console.ResetColor();

                //file
                if (!string.IsNullOrEmpty(this.logFile))
                {
                    var text = string.Empty;

                    if (!openLine) 
                    { 
                        text += $"[{DateTime.Now.ToString(CultureInfo.InvariantCulture)}]";
                    }

                    text += $" {message}";

                    if (newLine)
                    {
                        text += Environment.NewLine;
                    }

                    File.AppendAllText(this.logFile, text);
                }

                openLine = !newLine;
            }
        }
    }
}
