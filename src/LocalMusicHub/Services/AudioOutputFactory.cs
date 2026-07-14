using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LocalMusicHub.Services;

public static class AudioOutputFactory
{
    public static IWavePlayer Create(string backend, string? deviceId)
    {
        if (string.Equals(backend, "wasapi", StringComparison.OrdinalIgnoreCase))
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = !string.IsNullOrWhiteSpace(deviceId)
                ? enumerator.GetDevice(deviceId)
                : enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return new WasapiOut(device, AudioClientShareMode.Shared, false, 200);
        }

        return new WaveOutEvent();
    }

    public static IReadOnlyList<AudioDeviceInfo> ListOutputDevices()
    {
        var list = new List<AudioDeviceInfo>
        {
            new("default", "System default"),
        };

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                list.Add(new AudioDeviceInfo(device.ID, device.FriendlyName));
            }
        }
        catch
        {
            /* ignore enumeration failures */
        }

        return list;
    }
}

public readonly record struct AudioDeviceInfo(string Id, string Name);
