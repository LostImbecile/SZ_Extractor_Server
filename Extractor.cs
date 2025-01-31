using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using CUE4Parse.UE4.Objects.Core.Misc;
using System.Text.Json;
using CUE4Parse.UE4.VirtualFileSystem;
using System.Text.Json.Serialization;
using System.Diagnostics.CodeAnalysis;
using SZ_Extractor.Models;

namespace SZ_Extractor
{
    public class Extractor : IDisposable
    {
        private readonly ApiOptions _options;
        private readonly DefaultFileProvider _provider;
        private readonly Dictionary<string, List<string>> _duplicates;

        public Extractor(ApiOptions options)
        {
            _options = options;
            _options.OutputPath = Path.GetFullPath(_options.OutputPath);

            _provider = new DefaultFileProvider(
                _options.GameDir,
                SearchOption.AllDirectories,
                isCaseInsensitive: false,
                new VersionContainer(_options.EngineVersion)
            );

            _provider.Initialize();

            var aesKeyBytes = Convert.FromHexString(_options.AesKey.Replace("0x", ""));
            _provider.SubmitKey(new FGuid(), new FAesKey(aesKeyBytes));

            _provider.Mount();

            // Get the number of mounted files
            int mountedFileCount = _provider.Files.Count;

            // Print the number of mounted files to the console
            Console.WriteLine($"Successfully mounted {mountedFileCount} files");

            // Initialize _duplicates here
            _duplicates = FindDuplicateFiles();
        }

        public void Dispose()
        {
            _provider?.Dispose();
            GC.SuppressFinalize(this);
        }

        public void UpdateOutputPath(string newOutputPath)
        {
            _options.OutputPath = Path.GetFullPath(newOutputPath);
        }

        public Dictionary<string, List<string>> GetDuplicates()
        {
            return _duplicates;
        }

        public int GetMountedFilesCount()
        {
            return _provider.Files.Count;
        }

        private Dictionary<string, List<string>> FindDuplicateFiles()
        {
            var fileLocations = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var vfs in _provider.MountedVfs)
            {
                foreach (var file in vfs.Files)
                {
                    var normalizedPath = NormalizePath(file.Key);
                    if (!fileLocations.TryGetValue(normalizedPath, out List<string>? value))
                    {
                        value = ([]);
                        fileLocations[normalizedPath] = value;
                    }

                    value.Add(Path.GetFileNameWithoutExtension(vfs.Name));
                }
            }
            return fileLocations.Where(kv => kv.Value.Count > 1).ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        public bool Run(string contentPath)
        {
            var targetPathLower = NormalizePath(contentPath).ToLowerInvariant();
            Console.WriteLine($"Requested: {contentPath}"); // Print the requested file/folder

            bool anyExtractionSuccessful = false;
            foreach (var vfs in _provider.MountedVfs)
            {
                if (IsDirectory(targetPathLower, vfs))
                {
                    if (ExtractFolder(targetPathLower, vfs))
                    {
                        anyExtractionSuccessful = true;
                    }
                }
                else
                {
                    if (ExtractFile(targetPathLower, vfs))
                    {
                        anyExtractionSuccessful = true;
                    }
                }
            }

            return anyExtractionSuccessful;
        }

        private bool ExtractFile(string targetPathLower, IAesVfsReader vfs, string? archiveNameOverride = null)
        {
            bool isFilenameOnly = !targetPathLower.Contains('\\');

            // Find all matching files (either full path or filename match)
            var fileEntries = vfs.Files.Where(f =>
                isFilenameOnly
                    ? NormalizePath(f.Key).EndsWith(targetPathLower, StringComparison.OrdinalIgnoreCase)
                    : NormalizePath(f.Key).Equals(targetPathLower, StringComparison.OrdinalIgnoreCase)
            ).ToList();

            if (fileEntries.Count == 0)
            {
                return false;
            }

            bool extractionSuccess = false;
            foreach (var fileEntry in fileEntries)
            {
                if (_provider.TrySavePackage(fileEntry.Key, out var packageData))
                {
                    string archiveName = archiveNameOverride ?? Path.GetFileNameWithoutExtension(vfs.Name);
                    string outputDirectory = _options.OutputPath;

                    // Handle duplicates (different handling for filename-only mode)
                    if (isFilenameOnly)
                    {
                        // If filename-only, use the full path from the entry to determine duplicates
                        if (_duplicates.ContainsKey(NormalizePath(fileEntry.Key)))
                        {
                            outputDirectory = Path.Combine(outputDirectory, archiveName);
                        }
                    }
                    else
                    {
                        // If full path, use the provided target path for duplicate handling
                        if (_duplicates.ContainsKey(targetPathLower))
                        {
                            outputDirectory = Path.Combine(outputDirectory, archiveName);
                        }
                    }

                    WriteToFile(packageData, outputDirectory, fileEntry.Value.Name);
                    extractionSuccess = true;
                }
            }
            return extractionSuccess;
        }

