using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using CUE4Parse.UE4.Objects.Core.Misc;
using System.Text.Json;
using CUE4Parse.UE4.VirtualFileSystem;
using SZ_Extractor_Server.Models;

namespace SZ_Extractor_Server
{
    public class Extractor : IDisposable
    {
        private readonly ApiOptions _options;
        private readonly DefaultFileProvider _provider;
        private readonly Dictionary<string, List<string>> _duplicates;
        private static readonly HashSet<char> RegexSpecialChars = new() { '\\', '*', '+', '?', '|', '{', '}', '[', ']', '(', ')', '^', '$', '.' };

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
                        value = new List<string>();
                        fileLocations[normalizedPath] = value;
                    }

                    value.Add(Path.GetFileNameWithoutExtension(vfs.Name));
                }
            }
            return fileLocations.Where(kv => kv.Value.Count > 1).ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        // Updated Run method signature to accept an optional archiveName
        public (bool, List<string>) Run(string contentPath, string? archiveName = null)
        {
            var targetPathLower = NormalizePath(contentPath).ToLowerInvariant();
            Console.WriteLine($"Requested: {contentPath}");

            bool anyExtractionSuccessful = false;
            List<string> extractedFilePaths = new List<string>(); // List to store extracted file paths

            foreach (var vfs in _provider.MountedVfs)
            {
                // If an archive filter is provided, skip VFS whose name doesn't match
                if (!string.IsNullOrEmpty(archiveName))
                {
                    if (!string.Equals(Path.GetFileNameWithoutExtension(vfs.Name), archiveName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                if (IsDirectory(targetPathLower, vfs))
                {
                    // Pass archiveName as the archiveNameOverride parameter
                    var (success, folderFilePaths) = ExtractFolder(targetPathLower, vfs, archiveName);
                    anyExtractionSuccessful |= success;
                    extractedFilePaths.AddRange(folderFilePaths);
                }
                else
                {
                    var (success, filePaths) = ExtractFile(targetPathLower, vfs, archiveName);
                    anyExtractionSuccessful |= success;
                    extractedFilePaths.AddRange(filePaths);
                }
            }

            return (anyExtractionSuccessful, extractedFilePaths);
        }

        private (bool, List<string>) ExtractFile(string targetPathLower, IAesVfsReader vfs, string? archiveNameOverride = null)
        {
            bool isFilenameOnly = !targetPathLower.Contains('\\');
            var fileEntries = vfs.Files.Where(f =>
                isFilenameOnly
                    ? NormalizePath(f.Key).EndsWith(targetPathLower, StringComparison.OrdinalIgnoreCase)
                    : NormalizePath(f.Key).Equals(targetPathLower, StringComparison.OrdinalIgnoreCase)
            ).ToList();

            if (fileEntries.Count == 0)
            {
                return (false, new List<string>());
            }

            bool extractionSuccess = false;
            List<string> extractedFilePaths = new List<string>();
            foreach (var fileEntry in fileEntries)
            {
                if (_provider.TrySavePackage(fileEntry.Key, out var packageData))
                {
                    string archiveName = !string.IsNullOrWhiteSpace(archiveNameOverride)
                        ? archiveNameOverride 
                        : Path.GetFileNameWithoutExtension(vfs.Name);
                    string outputDirectory = _options.OutputPath;

                    if (isFilenameOnly)
                    {
                        if (_duplicates.ContainsKey(NormalizePath(fileEntry.Key)))
                        {
                            outputDirectory = Path.Combine(outputDirectory, archiveName);
                        }
                    }
                    else
                    {
                        if (_duplicates.ContainsKey(targetPathLower))
                        {
                            outputDirectory = Path.Combine(outputDirectory, archiveName);
                        }
                    }

                    // Get the final output path
                    string finalOutputPath = WriteToFile(packageData, outputDirectory, fileEntry.Value.Name);

                    extractedFilePaths.Add(finalOutputPath);
                    extractionSuccess = true;
                }
            }
            return (extractionSuccess, extractedFilePaths);
        }

        private (bool, List<string>) ExtractFolder(string targetPathLower, IAesVfsReader vfs, string? archiveNameOverride = null)
        {
            var files = vfs.Files
                .Where(x => NormalizePath(x.Key).StartsWith(targetPathLower, StringComparison.OrdinalIgnoreCase));

            bool extractionSuccess = false;
            List<string> extractedFilePaths = new List<string>();
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
                    // Updated archiveName assignment to handle empty strings
                    string archiveName = !string.IsNullOrWhiteSpace(archiveNameOverride)
                        ? archiveNameOverride
                        : Path.GetFileNameWithoutExtension(vfs.Name);
                        
                    if (_duplicates.ContainsKey(NormalizePath(file.Key)))
                    {
                        outputDirectory = Path.Combine(outputDirectory, archiveName);
                    }

                    if (!string.IsNullOrEmpty(subfolderPath))
                    {
                        outputDirectory = Path.Combine(outputDirectory, subfolderPath);
                    }

                    // Get the final output path
                    string finalOutputPath = WriteToFile(packageData, outputDirectory, file.Value.Name);

                    extractedFilePaths.Add(finalOutputPath);
                    extractionSuccess = true;
                }
            }
            return (extractionSuccess, extractedFilePaths);
        }

        private static string WriteToFile(IReadOnlyDictionary<string, byte[]> packageData, string outputDirectoryPath, string originalFilename)
        {
            string finalOutputPath = Path.Combine(Directory.CreateDirectory(outputDirectoryPath).FullName, Path.GetFileName(originalFilename));

            File.WriteAllBytes(finalOutputPath, packageData.First().Value);

            return finalOutputPath;
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
            // Check if filter contains regex characters, but treat escaped characters properly
            bool isRegexSearch = false;
            for (int i = 0; i < filter.Length; i++)
            {
                if (i > 0 && filter[i - 1] == '\\')
                    continue;
                    
                if (RegexSpecialChars.Contains(filter[i]))
                {
                    isRegexSearch = true;
                    break;
                }
            }

            if (!isRegexSearch)
            {
                return DumpPathsLegacy(filter);
            }

            var output = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            Console.WriteLine($"Searching with regex: {filter}");

            try
            {
                // Don't modify the regex pattern, just normalize slashes
                var normalizedFilter = filter.Replace('/', '\\');

                var regex = new System.Text.RegularExpressions.Regex(
                    normalizedFilter,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant
                );

                foreach (var vfs in _provider.MountedVfs)
                {
                    var archiveName = Path.GetFileNameWithoutExtension(vfs.Name);
                    var matchingFiles = new List<string>();

                    foreach (var file in vfs.Files)
                    {
                        var normalizedPath = NormalizePath(file.Key);
                        if (regex.IsMatch(normalizedPath))
                        {
                            matchingFiles.Add(file.Key);
                        }
                    }

                    if (matchingFiles.Count > 0)
                    {
                        if (output.TryGetValue(archiveName, out var existing))
                        {
                            existing.AddRange(matchingFiles);
                            output[archiveName] = existing.Distinct().ToList();
                        }
                        else
                        {
                            output[archiveName] = matchingFiles;
                        }
                    }
                }
            }
            catch (System.Text.RegularExpressions.RegexParseException)
            {
                return DumpPathsLegacy(filter);
            }

            return JsonSerializer.Serialize(output, options);
        }

        private string DumpPathsLegacy(string filter)
        {
            var output = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var filterNormalized = NormalizePath(filter).ToLowerInvariant();

            foreach (var vfs in _provider.MountedVfs)
            {
                var archiveName = Path.GetFileNameWithoutExtension(vfs.Name);
                var matchingFiles = vfs.Files
                    .Where(file => NormalizePath(file.Key).ToLowerInvariant().Contains(filterNormalized))
                    .Select(file => file.Key)
                    .ToList();

                if (matchingFiles.Count > 0)
                {
                    if (output.TryGetValue(archiveName, out var existing))
                    {
                        existing.AddRange(matchingFiles);
                        // Remove duplicates while preserving order
                        output[archiveName] = existing.Distinct().ToList();
                    }
                    else
                    {
                        output[archiveName] = matchingFiles;
                    }
                }
            }

            return JsonSerializer.Serialize(output, options);
        }
    }
}