using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace ChefKeys
{
    public static class ChefKeysManager
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int VK_LWIN = 0x5B;
        private const int VK_LALT = 0xA4;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private static LowLevelKeyboardProc _proc;
        private static IntPtr _hookID = IntPtr.Zero;
        private static bool _isSimulatingKeyPress = false;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        public static event Action<string> KeyRemapped;

        private static readonly Dictionary<int, KeyPressActionRecord> registeredHotkeys;


        private record KeyPressActionRecord(int vk_code, Func<IntPtr, int, bool> HandleKeyPress);

        static ChefKeysManager()
        {
            registeredHotkeys = new Dictionary<int, KeyPressActionRecord>() 
            {
                { VK_LWIN, new KeyPressActionRecord(VK_LWIN, HandleWinKeyPress) }
            };

            _proc = HookCallback;
        }

        public static void Start()
        {
            _hookID = SetHook(_proc);
        }

        public static void Stop()
        {
            UnhookWindowsHookEx(_hookID);
        }

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private static bool isLWinKeyDown = false;
        private static bool isOtherKeyDown = false;
        private static bool cancel = false;
        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && !_isSimulatingKeyPress)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                var HandleKeyPressFunc = registeredHotkeys.GetValueOrDefault(VK_LWIN).HandleKeyPress;

                var blockKeyPress = HandleKeyPressFunc(wParam, vkCode);

                if (blockKeyPress)
                    return (IntPtr)1;
            }
            
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private static bool HandleKeyPress(IntPtr wParam, int vkCode)
        {
            return false;
        }

        private static bool HandleWinKeyPress(IntPtr wParam, int vkCode)
        {
            var blockKeyPress = false;

            if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
            {
                if (vkCode == VK_LWIN)
                {
                    isLWinKeyDown = true;
                    
                    blockKeyPress = true;
                    
                    return blockKeyPress;
                }
                else if (vkCode != VK_LWIN && isLWinKeyDown)
                {
                    if (!cancel)
                    {
                        SendLWinKeyDown();
                        cancel = true;
                    }
                }
            }
            else if (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP)
            {
                if (vkCode == VK_LWIN)
                {
                    if (!cancel)
                    {
                        SendLWinKeyDown();
                        SendAltKeyDown();
                        SendLWinKeyUp();
                        isLWinKeyDown = false;
                        SendAltKeyUp();

                        KeyRemapped?.Invoke("LWin key remapped");

                        blockKeyPress = true;

                        return blockKeyPress;
                    }
                    else
                    {
                        isLWinKeyDown = false;
                        cancel = false;
                    }

                }
            }

            return blockKeyPress;
        }

        private static void SendLWinKeyDown()
        {
            _isSimulatingKeyPress = true;
            keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
            _isSimulatingKeyPress = false;
        }

        private static void SendAltKeyDown()
        {
            _isSimulatingKeyPress = true;
            keybd_event(VK_LALT, 0, 0, UIntPtr.Zero);
            _isSimulatingKeyPress = false;
        }

        private static void SendLWinKeyUp()
        {
            _isSimulatingKeyPress = true;
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            _isSimulatingKeyPress = false;
        }

        private static void SendAltKeyUp()
        {
            _isSimulatingKeyPress = true;
            keybd_event(VK_LALT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            _isSimulatingKeyPress = false;
        }
    }
}
