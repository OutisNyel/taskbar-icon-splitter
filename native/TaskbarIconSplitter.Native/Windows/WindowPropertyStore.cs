using System.Runtime.InteropServices;

namespace TaskbarIconSplitter.Native.Windows;

internal static class WindowPropertyStore
{
    private static readonly Guid PropertyStoreInterfaceId =
        new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private static readonly Guid AppUserModelFormatId =
        new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    private static readonly PropertyKey AppIdKey =
        new(AppUserModelFormatId, 5);
    private static readonly PropertyKey RelaunchIconResourceKey =
        new(AppUserModelFormatId, 3);
    private static readonly PropertyKey PreventPinningKey =
        new(AppUserModelFormatId, 9);

    private const ushort VtEmpty = 0;
    private const ushort VtBool = 11;
    private const ushort VtLpwstr = 31;

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(
        IntPtr hwnd,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore propertyStore);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant variant);

    internal static string? GetAppUserModelId(IntPtr hwnd)
    {
        return GetStringValue(hwnd, AppIdKey);
    }

    internal static string? GetRelaunchIconResource(IntPtr hwnd)
    {
        return GetStringValue(hwnd, RelaunchIconResourceKey);
    }

    private static string? GetStringValue(
        IntPtr hwnd,
        PropertyKey propertyKey)
    {
        var store = GetStore(hwnd);
        try
        {
            var key = propertyKey;
            var hr = store.GetValue(ref key, out var value);
            Marshal.ThrowExceptionForHR(hr);
            try
            {
                return value.VariantType == VtLpwstr && value.PointerValue != IntPtr.Zero
                    ? Marshal.PtrToStringUni(value.PointerValue)
                    : null;
            }
            finally
            {
                _ = PropVariantClear(ref value);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    internal static void SetIdentity(
        IntPtr hwnd,
        string appUserModelId,
        string? iconResourcePath)
    {
        var store = GetStore(hwnd);
        try
        {
            var preventKey = PreventPinningKey;
            var prevent = PropVariant.FromBool(true);
            Marshal.ThrowExceptionForHR(store.SetValue(ref preventKey, ref prevent));

            var appIdKey = AppIdKey;
            var appId = PropVariant.FromString(appUserModelId);
            var iconKey = RelaunchIconResourceKey;
            var icon = string.IsNullOrWhiteSpace(iconResourcePath)
                ? PropVariant.Empty
                : PropVariant.FromString(iconResourcePath);
            try
            {
                Marshal.ThrowExceptionForHR(store.SetValue(ref appIdKey, ref appId));
                Marshal.ThrowExceptionForHR(store.SetValue(ref iconKey, ref icon));
                Marshal.ThrowExceptionForHR(store.Commit());
            }
            finally
            {
                appId.DisposeAllocatedString();
                icon.DisposeAllocatedString();
            }
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    internal static void RestoreIdentity(
        IntPtr hwnd,
        string? originalAppUserModelId,
        string? originalRelaunchIconResource)
    {
        var store = GetStore(hwnd);
        try
        {
            var preventKey = PreventPinningKey;
            var empty = PropVariant.Empty;
            Marshal.ThrowExceptionForHR(store.SetValue(ref preventKey, ref empty));

            var appIdKey = AppIdKey;
            if (string.IsNullOrWhiteSpace(originalAppUserModelId))
            {
                var emptyAppId = PropVariant.Empty;
                Marshal.ThrowExceptionForHR(
                    store.SetValue(ref appIdKey, ref emptyAppId));
            }
            else
            {
                var appId = PropVariant.FromString(originalAppUserModelId);
                try
                {
                    Marshal.ThrowExceptionForHR(
                        store.SetValue(ref appIdKey, ref appId));
                }
                finally
                {
                    appId.DisposeAllocatedString();
                }
            }

            var iconKey = RelaunchIconResourceKey;
            if (string.IsNullOrWhiteSpace(originalRelaunchIconResource))
            {
                var emptyIcon = PropVariant.Empty;
                Marshal.ThrowExceptionForHR(
                    store.SetValue(ref iconKey, ref emptyIcon));
            }
            else
            {
                var icon = PropVariant.FromString(
                    originalRelaunchIconResource);
                try
                {
                    Marshal.ThrowExceptionForHR(
                        store.SetValue(ref iconKey, ref icon));
                }
                finally
                {
                    icon.DisposeAllocatedString();
                }
            }

            Marshal.ThrowExceptionForHR(store.Commit());
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    private static IPropertyStore GetStore(IntPtr hwnd)
    {
        var iid = PropertyStoreInterfaceId;
        var hr = SHGetPropertyStoreForWindow(hwnd, ref iid, out var store);
        Marshal.ThrowExceptionForHR(hr);
        return store;
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint propertyCount);

        [PreserveSig]
        int GetAt(uint propertyIndex, out PropertyKey key);

        [PreserveSig]
        int GetValue(ref PropertyKey key, out PropVariant value);

        [PreserveSig]
        int SetValue(ref PropertyKey key, ref PropVariant value);

        [PreserveSig]
        int Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private readonly struct PropertyKey
    {
        public PropertyKey(Guid formatId, uint propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }

        public readonly Guid FormatId;
        public readonly uint PropertyId;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)]
        public ushort VariantType;

        [FieldOffset(8)]
        public IntPtr PointerValue;

        [FieldOffset(8)]
        public short BooleanValue;

        public static PropVariant Empty => new() { VariantType = VtEmpty };

        public static PropVariant FromBool(bool value)
        {
            return new PropVariant
            {
                VariantType = VtBool,
                BooleanValue = value ? (short)-1 : (short)0
            };
        }

        public static PropVariant FromString(string value)
        {
            return new PropVariant
            {
                VariantType = VtLpwstr,
                PointerValue = Marshal.StringToCoTaskMemUni(value)
            };
        }

        public void DisposeAllocatedString()
        {
            if (VariantType == VtLpwstr && PointerValue != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(PointerValue);
                PointerValue = IntPtr.Zero;
            }
        }
    }
}
