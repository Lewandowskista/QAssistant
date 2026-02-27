// Copyright (C) 2026 Lewandowskista
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using QAssistant.Models;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;

namespace QAssistant.Services
{
    [JsonSerializable(typeof(List<Project>))]
    [JsonSerializable(typeof(Project))]
    [JsonSerializable(typeof(Note))]
    [JsonSerializable(typeof(ProjectTask))]
    [JsonSerializable(typeof(AnalysisEntry))]
    [JsonSerializable(typeof(Dictionary<string, List<AnalysisEntry>>))]
    [JsonSerializable(typeof(EmbedLink))]
    [JsonSerializable(typeof(FileAttachment))]
    [JsonSerializable(typeof(LinkType))]
    [JsonSerializable(typeof(Models.TaskStatus))]
    [JsonSerializable(typeof(TaskPriority))]
    [JsonSerializable(typeof(AttachmentScope))]
    [JsonSerializable(typeof(TestCase))]
    [JsonSerializable(typeof(TestCaseStatus))]
    [JsonSerializable(typeof(List<TestCase>))]
    [JsonSerializable(typeof(TestPlan))]
    [JsonSerializable(typeof(List<TestPlan>))]
    [JsonSerializable(typeof(TestExecution))]
    [JsonSerializable(typeof(List<TestExecution>))]
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
        private const string EncryptedPrefix = "ENC1:";
        private static readonly SemaphoreSlim s_fileLock = new(1, 1);

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
                Directory.CreateDirectory(folder);

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
            await s_fileLock.WaitAsync();
            try
            {
                if (!File.Exists(_dataPath))
                    return [];


                var fileContent = await File.ReadAllTextAsync(_dataPath);
                var normalizedJson = fileContent;

                if (fileContent.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
                {
                    var payload = fileContent[EncryptedPrefix.Length..];
                    var encryptedBytes = Convert.FromBase64String(payload);
                    var protectedBuffer = CryptographicBuffer.CreateFromByteArray(encryptedBytes);
                    var provider = new DataProtectionProvider();
                    var decryptedBuffer = await provider.UnprotectAsync(protectedBuffer);
                    CryptographicBuffer.CopyToByteArray(decryptedBuffer, out var decryptedBytes);
                    normalizedJson = Encoding.UTF8.GetString(decryptedBytes);
                }

                List<Project>? result = null;
                try
                {
                    result = JsonSerializer.Deserialize<List<Project>>(normalizedJson, _jsonContext.ListProject);
                }
                catch (Exception deserializeEx)
                {
                    LogMessage($"JsonSerializerContext deserialization failed: {deserializeEx.Message}. Trying default deserializer...");
                    var options = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
                    result = JsonSerializer.Deserialize<List<Project>>(normalizedJson, options);
                }

                result ??= new List<Project>();

                if (!fileContent.StartsWith(EncryptedPrefix, StringComparison.Ordinal))
                {
                    try { await SaveProjectsAsync(result); }
                    catch (Exception saveEx) { LogMessage($"Migration save failed: {saveEx.Message}"); }
                }

                return result;
            }
            catch (Exception ex)
            {
                LogMessage($"LoadProjectsAsync error: {ex.GetType().Name}: {ex.Message}");
                Debug.WriteLine($"StorageService LoadProjectsAsync error: {ex.Message}\n{ex.StackTrace}");
                return [];
            }
            finally
            {
                s_fileLock.Release();
            }
        }


        public async Task SaveProjectsAsync(List<Project> projects)
        {
            await s_fileLock.WaitAsync();
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

                var plainBytes = Encoding.UTF8.GetBytes(jsonContent);
                var plainBuffer = CryptographicBuffer.CreateFromByteArray(plainBytes);
                var provider = new DataProtectionProvider("LOCAL=user");
                var encryptedBuffer = await provider.ProtectAsync(plainBuffer);
                CryptographicBuffer.CopyToByteArray(encryptedBuffer, out var encryptedBytes);
                var encryptedPayload = EncryptedPrefix + Convert.ToBase64String(encryptedBytes);

                await File.WriteAllTextAsync(_dataPath, encryptedPayload);
            }
            catch (Exception ex)
            {
                LogMessage($"SaveProjectsAsync error: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                Debug.WriteLine($"StorageService SaveProjectsAsync error: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
            finally
            {
                s_fileLock.Release();
            }
        }

        public string GetLogPath() => _logPath;
        public string GetDataPath() => _dataPath;
    }
}