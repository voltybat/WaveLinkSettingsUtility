using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace WaveLinkSettingsUtility;

public enum AudioEndpointState { Active, Disabled, NotPresent, Unplugged, Missing, Unknown }
public sealed record AudioEndpointResult(AudioEndpointState State, string? Detail = null);
public sealed record AudioEndpoint(string Id, string FriendlyName);
public interface IAudioEndpointInspector
{
    AudioEndpointResult Inspect(string deviceId);
    IReadOnlyList<AudioEndpoint> GetActiveCaptureEndpoints();
}

public sealed record ReplacementSuggestion(AudioEndpoint Endpoint, bool ExactNameMatch);

public static partial class EndpointReplacementMatcher
{
    public static IReadOnlyList<ReplacementSuggestion> Find(HardwareChannel channel, IEnumerable<AudioEndpoint> endpoints) =>
        endpoints.Where(endpoint => !string.Equals(endpoint.Id, channel.DeviceId, StringComparison.OrdinalIgnoreCase))
            .Select(endpoint => new ReplacementSuggestion(endpoint,
                string.Equals(channel.InputName, endpoint.FriendlyName, StringComparison.OrdinalIgnoreCase)))
            .Where(suggestion => suggestion.ExactNameMatch ||
                string.Equals(Normalize(channel.InputName), Normalize(suggestion.Endpoint.FriendlyName), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(suggestion => suggestion.ExactNameMatch)
            .ThenBy(suggestion => suggestion.Endpoint.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string Normalize(string value) => NumberedDeviceRegex().Replace(value.Trim(), "(");

    [GeneratedRegex(@"\(\d+-\s*", RegexOptions.CultureInvariant)]
    private static partial Regex NumberedDeviceRegex();
}

public sealed class WindowsAudioEndpointInspector : IAudioEndpointInspector
{
    private const uint Active = 0x1;

    public AudioEndpointResult Inspect(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return new(AudioEndpointState.Missing, "Wave Link stored an empty endpoint ID.");
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            var hr = enumerator.GetDevice(deviceId, out device);
            if (hr < 0 || device is null) return new(AudioEndpointState.Missing, $"Windows could not resolve the endpoint (0x{hr:X8}).");
            hr = device.GetState(out var state);
            if (hr < 0) return new(AudioEndpointState.Unknown, $"Windows could not read the endpoint state (0x{hr:X8}).");
            return new(state switch
            {
                0x1 => AudioEndpointState.Active,
                0x2 => AudioEndpointState.Disabled,
                0x4 => AudioEndpointState.NotPresent,
                0x8 => AudioEndpointState.Unplugged,
                _ => AudioEndpointState.Unknown
            });
        }
        catch (COMException ex) { return new(AudioEndpointState.Missing, $"Windows could not resolve the endpoint (0x{ex.HResult:X8})."); }
        catch (Exception ex) { return new(AudioEndpointState.Unknown, ex.Message); }
        finally
        {
            if (device is not null && Marshal.IsComObject(device)) Marshal.ReleaseComObject(device);
            if (enumerator is not null && Marshal.IsComObject(enumerator)) Marshal.ReleaseComObject(enumerator);
        }
    }

    public IReadOnlyList<AudioEndpoint> GetActiveCaptureEndpoints()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        var results = new List<AudioEndpoint>();
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(1, Active, out collection)); // eCapture
            Marshal.ThrowExceptionForHR(collection.GetCount(out var count));
            for (uint i = 0; i < count; i++)
            {
                IMMDevice? device = null;
                IPropertyStore? properties = null;
                try
                {
                    Marshal.ThrowExceptionForHR(collection.Item(i, out device));
                    Marshal.ThrowExceptionForHR(device.GetId(out var id));
                    Marshal.ThrowExceptionForHR(device.OpenPropertyStore(0, out properties));
                    var key = FriendlyNameKey;
                    Marshal.ThrowExceptionForHR(properties.GetValue(ref key, out var value));
                    try
                    {
                        var name = value.GetString();
                        if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name)) results.Add(new(id, name));
                    }
                    finally { PropVariantClear(ref value); }
                }
                finally
                {
                    if (properties is not null) Marshal.ReleaseComObject(properties);
                    if (device is not null) Marshal.ReleaseComObject(device);
                }
            }
            return results;
        }
        finally
        {
            if (collection is not null) Marshal.ReleaseComObject(collection);
            if (enumerator is not null) Marshal.ReleaseComObject(enumerator);
        }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator;

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { public Guid FormatId; public uint PropertyId; }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort VariantType;
        [FieldOffset(8)] public IntPtr Pointer;
        public readonly string? GetString() => VariantType == 31 && Pointer != IntPtr.Zero ? Marshal.PtrToStringUni(Pointer) : null;
    }

    private static PropertyKey FriendlyNameKey => new()
    {
        FormatId = new("A45C254E-DF1C-4EFD-8020-67D146A850E0"), PropertyId = 14
    };

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
}
