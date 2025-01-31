using System.Net;
using SZ_Extractor.Services;

namespace SZ_Extractor
{
    public class HttpServer
    {
        private readonly int _port;
        private readonly HttpListener _listener;
        private readonly ExtractorService _extractorService;

        public HttpServer(int port, ExtractorService extractorService)
        {
            _port = port;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://*:{_port}/");
            _extractorService = extractorService;
        }

        public async Task StartAsync()
        {
            _listener.Start();
            Console.WriteLine($"Server running on port {_port}");

            while (true)
            {
                var context = await _listener.GetContextAsync();
                // Handle requests asynchronously but use the shared _extractorService instance
                _ = Task.Run(() => HandleRequest(context, _extractorService));
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

        private static async Task WriteResponse(HttpListenerContext context, string message)
        {
            var buffer = System.Text.Encoding.UTF8.GetBytes(message);
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer);
        }
    }
}