using System.Diagnostics;
using System.Text.Json;
using SZ_Extractor_Server.Services;
using SZ_Extractor_Server;

namespace SZ_Extractor_Server
{
    class Program
    {
        private static readonly bool _createdNew;
        private const string ConfigFileName = "config.json";
        private static readonly Config DefaultConfig = new Config { Port = 5000 };

        static Program()
        {
            _ = new Mutex(true, "SZ_Extractor_Server", out _createdNew);
        }

        static async Task Main(string[] args)
        {
            if (!_createdNew)
            {
                Console.WriteLine("Server already running");
                return;
            }

            // Load or create configuration
            Config config = LoadOrCreateConfig();

            // Optional PID monitoring
            if (args.Length > 0 && int.TryParse(args[0], out int mainPid))
            {
                MonitorParentProcess(mainPid);
            }

            // Create ExtractorService instance here
            var extractorService = new ExtractorService();

            // Start HTTP server
            var server = new HttpServer(config.Port, extractorService);

            // Keep the Task from exiting
            await server.StartAsync();
            await Task.Delay(Timeout.Infinite);
        }

        private static Config LoadOrCreateConfig()
        {
            if (File.Exists(ConfigFileName))
            {
                try
                {
                    var configJson = File.ReadAllText(ConfigFileName);
                    return JsonSerializer.Deserialize<Config>(configJson) ?? DefaultConfig;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading config file: {ex.Message}");
                }
            }

            // Create config file if it doesn't exist or if there was an error
            try
            {
                var configJson = JsonSerializer.Serialize(DefaultConfig, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFileName, configJson);
                Console.WriteLine($"Created default config file: {ConfigFileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating config file: {ex.Message}");
            }

            return DefaultConfig;
        }

        private static void MonitorParentProcess(int pid)
        {
            _ = new Timer(_ =>
            {
                try { Process.GetProcessById(pid); }
                catch { Environment.Exit(0); }
            }, null, 0, 2000);
        }
    }
}