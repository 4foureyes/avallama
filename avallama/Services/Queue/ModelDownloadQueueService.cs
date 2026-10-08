// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Threading;
using System.Threading.Tasks;
using avallama.Constants;
using avallama.Constants.States;
using avallama.Exceptions;
using avallama.Models.Download;
using avallama.Services.Ollama;
using avallama.Utilities;
using avallama.Utilities.Network;

namespace avallama.Services.Queue;

/// <summary>
/// Defines a contract for a queue service specialized in handling model download requests.
/// </summary>
public interface IModelDownloadQueueService : IQueueService<ModelDownloadRequest>;

/// <summary>
/// A specialized queue service responsible for processing Ollama model download requests sequentially or in parallel.
/// </summary>
public class ModelDownloadQueueService(
    IOllamaService ollamaService,
    INetworkManager networkManager)
    : QueueService<ModelDownloadRequest>, IModelDownloadQueueService
{
    /// <summary>
    /// Processes a single model download request by communicating with the Ollama service.
    /// Handles chunked downloads, speed calculation, and disk space verification.
    /// </summary>
    /// <param name="request">The download request containing model details.</param>
    /// <param name="ct">The cancellation token to abort the download operation.</param>
    /// <returns>A task representing the asynchronous download process.</returns>
    /// <exception cref="NoInternetConnectionException">Thrown when there is no active internet connection.</exception>
    /// <exception cref="InsufficientDiskSpaceException">Thrown when there is not enough available disk space to complete the download.</exception>
    protected override async Task ProcessItemAsync(ModelDownloadRequest request, CancellationToken ct)
    {
        if (!await networkManager.IsInternetAvailableAsync())
        {
            throw new NoInternetConnectionException();
        }

        request.DownloadPartCount = 1;

        await foreach (var chunk in ollamaService.DownloadModelAsync(request.ModelName, ct))
        {
            if (!DiskManager.IsEnoughDiskSpaceAvailable(request.TotalBytes))
            {
                throw new InsufficientDiskSpaceException(request.TotalBytes,
                    DiskManager.GetAvailableDiskSpaceBytes());
            }

            if (chunk is { Total: not null, Completed: not null })
            {
                // check if server started to stream a new part of the download
                if (request.TotalBytes != 0 && chunk.Total.Value != request.TotalBytes)
                {
                    request.DownloadPartCount++;
                }

                var speed = request.SpeedCalculator.CalculateSpeed(chunk.Completed.Value);

                if (request.Status == null || request.Status.DownloadState == DownloadState.Queued)
                {
                    request.Status = new ModelDownloadStatus(DownloadState.Downloading);
                }

                request.DownloadedBytes = chunk.Completed.Value;
                request.TotalBytes = chunk.Total.Value;
                request.DownloadSpeed = speed;
            }

            if (chunk.Status == "success")
            {
                request.Status = new ModelDownloadStatus(DownloadState.Downloaded);
            }
        }
    }

    /// <summary>
    /// Handles exceptions thrown during the download process, and sets a localized error message on the request status.
    /// </summary>
    /// <param name="request">The request that failed.</param>
    /// <param name="ex">The exception that caused the failure.</param>
    protected override void OnItemFailed(ModelDownloadRequest request, Exception ex)
    {
        string errorKey;
        string? arg = null;

        switch (ex)
        {
            case NoInternetConnectionException:
                errorKey = "Common.Network.NoConnection";
                break;

            case LostInternetConnectionException:
                errorKey = "Common.Network.ConnectionLost";
                break;

            case OllamaLocalServerUnreachableException:
                errorKey = "Ollama.Connection.LocalUnreachable";
                break;

            case OllamaRemoteServerUnreachableException:
                errorKey = "Ollama.Connection.RemoteUnreachable";
                break;

            case OllamaApiException apiEx:
                errorKey = "Downloads.Error.Failed";
                arg = apiEx.StatusCode.ToString();
                break;

            case InsufficientDiskSpaceException spaceEx:
                errorKey = "Downloads.Error.InsufficientSpace";
                arg = ConversionHelper.BytesToReadableSize(spaceEx.RequiredBytes);
                break;

            case DiskFullException:
                errorKey = "Downloads.Error.DiskFull";
                break;

            default:
                errorKey = "Downloads.Error.Failed";
                arg = ex.Message;
                break;
        }

        var localizedMessage = LocalizationService.GetString(errorKey);

        // safely format the localized string if an argument is provided
        if (!string.IsNullOrEmpty(arg))
        {
            try
            {
                localizedMessage = string.Format(localizedMessage, arg);
            }
            catch (FormatException)
            {
                localizedMessage += $" ({arg})";
            }
        }

        request.Status = new ModelDownloadStatus(DownloadState.Failed, localizedMessage);
    }

    /// <summary>
    /// Handles the cancellation logic for a download request, updating its state
    /// based on the explicit reason for cancellation.
    /// </summary>
    /// <param name="request">The request that was canceled.</param>
    protected override void OnItemCanceled(ModelDownloadRequest request)
    {
        request.DownloadSpeed = 0.0;
        request.SpeedCalculator.Reset();

        switch (request.QueueItemCancellationReason)
        {
            case QueueItemCancellationReason.UserCancelRequest:
                // full abort, reset all progress
                request.Status = new ModelDownloadStatus(DownloadState.Downloadable);
                request.DownloadedBytes = 0;
                request.TotalBytes = 0;
                request.DownloadPartCount = 0;
                break;

            case QueueItemCancellationReason.SystemScaling:
                // service is scaling down parallelism, requeue the item for later processing
                request.Status = new ModelDownloadStatus(DownloadState.Queued);
                request.QueueItemCancellationReason = QueueItemCancellationReason.Unknown;
                request.ResetToken();
                Enqueue(request);
                break;

            case QueueItemCancellationReason.UserPauseRequest:
            default:
                request.Status = new ModelDownloadStatus(DownloadState.Paused);
                break;
        }
    }
}
