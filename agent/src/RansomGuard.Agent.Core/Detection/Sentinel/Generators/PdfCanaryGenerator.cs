using System.Text;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;

namespace RansomGuard.Agent.Core.Detection.Sentinel.Generators;

/// <summary>
/// Generates valid PDF canary files with French medical content.
/// Uses PdfSharpCore (MIT) for cross-platform PDF generation.
/// </summary>
public sealed class PdfCanaryGenerator : ICanaryFileGenerator
{
    /// <inheritdoc />
    public string Extension => ".pdf";

    /// <inheritdoc />
    public byte[] Generate(string template)
    {
        (byte[] textContent, _) = CanaryContentGenerator.Generate(template);
        string text = Encoding.UTF8.GetString(textContent);

        using var document = new PdfDocument();
        document.Info.Title = $"RansomGuard SENTINEL — {template}";
        document.Info.Author = "Hopital Central de Yaounde";

        PdfPage page = document.AddPage();
        page.Size = PdfSharpCore.PageSize.A4;

        using XGraphics gfx = XGraphics.FromPdfPage(page);
        XFont titleFont = new("Arial", 14, XFontStyle.Bold);
        XFont bodyFont = new("Arial", 9, XFontStyle.Regular);

        double y = 40;
        string[] lines = text.Split('\n');

        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');

            if (y > page.Height - 40)
            {
                break; // Stay on single page
            }

            XFont font = line.Contains("═") || line.Contains("║") || line.Contains("─")
                ? titleFont
                : bodyFont;

            // Skip box-drawing chars that PDF fonts can't render — replace with dashes
            string cleaned = line
                .Replace('═', '=').Replace('║', '|').Replace('╔', '+').Replace('╗', '+')
                .Replace('╚', '+').Replace('╝', '+').Replace('─', '-');

            gfx.DrawString(cleaned.TrimStart(), font, XBrushes.Black, 40, y);
            y += font.Size * 1.5;
        }

        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }
}
