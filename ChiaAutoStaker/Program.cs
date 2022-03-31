using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChiaAutoStaker
{
    class Program
    {
        static void Main()
        {
            // Setup Host
            var host = CreateDefaultBuilder().Build();

            // Invoke Worker
            using IServiceScope serviceScope = host.Services.CreateScope();
            IServiceProvider provider = serviceScope.ServiceProvider;
            var workerInstance = provider.GetRequiredService<Worker>();
            workerInstance.DoWork();

            host.RunAsync();
        }

        static IHostBuilder CreateDefaultBuilder()
        {
            return Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration(app =>
                {
                    app.AddJsonFile("appsettings.json");
                })
                .ConfigureServices(services =>
                {
                    services.AddSingleton<Log>();
                    services.AddSingleton<Worker>();
                })
                .ConfigureLogging(logging => {
                    logging.ClearProviders();
                });
        }
    }
}