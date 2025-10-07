using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using avallama.Constants;
using avallama.Models;
using avallama.Services;
using avallama.Tests.Extensions;
using avallama.Tests.Fixtures;
using avallama.Tests.Utilities;
using Moq;
using Moq.Protected;
using Xunit;

namespace avallama.Tests.Services;

public class OllamaServiceTests : IClassFixture<TestServicesFixture>
{
    private readonly TestServicesFixture _fixture;
    private readonly Mock<HttpMessageHandler> _handlerMock;

    public OllamaServiceTests(TestServicesFixture fixture)
    {
        _fixture = fixture;

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

    [Fact]
    public async Task CallingOllamaServiceMethod_BeforeStart_Throws()
    {
        var ol = new OllamaService(_fixture.ConfigMock.Object, _fixture.DialogMock.Object,
            new SynchronousAvaloniaDispatcher());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await ol.GetModelParamNum("testllama"); });
    }

    [Fact]
    public async Task CallingOllamaServiceMethod_AfterStart_DoesNotThrow()
    {
        var httpClient = new HttpClient(_handlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:11434")
        };

        var ol = new OllamaService(_fixture.ConfigMock.Object, _fixture.DialogMock.Object,
            new SynchronousAvaloniaDispatcher())
        {
            StartProcessFunc = _ => new Process()
        };
        typeof(OllamaService)
            .GetField("_httpClient",
                BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(ol, httpClient);

        await ol.Start();

        await AsyncAssertExtensions.DoesNotThrowAsync(async () =>
        {
            await ol.GetModelParamNum("testllama");
        });
    }

    // Start() tests

    [Fact]
    public async Task StartingOllamaService_DoesNotThrow()
    {
        var httpClient = new HttpClient(_handlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:11434")
        };

        var ol = new OllamaService(_fixture.ConfigMock.Object, _fixture.DialogMock.Object,
            new SynchronousAvaloniaDispatcher())
        {
            StartProcessFunc = _ => new Process(),
            GetProcessCountFunc = () => 0
        };
        typeof(OllamaService)
            .GetField("_httpClient",
                BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(ol, httpClient);

        await AsyncAssertExtensions.DoesNotThrowAsync(async () => { await ol.Start(); });
    }

    [Fact]
    public async Task StartingOllamaService_WhenMockedRunning_SetsRunning()
    {
        var httpClient = new HttpClient(_handlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:11434")
        };

        var ol = new OllamaService(_fixture.ConfigMock.Object, _fixture.DialogMock.Object,
            new SynchronousAvaloniaDispatcher())
        {
            StartProcessFunc = _ => new Process(),
            GetProcessCountFunc = () => 0
        };
        typeof(OllamaService)
            .GetField("_httpClient",
                BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(ol, httpClient);

        ServiceStatus? reportedStatus = null;
        ol.ServiceStatusChanged += (status, _) => reportedStatus = status;

        await ol.Start();

        Assert.Equal(ServiceStatus.Running, reportedStatus);
    }

    [Fact]
    public async Task StartingOllamaService_WhenProcessIsNull_SetsNotInstalled()
    {
        var httpClient = new HttpClient(_handlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:11434")
        };

        var ol = new OllamaService(_fixture.ConfigMock.Object, _fixture.DialogMock.Object,
            new SynchronousAvaloniaDispatcher())
        {
            StartProcessFunc = _ => null,
            GetProcessCountFunc = () => 0
        };
        typeof(OllamaService)
            .GetField("_httpClient",
                BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(ol, httpClient);

        ServiceStatus? reportedStatus = null;
        ol.ServiceStatusChanged += (status, _) => reportedStatus = status;

        await ol.Start();

        Assert.Equal(ServiceStatus.NotInstalled, reportedStatus);
    }

    [Fact]
    public async Task StartingOllamaService_WithProcessRunning_SetsRunning()
    {
        var httpClient = new HttpClient(_handlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:11434")
        };

        var ol = new OllamaService(_fixture.ConfigMock.Object, _fixture.DialogMock.Object,
            new SynchronousAvaloniaDispatcher())
        {
            StartProcessFunc = _ => null,
            GetProcessCountFunc = () => 1
        };
        typeof(OllamaService)
            .GetField("_httpClient",
                BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(ol, httpClient);

        ServiceStatus? reportedStatus = null;
        ol.ServiceStatusChanged += (status, _) => reportedStatus = status;

        await ol.Start();

        Assert.Equal(ServiceStatus.Running, reportedStatus);
    }

    [Fact]
    public async Task Start_WithDelayedResponse_RetriesUntilRunning()
    {
        var callCount = 0;
        var statusEvents = new List<ServiceStatus?>();

        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .Returns((HttpRequestMessage request, CancellationToken token) =>
            {
                return Task.Run(async () =>
                {
                    if (request.RequestUri!.AbsolutePath == "/api/version")
                    {
                        Interlocked.Increment(ref callCount);
                        // simulate network delay
                        await Task.Delay(500, token);

                        if (callCount < 4)
                            return new HttpResponseMessage(HttpStatusCode.NotFound);

                        return new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent("{\"version\":\"1.0.0\"}")
                        };
                    }

                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }, token);
            });

        var httpClient = new HttpClient(_handlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:11434")
        };

        var ol = new OllamaService(_fixture.ConfigMock.Object, _fixture.DialogMock.Object,
            new SynchronousAvaloniaDispatcher())
        {
            StartProcessFunc = _ => new Process(),
            GetProcessCountFunc = () => 0
        };
        typeof(OllamaService)
            .GetField("_httpClient",
                BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(ol, httpClient);

        ol.ServiceStatusChanged += (status, _) => { statusEvents.Add(status); };

        await ol.Start();

        Assert.Equal(1, statusEvents.Count(e => e == ServiceStatus.Running));
        Assert.Equal(1, statusEvents.Count(e => e == ServiceStatus.Retrying));
        Assert.Equal(4, callCount);
    }

    [Fact]
    public async Task GenerateMessage_ReturnsResponses_AndSetsRunningStatus()
    {
        // Arrange
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

        var httpClient = new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri("http://localhost:11434")
        };

        var ol = new OllamaService(_fixture.ConfigMock.Object, _fixture.DialogMock.Object,
            new SynchronousAvaloniaDispatcher())
        {
            StartProcessFunc = _ => new Process(),
            GetProcessCountFunc = () => 0
        };

        // Inject mocked HttpClient
        typeof(OllamaService)
            .GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(ol, httpClient);

        var statusEvents = new List<(ServiceStatus? status, string? message)>();
        ol.ServiceStatusChanged += (s, m) => statusEvents.Add((s, m));

        await ol.Start();

        // Act
        var results = new List<OllamaResponse>();
        await foreach (var r in ol.GenerateMessage(new List<Message>(), "test-model"))
        {
            results.Add(r);
        }

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Equal("Hello", results[0].Message?.Content);
        Assert.Equal("World!", results[1].Message?.Content);

        Assert.Contains(statusEvents, e => e.status == ServiceStatus.Running);
    }

    [Fact]
    public async Task GenerateMessage_InvalidJson_OnlyShowsValid()
    {
        var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict);

        string responseContent =
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

        var httpClient = new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri("http://localhost:11434")
        };

        var ol = new OllamaService(_fixture.ConfigMock.Object, _fixture.DialogMock.Object,
            new SynchronousAvaloniaDispatcher())
        {
            StartProcessFunc = _ => new Process(),
            GetProcessCountFunc = () => 0
        };

        typeof(OllamaService)
            .GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(ol, httpClient);

        await ol.Start();

        var results = new List<OllamaResponse>();
        await foreach (var r in ol.GenerateMessage([], "llama3.2"))
        {
            results.Add(r);
        }

        // Only the valid JSON line should be returned
        Assert.Single(results);
        Assert.Equal("hello", results[0].Message?.Content);
    }
}
