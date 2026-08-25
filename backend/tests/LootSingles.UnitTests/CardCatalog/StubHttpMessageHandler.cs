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

    /// <summary>
    /// For providers that make more than one differently-shaped call (e.g. a reference-data
    /// lookup followed by a record lookup): routes each request to its own response based on
    /// the request itself (typically its URI).
    /// </summary>
    public static StubHttpMessageHandler RespondingPerRequest(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory
    ) => new(responseFactory, null);

    public HttpRequestMessage? LastRequest { get; private set; }

    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        LastRequest = request;
        Requests.Add(request);

        if (_exceptionToThrow is not null)
        {
            throw _exceptionToThrow;
        }

        return Task.FromResult(_responseFactory!(request));
    }
}
