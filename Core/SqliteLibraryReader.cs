using System.Runtime.InteropServices;

namespace EricGameLauncher;

internal static class SqliteLibraryReader
{
    internal static Dictionary<string, byte[]> ReadBlobs(string path, string query, int maxRows, int maxBlobBytes = 64 * 1024 * 1024)
    {
        IntPtr db = IntPtr.Zero, statement = IntPtr.Zero;
        try
        {
            if (sqlite3_open_v2(path, out db, 1, IntPtr.Zero) != 0) throw new IOException();
            if (sqlite3_busy_timeout(db, 2000) != 0 || sqlite3_prepare_v2(db, query, -1, out statement, IntPtr.Zero) != 0) throw new InvalidDataException();
            var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            int status;
            while ((status = sqlite3_step(statement)) == 100)
            {
                string? key = Marshal.PtrToStringUTF8(sqlite3_column_text(statement, 0));
                int length = sqlite3_column_bytes(statement, 1);
                IntPtr blob = sqlite3_column_blob(statement, 1);
                if (result.Count >= maxRows || string.IsNullOrEmpty(key) || length <= 0 || length > maxBlobBytes || blob == IntPtr.Zero) throw new InvalidDataException();
                var data = new byte[length];
                Marshal.Copy(blob, data, 0, length);
                if (!result.TryAdd(key, data)) throw new InvalidDataException();
            }
            if (status != 101) throw new IOException();
            return result;
        }
        finally
        {
            if (statement != IntPtr.Zero) sqlite3_finalize(statement);
            if (db != IntPtr.Zero) sqlite3_close(db);
        }
    }

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_prepare_v2(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int length, out IntPtr statement, IntPtr tail);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_bytes(IntPtr statement, int column);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_blob(IntPtr statement, int column);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_busy_timeout(IntPtr db, int milliseconds);
}
