using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ShadowLink.Core.Contracts;
using ShadowLink.Core.Models;
using ShadowLink.Infrastructure.Serialization;

namespace ShadowLink.Infrastructure.Persistence;

public sealed class AppDataSettingsRepository : ISettingsRepository
{
    private const String ApplicationFolderName = "ShadowLink";
    private const String SettingsFileName = "settings.json";
    private const String TemporaryFileSuffix = ".tmp";
    private const String CorruptFileSuffix = ".corrupt-";
    private const Int32 FileBufferSize = 4096;
    private readonly String _settingsPath;

    public AppDataSettingsRepository()
    {
        String applicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _settingsPath = Path.Combine(applicationDataPath, ApplicationFolderName, SettingsFileName);
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsPath))
        {
            AppSettings defaults = AppSettings.CreateDefault();
            await SaveAsync(defaults, cancellationToken).ConfigureAwait(false);
            return defaults;
        }

        try
        {
            await using FileStream stream = new FileStream(_settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read, FileBufferSize, FileOptions.Asynchronous);
            AppSettings? settings = await JsonSerializer.DeserializeAsync(stream, ShadowLinkJsonSerializerContext.Default.AppSettings, cancellationToken).ConfigureAwait(false);
            if (settings is not null)
            {
                return settings;
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
        {
            TryMoveCorruptedSettingsAside();
        }

        AppSettings fallbackSettings = AppSettings.CreateDefault();
        await SaveAsync(fallbackSettings, cancellationToken).ConfigureAwait(false);
        return fallbackSettings;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        String? directory = Path.GetDirectoryName(_settingsPath);
        if (!String.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        String tempPath = _settingsPath + TemporaryFileSuffix;
        try
        {
            await using (FileStream stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, FileBufferSize, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, settings, ShadowLinkJsonSerializerContext.Default.AppSettings, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(_settingsPath))
            {
                try
                {
                    File.Replace(tempPath, _settingsPath, null);
                    return;
                }
                catch (PlatformNotSupportedException)
                {
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }

                File.Delete(_settingsPath);
            }

            File.Move(tempPath, _settingsPath);
        }
        finally
        {
            TryDeleteTempFile(tempPath);
        }
    }

    private void TryMoveCorruptedSettingsAside()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return;
            }

            String backupPath = _settingsPath + CorruptFileSuffix + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            File.Move(_settingsPath, backupPath, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteTempFile(String tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
        }
    }
}
