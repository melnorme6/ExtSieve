using System.Text.Json;

namespace ExtSieve.App.LocalData;

public sealed record LocalDataResult<T>(T Value, bool IsAvailable);

internal sealed class AtomicJsonFile<T>(string path) where T : class
{
    private const int MaximumLength = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path = Path.GetFullPath(path);

    public async Task<LocalDataResult<T>> LoadAsync(T empty, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > MaximumLength) return new(empty, false);
            var value = await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken).ConfigureAwait(false);
            return value is null ? new(empty, false) : new(value, true);
        }
        catch (FileNotFoundException) { return new(empty, true); }
        catch (DirectoryNotFoundException) { return new(empty, true); }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        { return new(empty, false); }
    }

    public async Task SaveAsync(T value, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken).ConfigureAwait(false);
                if (stream.Length > MaximumLength) throw new IOException("Local data exceeds the supported size.");
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }
}
