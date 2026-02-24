using System;
using System.Collections.Generic;
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
        private readonly AppJsonContext _jsonContext;

        public StorageService()
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "QAssistant");
            Directory.CreateDirectory(folder);
            _dataPath = Path.Combine(folder, "projects.json");

            _jsonContext = new AppJsonContext(new JsonSerializerOptions { WriteIndented = true });
        }

        public async Task<List<Project>> LoadProjectsAsync()
        {
            try
            {
                if (!File.Exists(_dataPath)) return new List<Project>();

                using var stream = File.OpenRead(_dataPath);
                return await JsonSerializer.DeserializeAsync(stream, _jsonContext.ListProject) ?? new List<Project>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StorageService LoadProjectsAsync error: {ex.Message}");
                return new List<Project>();
            }
        }

        public async Task SaveProjectsAsync(List<Project> projects)
        {
            try
            {
                using var stream = File.Create(_dataPath);
                await JsonSerializer.SerializeAsync(stream, projects, _jsonContext.ListProject);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StorageService SaveProjectsAsync error: {ex.Message}");
            }
        }
    }
}