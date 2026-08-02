using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LocalMusicHub.Services;

public static class AudioOutputFactory
{
    /// <summary>Default buffer — roomy enough for Bluetooth (AirPods) without feeling laggy.</summary>
    public const int DefaultLatencyMs = 300;

    public static IWavePlayer Create(string backend, string? deviceId, int latencyMs = DefaultLatencyMs)
    {
        var latency = Math.Clamp(latencyMs <= 0 ? DefaultLatencyMs : latencyMs, 100, 800);

        // Device IDs come from WASAPI enumeration; WaveOut cannot target them.
        var useWasapi = string.Equals(backend, "wasapi", StringComparison.OrdinalIgnoreCase)
                        || !string.IsNullOrWhiteSpace(deviceId);

        if (useWasapi)
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = !string.IsNullOrWhiteSpace(deviceId)
                ? enumerator.GetDevice(deviceId)
                : enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            // Event sync refills as soon as WASAPI needs data (timer sync underruns more often,
            // especially on Bluetooth). Larger latency = bigger underrun safety margin.
            return new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latency);
        }

        return new WaveOutEvent
        {
            DesiredLatency = latency,
            NumberOfBuffers = 3,
        };
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
