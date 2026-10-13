using System.Text.Json;
using System.Text.Json.Serialization;
using ExtSieve.Core.Models;

namespace ExtSieve.App.LocalData;

public static class ProfileExchangeJson
{
    public const int MaximumBytes = 4 * 1024 * 1024;

    public static async Task<IReadOnlyList<ExtensionProfile>> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (stream.CanSeek && stream.Length - stream.Position > MaximumBytes) throw new InvalidDataException("Profile document is too large.");
        using var bounded = new BoundedReadStream(stream);
        var document = await JsonSerializer.DeserializeAsync<ProfileDocument>(bounded, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (document?.SchemaVersion != 1 || document.Profiles is null) throw new InvalidDataException("Unsupported profile document.");
        var profiles = document.Profiles.Select(item => item?.Name is null || item.Extensions is null
            ? throw new InvalidDataException("Invalid profile.") : ExtensionProfile.Create(item.Name, item.Extensions)).ToArray();
        return JsonProfileStore.Normalize(profiles);
    }

    public static Task WriteAsync(string path, ExtensionProfile profile, CancellationToken cancellationToken = default)
    {
        var normalized = ExtensionProfile.Create(profile.Name, profile.ExtensionKeys);
        return new AtomicJsonFile<ProfileDocument>(path).SaveAsync(new ProfileDocument(1,
            [new ProfileData(normalized.Name, normalized.ExtensionKeys)]), cancellationToken);
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record ProfileDocument(
        [property: JsonPropertyName("schemaVersion")] int? SchemaVersion,
        [property: JsonPropertyName("profiles")] IReadOnlyList<ProfileData?>? Profiles);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record ProfileData(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("extensions")] IReadOnlyList<string>? Extensions);

    private sealed class BoundedReadStream(Stream inner) : Stream
    {
        private long _read;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));
        private int Count(int count)
        {
            _read += count;
            if (_read > MaximumBytes) throw new InvalidDataException("Profile document is too large.");
            return count;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
