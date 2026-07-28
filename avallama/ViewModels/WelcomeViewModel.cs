// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using avallama.Constants.Application;

namespace avallama.ViewModels;

public partial class WelcomeViewModel : PageViewModel
{
    public static string AppVersion => $"v{App.Version.ToString(3)}";

    public WelcomeViewModel()
    {
        // setting the page type that this viewmodel handles
        Page = ApplicationPage.Welcome;
    }
}
