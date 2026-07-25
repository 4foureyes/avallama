// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using avallama.Constants.States;
using avallama.Models;
using avallama.Models.Ollama;
using avallama.Tests.Fixtures;
using avallama.ViewModels;
using avallama.Views;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Moq;
using Xunit;

namespace avallama.Tests.Views;

public class HomeViewTests : IClassFixture<TestServicesFixture>
{
    private readonly TestServicesFixture _fixture;

    public HomeViewTests(TestServicesFixture fixture)
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

        _fixture.OllamaMock.Raise(x =>
            x.StatusChanged += null, new OllamaServiceStatus(OllamaServiceState.Ready));

        _fixture.OllamaMock
            .Setup(x => x.GetDownloadedModelsAsync())
            .ReturnsAsync(new List<OllamaModel>());

        _fixture.DbMock.Setup(x => x.GetConversations()).ReturnsAsync([]);
        _fixture.DbMock.Setup(x => x.CreateConversation(It.IsAny<Conversation>())).ReturnsAsync(Guid.NewGuid());
        _fixture.DbMock.Setup(x => x.GetMessagesForConversation(It.IsAny<Conversation>())).ReturnsAsync([]);
    }

    private HomeViewModel CreateHomeViewModel()
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

    private (HomeView View, HomeViewModel ViewModel) CreateAndShowHomeView()
    {
        var viewModel = CreateHomeViewModel();
        var view = new HomeView
        {
            DataContext = viewModel,
            IsFullScreenOverride = () => false
        };
        var window = new Window { Content = view };
        window.Show();
        return (view, viewModel);
    }

    [AvaloniaFact]
    public void SideBar_ClickingSideBarButton_TogglesSideBarCorrectly()
    {
        var (view, _) = CreateAndShowHomeView();

        var sideBarButton = view.FindControl<Button>("SideBarButton");
        var sideBar = view.FindControl<Grid>("SideBar");
        var mainGrid = view.FindControl<Grid>("MainGrid");

        Assert.NotNull(sideBarButton);
        Assert.NotNull(sideBar);
        Assert.NotNull(mainGrid);

        // check if sidebar is opened by default when window is shown
        Assert.Contains(sideBar, mainGrid.Children);
        Assert.True(mainGrid.ColumnDefinitions[0].Width.Value > 0); // checking sidebar's width

        // simulate a click on sidebar button (close)
        sideBarButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        // check if sidebar is closed after clicking the sidebar button
        Assert.DoesNotContain(sideBar, mainGrid.Children);
        Assert.Equal(0, mainGrid.ColumnDefinitions[0].Width.Value);

        // simulate another click on sidebar button (open)
        sideBarButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        // check if sidebar is opened after clicking the sidebar button
        Assert.Contains(sideBar, mainGrid.Children);
        Assert.True(mainGrid.ColumnDefinitions[0].Width.Value > 0);
    }

    [AvaloniaFact]
    public async Task NewConversationButton_AddsConversationCorrectly()
    {
        var (view, viewModel) = CreateAndShowHomeView();

        var newConversationBtn = view.FindControl<Button>("NewConversationBtn");
        Assert.NotNull(newConversationBtn);

        var initialCount = viewModel.ConversationViewModels.Count;

        newConversationBtn.Command?.Execute(null);

        Assert.Equal(initialCount + 1, viewModel.ConversationViewModels.Count);
        Assert.NotNull(viewModel.SelectedConversationViewModel);

        // ensure the newly created conversation becomes the selected one
        Assert.Equal(viewModel.ConversationViewModels[0], viewModel.SelectedConversationViewModel);
    }


    // TODO: implement missing testcases such as
    // (ConversationScrollViewer) 'Scroll to bottom' button appears when scrolling down and configuration set to 'Floating button'
    // (ConversationScrollViewer) Scrolls to bottom correctly when scroll to bottom is clicked
    // (ConversationScrollViewer) Scrolls automatically to bottom when ScrollViewer height expands (new message) and configuration set to 'Automatic'
    // (ConversationScrollViewer) Scroll position stays still when ScrollViewer height expands (new message) and configration is set to 'None'
    // (ConversationItem) Conversation deletes correctly when right clicked on Conversation item and Delete is clicked
    // (NewConversationButton) New conversation button adds a conversation correctly
    // (ModelManagerButton) Opens ModelManagerView when clicking on Model Manager icon button
    // (SettingsButton) Opens SettingsView when clicking on Settings icon button
    // (ModelsComboBox) Selecting a model from the combobox changes selected model correctly in HomeViewModel
    // (ModelsComboBox) When opening existing conversation the last used model is selected (if available) correctly - this would fail atm
    // (MessageBox) Pressing enter with empty or whitespace message does not adds messages
    // (MessageBox) When message has whitespace characters the message is trimmed correctly
    // (InformationalMessages) When configuration is set to "true" informational messages appear correctly
    // (InformationalMessages) When configuration is set to "false" informational messages won't appear
    // etc.

    /* this is how to set up ConfigMock, so I won't forget syntax
       _fixture.ConfigMock
           .Setup(x => x.ReadSetting(It.IsAny<string>()))
           .Returns((string key) => key switch
           {
               ConfigurationKey.ShowInformationalMessages => "True",
               _ => ""
           });
    */
}
