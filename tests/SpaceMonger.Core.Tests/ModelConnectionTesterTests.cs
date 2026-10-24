using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using SpaceMonger.Core.Services.Assistant;
namespace SpaceMonger.Core.Tests;
public class ModelConnectionTesterTests
{
    [Fact]
    public async Task ConnectionTestUsesSelectedModelAndNativeEndpoint()
    {
        var handler=new Handler();var factory=Substitute.For<IHttpClientFactory>();factory.CreateClient("Anthropic").Returns(new HttpClient(handler));
        (await new ModelConnectionTester(factory).ValidateAsync("fixture-key","http://127.0.0.1:39386","selected-model",CancellationToken.None)).Should().BeTrue();
        handler.Model.Should().Be("selected-model");handler.Uri.Should().Be("http://127.0.0.1:39386/v1/messages");
    }
    private sealed class Handler:HttpMessageHandler
    {
        public string? Model,Uri;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Uri=request.RequestUri!.ToString();using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));Model=body.RootElement.GetProperty("model").GetString();
            var stream="data: {\"type\":\"message_start\"}\n\ndata: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"}}\n\ndata: {\"type\":\"message_stop\"}\n\n";
            return new(HttpStatusCode.OK){Content=new StringContent(stream,Encoding.UTF8,"text/event-stream")};
        }
    }
}
