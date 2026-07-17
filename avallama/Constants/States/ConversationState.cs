// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

namespace avallama.Constants.States;

/// <summary>
/// Defines the possible states of a conversation.
/// </summary>
public enum ConversationState
{
    /// <summary>
    /// The conversation is currently initializing (fetching messages from db, etc.)
    /// </summary>
    Initializing,

    /// <summary>
    /// The conversation is ready to process messages.
    /// </summary>
    Idle,

    /// <summary>
    /// The conversation sent the request to Ollama and processing the request.
    /// </summary>
    ProcessingMessage,

    /// <summary>
    /// The conversation is streaming the response from Ollama.
    /// </summary>
    StreamingResponse,

    /// <summary>
    /// The message generation failed in the conversation.
    /// </summary>
    Failed
}
