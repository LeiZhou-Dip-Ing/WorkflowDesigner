using WorkflowCore.WpfDemo.Models;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class RuntimeEventItemTests
{
    [Fact]
    public void CompletedActionWithoutOutput_ShowsSucceededStatus()
    {
        var item = CreateItem();

        item.Start(DateTimeOffset.Now);
        item.Complete(DateTimeOffset.Now);

        Assert.Equal("Succeeded", item.Status);
        Assert.Equal(string.Empty, item.Result);
        Assert.True(item.IsCompleted);
    }

    [Fact]
    public void ExplicitActionOutput_IsShownInsteadOfVariableNoise()
    {
        var item = CreateItem();
        item.Start(DateTimeOffset.Now);

        item.CaptureOutput("Greeting = Hello, Max!", isExplicitOutput: false);
        item.CaptureOutput("Hello, Max!", isExplicitOutput: true);
        item.Complete(DateTimeOffset.Now);

        Assert.Equal("Succeeded", item.Status);
        Assert.Equal("Hello, Max!", item.Result);
    }

    [Fact]
    public void FailedAction_ShowsErrorAsItsResult()
    {
        var item = CreateItem();
        item.Start(DateTimeOffset.Now);

        item.Fail(DateTimeOffset.Now, "cannot continue");

        Assert.Equal("Failed", item.Status);
        Assert.Equal("cannot continue", item.Result);
        Assert.True(item.IsFailed);
    }

    [Fact]
    public void RunningDelay_ShowsBackendDurationAsCountdown()
    {
        var item = CreateItem();
        var startedAt = new DateTimeOffset(2026, 7, 17, 10, 0, 0, TimeSpan.Zero);

        item.Start(startedAt, durationMilliseconds: 1_000);
        item.UpdateCountdown(startedAt.AddMilliseconds(400));

        Assert.Equal("Waiting", item.Status);
        Assert.Equal("0.6s remaining", item.Result);
    }

    [Fact]
    public void ActionTime_ShowsLocalStartTimeAndIsNotOverwrittenWhenItFinishes()
    {
        var item = CreateItem();
        var startedAt = DateTimeOffset.Now.AddSeconds(-2);
        var expectedStartTime = startedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff");

        item.Start(startedAt);
        item.Complete(startedAt.AddSeconds(2));

        Assert.Equal(expectedStartTime, item.Time);
    }

    private static RuntimeEventItem CreateItem()
        => new()
        {
            ActionExecutionId = Guid.NewGuid(),
            ActionName = "Greeting",
            MethodName = "Main",
            LineNumber = 2
        };
}
