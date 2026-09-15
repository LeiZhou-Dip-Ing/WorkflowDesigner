using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WorkflowCore.WpfDemo.Services;

namespace WorkflowCore.WpfDemo.Views;

public partial class MethodDescriptionEditorView : UserControl
{
    private const int MaximumInsertedImageWidth = 1600;
    private readonly MethodDescriptionDocumentCodec _codec = new();
    private readonly DispatcherTimer _commitTimer;
    private bool _isLoading;
    private bool _hasPendingChanges;

    public MethodDescriptionEditorView()
    {
        InitializeComponent();
        _commitTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _commitTimer.Tick += (_, _) => CommitPendingChanges();
        IsKeyboardFocusWithinChanged += (_, args) =>
        {
            if (args.NewValue is false) CommitPendingChanges();
        };
        Unloaded += (_, _) => CommitPendingChanges();
    }

    public event EventHandler<MethodDescriptionChangedEventArgs>? DescriptionChanged;
    public event EventHandler? CloseRequested;

    public void Open(string methodName, string? descriptionDocument)
    {
        _commitTimer.Stop();
        _isLoading = true;
        try
        {
            Editor.Document = _codec.Decode(descriptionDocument);
            _hasPendingChanges = false;
            StatusText.Text = methodName;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or ArgumentException)
        {
            StatusText.Text = "The saved description could not be loaded.";
            MessageBox.Show(Window.GetWindow(this), exception.Message, "Method description", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _isLoading = false;
        }
    }

    public void CommitPendingChanges()
    {
        _commitTimer.Stop();
        if (!_hasPendingChanges || _isLoading) return;

        try
        {
            var encoded = _codec.Encode(Editor.Document);
            _hasPendingChanges = false;
            DescriptionChanged?.Invoke(this, new MethodDescriptionChangedEventArgs(encoded));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or ArgumentException)
        {
            MessageBox.Show(Window.GetWindow(this), exception.Message, "Method description", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void ReleaseDocument()
    {
        _commitTimer.Stop();
        _isLoading = true;
        Editor.Document = new FlowDocument(new Paragraph());
        _hasPendingChanges = false;
        _isLoading = false;
    }

    private void CloseOnClick(object sender, RoutedEventArgs e)
    {
        CommitPendingChanges();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void EditorOnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading) return;
        _hasPendingChanges = true;
        ScheduleCommit();
    }

    private void InsertImageOnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Insert image", Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        try
        {
            var image = new Image
            {
                Source = LoadEmbeddedBitmap(dialog.FileName),
                MaxWidth = 720,
                Stretch = System.Windows.Media.Stretch.Uniform,
                Margin = new Thickness(2)
            };
            var insertionPosition = Editor.CaretPosition.GetInsertionPosition(LogicalDirection.Forward) ?? Editor.Document.ContentEnd;
            var container = new InlineUIContainer(image, insertionPosition);
            Editor.CaretPosition = container.ElementEnd.GetInsertionPosition(LogicalDirection.Forward) ?? Editor.Document.ContentEnd;
            _hasPendingChanges = true;
            ScheduleCommit();
            Editor.Focus();
            StatusText.Text = "Image inserted.";
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException)
        {
            MessageBox.Show(Window.GetWindow(this), exception.Message, "Insert image", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LoadDocumentOnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Load method description", Filter = "Method description package|*.xamlpackage|All files|*.*" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        try
        {
            _isLoading = true;
            Editor.Document = _codec.LoadFile(dialog.FileName);
            _hasPendingChanges = true;
            ScheduleCommit();
            StatusText.Text = "Description loaded from file.";
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or ArgumentException)
        {
            MessageBox.Show(Window.GetWindow(this), exception.Message, "Load method description", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void SaveDocumentOnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save method description",
            Filter = "Method description package|*.xamlpackage",
            DefaultExt = ".xamlpackage",
            AddExtension = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        try
        {
            _codec.SaveFile(Editor.Document, dialog.FileName);
            StatusText.Text = "Description saved to file.";
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or ArgumentException)
        {
            MessageBox.Show(Window.GetWindow(this), exception.Message, "Save method description", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void FontSizeOnSelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFontSize();
    private void FontSizeOnLostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e) => ApplyFontSize();

    private void ApplyFontSize()
    {
        if (!IsLoaded || _isLoading || !double.TryParse(FontSizeBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var size)) return;
        Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, Math.Clamp(size, 8, 72));
        _hasPendingChanges = true;
        ScheduleCommit();
        Editor.Focus();
    }

    private void ScheduleCommit()
    {
        _commitTimer.Stop();
        _commitTimer.Start();
    }

    private static BitmapImage LoadEmbeddedBitmap(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = MaximumInsertedImageWidth;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}

public sealed class MethodDescriptionChangedEventArgs(string? descriptionDocument) : EventArgs
{
    public string? DescriptionDocument { get; } = descriptionDocument;
}
