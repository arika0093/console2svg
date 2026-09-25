using System;
using ConsoleToSvg.Recording;

namespace ConsoleToSvg.Tests.Cli;

public sealed class SessionTextConditionTests
{
    [Test]
    public void PresenceConditionMatchesLiteralTextOnTheCurrentScreen()
    {
        var condition = new TerminalConditionEvaluator(new TerminalCondition(Text: "Hi!"));

        condition.IsSatisfied("Prompt").ShouldBeFalse();
        condition.IsSatisfied("Hi! How can I help?").ShouldBeTrue();
    }

    [Test]
    public void AbsenceConditionRequiresTextToHaveBeenSeenBeforeDisappearing()
    {
        var condition = new TerminalConditionEvaluator(
            new TerminalCondition(Text: "Working", Until: TerminalConditionUntil.Absent)
        );

        condition.IsSatisfied("Ready").ShouldBeFalse();
        condition.IsSatisfied("Working").ShouldBeFalse();
        condition.IsSatisfied("Ready").ShouldBeTrue();
    }

    [Test]
    public void TextConditionRejectsAnEmptyNeedle()
    {
        Should.Throw<FormatException>(() => new TerminalConditionEvaluator(new TerminalCondition()));
    }

    [Test]
    public void RegexConditionMatchesTheScreenText()
    {
        var condition = new TerminalConditionEvaluator(new TerminalCondition(Regex: @"^Ready$"));

        condition.IsSatisfied("Prompt\nReady\n").ShouldBeTrue();
        condition.IsSatisfied("Prompt\nWorking\n").ShouldBeFalse();
    }

    [Test]
    public void ConditionRequiresExactlyOneMatcher()
    {
        Should.Throw<FormatException>(() =>
            new TerminalConditionEvaluator(new TerminalCondition(Text: "Ready", Regex: "Ready"))
        );
    }
}
