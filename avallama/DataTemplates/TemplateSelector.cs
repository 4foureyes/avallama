// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Metadata;

namespace avallama.DataTemplates;

/// <summary>
/// A data template selector that dynamically resolves templates based on the string representation of the data context.
/// </summary>
public class TemplateSelector : IDataTemplate
{
    /// <summary>
    /// Gets the dictionary of available data templates mapped by their string keys.
    /// </summary>
    [Content]
    public Dictionary<string, IDataTemplate> AvailableTemplates { get; } = new();

    /// <summary>
    /// Builds the visual control for the provided data item by matching its string representation to a template key.
    /// </summary>
    /// <param name="param">The data item to build a control for.</param>
    /// <returns>The constructed control, or null if no matching template is found.</returns>
    public Control? Build(object? param)
    {
        if (param is null) return null;

        var key = param.ToString();

        if (string.IsNullOrEmpty(key)) return null;

        return AvailableTemplates.TryGetValue(key, out var template) ? template.Build(param) : null;
    }

    /// <summary>
    /// Determines whether this selector can provide a template for the given data item.
    /// </summary>
    /// <param name="data">The data item to evaluate.</param>
    /// <returns>True if a matching template exists for the item's string representation; otherwise, false.</returns>
    public bool Match(object? data)
    {
        if (data is null) return false;

        var key = data.ToString();

        return !string.IsNullOrEmpty(key) && AvailableTemplates.ContainsKey(key);
    }
}
