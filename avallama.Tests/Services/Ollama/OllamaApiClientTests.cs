// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using avallama.Constants.Keys;
using avallama.Constants.States;
using avallama.Exceptions;
using avallama.Models.Dtos;
using avallama.Services.Ollama;
using avallama.Tests.Fixtures;
using avallama.Tests.Mocks;
using avallama.Utilities.Time;
using Moq;
using Moq.Protected;
using Xunit;

namespace avallama.Tests.Services.Ollama;

public class OllamaApiClientTests : IClassFixture<TestServicesFixture>
{
    private readonly TestServicesFixture _fixture;
    private readonly Mock<HttpMessageHandler> _handlerMock;

    public OllamaApiClientTests(TestServicesFixture fixture)
    {
        _fixture = fixture;

        _fixture.ConfigMock
            .Setup(x => x.ReadSetting(ConfigurationKey.ApiHost))
            .Returns("localhost");

        _fixture.ConfigMock
            .Setup(x => x.ReadSetting(ConfigurationKey.ApiPort))
            .Returns("11434");

        // Create a mock HttpMessageHandler
        _handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

        // Setup a default SendAsync behavior
        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                if (request.RequestUri!.AbsolutePath == "/api/version")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"version\":\"1.0.0\"}")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });
    }

    private Mock<IHttpClientFactory> CreateMockHttpClientFactory(HttpClient checkClient, HttpClient heavyClient)
    {
        var mockFactory = new Mock<IHttpClientFactory>();

        mockFactory
            .Setup(x => x.CreateClient("OllamaCheckHttpClient"))
            .Returns(checkClient);

        mockFactory
            .Setup(x => x.CreateClient("OllamaHeavyHttpClient"))
            .Returns(heavyClient);

        return mockFactory;
    }

    [Fact]
    public async Task CheckConnectionAsync_WithDelayedResponse_RetriesConnecting()
    {
        var timeMock = new TimeProviderMock();
        var delayerMock = new TaskDelayerMock(timeMock);

        var callCount = 0;
        var stateEvents = new List<OllamaApiState?>();

        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                if (request.RequestUri!.AbsolutePath != "/api/version")
                    return new HttpResponseMessage(HttpStatusCode.NotFound);

                callCount++; // doesn't need interlocked because it runs synchronously with fake time

                if (callCount < 4)
                    return new HttpResponseMessage(HttpStatusCode.NotFound);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"version\":\"1.0.0\"}")
                };
            });

        var httpClient = new HttpClient(_handlerMock.Object);
        var mockHttpClientFactory = CreateMockHttpClientFactory(httpClient, httpClient);

        var oac = CreateOllamaApiClient(mockHttpClientFactory.Object, timeMock, delayerMock);

        oac.StatusChanged += status => { stateEvents.Add(status.ApiState); };

        await oac.CheckConnectionAsync();

        Assert.Equal(1, stateEvents.Count(e => e == OllamaApiState.Connected));
        Assert.Equal(1, stateEvents.Count(e => e == OllamaApiState.Reconnecting));
        Assert.Equal(4, callCount);
        Assert.True(timeMock.Elapsed.TotalMilliseconds >= 100);
    }

    [Fact]
    public async Task CheckConnectionAsync_WhenApiNeverResponds_SetsFaultedStateAfterMaxRetries()
    {
        var timeMock = new TimeProviderMock();
        var delayerMock = new TaskDelayerMock(timeMock);

        var callCount = 0;

        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(() =>
            {
                callCount++;
                throw new HttpRequestException("Connection refused");
            });

        var httpClient = new HttpClient(_handlerMock.Object);
        var mockHttpClientFactory = CreateMockHttpClientFactory(httpClient, httpClient);

        var oac = CreateOllamaApiClient(mockHttpClientFactory.Object, timeMock, delayerMock);

        var stateEvents = new List<OllamaApiState?>();
        oac.StatusChanged += status => { stateEvents.Add(status.ApiState); };

        await oac.CheckConnectionAsync();

        Assert.Equal(OllamaApiState.Connecting, stateEvents[0]);
        Assert.Equal(OllamaApiState.Reconnecting, stateEvents[1]);
        Assert.Equal(OllamaApiState.Failed, stateEvents[2]);
        Assert.Equal(3, stateEvents.Count);
        Assert.True(callCount > 1, $"Expected multiple calls, but got {callCount}");
    }

    [Fact]
    public async Task CheckConnectionAsync_WhenRequestTimeouts_RetriesConnecting()
    {
        var callCount = 0;

        var timeMock = new TimeProviderMock();
        var delayerMock = new TaskDelayerMock(timeMock);

        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .Returns((HttpRequestMessage _, CancellationToken _) =>
            {
                callCount++;

                if (callCount == 1)
                {
                    timeMock.Advance(TimeSpan.FromMilliseconds(50));

                    // simulating a timeout
                    throw new TaskCanceledException("Request timed out");
                }

                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"version\":\"1.0.0\"}")
                };

                return Task.FromResult(response);
            });

        var httpClient = new HttpClient(_handlerMock.Object);
        var mockHttpClientFactory = CreateMockHttpClientFactory(httpClient, httpClient);

        var oac = CreateOllamaApiClient(mockHttpClientFactory.Object, timeMock, delayerMock);

        var stateEvents = new List<OllamaApiState?>();
        oac.StatusChanged += status => { stateEvents.Add(status.ApiState); };

        await oac.CheckConnectionAsync();

        Assert.Equal(2, callCount);
        Assert.Equal(OllamaApiState.Connecting, stateEvents[0]);
        Assert.Equal(OllamaApiState.Reconnecting, stateEvents[1]);
        Assert.Equal(OllamaApiState.Connected, oac.Status.ApiState);
    }

    [Fact]
    public async Task GenerateMessageAsync_ReturnsResponses_AndSetsConnectedStatus()
    {
        var timeMock = new TimeProviderMock();
        var delayerMock = new TaskDelayerMock(timeMock);

        var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);

        var msg1 = new MessageContent
        {
            Content = "Hello"
        };
        var msg2 = new MessageContent
        {
            Content = "World!"
        };

        var responses = new[]
        {
            JsonSerializer.Serialize(new OllamaResponse
            {
                Model = "TestLlama", CreatedAt = "2023-08-04T08:52:19.385406455-07:00", Message = msg1, Done = false
            }),
            JsonSerializer.Serialize(new OllamaResponse
            {
                Model = "TestLlama", CreatedAt = "2023-08-04T08:52:19.385406455-07:00", Message = msg2, Done = false
            })
        };

        var responseContent = string.Join("\n", responses);

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseContent)
            });

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.AbsolutePath.Contains("/api/version")),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        var httpClient = new HttpClient(mockHandler.Object);
        var mockHttpClientFactory = CreateMockHttpClientFactory(httpClient, httpClient);

        var oac = CreateOllamaApiClient(mockHttpClientFactory.Object, timeMock, delayerMock);

        var stateEvents = new List<OllamaApiState?>();
        oac.StatusChanged += status => { stateEvents.Add(status.ApiState); };

        await oac.CheckConnectionAsync();

        var results = new List<OllamaResponse>();
        await foreach (var r in oac.GenerateMessageAsync([], "test-model", CancellationToken.None))
        {
            results.Add(r);
        }

        Assert.Equal(2, results.Count);
        Assert.Equal("Hello", results[0].Message?.Content);
        Assert.Equal("World!", results[1].Message?.Content);

        Assert.Contains(stateEvents, state => state == OllamaApiState.Connected);
    }

    [Fact]
    public async Task GenerateMessage_InvalidJson_OnlyShowsValid()
    {
        var timeMock = new TimeProviderMock();
        var delayerMock = new TaskDelayerMock(timeMock);

        var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);

        const string responseContent =
            "INVALID_JSON\n{\"model\": \"llama3.2\",\"created_at\": \"2023-08-04T08:52:19.385406455-07:00\",\"message\": {\"role\": \"assistant\", \"content\": \"hello\"},\"done\": false}";

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseContent)
            });

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.AbsolutePath.Contains("/api/version")),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        var httpClient = new HttpClient(mockHandler.Object);
        var mockHttpClientFactory = CreateMockHttpClientFactory(httpClient, httpClient);

        var oac = CreateOllamaApiClient(mockHttpClientFactory.Object, timeMock, delayerMock);

        var results = new List<OllamaResponse>();
        await foreach (var r in oac.GenerateMessageAsync([], "llama3.2", CancellationToken.None))
        {
            results.Add(r);
        }

        // Only the valid JSON line should be returned
        Assert.Single(results);
        Assert.Equal("hello", results[0].Message?.Content);
    }

    [Fact]
    public async Task GenerateMessage_WhenConnectionLost_UpdatesStateToFailed()
    {
        var timeMock = new TimeProviderMock();
        var delayerMock = new TaskDelayerMock(timeMock);

        var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.AbsolutePath.Contains("/api/version")),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.AbsolutePath.Contains("/api/chat")),
                ItExpr.IsAny<CancellationToken>()
            )
            .ThrowsAsync(new HttpRequestException("Connection reset by peer"));

        var httpClient = new HttpClient(mockHandler.Object);
        var mockHttpClientFactory = CreateMockHttpClientFactory(httpClient, httpClient);

        var oac = CreateOllamaApiClient(mockHttpClientFactory.Object, timeMock, delayerMock);

        var stateEvents = new List<OllamaApiState?>();
        oac.StatusChanged += status => { stateEvents.Add(status.ApiState); };

        await Assert.ThrowsAsync<OllamaLocalServerUnreachableException>(async () =>
        {
            await foreach (var _ in oac.GenerateMessageAsync([], "model", CancellationToken.None))
            {
            }
        });

        Assert.Equal(OllamaApiState.Failed, oac.Status.ApiState);
        Assert.Contains(OllamaApiState.Failed, stateEvents);
    }

    [Fact]
    public async Task GenerateMessageAsync_WhenModelLoadIsSlow_ShouldNotTimeout()
    {
        var timeMock = new TimeProviderMock();
        var delayerMock = new TaskDelayerMock(timeMock);

        var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var msg = new MessageContent { Content = "Finally loaded!" };
        var responseJson = JsonSerializer.Serialize(new OllamaResponse
        {
            Model = "HeavyModel",
            Message = msg,
            Done = true
        });

        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.AbsolutePath.Contains("/api/version")),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        // slow chat response
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.AbsolutePath.Contains("/api/chat")),
                ItExpr.IsAny<CancellationToken>()
            )
            .Returns((HttpRequestMessage _, CancellationToken token) =>
            {
                // simulating a delay
                timeMock.Advance(TimeSpan.FromMilliseconds(200));

                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson)
                };

                return Task.FromResult(response);
            });

        // Create CheckClient with short timeout (simulating fast ping requirement)
        var checkClient = new HttpClient(mockHandler.Object);
        checkClient.Timeout = TimeSpan.FromMilliseconds(50);

        // Create HeavyClient with long timeout (simulating patience for model loading)
        var heavyClient = new HttpClient(mockHandler.Object);
        heavyClient.Timeout = TimeSpan.FromMilliseconds(500);

        var mockHttpClientFactory = CreateMockHttpClientFactory(checkClient, heavyClient);

        var oac = CreateOllamaApiClient(mockHttpClientFactory.Object, timeMock, delayerMock);

        var results = new List<OllamaResponse>();

        try
        {
            await foreach (var r in oac.GenerateMessageAsync([], "heavy-model", CancellationToken.None))
            {
                results.Add(r);
            }
        }
        catch (Exception)
        {
            // should not happen if heavyclient is used correctly
        }

        Assert.Single(results);
        Assert.Equal("Finally loaded!", results[0].Message?.Content);
        Assert.NotEqual(OllamaApiState.Disconnected, oac.Status.ApiState);
        Assert.NotEqual(OllamaApiState.Failed, oac.Status.ApiState);
    }

    private OllamaApiClient CreateOllamaApiClient(
        IHttpClientFactory mockHttpClientFactory,
        ITimeProvider? timeMock,
        ITaskDelayer? delayerMock)
    {
        return new OllamaApiClient(
            _fixture.ConfigMock.Object,
            _fixture.NetworkManagerMock.Object,
            mockHttpClientFactory,
            timeMock,
            delayerMock)
        {
            MaxRetryingTime = TimeSpan.FromSeconds(2),
            ConnectionCheckInterval = TimeSpan.FromMilliseconds(50)
        };
    }
}
