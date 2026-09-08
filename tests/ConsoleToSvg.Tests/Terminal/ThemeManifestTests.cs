using System;
using System.IO;
using System.Text.Json;
using ConsoleToSvg.Terminal;

namespace ConsoleToSvg.Tests.Terminal;

public sealed class ThemeManifestTests
{
    [Test]
    public void IncludeAcceptsIdsAndSourceObjects()
    {
        var manifest = JsonSerializer.Deserialize<ThemeManifest>(
            """{"schemaVersion":1,"id":"child","name":"Child","$include":["base",{"id":"icons","source":"owner/repo"}]}"""
        )!;

        manifest.Includes.ShouldBe(
            [new ThemeInclude("base"), new ThemeInclude("icons", "owner/repo")]
        );
    }

    [Test]
    public void CatalogResolvesDeclaredPcVariantId()
    {
        var entry = new ThemeCatalog().Resolve("macos-pc");

        entry.IsPcVariant.ShouldBeTrue();
        entry.Manifest.Appearance!.Pc!.Id.ShouldBe("macos-pc");
    }

    [Test]
    public void ValidatorRequiresIdsForAppearanceVariants()
    {
        var manifest = new ThemeManifest
        {
            SchemaVersion = 1,
            Id = "theme",
            Name = "Theme",
            Appearance = new ThemeAppearance { Normal = new ThemeVariant() },
        };

        Should.Throw<InvalidDataException>(() => ThemeManifestValidator.Validate(manifest, "."));
    }

    [Test]
    public void SourceParsesGitHubShorthandWithRefAndDirectory()
    {
        var source = ThemeSource.Parse("owner/repo@v1/themes/base");

        source.ShouldBe(new ThemeSource("https://github.com/owner/repo.git", "v1", "themes/base"));
    }
}
