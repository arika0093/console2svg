using ConsoleToSvg.Recording;

namespace ConsoleToSvg.Tests.Svg;

public sealed class SvgRendererTests
{
    [Test]
    public void RenderStaticSvgFromLastFrame()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "Hi");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                Crop = ConsoleToSvg.Svg.CropOptions.Parse("0", "0", "0", "0"),
            }
        );

        svg.ShouldContain("<svg");
        svg.ShouldContain("viewBox=\"0 0 72 36\"");
        svg.ShouldContain(">H<");
        svg.ShouldContain(">i<");
    }

    [Test]
    public void RenderStaticSvgWithCharacterCrop()
    {
        var session = new RecordingSession(width: 8, height: 2);
        session.AddEvent(0.01, "Hello");

        var svg = ConsoleToSvg.Svg.SvgRenderer.Render(
            session,
            new ConsoleToSvg.Svg.SvgRenderOptions
            {
                Theme = "dark",
                Crop = ConsoleToSvg.Svg.CropOptions.Parse("1ch", "1ch", "0", "0"),
            }
        );

        svg.ShouldContain("viewBox=\"0 0 63 18\"");
    }
}
