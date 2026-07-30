// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

namespace avallama.Constants;

/// <summary>
/// Represents the available content steps within the onboarding flow.
/// </summary>
public enum OnboardingContent
{
    /// <summary>
    /// The step for configuring and testing the connection to the Ollama service.
    /// </summary>
    Connection,

    /// <summary>
    /// The step for setting up the model library via the web scraper.
    /// </summary>
    Scraper
}
