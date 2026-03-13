using System;
using System.IO;
using System.Text;

namespace RomM.Platforms.PS3.Inspection
{
    public sealed class ParamSfoParser
    {
        public ParamSfoData Parse(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("PARAM.SFO path missing.", nameof(path));
            }

            using var stream = File.OpenRead(path);
            var headerStart = FindHeaderStart(stream);
            if (headerStart < 0)
            {
                throw new InvalidDataException("Invalid PARAM.SFO magic header (PSF\\0 not found).");
            }

            stream.Seek(headerStart, SeekOrigin.Begin);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            var magic = reader.ReadBytes(4);
            var isPsf0 = magic.Length == 4 && magic[0] == 0x50 && magic[1] == 0x53 && magic[2] == 0x46 && magic[3] == 0x00;
            var is0Psf = magic.Length == 4 && magic[0] == 0x00 && magic[1] == 0x50 && magic[2] == 0x53 && magic[3] == 0x46;
            if (!isPsf0 && !is0Psf)
            {
                var magicText = BitConverter.ToString(magic);
                throw new InvalidDataException($"Invalid PARAM.SFO magic header (magic={magicText}).");
            }

            var version = reader.ReadUInt32();
            var keyTableStart = reader.ReadUInt32();
            var dataTableStart = reader.ReadUInt32();
            var entryCount = reader.ReadUInt32();
            if (entryCount > 2048)
            {
                throw new InvalidDataException($"PARAM.SFO entry count appears invalid (count={entryCount}).");
            }

            var entryTableSize = entryCount * 16;
            var entryTableAbsolute = headerStart + 20;
            var keyTableAbsolute = headerStart + keyTableStart;
            var dataTableAbsolute = headerStart + dataTableStart;
            var streamLength = stream.Length;
            if (entryTableAbsolute + entryTableSize > streamLength)
            {
                throw new InvalidDataException("PARAM.SFO entry table exceeds file bounds.");
            }
            if (keyTableAbsolute < entryTableAbsolute + entryTableSize)
            {
                throw new InvalidDataException($"PARAM.SFO key table offset overlaps entry table (keyTableOffset={keyTableStart}).");
            }
            if (keyTableAbsolute > streamLength || dataTableAbsolute > streamLength)
            {
                throw new InvalidDataException("PARAM.SFO table offsets fall outside file bounds.");
            }
            if (dataTableAbsolute < keyTableAbsolute)
            {
                throw new InvalidDataException("PARAM.SFO data table offset precedes key table.");
            }

            stream.Seek(entryTableAbsolute, SeekOrigin.Begin);
            var entries = new List<ParamSfoEntry>();
            for (var i = 0; i < entryCount; i++)
            {
                entries.Add(new ParamSfoEntry
                {
                    KeyOffset = reader.ReadUInt16(),
                    DataFormat = reader.ReadUInt16(),
                    DataLength = reader.ReadUInt32(),
                    DataMaxLength = reader.ReadUInt32(),
                    DataOffset = reader.ReadUInt32()
                });
            }

            var result = new ParamSfoData();
            foreach (var entry in entries)
            {
                var key = ReadNullTerminatedString(stream, (uint)(keyTableAbsolute + entry.KeyOffset));
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (entry.DataLength > entry.DataMaxLength)
                {
                    throw new InvalidDataException($"PARAM.SFO entry '{key}' length exceeds max length (len={entry.DataLength}, max={entry.DataMaxLength}).");
                }

                var dataAbsolute = dataTableAbsolute + entry.DataOffset;
                if (dataAbsolute + entry.DataLength > streamLength)
                {
                    throw new InvalidDataException($"PARAM.SFO entry '{key}' data exceeds file bounds.");
                }

                var valueBytes = ReadBytes(stream, (uint)dataAbsolute, entry.DataLength);
                var value = DecodeValue(valueBytes, entry.DataFormat, key);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result.Strings[key] = value;
                }
            }

            return result;
        }

        private static long FindHeaderStart(Stream stream)
        {
            if (stream == null || !stream.CanRead)
            {
                return -1;
            }

            stream.Seek(0, SeekOrigin.Begin);
            var length = Math.Min(256, stream.Length);
            if (length < 4)
            {
                return -1;
            }

            var buffer = new byte[length];
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read < 4)
            {
                return -1;
            }

            for (var i = 0; i <= read - 4; i++)
            {
                if (buffer[i] == 0x50
                    && buffer[i + 1] == 0x53
                    && buffer[i + 2] == 0x46
                    && buffer[i + 3] == 0x00)
                {
                    return i;
                }
            }

            for (var i = 0; i <= read - 4; i++)
            {
                if (buffer[i] == 0x00
                    && buffer[i + 1] == 0x50
                    && buffer[i + 2] == 0x53
                    && buffer[i + 3] == 0x46)
                {
                    return i;
                }
            }

            return -1;
        }

        private static string ReadNullTerminatedString(Stream stream, uint offset)
        {
            stream.Seek(offset, SeekOrigin.Begin);
            using var buffer = new MemoryStream();
            int next;
            while ((next = stream.ReadByte()) >= 0)
            {
                if (next == 0x00)
                {
                    break;
                }

                buffer.WriteByte((byte)next);
            }

            if (buffer.Length == 0)
            {
                return string.Empty;
            }

            return Encoding.UTF8.GetString(buffer.ToArray()).Trim();
        }

        private static byte[] ReadBytes(Stream stream, uint offset, uint length)
        {
            stream.Seek(offset, SeekOrigin.Begin);
            if (length == 0)
            {
                return Array.Empty<byte>();
            }

            var buffer = new byte[length];
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                return Array.Empty<byte>();
            }

            if (read == buffer.Length)
            {
                return buffer;
            }

            var trimmed = new byte[read];
            Array.Copy(buffer, trimmed, read);
            return trimmed;
        }

        private static string DecodeValue(byte[] bytes, ushort format, string key)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return string.Empty;
            }

            switch (format)
            {
                case 0x0204:
                case 0x0004:
                    return Encoding.UTF8.GetString(bytes).TrimEnd('\0').Trim();
                case 0x0404:
                case 0x0400:
                    if (bytes.Length < 4)
                    {
                        throw new InvalidDataException($"PARAM.SFO entry '{key}' integer value too short (len={bytes.Length}).");
                    }

                    var value = BitConverter.ToUInt32(bytes, 0);
                    return $"0x{value:X8}";
                default:
                    throw new InvalidDataException($"Unsupported PARAM.SFO value format 0x{format:X4} for '{key}'.");
            }
        }

        private sealed class ParamSfoEntry
        {
            public ushort KeyOffset { get; init; }
            public ushort DataFormat { get; init; }
            public uint DataLength { get; init; }
            public uint DataMaxLength { get; init; }
            public uint DataOffset { get; init; }
        }
    }
}
