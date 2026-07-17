using CodexRemaining.App.Infrastructure;

namespace CodexRemaining.App.Tests;

public sealed class CapsulePlacementTests
{
    [Fact]
    public void SnapsToNearbyRightEdgeAndPreservesAlongEdgePosition()
    {
        var workArea = new CapsuleBounds(0, 0, 1920, 1040);
        var proposed = new CapsuleBounds(1712, 200, 200, 60);

        var result = CapsulePlacement.Snap(proposed, workArea, 12);

        Assert.Equal(CapsuleAnchor.Right, result.Anchor);
        Assert.Equal(new CapsuleBounds(1720, 200, 200, 60), result.Bounds);
        Assert.Equal(200d / 980d, result.EdgeOffsetRatio, precision: 6);
    }

    [Fact]
    public void SnapsToBothEdgesWhenDroppedNearCorner()
    {
        var workArea = new CapsuleBounds(0, 0, 1920, 1040);
        var proposed = new CapsuleBounds(1712, 6, 200, 60);

        var result = CapsulePlacement.Snap(proposed, workArea, 12);

        Assert.Equal(CapsuleAnchor.TopRight, result.Anchor);
        Assert.Equal(new CapsuleBounds(1720, 0, 200, 60), result.Bounds);
        Assert.Equal(0, result.EdgeOffsetRatio);
    }

    [Fact]
    public void RestoresEdgeAttachmentAfterWorkAreaAndSizeChange()
    {
        var changedWorkArea = new CapsuleBounds(1920, 0, 2560, 1400);

        var result = CapsulePlacement.Restore(
            CapsuleAnchor.Right,
            edgeOffsetRatio: 0.5,
            changedWorkArea,
            width: 250,
            height: 100);

        Assert.Equal(CapsuleAnchor.Right, result.Anchor);
        Assert.Equal(new CapsuleBounds(4230, 650, 250, 100), result.Bounds);
        Assert.Equal(0.5, result.EdgeOffsetRatio);
    }

    [Fact]
    public void KeepsDetachedCapsuleFullyInsideWorkArea()
    {
        var workArea = new CapsuleBounds(0, 0, 1920, 1040);
        var proposed = new CapsuleBounds(1850, -20, 200, 60);

        var result = CapsulePlacement.KeepVisible(proposed, workArea);

        Assert.Equal(CapsuleAnchor.None, result.Anchor);
        Assert.Equal(new CapsuleBounds(1720, 0, 200, 60), result.Bounds);
    }

    [Fact]
    public void SelectsNearestAvailableDisplayWhenAttachedDisplayWasRemoved()
    {
        var displays = new[]
        {
            new CapsuleDisplay(@"\\.\DISPLAY1", 960, 540, new CapsuleBounds(0, 0, 1920, 1040)),
            new CapsuleDisplay(@"\\.\DISPLAY3", -1280, 512, new CapsuleBounds(-2560, 0, 1280, 984)),
        };

        var result = CapsulePlacement.SelectDisplay(
            @"\\.\DISPLAY2",
            savedCenterX: 3200,
            savedCenterY: 720,
            displays);

        Assert.Equal(@"\\.\DISPLAY1", result?.DeviceName);
    }
}
