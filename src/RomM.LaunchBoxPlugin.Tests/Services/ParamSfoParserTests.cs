using System;
using System.IO;
using System.Text;
using FluentAssertions;
using RomM.Platforms.PS3.Inspection;
using RomMbox.Tests.Utilities;

namespace RomMbox.Tests.Services
{
    public sealed class ParamSfoParserTests
    {
        [Fact]
        public void Parses_TitleId_And_Title()
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.Path, "PARAM.SFO");
            WriteMinimalSfo(path, null, null, new[] { ("TITLE_ID", "BLUS12345"), ("TITLE", "Demo") });

            var parser = new ParamSfoParser();
            var data = parser.Parse(path);

            data.GetString("TITLE_ID").Should().Be("BLUS12345");
            data.GetString("TITLE").Should().Be("Demo");
        }

        [Fact]
        public void Parses_Reference_ParamSfo_File()
        {
            var path = ResolveReferencePath("PARAM.SFO");
            var parser = new ParamSfoParser();
            var data = parser.Parse(path);

            data.GetString("TITLE_ID").Should().Be("BLUS31054");
            data.GetString("TITLE").Should().Be("Angry Birds Trilogy");
            data.GetString("APP_VER").Should().Be("01.00");
            data.GetString("VERSION").Should().Be("01.00");
            data.GetString("CATEGORY").Should().Be("DG");
            data.GetString("PS3_SYSTEM_VER").Should().Be("04.2100");
            data.GetString("PARENTAL_LEVEL").Should().Be("0x00000003");
            data.GetString("RESOLUTION").Should().Be("0x0000003F");
            data.GetString("SOUND_FORMAT").Should().Be("0x00000307");
        }

        [Fact]
        public void Handles_Malformed_Header()
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.Path, "PARAM.SFO");
            File.WriteAllText(path, "BAD");

            var parser = new ParamSfoParser();
            Action act = () => parser.Parse(path);
            act.Should().Throw<InvalidDataException>();
        }

        [Fact]
        public void Throws_For_Unsupported_Format()
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.Path, "PARAM.SFO");
            WriteMinimalSfo(path, null, 0x9999, new[] { ("TITLE_ID", "BLUS12345") });

            var parser = new ParamSfoParser();
            Action act = () => parser.Parse(path);
            act.Should().Throw<InvalidDataException>().WithMessage("*Unsupported PARAM.SFO value format*");
        }

        [Fact]
        public void Parses_TitleId_When_Header_Is_Offset()
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.Path, "PARAM.SFO");
            var prefix = new byte[] { 0x00, 0x00, 0x00, 0x00 };
            WriteMinimalSfo(path, prefix, null, new[] { ("TITLE_ID", "BLUS31054") });

            var parser = new ParamSfoParser();
            var data = parser.Parse(path);

            data.GetString("TITLE_ID").Should().Be("BLUS31054");
        }

        private static void WriteMinimalSfo(
            string path,
            byte[]? prependBytes = null,
            ushort? formatOverride = null,
            (string Key, string Value)[]? entries = null)
        {
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

            entries ??= Array.Empty<(string Key, string Value)>();
            var keyTable = new MemoryStream();
            var dataTable = new MemoryStream();
            var entryList = new MemoryStream();
            foreach (var (key, value) in entries)
            {
                var keyOffset = (ushort)keyTable.Position;
                var keyBytes = Encoding.UTF8.GetBytes(key + "\0");
                keyTable.Write(keyBytes, 0, keyBytes.Length);

                var dataOffset = (uint)dataTable.Position;
                var valueBytes = Encoding.UTF8.GetBytes(value + "\0");
                dataTable.Write(valueBytes, 0, valueBytes.Length);

                entryList.Write(BitConverter.GetBytes(keyOffset), 0, 2);
                var format = formatOverride ?? (ushort)0x0204;
                entryList.Write(BitConverter.GetBytes(format), 0, 2);
                entryList.Write(BitConverter.GetBytes((uint)valueBytes.Length), 0, 4);
                entryList.Write(BitConverter.GetBytes((uint)valueBytes.Length), 0, 4);
                entryList.Write(BitConverter.GetBytes(dataOffset), 0, 4);
            }

            var headerSize = 20u;
            var entrySize = 16u;
            var keyTableStart = headerSize + entrySize * (uint)entries.Length;
            var dataTableStart = keyTableStart + (uint)keyTable.Length;
            if (prependBytes != null && prependBytes.Length > 0)
            {
                writer.Write(prependBytes);
            }

            writer.Write(new byte[] { 0x50, 0x53, 0x46, 0x00 });
            writer.Write((uint)0x00000101);
            writer.Write(keyTableStart);
            writer.Write(dataTableStart);
            writer.Write((uint)entries.Length);

            entryList.Position = 0;
            entryList.CopyTo(stream);
            keyTable.Position = 0;
            keyTable.CopyTo(stream);
            dataTable.Position = 0;
            dataTable.CopyTo(stream);
        }

        private static string ResolveReferencePath(string fileName)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "api_reference", fileName),
                Path.Combine(baseDir, "..", "..", "..", "..", "..", "api_reference", fileName),
                Path.Combine(baseDir, "..", "..", "..", "..", "..", "..", "api_reference", fileName)
            };

            foreach (var candidate in candidates)
            {
                var full = Path.GetFullPath(candidate);
                if (File.Exists(full))
                {
                    return full;
                }
            }

            return Path.Combine("api_reference", fileName);
        }
    }
}
