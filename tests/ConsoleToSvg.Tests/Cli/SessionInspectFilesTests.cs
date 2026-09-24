using System;
using System.IO;

namespace ConsoleToSvg.Tests.Cli;

public sealed class SessionInspectFilesTests
{
    [Test]
    public void InspectRetentionPolicyIsBoundedAt24Hours()
    {
        SessionInspectFiles.Retention.ShouldBe(TimeSpan.FromHours(24));
    }

    [Test]
    public void CreateInspectPathGeneratesUniqueRandomizedPathsUnderPerUserTempRoot()
    {
        var tempBase = Path.Combine(Path.GetTempPath(), "c2s-inspect-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = SessionInspectFiles.CreateInspectPath(tempBase);
            var second = SessionInspectFiles.CreateInspectPath(tempBase);

            first.ShouldNotBe(second);
            Path.GetFileName(first).ShouldBe("inspect.svg");
            Path.GetFileName(second).ShouldBe("inspect.svg");
            Path.GetFileName(Path.GetDirectoryName(first)!).ShouldStartWith("inspect-");
            Path.GetDirectoryName(first)!.ShouldStartWith(SessionInspectFiles.GetInspectRoot(tempBase));
            Directory.Exists(Path.GetDirectoryName(first)!).ShouldBeTrue();
            Directory.Exists(Path.GetDirectoryName(second)!).ShouldBeTrue();
        }
        finally
        {
            if (Directory.Exists(tempBase))
            {
                Directory.Delete(tempBase, recursive: true);
            }
        }
    }

    [Test]
    public void SweepRemovesOnlyStaleInspectionEntries()
    {
        var tempBase = Path.Combine(Path.GetTempPath(), "c2s-inspect-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var fresh = SessionInspectFiles.CreateInspectPath(tempBase);
            var stale = SessionInspectFiles.CreateInspectPath(tempBase);
            File.WriteAllText(stale, "<svg></svg>");
            File.WriteAllText(fresh, "<svg></svg>");
            var staleDirectory = Path.GetDirectoryName(stale)!;
            var now = DateTimeOffset.UtcNow;
            File.SetLastWriteTimeUtc(stale, now.AddHours(-25).UtcDateTime);
            Directory.SetLastWriteTimeUtc(staleDirectory, now.AddHours(-25).UtcDateTime);

            var removed = SessionInspectFiles.SweepStaleInspectFiles(now, tempBase);

            removed.ShouldBe(1);
            Directory.Exists(staleDirectory).ShouldBeFalse();
            Directory.Exists(Path.GetDirectoryName(fresh)!).ShouldBeTrue();
        }
        finally
        {
            if (Directory.Exists(tempBase))
            {
                Directory.Delete(tempBase, recursive: true);
            }
        }
    }
}