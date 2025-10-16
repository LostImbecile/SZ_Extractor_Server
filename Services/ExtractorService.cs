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
            ApiOptions options;
            try
            {
                options = await ReadJson<ApiOptions>(context.Request);
            }
            catch (JsonException)
            {
                await WriteResponse(context, "Invalid JSON format", HttpStatusCode.BadRequest);
                return;
            }

            await _configSemaphore.WaitAsync();
            try
            {
                try
                {
                    options.Validate();
                }
                catch (ArgumentException ex)
                {
                    await WriteResponse(context, ex.Message, HttpStatusCode.BadRequest);
                    return;
                }

                _currentOptions = options;
                _extractor?.Dispose();
                _extractor = new Extractor(_currentOptions);

                int mountedFilesCount = _extractor.GetMountedFilesCount();

                var responseMessage = new
                {
                    Message = $"Configuration updated, {_currentOptions.GameDir} mounted",
                    MountedFiles = mountedFilesCount
                };

                await WriteJsonResponse(context, responseMessage, HttpStatusCode.OK);
            }
            finally
            {
                _configSemaphore.Release();
            }
        }

        public async Task HandleExtract(HttpListenerContext context)
        {
            ExtractRequest request;
            try
            {
                request = await ReadJson<ExtractRequest>(context.Request);
            }
            catch (JsonException)
            {
                await WriteResponse(context, "Invalid JSON format", HttpStatusCode.BadRequest);
                return;
            }

            await _configSemaphore.WaitAsync();
            try
            {
                if (_currentOptions == null || _extractor == null)
                {
                    await WriteResponse(context, "Not configured", HttpStatusCode.BadRequest);
                    return;
                }

                if (!string.IsNullOrEmpty(request.OutputPath))
                {
                    _currentOptions.OutputPath = request.OutputPath;
                    _extractor.UpdateOutputPath(request.OutputPath);
                }

                if (string.IsNullOrEmpty(request.ContentPath))
                {
                    await WriteResponse(context, "ContentPath is required", HttpStatusCode.BadRequest);
                    return;
                }
            }
            finally
            {
                _configSemaphore.Release();
            }

            try
            {
                var (success, filePaths) = _extractor.Run(request.ContentPath, request.ArchiveName);

                var responseMessage = new
                {
                    Message = success ? "Extraction successful" : "Extraction failed",
                    FilePaths = filePaths
                };
                
                await WriteJsonResponse(context, responseMessage, 
                    success ? HttpStatusCode.Created : HttpStatusCode.BadRequest);
            }
            catch (Exception ex)
            {
                await WriteResponse(context, ex.Message, HttpStatusCode.InternalServerError);
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
                    await WriteResponse(context, "Not configured", HttpStatusCode.BadRequest);
                    return;
                }

                var duplicates = _extractor.GetDuplicates();
                await WriteJsonResponse(context, duplicates, HttpStatusCode.OK);
            }
            catch (Exception ex)
            {
                await WriteResponse(context, ex.Message, HttpStatusCode.InternalServerError);
            }
            finally
            {
                _configSemaphore.Release();
                context.Response.Close();
            }
        }

        public async Task HandleDump(HttpListenerContext context)
        {
            DumpRequest request;
            try
            {
                request = await ReadJson<DumpRequest>(context.Request);
            }
            catch (JsonException)
            {
                await WriteResponse(context, "Invalid JSON format", HttpStatusCode.BadRequest);
                return;
            }

            await _configSemaphore.WaitAsync();
            try
            {
                if (_currentOptions == null || _extractor == null)
                {
                    await WriteResponse(context, "Not configured", HttpStatusCode.BadRequest);
                    return;
                }

                string dumpResult = _extractor.DumpPaths(request.Filter);
                await WriteResponse(context, dumpResult, HttpStatusCode.OK);
            }
            catch (Exception ex)
            {
                await WriteResponse(context, ex.Message, HttpStatusCode.InternalServerError);
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

        private static async Task WriteResponse(HttpListenerContext context, string message, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            context.Response.StatusCode = (int)statusCode;
            var buffer = System.Text.Encoding.UTF8.GetBytes(message);
            context.Response.ContentLength64 = buffer.Length;
            context.Response.ContentType = "text/plain";
            await context.Response.OutputStream.WriteAsync(buffer);
        }

        private static async Task WriteJsonResponse(HttpListenerContext context, object responseObj, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            context.Response.StatusCode = (int)statusCode;
            var json = JsonSerializer.Serialize(responseObj);
            var buffer = System.Text.Encoding.UTF8.GetBytes(json);
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer);
        }
    }
}