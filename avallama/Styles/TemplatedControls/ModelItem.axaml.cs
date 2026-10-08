// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using avallama.Constants.Keys;
using avallama.Models.Download;
using avallama.Models.Ollama;
using avallama.Services;
using avallama.Utilities;
using Avalonia;
using Avalonia.Controls.Primitives;

namespace avallama.Styles.TemplatedControls;

public class ModelItem : TemplatedControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<ModelItem, string?>(nameof(Title));

    public static readonly StyledProperty<string?> InformationProperty =
        AvaloniaProperty.Register<ModelItem, string?>(nameof(Information));

    // This is a styled property so AXAML will work better (e.g. for preview)
    public static readonly StyledProperty<ModelDownloadStatus?> DownloadStatusProperty =
        AvaloniaProperty.Register<ModelListItem, ModelDownloadStatus?>(nameof(DownloadStatus));

    public static readonly DirectProperty<ModelItem, OllamaModelFamily?> FamilyProperty =
        AvaloniaProperty.RegisterDirect<ModelItem, OllamaModelFamily?>(
            nameof(Family),
            o => o.Family,
            (o, v) => o.Family = v
        );

    public static readonly DirectProperty<ModelItem, long?> ParametersProperty =
        AvaloniaProperty.RegisterDirect<ModelItem, long?>(
            nameof(Parameters),
            o => o.Parameters,
            (o, v) => o.Parameters = v
        );

    public static readonly DirectProperty<ModelItem, IDictionary<string, string>?> InformationSourceProperty =
        AvaloniaProperty.RegisterDirect<ModelItem, IDictionary<string, string>?>(
            nameof(InformationSource),
            o => o.InformationSource,
            (o, v) => o.InformationSource = v
        );

    public static readonly DirectProperty<ModelItem, long> SizeInBytesProperty =
        AvaloniaProperty.RegisterDirect<ModelItem, long>(
            nameof(SizeInBytes),
            o => o.SizeInBytes,
            (o, v) => o.SizeInBytes = v,
            unsetValue: 0
        );

    public static readonly DirectProperty<ModelItem, bool?> RunsSlowProperty =
        AvaloniaProperty.RegisterDirect<ModelItem, bool?>(
            nameof(RunsSlow),
            o => o.RunsSlow,
            (o, v) => o.RunsSlow = v
        );

    public static readonly StyledProperty<string?> StatusTextProperty =
        AvaloniaProperty.Register<ModelItem, string?>(nameof(StatusText));

    public static readonly StyledProperty<string?> SpeedTextProperty =
        AvaloniaProperty.Register<ModelItem, string?>(nameof(SpeedTextProperty));

    public static readonly StyledProperty<ICommand> DownloadCommandProperty =
        AvaloniaProperty.Register<ModelItem, ICommand>(nameof(DownloadCommand));

    public static readonly StyledProperty<ICommand> PauseCommandProperty =
        AvaloniaProperty.Register<ModelItem, ICommand>(nameof(PauseCommand));

    public static readonly StyledProperty<ICommand> ResumeCommandProperty =
        AvaloniaProperty.Register<ModelItem, ICommand>(nameof(ResumeCommand));

    public static readonly StyledProperty<ICommand> DeleteCommandProperty =
        AvaloniaProperty.Register<ModelItem, ICommand>(nameof(DeleteCommand));

    public static readonly StyledProperty<ICommand> CancelCommandProperty =
        AvaloniaProperty.Register<ModelItem, ICommand>(nameof(CancelCommand));

    public static readonly StyledProperty<ICommand> RetryCommandProperty =
        AvaloniaProperty.Register<ModelItem, ICommand>(nameof(RetryCommand));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Information
    {
        get => GetValue(InformationProperty);
        set => SetValue(InformationProperty, value);
    }

    public long? Parameters
    {
        get;
        set => SetAndRaise(ParametersProperty, ref field, value);
    }

    public OllamaModelFamily? Family
    {
        get;
        set => SetAndRaise(FamilyProperty, ref field, value);
    }

    public IDictionary<string, string>? InformationSource
    {
        get;
        set => SetAndRaise(InformationSourceProperty, ref field, value);
    }

    public long SizeInBytes
    {
        get;
        set => SetAndRaise(SizeInBytesProperty, ref field, value);
    }

    public ModelDownloadStatus? DownloadStatus
    {
        get => GetValue(DownloadStatusProperty);
        set => SetValue(DownloadStatusProperty, value);
    }

    public bool? RunsSlow
    {
        get;
        set => SetAndRaise(RunsSlowProperty, ref field, value);
    }

    public string? StatusText
    {
        get => GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public string? SpeedText
    {
        get => GetValue(SpeedTextProperty);
        set => SetValue(SpeedTextProperty, value);
    }

    public ICommand DownloadCommand
    {
        get => GetValue(DownloadCommandProperty);
        set => SetValue(DownloadCommandProperty, value);
    }

    public ICommand PauseCommand
    {
        get => GetValue(PauseCommandProperty);
        set => SetValue(PauseCommandProperty, value);
    }

    public ICommand ResumeCommand
    {
        get => GetValue(ResumeCommandProperty);
        set => SetValue(ResumeCommandProperty, value);
    }

    public ICommand DeleteCommand
    {
        get => GetValue(DeleteCommandProperty);
        set => SetValue(DeleteCommandProperty, value);
    }

    public ICommand CancelCommand
    {
        get => GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }

    public ICommand RetryCommand
    {
        get => GetValue(RetryCommandProperty);
        set => SetValue(RetryCommandProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != InformationSourceProperty) return;
        if (InformationSource == null) return;

        // Add pull count from family to model info
        InformationSource[ModelInfoKey.PullCount] =
            ConversionHelper.FormatToAbbreviatedNumber(Family?.PullCount ?? 0);

        // Add last updated date from family to model info
        InformationSource[ModelInfoKey.LastUpdated] =
            Family?.LastUpdated.ToString("yyyy-MM-dd") ?? string.Empty;

        // Add parameters to model info
        if (Parameters is > 0)
        {
            InformationSource[ModelInfoKey.Parameters] = ConversionHelper.FormatToAbbreviatedNumber(Parameters.Value);
        }

        var licenseInfo = InformationSource.FirstOrDefault(kv => kv.Key == ModelInfoKey.License);
        var sortedInfoWithoutLicense = InformationSource.Where(kv => kv.Key != ModelInfoKey.License)
            .OrderBy(kv => kv.Key)
            .ToList();

        // Merging all information values into one string
        Information = string.Join('\n', sortedInfoWithoutLicense.Select(kv =>
        {
            // localized text for information key
            var localizedKey = kv.Key switch
            {
                ModelInfoKey.Format => LocalizationService.GetString("Models.Details.Format"),
                ModelInfoKey.Architecture => LocalizationService.GetString("Models.Details.Architecture"),
                ModelInfoKey.QuantizationLevel => LocalizationService.GetString("Models.Details.QuantizationLevel"),
                ModelInfoKey.Parameters => LocalizationService.GetString("Models.Details.Parameters"),
                ModelInfoKey.BlockCount => LocalizationService.GetString("Models.Details.BlockCount"),
                ModelInfoKey.ContextLength => LocalizationService.GetString("Models.Details.ContextLength"),
                ModelInfoKey.EmbeddingLength => LocalizationService.GetString("Models.Details.EmbeddingLength"),
                ModelInfoKey.PullCount => LocalizationService.GetString("Models.Details.PullCount"),
                ModelInfoKey.LastUpdated => LocalizationService.GetString("Models.Details.LastUpdated"),
                _ => kv.Key
            };

            return $"{localizedKey}: {kv.Value}";
        }));

        if (!string.IsNullOrWhiteSpace(licenseInfo.Value)) Information += $"\n{LocalizationService.GetString("Models.Details.License")}: {licenseInfo.Value}";
    }
}
