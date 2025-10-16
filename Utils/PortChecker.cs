using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace SZ_Extractor_Server.Utils
{
    public static class PortChecker
    {
        public static async Task<bool> CheckPortAvailability(int port)
        {
            if (!IsPortInUse(port))
            {
                return true;
            }

            if (await IsServerAlreadyRunning(port))
            {
                Console.WriteLine("Server already running");
                return false;
            }

            Console.WriteLine($"Port {port} is being used by another application.");
            return false;
        }

        private static bool IsPortInUse(int port)
        {
            try
            {
                using var client = new TcpClient();
                client.Connect("127.0.0.1", port);
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        private static async Task<bool> IsServerAlreadyRunning(int port)
        {
            try
            {
                using var httpClient = new HttpClient
                {
                    Timeout = TimeSpan.FromMilliseconds(500)
                };

                var response = await httpClient.GetAsync($"http://localhost:{port}/identify");
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var identification = JsonSerializer.Deserialize<ServerIdentification>(content);
                    
                    if (identification?.Service == "SZ_Extractor_Server")
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private class ServerIdentification
        {
            public string? Service { get; set; }
            public string? Version { get; set; }
            public string? Status { get; set; }
            public string? Description { get; set; }
        }
    }
}

