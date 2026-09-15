using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace WorkflowCore.WpfDemo.Services;

/// <summary>
/// Converts method documentation between its persisted, opaque payload and a WPF document.
/// This codec is used only while the description editor is open.
/// </summary>
public sealed class MethodDescriptionDocumentCodec
{
    public const int MaximumDocumentBytes = 16 * 1024 * 1024;

    public FlowDocument Decode(string? encodedDocument)
    {
        var document = CreateEmptyDocument();
        if (string.IsNullOrWhiteSpace(encodedDocument))
        {
            return document;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(encodedDocument);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("The method description is not a valid document payload.", exception);
        }

        EnsureSize(bytes.Length);
        using var stream = new MemoryStream(bytes, writable: false);
        Load(document, stream);
        return document;
    }

    public string? Encode(FlowDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (IsEmpty(document))
        {
            return null;
        }

        using var stream = new MemoryStream();
        Save(document, stream);
        EnsureSize(stream.Length);
        return Convert.ToBase64String(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    public FlowDocument LoadFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var file = new FileInfo(filePath);
        EnsureSize(file.Length);
        var document = CreateEmptyDocument();
        using var stream = file.OpenRead();
        Load(document, stream);
        return document;
    }

    public void SaveFile(FlowDocument document, string filePath)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        using var stream = new MemoryStream();
        Save(document, stream);
        EnsureSize(stream.Length);
        File.WriteAllBytes(filePath, stream.ToArray());
    }

    private static FlowDocument CreateEmptyDocument()
        => new(new Paragraph())
        {
            PagePadding = new Thickness(42),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            Foreground = Brushes.Black
        };

    private static void Load(FlowDocument document, Stream stream)
    {
        var range = new TextRange(document.ContentStart, document.ContentEnd);
        range.Load(stream, DataFormats.XamlPackage);
    }

    private static void Save(FlowDocument document, Stream stream)
    {
        var range = new TextRange(document.ContentStart, document.ContentEnd);
        range.Save(stream, DataFormats.XamlPackage);
    }

    private static bool IsEmpty(FlowDocument document)
    {
        var text = new TextRange(document.ContentStart, document.ContentEnd).Text;
        return string.IsNullOrWhiteSpace(text) && !ContainsImage(document);
    }

    private static bool ContainsImage(DependencyObject element)
    {
        if (element is System.Windows.Controls.Image)
        {
            return true;
        }

        foreach (var child in LogicalTreeHelper.GetChildren(element))
        {
            if (child is DependencyObject dependencyObject && ContainsImage(dependencyObject))
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsureSize(long byteCount)
    {
        if (byteCount > MaximumDocumentBytes)
        {
            throw new InvalidDataException("Method descriptions are limited to 16 MB, including embedded images.");
        }
    }
}
