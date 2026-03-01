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
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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
    [JsonSerializable(typeof(SavedApiRequest))]
    [JsonSerializable(typeof(List<SavedApiRequest>))]
    [JsonSerializable(typeof(ApiRequestHistoryEntry))]
    [JsonSerializable(typeof(List<ApiRequestHistoryEntry>))]
    [JsonSerializable(typeof(ChecklistTemplate))]
    [JsonSerializable(typeof(List<ChecklistTemplate>))]
    [JsonSerializable(typeof(ChecklistItem))]
    [JsonSerializable(typeof(ChecklistItemPriority))]
    [JsonSerializable(typeof(QaEnvironment))]
    [JsonSerializable(typeof(List<QaEnvironment>))]
    [JsonSerializable(typeof(EnvironmentType))]
    [JsonSerializable(typeof(TestDataGroup))]
    [JsonSerializable(typeof(List<TestDataGroup>))]
    [JsonSerializable(typeof(TestDataEntry))]
    [JsonSerializable(typeof(JiraConnection))]
    [JsonSerializable(typeof(List<JiraConnection>))]
    [JsonSerializable(typeof(LinearConnection))]
    [JsonSerializable(typeof(List<LinearConnection>))]
    [JsonSerializable(typeof(SapCommerceModule))]
    [JsonSerializable(typeof(SapCommerceModule?))]
    [JsonSerializable(typeof(Runbook))]
    [JsonSerializable(typeof(List<Runbook>))]
    [JsonSerializable(typeof(RunbookStep))]
    [JsonSerializable(typeof(RunbookCategory))]
    [JsonSerializable(typeof(RunbookStepStatus))]
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
        private readonly string _settingsPath;
        private readonly AppJsonContext _jsonContext;
        private const string EncryptedPrefix = "ENC1:";
        private static readonly SemaphoreSlim s_fileLock = new(1, 1);
        private static readonly object s_settingsLock = new();
        private Dictionary<string, string>? _settingsCache;
        private static readonly object s_secretsLock = new();
        private Dictionary<string, string>? _secretsCache;
        private readonly string _secretsPath;

        private StorageService()
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
                _settingsPath = Path.Combine(folder, "settings.json");
                _secretsPath = Path.Combine(folder, "secrets.json");

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
                    try { await SaveProjectsInternalAsync(result); }
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
                await SaveProjectsInternalAsync(projects);
            }
            finally
            {
                s_fileLock.Release();
            }
        }

        /// <summary>
        /// Performs the actual save without acquiring <see cref="s_fileLock"/>.
        /// Must only be called while the lock is already held.
        /// </summary>
        private async Task SaveProjectsInternalAsync(List<Project> projects)
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
        }

        public string GetLogPath() => _logPath;
        public string GetDataPath() => _dataPath;

        // ── Local Settings (non-secret configuration) ─────────────────

        /// <summary>
        /// Reads a non-secret setting from the local settings file.
        /// Returns <c>null</c> when the key does not exist.
        /// </summary>
        public string? GetSetting(string key)
        {
            lock (s_settingsLock)
            {
                _settingsCache ??= LoadSettingsFromDisk();
                return _settingsCache.TryGetValue(key, out var value) ? value : null;
            }
        }

        /// <summary>
        /// Writes a non-secret setting to the local settings file.
        /// Use this instead of <see cref="CredentialService"/> for values
        /// that are not secrets (toggles, port numbers, feature flags, etc.).
        /// </summary>
        public void SaveSetting(string key, string value)
        {
            lock (s_settingsLock)
            {
                _settingsCache ??= LoadSettingsFromDisk();
                _settingsCache[key] = value;
                SaveSettingsToDisk(_settingsCache);
            }
        }

        private Dictionary<string, string> LoadSettingsFromDisk()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                    return new Dictionary<string, string>();

                var json = File.ReadAllText(_settingsPath, Encoding.UTF8);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                    ?? new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                LogMessage($"LoadSettings error: {ex.Message}");
                return new Dictionary<string, string>();
            }
        }

        private void SaveSettingsToDisk(Dictionary<string, string> settings)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                LogMessage($"SaveSettings error: {ex.Message}");
            }
        }

        // ── Encrypted Secret Storage (DPAPI) ────────────────────────

        /// <summary>
        /// Saves a secret value encrypted with DPAPI to a local file.
        /// This replaces Windows Credential Manager storage and has no
        /// per-user size limit while providing equivalent security.
        /// </summary>
        public void SaveSecret(string key, string value)
        {
            var encrypted = DpapiEncrypt(value);
            lock (s_secretsLock)
            {
                _secretsCache ??= LoadSecretsFromDisk();
                _secretsCache[key] = encrypted;
                SaveSecretsToDisk(_secretsCache);
            }
        }

        /// <summary>
        /// Loads and decrypts a secret value from the local encrypted store.
        /// Returns <c>null</c> when the key does not exist.
        /// </summary>
        public string? LoadSecret(string key)
        {
            lock (s_secretsLock)
            {
                _secretsCache ??= LoadSecretsFromDisk();
                if (!_secretsCache.TryGetValue(key, out var encrypted))
                    return null;

                try
                {
                    return DpapiDecrypt(encrypted);
                }
                catch (Exception ex)
                {
                    LogMessage($"LoadSecret decryption error for key '{key}': {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// Removes a secret from the local encrypted store.
        /// </summary>
        public void DeleteSecret(string key)
        {
            lock (s_secretsLock)
            {
                _secretsCache ??= LoadSecretsFromDisk();
                if (_secretsCache.Remove(key))
                    SaveSecretsToDisk(_secretsCache);
            }
        }

        private Dictionary<string, string> LoadSecretsFromDisk()
        {
            try
            {
                if (!File.Exists(_secretsPath))
                    return new Dictionary<string, string>();

                var json = File.ReadAllText(_secretsPath, Encoding.UTF8);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                    ?? new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                LogMessage($"LoadSecrets error: {ex.Message}");
                return new Dictionary<string, string>();
            }
        }

        private void SaveSecretsToDisk(Dictionary<string, string> secrets)
        {
            try
            {
                var json = JsonSerializer.Serialize(secrets, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_secretsPath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                LogMessage($"SaveSecrets error: {ex.Message}");
                throw;
            }
        }

        // ── Win32 DPAPI (user-scoped, synchronous) ───────────────────────
        // Uses CryptProtectData/CryptUnprotectData directly instead of the WinRT
        // DataProtectionProvider async API to avoid Task.Run().GetAwaiter().GetResult()
        // blocking the calling thread and the associated deadlock risk under STA contexts.
        // Projects.json still uses the WinRT async API correctly (awaited in async methods).

        [StructLayout(LayoutKind.Sequential)]
        private struct DATA_BLOB
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptProtectData(
            ref DATA_BLOB pDataIn,
            [MarshalAs(UnmanagedType.LPWStr)] string? szDataDescr,
            IntPtr pOptionalEntropy,
            IntPtr pvReserved,
            IntPtr pPromptStruct,
            uint dwFlags,
            out DATA_BLOB pDataOut);

        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptUnprotectData(
            ref DATA_BLOB pDataIn,
            [MarshalAs(UnmanagedType.LPWStr)] out string? pszDataDescr,
            IntPtr pOptionalEntropy,
            IntPtr pvReserved,
            IntPtr pPromptStruct,
            uint dwFlags,
            out DATA_BLOB pDataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        private static string DpapiEncrypt(string plainText)
        {
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var handle = GCHandle.Alloc(plainBytes, GCHandleType.Pinned);
            try
            {
                var inBlob = new DATA_BLOB { cbData = plainBytes.Length, pbData = handle.AddrOfPinnedObject() };
                if (!CryptProtectData(ref inBlob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var outBlob))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "DPAPI encryption failed");
                try
                {
                    var result = new byte[outBlob.cbData];
                    Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
                    return Convert.ToBase64String(result);
                }
                finally
                {
                    LocalFree(outBlob.pbData);
                }
            }
            finally
            {
                handle.Free();
                Array.Clear(plainBytes);
            }
        }

        private static string DpapiDecrypt(string encryptedBase64)
        {
            var encryptedBytes = Convert.FromBase64String(encryptedBase64);
            var handle = GCHandle.Alloc(encryptedBytes, GCHandleType.Pinned);
            try
            {
                var inBlob = new DATA_BLOB { cbData = encryptedBytes.Length, pbData = handle.AddrOfPinnedObject() };
                if (!CryptUnprotectData(ref inBlob, out _, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var outBlob))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "DPAPI decryption failed");
                try
                {
                    var decryptedBytes = new byte[outBlob.cbData];
                    Marshal.Copy(outBlob.pbData, decryptedBytes, 0, outBlob.cbData);
                    try
                    {
                        return Encoding.UTF8.GetString(decryptedBytes);
                    }
                    finally
                    {
                        Array.Clear(decryptedBytes);
                    }
                }
                finally
                {
                    LocalFree(outBlob.pbData);
                }
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>
        /// Serialises a single project to a plain (unencrypted) JSON file so it can
        /// be shared with teammates.  Credentials are never included — they live in
        /// the local encrypted store and must be re-entered on the receiving machine.
        /// </summary>
        public async Task ExportProjectAsync(Project project, string filePath)
        {
            string json;
            try
            {
                json = JsonSerializer.Serialize(project, _jsonContext.Project);
            }
            catch (Exception ex)
            {
                LogMessage($"ExportProjectAsync context serialization failed: {ex.Message}. Trying default serializer...");
                var options = new JsonSerializerOptions { WriteIndented = true };
                json = JsonSerializer.Serialize(project, options);
            }
            await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
            LogMessage($"Project '{project.Name}' exported to {filePath}");
        }

        /// <summary>
        /// Deserialises a project from a plain JSON file previously created by
        /// <see cref="ExportProjectAsync"/>.  Returns <c>null</c> if the file cannot
        /// be parsed.
        /// </summary>
        public async Task<Project?> ImportProjectFromJsonAsync(string filePath)
        {
            var json = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            try
            {
                return JsonSerializer.Deserialize<Project>(json, _jsonContext.Project);
            }
            catch (Exception ex)
            {
                LogMessage($"ImportProjectFromJsonAsync context deserialization failed: {ex.Message}. Trying default deserializer...");
                var options = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<Project>(json, options);
            }
        }
    }
}