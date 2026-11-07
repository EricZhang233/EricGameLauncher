using System.Runtime.InteropServices;
using System.Text;

namespace EricGameLauncher;

internal static class NativeJumpList
{
    internal sealed record Task(string Title, string ApplicationPath, string Arguments, string WorkingDirectory, string IconResourcePath, int IconResourceIndex);

    internal sealed class Collection : IDisposable
    {
        private object? _native;
        internal IObjectArray Array { get; }
        internal IReadOnlyList<Task> Tasks { get; }

        internal Collection(object native, IReadOnlyList<Task> tasks)
        {
            _native = native;
            Array = (IObjectArray)native;
            Tasks = tasks;
        }

        public void Dispose()
        {
            object? native = Interlocked.Exchange(ref _native, null);
            if (native != null) Marshal.ReleaseComObject(native);
        }
    }

    internal static readonly PropertyKey TitleKey = new(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);
    internal static readonly PropertyKey SeparatorKey = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 6);

    internal static IReadOnlyList<Task> Publish(string appId, IEnumerable<Task?> tasks)
    {
        using var collection = CreateCollection(tasks);
        object? destination = null;
        object? removed = null;
        bool started = false;
        try
        {
            destination = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("77F10CF0-3DB5-4966-B520-B7C54FD35ED6"))!)!;
            var list = (ICustomDestinationList)destination;
            list.SetAppID(appId);
            Guid arrayId = typeof(IObjectArray).GUID;
            list.BeginList(out _, ref arrayId, out removed);
            started = true;
            collection.Array.GetCount(out uint count);
            if (count > 0) list.AddUserTasks(collection.Array);
            list.CommitList();
            started = false;
            return collection.Tasks;
        }
        catch
        {
            if (started)
            {
                try { ((ICustomDestinationList)destination!).AbortList(); }
                catch (Exception ex) { LogService.Write("JumpList", "Native publication abort failed", ex); }
            }
            throw;
        }
        finally
        {
            if (removed != null) Marshal.ReleaseComObject(removed);
            if (destination != null) Marshal.ReleaseComObject(destination);
        }
    }

    internal static Collection CreateCollection(IEnumerable<Task?> tasks)
    {
        object native = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("2D3468C1-36A7-43B6-AC24-D3F02FD9607A"))!)!;
        try
        {
            var collection = (IObjectCollection)native;
            var accepted = new List<Task>();
            bool separator = false;
            foreach (var task in tasks)
            {
                if (task == null) { separator = accepted.Count > 0; continue; }
                object link;
                try { link = CreateLink(task); }
                catch (Exception ex) when (ex is COMException or ArgumentException)
                {
                    LogService.Write("JumpList", "Native task rejected", ex);
                    continue;
                }
                try
                {
                    if (separator)
                    {
                        object boundary = CreateLink(null);
                        try { collection.AddObject(boundary); }
                        finally { Marshal.ReleaseComObject(boundary); }
                    }
                    collection.AddObject(link);
                    accepted.Add(task);
                    separator = false;
                }
                finally { Marshal.ReleaseComObject(link); }
            }
            return new Collection(native, accepted);
        }
        catch { Marshal.ReleaseComObject(native); throw; }
    }

    private static object CreateLink(Task? task)
    {
        if (task != null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(task.ApplicationPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(task.Title);
            ArgumentException.ThrowIfNullOrWhiteSpace(task.Arguments);
        }
        object native = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))!)!;
        try
        {
            if (task == null)
            {
                var key = SeparatorKey;
                var value = new PropertyValue { Type = 11, Boolean = -1 };
                ((IPropertyStore)native).SetValue(ref key, ref value);
                ((IPropertyStore)native).Commit();
            }
            else
            {
                var link = (IShellLink)native;
                link.SetPath(task.ApplicationPath);
                link.SetArguments(task.Arguments);
                link.SetWorkingDirectory(task.WorkingDirectory);
                link.SetIconLocation(task.IconResourcePath, task.IconResourceIndex);
                link.SetShowCmd(1);
                SetStringProperty(native, TitleKey, task.Title);
            }
            return native;
        }
        catch { Marshal.ReleaseComObject(native); throw; }
    }

    internal static void SetAppId(object link, string appId)
        => SetStringProperty(link, new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5), appId);

    private static void SetStringProperty(object link, PropertyKey key, string text)
    {
        IntPtr pointer = Marshal.StringToCoTaskMemUni(text);
        try
        {
            var value = new PropertyValue { Type = 31, Pointer = pointer };
            ((IPropertyStore)link).SetValue(ref key, ref value);
            ((IPropertyStore)link).Commit();
        }
        finally { Marshal.FreeCoTaskMem(pointer); }
    }

    [DllImport("ole32.dll")]
    internal static extern int PropVariantClear(ref PropertyValue value);

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyKey
    {
        public Guid Format;
        public uint Id;
        internal PropertyKey(Guid format, uint id) { Format = format; Id = id; }
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct PropertyValue
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
        [FieldOffset(8)] public short Boolean;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropertyValue value);
        void SetValue(ref PropertyKey key, ref PropertyValue value);
        void Commit();
    }

    [ComImport, Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IObjectArray
    {
        void GetCount(out uint count);
        void GetAt(uint index, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object value);
    }

    [ComImport, Guid("5632B1A4-E38A-400A-928A-D4CD63230295"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectCollection
    {
        void GetCount(out uint count);
        void GetAt(uint index, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object value);
        void AddObject([MarshalAs(UnmanagedType.IUnknown)] object value);
        void AddFromArray(IObjectArray values);
        void RemoveObjectAt(uint index);
        void Clear();
    }

    [ComImport, Guid("6332DEBF-87B5-4670-90C0-5E57B408A49E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICustomDestinationList
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);
        void BeginList(out uint slots, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object removed);
        void AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string name, IObjectArray values);
        void AppendKnownCategory(uint category);
        void AddUserTasks(IObjectArray values);
        void CommitList();
        void GetRemovedDestinations(ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object removed);
        void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string appId);
        void AbortList();
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellLink
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int capacity, IntPtr data, uint flags);
        void GetIDList(out IntPtr id);
        void SetIDList(IntPtr id);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int capacity);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int capacity);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string path);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int capacity);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int capacity, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
