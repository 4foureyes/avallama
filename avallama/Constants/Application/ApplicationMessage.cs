// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using avallama.Models.Ollama;
using avallama.ViewModels;

namespace avallama.Constants.Application;

public static class ApplicationMessage
{
    // request for app shutdown
    public record Shutdown;

    // request for app restart
    public record Restart;

    // request for settings to reload
    public record ReloadSettings;

    // navigate to page (in MainViewModel)
    public record NavigateToPage(ApplicationPage Page);

    // navigate back (in MainViewModel)
    public record NavigateBack;

    // notification for ModelManagerViewModel when a Model status' changes
    public record ModelStatusChanged(string ModelName);

    // Message sent when a conversation gets a new message and should be bumped to the top of the list.
    public record ConversationBump(ConversationViewModel ViewModel);

    // Ollama status changed message (used for transient VMs instead of events so GC can clean it)
    public record OllamaStatusChangedMessage(OllamaServiceStatus Status);
}
