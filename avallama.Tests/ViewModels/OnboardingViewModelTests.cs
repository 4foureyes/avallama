// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System.Threading;
using System.Threading.Tasks;
using avallama.Constants;
using avallama.Constants.States;
using avallama.Models.Ollama;
using avallama.Services.Ollama;
using avallama.Services.Persistence;
using avallama.Tests.Fixtures;
using avallama.ViewModels;
using Moq;
using Xunit;

namespace avallama.Tests.ViewModels;

public class OnboardingViewModelTests(TestServicesFixture fixture) : IClassFixture<TestServicesFixture>
{
    [Fact]
    public async Task SkipConnectionTest_WhenTestingConnection_CancelsTestAndNavigates()
    {
        var ollamaMock = fixture.OllamaMock;
        var configMock = fixture.ConfigMock;
        var messengerMock = fixture.MessengerMock;

        ollamaMock.Setup(x => x.CurrentServiceStatus)
            .Returns(new OllamaServiceStatus(OllamaServiceState.Stopped));

        var capturedToken = CancellationToken.None;

        ollamaMock
            .Setup(x => x.CheckConnectionAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(ct =>
            {
                capturedToken = ct;
            })
            .Returns(async (CancellationToken ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
            });

        var viewModel = new OnboardingViewModel(
            ollamaMock.Object,
            configMock.Object,
            messengerMock.Object);

        var testTask = viewModel.TestConnectionAsync();

        await Task.Delay(50);

        Assert.False(capturedToken.IsCancellationRequested);

        viewModel.SkipConnectionTest();

        var exception = await Record.ExceptionAsync(() => testTask);
        Assert.Null(exception);
        Assert.Equal(OnboardingContent.Scraper, viewModel.Content);

        Assert.True(capturedToken.IsCancellationRequested);
    }
}
