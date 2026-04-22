using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ShadowLink.Core.Contracts;
using ShadowLink.Core.Models;

namespace ShadowLink.Services;

public sealed class RemoteSessionWindowManager : IRemoteSessionWindowManager
{
    private readonly IUiDispatcher _uiDispatcher;
    private readonly Dictionary<String, RemoteDisplayWindow> _windows;
    private Boolean _suppressCloseNotifications;

    public RemoteSessionWindowManager(IUiDispatcher uiDispatcher)
    {
        _uiDispatcher = uiDispatcher;
        _windows = new Dictionary<String, RemoteDisplayWindow>(StringComparer.OrdinalIgnoreCase);
    }

    public event EventHandler? AllDisplaysClosed;

    public void ShowDisplays(IReadOnlyList<RemoteDisplayDescriptor> displays, String releaseGesture, Func<RemoteInputEvent, Task> inputSink, ConnectionDirection direction, RemoteDisplayScaleMode displayScaleMode)
    {
        _uiDispatcher.Post(() =>
        {
            HashSet<String> incomingIds = new HashSet<String>(displays.Select(item => item.DisplayId), StringComparer.OrdinalIgnoreCase);

            foreach (RemoteDisplayDescriptor display in displays)
            {
                if (_windows.ContainsKey(display.DisplayId))
                {
                    continue;
                }

                RemoteDisplayWindow window = new RemoteDisplayWindow(display, releaseGesture, inputSink, direction, displayScaleMode);
                window.Closed += (_, _) => HandleWindowClosed(display.DisplayId);
                _windows.Add(display.DisplayId, window);
                window.Show();
            }

            foreach (String existingId in _windows.Keys.ToArray())
            {
                if (!incomingIds.Contains(existingId))
                {
                    CloseWindow(existingId);
                }
            }
        });
    }

    public void UpdateFrame(String displayId, Byte[] framePixels, Int32 frameWidth, Int32 frameHeight, Int32 frameStride)
    {
        Byte[] frameCopy = new Byte[framePixels.Length];
        Buffer.BlockCopy(framePixels, 0, frameCopy, 0, framePixels.Length);

        _uiDispatcher.Post(() =>
        {
            if (_windows.TryGetValue(displayId, out RemoteDisplayWindow? window))
            {
                window.UpdateFrame(frameCopy, frameWidth, frameHeight, frameStride);
            }
        });
    }

    public void CloseAll()
    {
        _uiDispatcher.Post(() =>
        {
            _suppressCloseNotifications = true;

            try
            {
                foreach (String displayId in _windows.Keys.ToArray())
                {
                    CloseWindow(displayId);
                }
            }
            finally
            {
                _suppressCloseNotifications = false;
                _windows.Clear();
            }
        });
    }

    private void CloseWindow(String displayId)
    {
        if (_windows.TryGetValue(displayId, out RemoteDisplayWindow? window))
        {
            _windows.Remove(displayId);
            window.Close();
        }
    }

    private void HandleWindowClosed(String displayId)
    {
        _windows.Remove(displayId);

        if (!_suppressCloseNotifications && _windows.Count == 0)
        {
            AllDisplaysClosed?.Invoke(this, EventArgs.Empty);
        }
    }
}
