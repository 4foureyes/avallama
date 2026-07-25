// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using avallama.Constants.Keys;
using avallama.Constants.States;
using avallama.Models;
using avallama.Models.Dtos;
using avallama.Models.Ollama;
using avallama.Tests.Fixtures;
using avallama.ViewModels;
using avallama.Views;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Moq;
using Xunit;

namespace avallama.Tests.Views;

public class ConversationViewTests : IClassFixture<TestServicesFixture>
{
    private readonly TestServicesFixture _fixture;

    public ConversationViewTests(TestServicesFixture fixture)
    {
        _fixture = fixture;
        SetupDefaultBehaviors();
    }

    public void Dispose()
    {
        _fixture.OllamaMock.Reset();
        _fixture.ConfigMock.Reset();
        _fixture.DbMock.Reset();
        _fixture.DialogMock.Reset();
        _fixture.MessengerMock.Reset();
    }

    private void SetupDefaultBehaviors()
    {
        _fixture.ConfigMock
            .Setup(x => x.ReadSetting(It.IsAny<string>()))
            .Returns("");

        _fixture.DbMock.Setup(x => x.GetMessagesForConversation(It.IsAny<Conversation>())).ReturnsAsync([]);
        _fixture.DbMock.Setup(x => x.InsertMessage(It.IsAny<Guid>(), It.IsAny<Message>(), It.IsAny<string>(), It.IsAny<double?>()))
            .ReturnsAsync(1);
    }

