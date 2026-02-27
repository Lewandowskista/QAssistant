using System;
using System.Runtime.InteropServices;
using System.Text;

namespace QAssistant.Services
{
    public class CredentialService
    {
        public static void SaveCredential(string key, string value)
        {
            var byteArray = Encoding.UTF8.GetBytes(value);
            IntPtr targetName = IntPtr.Zero;
            IntPtr credentialBlob = IntPtr.Zero;
            IntPtr userName = IntPtr.Zero;
            IntPtr credPtr = IntPtr.Zero;

            try
            {
                targetName = Marshal.StringToCoTaskMemUni("QAssistant_" + key);
                credentialBlob = Marshal.AllocCoTaskMem(byteArray.Length);
                userName = Marshal.StringToCoTaskMemUni("QAssistant");

                Marshal.Copy(byteArray, 0, credentialBlob, byteArray.Length);

                var cred = new CREDENTIAL
                {
                    Type = 1,
                    TargetName = targetName,
                    CredentialBlob = credentialBlob,
                    CredentialBlobSize = (uint)byteArray.Length,
                    Persist = 2,
                    UserName = userName
                };

                credPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf(cred));
                Marshal.StructureToPtr(cred, credPtr, false);
                CredWrite(credPtr, 0);
            }
            finally
            {
                if (credPtr != IntPtr.Zero) Marshal.FreeCoTaskMem(credPtr);
                if (credentialBlob != IntPtr.Zero) Marshal.FreeCoTaskMem(credentialBlob);
                if (targetName != IntPtr.Zero) Marshal.FreeCoTaskMem(targetName);
                if (userName != IntPtr.Zero) Marshal.FreeCoTaskMem(userName);
            }
        }

        public static string? LoadCredential(string key)
        {
            if (!CredRead("QAssistant_" + key, 1, 0, out IntPtr credPtr))
                return null;

            try
            {
                var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
                if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0)
                    return null;

                var bytes = new byte[cred.CredentialBlobSize];
                Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                CredFree(credPtr);
            }
        }

        public static void DeleteCredential(string key)
        {
            CredDelete("QAssistant_" + key, 1, 0);
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
        private static extern bool CredWrite(IntPtr Credential, uint Flags);

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
            public IntPtr TargetName;
            public IntPtr Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public IntPtr TargetAlias;
            public IntPtr UserName;
        }
    }
}