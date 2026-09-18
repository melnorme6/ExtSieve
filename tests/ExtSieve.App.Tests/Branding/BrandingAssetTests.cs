using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ExtSieve.App.Tests.Branding;

public sealed partial class BrandingAssetTests
{
    private static readonly XNamespace Presentation = "https://github.com/avaloniaui";
    private static readonly string[] ApprovedReadmeImageHosts =
        ["img.shields.io", "storage.ko-fi.com"];

    public static TheoryData<string, string> ApprovedSourceAssets => new()
    {
        {
            "extsieve-app-icon-light-master.png",
            "EC65FD29CCDD08225949795258966D0BDB99810B8A7958033EF375BE4F1278B4"
        },
        {
            "extsieve-app-icon-dark-master.png",
            "46898D3A76D13DFB5F21C63EAF1F2070C1C70E55E7AF973D0E2F01033743CBAA"
        },
        {
            "extsieve-logo-horizontal-master.png",
            "61BD650035ECF1C2A0D42F5E7CCF700B1EAE0A3F8073FA49C9DB03A801811D3A"
        },
        {
            "extsieve-readme-hero-master.png",
            "4AFA289E785871E008619328DB9B20B92C94C045DB81C0F1C427EF58F169D063"
        },
    };

    [Theory]
    [MemberData(nameof(ApprovedSourceAssets))]
    public void PublicSourceBrandingPreservesApprovedSanitizedBytes(
        string fileName,
        string expectedHash)
    {
        var bytes = File.ReadAllBytes(ProductPath("branding", "source", fileName));

        Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    [Fact]
    public void BrandingPackageContainsTheRequiredDocumentAndApplicationAssets()
    {
        string[] relativePaths =
        [
            "branding/ASSET_NOTES.md",
            "branding/docs/extsieve-logo-horizontal.png",
            "branding/docs/extsieve-logo-mark.png",
            "branding/docs/extsieve-logo-mark-dark.png",
            "branding/docs/extsieve-readme-hero.png",
            "branding/app/extsieve-app-icon.png",
            "branding/app/extsieve-app-icon-dark.png",
            "branding/app/extsieve-app-icon.ico",
            "branding/app/extsieve-app-icon-256.png",
        ];

        Assert.All(relativePaths, relativePath => Assert.True(
            File.Exists(ProductPath(relativePath.Split('/'))),
            $"Missing branding asset: {relativePath}"));
    }

    [Fact]
    public void LinuxLauncherIconIsThePinned256PixelDerivative()
    {
        var bytes = File.ReadAllBytes(ProductPath(
            "branding", "app", "extsieve-app-icon-256.png"));

        Assert.Equal(
            "25F012127579F7A0C045BA3CDAF3E1E91DD25075322230EB27C6932C428E8AF0",
            Convert.ToHexString(SHA256.HashData(bytes)));
        Assert.Equal(256, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)));
        Assert.Equal(256, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
    }

    [Fact]
    public void PublicPngAssetsContainNoPublishUnnecessaryMetadataChunks()
    {
        var pngPaths = Directory.GetFiles(
                ProductPath("branding"),
                "*.png",
                SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(
                ProductPath("docs", "screenshots"),
                "*.png",
                SearchOption.TopDirectoryOnly))
            .ToArray();
        string[] forbiddenChunkTypes = ["caBX", "tEXt", "zTXt", "iTXt", "eXIf", "tIME"];

        Assert.Equal(12, pngPaths.Length);
        Assert.All(pngPaths, pngPath =>
        {
            var bytes = File.ReadAllBytes(pngPath);
            var offset = 8;

            while (offset + 12 <= bytes.Length)
            {
                var dataLength = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));
                Assert.True(dataLength >= 0 && offset + dataLength + 12 <= bytes.Length);
                var chunkType = System.Text.Encoding.ASCII.GetString(bytes, offset + 4, 4);

                Assert.DoesNotContain(chunkType, forbiddenChunkTypes);
                offset += dataLength + 12;
                if (chunkType == "IEND")
                {
                    break;
                }
            }

            Assert.Equal(bytes.Length, offset);
        });
    }

    [Fact]
    public void ReadmeImageReferencesResolveInsideTheProductTree()
    {
        var readmePath = ProductPath("README.md");
        var readmeDirectory = Path.GetDirectoryName(readmePath)!;
        var imagePaths = HtmlImageSourcePattern().Matches(File.ReadAllText(readmePath))
            .Select(match => match.Groups[1].Value)
            .Concat(MarkdownImageSourcePattern().Matches(File.ReadAllText(readmePath))
                .Select(match => match.Groups[1].Value))
            .ToArray();

        Assert.NotEmpty(imagePaths);
        Assert.All(imagePaths, imagePath =>
        {
            if (Uri.TryCreate(imagePath, UriKind.Absolute, out var uri))
            {
                Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
                Assert.Contains(uri.Host, ApprovedReadmeImageHosts);
                return;
            }

            Assert.True(
                File.Exists(Path.GetFullPath(imagePath, readmeDirectory)),
                $"Broken README image reference: {imagePath}");
        });
    }

    [Fact]
    public void ProjectAndWindowUseTheApprovedApplicationIcon()
    {
        var project = XDocument.Load(ProductPath("src", "ExtSieve.App", "ExtSieve.App.csproj"));
        var window = XDocument.Load(ProductPath("src", "ExtSieve.App", "MainWindow.axaml"));

        Assert.Equal(
            @"..\..\branding\app\extsieve-app-icon.ico",
            project.Descendants("ApplicationIcon").Single().Value);
        Assert.Contains(project.Descendants("AvaloniaResource"), element =>
            (string?)element.Attribute("Link") == @"Assets\extsieve-app-icon.png");
        Assert.Equal(
            "avares://ExtSieve.App/Assets/extsieve-app-icon.png",
            (string?)window.Root!.Attribute("Icon"));
    }

    [GeneratedRegex("<img\\s+[^>]*src=\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlImageSourcePattern();

    [GeneratedRegex("!\\[[^\\]]*\\]\\(([^)]+)\\)")]
    private static partial Regex MarkdownImageSourcePattern();

    private static string ProductPath(params string[] parts) => Path.GetFullPath(
        Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            Path.Combine(parts)));
}
