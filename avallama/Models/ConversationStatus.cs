// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using avallama.Constants.States;
using avallama.DataTemplates;

namespace avallama.Models;

/// <summary>
/// Represents the current status of a conversation, including its state and an optional message.
/// </summary>
public class ConversationStatus(ConversationState conversationState, string? message = null) : IStatefulTemplateItem
{
    /// <summary>
    /// Gets the current state of the conversation.
    /// </summary>
    public ConversationState ConversationState { get; } = conversationState;

    /// <summary>
    /// Gets the optional message.
    /// </summary>
    public string? Message { get; } = message;


    /// <summary>
    /// Retrieves the string representation of the current state key.
    /// </summary>
    /// <returns>The conversation state as a string.</returns>
    public string GetCurrentStateKey() => ConversationState.ToString();
}
