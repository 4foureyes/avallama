// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.ComponentModel;
using System.Threading.Tasks;
using avallama.Constants;
using avallama.Constants.Application;
using avallama.Constants.States;
using avallama.Models.Download;
using avallama.Models.Ollama;
using avallama.Services;
using avallama.Services.Ollama;
using avallama.Services.Persistence;
using avallama.Services.Queue;
using avallama.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace avallama.ViewModels;

public partial class ModelItemViewModel : ViewModelBase
{
    private readonly IOllamaService _ollamaService;
    private readonly IModelDownloadQueueService _modelDownloadQueueService;
    private readonly IDialogService _dialogService;
    private readonly IModelCacheService _modelCacheService;
    private readonly IMessenger _messenger;

    [ObservableProperty] private OllamaModel _model;

    // a download request that can be used in the queue
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CurrentStatus))]
    private ModelDownloadRequest? _downloadRequest;

    // short download status text when the viewmodel is used inside a ModelListItem
    [ObservableProperty] private string _shortDownloadStatusText = string.Empty;

    // longer download status text when the viewmodel is used inside a ModelItem
    [ObservableProperty] private string _longDownloadStatusText = string.Empty;

    [ObservableProperty] private string _downloadSpeedText = string.Empty;

    public ModelItemViewModel(
        OllamaModel model,
        IOllamaService ollamaService,
        IModelDownloadQueueService modelDownloadQueueService,
        IDialogService dialogService,
        IModelCacheService modelCacheService,
        IMessenger messenger)
    {
        Model = model;
        _ollamaService = ollamaService;
        _modelDownloadQueueService = modelDownloadQueueService;
        _dialogService = dialogService;
        _modelCacheService = modelCacheService;
        _messenger = messenger;
    }

    // computed property which provides the status of the model itself
    // it uses the downloadrequest's current status or the model's status if there is no active request
    public ModelDownloadStatus CurrentStatus
    {
        get
        {
            ModelDownloadStatus returnedStatus;

            if (DownloadRequest is { Status: not null })
            {
                returnedStatus = DownloadRequest.Status;
            }
            else
            {
                returnedStatus = Model.IsDownloaded
                    ? new ModelDownloadStatus(DownloadState.Downloaded)
                    : new ModelDownloadStatus(); // Downloadable
            }

            return returnedStatus;
        }
    }

    [RelayCommand]
    public void Download()
    {
        if (Model.IsDownloaded) return;
        DownloadRequest = new ModelDownloadRequest
        {
            ModelName = Model.Name,
            Status = new ModelDownloadStatus(DownloadState.Queued),
        };
        _modelDownloadQueueService.Enqueue(DownloadRequest);
        _messenger.Send(new ApplicationMessage.ModelStatusChanged(Model.Name));
    }

    [RelayCommand]
    public void Pause()
    {
        if (DownloadRequest == null ||
            DownloadRequest.Status == null ||
            DownloadRequest.Status.DownloadState == DownloadState.Paused) return;

        DownloadRequest?.Cancel();
        DownloadRequest?.QueueItemCancellationReason = QueueItemCancellationReason.UserPauseRequest;
        DownloadRequest?.Status = new ModelDownloadStatus(DownloadState.Paused);
        _messenger.Send(new ApplicationMessage.ModelStatusChanged(Model.Name));
    }

    [RelayCommand]
    public void Resume()
    {
        if (DownloadRequest == null ||
            DownloadRequest.Status == null ||
            DownloadRequest.Status.DownloadState != DownloadState.Paused) return;

        DownloadRequest.QueueItemCancellationReason = QueueItemCancellationReason.Unknown;
        DownloadRequest.ResetToken();
        _modelDownloadQueueService.Enqueue(DownloadRequest);
        DownloadRequest.Status = new ModelDownloadStatus(DownloadState.Queued);
        _messenger.Send(new ApplicationMessage.ModelStatusChanged(Model.Name));
    }

    [RelayCommand]
    public void Cancel()
    {
        if (DownloadRequest == null) return;
        DownloadRequest.Cancel();
        DownloadRequest.QueueItemCancellationReason = QueueItemCancellationReason.UserCancelRequest;
        DownloadRequest = null;
        OnPropertyChanged(nameof(CurrentStatus));
        _messenger.Send(new ApplicationMessage.ModelStatusChanged(Model.Name));
    }

    [RelayCommand]
    public async Task Delete()
    {
        // TODO: add a force delete option in case data is somehow corrupted and throws error while deleting

        if (!Model.IsDownloaded) return;
        var dialogResult = await _dialogService.ShowConfirmationDialogAsync(
            LocalizationService.GetString("Common.DeleteDialog.Title"),
            LocalizationService.GetString("Common.Button.Delete"),
            LocalizationService.GetString("Common.Button.Cancel"),
            string.Format(LocalizationService.GetString("Common.DeleteDialog.Description"),
                Model.Name),
            ConfirmationType.Positive
        );

        if (dialogResult is ConfirmationResult { Confirmation: ConfirmationType.Positive })
        {
            if (await _ollamaService.DeleteModelAsync(Model.Name))
            {
                Model.IsDownloaded = false;
                DownloadRequest = null;
                await _modelCacheService.UpdateModelAsync(Model);
                OnPropertyChanged(nameof(CurrentStatus));
                _messenger.Send(new ApplicationMessage.ModelStatusChanged(Model.Name));
            }
            else
            {
                _dialogService.ShowErrorDialog(LocalizationService.GetString("Models.Error.DeleteFailed"),
                    false);
            }
        }
    }

    [RelayCommand]
    public void Retry()
    {
        if (DownloadRequest?.Status?.DownloadState != DownloadState.Failed) return;
        DownloadCommand.Execute(null);
    }

    // this is called inside the active download request's setter (which is generated automatically)
    partial void OnDownloadRequestChanged(ModelDownloadRequest? oldValue, ModelDownloadRequest? newValue)
    {
        if (oldValue != null)
        {
            oldValue.PropertyChanged -= OnDownloadRequestPropertyChanged;
        }

        if (newValue != null)
        {
            newValue.PropertyChanged += OnDownloadRequestPropertyChanged;

            UpdateStatusTexts();
        }
        else
        {
            ShortDownloadStatusText = string.Empty;
            LongDownloadStatusText = string.Empty;
        }
    }

    // async void allowed for event handlers as it's necessary, otherwise it must be avoided
    // the relevant code must be placed inside a try-catch block, exceptions will not be caught by callers (fire and forget)
    // https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/async-return-types#void-return-type
    private async void OnDownloadRequestPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null) return;

        // download status changes
        if (e.PropertyName.Equals(nameof(DownloadRequest.Status)))
        {
            if (DownloadRequest?.Status == null) return;
            OnPropertyChanged(nameof(CurrentStatus));

            switch (DownloadRequest.Status.DownloadState)
            {
                case DownloadState.Downloaded:
                    try
                    {
                        Model.IsDownloaded = true;
                        DownloadRequest = null;
                        _messenger.Send(new ApplicationMessage.ModelStatusChanged(Model.Name));

                        // enrich the selected model with the information available from Ollama API
                        await _ollamaService.EnrichModelAsync(Model);

                        // update model in the db with all the new info, or insert it if it's non-existent
                        if (await _modelCacheService.ContainsModelAsync(Model))
                        {
                            await _modelCacheService.UpdateModelAsync(Model);
                        }
                        else
                        {
                            await _modelCacheService.InsertModelAsync(Model);
                        }
                    }
                    catch (Exception ex)
                    {
                        // TODO: proper logging
                        _dialogService.ShowErrorDialog(
                            string.Format(LocalizationService.GetString("Models.Error.MetadataUpdateFailed"), ex.Message),
                            false);
                    }

                    break;
                case DownloadState.Failed:
                    _dialogService.ShowErrorDialog(
                        DownloadRequest.Status.Message ?? LocalizationService.GetString("Common.Error.Unknown"),
                        false);
                    break;
            }
        }
        // downloaded bytes or download speed changes
        else if (e.PropertyName.Equals(nameof(DownloadRequest.DownloadedBytes)) ||
                 e.PropertyName.Equals(nameof(DownloadRequest.DownloadSpeed)))
        {
            // TODO: optimize status text updating to reduce UI rendering
            // currently UI renders status texts approximately 1k-2k times when downloading a model with a size of 2.33 GB
            // this needs to be reduced to update status texts every 500-1000 ms
            UpdateStatusTexts();
        }
    }

    private void UpdateStatusTexts()
    {
        if (DownloadRequest?.Status == null) return;

        switch (DownloadRequest.DownloadPartCount)
        {
            case > 1:
                ShortDownloadStatusText = LocalizationService.GetString("Downloads.Status.FinalizingShort");
                LongDownloadStatusText = LocalizationService.GetString("Downloads.Status.Finalizing");
                break;
            case 1:
            {
                var downloadedBytes = DownloadRequest.DownloadedBytes;
                var totalBytes = DownloadRequest.TotalBytes;

                if (totalBytes > 0)
                {
                    var percentage = (double)downloadedBytes / totalBytes * 100;
                    ShortDownloadStatusText = $"{percentage:F2}%";
                }
                else
                {
                    ShortDownloadStatusText = "0%";
                }

                LongDownloadStatusText = LocalizationService.GetString("Downloads.Status.Downloading");
                LongDownloadStatusText +=
                    $" - {ConversionHelper.BytesToReadableSize(downloadedBytes)}/{ConversionHelper.BytesToReadableSize(totalBytes)}";

                DownloadSpeedText = $"{DownloadRequest.DownloadSpeed:0.##} MB/s";
                break;
            }
            default:
                ShortDownloadStatusText = string.Empty;
                LongDownloadStatusText = string.Empty;
                break;
        }
    }
}
