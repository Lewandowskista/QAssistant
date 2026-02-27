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
        public bool IsVideo => MimeType.StartsWith("video/");
        public bool IsAudio => MimeType.StartsWith("audio/");
        public bool IsMedia => IsImage || IsVideo || IsAudio;

        public string FileSizeDisplay => FileSizeBytes < 1024 * 1024
            ? $"{FileSizeBytes / 1024} KB"
            : $"{FileSizeBytes / (1024 * 1024)} MB";
    }
}