using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using WorkflowCore.WpfDemo.Controls;
using WorkflowCore.WpfDemo.Editor;
using WorkflowCore.WpfDemo.Models;
using WorkflowCore.WpfDemo.Services.Designer;
using WorkflowDesigner.WpfSdk;
using WorkflowRuntime.Contracts;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class ActionPropertyPanelLayoutTests
{
    [Fact]
    public void ShortDynamicProperties_StretchAcrossTheSharedPropertyPanel()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                VerifyShortPropertyRowsStretch();
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
            throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }

    private static void VerifyShortPropertyRowsStretch()
    {
        var app = new App();
        app.InitializeComponent();
        var registry = new WorkflowDesignerRegistry();
        WorkflowDesignerRegistryHost.Initialize(registry);
        BuiltInDesignerRegistration.Register(registry);

        var action = WorkflowAction.Create("test");
        var properties = new[]
        {
            new ActionPropertyItem(action, Field("Method"), () => { }),
            new ActionPropertyItem(action, Field("param1"), () => { }),
            new ActionPropertyItem(action, Field("param2"), () => { }),
            new ActionPropertyItem(action, Field("publishPreview", "boolean", "checkbox"), () => { })
        };
        var propertiesView = CollectionViewSource.GetDefaultView(properties);
        propertiesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ActionPropertyItem.Category)));

        var panel = new ActionPropertyPanel
        {
            Width = 430,
            Height = 500,
            ItemsSource = propertiesView
        };

        panel.Measure(new Size(panel.Width, panel.Height));
        panel.Arrange(new Rect(0, 0, panel.Width, panel.Height));
        panel.UpdateLayout();

        var row = FindVisual<System.Windows.Controls.Grid>(panel, element => ReferenceEquals(element.DataContext, properties[0]));
        Assert.NotNull(row);
        Assert.True(row.ActualWidth >= panel.ActualWidth - 2,
            $"Dynamic property row width {row.ActualWidth} did not fill panel width {panel.ActualWidth}.");
        Assert.NotNull(FindVisual<System.Windows.Controls.CheckBox>(panel, _ => true));
        var groupExpander = FindVisual<System.Windows.Controls.Expander>(panel, _ => true);
        Assert.NotNull(groupExpander);
        Assert.Same(app.TryFindResource("PropertyGroupExpanderStyle"), groupExpander.Style);
    }

    private static WorkflowActionFieldDto Field(
        string name,
        string valueType = "string",
        string editor = "text")
        => new()
        {
            Name = name,
            DisplayName = name,
            ValueType = valueType,
            Direction = "property",
            Category = "Action",
            Editor = editor
        };

    private static T? FindVisual<T>(DependencyObject root, Func<T, bool> predicate)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T candidate && predicate(candidate))
            {
                return candidate;
            }

            var descendant = FindVisual(child, predicate);
            if (descendant != null)
            {
                return descendant;
            }
        }

        return null;
    }
}
