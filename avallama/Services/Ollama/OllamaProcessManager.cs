// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using avallama.Constants.States;
using avallama.Models.Ollama;

namespace avallama.Services.Ollama;

// TODO:
// stop process when user changes to a remote connection and restart if its changed to local
// (only if the user started the process from Avallama, otherwise ignore it)

/// <summary>
/// Delegate for handling changes in the Ollama process status.
/// </summary>
internal delegate void OllamaProcessStatusChangedHandler(OllamaProcessStatus status);

/// <summary>
/// Defines a contract for managing the lifecycle of the local Ollama process.
/// </summary>
internal interface IOllamaProcessManager
{
    #region Interface

    /// <summary>
    /// Gets the current status of the Ollama process.
    /// </summary>
    OllamaProcessStatus Status { get; }

    /// <summary>
    /// Event raised when the process status changes.
    /// </summary>
    event OllamaProcessStatusChangedHandler? StatusChanged;

    /// <summary>
    /// Starts the Ollama process asynchronously.
    /// </summary>
    Task StartAsync();

    /// <summary>
    /// Stops the Ollama process asynchronously.
    /// </summary>
    Task StopAsync();

    #endregion
}

/// <summary>
/// Manages the lifecycle of the local Ollama server process, including starting, stopping, and monitoring its status.
/// Handles both processes started by the application and existing instances.
/// </summary>
internal class OllamaProcessManager : IOllamaProcessManager, IDisposable
{
    #region Dependencies & Fields

    /// <summary>
    /// Function to create and start a new process. Can be replaced for testing purposes.
    /// </summary>
    public Func<ProcessStartInfo, IOllamaProcess?> StartProcessFunc { get; init; } = psi =>
    {
        var process = Process.Start(psi);
        return process != null ? new OllamaProcess(process) : null;
    };

    /// <summary>
    /// Function to retrieve running processes. Can be replaced for testing purposes.
    /// </summary>
    public Func<IEnumerable<IOllamaProcess>> GetProcessesFunc { get; init; }

    /// <summary>
    /// Function to retrieve the Ollama executable path. Can be replaced for testing purposes.
    /// </summary>
    public Func<string> GetExecutablePathFunc { get; init; }

    /// <summary>
    /// Function to check if Ollama is running as a systemd service (Linux only). Can be replaced for testing purposes.
    /// </summary>
    public Func<bool> CheckSystemdStatusFunc { get; init; }

    private readonly SemaphoreSlim _startSemaphore = new(1, 1);
    private string OllamaPath { get; set; } = "";
    private IOllamaProcess? _ollamaProcess;
    private bool _isProcessStartedByAvallama;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="OllamaProcessManager"/> class.
    /// </summary>
    public OllamaProcessManager()
    {
        // Initialize default implementations if not provided (e.g., by tests)
        GetProcessesFunc ??= () => Process.GetProcessesByName("ollama").Select(p => new OllamaProcess(p));
        CheckSystemdStatusFunc ??= IsOllamaRunningAsSystemdService;
        GetExecutablePathFunc ??= GetOllamaExecutablePath;
    }

    #endregion

    #region Event & Status

    public event OllamaProcessStatusChangedHandler? StatusChanged;

    public OllamaProcessStatus Status
    {
        get;
        private set
        {
            if (field == value) return;
            field = value;
            StatusChanged?.Invoke(value);
        }
    } = new(OllamaProcessState.Stopped);

    #endregion

    #region Public Methods

