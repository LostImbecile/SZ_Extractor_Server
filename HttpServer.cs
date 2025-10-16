using System.Net;
using SZ_Extractor_Server.Services;

namespace SZ_Extractor_Server
{
    public class HttpServer
    {
        private readonly int _port;
        private readonly HttpListener _listener;
        private readonly ExtractorService _extractorService;
        private readonly bool _bindToAllInterfaces;

        public HttpServer(int port, ExtractorService extractorService, bool bindToAllInterfaces = false)
        {
            _port = port;
            _bindToAllInterfaces = bindToAllInterfaces;
            _listener = new HttpListener();
            // Use localhost by default, or * if binding to all interfaces
            string prefix = _bindToAllInterfaces ? $"http://*:{_port}/" : $"http://localhost:{_port}/";
            _listener.Prefixes.Add(prefix);
            _extractorService = extractorService;
        }

        public async Task StartAsync()
        {
            try 
            {
                _listener.Start();
                Console.WriteLine($"Server running on {(_bindToAllInterfaces ? "all interfaces" : "localhost")} port {_port}");

                while (true)
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(context, _extractorService));
                }
            }
            catch (HttpListenerException ex) when (ex.ErrorCode == 5)
            {
                if (_bindToAllInterfaces)
                {
                    Console.WriteLine("[Error] Access denied. To bind to all interfaces, the application must be run with administrator privileges.");
                    Console.WriteLine("        Alternative: Set 'BindToAllInterfaces': false in config.json to bind to localhost only.");
                }
                else
                {
                    Console.WriteLine("[Error] Access denied when trying to bind to port {_port}.");
                    Console.WriteLine("        Check if another application is using this port or if you need elevated privileges.");
                }
                throw;
            }
        }

        private static async Task HandleRequest(HttpListenerContext context, ExtractorService service)
        {
            var path = context.Request.Url?.AbsolutePath.ToLower();

            try
            {
                if (context.Request.HttpMethod == "POST")
                {
                    switch (path)
                    {
                        case "/configure":
                            await service.HandleConfigure(context);
                            break;
                        case "/extract":
                            await service.HandleExtract(context);
                            break;
                        case "/dump":
                            await service.HandleDump(context);
                            break;
                        default:
                            context.Response.StatusCode = 404;
                            break;
                    }
                }
                else if (context.Request.HttpMethod == "GET")
                {
                    switch (path)
                    {
                        case "/duplicates":
                            await service.HandleDuplicates(context);
                            break;
                        case "/identify":
                        case "/":
                            await HandleIdentify(context);
                            break;
                        default:
                            context.Response.StatusCode = 404;
                            break;
                    }
                }
                else
                {
                    context.Response.StatusCode = 405;
                    await WriteResponse(context, "Method Not Allowed");
                }
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                await WriteResponse(context, $"Error: {ex.Message}");
            }
            finally
            {
                context.Response.Close();
            }
        }

        private static async Task HandleIdentify(HttpListenerContext context)
        {
            var identification = new
            {
                Service = "SZ_Extractor_Server",
                Version = "1.2",
                Status = "running",
                Description = "Unreal Engine asset extraction service"
            };

            var json = System.Text.Json.JsonSerializer.Serialize(identification);
            var buffer = System.Text.Encoding.UTF8.GetBytes(json);
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer);
        }

        private static async Task WriteResponse(HttpListenerContext context, string message)
        {
            var buffer = System.Text.Encoding.UTF8.GetBytes(message);
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer);
        }
    }
}