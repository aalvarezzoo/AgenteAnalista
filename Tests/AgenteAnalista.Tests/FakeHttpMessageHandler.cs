using System.Net.Http;

namespace AgenteAnalista.Tests;

/// <summary>HttpMessageHandler de mentira para no pegarle a ninguna API/instalación real en los tests.</summary>
internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(responder(request));
}
