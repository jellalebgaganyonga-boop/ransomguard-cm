using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace RansomGuard.Agent.Core.Detection.Sentinel.Generators;

/// <summary>
/// Generates valid .docx (OpenXML WordprocessingML) canary files.
/// The resulting files open correctly in Microsoft Word.
/// </summary>
public sealed class DocxCanaryGenerator : ICanaryFileGenerator
{
    /// <inheritdoc />
    public string Extension => ".docx";

    /// <inheritdoc />
    public byte[] Generate(string template)
    {
        // Get the text content from the base generator
        (byte[] textBytes, _) = CanaryContentGenerator.Generate(template);
        string textContent = Encoding.UTF8.GetString(textBytes);

        using var stream = new MemoryStream();
        using (WordprocessingDocument doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            MainDocumentPart mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            Body body = mainPart.Document.AppendChild(new Body());

            string[] lines = textContent.Split('\n');
            foreach (string line in lines)
            {
                string trimmed = line.TrimEnd('\r');
                var paragraph = new Paragraph(
                    new Run(
                        new Text(trimmed) { Space = SpaceProcessingModeValues.Preserve }
                    )
                );
                body.AppendChild(paragraph);
            }
        }

        return stream.ToArray();
    }
}
