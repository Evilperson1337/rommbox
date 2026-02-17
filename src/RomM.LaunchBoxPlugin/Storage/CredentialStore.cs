using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using RomMbox.Services.Logging;

namespace RomMbox.Storage
{
    /// <summary>
    /// Stores credentials using Windows Credential Manager.
    /// </summary>
    internal sealed class CredentialStore
    {
        private readonly LoggingService _logger;
        private const int CredentialTypeGeneric = 1;
        private const int CredentialPersistLocalMachine = 2;

        /// <summary>
        /// Creates a credential store with an optional logger.
        /// </summary>
        /// <param name="logger">Logger for diagnostics.</param>
        public CredentialStore(LoggingService logger = null)
        {
            _logger = logger;
        }

        /// <summary>
        /// Saves credentials for the specified server URL.
        /// </summary>
        /// <param name="serverUrl">The server URL used to scope credentials.</param>
        /// <param name="username">The username.</param>
        /// <param name="password">The password.</param>
        public void SaveCredentials(string serverUrl, string username, string password)
        {
            _logger?.Debug($"CredentialStore.SaveCredentials - ServerUrl: {LoggingService.SanitizeUrl(serverUrl)}, Username: <redacted>, Password length: {password?.Length ?? 0}");
            
            if (serverUrl == null)
            {
                throw new ArgumentNullException(nameof(serverUrl));
            }

            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                throw new ArgumentException("Server URL must not be empty.", nameof(serverUrl));
            }

            var target = BuildTargetName(serverUrl);
            var secret = password ?? string.Empty;
            var secretBytes = Encoding.Unicode.GetBytes(secret);
            _logger?.Debug($"Built target name for credential store. Secret length: {secretBytes.Length}");

            var credential = new NativeCredential
            {
                AttributeCount = 0,
                Attributes = IntPtr.Zero,
                Comment = null,
                TargetAlias = null,
                Type = CredentialTypeGeneric,
                Persist = CredentialPersistLocalMachine,
                TargetName = target,
                UserName = username ?? string.Empty,
                CredentialBlobSize = (uint)secretBytes.Length,
                CredentialBlob = Marshal.StringToCoTaskMemUni(secret)
            };

            try
            {
                if (!CredWrite(ref credential, 0))
                {
                    var error = Marshal.GetLastWin32Error();
                    _logger?.Error($"CredWrite failed with error code: {error}");
                    throw new Win32Exception(error);
                }
                _logger?.Debug("Credentials saved successfully using Windows Credential Manager");
            }
            finally
            {
                if (credential.CredentialBlob != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(credential.CredentialBlob);
                }
            }
        }

        /// <summary>
        /// Retrieves saved credentials for the specified server URL.
        /// </summary>
        /// <param name="serverUrl">The server URL used to scope credentials.</param>
        /// <returns>The credentials, or null if not found.</returns>
        public CredentialResult GetCredentials(string serverUrl)
        {
            _logger?.Debug($"CredentialStore.GetCredentials - ServerUrl: {LoggingService.SanitizeUrl(serverUrl)}");
            
            if (serverUrl == null)
            {
                throw new ArgumentNullException(nameof(serverUrl));
            }

            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                throw new ArgumentException("Server URL must not be empty.", nameof(serverUrl));
            }

            var target = BuildTargetName(serverUrl);
            _logger?.Debug("Looking for credentials with target name.");
            
            if (!CredRead(target, CredentialTypeGeneric, 0, out var credentialPtr))
            {
                _logger?.Debug("CredRead returned false, no credentials found for target.");
                return null;
            }

            try
            {
                var credential = Marshal.PtrToStructure<NativeCredential>(credentialPtr);
                var username = credential.UserName ?? string.Empty;
                var password = string.Empty;
                if (credential.CredentialBlob != IntPtr.Zero && credential.CredentialBlobSize > 0)
                {
                    password = Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2) ?? string.Empty;
                }

                _logger?.Debug($"Retrieved credentials - Username: <redacted>, Password length: {password.Length}");
                return new CredentialResult(username, password);
            }
            finally
            {
                CredFree(credentialPtr);
            }
        }

        /// <summary>
        /// Deletes saved credentials for the specified server URL.
        /// </summary>
        /// <param name="serverUrl">The server URL used to scope credentials.</param>
        public void DeleteCredentials(string serverUrl)
        {
            if (serverUrl == null)
            {
                throw new ArgumentNullException(nameof(serverUrl));
            }

            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                throw new ArgumentException("Server URL must not be empty.", nameof(serverUrl));
            }

            var target = BuildTargetName(serverUrl);
            CredDelete(target, CredentialTypeGeneric, 0);
        }

        /// <summary>
        /// Builds the target name used by the Windows credential store.
        /// </summary>
        /// <param name="serverUrl">The server URL.</param>
        /// <returns>The composed target name.</returns>
        private static string BuildTargetName(string serverUrl)
        {
            return "RomM_LaunchBoxPlugin_" + serverUrl.Trim();
        }

        /// <summary>
        /// Native credential structure for Windows API interop.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeCredential
        {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        /// <summary>
        /// P/Invoke to write a credential entry.
        /// </summary>
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CredWrite([In] ref NativeCredential userCredential, [In] uint flags);

        /// <summary>
        /// P/Invoke to read a credential entry.
        /// </summary>
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

        /// <summary>
        /// P/Invoke to delete a credential entry.
        /// </summary>
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CredDelete(string target, int type, int flags);

        /// <summary>
        /// P/Invoke to free credential memory.
        /// </summary>
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern void CredFree([In] IntPtr buffer);
    }

    /// <summary>
    /// Represents retrieved credentials from the store.
    /// </summary>
    internal sealed class CredentialResult
    {
        /// <summary>
        /// Creates a credential result with the given values.
        /// </summary>
        /// <param name="username">The username.</param>
        /// <param name="password">The password.</param>
        public CredentialResult(string username, string password)
        {
            Username = username;
            Password = password;
        }

        /// <summary>
        /// Gets the stored username.
        /// </summary>
        public string Username { get; }
        /// <summary>
        /// Gets the stored password.
        /// </summary>
        public string Password { get; }
    }
}