        private bool ExtractFolder(string targetPathLower, IAesVfsReader vfs)
        {
            var files = vfs.Files
                .Where(x => NormalizePath(x.Key).StartsWith(targetPathLower, StringComparison.OrdinalIgnoreCase));

            bool extractionSuccess = false;
            foreach (var file in files)
            {
                if (_provider.TrySavePackage(file.Key, out var packageData))
                {
                    var relativePath = NormalizePath(file.Key[targetPathLower.Length..]);
                    var lastSlashIndex = relativePath.LastIndexOf('\\');

                    string subfolderPath = string.Empty;
                    if (lastSlashIndex != -1)
                    {
                        subfolderPath = relativePath[..lastSlashIndex];
                    }

                    string outputDirectory = _options.OutputPath;

                    // Handle duplicates by adding archive name to the path
                    string archiveName = Path.GetFileNameWithoutExtension(vfs.Name);
                    if (_duplicates.ContainsKey(NormalizePath(file.Key)))
                    {
                        outputDirectory = Path.Combine(outputDirectory, archiveName);
                    }

                    if (!string.IsNullOrEmpty(subfolderPath))
                    {
                        outputDirectory = Path.Combine(outputDirectory, subfolderPath);
                    }

                    WriteToFile(packageData, outputDirectory, file.Value.Name);
                    extractionSuccess = true;
                }
            }
            return extractionSuccess;
        }

        private static void WriteToFile(IReadOnlyDictionary<string, byte[]> packageData, string outputDirectoryPath, string originalFilename)
        {
            string finalOutputPath = Path.Combine(Directory.CreateDirectory(outputDirectoryPath).FullName, Path.GetFileName(originalFilename));

            File.WriteAllBytes(finalOutputPath, packageData.First().Value);
        }

        private static bool IsDirectory(string targetPathLower, IAesVfsReader vfs)
        {
            return vfs.Files.Any(x => NormalizePath(x.Key).StartsWith(targetPathLower, StringComparison.OrdinalIgnoreCase));
        }
        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            string normalizedPath = path.Replace('/', '\\');

            // Replace multiple consecutive slashes with a single slash
            while (normalizedPath.Contains("\\\\"))
            {
                normalizedPath = normalizedPath.Replace("\\\\", "\\");
            }

            return normalizedPath.TrimStart('\\').TrimEnd('\\');
        }
        readonly JsonSerializerOptions options = new() { WriteIndented = true };
        public string DumpPaths(string filter)
        {
            var output = new Dictionary<string, List<string>>();
            var filterNormalised = NormalizePath(filter);

            foreach (var vfs in _provider.MountedVfs)
            {
                var archiveName = Path.GetFileNameWithoutExtension(vfs.Name);
                var matchingFiles = new List<string>(); // Initialize outside the inner loop

                foreach (var file in vfs.Files)
                {
                    var normalizedPath = NormalizePath(file.Key);
                    if (normalizedPath.Contains(filterNormalised, StringComparison.OrdinalIgnoreCase))
                    {
                        matchingFiles.Add(file.Key);
                    }
                }

                // Only add the entry if there are matching files
                if (matchingFiles.Count != 0)
                {
                    output[archiveName] = matchingFiles;
                }
            }

            return JsonSerializer.Serialize(output, options);
        }
    }
}