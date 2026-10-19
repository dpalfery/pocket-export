using System.Buffers.Binary;
using System.Security.Cryptography;
using FluentAssertions;
using PocketExport.Core.Metadata;
using Xunit;

namespace PocketExport.Core.Tests.Metadata;

/// <summary>
///     Unit and contract tests specifying container tagging behavior for MP3 (ID3) and M4A/MP4 containers.
/// </summary>
public sealed class AudioContainerTaggerTests : IDisposable
{
    private readonly string _tempDirectory;

    public AudioContainerTaggerTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "pocket-export-tagger-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }

    [Fact]
    public async Task TagAudioFileAsync_WithMp3AudioFile_WritesId3ContainerMetadataAndPreservesAudioStreamChecksum()
    {
        // Arrange
        var mp3Path = Path.Combine(_tempDirectory, "test_audio.mp3");
        CreateSampleMp3File(mp3Path);

        var expectedAudioChecksum = ComputeInvariantAudioChecksum(mp3Path);

        IAudioContainerTagger sut = new AudioContainerTagger();
        const string title = "Weekly Strategy Sync";
        var artists = new List<string> { "Alice Smith", "Bob Jones" };
        const string album = "Executive Planning";
        const string comment = "Pocket AI recording transcript tagged";

        // Act
        await sut.TagAudioFileAsync(mp3Path, title, artists, album, comment);

        // Assert
        using (var taggedFile = TagLib.File.Create(mp3Path))
        {
            taggedFile.Tag.Title.Should().Be(title);
            taggedFile.Tag.Performers.Should().BeEquivalentTo(artists);
            taggedFile.Tag.Album.Should().Be(album);
            taggedFile.Tag.Comment.Should().Be(comment);
        }

        var actualAudioChecksum = ComputeInvariantAudioChecksum(mp3Path);
        actualAudioChecksum.Should().Equal(expectedAudioChecksum);
    }

    [Fact]
    public async Task TagAudioFileAsync_WithM4aAudioFile_WritesMp4ContainerMetadataAndPreservesAudioStreamChecksum()
    {
        // Arrange
        var m4aPath = Path.Combine(_tempDirectory, "test_audio.m4a");
        CreateSampleM4aFile(m4aPath);

        var expectedAudioChecksum = ComputeInvariantAudioChecksum(m4aPath);

        IAudioContainerTagger sut = new AudioContainerTagger();
        const string title = "Customer Discovery Call";
        var artists = new List<string> { "Charlie Brown" };
        const string album = "Customer Interviews";
        const string comment = "Pocket export tagged M4A";

        // Act
        await sut.TagAudioFileAsync(m4aPath, title, artists, album, comment);

        // Assert
        using (var taggedFile = TagLib.File.Create(m4aPath))
        {
            taggedFile.Tag.Title.Should().Be(title);
            taggedFile.Tag.Performers.Should().BeEquivalentTo(artists);
            taggedFile.Tag.Album.Should().Be(album);
            taggedFile.Tag.Comment.Should().Be(comment);
        }

        var actualAudioChecksum = ComputeInvariantAudioChecksum(m4aPath);
        actualAudioChecksum.Should().Equal(expectedAudioChecksum);
    }

    [Fact]
    public async Task TagAudioFileAsync_WhenFileDoesNotExist_ThrowsFileNotFoundException()
    {
        // Arrange
        IAudioContainerTagger sut = new AudioContainerTagger();
        var nonexistentPath = Path.Combine(_tempDirectory, "does_not_exist.mp3");

        // Act
        var act = () => sut.TagAudioFileAsync(nonexistentPath, "Title", ["Artist"], "Album", "Comment");

        // Assert
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    private static byte[] ComputeInvariantAudioChecksum(string filePath)
    {
        using var tagFile = TagLib.File.Create(filePath);
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);

        var start = tagFile.InvariantStartPosition;
        var end = tagFile.InvariantEndPosition;
        var length = end - start;

        fs.Seek(start, SeekOrigin.Begin);
        var buffer = new byte[length];
        fs.ReadExactly(buffer, 0, (int)length);

        return MD5.HashData(buffer);
    }

    private static void CreateSampleMp3File(string filePath)
    {
        // 5 MPEG-1 Layer 3 frames, 44.1kHz, 128kbps, padded (418 bytes per frame)
        // Frame header: 0xFF, 0xFB, 0x90, 0x64
        var frameHeader = new byte[] { 0xFF, 0xFB, 0x90, 0x64 };
        var frameData = new byte[418 - 4];
        for (var i = 0; i < frameData.Length; i++)
        {
            frameData[i] = (byte)(0x55 ^ (i & 0xFF));
        }

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        for (var frame = 0; frame < 5; frame++)
        {
            fs.Write(frameHeader, 0, frameHeader.Length);
            fs.Write(frameData, 0, frameData.Length);
        }
    }

    private static void CreateSampleM4aFile(string filePath)
    {
        using var ms = new MemoryStream();

        // 1. ftyp box
        var ftypData = "M4A \0\0\0\0M4A mp42"u8.ToArray();
        WriteBox(ms, "ftyp"u8, ftypData);

        // 2. mvhd box inside moov
        using var mvhdStream = new MemoryStream();
        mvhdStream.Write(new byte[4]); // version & flags
        mvhdStream.Write(new byte[8]); // creation & modification time
        WriteUInt32BigEndian(mvhdStream, 1000); // timescale
        WriteUInt32BigEndian(mvhdStream, 1000); // duration
        WriteInt32BigEndian(mvhdStream, 0x00010000); // rate 1.0
        WriteInt16BigEndian(mvhdStream, 0x0100); // volume 1.0
        mvhdStream.Write(new byte[10]); // reserved
        // unity matrix
        WriteInt32BigEndian(mvhdStream, 0x00010000);
        mvhdStream.Write(new byte[12]);
        WriteInt32BigEndian(mvhdStream, 0x00010000);
        mvhdStream.Write(new byte[12]);
        WriteInt32BigEndian(mvhdStream, 0x40000000);
        mvhdStream.Write(new byte[24]); // pre-defined
        WriteUInt32BigEndian(mvhdStream, 2); // next track ID

        using var moovStream = new MemoryStream();
        WriteBox(moovStream, "mvhd"u8, mvhdStream.ToArray());
        WriteBox(ms, "moov"u8, moovStream.ToArray());

        // 3. mdat box containing audio payload
        var rawAudio = new byte[1024];
        for (var i = 0; i < rawAudio.Length; i++)
        {
            rawAudio[i] = (byte)(0xAA ^ (i & 0xFF));
        }
        WriteBox(ms, "mdat"u8, rawAudio);

        File.WriteAllBytes(filePath, ms.ToArray());
    }

    private static void WriteBox(Stream stream, ReadOnlySpan<byte> boxType, ReadOnlySpan<byte> data)
    {
        var boxSize = (uint)(8 + data.Length);
        Span<byte> sizeHeader = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(sizeHeader, boxSize);
        stream.Write(sizeHeader);
        stream.Write(boxType);
        stream.Write(data);
    }

    private static void WriteUInt32BigEndian(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteInt32BigEndian(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteInt16BigEndian(Stream stream, short value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(buffer, value);
        stream.Write(buffer);
    }
}
