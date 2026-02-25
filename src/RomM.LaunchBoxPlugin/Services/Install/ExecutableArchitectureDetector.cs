using System;
using System.IO;

namespace RomMbox.Services.Install
{
    /// <summary>
    /// Detects executable architecture from PE headers.
    /// </summary>
    internal static class ExecutableArchitectureDetector
    {
        public static ExecutableArchitecture GetArchitecture(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return ExecutableArchitecture.Unknown;
            }

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new BinaryReader(stream);

                if (stream.Length < 64)
                {
                    return ExecutableArchitecture.Unknown;
                }

                var mz = reader.ReadUInt16();
                if (mz != 0x5A4D)
                {
                    return ExecutableArchitecture.Unknown;
                }

                stream.Seek(0x3C, SeekOrigin.Begin);
                var peOffset = reader.ReadInt32();
                if (peOffset <= 0 || peOffset > stream.Length - 6)
                {
                    return ExecutableArchitecture.Unknown;
                }

                stream.Seek(peOffset, SeekOrigin.Begin);
                var peSignature = reader.ReadUInt32();
                if (peSignature != 0x00004550)
                {
                    return ExecutableArchitecture.Unknown;
                }

                var machine = reader.ReadUInt16();
                return machine switch
                {
                    0x014C => ExecutableArchitecture.X86,
                    0x8664 => ExecutableArchitecture.X64,
                    0xAA64 => ExecutableArchitecture.Arm64,
                    _ => ExecutableArchitecture.Unknown
                };
            }
            catch
            {
                return ExecutableArchitecture.Unknown;
            }
        }

        public static int GetPreferencePenalty(ExecutableArchitecture architecture)
        {
            var is64BitOs = Environment.Is64BitOperatingSystem;
            if (is64BitOs)
            {
                return architecture switch
                {
                    ExecutableArchitecture.X64 => 0,
                    ExecutableArchitecture.Arm64 => 0,
                    ExecutableArchitecture.X86 => 1,
                    _ => 2
                };
            }

            return architecture switch
            {
                ExecutableArchitecture.X86 => 0,
                ExecutableArchitecture.X64 => 1,
                ExecutableArchitecture.Arm64 => 1,
                _ => 2
            };
        }

        public static string GetDisplayName(ExecutableArchitecture architecture)
        {
            return architecture switch
            {
                ExecutableArchitecture.X86 => "x86",
                ExecutableArchitecture.X64 => "x64",
                ExecutableArchitecture.Arm64 => "ARM64",
                _ => "Unknown"
            };
        }
    }

    public enum ExecutableArchitecture
    {
        Unknown = 0,
        X86 = 1,
        X64 = 2,
        Arm64 = 3
    }
}
