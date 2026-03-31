using System.IO.Compression;
using System.Text;
using Xunit;

namespace LceWorldConverter.Tests;

public sealed class SaveDataContainerTests
{
    [Fact]
    public void Save_WritesFooterEntriesInPhysicalStartOffsetOrder()
    {
        string outputPath = Path.Combine(Path.GetTempPath(), $"savecontainer-order-{Guid.NewGuid():N}.ms");
        try
        {
            var container = new SaveDataContainer(7, 9);

            SaveFileEntry first = container.CreateFile("first.dat");
            SaveFileEntry second = container.CreateFile("second.dat");
            SaveFileEntry third = container.CreateFile("third.dat");

            container.WriteToFile(third, new byte[] { 0x33 });
            container.WriteToFile(first, new byte[] { 0x11, 0x11 });
            container.WriteToFile(second, new byte[] { 0x22, 0x22, 0x22 });

            container.Save(outputPath);

            byte[] rawBlob = ReadRawBlob(outputPath);
            int tableOffset = (int)BitConverter.ToUInt32(rawBlob, 0);
            int fileCount = (int)BitConverter.ToUInt32(rawBlob, 4);

            Assert.Equal(3, fileCount);

            var names = new List<string>();
            var offsets = new List<uint>();
            int pos = tableOffset;
            for (int i = 0; i < fileCount; i++)
            {
                names.Add(Encoding.Unicode.GetString(rawBlob, pos, 128).TrimEnd('\0'));
                pos += 128;
                pos += 4; // length
                offsets.Add(BitConverter.ToUInt32(rawBlob, pos));
                pos += 4;
                pos += 8; // last modified
            }

            Assert.Equal(new[] { "third.dat", "first.dat", "second.dat" }, names);
            Assert.Equal(new uint[] { 12, 13, 15 }, offsets);
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    private static byte[] ReadRawBlob(string savePath)
    {
        byte[] containerBytes = File.ReadAllBytes(savePath);
        Assert.True(containerBytes.Length >= 8);
        Assert.Equal(0, BitConverter.ToInt32(containerBytes, 0));

        using var compressed = new MemoryStream(containerBytes, 8, containerBytes.Length - 8);
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }
}
