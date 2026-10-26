using FluentAssertions;
using DiskPilotX.Core.Services.Copilot;

namespace DiskPilotX.Core.Tests;

public class AiInteractionCardTests
{
    [Fact]
    public void StatusIconGlyph_ReflectsIdleRunningAndFinishStates()
    {
        var card = new AiInteractionCard
        {
            Title = "Scan",
            Description = "Scan drive",
            Action = new AiActionRequest(AiActionKind.StartScan, Path: @"D:\")
        };

        card.StatusIconState.Should().Be("idle");
        card.StatusIconGlyph.Should().Be("○");

        card.IsBusy = true;
        card.StatusIconState.Should().Be("running");
        card.StatusIconGlyph.Should().Be("◐");

        card.IsBusy = false;
        card.Status = AiInteractionCardStatus.Completed;
        card.StatusIconState.Should().Be("finish");
        card.StatusIconGlyph.Should().Be("✓");
    }
}
