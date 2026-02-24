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
            if (!File.Exists(_dataPath)) return new List<Project>();

            using var stream = File.OpenRead(_dataPath);

            return await JsonSerializer.DeserializeAsync(stream, _jsonContext.ListProject) ?? new List<Project>();
        }

        public async Task SaveProjectsAsync(List<Project> projects)
        {
            using var stream = File.Create(_dataPath);

            await JsonSerializer.SerializeAsync(stream, projects, _jsonContext.ListProject);
        }
    }
}