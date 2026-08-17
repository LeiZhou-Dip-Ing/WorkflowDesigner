using System.Net;
using WorkflowCore.WpfDemo.Services;
using Xunit;

namespace WorkflowCore.WpfDemo.Tests;

public sealed class RuntimeApiExceptionTests
{
    [Fact]
    public void ApiError_PreservesStatusBodyAndProblemDetail()
    {
        const string responseBody = "{\"title\":\"Publish failed\",\"detail\":\"Storage is unavailable.\"}";

        var exception = new RuntimeApiException(
            HttpStatusCode.InternalServerError,
            responseBody);

        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        Assert.Equal(responseBody, exception.ResponseBody);
        Assert.Equal("Storage is unavailable.", exception.ProblemDetail);
        Assert.Equal("Storage is unavailable.", exception.Message);
    }

    [Fact]
    public void RevisionConflict_IsA409ApiError()
    {
        var exception = new RuntimeRevisionConflictException(
            "editor-default",
            4,
            5,
            "hash-5",
            responseBody: "{\"detail\":\"Revision conflict.\"}");

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Equal(4, exception.ExpectedRevision);
        Assert.Equal(5, exception.CurrentRevision);
        Assert.Equal("hash-5", exception.CurrentContentHash);
    }
}
