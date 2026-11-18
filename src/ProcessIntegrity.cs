using System;
using System.Runtime.InteropServices;

namespace Explorer2Dialog
{
    internal static class ProcessIntegrity
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentProcessId();
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr token, int kind, IntPtr buffer, int size, out int required);
        [DllImport("advapi32.dll")]
        private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
        [DllImport("advapi32.dll")]
        private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);

        public static int? Read(uint pid)
        {
            var process = OpenProcess(0x1000, false, pid); // QUERY_LIMITED_INFORMATION
            if (process == IntPtr.Zero) return null;
            try
            {
                IntPtr token;
                if (!OpenProcessToken(process, 0x8, out token)) return null; // TOKEN_QUERY
                try
                {
                    const int integrity = 25; // TokenIntegrityLevel
                    int size;
                    GetTokenInformation(token, integrity, IntPtr.Zero, 0, out size);
                    if (size <= 0) return null;
                    var buffer = Marshal.AllocHGlobal(size);
                    try
                    {
                        if (!GetTokenInformation(token, integrity, buffer, size, out size)) return null;
                        var sid = Marshal.ReadIntPtr(buffer); // TOKEN_MANDATORY_LABEL.Label.Sid
                        if (sid == IntPtr.Zero) return null;
                        var count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                        return count == 0 ? (int?)null : Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
                finally { CloseHandle(token); }
            }
            finally { CloseHandle(process); }
        }
    }
}
