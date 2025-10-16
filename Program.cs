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
        private static readonly Config DefaultConfig = new Config { Port = 5000, BindToAllInterfaces = false };

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

            // Parse command-line arguments
            int? portOverride = null;
            int? pidToMonitor = null;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                
                // Check for port argument
                if ((arg == "--port" || arg == "-p") && i + 1 < args.Length)
                {
                    if (int.TryParse(args[i + 1], out int port))
                    {
                        portOverride = port;
                        i++; // Skip next argument since we consumed it
                    }
                    else
                    {
                        Console.WriteLine($"Invalid port value: {args[i + 1]}");
                    }
                }
                // Check for PID argument
                else if ((arg == "--pid" || arg == "-pid") && i + 1 < args.Length)
                {
                    if (int.TryParse(args[i + 1], out int pid))
                    {
                        pidToMonitor = pid;
                        i++; // Skip next argument since we consumed it
                    }
                }
                // Support legacy positional argument for PID (first arg is PID if it's just a number)
                else if (i == 0 && int.TryParse(arg, out int legacyPid) && !arg.StartsWith("-"))
                {
                    pidToMonitor = legacyPid;
                }
            }

            // Override config port if specified
            if (portOverride.HasValue)
            {
                Console.WriteLine($"Port override from command-line: {portOverride.Value}");
                config.Port = portOverride.Value;
            }

            // Optional PID monitoring
            if (pidToMonitor.HasValue)
            {
                try
                {
                    // Verify PID exists before starting monitoring
                    var parentProcess = Process.GetProcessById(pidToMonitor.Value);
                    if (parentProcess != null)
                    {
                        MonitorParentProcess(parentProcess);
                    }
                    else
                    {
                        Console.WriteLine($"Parent process with PID {pidToMonitor.Value} not found");
                    }
                }
                catch (ArgumentException)
                {
                    Console.WriteLine($"Parent process with PID {pidToMonitor.Value} not found");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error verifying parent process: {ex.Message}");
                }
            }

            var extractorService = new ExtractorService();
            
            try 
            {
                var server = new HttpServer(config.Port, extractorService, config.BindToAllInterfaces);
                Console.WriteLine($"Starting server on {(config.BindToAllInterfaces ? "all interfaces" : "localhost")}:{config.Port}");
                
                // Keep the Task from exiting
                await server.StartAsync();
                await Task.Delay(Timeout.Infinite);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Server Error] {ex.Message}");
                // Optional: Add a small delay to keep the error message visible
                await Task.Delay(TimeSpan.FromSeconds(5));
                throw;
            }
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

        private static void MonitorParentProcess(Process parentProcess)
        {
            var timer = new Timer(_ =>
            {
                try
                {
                    // Check if process has exited
                    if (parentProcess.HasExited || parentProcess.WaitForExit(0))
                    {
                        Console.WriteLine($"Parent process {parentProcess.Id} has exited, shutting down...");
                        Environment.Exit(0);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Process has already exited
                    Console.WriteLine($"Parent process {parentProcess.Id} has exited, shutting down...");
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error monitoring parent process: {ex.Message}");
                    Environment.Exit(1);
                }
            }, null, 0, 2000);

            // Ensure timer is properly disposed when application exits
            AppDomain.CurrentDomain.ProcessExit += (s, e) => timer?.Dispose();
        }
    }
}