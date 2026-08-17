using Prism.Commands;
using WorkflowCore.WpfDemo.Docking;
using WorkflowCore.WpfDemo.Models;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class DockingBehaviorTests
{
    [Fact]
    public void ClosingLayoutDocument_InvokesPaneCloseCommandToRemoveDocumentsSourceItem()
    {
        var closeCount = 0;
        var pane = new DockPaneItem
        {
            ContentId = "method:test",
            Content = new object(),
            CloseCommand = new DelegateCommand(() => closeCount++)
        };

        DockingBehavior.SynchronizeClosedDocument(pane);

        Assert.Equal(1, closeCount);
    }
}
