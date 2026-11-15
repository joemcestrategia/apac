using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ApacKiosk.Services;

internal sealed class UiAutomationHelper : IDisposable
{
    private static readonly Guid ClsidCuiAutomation = new("FF48DBA4-60EF-4201-AA87-54103EEF594E");
    private const int TreeScopeDescendants = 4;

    private IUIAutomation? _uia;
    private IUIAutomationCondition? _trueCondition;

    public List<DialogBlockedRegion> GetBlockedDialogRegions(IntPtr dialogHwnd)
    {
        var regions = new List<DialogBlockedRegion>();
        try
        {
            EnsureInitialized();
            if (_uia == null || _trueCondition == null || dialogHwnd == IntPtr.Zero)
                return regions;

            if (_uia.ElementFromHandle(dialogHwnd, out var root) != 0 || root == null)
                return regions;

            try
            {
                if (root.FindAll(TreeScopeDescendants, _trueCondition, out var elements) != 0 || elements == null)
                    return regions;

                try
                {
                    if (elements.get_Length(out var count) != 0)
                        return regions;

                    for (int i = 0; i < count; i++)
                    {
                        if (elements.GetElement(i, out var el) != 0 || el == null)
                            continue;

                        try
                        {
                            el.get_CurrentIsOffscreen(out var isOffscreen);
                            if (isOffscreen) continue;

                            el.get_CurrentAutomationId(out var autoId);
                            el.get_CurrentName(out var name);
                            el.get_CurrentClassName(out var className);

                            if (!IsBlockedUiaElement(autoId, name, className))
                                continue;

                            if (el.get_CurrentBoundingRectangle(out var rect) == 0 &&
                                rect.Right > rect.Left && rect.Bottom > rect.Top)
                            {
                                regions.Add(new DialogBlockedRegion
                                {
                                    Left = rect.Left,
                                    Top = rect.Top,
                                    Right = rect.Right,
                                    Bottom = rect.Bottom,
                                    Label = name ?? autoId ?? className ?? ""
                                });
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(el);
                        }
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(elements);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(root);
            }
        }
        catch { }

        return regions;
    }

    private static bool IsBlockedUiaElement(string? autoId, string? name, string? className)
    {
        if (!string.IsNullOrEmpty(autoId))
        {
            if (string.Equals(autoId, "{7DDC1264-7E4D-4F74-BBC0-D191987C8D0F}", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(autoId, "HelpButton", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(autoId, "SearchEditBox", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(autoId, "SearchBoxSearchButton", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (!string.IsNullOrEmpty(name))
        {
            var normalized = name.Replace("&", "").Trim();
            if (string.Equals(normalized, "Organizar", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "Organize", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "Ajuda", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "Help", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "Ocultar pastas", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "Procurar pastas", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "Hide Folders", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "Browse Folders", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "Ferramentas", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "Tools", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (!string.IsNullOrEmpty(className))
        {
            if (string.Equals(className, "SearchEditBox", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(className, "ProperTreeHost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void EnsureInitialized()
    {
        if (_uia != null) return;
        var type = Type.GetTypeFromCLSID(ClsidCuiAutomation, throwOnError: false);
        if (type == null) return;

        _uia = Activator.CreateInstance(type) as IUIAutomation;
        if (_uia != null)
            _uia.CreateTrueCondition(out _trueCondition);
    }

    public void Dispose()
    {
        try
        {
            if (_trueCondition != null)
            {
                Marshal.ReleaseComObject(_trueCondition);
                _trueCondition = null;
            }
            if (_uia != null)
            {
                Marshal.ReleaseComObject(_uia);
                _uia = null;
            }
        }
        catch { }
    }

    internal struct DialogBlockedRegion
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public string Label;

        public readonly bool Contains(int x, int y) => x >= Left && x <= Right && y >= Top && y <= Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UiaRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UiaPoint
    {
        public int X;
        public int Y;
    }

    [ComImport]
    [Guid("352FFBA8-0973-437C-A61F-F64CAFD81DF9")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationCondition
    {
    }

    [ComImport]
    [Guid("14314595-B4BC-4055-95F2-58F2E42C9855")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElementArray
    {
        [PreserveSig] int get_Length(out int length);
        [PreserveSig] int GetElement(int index, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement element);
    }

    [ComImport]
    [Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement
    {
        [PreserveSig] int SetFocus();
        [PreserveSig] int GetRuntimeId(out IntPtr runtimeId);
        [PreserveSig] int FindFirst(int scope, IUIAutomationCondition condition, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement found);
        [PreserveSig] int FindAll(int scope, IUIAutomationCondition condition, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElementArray found);
        [PreserveSig] int FindFirstBuildCache(int scope, IUIAutomationCondition condition, IntPtr cacheRequest, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement found);
        [PreserveSig] int FindAllBuildCache(int scope, IUIAutomationCondition condition, IntPtr cacheRequest, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElementArray found);
        [PreserveSig] int BuildUpdatedCache(IntPtr cacheRequest, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement updatedElement);
        [PreserveSig] int GetCurrentPropertyValue(int propertyId, out object retVal);
        [PreserveSig] int GetCurrentPropertyValueEx(int propertyId, [MarshalAs(UnmanagedType.Bool)] bool ignoreDefaultValue, out object retVal);
        [PreserveSig] int GetCachedPropertyValue(int propertyId, out object retVal);
        [PreserveSig] int GetCachedPropertyValueEx(int propertyId, [MarshalAs(UnmanagedType.Bool)] bool ignoreDefaultValue, out object retVal);
        [PreserveSig] int GetCurrentPatternAs(int patternId, ref Guid riid, out IntPtr patternObject);
        [PreserveSig] int GetCachedPatternAs(int patternId, ref Guid riid, out IntPtr patternObject);
        [PreserveSig] int GetCurrentPattern(int patternId, [MarshalAs(UnmanagedType.IUnknown)] out object patternObject);
        [PreserveSig] int GetCachedPattern(int patternId, [MarshalAs(UnmanagedType.IUnknown)] out object patternObject);
        [PreserveSig] int GetCachedParent([MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement parent);
        [PreserveSig] int GetCachedChildren([MarshalAs(UnmanagedType.Interface)] out IUIAutomationElementArray children);
        [PreserveSig] int get_CurrentProcessId(out int retVal);
        [PreserveSig] int get_CurrentControlType(out int retVal);
        [PreserveSig] int get_CurrentLocalizedControlType([MarshalAs(UnmanagedType.BStr)] out string retVal);
        [PreserveSig] int get_CurrentName([MarshalAs(UnmanagedType.BStr)] out string retVal);
        [PreserveSig] int get_CurrentAcceleratorKey([MarshalAs(UnmanagedType.BStr)] out string retVal);
        [PreserveSig] int get_CurrentAccessKey([MarshalAs(UnmanagedType.BStr)] out string retVal);
        [PreserveSig] int get_CurrentHasKeyboardFocus([MarshalAs(UnmanagedType.Bool)] out bool retVal);
        [PreserveSig] int get_CurrentIsKeyboardFocusable([MarshalAs(UnmanagedType.Bool)] out bool retVal);
        [PreserveSig] int get_CurrentIsEnabled([MarshalAs(UnmanagedType.Bool)] out bool retVal);
        [PreserveSig] int get_CurrentAutomationId([MarshalAs(UnmanagedType.BStr)] out string retVal);
        [PreserveSig] int get_CurrentClassName([MarshalAs(UnmanagedType.BStr)] out string retVal);
        [PreserveSig] int get_CurrentHelpText([MarshalAs(UnmanagedType.BStr)] out string retVal);
        [PreserveSig] int get_CurrentCulture(out int retVal);
        [PreserveSig] int get_CurrentIsControlElement([MarshalAs(UnmanagedType.Bool)] out bool retVal);
        [PreserveSig] int get_CurrentIsContentElement([MarshalAs(UnmanagedType.Bool)] out bool retVal);
        [PreserveSig] int get_CurrentIsPassword([MarshalAs(UnmanagedType.Bool)] out bool retVal);
        [PreserveSig] int get_CurrentNativeWindowHandle(out IntPtr retVal);
        [PreserveSig] int get_CurrentItemType([MarshalAs(UnmanagedType.BStr)] out string retVal);
        [PreserveSig] int get_CurrentIsOffscreen([MarshalAs(UnmanagedType.Bool)] out bool retVal);
        [PreserveSig] int get_CurrentOrientation(out int retVal);
        [PreserveSig] int get_CurrentFrameworkId([MarshalAs(UnmanagedType.BStr)] out string retVal);
        [PreserveSig] int get_CurrentIsRequiredForForm([MarshalAs(UnmanagedType.Bool)] out bool retVal);
        [PreserveSig] int get_CurrentItemStatus([MarshalAs(UnmanagedType.BStr)] out string retVal);
        [PreserveSig] int get_CurrentBoundingRectangle(out UiaRect retVal);
    }

    [ComImport]
    [Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        [PreserveSig] int CompareElements(IUIAutomationElement el1, IUIAutomationElement el2, [MarshalAs(UnmanagedType.Bool)] out bool areSame);
        [PreserveSig] int CompareRuntimeIds(IntPtr runtimeId1, IntPtr runtimeId2, [MarshalAs(UnmanagedType.Bool)] out bool areSame);
        [PreserveSig] int GetRootElement([MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement root);
        [PreserveSig] int ElementFromHandle(IntPtr hwnd, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement element);
        [PreserveSig] int ElementFromPoint(UiaPoint pt, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement element);
        [PreserveSig] int GetFocusedElement([MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement element);
        [PreserveSig] int GetRootElementBuildCache(IntPtr cacheRequest, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement root);
        [PreserveSig] int ElementFromHandleBuildCache(IntPtr hwnd, IntPtr cacheRequest, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement element);
        [PreserveSig] int ElementFromPointBuildCache(UiaPoint pt, IntPtr cacheRequest, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement element);
        [PreserveSig] int GetFocusedElementBuildCache(IntPtr cacheRequest, [MarshalAs(UnmanagedType.Interface)] out IUIAutomationElement element);
        [PreserveSig] int CreateTreeWalker(IUIAutomationCondition pCondition, out IntPtr walker);
        [PreserveSig] int get_ControlViewWalker(out IntPtr walker);
        [PreserveSig] int get_ContentViewWalker(out IntPtr walker);
        [PreserveSig] int get_RawViewWalker(out IntPtr walker);
        [PreserveSig] int get_RawViewCondition([MarshalAs(UnmanagedType.Interface)] out IUIAutomationCondition condition);
        [PreserveSig] int get_ControlViewCondition([MarshalAs(UnmanagedType.Interface)] out IUIAutomationCondition condition);
        [PreserveSig] int get_ContentViewCondition([MarshalAs(UnmanagedType.Interface)] out IUIAutomationCondition condition);
        [PreserveSig] int CreateCacheRequest(out IntPtr cacheRequest);
        [PreserveSig] int CreateTrueCondition([MarshalAs(UnmanagedType.Interface)] out IUIAutomationCondition newCondition);
    }
}
