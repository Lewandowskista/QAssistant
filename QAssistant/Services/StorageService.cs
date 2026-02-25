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
    public partial class AppJsonContext : JsonSerializerContext
    {
    }

    public class StorageService
    {
        private readonly string _dataPath;
        private readonly string _logPath;
        private readonly AppJsonContext _jsonContext;

        public StorageService()
        {
            try
            {
                var folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "QAssistant");

                // Ensure directory exists and is writable
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                _dataPath = Path.Combine(folder, "projects.json");
                _logPath = Path.Combine(folder, "storage.log");

                _jsonContext = new AppJsonContext(new JsonSerializerOptions { WriteIndented = true });

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
                File.AppendAllText(_logPath, logEntry);
            }
            catch { /* Ignore log errors */ }
        }

        public async Task<List<Project>> LoadProjectsAsync()
        {
            try
            {
                LogMessage($"LoadProjectsAsync called. File exists: {File.Exists(_dataPath)}");

                if (!File.Exists(_dataPath))
                {
                    LogMessage("projects.json does not exist, returning empty list");
                    return new List<Project>();
                }

                var fileContent = await File.ReadAllTextAsync(_dataPath);
                LogMessage($"Read file content, length: {fileContent.Length}");

                List<Project>? result = null;
                try
                {
                    result = JsonSerializer.Deserialize<List<Project>>(fileContent, _jsonContext.ListProject);
                }
                catch (Exception deserializeEx)
                {
                    LogMessage($"JsonSerializerContext deserialization failed: {deserializeEx.Message}. Trying default deserializer...");
                    // Fallback to default deserializer for debugging
                    var options = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
                    result = JsonSerializer.Deserialize<List<Project>>(fileContent, options);
                }

                result ??= new List<Project>();
                LogMessage($"LoadProjectsAsync succeeded. Loaded {result.Count} projects");
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
                LogMessage($"SaveProjectsAsync called with {projects.Count} projects");

                // Ensure directory exists before writing
                var folder = Path.GetDirectoryName(_dataPath);
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                    LogMessage($"Created directory: {folder}");
                }

                // Serialize to string first for better error logging
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

                // Write to file
                await File.WriteAllTextAsync(_dataPath, jsonContent);
                LogMessage($"SaveProjectsAsync succeeded. Saved {projects.Count} projects to {_dataPath}");
            }
            catch (Exception ex)
            {
                LogMessage($"SaveProjectsAsync error: {ex.GetType().Name}: {ex.Message}");
                Debug.WriteLine($"StorageService SaveProjectsAsync error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public string GetLogPath() => _logPath;
        public string GetDataPath() => _dataPath;
    }
}