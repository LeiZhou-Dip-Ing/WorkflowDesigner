using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WorkflowCore.WpfDemo.Services;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class MethodDescriptionDocumentCodecTests
{
    [Fact]
    public void EncodeAndDecode_PreserveFormattedTextAndEmbeddedImage()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var codec = new MethodDescriptionDocumentCodec();
                var paragraph = new Paragraph();
                paragraph.Inlines.Add(new Bold(new Run("Important")));
                paragraph.Inlines.Add(new Run(" method documentation "));
                paragraph.Inlines.Add(new InlineUIContainer(new Image { Source = CreatePixel() }));
                var encoded = codec.Encode(new FlowDocument(paragraph));

                var restored = codec.Decode(encoded);
                var text = new TextRange(restored.ContentStart, restored.ContentEnd).Text;

                Assert.False(string.IsNullOrWhiteSpace(encoded));
                Assert.Contains("Important", text);
                Assert.True(ContainsImage(restored));
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static BitmapSource CreatePixel()
    {
        var bitmap = BitmapSource.Create(
            1,
            1,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            new byte[] { 0x00, 0x80, 0xff, 0xff },
            4);
        bitmap.Freeze();
        return bitmap;
    }

    private static bool ContainsImage(DependencyObject element)
    {
        if (element is Image)
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
}
