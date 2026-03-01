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
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace QAssistant.Services
{
    public class CredentialService
    {
        public static void SaveCredential(string key, string value)
        {
            ArgumentException.ThrowIfNullOrEmpty(key);
            ArgumentNullException.ThrowIfNull(value);

            var byteArray = Encoding.UTF8.GetBytes(value);
            var credentialBlob = Marshal.AllocHGlobal(byteArray.Length);
            try
            {
                Marshal.Copy(byteArray, 0, credentialBlob, byteArray.Length);

                var cred = new CREDENTIAL
                {
                    Type = 1,
                    TargetName = "QAssistant_" + key,
                    CredentialBlob = credentialBlob,
                    CredentialBlobSize = (uint)byteArray.Length,
                    Persist = 2,
                    UserName = "QAssistant"
                };

                if (!CredWrite(ref cred, 0))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                Array.Clear(byteArray);
                Marshal.FreeHGlobal(credentialBlob);
            }
        }

        public static string? LoadCredential(string key)
        {
            ArgumentException.ThrowIfNullOrEmpty(key);

            if (!CredRead("QAssistant_" + key, 1, 0, out IntPtr credPtr))
                return null;

            try
            {
                var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
                if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0)
                    return null;

                var bytes = new byte[cred.CredentialBlobSize];
                Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
                try
                {
                    return Encoding.UTF8.GetString(bytes);
                }
                finally
                {
                    Array.Clear(bytes);
                }
            }
            finally
            {
                CredFree(credPtr);
            }
        }

        public static void DeleteCredential(string key)
        {
            ArgumentException.ThrowIfNullOrEmpty(key);

            if (!CredDelete("QAssistant_" + key, 1, 0))
            {
                int error = Marshal.GetLastWin32Error();
                if (error != 1168) // ERROR_NOT_FOUND — credential doesn't exist
                    throw new Win32Exception(error);
            }
        }

        /// <summary>
        /// Save a credential scoped to a specific project.
        /// </summary>
        public static void SaveProjectCredential(Guid projectId, string key, string value)
        {
            SaveCredential($"{projectId}_{key}", value);
        }

        /// <summary>
        /// Load a credential scoped to a specific project.
        /// Returns null when the project has no value saved for this key.
        /// </summary>
        public static string? LoadProjectCredential(Guid projectId, string key)
        {
            return LoadCredential($"{projectId}_{key}");
        }

        /// <summary>
        /// Delete a project-scoped credential.
        /// </summary>
        public static void DeleteProjectCredential(Guid projectId, string key)
        {
            DeleteCredential($"{projectId}_{key}");
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CredWrite([In] ref CREDENTIAL userCredential, uint flags);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credentialPtr);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern void CredFree(IntPtr buffer);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CredDelete(string target, uint type, uint flags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public uint Flags;
            public uint Type;
            [MarshalAs(UnmanagedType.LPWStr)] public string? TargetName;
            [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
            [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
        }
    }
}