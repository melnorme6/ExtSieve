using System.Diagnostics;
using System.Xml.Linq;

namespace ExtSieve.App.Tests.Services;

public sealed class PublicBuildMetadataTests
{
    [Theory]
    [InlineData("false", "", true, "", false)]
    [InlineData("true", "", false, "Public release builds require PublicSourceRevision", false)]
    [InlineData("true", "abcdef1234567890abcdef1234567890abcdef12", true, "", false)]
    [InlineData("true", "ABCDEF1234567890ABCDEF1234567890ABCDEF12", true, "", false)]
    [InlineData("false", "abcdef1234567890abcdef1234567890abcdef12", true, "", false)]
    [InlineData("true", "abcdef1", false, "40-character", false)]
    [InlineData("false", "invalid", false, "40-character", false)]
    [InlineData("invalid", "", false, "PublicReleaseBuild must be true or false", false)]
    [InlineData("true", "abcdef1234567890abcdef1234567890abcdef12", false, "do not use --no-build", true)]
    public async Task ActualMsBuildGuardRejectsMissingOrInvalidPublicProvenanceBeforePackaging(
        string publicBuild, string revision, bool succeeds, string expectedError, bool noBuild)
    {
        var product = new DirectoryInfo(AppContext.BaseDirectory);
        while (product is not null && !File.Exists(Path.Combine(product.FullName, "Directory.Build.targets")))
            product = product.Parent;
        Assert.NotNull(product);
        var root = Path.Combine(Path.GetTempPath(), $"ExtSieve.PublicBuild.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var fixture = Path.Combine(root, "Guard.proj");
            new XDocument(new XElement("Project",
                new XElement("Import", new XAttribute("Project", Path.Combine(product.FullName, "Directory.Build.props"))),
                new XElement("Import", new XAttribute("Project", Path.Combine(product.FullName, "Directory.Build.targets")))))
                .Save(fixture);
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var argument in new[] { "msbuild", fixture, "-target:ValidatePublicSourceRevision", "-nologo",
                "-p:PublicReleaseBuild=" + publicBuild, "-p:PublicSourceRevision=" + revision, "-p:NoBuild=" + noBuild })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var error = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                var message = await output + await error;
                if (succeeds) Assert.Equal(0, process.ExitCode);
                else
                {
                    Assert.NotEqual(0, process.ExitCode);
                    Assert.Contains(expectedError, message, StringComparison.Ordinal);
                }
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
