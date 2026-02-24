using System;

namespace QAssistant.Models
{
    public enum AttachmentScope { Project, Note }

    public class FileAttachment
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string MimeType { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public DateTime AddedAt { get; set; } = DateTime.Now;
        public AttachmentScope Scope { get; set; } = AttachmentScope.Project;
        public Guid? NoteId { get; set; }

        public bool IsImage => MimeType.StartsWith("image/");

        public string FileSizeDisplay => FileSizeBytes < 1024 * 1024
            ? $"{FileSizeBytes / 1024} KB"
            : $"{FileSizeBytes / (1024 * 1024)} MB";
    }
}