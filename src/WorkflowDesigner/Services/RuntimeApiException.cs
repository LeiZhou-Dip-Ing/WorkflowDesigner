using System.Net;
using System.Text.Json.Nodes;

namespace WorkflowCore.WpfDemo.Services;

public class RuntimeApiException : Exception
{
    public RuntimeApiException(
        HttpStatusCode statusCode,
        string responseBody,
        string? message = null)
        : base(string.IsNullOrWhiteSpace(message)
            ? CreateMessage(statusCode, responseBody)
            : message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody ?? string.Empty;
        ProblemDetail = ReadProblemDetail(ResponseBody);
    }

    public HttpStatusCode StatusCode { get; }

    public string ResponseBody { get; }

    public string? ProblemDetail { get; }

    private static string CreateMessage(HttpStatusCode statusCode, string responseBody)
    {
        var detail = ReadProblemDetail(responseBody);
        return string.IsNullOrWhiteSpace(detail)
            ? $"Workflow Runtime returned HTTP {(int)statusCode} ({statusCode})."
            : detail;
    }

    private static string? ReadProblemDetail(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody)) return null;
        try
        {
            var document = JsonNode.Parse(responseBody);
            return document?["detail"]?.ToString()
                ?? document?["error"]?.ToString()
                ?? document?["title"]?.ToString();
        }
        catch (System.Text.Json.JsonException)
        {
            return responseBody;
        }
    }
}
