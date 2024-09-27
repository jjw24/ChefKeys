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


        private record KeyPressActionRecord() 
        {
            internal int vk_code { get; set; }
            internal Func<IntPtr, int, bool> HandleKeyPress { get; set; }
        };

        static ChefKeysManager()
        {
            registeredHotkeys = new Dictionary<int, KeyPressActionRecord>()
            {
                { VK_LWIN, new KeyPressActionRecord {vk_code = VK_LWIN, HandleKeyPress = HandleWinKeyPress } },
                { VK_LALT, new KeyPressActionRecord{ vk_code = VK_LALT, HandleKeyPress = HandleKeyPress } }
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
        private static bool otherKeyCancel = false;
        private static bool registeredKeyDown = false;

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && !_isSimulatingKeyPress)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                var record = new KeyPressActionRecord();

                if (!registeredHotkeys.TryGetValue(vkCode, out KeyPressActionRecord keyRecord_non) && !isLWinKeyDown)
                {
                    record.HandleKeyPress = HandleNonRegisteredKeys; 
                }
                else if (isLWinKeyDown)
                {
                    registeredHotkeys.TryGetValue(VK_LWIN, out KeyPressActionRecord keyRecord);
                    record = keyRecord;
                }
                else
                {
                    registeredHotkeys.TryGetValue(vkCode, out KeyPressActionRecord keyRecord);
                    record = keyRecord;
                }


                var blockKeyPress = record.HandleKeyPress(wParam, vkCode);

                if (blockKeyPress)
                    return (IntPtr)1;
            }
            
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private static bool HandleKeyPress(IntPtr wParam, int vkCode)
        {
            if ((wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                if (isOtherKeyDown)
                    otherKeyCancel = true;
                
                registeredKeyDown = true;
            }

            if ((wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP))//< ---here removed !isOtherKeyDown
            {
                if (!otherKeyCancel)
                {
                    KeyRemapped?.Invoke("");
                }
                
                otherKeyCancel = false;
                registeredKeyDown = false;
            }

            return false;
        }

        private static bool HandleNonRegisteredKeys(IntPtr wParam, int vkCode)
        {
            if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
                isOtherKeyDown = true;

            if ((wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP))
                isOtherKeyDown = false;

            if (registeredKeyDown)
                otherKeyCancel = true;

            return false;
        }

        private static bool HandleWinKeyPress(IntPtr wParam, int vkCode)
        {
            var keyRegistered = registeredHotkeys.TryGetValue(vkCode, out KeyPressActionRecord keyRecord);

            var blockKeyPress = false;

            if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
            {
                if (keyRegistered)
                {
                    isLWinKeyDown = true;
                    
                    blockKeyPress = true;
                    
                    return blockKeyPress;
                }
                else if (!keyRegistered && isLWinKeyDown)
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
                if (keyRegistered)
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
