using System.Net;
using System.Text.Json;
using SZ_Extractor_Server.Models;

namespace SZ_Extractor_Server.Services
{
    public class ExtractorService
    {
        private ApiOptions? _currentOptions;
        private Extractor? _extractor;
        private readonly SemaphoreSlim _configSemaphore = new(1, 1);

        public async Task HandleConfigure(HttpListenerContext context)
        {
            var options = await ReadJson<ApiOptions>(context.Request);

            await _configSemaphore.WaitAsync();
            try
            {
                options.Validate();
                _currentOptions = options;
                _extractor?.Dispose();
                _extractor = new Extractor(_currentOptions);

                // Get mounted files count from Extractor
                int mountedFilesCount = _extractor.GetMountedFilesCount();

                var responseMessage = new
                {
                    Message = $"Configuration updated, {_currentOptions.GameDir} mounted",
                    MountedFiles = mountedFilesCount
                };

                await WriteJsonResponse(context, responseMessage);
            }
            finally
            {
                _configSemaphore.Release();
            }
        }

        public async Task HandleExtract(HttpListenerContext context)
        {
            var request = await ReadJson<ExtractRequest>(context.Request);

            await _configSemaphore.WaitAsync();
            try
            {
                if (_currentOptions == null || _extractor == null)
                {
                    context.Response.StatusCode = 400;
                    await WriteResponse(context, "Not configured");
                    context.Response.Close();
                    return;
                }

                if (!string.IsNullOrEmpty(request.OutputPath))
                {
                    _currentOptions.OutputPath = request.OutputPath;
                    _extractor.UpdateOutputPath(request.OutputPath);
                }

                if (string.IsNullOrEmpty(request.ContentPath))
                {
                    context.Response.StatusCode = 400;
                    await WriteResponse(context, "ContentPath is required");
                    context.Response.Close();
                    return;
                }
            }
            finally
            {
                _configSemaphore.Release();
            }

            try
            {
                // Run extraction and get the success status
                var (success, filePaths) = _extractor.Run(request.ContentPath);

                // Send success/failure message in response
                var responseMessage = new
                {
                    Message = success ? "Extraction successful" : "Extraction failed",
                    FilePaths = filePaths
                };
                await WriteJsonResponse(context, responseMessage);
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                await WriteResponse(context, ex.Message);
            }
            finally
            {
                context.Response.Close();
            }
        }

        public async Task HandleDuplicates(HttpListenerContext context)
        {
            await _configSemaphore.WaitAsync();
            try
            {
                if (_currentOptions == null || _extractor == null)
                {
                    context.Response.StatusCode = 400;
                    await WriteResponse(context, "Not configured");
                    return;
                }

                var duplicates = _extractor.GetDuplicates();
                await WriteJsonResponse(context, duplicates);
            }
            finally
            {
                _configSemaphore.Release();
                context.Response.Close();
            }
        }

        public async Task HandleDump(HttpListenerContext context)
        {
            var request = await ReadJson<DumpRequest>(context.Request);

            await _configSemaphore.WaitAsync();
            try
            {
                if (_currentOptions == null || _extractor == null)
                {
                    context.Response.StatusCode = 400;
                    await WriteResponse(context, "Not configured");
                    return;
                }

                string dumpResult = _extractor.DumpPaths(request.FilterPath);
                await WriteResponse(context, dumpResult);
            }
            finally
            {
                _configSemaphore.Release();
                context.Response.Close();
            }
        }

        private static async Task<T> ReadJson<T>(HttpListenerRequest request)
        {
            using var reader = new StreamReader(request.InputStream);
            var json = await reader.ReadToEndAsync();
            return JsonSerializer.Deserialize<T>(json)
                ?? throw new ArgumentException("Invalid request body");
        }

        private static async Task WriteResponse(HttpListenerContext context, string message)
        {
            var buffer = System.Text.Encoding.UTF8.GetBytes(message);
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer);
        }

        private static async Task WriteJsonResponse(HttpListenerContext context, object responseObj)
        {
            var json = JsonSerializer.Serialize(responseObj);
            var buffer = System.Text.Encoding.UTF8.GetBytes(json);
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer);
            context.Response.StatusCode = 200;
        }
    }
}