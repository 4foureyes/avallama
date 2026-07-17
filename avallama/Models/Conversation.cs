// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace avallama.Models;

/// <summary>
/// Represents a chat conversation containing messages and metadata.
/// </summary>
public class Conversation : ObservableObject
{
    #region Properties

    /// <summary>
    /// Gets or sets the unique identifier of the conversation.
    /// </summary>
    public Guid Id
    {
        get;
        set => SetProperty(ref field, value);
    } = Guid.Empty;

    /// <summary>
    /// Gets or sets the title of the conversation.
    /// </summary>
    public string Title
    {
        get;
        set => SetProperty(ref field, value);
    }

    /// <summary>
    /// Gets or sets the model name used for the conversation.
    /// </summary>
    public string Model
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    /// <summary>
    /// Gets or sets the collection of messages within the conversation.
    /// </summary>
    public IList<Message> Messages
    {
        get;
        set => SetProperty(ref field, value);
    } = [];

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="Conversation"/> class with default values.
    /// </summary>
    public Conversation()
    {
        Title = string.Empty;
        Model = string.Empty;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Conversation"/> class with a title and model.
    /// </summary>
    /// <param name="title">The title of the conversation.</param>
    /// <param name="model">The model associated with the conversation.</param>
    public Conversation(string title, string model)
    {
        Title = title;
        Model = model;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Conversation"/> class with specified id, title, and messages.
    /// </summary>
    /// <param name="guid">The unique identifier.</param>
    /// <param name="title">The title of the conversation.</param>
    /// <param name="messages">The list of initial messages.</param>
    public Conversation(Guid guid, string title, IList<Message> messages)
    {
        Id = guid;
        Title = title;
        Messages = new ObservableCollection<Message>(messages);
    }

    #endregion
}
