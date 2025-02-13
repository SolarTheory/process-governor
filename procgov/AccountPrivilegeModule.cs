﻿using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using static LowLevelDesign.Win32Commons;

namespace LowLevelDesign
{
    internal record class AccountPrivilege(string PrivilegeName, string Result, TOKEN_PRIVILEGES ReplacedPrivilege);

    internal unsafe static class AccountPrivilegeModule
    {
        private static readonly TraceSource logger = Program.Logger;

        public static bool IsCurrentUserAdministrator()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        internal static List<AccountPrivilege> EnablePrivileges(SafeHandle processHandle, string[] privilegeNames)
        {
            CheckWin32Result(PInvoke.OpenProcessToken(processHandle, TOKEN_ACCESS_MASK.TOKEN_QUERY | TOKEN_ACCESS_MASK.TOKEN_ADJUST_PRIVILEGES,
                out var tokenHandle));
            try
            {
                return privilegeNames.Select(privilegeName =>
                {
                    CheckWin32Result(PInvoke.LookupPrivilegeValue(null, privilegeName, out var luid));

                    var privileges = new TOKEN_PRIVILEGES
                    {
                        PrivilegeCount = 1,
                        Privileges = new() { _0 = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_ENABLED } }
                    };
                    var previousPrivileges = new TOKEN_PRIVILEGES();
                    uint length = 0;
                    if (PInvoke.AdjustTokenPrivileges(tokenHandle, false, privileges, (uint)Marshal.SizeOf(previousPrivileges), &previousPrivileges, &length))
                    {
                        Debug.Assert(length == Marshal.SizeOf(previousPrivileges));
                        var result = Marshal.GetLastWin32Error() == (int)WIN32_ERROR.NO_ERROR ? "ENABLED" : "FAILED (privilege not available)";
                        return new AccountPrivilege(privilegeName, result, previousPrivileges);
                    }
                    else
                    {
                        var result = $"FAILED (error 0x{Marshal.GetLastWin32Error():x}";
                        return new AccountPrivilege(privilegeName, result, new TOKEN_PRIVILEGES { PrivilegeCount = 0 });
                    }
                }).ToList();
            }
            finally
            {
                tokenHandle.Dispose();
            }
        }

        internal static void RestorePrivileges(SafeHandle processHandle, List<AccountPrivilege> privileges)
        {
            CheckWin32Result(PInvoke.OpenProcessToken(processHandle, TOKEN_ACCESS_MASK.TOKEN_ADJUST_PRIVILEGES, out var tokenHandle));
            try
            {
                foreach (var priv in privileges.Where(priv => priv.Result == "ENABLED"))
                {
                    if (!PInvoke.AdjustTokenPrivileges(tokenHandle, false, priv.ReplacedPrivilege, 0, null, null))
                    {
                        logger.TraceEvent(TraceEventType.Error, 0, "Error while reverting the {0} privilege: 0x{1:x}",
                            priv.PrivilegeName, Marshal.GetLastWin32Error());
                    }
                }
            }
            finally
            {
                tokenHandle.Dispose();
            }
        }

    }
}
