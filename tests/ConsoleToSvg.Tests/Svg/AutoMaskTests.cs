using ConsoleToSvg.Recording;
using ConsoleToSvg.Svg;

namespace ConsoleToSvg.Tests.Svg;

public sealed class AutoMaskTests
{
    [Test]
    public void StaticSvgMasksPasswordAndTokenSuffixKeys()
    {
        var session = new RecordingSession(width: 80, height: 5);
        session.AddEvent(
            0.01,
            "PASSWORD=first\r\nFOOBAR_PASSWORD: second\r\n\"apiToken\": \"third\""
                + "\r\nPASSWORD=\"two words\"\r\n$env:FOO_TOKEN = 'fourth'"
        );

        var svg = SvgRenderer.Render(
            session,
            new SvgRenderOptions { AutoMask = AutoMaskCategory.Password | AutoMaskCategory.Token }
        );

        svg.ShouldNotContain("first");
        svg.ShouldNotContain("second");
        svg.ShouldNotContain("third");
        svg.ShouldNotContain("two words");
        svg.ShouldNotContain("fourth");
        svg.ShouldContain("PASSWORD=*****");
        svg.ShouldContain("FOOBAR_PASSWORD: ******");
        svg.ShouldContain("apiToken&quot;: &quot;*****");
        svg.ShouldContain("PASSWORD=&quot;*********");
        svg.ShouldContain("FOO_TOKEN = &apos;******");
    }

    [Test]
    public void PasswordAndTokenCategoriesCanBeEnabledIndependently()
    {
        var session = new RecordingSession(width: 60, height: 2);
        session.AddEvent(0.01, "DB_PASSWORD=visible1\r\nACCESS_TOKEN=visible2");

        var svg = SvgRenderer.Render(
            session,
            new SvgRenderOptions { AutoMask = AutoMaskCategory.Token }
        );

        svg.ShouldContain("visible1");
        svg.ShouldNotContain("visible2");
        svg.ShouldContain("ACCESS_TOKEN=********");
    }

    [Test]
    public void AutoMaskRequiresPasswordOrTokenAtTheEndOfTheKey()
    {
        var session = new RecordingSession(width: 60, height: 2);
        session.AddEvent(0.01, "PASSWORD_POLICY=strict\r\nTOKEN_TYPE=public");

        var svg = SvgRenderer.Render(
            session,
            new SvgRenderOptions { AutoMask = AutoMaskCategory.Password | AutoMaskCategory.Token }
        );

        svg.ShouldContain("PASSWORD_POLICY=strict");
        svg.ShouldContain("TOKEN_TYPE=public");
    }

    [Test]
    public void AutoMaskWorksAcrossTerminalTextStyles()
    {
        var session = new RecordingSession(width: 40, height: 1);
        session.AddEvent(0.01, "FOOBAR_\u001b[31mTOKEN\u001b[0m=secret");

        var svg = SvgRenderer.Render(
            session,
            new SvgRenderOptions { AutoMask = AutoMaskCategory.Token }
        );

        svg.ShouldNotContain("secret");
        svg.ShouldContain("******");
    }

    [Test]
    public void AutoMaskAppliesToCommandHeader()
    {
        var session = new RecordingSession(width: 40, height: 1);
        session.AddEvent(0.01, "done");

        var svg = SvgRenderer.Render(
            session,
            new SvgRenderOptions
            {
                CommandHeader = "$ FOOBAR_TOKEN=secret command",
                AutoMask = AutoMaskCategory.Token,
            }
        );

        svg.ShouldNotContain("FOOBAR_TOKEN=secret");
        svg.ShouldContain("FOOBAR_TOKEN=******");
    }

    [Test]
    public void AnimatedSvgMasksKeyedSecretWhileItIsBeingTyped()
    {
        var session = new RecordingSession(width: 40, height: 1);
        session.AddEvent(0.01, "FOOBAR_TOKEN=");
        session.AddEvent(0.10, "a");
        session.AddEvent(0.20, "b");
        session.AddEvent(0.30, "c");

        var svg = AnimatedSvgRenderer.Render(
            session,
            new SvgRenderOptions { AutoMask = AutoMaskCategory.Token, VideoFps = 0 }
        );

        svg.ShouldNotContain("FOOBAR_TOKEN=a");
        svg.ShouldNotContain("FOOBAR_TOKEN=ab");
        svg.ShouldNotContain("FOOBAR_TOKEN=abc");
        svg.ShouldContain("FOOBAR_TOKEN=*");
        svg.ShouldContain("FOOBAR_TOKEN=***");
    }

    [Test]
    public void AnimatedSvgMasksPartialHomeDirectoryUserName()
    {
        var session = new RecordingSession(width: 40, height: 1);
        session.AddEvent(0.01, "C:\\Users\\");
        session.AddEvent(0.10, "a");
        session.AddEvent(0.20, "r");
        session.AddEvent(0.30, "i");
        session.AddEvent(0.40, "k");
        session.AddEvent(0.50, "a");

        var svg = AnimatedSvgRenderer.Render(
            session,
            new SvgRenderOptions
            {
                AutoMask = AutoMaskCategory.HomeDirectory,
                AutoMaskHomeDirectory = "C:\\Users\\arika",
                VideoFps = 0,
            }
        );

        svg.ShouldNotContain("C:\\Users\\a");
        svg.ShouldNotContain("C:\\Users\\arika");
        svg.ShouldContain("C:\\Users\\*");
        svg.ShouldContain("C:\\Users\\*****");
    }

    [Test]
    public void HomeDirectoryMaskSupportsUnixAndWindowsSlashPaths()
    {
        var unixSession = new RecordingSession(width: 40, height: 1);
        unixSession.AddEvent(0.01, "/home/arika/project");
        var windowsSession = new RecordingSession(width: 40, height: 1);
        windowsSession.AddEvent(0.01, "C:/Users/arika/project");

        var unixSvg = SvgRenderer.Render(
            unixSession,
            new SvgRenderOptions
            {
                AutoMask = AutoMaskCategory.HomeDirectory,
                AutoMaskHomeDirectory = "/home/arika",
            }
        );
        var windowsSvg = SvgRenderer.Render(
            windowsSession,
            new SvgRenderOptions
            {
                AutoMask = AutoMaskCategory.HomeDirectory,
                AutoMaskHomeDirectory = "C:\\Users\\arika",
            }
        );

        unixSvg.ShouldContain("/home/*****/project");
        windowsSvg.ShouldContain("C:/Users/*****/project");
    }

    [Test]
    public void HomeDirectoryMaskDoesNotMaskAnotherUserWithTheSamePrefix()
    {
        var session = new RecordingSession(width: 40, height: 1);
        session.AddEvent(0.01, "/home/arika2/project");

        var svg = SvgRenderer.Render(
            session,
            new SvgRenderOptions
            {
                AutoMask = AutoMaskCategory.HomeDirectory,
                AutoMaskHomeDirectory = "/home/arika",
            }
        );

        svg.ShouldContain("/home/arika2/project");
    }

    [Test]
    public void HomeDirectoryMaskSupportsWideCharacterUserNames()
    {
        var session = new RecordingSession(width: 40, height: 1);
        session.AddEvent(0.01, "/home/山田/project");

        var svg = SvgRenderer.Render(
            session,
            new SvgRenderOptions
            {
                AutoMask = AutoMaskCategory.HomeDirectory,
                AutoMaskHomeDirectory = "/home/山田",
            }
        );

        svg.ShouldNotContain("山田");
        svg.ShouldContain("**");
    }
}
