using AnnoTape.Core.Models;

namespace AnnoTape.Core.Geometry;

public readonly record struct PixelRect(double X, double Y, double Width, double Height)
{
    public bool Contains(double x, double y) => x >= X && y >= Y && x <= X + Width && y <= Y + Height;
}

public static class ImageGeometry
{
    public static PixelRect FitContain(double sourceWidth, double sourceHeight, PixelRect viewport)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || viewport.Width <= 0 || viewport.Height <= 0)
            return new(viewport.X, viewport.Y, 0, 0);

        var scale = Math.Min(viewport.Width / sourceWidth, viewport.Height / sourceHeight);
        var width = sourceWidth * scale;
        var height = sourceHeight * scale;
        return new(viewport.X + (viewport.Width - width) / 2d, viewport.Y + (viewport.Height - height) / 2d, width, height);
    }

    public static NormalizedPoint ToNormalized(double x, double y, PixelRect imageRect) =>
        NormalizedPoint.Clamp((x - imageRect.X) / imageRect.Width, (y - imageRect.Y) / imageRect.Height);

    public static (double X, double Y) ToPixels(NormalizedPoint point, PixelRect imageRect) =>
        (imageRect.X + point.X * imageRect.Width, imageRect.Y + point.Y * imageRect.Height);

    public static double DistanceToSegmentPixels(
        NormalizedPoint point,
        NormalizedPoint start,
        NormalizedPoint end,
        double imageWidth,
        double imageHeight)
    {
        var px = point.X * imageWidth;
        var py = point.Y * imageHeight;
        var ax = start.X * imageWidth;
        var ay = start.Y * imageHeight;
        var bx = end.X * imageWidth;
        var by = end.Y * imageHeight;
        var dx = bx - ax;
        var dy = by - ay;
        var lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared == 0 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / lengthSquared, 0, 1);
        var ex = px - (ax + t * dx);
        var ey = py - (ay + t * dy);
        return Math.Sqrt(ex * ex + ey * ey);
    }
}

