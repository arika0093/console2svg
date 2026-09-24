using System;
using ConsoleToSvg;

namespace ConsoleToSvg.Tests.Cli;

public sealed class SessionTextConditionTests
{
    [Test]
    public void PresenceConditionMatchesLiteralTextOnTheCurrentScreen()
    {
        var condition = new SessionTextCondition("Hi!", untilAbsent: false);

        condition.IsSatisfied("Prompt").ShouldBeFalse();
        condition.IsSatisfied("Hi! How can I help?").ShouldBeTrue();
    }

    [Test]
    public void AbsenceConditionRequiresTextToHaveBeenSeenBeforeDisappearing()
    {
        var condition = new SessionTextCondition("Working", untilAbsent: true);

        condition.IsSatisfied("Ready").ShouldBeFalse();
        condition.IsSatisfied("Working").ShouldBeFalse();
        condition.IsSatisfied("Ready").ShouldBeTrue();
    }

    [Test]
    public void TextConditionRejectsAnEmptyNeedle()
    {
        Should.Throw<ArgumentException>(() => new SessionTextCondition("", untilAbsent: false));
    }
}