    public async Task StartAsync()
    {
        await _startSemaphore.WaitAsync();

        try
        {
            Status = new OllamaProcessStatus(OllamaProcessState.Starting);

            OllamaPath = GetExecutablePathFunc();

            if (string.IsNullOrEmpty(OllamaPath))
            {
                Status = new OllamaProcessStatus(OllamaProcessState.NotInstalled);
                return;
            }

            // check for existing instances (real server processes).
            // we get all processes named "ollama" and ensure they don't have a window handle
            // (to distinguish from GUI wrappers).
            var ollamaServerProcesses = GetProcessesFunc()
                .Where(p => p.MainWindowHandle == IntPtr.Zero)
                .Where(p => !p.ProcessName.Equals("Ollama", StringComparison.Ordinal))
                .ToArray();

            if (ollamaServerProcesses.Length == 1)
            {
                // attach to the existing process
                _isProcessStartedByAvallama = false;
                _ollamaProcess = ollamaServerProcesses.FirstOrDefault();
                if (_ollamaProcess != null)
                {
                    _ollamaProcess.EnableRaisingEvents = true;
                    _ollamaProcess.Exited += OnOllamaProcessExited;
                }

                Status = new OllamaProcessStatus(OllamaProcessState.Running,
                    LocalizationService.GetString("Ollama.Process.AlreadyRunning"));
                return;
            }
            else if (ollamaServerProcesses.Length > 1)
            {
                _isProcessStartedByAvallama = false;
                // multiple instances found, report failure rather than attempting to kill them
                Status = new OllamaProcessStatus(OllamaProcessState.Failed,
                    LocalizationService.GetString("Ollama.Process.MultipleInstances"));
                return;
            }

            // TODO: Add custom env variables (e.g. OLLAMA_MODELS for saving local models at a custom path)
            // attempt to start a new process
            var startInfo = new ProcessStartInfo
            {
                FileName = OllamaPath,
                Arguments = "serve",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            IOllamaProcess? tempProcess = null;
            try
            {
                tempProcess = StartProcessFunc(startInfo);
                if (tempProcess != null)
                {
                    tempProcess.EnableRaisingEvents = true;
                    tempProcess.Exited += OnOllamaProcessExited;

                    // Only set newly started process if everything ran correctly without exceptions
                    _ollamaProcess = tempProcess;
                    _isProcessStartedByAvallama = true;
                    Status = new OllamaProcessStatus(OllamaProcessState.Running);
                    return;
                }

                Status = new OllamaProcessStatus(OllamaProcessState.NotInstalled);
            }
            catch (Win32Exception)
            {
                Status = new OllamaProcessStatus(OllamaProcessState.NotInstalled);
                try { tempProcess?.Kill(); } catch { /* ignore */ }
                tempProcess?.Dispose();
            }
            catch (Exception ex)
            {
                Status = new OllamaProcessStatus(OllamaProcessState.Failed,
                    string.Format(LocalizationService.GetString("Ollama.Process.StartFailed"), ex.Message));
                try { tempProcess?.Kill(); } catch { /* ignore */ }
                tempProcess?.Dispose();
            }
        }
        finally
        {
            _startSemaphore.Release();
        }
    }

    public async Task StopAsync()
    {
        await _startSemaphore.WaitAsync();

        // do not stop if the user started the process outside the app
        if (!_isProcessStartedByAvallama)
        {
            _startSemaphore.Release();
            return;
        }

        try
        {
            // on Linux, check if running as a systemd service before attempting to kill
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && CheckSystemdStatusFunc())
            {
                return;
            }

            // kills the Ollama process
            if (_ollamaProcess != null)
            {
                _ollamaProcess.EnableRaisingEvents = false;
                _ollamaProcess.Exited -= OnOllamaProcessExited;
                _ollamaProcess.Kill();
                await _ollamaProcess.WaitForExitAsync();
                _ollamaProcess.Dispose();
                _isProcessStartedByAvallama = false;
            }
        }
        finally
        {
            _startSemaphore.Release();
        }

        _ollamaProcess = null;
        Status = new OllamaProcessStatus(OllamaProcessState.Stopped);
    }

    #endregion

    #region Private Methods

    private void OnOllamaProcessExited(object? sender, EventArgs e)
    {
        // ignore process exit if the user stopped the process intentionally via the app
        if (Status.ProcessState == OllamaProcessState.Stopped) return;

        string? message = null;
        var newState = OllamaProcessState.Stopped;

        if (sender is IOllamaProcess process)
        {
            try
            {
                if (process.ExitCode != 0)
                {
                    newState = OllamaProcessState.Failed;
                    message = LocalizationService.GetString("Ollama.Process.StoppedUnexpectedly")
                              + $" ({LocalizationService.GetString("Ollama.Process.ExitCodeLabel")}: {process.ExitCode})";
                }
            }
            catch (Exception)
            {
                newState = OllamaProcessState.Failed;
                message = LocalizationService.GetString("Ollama.Process.StoppedUnexpectedly");
            }
        }

        Status = new OllamaProcessStatus(newState, message);

        _ollamaProcess?.Dispose();
        _ollamaProcess = null;
    }

    private string GetOllamaExecutablePath()
    {
        var executableName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ollama.exe" : "ollama";

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        if (string.IsNullOrEmpty(pathVariable)) return string.Empty;

        var paths = pathVariable.Split(Path.PathSeparator);

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;

            var fullPath = Path.Combine(path.Trim(), executableName);

            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return string.Empty;
    }

    private static bool IsOllamaRunningAsSystemdService()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            throw new InvalidOperationException("Systemd check should only be called on Linux");
        }

        var psiRoot = new ProcessStartInfo
        {
            FileName = "systemctl",
            Arguments = "is-active ollama",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var psiUser = new ProcessStartInfo
        {
            FileName = "systemctl",
            Arguments = "--user is-active ollama",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            // Check system-level service
            using var processRoot = Process.Start(psiRoot);
            if (processRoot == null) return false;

            processRoot.WaitForExit();
            if (processRoot.StandardOutput.ReadToEnd().Trim() == "active") return true;

            // Retry with user-level service
            using var processUser = Process.Start(psiUser);
            if (processUser == null) return false;

            processUser.WaitForExit();
            return processUser.StandardOutput.ReadToEnd().Trim() == "active";
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            // 'systemctl' command not found
            return false;
        }
        catch (Win32Exception)
        {
            // Permission denied or other errors
            return false;
        }
    }

    #endregion

    #region Dispose

    public void Dispose()
    {
        _startSemaphore.Dispose();
        StopAsync().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    #endregion
}
