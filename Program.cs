using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SZ_Extractor.Services;

namespace SZ_Extractor
{
    class Program
    {
        private static readonly bool _createdNew;

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

            // Optional PID monitoring
            if (args.Length > 0 && int.TryParse(args[0], out int mainPid))
            {
                MonitorParentProcess(mainPid);
            }

            // Create ExtractorService instance here
            var extractorService = new ExtractorService();

            // Start HTTP server
            var server = new HttpServer(5000, extractorService);

            // Keep the Task from exiting
            await server.StartAsync();
            await Task.Delay(Timeout.Infinite);
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