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
        private const int VK_RWIN = 0x5C;
        private const int VK_LALT = 0xA4;
        private const int VK_LCTRL = 0xA2;
        private const int VK_LSHIFT = 0xA0;
        private const int VK_SHIFT = 0x10;
        private const int VK_Z = 0x5A;
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

        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
        private static extern short GetKeyState(int keyCode);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        public static event Action<string> KeyRemapped;

        private record KeyPressActionRecord()
        {
            internal int vk_code { get; set; }

            internal int vkCodeCombo0 { get; set; } = 0;

            internal int vkCodeCombo1 { get; set; } = 0;

            internal int vkCodeCombo2 { get; set; } = 0;

            internal Func<IntPtr, int, int, int, int, bool> HandleKeyPress { get; set; }
        };

        private static readonly Dictionary<int, KeyPressActionRecord> registeredHotkeys;

        private static readonly KeyPressActionRecord nonRegisteredKeyRecord; 

        static ChefKeysManager()
        {
            registeredHotkeys = new Dictionary<int, KeyPressActionRecord>()
            {
                //{ VK_LCTRL, new KeyPressActionRecord {vk_code = VK_LCTRL, HandleKeyPress = HandleRegisteredSingleKeyPress } },
                { VK_LWIN, new KeyPressActionRecord {vk_code = VK_LWIN, HandleKeyPress = HandleRegisteredSingleKeyPress } },
                { VK_Z, new KeyPressActionRecord{ vk_code = VK_Z, HandleKeyPress = HandleRegisteredComboKeyPress, vkCodeCombo0 = VK_LSHIFT, vkCodeCombo1 = VK_LALT, vkCodeCombo2 = VK_LCTRL } },
                { VK_LCTRL, new KeyPressActionRecord{ vk_code = VK_LCTRL, HandleKeyPress = HandleRegisteredComboKeyPress, vkCodeCombo0 = VK_LALT, vkCodeCombo1 = VK_LSHIFT, vkCodeCombo2 = VK_Z } },
                { VK_LALT, new KeyPressActionRecord{ vk_code = VK_LALT, HandleKeyPress = HandleRegisteredComboKeyPress, vkCodeCombo0 = VK_LCTRL, vkCodeCombo1 = VK_LSHIFT, vkCodeCombo2 = VK_Z } },
                { VK_LSHIFT, new KeyPressActionRecord{ vk_code = VK_LSHIFT, HandleKeyPress = HandleRegisteredComboKeyPress, vkCodeCombo0 = VK_LALT, vkCodeCombo1 = VK_LCTRL, vkCodeCombo2 = VK_Z } }
            };
            
            nonRegisteredKeyRecord = new KeyPressActionRecord { vk_code = 0, HandleKeyPress = HandleNonRegisteredKeyPress };

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
        private static bool nonRegisteredKeyDown = false;
        private static bool cancel = false;
        private static bool cancelAction = false;
        private static bool registeredSingleKeyDown = false;
        private static bool registeredComboKeyDown = false;

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && !_isSimulatingKeyPress)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                var keyRecord = !registeredHotkeys.TryGetValue(vkCode, out KeyPressActionRecord registeredKeyRecord)
                                ? nonRegisteredKeyRecord
                                : registeredKeyRecord;

                var blockKeyPress = keyRecord.HandleKeyPress(wParam, vkCode, keyRecord.vkCodeCombo0, keyRecord.vkCodeCombo1, keyRecord.vkCodeCombo2);

                if (blockKeyPress)
                    return (IntPtr)1;
            }
            
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private static bool HandleRegisteredSingleKeyPress(IntPtr wParam, int vkCode, int vkCodeCombo0 = 0, int vkCodeCombo1 = 0, int vkCodeCombo2 = 0)
        {
            if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
            {
                if (nonRegisteredKeyDown || registeredComboKeyDown)
                    cancelAction = true;

                if (vkCode == VK_LWIN || vkCode == VK_RWIN)
                    isLWinKeyDown = true;
            }

            if (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP)
            {
                if (vkCode == VK_LWIN || vkCode == VK_RWIN)
                {
                    if (!cancelAction && isLWinKeyDown)
                    {
                        SendAltKeyDown();
                        SendLWinKeyUp();
                        isLWinKeyDown = false;
                        SendAltKeyUp();

                        KeyRemapped?.Invoke("LWin key remapped");

                        return true;
                    }

                    isLWinKeyDown = false;
                    cancelAction = false;

                    return false;
                }

                if (!cancelAction)
                    KeyRemapped?.Invoke("");

                cancelAction = false;
            }

            return false;
        }

        private static bool HandleRegisteredComboKeyPress(IntPtr wParam, int vkCode, int vkCodeCombo0 = 0, int vkCodeCombo1 = 0, int vkCodeCombo2 = 0)
        {
            if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
            {
                if (nonRegisteredKeyDown)
                    cancelAction = true;

                registeredComboKeyDown = true;
            }

            if (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP)
            {
                registeredComboKeyDown = false;

                if (cancelAction)
                {
                    cancelAction = false;
                    return false;
                }

                if (vkCodeCombo0 > 0)
                {
                    var triggerCombo = true;

                    if ((GetAsyncKeyState(vkCodeCombo0) & 0x8000) == 0)
                        triggerCombo = false;

                    if (vkCodeCombo1 > 0 && ((GetAsyncKeyState(vkCodeCombo1) & 0x8000) == 0))
                        triggerCombo = false;

                    if (vkCodeCombo2 > 0 && ((GetAsyncKeyState(vkCodeCombo2) & 0x8000) == 0))
                        triggerCombo = false;

                    if (triggerCombo)
                        KeyRemapped?.Invoke("");
                }
            }

            return false;
        }

        private static bool HandleNonRegisteredKeyPress(IntPtr wParam, int vkCode, int vkCodeCombo0 = 0, int vkCodeCombo1 = 0, int vkCodeCombo2 = 0)
        {
            if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
                nonRegisteredKeyDown = true;

            if (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP)
                nonRegisteredKeyDown = false;

            return false;
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
