namespace CodexRemaining.App.Infrastructure;

public enum CapsuleAnchor
{
    None,
    Left,
    Top,
    Right,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

public readonly record struct CapsuleBounds(
    double Left,
    double Top,
    double Width,
    double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;
}

public sealed record CapsulePlacementResult(
    CapsuleBounds Bounds,
    CapsuleAnchor Anchor,
    double EdgeOffsetRatio);

public sealed record CapsuleDisplay(
    string DeviceName,
    int CenterX,
    int CenterY,
    CapsuleBounds WorkArea);

public static class CapsulePlacement
{
    public static CapsulePlacementResult Snap(
        CapsuleBounds proposed,
        CapsuleBounds workArea,
        double snapDistance)
    {
        var horizontal = NearestHorizontalAnchor(proposed, workArea, snapDistance);
        var vertical = NearestVerticalAnchor(proposed, workArea, snapDistance);
        var anchor = Combine(horizontal, vertical);
        var left = horizontal switch
        {
            CapsuleAnchor.Left => workArea.Left,
            CapsuleAnchor.Right => workArea.Right - proposed.Width,
            _ => ClampLeft(proposed.Left, proposed.Width, workArea),
        };
        var top = vertical switch
        {
            CapsuleAnchor.Top => workArea.Top,
            CapsuleAnchor.Bottom => workArea.Bottom - proposed.Height,
            _ => ClampTop(proposed.Top, proposed.Height, workArea),
        };
        var bounds = proposed with { Left = left, Top = top };

        return new CapsulePlacementResult(bounds, anchor, EdgeOffsetRatio(bounds, workArea, anchor));
    }

    public static CapsulePlacementResult Restore(
        CapsuleAnchor anchor,
        double edgeOffsetRatio,
        CapsuleBounds workArea,
        double width,
        double height)
    {
        var ratio = Math.Clamp(edgeOffsetRatio, 0, 1);
        var left = anchor switch
        {
            CapsuleAnchor.Left or CapsuleAnchor.TopLeft or CapsuleAnchor.BottomLeft => workArea.Left,
            CapsuleAnchor.Right or CapsuleAnchor.TopRight or CapsuleAnchor.BottomRight =>
                workArea.Right - width,
            CapsuleAnchor.Top or CapsuleAnchor.Bottom =>
                workArea.Left + (Math.Max(0, workArea.Width - width) * ratio),
            _ => workArea.Left,
        };
        var top = anchor switch
        {
            CapsuleAnchor.Top or CapsuleAnchor.TopLeft or CapsuleAnchor.TopRight => workArea.Top,
            CapsuleAnchor.Bottom or CapsuleAnchor.BottomLeft or CapsuleAnchor.BottomRight =>
                workArea.Bottom - height,
            CapsuleAnchor.Left or CapsuleAnchor.Right =>
                workArea.Top + (Math.Max(0, workArea.Height - height) * ratio),
            _ => workArea.Top,
        };
        var bounds = new CapsuleBounds(
            ClampLeft(left, width, workArea),
            ClampTop(top, height, workArea),
            width,
            height);

        return new CapsulePlacementResult(bounds, anchor, ratio);
    }

    public static CapsulePlacementResult KeepVisible(
        CapsuleBounds proposed,
        CapsuleBounds workArea)
    {
        var bounds = proposed with
        {
            Left = ClampLeft(proposed.Left, proposed.Width, workArea),
            Top = ClampTop(proposed.Top, proposed.Height, workArea),
        };
        return new CapsulePlacementResult(bounds, CapsuleAnchor.None, 0);
    }

    public static CapsuleDisplay? SelectDisplay(
        string savedDeviceName,
        int savedCenterX,
        int savedCenterY,
        IReadOnlyCollection<CapsuleDisplay> displays)
    {
        var exact = displays.FirstOrDefault(display => string.Equals(
            display.DeviceName,
            savedDeviceName,
            StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        return displays.MinBy(display => DistanceSquared(
            display.CenterX,
            display.CenterY,
            savedCenterX,
            savedCenterY));
    }

    private static CapsuleAnchor NearestHorizontalAnchor(
        CapsuleBounds proposed,
        CapsuleBounds workArea,
        double snapDistance)
    {
        var leftDistance = Math.Abs(proposed.Left - workArea.Left);
        var rightDistance = Math.Abs(proposed.Right - workArea.Right);
        var nearestDistance = Math.Min(leftDistance, rightDistance);

        return nearestDistance > snapDistance
            ? CapsuleAnchor.None
            : leftDistance <= rightDistance ? CapsuleAnchor.Left : CapsuleAnchor.Right;
    }

    private static CapsuleAnchor NearestVerticalAnchor(
        CapsuleBounds proposed,
        CapsuleBounds workArea,
        double snapDistance)
    {
        var topDistance = Math.Abs(proposed.Top - workArea.Top);
        var bottomDistance = Math.Abs(proposed.Bottom - workArea.Bottom);
        var nearestDistance = Math.Min(topDistance, bottomDistance);

        return nearestDistance > snapDistance
            ? CapsuleAnchor.None
            : topDistance <= bottomDistance ? CapsuleAnchor.Top : CapsuleAnchor.Bottom;
    }

    private static CapsuleAnchor Combine(CapsuleAnchor horizontal, CapsuleAnchor vertical) =>
        (horizontal, vertical) switch
        {
            (CapsuleAnchor.Left, CapsuleAnchor.Top) => CapsuleAnchor.TopLeft,
            (CapsuleAnchor.Right, CapsuleAnchor.Top) => CapsuleAnchor.TopRight,
            (CapsuleAnchor.Left, CapsuleAnchor.Bottom) => CapsuleAnchor.BottomLeft,
            (CapsuleAnchor.Right, CapsuleAnchor.Bottom) => CapsuleAnchor.BottomRight,
            (not CapsuleAnchor.None, _) => horizontal,
            (_, not CapsuleAnchor.None) => vertical,
            _ => CapsuleAnchor.None,
        };

    private static double ClampLeft(double left, double width, CapsuleBounds workArea) =>
        Math.Clamp(left, workArea.Left, Math.Max(workArea.Left, workArea.Right - width));

    private static double ClampTop(double top, double height, CapsuleBounds workArea) =>
        Math.Clamp(top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height));

    private static double EdgeOffsetRatio(
        CapsuleBounds bounds,
        CapsuleBounds workArea,
        CapsuleAnchor anchor)
    {
        if (anchor is CapsuleAnchor.Left or CapsuleAnchor.Right)
        {
            var travel = Math.Max(0, workArea.Height - bounds.Height);
            return travel == 0 ? 0 : (bounds.Top - workArea.Top) / travel;
        }

        if (anchor is CapsuleAnchor.Top or CapsuleAnchor.Bottom)
        {
            var travel = Math.Max(0, workArea.Width - bounds.Width);
            return travel == 0 ? 0 : (bounds.Left - workArea.Left) / travel;
        }

        return 0;
    }

    private static double DistanceSquared(int x1, int y1, int x2, int y2)
    {
        var x = (double)x1 - x2;
        var y = (double)y1 - y2;
        return (x * x) + (y * y);
    }
}
