using System;
using ConsoleToSvg.Conversion;

namespace ConsoleToSvg.Tests.Conversion;

public sealed class ToolResolverTests
{
    [Test]
    public void ResolveResvgPrefersEnvironmentVariable()
    {
        using var tempDir = new TestTempDirectory();
        var fakeTool = tempDir.CreateExecutable("custom-resvg", "fake-resvg");
        using var scope = new EnvironmentScope();
        scope.Set(ToolResolver.ResvgEnvironmentVariable, fakeTool);

        ToolResolver.ResolveResvg().ShouldBe(fakeTool);
    }

    [Test]
    public void ResolveFfmpegFallsBackToPath()
    {
        using var tempDir = new TestTempDirectory();
        var fakeTool = tempDir.CreateExecutable("ffmpeg", "fake-ffmpeg");
        using var scope = new EnvironmentScope();
        scope.PrependPath(tempDir.Path);

        string.Equals(ToolResolver.ResolveFfmpeg(), fakeTool, StringComparison.OrdinalIgnoreCase)
            .ShouldBeTrue();
    }
}
