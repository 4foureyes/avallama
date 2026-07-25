// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Threading.Tasks;
using avallama.Constants.States;
using avallama.Models;
using avallama.Models.Ollama;
using avallama.Tests.Fixtures;
using avallama.ViewModels;
using Moq;
using Xunit;

namespace avallama.Tests.ViewModels;

public class HomeViewModelTests : IClassFixture<TestServicesFixture>
{
    private readonly TestServicesFixture _fixture;

    public HomeViewModelTests(TestServicesFixture fixture)
    {
        _fixture = fixture;
        SetupMock();
    }

    private void SetupMock()
    {
        // Mock db svc to prevent NullReferenceException when testing
        _fixture.DbMock.Setup(db => db.GetConversations())
            .ReturnsAsync([]);

        // Provide empty model list by default
        _fixture.OllamaMock
            .Setup(o => o.GetDownloadedModelsAsync())
            .ReturnsAsync([]);

        _fixture.ModelCacheMock
            .Setup(o => o.GetDownloadedModelsAsync())
            .ReturnsAsync([]);
    }

    private HomeViewModel CreateViewModel()
    {
        return new HomeViewModel(
            _fixture.OllamaMock.Object,
            _fixture.DialogMock.Object,
            _fixture.ConfigMock.Object,
            _fixture.DbMock.Object,
            _fixture.UpdateMock.Object,
            _fixture.ModelCacheMock.Object,
            _fixture.MessengerMock.Object
        );
    }

    [Fact]
    public async Task SearchBoxText_WhenChanged_FiltersConversationsCorrectly()
    {
        _fixture.DbMock.Reset();
        _fixture.OllamaMock.Reset();

        var conv1 = new Conversation("C# Programming", string.Empty) { Id = Guid.NewGuid() };
        var conv2 = new Conversation("Python Scripts", string.Empty) { Id = Guid.NewGuid() };
        var conv3 = new Conversation("Avalonia UI Design", string.Empty) { Id = Guid.NewGuid() };

        _fixture.DbMock.Setup(db => db.GetConversations()).ReturnsAsync([conv1, conv2, conv3]);

        var vm = CreateViewModel();

        _fixture.OllamaMock.Raise(x =>
            x.StatusChanged += null, new OllamaServiceStatus(OllamaServiceState.Ready));

        await vm.InitializeAsync();

        Assert.Equal(3, vm.ConversationViewModels.Count);

        vm.SearchBoxText = "Python";

        Assert.Single(vm.ConversationViewModels);
        Assert.Equal(conv2, vm.ConversationViewModels[0].Conversation);
    }

    [Fact]
    public async Task SearchBoxText_WhenCleared_RestoresAllConversations()
    {
        _fixture.DbMock.Reset();
        _fixture.OllamaMock.Reset();

        var conv1 = new Conversation("C# Programming", string.Empty) { Id = Guid.NewGuid() };
        var conv2 = new Conversation("Python Scripts", string.Empty) { Id = Guid.NewGuid() };

        _fixture.DbMock.Setup(db => db.GetConversations()).ReturnsAsync([conv1, conv2]);

        var vm = CreateViewModel();

        _fixture.OllamaMock.Raise(x =>
            x.StatusChanged += null, new OllamaServiceStatus(OllamaServiceState.Ready));

        await vm.InitializeAsync();

        vm.SearchBoxText = "C#";
        vm.SearchBoxText = string.Empty;

        Assert.Equal(2, vm.ConversationViewModels.Count);
        Assert.Contains(vm.ConversationViewModels, cvm => cvm.Conversation == conv1);
        Assert.Contains(vm.ConversationViewModels, cvm => cvm.Conversation == conv2);
    }

    [Fact]
    public async Task CreateNewConversation_AddsToCollectionAndSelectsIt()
    {
        var vm = CreateViewModel();
        await vm.InitializeAsync();

        var initialCount = vm.ConversationViewModels.Count;

        await vm.CreateNewConversation();

        Assert.Equal(initialCount + 1, vm.ConversationViewModels.Count);
        Assert.NotNull(vm.SelectedConversationViewModel);
        Assert.NotNull(vm.ActiveConversationViewModel);
        Assert.Equal(vm.ConversationViewModels[0], vm.SelectedConversationViewModel);
    }
}
