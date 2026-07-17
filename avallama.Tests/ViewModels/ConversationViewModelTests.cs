// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using avallama.Constants.States;
using avallama.Models;
using avallama.Models.Dtos;
using avallama.Tests.Fixtures;
using avallama.ViewModels;
using Avalonia.Headless.XUnit;
using Moq;
using Xunit;

namespace avallama.Tests.ViewModels;

public class ConversationViewModelTests : IClassFixture<TestServicesFixture>
{
    private readonly TestServicesFixture _fixture;

    public ConversationViewModelTests(TestServicesFixture fixture)
    {
        _fixture = fixture;
        SetupDefaultBehaviors();
    }

    private void SetupDefaultBehaviors()
    {
        _fixture.ConfigMock
            .Setup(x => x.ReadSetting(It.IsAny<string>()))
            .Returns("");

        _fixture.DbMock.Setup(x => x.GetMessagesForConversation(It.IsAny<Conversation>())).ReturnsAsync([]);
        _fixture.DbMock.Setup(x =>
                x.InsertMessage(It.IsAny<Guid>(), It.IsAny<Message>(), It.IsAny<string?>(), It.IsAny<double?>()))
            .ReturnsAsync(1L);
    }

    private ConversationViewModel CreateViewModel(Conversation conversation,
        ObservableCollection<string>? models = null)
    {
        models ??= [];

        return new ConversationViewModel(
            conversation,
            _fixture.OllamaMock.Object,
            _fixture.DialogMock.Object,
            _fixture.ConfigMock.Object,
            _fixture.DbMock.Object,
            _fixture.MessengerMock.Object,
            models
        );
    }

    [Fact]
    public void InitializeModels_WhenNonEmptyList_SelectsFirstModel_MessageBoxEnabled()
    {
        var availableModels = new ObservableCollection<string>
        {
            "model1",
            "model2"
        };

        var conversation = new Conversation("A", string.Empty) { Id = Guid.NewGuid() };
        var vm = CreateViewModel(conversation, availableModels);

        Assert.Equal("model1", vm.SelectedModelName);
        Assert.True(vm.IsMessageBoxEnabled);
    }

    [Fact]
    public async Task DeleteMessage_WhenValidMessage_DeletesFromDatabaseAndRemovesFromCollection()
    {
        _fixture.DbMock.Reset();

        var conversation = new Conversation("A", "model-1:1b") { Id = Guid.NewGuid() };
        var message = new Message("Test message") { Id = 10 };
        conversation.Messages.Add(message);

        var vm = CreateViewModel(conversation);

        await vm.DeleteMessageCommand.ExecuteAsync(message);

        _fixture.DbMock.Verify(db => db.DeleteMessage(10), Times.Once);
        Assert.DoesNotContain(message, vm.Conversation.Messages);
    }

    [Fact]
    public async Task DeleteMessage_WhenFailedMessage_RemovesFromCollectionButDoesNotCallDatabase()
    {
        _fixture.DbMock.Reset();

        var conversation = new Conversation("A", "model-1:1b") { Id = Guid.NewGuid() };
        var failedMessage = new FailedMessage { Id = -1 };
        conversation.Messages.Add(failedMessage);

        var vm = CreateViewModel(conversation);

        await vm.DeleteMessageCommand.ExecuteAsync(failedMessage);

        _fixture.DbMock.Verify(db => db.DeleteMessage(It.IsAny<long>()), Times.Never);
        Assert.DoesNotContain(failedMessage, vm.Conversation.Messages);
    }

    [Fact]
    public async Task TitleRegenerates_WhenFirstMessageExchangeOccurs()
    {
        _fixture.OllamaMock.Reset();
        _fixture.DbMock.Reset();

        var conv = new Conversation("A", string.Empty) { Id = Guid.NewGuid() };

        _fixture.OllamaMock
            .Setup(o => o.GenerateMessageAsync(
                It.IsAny<List<Message>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(MainStreamAsync);

        var updatedConversationIds = new List<Guid>();
        _fixture.DbMock
            .Setup(db => db.UpdateConversationTitle(It.IsAny<Conversation>()))
            .Callback<Conversation>(c => updatedConversationIds.Add(c.Id))
            .ReturnsAsync(true);

        _fixture.DbMock.Setup(db =>
                db.InsertMessage(It.IsAny<Guid>(), It.IsAny<Message>(), It.IsAny<string?>(), It.IsAny<double?>()))
            .ReturnsAsync(1L);

        var models = new ObservableCollection<string> { "model1" };
        var vm = CreateViewModel(conv, models);

        vm.NewMessageText = "hello";
        await vm.SendMessageCommand.ExecuteAsync(null);

        Assert.Contains(conv.Id, updatedConversationIds);
        _fixture.DbMock.Verify(
            db => db.UpdateConversationTitle(It.Is<Conversation>(c => c.Id == conv.Id)),
            Times.AtLeastOnce);
    }

    // TODO: add tests for generation cancellation

    private static async IAsyncEnumerable<OllamaResponse> MainStreamAsync()
    {
        yield return new OllamaResponse
        {
            Message = new MessageContent { Content = "assistant chunk" },
            EvalCount = 10,
            EvalDuration = 1_000_000_000
        };

        await Task.CompletedTask;
    }
}
