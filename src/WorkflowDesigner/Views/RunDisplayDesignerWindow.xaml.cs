using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Xml;
using ICSharpCode.WpfDesign;
using ICSharpCode.WpfDesign.Designer;
using ICSharpCode.WpfDesign.Designer.Services;
using ICSharpCode.WpfDesign.Designer.Xaml;
using ICSharpCode.WpfDesign.XamlDom;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.ViewModels;

namespace WorkflowCore.WpfDemo.Views;

public partial class RunDisplayDesignerView : UserControl
{
    private static readonly object MetadataLock = new();
    private static bool _metadataRegistered;
    private ISelectionService? _selectionService;
    private bool _initialized;

    public RunDisplayDesignerView()
    {
        InitializeComponent();
        ToolboxList.ItemsSource = CreateToolboxItems();
        Loaded += (_, _) => InitializeDesigner();
        Unloaded += (_, _) => DetachDesigner();
    }

    private RunDisplayEditorViewModel? ViewModel => DataContext as RunDisplayEditorViewModel;

    private static ObservableCollection<RunDisplayToolboxItem> CreateToolboxItems()
        =>
        [
            new("Text", "\uE8D2", typeof(TextBlock)),
            new("Button", "\uE8FB", typeof(Button)),
            new("Text box", "\uE70F", typeof(TextBox)),
            new("Check box", "\uE73A", typeof(CheckBox)),
            new("Progress", "\uE895", typeof(ProgressBar)),
            new("Image", "\uEB9F", typeof(Image)),
            new("Border", "\uE7C8", typeof(Border)),
            new("Stack panel", "\uE8A0", typeof(StackPanel)),
            new("Grid", "\uF0E2", typeof(Grid))
        ];

    private void InitializeDesigner()
    {
        if (_initialized) return;
        lock (MetadataLock)
        {
            if (!_metadataRegistered)
            {
                BasicMetadata.Register();
                _metadataRegistered = true;
            }
        }

        _initialized = true;
        LoadXaml(ViewModel?.RunDisplay.Xaml ?? RunDisplayDefaults.InitialXaml);
    }

    private void LoadXaml(string xaml)
    {
        try
        {
            DetachSelectionService();
            DesignerSurface.UnloadDesigner();
            var settings = new XamlLoadSettings();
            settings.DesignerAssemblies.Add(GetType().Assembly);
            using var reader = XmlReader.Create(new StringReader(xaml));
            DesignerSurface.LoadDesigner(reader, settings);
            _selectionService = DesignerSurface.DesignContext.Services.Selection;
            _selectionService.SelectionChanged += SelectionServiceOnSelectionChanged;
            UpdatePropertyGridSelection();
            XamlEditor.Text = ExportXaml();
            PreviewHost.Content = null;
            StatusText.Text = "Ready. Select a toolbox item, then drag on the page.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Could not load XAML", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "XAML load failed.";
        }
    }

    private string ExportXaml()
    {
        var builder = new StringBuilder();
        using var writer = new XamlXmlWriter(builder, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true });
        DesignerSurface.SaveDesigner(writer);
        return builder.ToString();
    }

    private void ToolboxOnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ToolboxList.SelectedItem is not RunDisplayToolboxItem item || DesignerSurface.DesignContext == null) return;
        DesignerSurface.DesignContext.Services.Tool.CurrentTool = new CreateComponentTool(item.ControlType);
        StatusText.Text = $"{item.DisplayName} selected. Drag a rectangle on the page to place it.";
    }

    private void SelectionServiceOnSelectionChanged(object? sender, DesignItemCollectionEventArgs e) => UpdatePropertyGridSelection();

    private void UpdatePropertyGridSelection()
    {
        if (_selectionService != null) DesignerPropertyGrid.SelectedItems = _selectionService.SelectedItems;
    }

    private void UndoOnClick(object sender, RoutedEventArgs e)
    {
        DesignerSurface.DesignContext?.Services.GetService<UndoService>()?.Undo();
        CommitDesignerXaml();
    }

    private void RedoOnClick(object sender, RoutedEventArgs e)
    {
        DesignerSurface.DesignContext?.Services.GetService<UndoService>()?.Redo();
        CommitDesignerXaml();
    }

    private void RefreshXamlOnClick(object sender, RoutedEventArgs e)
    {
        CommitDesignerXaml();
        XamlEditor.Text = ViewModel?.RunDisplay.Xaml ?? ExportXaml();
        InspectorTabs.SelectedItem = XamlTab;
        StatusText.Text = "XAML refreshed from the design surface.";
    }

    private void ApplyXamlOnClick(object sender, RoutedEventArgs e)
    {
        LoadXaml(XamlEditor.Text);
        CommitDesignerXaml();
    }

    private void PreviewOnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            CommitDesignerXaml();
            using var reader = XmlReader.Create(new StringReader(ViewModel?.RunDisplay.Xaml ?? ExportXaml()));
            PreviewHost.Content = XamlReader.Load(reader);
            InspectorTabs.SelectedItem = PreviewTab;
            StatusText.Text = "Runtime preview loaded from the generated XAML.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Could not preview XAML", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DesignerSurfaceOnPreviewMouseUp(object sender, MouseButtonEventArgs e) => CommitDesignerXaml();

    private void DesignerSurfaceOnPreviewKeyUp(object sender, KeyEventArgs e) => CommitDesignerXaml();

    private void CommitDesignerXaml()
    {
        if (DesignerSurface.DesignContext != null) ViewModel?.UpdateXaml(ExportXaml());
    }

    private void DetachSelectionService()
    {
        if (_selectionService == null) return;
        _selectionService.SelectionChanged -= SelectionServiceOnSelectionChanged;
        _selectionService = null;
    }

    private void DetachDesigner()
    {
        if (!_initialized) return;
        CommitDesignerXaml();
        DetachSelectionService();
        DesignerSurface.UnloadDesigner();
        _initialized = false;
    }

    private sealed record RunDisplayToolboxItem(string DisplayName, string Glyph, Type ControlType);
}
