using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using QAssistant.Models;

namespace QAssistant.Services
{
    [JsonSerializable(typeof(List<Project>))]
    [JsonSerializable(typeof(Project))]
    [JsonSerializable(typeof(Note))]
    [JsonSerializable(typeof(ProjectTask))]
    [JsonSerializable(typeof(EmbedLink))]
    [JsonSerializable(typeof(FileAttachment))]
    [JsonSerializable(typeof(LinkType))]
    [JsonSerializable(typeof(Models.TaskStatus))]
    [JsonSerializable(typeof(TaskPriority))]
    [JsonSourceGenerationOptions(
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true)]
    public partial class AppJsonContext : JsonSerializerContext
    {
    }

    public class StorageService
    {
        private static readonly Lazy<StorageService> _instance = new(() => new StorageService());
        public static StorageService Instance => _instance.Value;

        private readonly string _dataPath;
        private readonly string _logPath;
        private readonly AppJsonContext _jsonContext;

        public StorageService()
        {
            try
            {
                // Try ApplicationData folder first (standard location)
                string folder;
                try
                {
                    folder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "QAssistant");
                }
                catch
                {
                    // Fallback to LocalApplicationData if ApplicationData fails (can happen in some packaged scenarios)
                    folder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "QAssistant");
                }

                // Ensure directory exists and is writable
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                _dataPath = Path.Combine(folder, "projects.json");
                _logPath = Path.Combine(folder, "storage.log");

                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNameCaseInsensitive = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                _jsonContext = new AppJsonContext(options);

                LogMessage($"StorageService initialized. Data path: {_dataPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"StorageService initialization error: {ex.Message}");
                throw;
            }
        }

        private void LogMessage(string message)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var logEntry = $"[{timestamp}] {message}{Environment.NewLine}";
                _ = Task.Run(() =>
                {
                    try { File.AppendAllText(_logPath, logEntry); }
                    catch { /* Ignore log errors */ }
                });
            }
            catch { /* Ignore log errors */ }
        }

        public async Task<List<Project>> LoadProjectsAsync()
        {
            try
            {
                if (!File.Exists(_dataPath))
                    return new List<Project>();

                var fileContent = await File.ReadAllTextAsync(_dataPath);

                List<Project>? result = null;
                try
                {
                    result = JsonSerializer.Deserialize<List<Project>>(fileContent, _jsonContext.ListProject);
                }
                catch (Exception deserializeEx)
                {
                    LogMessage($"JsonSerializerContext deserialization failed: {deserializeEx.Message}. Trying default deserializer...");
                    var options = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
                    result = JsonSerializer.Deserialize<List<Project>>(fileContent, options);
                }

                result ??= new List<Project>();
                return result;
            }
            catch (Exception ex)
            {
                LogMessage($"LoadProjectsAsync error: {ex.GetType().Name}: {ex.Message}");
                Debug.WriteLine($"StorageService LoadProjectsAsync error: {ex.Message}\n{ex.StackTrace}");
                return new List<Project>();
            }
        }

        public async Task SaveProjectsAsync(List<Project> projects)
        {
            try
            {
                // Ensure directory exists before writing
                var folder = Path.GetDirectoryName(_dataPath);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
                    Directory.CreateDirectory(folder);

                // Serialize
                string jsonContent;
                try
                {
                    jsonContent = JsonSerializer.Serialize(projects, _jsonContext.ListProject);
                }
                catch (Exception serializeEx)
                {
                    LogMessage($"JsonSerializerContext serialization failed: {serializeEx.Message}. Trying default serializer...");
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    jsonContent = JsonSerializer.Serialize(projects, options);
                }

                await File.WriteAllTextAsync(_dataPath, jsonContent);
            }
            catch (Exception ex)
            {
                LogMessage($"SaveProjectsAsync error: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                Debug.WriteLine($"StorageService SaveProjectsAsync error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public string GetLogPath() => _logPath;
        public string GetDataPath() => _dataPath;
    }
}