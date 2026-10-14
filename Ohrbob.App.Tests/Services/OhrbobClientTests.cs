using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Ohrbob.App.Services;

namespace Ohrbob.App.Tests.Services;

[TestFixture]
public class OhrbobClientTests
{
    private ConfigService _mockConfig = null!;
    private const string ServerUrl = "https://ohrbob-test.local";
    private const string StreamToken = "test-token-value";

    [SetUp]
    public void SetUp()
    {
        _mockConfig = new ConfigService();
        _mockConfig.ServerUrl = ServerUrl;
        _mockConfig.StreamToken = StreamToken;
    }

    [Test]
    public async Task GetStationsAsync_SendsCorrectRequestAndReturnsStations()
    {
        var mockResponseJson = "[{\"id\":\"s1\",\"name\":\"Station 1\"}]";
        var handler = new MockHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(mockResponseJson, System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new OhrbobClient(_mockConfig, handler);
        var stations = await client.GetStationsAsync();

        Assert.That(handler.LastRequest, Is.Not.Null);
        Assert.That(handler.LastRequest.Method, Is.EqualTo(HttpMethod.Get));
        Assert.That(handler.LastRequest.RequestUri?.ToString(), Is.EqualTo($"{ServerUrl}/stream/stations"));
        Assert.That(handler.LastRequest.Headers.Contains("X-Stream-Token"), Is.True);
        
        var headerVal = handler.LastRequest.Headers.GetValues("X-Stream-Token");
        Assert.That(headerVal, Contains.Item(StreamToken));

        Assert.That(stations, Is.Not.Null);
        Assert.That(stations.Count, Is.EqualTo(1));
        Assert.That(stations[0].Id, Is.EqualTo("s1"));
        Assert.That(stations[0].Name, Is.EqualTo("Station 1"));
    }

    [Test]
    public async Task GetStatusAsync_SendsCorrectRequestAndReturnsStatus()
    {
        var mockResponseJson = "{\"isPlaying\":true,\"currentStation\":\"s1\"}";
        var handler = new MockHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(mockResponseJson, System.Text.Encoding.UTF8, "application/json")
            };
        });

        var client = new OhrbobClient(_mockConfig, handler);
        var status = await client.GetStatusAsync();

        Assert.That(handler.LastRequest, Is.Not.Null);
        Assert.That(handler.LastRequest.Method, Is.EqualTo(HttpMethod.Get));
        Assert.That(handler.LastRequest.RequestUri?.ToString(), Is.EqualTo($"{ServerUrl}/stream/status"));
        Assert.That(handler.LastRequest.Headers.Contains("X-Stream-Token"), Is.True);

        var headerVal = handler.LastRequest.Headers.GetValues("X-Stream-Token");
        Assert.That(headerVal, Contains.Item(StreamToken));

        Assert.That(status.ValueKind, Is.EqualTo(JsonValueKind.Object));
        Assert.That(status.GetProperty("isPlaying").GetBoolean(), Is.True);
        Assert.That(status.GetProperty("currentStation").GetString(), Is.EqualTo("s1"));
    }

    [Test]
    public async Task PlayStationAsync_SendsCorrectRequest()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new OhrbobClient(_mockConfig, handler);
        await client.PlayStationAsync("s1");

        Assert.That(handler.LastRequest, Is.Not.Null);
        Assert.That(handler.LastRequest.Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(handler.LastRequest.RequestUri?.ToString(), Is.EqualTo($"{ServerUrl}/stream/play-station/s1"));
        Assert.That(handler.LastRequest.Headers.Contains("X-Stream-Token"), Is.True);

        var headerVal = handler.LastRequest.Headers.GetValues("X-Stream-Token");
        Assert.That(headerVal, Contains.Item(StreamToken));
    }

    [Test]
    public async Task SyncStatusAsync_SendsCorrectRequestAndPayload()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new OhrbobClient(_mockConfig, handler);
        await client.SyncStatusAsync("ep123", "s1", 45);

        Assert.That(handler.LastRequest, Is.Not.Null);
        Assert.That(handler.LastRequest.Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(handler.LastRequest.RequestUri?.ToString(), Is.EqualTo($"{ServerUrl}/stream/status"));
        Assert.That(handler.LastRequest.Headers.Contains("X-Stream-Token"), Is.True);

        var headerVal = handler.LastRequest.Headers.GetValues("X-Stream-Token");
        Assert.That(headerVal, Contains.Item(StreamToken));

        Assert.That(handler.LastRequestContent, Is.Not.Null);
        using var jsonDoc = JsonDocument.Parse(handler.LastRequestContent);
        var root = jsonDoc.RootElement;
        Assert.That(root.GetProperty("episodeId").GetString(), Is.EqualTo("ep123"));
        Assert.That(root.GetProperty("stationId").GetString(), Is.EqualTo("s1"));
        Assert.That(root.GetProperty("positionSeconds").GetInt32(), Is.EqualTo(45));
    }

    [Test]
    public async Task NextAsync_SendsCorrectRequest()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new OhrbobClient(_mockConfig, handler);
        await client.NextAsync();

        Assert.That(handler.LastRequest, Is.Not.Null);
        Assert.That(handler.LastRequest.Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(handler.LastRequest.RequestUri?.ToString(), Is.EqualTo($"{ServerUrl}/stream/next"));
        Assert.That(handler.LastRequest.Headers.Contains("X-Stream-Token"), Is.True);

        var headerVal = handler.LastRequest.Headers.GetValues("X-Stream-Token");
        Assert.That(headerVal, Contains.Item(StreamToken));
    }
}

public class MockHttpMessageHandler : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestContent { get; private set; }
    
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFunc;

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFunc)
    {
        _responseFunc = responseFunc;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
    {
        LastRequest = request;
        if (request.Content != null)
        {
            LastRequestContent = await request.Content.ReadAsStringAsync(cancellationToken);
        }
        else
        {
            LastRequestContent = null;
        }
        return _responseFunc(request);
    }
}