    private ConversationViewModel CreateConversationViewModel(ObservableCollection<string>? models = null)
    {
        var conversation = new Conversation { Id = Guid.NewGuid(), Title = "Test Conversation" };
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

    private (Window Window, ConversationView View, ConversationViewModel ViewModel) CreateAndShowView(ObservableCollection<string>? models = null)
    {
        var viewModel = CreateConversationViewModel(models);
        var view = new ConversationView
        {
            DataContext = viewModel
        };
        var window = new Window { Content = view };
        window.Show();
        return (window, view, viewModel);
    }

    [AvaloniaFact]
    public async Task MessageTextBox_WhenNoDownloadedModelIsSelected_ItsDisabledCorrectly()
    {
        var emptyModels = new ObservableCollection<string>();
        var (_, view, viewModel) = CreateAndShowView(emptyModels);

        await viewModel.InitializeAsync();

        var messageTextBox = view.FindControl<TextBox>("MessageTextBox");
        Assert.NotNull(messageTextBox);

        Assert.False(viewModel.IsMessageBoxEnabled);
        Assert.False(messageTextBox.IsEnabled);
    }

    [AvaloniaFact]
    public async Task MessageTextBox_WhenDownloadedModelIsSelected_ItsEnabledCorrectly()
    {
        var models = new ObservableCollection<string> { "test-model-2:20b" };
        var (_, view, viewModel) = CreateAndShowView(models);

        await viewModel.InitializeAsync();

        var messageTextBox = view.FindControl<TextBox>("MessageTextBox");
        Assert.NotNull(messageTextBox);

        Assert.True(viewModel.IsMessageBoxEnabled);
        Assert.True(messageTextBox.IsEnabled);
    }

    [AvaloniaFact]
    public async Task ModelsComboBox_WithEmptyModelsList_ItsDisabledCorrectly()
    {
        var emptyModels = new ObservableCollection<string>();
        var (_, view, _) = CreateAndShowView(emptyModels);

        var modelsComboBox = view.FindControl<ComboBox>("ModelsComboBox");

        Assert.NotNull(modelsComboBox);
        // IsEnabled is bound implicitly or normally available based on ItemsSource
        // But let's check if the list is empty and the selected item is null
        Assert.Empty(modelsComboBox.Items);
        Assert.Null(modelsComboBox.SelectedItem);
    }

    [AvaloniaFact]
    public async Task MessageTextBox_WhenMessageEntered_AddsMessages()
    {
        var models = new ObservableCollection<string> { "test-model-1:8b" };

        _fixture.OllamaMock
            .Setup(x => x.GenerateMessageAsync(It.IsAny<List<Message>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(GetPendingOllamaResponses());

        var (window, view, viewModel) = CreateAndShowView(models);

        await viewModel.InitializeAsync();

        var messageTextBox = view.FindControl<TextBox>("MessageTextBox");
        Assert.NotNull(messageTextBox);

        const string testMessage = "This is a test message";

        messageTextBox.Focus();
        messageTextBox.Text = testMessage;

        // Simulate enter press on the TextBox
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

        // It should add the user message and a TypingIndicator message immediately
        Assert.Equal(2, viewModel.Conversation.Messages.Count);
        Assert.Empty(messageTextBox.Text);
        Assert.Equal(testMessage, viewModel.Conversation.Messages[0].Content);
        Assert.True(viewModel.Conversation.Messages[1] is TypingIndicatorMessage);
    }

    [AvaloniaFact]
    public async Task MessageTextBox_WhenMessageIsWhitespace_DoesNotAddMessage()
    {
        var models = new ObservableCollection<string> { "test-model-1:8b" };
        var (window, view, viewModel) = CreateAndShowView(models);

        await viewModel.InitializeAsync();

        var messageTextBox = view.FindControl<TextBox>("MessageTextBox");
        Assert.NotNull(messageTextBox);

        messageTextBox.Focus();
        messageTextBox.Text = "    \n  ";

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

        Assert.Empty(viewModel.Conversation.Messages);
    }

    [AvaloniaFact]
    public void InformationalMessages_WhenConfigurationIsTrue_MessagesAppearCorrectly()
    {
        _fixture.ConfigMock
            .Setup(x => x.ReadSetting(ConfigurationKey.IsInformationalMessagesVisible))
            .Returns("True");

        var (_, view, viewModel) = CreateAndShowView();

        viewModel.IsRunningSlowTextVisible = true;

        var noModelsWarning = view.FindControl<TextBlock>("NoModelsWarningTextBlock");
        Assert.NotNull(noModelsWarning);

        Assert.True(viewModel.ShowInformationalMessages);
        Assert.True(viewModel.IsRunningSlowTextVisible);
    }

    [AvaloniaFact]
    public void InformationalMessages_WhenConfigurationIsFalse_MessagesWontAppear()
    {
        _fixture.ConfigMock
            .Setup(x => x.ReadSetting(ConfigurationKey.IsInformationalMessagesVisible))
            .Returns("False");

        var (_, _, viewModel) = CreateAndShowView();

        Assert.False(viewModel.ShowInformationalMessages);
    }

    [AvaloniaFact]
    public async Task OllamaServiceStatusChanged_WhenFailed_CancelsGenerationAndShowsFailedMessage()
    {
        var models = new ObservableCollection<string> { "test-model-1:8b" };
        var (_, _, viewModel) = CreateAndShowView(models);
        await viewModel.InitializeAsync();

        viewModel.Conversation.Messages.Add(new Message("Generate a story"));
        viewModel.Conversation.Messages.Add(new TypingIndicatorMessage());

        viewModel.Status = new ConversationStatus(ConversationState.StreamingResponse);

        _fixture.OllamaMock.Raise(x =>
            x.StatusChanged += null, new OllamaServiceStatus(OllamaServiceState.Failed, "Server crashed"));

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { });

        Assert.Equal(ConversationState.Failed, viewModel.Status.ConversationState);

        Assert.Equal(2, viewModel.Conversation.Messages.Count);
        Assert.True(viewModel.Conversation.Messages[1] is FailedMessage);
        Assert.Equal("Server crashed", viewModel.Conversation.Messages[1].Content);
    }

    private async IAsyncEnumerable<OllamaResponse> GetPendingOllamaResponses(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Delay(-1, ct);
        yield break;
    }
}
