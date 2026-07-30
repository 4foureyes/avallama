// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Threading;
using System.Threading.Tasks;
using avallama.ViewModels;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;

namespace avallama.Utilities;

/// <summary>
/// A custom page transition that selectively applies animations based on the navigation route.
/// It ensures that only specific view transitions (like Welcome page) are animated,
/// while standard navigation happens instantly without delay.
/// </summary>
public class SmartPageTransition : IPageTransition
{
    // default animation to use when a transition is required
    private readonly CrossFade _crossFade = new (TimeSpan.FromSeconds(0.25));

    /// <summary>
    /// Executes the visual transition between the old and the new view.
    /// </summary>
    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        var shouldAnimate = false;

        var fromVm = (from as Control)?.DataContext;
        var toVm = (to as Control)?.DataContext;

        if (fromVm is WelcomeViewModel && toVm is OnboardingViewModel)
        {
            shouldAnimate = true;
        }

        if (shouldAnimate)
        {
            await _crossFade.Start(from, to, cancellationToken);
        }
        else
        {
            if (to != null)
            {
                to.IsVisible = true;
                to.Opacity = 1;
            }

            from?.IsVisible = false;
        }
    }
}
