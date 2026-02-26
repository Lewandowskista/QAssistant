using System;
using System.IO;
using System.Threading.Tasks;
using QAssistant.Models;
using Windows.Storage;
using Windows.System;

namespace QAssistant.Services
{
    public class FileStorageService
    {
        private readonly string _filesFolder;

        public FileStorageService()
        {
            _filesFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "QAssistant", "Files");
            Directory.CreateDirectory(_filesFolder);
        }

        public async Task<FileAttachment?> SaveFileAsync(string sourcePath, AttachmentScope scope, Guid? noteId = null)
        {
            try
            {
                var fileName = Path.GetFileName(sourcePath);
                var ext = Path.GetExtension(sourcePath).ToLower();
                var uniqueName = $"{Guid.NewGuid()}{ext}";
                var destPath = Path.Combine(_filesFolder, uniqueName);

                await Task.Run(() => File.Copy(sourcePath, destPath, true));

                return new FileAttachment
                {
                    FileName = fileName,
                    FilePath = destPath,
                    MimeType = GetMimeType(ext),
                    FileSizeBytes = new FileInfo(destPath).Length,
                    Scope = scope,
                    NoteId = noteId
                };
            }
            catch { return null; }
        }

        public async Task<FileAttachment?> SaveBytesAsync(byte[] bytes, string fileName, AttachmentScope scope, Guid? noteId = null)
        {
            try
            {
                var ext = Path.GetExtension(fileName).ToLower();
                var uniqueName = $"{Guid.NewGuid()}{ext}";
                var destPath = Path.Combine(_filesFolder, uniqueName);

                await File.WriteAllBytesAsync(destPath, bytes);

                return new FileAttachment
                {
                    FileName = fileName,
                    FilePath = destPath,
                    MimeType = GetMimeType(ext),
                    FileSizeBytes = bytes.Length,
                    Scope = scope,
                    NoteId = noteId
                };
            }
            catch { return null; }
        }

        public void DeleteFile(FileAttachment attachment)
        {
            try
            {
                if (File.Exists(attachment.FilePath))
                    File.Delete(attachment.FilePath);
            }
            catch { }
        }

        public async Task OpenFileAsync(FileAttachment attachment)
        {
            try
            {
                if (!File.Exists(attachment.FilePath)) return;
                await Launcher.LaunchUriAsync(new Uri("file:///" + attachment.FilePath.Replace("\\", "/")));
            }
            catch { }
        }

        private static string GetMimeType(string ext) => ext switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".avi" => "video/x-msvideo",
            ".mov" => "video/quicktime",
            ".mkv" => "video/x-matroska",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            ".flac" => "audio/flac",
            ".m4a" => "audio/mp4",
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".txt" => "text/plain",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };
    }
}