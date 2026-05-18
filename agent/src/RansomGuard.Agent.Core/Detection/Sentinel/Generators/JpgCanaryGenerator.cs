using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace RansomGuard.Agent.Core.Detection.Sentinel.Generators;

/// <summary>
/// Generates valid JPEG canary files simulating medical imaging metadata.
/// Uses SixLabors.ImageSharp 2.x (Apache 2.0) for cross-platform image generation.
/// </summary>
public sealed class JpgCanaryGenerator : ICanaryFileGenerator
{
    /// <inheritdoc />
    public string Extension => ".jpg";

    /// <inheritdoc />
    public byte[] Generate(string template)
    {
        // Create a medical imaging-like image (gradient background + text)
        using var image = new Image<Rgba32>(800, 600);

        // Dark gradient background (medical imaging style)
        image.Mutate(ctx =>
        {
            ctx.BackgroundColor(Color.FromRgb(20, 20, 30));

            // Draw horizontal gradient bands (simulates medical scan lines)
            for (int y = 0; y < 600; y += 3)
            {
                byte intensity = (byte)(40 + (y % 120));
                ctx.DrawLine(
                    Color.FromRgb(intensity, intensity, (byte)(intensity + 10)),
                    1.0f,
                    new PointF(0, y), new PointF(800, y));
            }
        });

        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        return stream.ToArray();
    }
}
