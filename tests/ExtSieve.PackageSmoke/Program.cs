using System.IO.Compression;
using ExtSieve.Core.Models;
using ExtSieve.Core.Services;

namespace ExtSieve.PackageSmoke;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: ExtSieve.PackageSmoke WORK_DIRECTORY");
            return 2;
        }

        try
        {
            var workDirectory = Path.GetFullPath(args[0]);
            PrepareEmptyDirectory(workDirectory);

            var sourceDirectory = Path.Combine(workDirectory, "source");
            var nestedDirectory = Path.Combine(sourceDirectory, "nested");
            Directory.CreateDirectory(nestedDirectory);

            await File.WriteAllTextAsync(
                Path.Combine(sourceDirectory, "same.txt"),
                "root text\n");
            await File.WriteAllTextAsync(
                Path.Combine(nestedDirectory, "same.txt"),
                "nested text\n");
            await File.WriteAllBytesAsync(
                Path.Combine(nestedDirectory, "image.bin"),
                [0x00, 0x01, 0x7f, 0x80, 0xff]);

            var scan = await new FolderScanner().ScanAsync(
                sourceDirectory,
                discoveredFileProgress: null,
                CancellationToken.None);
            Ensure(scan.Warnings.Count == 0, "The package smoke scan returned warnings.");
            Ensure(scan.Files.Count == 3, "The package smoke scan did not find three files.");

            var selectedGroups = scan.Groups
                .Select(group => group.ExtensionKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var preserveArchive = Path.Combine(workDirectory, "preserve.zip");
            var preservePlan = new ArchivePlanner().CreatePlan(new ArchiveRequest(
                sourceDirectory,
                preserveArchive,
                selectedGroups,
                ArchiveMode.PreserveStructure,
                scan.Files));
            await new ZipArchiveService().CreateAsync(
                preserveArchive,
                preservePlan,
                replaceExistingDestination: false,
                progress: null,
                CancellationToken.None);
            ValidateArchive(
                preserveArchive,
                new Dictionary<string, byte[]>(StringComparer.Ordinal)
                {
                    ["nested/image.bin"] = [0x00, 0x01, 0x7f, 0x80, 0xff],
                    ["nested/same.txt"] = "nested text\n"u8.ToArray(),
                    ["same.txt"] = "root text\n"u8.ToArray(),
                });

            var flatArchive = Path.Combine(workDirectory, "flat.zip");
            var flatPlan = new ArchivePlanner().CreatePlan(new ArchiveRequest(
                sourceDirectory,
                flatArchive,
                selectedGroups,
                ArchiveMode.Flatten,
                scan.Files));
            await new ZipArchiveService().CreateAsync(
                flatArchive,
                flatPlan,
                replaceExistingDestination: false,
                progress: null,
                CancellationToken.None);
            ValidateArchive(
                flatArchive,
                new Dictionary<string, byte[]>(StringComparer.Ordinal)
                {
                    ["image.bin"] = [0x00, 0x01, 0x7f, 0x80, 0xff],
                    ["same.txt"] = "nested text\n"u8.ToArray(),
                    ["same (2).txt"] = "root text\n"u8.ToArray(),
                });

            Console.WriteLine("ExtSieve package archive smoke: PASS");
            Console.WriteLine($"Preserve archive: {preserveArchive}");
            Console.WriteLine($"Flat archive: {flatArchive}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"ExtSieve package archive smoke: FAIL ({exception.GetType().Name}: {exception.Message})");
            return 1;
        }
    }

    private static void PrepareEmptyDirectory(string path)
    {
        if (File.Exists(path))
        {
            throw new IOException("The package smoke work path is an existing file.");
        }

        if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
        {
            throw new IOException("The package smoke work directory must be empty.");
        }

        Directory.CreateDirectory(path);
    }

    private static void ValidateArchive(
        string archivePath,
        Dictionary<string, byte[]> expectedEntries)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        Ensure(
            archive.Entries.Count == expectedEntries.Count,
            $"Archive '{Path.GetFileName(archivePath)}' has an unexpected entry count.");

        foreach (var entry in archive.Entries)
        {
            Ensure(
                expectedEntries.TryGetValue(entry.FullName, out var expectedBytes),
                $"Archive '{Path.GetFileName(archivePath)}' contains unexpected entry '{entry.FullName}'.");
            using var input = entry.Open();
            using var output = new MemoryStream();
            input.CopyTo(output);
            Ensure(
                output.ToArray().AsSpan().SequenceEqual(expectedBytes),
                $"Archive entry '{entry.FullName}' has unexpected content.");
        }
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
