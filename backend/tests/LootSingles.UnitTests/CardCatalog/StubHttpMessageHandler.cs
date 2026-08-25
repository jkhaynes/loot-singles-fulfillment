using System.Net;

namespace LootSingles.UnitTests.CardCatalog;

/// <summary>
/// Hand-rolled <see cref="HttpMessageHandler"/> stub for provider HTTP tests (research.md §9 -
/// no mocking library in this codebase). Returns a fixed response, or throws a fixed exception,
/// regardless of the request sent.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage>? _responseFactory;
    private readonly Exception? _exceptionToThrow;

    private StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage>? responseFactory,
        Exception? exceptionToThrow
    )
    {
        _responseFactory = responseFactory;
        _exceptionToThrow = exceptionToThrow;
    }

    public static StubHttpMessageHandler ReturningJson(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK
    ) => new(_ => new HttpResponseMessage(statusCode) { Content = new StringContent(json) }, null);

    public static StubHttpMessageHandler Throwing(Exception exception) => new(null, exception);

    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        LastRequest = request;

        if (_exceptionToThrow is not null)
        {
            throw _exceptionToThrow;
        }

        return Task.FromResult(_responseFactory!(request));
    }
}
