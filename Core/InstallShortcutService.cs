using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace EricGameLauncher.Installation;

public static class ShortcutService
{
    public static void Install(string executable, IReadOnlyList<string> destinations, string description, Action<string>? checkpoint = null)
    {
        executable = Path.GetFullPath(executable);
        if (!File.Exists(executable)) throw new FileNotFoundException(executable);
        var staged = new Dictionary<string, string>();
        var backups = new Dictionary<string, string>();
        var installed = new List<string>();
        bool recovered = false;
        try
        {
            foreach (string target in destinations)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
                string temporary = target + "." + Guid.NewGuid().ToString("N") + ".lnk";
                staged.Add(target, temporary);
                checkpoint?.Invoke("create:" + target);
                Create(executable, temporary, description);
                if (File.Exists(target))
                {
                    string backup = temporary + ".bak";
                    File.Copy(target, backup);
                    backups.Add(target, backup);
                }
            }
            foreach (var entry in staged)
            {
                checkpoint?.Invoke("install:" + entry.Key);
                File.Move(entry.Value, entry.Key, true);
                installed.Add(entry.Key);
            }
            recovered = true;
        }
        catch
        {
            foreach (string target in installed.AsEnumerable().Reverse())
            {
                if (backups.TryGetValue(target, out string? backup)) File.Copy(backup, target, true);
                else File.Delete(target);
            }
            recovered = true;
            throw;
        }
        finally
        {
            foreach (string temporary in staged.Values) if (File.Exists(temporary)) File.Delete(temporary);
            if (recovered) foreach (string backup in backups.Values) if (File.Exists(backup)) File.Delete(backup);
        }
    }

    public static void Create(string executable, string destination, string description)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            IntPtr link = IntPtr.Zero, file = IntPtr.Zero, store = IntPtr.Zero;
            int initialized = CoInitializeEx(IntPtr.Zero, 2);
            try
            {
                Marshal.ThrowExceptionForHR(initialized);
                Guid clsid = new("00021401-0000-0000-C000-000000000046"), iid = new("000214F9-0000-0000-C000-000000000046");
                Marshal.ThrowExceptionForHR(CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out link));
                Marshal.ThrowExceptionForHR(Method<SetString>(link, 20)(link, executable));
                Marshal.ThrowExceptionForHR(Method<SetString>(link, 9)(link, Path.GetDirectoryName(executable)!));
                Marshal.ThrowExceptionForHR(Method<SetString>(link, 7)(link, description));
                if (Path.GetFileName(executable).Equals("EricGameLauncher.exe", StringComparison.OrdinalIgnoreCase))
                {
                    Guid propertyStore = new("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99");
                    Marshal.ThrowExceptionForHR(Method<QueryInterface>(link, 0)(link, ref propertyStore, out store));
                    var property = new PropertyKey { Format = new Guid("9f4c2855-9f79-4b39-a8d0-e1d42de1d5f3"), Id = 5 };
                    IntPtr value = Marshal.AllocCoTaskMem(24), identifier = Marshal.StringToCoTaskMemUni("EricSoft.EricGameLauncher");
                    try
                    {
                        for (int i = 0; i < 24; i++) Marshal.WriteByte(value, i, 0);
                        Marshal.WriteInt16(value, 31);
                        Marshal.WriteIntPtr(value, 8, identifier);
                        Marshal.ThrowExceptionForHR(Method<SetValue>(store, 6)(store, ref property, value));
                        Marshal.ThrowExceptionForHR(Method<Commit>(store, 7)(store));
                    }
                    finally { Marshal.FreeCoTaskMem(identifier); Marshal.FreeCoTaskMem(value); }
                }
                Guid persist = new("0000010b-0000-0000-C000-000000000046");
                Marshal.ThrowExceptionForHR(Method<QueryInterface>(link, 0)(link, ref persist, out file));
                Marshal.ThrowExceptionForHR(Method<SaveFile>(file, 6)(file, Path.GetFullPath(destination), 1));
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                if (file != IntPtr.Zero) Method<Release>(file, 2)(file);
                if (store != IntPtr.Zero) Method<Release>(store, 2)(store);
                if (link != IntPtr.Zero) Method<Release>(link, 2)(link);
                if (initialized >= 0) CoUninitialize();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    public static bool Targets(string shortcut, string executable)
    {
        bool matches = false;
        var thread = new Thread(() =>
        {
            IntPtr link = IntPtr.Zero, file = IntPtr.Zero;
            int initialized = CoInitializeEx(IntPtr.Zero, 2);
            try
            {
                Marshal.ThrowExceptionForHR(initialized);
                Guid clsid = new("00021401-0000-0000-C000-000000000046"), iid = new("000214F9-0000-0000-C000-000000000046"), persist = new("0000010b-0000-0000-C000-000000000046");
                Marshal.ThrowExceptionForHR(CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out link));
                Marshal.ThrowExceptionForHR(Method<QueryInterface>(link, 0)(link, ref persist, out file));
                Marshal.ThrowExceptionForHR(Method<LoadFile>(file, 5)(file, shortcut, 0));
                var target = new System.Text.StringBuilder(32768);
                Marshal.ThrowExceptionForHR(Method<GetPath>(link, 3)(link, target, target.Capacity, IntPtr.Zero, 4));
                matches = Path.GetFullPath(target.ToString()).Equals(Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase);
            }
            catch { }
            finally
            {
                if (file != IntPtr.Zero) Method<Release>(file, 2)(file);
                if (link != IntPtr.Zero) Method<Release>(link, 2)(link);
                if (initialized >= 0) CoUninitialize();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return matches;
    }

    private static T Method<T>(IntPtr instance, int slot) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int SetString(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int SaveFile(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string value, int remember);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int LoadFile(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string value, uint mode);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int GetPath(IntPtr self, [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder value, int capacity, IntPtr data, uint flags);
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetValue(IntPtr self, ref PropertyKey key, IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Commit(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int QueryInterface(IntPtr self, ref Guid iid, out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint Release(IntPtr self);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr value);
}
