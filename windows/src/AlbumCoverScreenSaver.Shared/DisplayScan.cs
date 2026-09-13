using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AlbumCoverScreenSaver.Shared;

/// <summary>
/// Asking Windows what displays are connected, and what to call them.
/// </summary>
/// <remarks>
/// <para>
/// This lives in the shared library rather than in either application because
/// both need the same answer and they must agree exactly: the settings window
/// files a choice under a key, and the screen saver looks it up under the same
/// key. Two copies of this that drifted apart would mean a setting that saves
/// and never applies, with nothing anywhere to say why.
/// </para>
/// <para>
/// It is the one piece of Windows interop in a library that otherwise targets
/// anything. Off Windows it returns nothing rather than failing, so the tests,
/// which run wherever they are asked to, can call it and get a sensible answer.
/// </para>
/// </remarks>
public static class DisplayScan
{
    /// <summary>
    /// Every connected display, keyed so its settings survive a reboot.
    /// </summary>
    /// <remarks>
    /// Empty off Windows, and empty rather than throwing if Windows refuses the
    /// question. A settings window with no Screens section is a small loss; one
    /// that will not open is not.
    /// </remarks>
    public static List<DisplayCard> Connected()
    {
        if (!OperatingSystem.IsWindows()) return [];

        try
        {
            return Scan();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// True when Windows is set to Duplicate, so several monitors are one screen.
    /// </summary>
    /// <remarks>
    /// The only thing this is used for is one line of explanatory text. A user
    /// with two duplicated monitors sees one entry and needs to be told why,
    /// otherwise the feature reads as broken. It must not be used for anything
    /// else: an application fundamentally cannot put different content on the
    /// two halves of a duplicated pair, because they share one framebuffer.
    /// </remarks>
    public static bool IsDuplicated()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            return Duplicated();
        }
        catch (Exception)
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static List<DisplayCard> Scan()
    {
        var cards = new List<DisplayCard>();

        if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount) != 0)
        {
            return cards;
        }

        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

        if (QueryDisplayConfig(
                QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, nint.Zero) != 0)
        {
            return cards;
        }

        var index = 0;

        foreach (var path in paths)
        {
            var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME
            {
                header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = DeviceInfoGetTargetName,
                    size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                    adapterId = path.targetInfo.adapterId,
                    id = path.targetInfo.id,
                },
            };

            var named = DisplayConfigGetDeviceInfo(ref target) == 0;

            var key = named
                ? DisplayKeys.For(
                    target.monitorDevicePath, target.edidManufactureId,
                    target.edidProductCodeId, target.connectorInstance)
                : DisplayKeys.FromDeviceName(null, index);

            var friendly = named && !string.IsNullOrWhiteSpace(target.monitorFriendlyDeviceName)
                ? target.monitorFriendlyDeviceName.Trim()
                : $"Display {index + 1}";

            var rect = SourceRect(path, modes, out var isPrimary);

            cards.Add(new DisplayCard(key, friendly, rect, isPrimary, Connected: true));
            index++;
        }

        return cards;
    }

    /// <summary>
    /// Where a display sits on the virtual desktop.
    /// </summary>
    /// <remarks>
    /// Taken from the source mode rather than from the target, because the
    /// source is the part of the desktop being sent to the monitor and that is
    /// what the arrangement map is a picture of.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    private static int[] SourceRect(
        DISPLAYCONFIG_PATH_INFO path, DISPLAYCONFIG_MODE_INFO[] modes, out bool isPrimary)
    {
        isPrimary = false;

        var index = path.sourceInfo.modeInfoIdx;
        if (index >= modes.Length) return [0, 0, 1920, 1080];

        var mode = modes[index];
        if (mode.infoType != ModeInfoTypeSource) return [0, 0, 1920, 1080];

        var source = mode.sourceMode;

        // The primary display is the one whose top-left corner is the origin of
        // the virtual desktop. Everything else is placed relative to it.
        isPrimary = source.position.x == 0 && source.position.y == 0;

        return [source.position.x, source.position.y, (int)source.width, (int)source.height];
    }

    [SupportedOSPlatform("windows")]
    private static bool Duplicated()
    {
        if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount) != 0)
        {
            return false;
        }

        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

        if (QueryDisplayConfig(
                QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, nint.Zero) != 0)
        {
            return false;
        }

        // Clone mode is more than one active target fed from the same source.
        var sources = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var path in paths)
        {
            var key = $"{path.sourceInfo.adapterId.High}:{path.sourceInfo.adapterId.Low}:{path.sourceInfo.id}";
            sources[key] = sources.GetValueOrDefault(key) + 1;
        }

        return sources.Values.Any(count => count > 1);
    }

    // --- interop ------------------------------------------------------------

    private const uint QdcOnlyActivePaths = 2;
    private const uint DeviceInfoGetTargetName = 2;
    private const uint ModeInfoTypeSource = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint Low;
        public int High;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public ulong refreshRate;
        public uint scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTL
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_SOURCE_MODE
    {
        public uint width;
        public uint height;
        public uint pixelFormat;
        public POINTL position;
    }

    /// <summary>
    /// The mode union, held as a source mode plus padding.
    /// </summary>
    /// <remarks>
    /// The real structure is a union of a target mode, a source mode and a
    /// desktop image mode, and the target mode is the largest. Only the source
    /// mode is read here, so the rest is reserved space: it has to be the right
    /// size or the array stride is wrong and every entry after the first is
    /// read from the wrong place.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        public uint infoType;
        public uint id;
        public LUID adapterId;
        public DISPLAYCONFIG_SOURCE_MODE sourceMode;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 44)]
        public byte[] reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public uint type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string monitorFriendlyDeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string monitorDevicePath;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(
        uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint pathCount,
        [Out] DISPLAYCONFIG_PATH_INFO[] paths,
        ref uint modeCount,
        [Out] DISPLAYCONFIG_MODE_INFO[] modes,
        nint currentTopology);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME request);
}
