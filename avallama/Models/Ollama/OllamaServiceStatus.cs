// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using avallama.Constants.States;
using avallama.DataTemplates;

namespace avallama.Models.Ollama;

/// <summary>
/// Represents the unified status of Ollama (Process + API).
/// </summary>
public class OllamaServiceStatus(OllamaServiceState serviceState, string? message = null) : IStatefulTemplateItem
{
    /// <summary>
    /// Gets or sets the unified current state.
    /// </summary>
    public OllamaServiceState ServiceState { get; set; } = serviceState;

    /// <summary>
    /// Gets or sets an optional message regarding the status (e.g., error details).
    /// </summary>
    public string? Message { get; set; } = message;

    /// <summary>
    /// Retrieves the string representation of the current state key.
    /// </summary>
    /// <returns>The service state as a string.</returns>
    public string GetCurrentStateKey() => ServiceState.ToString();
}
