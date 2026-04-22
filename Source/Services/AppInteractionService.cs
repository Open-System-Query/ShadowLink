using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ShadowLink.Core.Contracts;
using ShadowLink.Core.Models;
using ShadowLink.Localization;

namespace ShadowLink.Services;

public sealed class AppInteractionService : IAppInteractionService
{
    public Boolean CanOfferElevatedControl => OperatingSystem.IsWindows() && !IsElevatedControlEnabled;

    public Boolean IsElevatedControlEnabled
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return true;
            }

            return IsCurrentProcessElevated() || IsCurrentProcessUiAccessEnabled();
        }
    }

    public Task<Boolean> PromptForElevatedStartupAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (global::ShadowLink.StartupControlElevation.HasHandledStartupRequest)
        {
            return Task.FromResult(false);
        }

        if (!CanOfferElevatedControl)
        {
            return Task.FromResult(false);
        }

        String? processPath = Environment.ProcessPath;
        if (String.IsNullOrWhiteSpace(processPath))
        {
            return Task.FromResult(false);
        }

        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo(processPath)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            };

            String[] arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
            if (arguments.Length > 0)
            {
                startInfo.Arguments = String.Join(" ", arguments.Select(QuoteArgument));
            }

            Process.Start(startInfo);
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }

            return Task.FromResult(true);
        }
        catch (Win32Exception)
        {
            return Task.FromResult(false);
        }
    }

    public async Task<String?> PromptForPassphraseAsync(String title, String detail, CancellationToken cancellationToken)
    {
        String? passphrase = await ShowPassphraseDialogOnUiThreadAsync(title, detail).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return passphrase;
    }

    public async Task<String?> GetClipboardTextAsync(CancellationToken cancellationToken)
    {
        String? text = await GetClipboardTextOnUiThreadAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return text;
    }

    public async Task SetClipboardTextAsync(String text, CancellationToken cancellationToken)
    {
        await SetClipboardTextOnUiThreadAsync(text).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task<IReadOnlyList<LocalFileReference>> PickFilesForTransferAsync(String title, CancellationToken cancellationToken)
    {
        IReadOnlyList<LocalFileReference> files = await PickFilesForTransferOnUiThreadAsync(title).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return files;
    }

    private static Window? GetOwnerWindow()
    {
        return Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
    }

    [SupportedOSPlatform("windows")]
    private static Boolean IsCurrentProcessElevated()
    {
        WindowsIdentity? identity = WindowsIdentity.GetCurrent();
        if (identity is null)
        {
            return false;
        }

        WindowsPrincipal principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    [SupportedOSPlatform("windows")]
    private static Boolean IsCurrentProcessUiAccessEnabled()
    {
        IntPtr processHandle = AppInteractionNativeMethods.GetCurrentProcess();
        if (!AppInteractionNativeMethods.OpenProcessToken(processHandle, AppInteractionNativeMethods.TokenQuery, out IntPtr tokenHandle))
        {
            return false;
        }

        try
        {
            IntPtr buffer = Marshal.AllocHGlobal(sizeof(Int32));
            try
            {
                if (!AppInteractionNativeMethods.GetTokenInformation(tokenHandle, TokenInformationClass.TokenUIAccess, buffer, sizeof(Int32), out Int32 returnLength) ||
                    returnLength < sizeof(Int32))
                {
                    return false;
                }

                return Marshal.ReadInt32(buffer) != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            AppInteractionNativeMethods.CloseHandle(tokenHandle);
        }
    }

    private static String QuoteArgument(String value)
    {
        return value.Contains(' ') ? "\"" + value.Replace("\"", "\\\"") + "\"" : value;
    }

    private static async Task<Boolean?> ShowConfirmationDialogOnUiThreadAsync(String title, String detail, String primaryLabel, String secondaryLabel)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Window? owner = GetOwnerWindow();
            if (owner is null)
            {
                return false;
            }

            return await ShowConfirmationDialogAsync(owner, title, detail, primaryLabel, secondaryLabel).ConfigureAwait(true);
        }

        TaskCompletionSource<Boolean?> completionSource = new TaskCompletionSource<Boolean?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                completionSource.SetResult(await ShowConfirmationDialogOnUiThreadAsync(title, detail, primaryLabel, secondaryLabel).ConfigureAwait(true));
            }
            catch (Exception ex)
            {
                completionSource.SetException(ex);
            }
        });
        return await completionSource.Task.ConfigureAwait(false);
    }

    private static async Task<String?> ShowPassphraseDialogOnUiThreadAsync(String title, String detail)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Window? owner = GetOwnerWindow();
            if (owner is null)
            {
                return null;
            }

            PassphraseDialogWindow dialogWindow = new PassphraseDialogWindow(title, detail);
            return await dialogWindow.ShowDialog<String?>(owner).ConfigureAwait(true);
        }

        TaskCompletionSource<String?> completionSource = new TaskCompletionSource<String?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                completionSource.SetResult(await ShowPassphraseDialogOnUiThreadAsync(title, detail).ConfigureAwait(true));
            }
            catch (Exception ex)
            {
                completionSource.SetException(ex);
            }
        });
        return await completionSource.Task.ConfigureAwait(false);
    }

    private static async Task<String?> GetClipboardTextOnUiThreadAsync()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Window? owner = GetOwnerWindow();
            return owner?.Clipboard is null
                ? null
                : await ClipboardExtensions.TryGetTextAsync(owner.Clipboard).ConfigureAwait(true);
        }

        TaskCompletionSource<String?> completionSource = new TaskCompletionSource<String?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                completionSource.SetResult(await GetClipboardTextOnUiThreadAsync().ConfigureAwait(true));
            }
            catch (Exception ex)
            {
                completionSource.SetException(ex);
            }
        });
        return await completionSource.Task.ConfigureAwait(false);
    }

    private static async Task SetClipboardTextOnUiThreadAsync(String text)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Window? owner = GetOwnerWindow();
            if (owner?.Clipboard is not null)
            {
                await ClipboardExtensions.SetTextAsync(owner.Clipboard, text ?? String.Empty).ConfigureAwait(true);
            }

            return;
        }

        TaskCompletionSource completionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await SetClipboardTextOnUiThreadAsync(text).ConfigureAwait(true);
                completionSource.SetResult();
            }
            catch (Exception ex)
            {
                completionSource.SetException(ex);
            }
        });
        await completionSource.Task.ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<LocalFileReference>> PickFilesForTransferOnUiThreadAsync(String title)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Window? owner = GetOwnerWindow();
            if (owner?.StorageProvider is null || !owner.StorageProvider.CanOpen)
            {
                return Array.Empty<LocalFileReference>();
            }

            IReadOnlyList<IStorageFile> pickedFiles = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = true
            }).ConfigureAwait(true);

            List<LocalFileReference> resolvedFiles = new List<LocalFileReference>(pickedFiles.Count);
            foreach (IStorageFile pickedFile in pickedFiles)
            {
                String? localPath = pickedFile.TryGetLocalPath();
                if (String.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
                {
                    continue;
                }

                FileInfo fileInfo = new FileInfo(localPath);
                resolvedFiles.Add(new LocalFileReference
                {
                    LocalPath = localPath,
                    FileName = fileInfo.Name,
                    Length = fileInfo.Length
                });
            }

            return resolvedFiles;
        }

        TaskCompletionSource<IReadOnlyList<LocalFileReference>> completionSource = new TaskCompletionSource<IReadOnlyList<LocalFileReference>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                completionSource.SetResult(await PickFilesForTransferOnUiThreadAsync(title).ConfigureAwait(true));
            }
            catch (Exception ex)
            {
                completionSource.SetException(ex);
            }
        });
        return await completionSource.Task.ConfigureAwait(false);
    }

    private static Task<Boolean?> ShowConfirmationDialogAsync(Window owner, String title, String detail, String primaryLabel, String secondaryLabel)
    {
        SimpleChoiceDialogWindow dialogWindow = new SimpleChoiceDialogWindow(title, detail, primaryLabel, secondaryLabel);
        return dialogWindow.ShowDialog<Boolean?>(owner);
    }
}
