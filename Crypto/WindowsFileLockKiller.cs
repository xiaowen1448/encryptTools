using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace EncryptTools
{
    [SupportedOSPlatform("windows")]
    internal static class WindowsFileLockKiller
    {
        // Restart Manager API
        private const int RmRebootReasonNone = 0;
        private const int CchRmSessionKey = 32;
        private const int ErrorMoreData = 234;
        private const int RmMaxAppName = 255;
        private const int RmMaxSvcName = 63;

        [StructLayout(LayoutKind.Sequential)]
        private struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public FILETIME ProcessStartTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = RmMaxAppName + 1)]
            public string strAppName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = RmMaxSvcName + 1)]
            public string strServiceShortName;

            public uint ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;

            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(
            uint pSessionHandle,
            uint nFiles,
            string[] rgsFilenames,
            uint nApplications,
            [In] RM_UNIQUE_PROCESS[]? rgApplications,
            uint nServices,
            string[]? rgsServiceNames);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmGetList(
            uint dwSessionHandle,
            out uint pnProcInfoNeeded,
            ref uint pnProcInfo,
            [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
            ref uint lpdwRebootReasons);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmEndSession(uint pSessionHandle);

        public static bool TryKillLockingProcesses(string filePath, Action<string>? log, out List<int> killedPids)
        {
            killedPids = new List<int>();
            if (string.IsNullOrWhiteSpace(filePath)) return false;

            try
            {
                var pids = GetLockingProcessIds(filePath);
                if (pids.Count == 0) return false;

                foreach (var pid in pids)
                {
                    try
                    {
                        using var p = Process.GetProcessById(pid);
                        var name = "";
                        try { name = p.ProcessName; } catch { }
                        log?.Invoke($"源文件被占用，强制结束进程 PID={pid}{(string.IsNullOrEmpty(name) ? "" : " (" + name + ")")} …");
#if NET46 || NET48 || NET461
                        p.Kill();
#else
                        p.Kill(entireProcessTree: true);
#endif
                        p.WaitForExit(2000);
                        killedPids.Add(pid);
                    }
                    catch (Exception ex)
                    {
                        log?.Invoke($"结束进程失败 PID={pid}: {ex.Message}");
                    }
                }

                return killedPids.Count > 0;
            }
            catch (Exception ex)
            {
                log?.Invoke("检测/结束占用进程失败: " + ex.Message);
                return false;
            }
        }

        private static List<int> GetLockingProcessIds(string path)
        {
            var result = new List<int>();
            uint handle;
            string key = Guid.NewGuid().ToString("N").Substring(0, CchRmSessionKey);
            int rc = RmStartSession(out handle, 0, key);
            if (rc != 0) throw new Win32Exception(rc);

            try
            {
                string[] resources = new[] { path };
                rc = RmRegisterResources(handle, (uint)resources.Length, resources, 0, null, 0, null);
                if (rc != 0) throw new Win32Exception(rc);

                uint needed = 0;
                uint procInfo = 0;
                uint rebootReasons = RmRebootReasonNone;
                rc = RmGetList(handle, out needed, ref procInfo, null, ref rebootReasons);
                if (rc == ErrorMoreData)
                {
                    var infos = new RM_PROCESS_INFO[needed];
                    procInfo = needed;
                    rc = RmGetList(handle, out needed, ref procInfo, infos, ref rebootReasons);
                    if (rc != 0) throw new Win32Exception(rc);

                    for (int i = 0; i < procInfo; i++)
                    {
                        int pid = infos[i].Process.dwProcessId;
                        if (pid > 0 && !result.Contains(pid)) result.Add(pid);
                    }
                }
                else if (rc != 0)
                {
                    // 0 means none, other means error
                    throw new Win32Exception(rc);
                }
            }
            finally
            {
                RmEndSession(handle);
            }

            return result;
        }
    }
}

