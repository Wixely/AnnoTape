using AnnoTape.Core.Measurements;
using AnnoTape.Core.Models;
using SkiaSharp;

namespace AnnoTape.Core.Export;

public enum ExportFormat
{
    Jpeg,
    Png
}

public sealed record ExportOptions(ExportFormat Format, int JpegQuality = 92, bool IncludeProjectDetails = false);

public sealed class AnnotationExporter
{
    public async Task ExportAsync(
        PhotoDocument document,
        Stream destination,
        ExportOptions options,
        CancellationToken cancellationToken = default)
    {
        await using var source = File.OpenRead(document.SourcePath);
        using var data = SKData.Create(source);
        using var sourceImage = SKImage.FromEncodedData(data) ?? throw new InvalidDataException("The source image could not be decoded.");

        var rotation = ((document.RotationDegrees % 360) + 360) % 360;
        var rotated = rotation is 90 or 270;
        var outputWidth = rotated ? sourceImage.Height : sourceImage.Width;
        var outputHeight = rotated ? sourceImage.Width : sourceImage.Height;
        using var surface = SKSurface.Create(new SKImageInfo(outputWidth, outputHeight, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Could not allocate the export surface.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        canvas.Save();
        ApplySourceRotation(canvas, rotation, outputWidth, outputHeight);
        canvas.DrawImage(sourceImage, 0, 0);
        canvas.Restore();

        foreach (var annotation in document.Annotations) DrawAnnotation(canvas, annotation, outputWidth, outputHeight);
        canvas.Flush();
        using var image = surface.Snapshot();
        var encodedFormat = options.Format == ExportFormat.Png ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg;
        var quality = options.Format == ExportFormat.Png ? 100 : Math.Clamp(options.JpegQuality, 1, 100);
        using var encoded = image.Encode(encodedFormat, quality) ?? throw new InvalidOperationException("The export encoder failed.");
        cancellationToken.ThrowIfCancellationRequested();
        encoded.SaveTo(destination);
        await destination.FlushAsync(cancellationToken);
    }

    private static void ApplySourceRotation(SKCanvas canvas, int rotation, int width, int height)
    {
        switch (rotation)
        {
            case 90:
                canvas.Translate(width, 0);
                canvas.RotateDegrees(90);
                break;
            case 180:
                canvas.Translate(width, height);
                canvas.RotateDegrees(180);
                break;
            case 270:
                canvas.Translate(0, height);
                canvas.RotateDegrees(270);
                break;
        }
    }

    private static void DrawAnnotation(SKCanvas canvas, DimensionAnnotation annotation, int width, int height)
    {
        var start = new SKPoint((float)(annotation.Start.X * width), (float)(annotation.Start.Y * height));
        var end = new SKPoint((float)(annotation.End.X * width), (float)(annotation.End.Y * height));
        var label = new SKPoint((float)(annotation.LabelAnchor.X * width), (float)(annotation.LabelAnchor.Y * height));
        var scale = Math.Max(1f, Math.Min(width, height) / 1080f);
        var colour = SKColor.Parse(AnnotationColours.Normalize(annotation.ColourHex, annotation.Style));
        using var stroke = new SKPaint { Color = colour, IsAntialias = true, StrokeWidth = 4f * scale, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
        canvas.DrawLine(start, end, stroke);
        DrawArrow(canvas, end, start, stroke, 18f * scale);
        DrawArrow(canvas, start, end, stroke, 18f * scale);
        canvas.DrawCircle(start, 6f * scale, stroke);
        canvas.DrawCircle(end, 6f * scale, stroke);

        var caption = string.IsNullOrWhiteSpace(annotation.Label)
            ? $"{annotation.DisplayText} {MeasurementParser.Suffix(annotation.Unit)}"
            : $"{annotation.Label}: {annotation.DisplayText} {MeasurementParser.Suffix(annotation.Unit)}";
        using var font = new SKFont(SKTypeface.Default, 26f * scale);
        using var textPaint = new SKPaint { Color = colour, IsAntialias = true };
        var textWidth = font.MeasureText(caption);
        var metrics = font.Metrics;
        var pad = 9f * scale;
        var rect = SKRect.Create(label.X - textWidth / 2f - pad, label.Y + metrics.Ascent - pad, textWidth + pad * 2, metrics.Descent - metrics.Ascent + pad * 2);
        using var background = new SKPaint { Color = new SKColor(20, 22, 26, 210), IsAntialias = true };
        canvas.DrawRoundRect(rect, 8f * scale, 8f * scale, background);
        canvas.DrawText(caption, label.X - textWidth / 2f, label.Y, font, textPaint);
    }

    private static void DrawArrow(SKCanvas canvas, SKPoint tip, SKPoint toward, SKPaint paint, float size)
    {
        var angle = MathF.Atan2(toward.Y - tip.Y, toward.X - tip.X);
        var left = new SKPoint(tip.X + MathF.Cos(angle + 0.55f) * size, tip.Y + MathF.Sin(angle + 0.55f) * size);
        var right = new SKPoint(tip.X + MathF.Cos(angle - 0.55f) * size, tip.Y + MathF.Sin(angle - 0.55f) * size);
        canvas.DrawLine(tip, left, paint);
        canvas.DrawLine(tip, right, paint);
    }

}
