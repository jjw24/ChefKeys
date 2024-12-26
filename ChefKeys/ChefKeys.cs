using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        private const int VK_RALT = 0xA5;
        private const int VK_LCTRL = 0xA2;
        private const int VK_RCTRL = 0xA3;
        private const int VK_LSHIFT = 0xA0;
        private const int VK_RSHIFT = 0xA1;
        private const int VK_SHIFT = 0x10;
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

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        private record KeyPressActionRecord()
        {
            internal int vk_code { get; set; }

            internal List<KeyComboRecord> KeyComboRecords = new();

            internal bool isSingleKeyRegistered { get; set; } = false;

            internal Action<string> action;

            internal Func<IntPtr, int, KeyPressActionRecord, bool> HandleKeyPress { get; set; }

            internal bool AreKeyCombosRegistered() => KeyComboRecords.Count > 0;

            internal void RegisterKeyCombo(string hotkey, int vk_code, Action<string> action, int vkCodeCombo0, int vkCodeCombo1 = 0, int vkCodeCombo2 = 0)
            {
                if (KeyComboRecords.Any(x => x.comboRaw == hotkey))
                    return;

                KeyComboRecords
                    .Add(
                        new KeyComboRecord 
                        {
                            vk_code = vk_code,
                            vkCodeCombo0 = vkCodeCombo0,
                            vkCodeCombo1 = vkCodeCombo1, 
                            vkCodeCombo2 = vkCodeCombo2,
                            action = action,
                            comboRaw = hotkey
                        });
                
            }
        };

        private record KeyComboRecord()
        {
            internal int vk_code { get; set; }

            internal Action<string> action;

            internal int vkCodeCombo0 { get; set; } = 0;

            internal int vkCodeCombo1 { get; set; } = 0;

            internal int vkCodeCombo2 { get; set; } = 0;

            internal string comboRaw { get; set; } = string.Empty;

            internal bool AreComboKeysHeldDown()
            {
                var heldDown = true;

                // vk_code is the release key, which is already pressed, no need to check.
                var comboKeys = new Dictionary<int, string> { { vk_code, string.Empty } };

                if (vkCodeCombo0 > 0)
                {
                    comboKeys.Add(vkCodeCombo0, string.Empty);

                    if ((GetAsyncKeyState(vkCodeCombo0) & 0x8000) == 0)
                        heldDown = false;
                }

                if (vkCodeCombo1 > 0)
                {
                    comboKeys.Add(vkCodeCombo1, string.Empty);

                    if ((GetAsyncKeyState(vkCodeCombo1) & 0x8000) == 0)
                        heldDown = false;
                }

                if (vkCodeCombo2 > 0)
                {
                    comboKeys.Add(vkCodeCombo2, string.Empty);

                    if ((GetAsyncKeyState(vkCodeCombo2) & 0x8000) == 0)
                        heldDown= false;
                }

                if (NonRegisteredModifierKeyPressed(comboKeys))
                    heldDown = false;

                return heldDown;
            }

            private bool NonRegisteredModifierKeyPressed(Dictionary<int, string> comboKeys)
            {
                if (!comboKeys.ContainsKey(VK_LCTRL) && ((GetAsyncKeyState(VK_LCTRL) & 0x8000) != 0))
                    return true;

                if (!comboKeys.ContainsKey(VK_RCTRL) && ((GetAsyncKeyState(VK_RCTRL) & 0x8000) != 0))
                    return true;

                if (!comboKeys.ContainsKey(VK_LALT) && ((GetAsyncKeyState(VK_LALT) & 0x8000) != 0))
                    return true;

                if (!comboKeys.ContainsKey(VK_RALT) && ((GetAsyncKeyState(VK_RALT) & 0x8000) != 0))
                    return true;

                if (!comboKeys.ContainsKey(VK_LSHIFT) && ((GetAsyncKeyState(VK_LSHIFT) & 0x8000) != 0))
                    return true;

                if (!comboKeys.ContainsKey(VK_RSHIFT) && ((GetAsyncKeyState(VK_RSHIFT) & 0x8000) != 0))
                    return true;

                if (!comboKeys.ContainsKey(VK_LWIN) && ((GetAsyncKeyState(VK_LWIN) & 0x8000) != 0))
                    return true;

                if (!comboKeys.ContainsKey(VK_RWIN) && ((GetAsyncKeyState(VK_RWIN) & 0x8000) != 0))
                    return true;

                return false;
            }
        }

        private static readonly Dictionary<int, KeyPressActionRecord> registeredHotkeys;
        private static readonly KeyPressActionRecord nonRegisteredKeyRecord;
        
        private static bool isLWinKeyDown = false;
        private static bool nonRegisteredKeyDown = false;
        private static bool cancelAction = false;
        private static bool registeredKeyDown = false;
    
        static ChefKeysManager()
        {
            registeredHotkeys = new Dictionary<int, KeyPressActionRecord>();
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

        private static IEnumerable<string> SplitHotkeyReversed(string hotkeys) => hotkeys.Split("+", StringSplitOptions.RemoveEmptyEntries).Reverse();

        public static void RegisterHotkey(string hotkeys, Action<string> action) => RegisterHotkey(hotkeys, hotkeys, action);

        public static void RegisterHotkey(string hotkeys, string previousHotkey, Action<string> action)
        {
            hotkeys = ConvertIncorrectKeyString(hotkeys);
            previousHotkey = ConvertIncorrectKeyString(previousHotkey);

            UnregisterHotkey(hotkeys, previousHotkey, action);

            // The released key need to be the unique key in the dictionary.
            // The last key in the combo is the release key that triggers action
            var keys = SplitHotkeyReversed(hotkeys);

            var vkCodeCombo0 = keys.ElementAtOrDefault(1) is not null ? ToKeyCode(keys.ElementAtOrDefault(1)) : 0;
            var vkCodeCombo1 = keys.ElementAtOrDefault(2) is not null ? ToKeyCode(keys.ElementAtOrDefault(2)) : 0;
            var vkCodeCombo2 = keys.ElementAtOrDefault(3) is not null ? ToKeyCode(keys.ElementAtOrDefault(3)) : 0;

            var singleKey = vkCodeCombo0 + vkCodeCombo1 + vkCodeCombo2 == 0;
            var comboKeys = singleKey is false;
            var vk_code = ToKeyCode(keys.First());

            if (registeredHotkeys.TryGetValue(vk_code, out var existingKeyRecord))
            {
                if (singleKey && !existingKeyRecord.isSingleKeyRegistered)
                    existingKeyRecord.isSingleKeyRegistered = true;

                if (comboKeys)
                    existingKeyRecord.RegisterKeyCombo(hotkeys, vk_code, action, vkCodeCombo0, vkCodeCombo1, vkCodeCombo2);

                return;
            }

            var keyRecord = new KeyPressActionRecord
            {
                vk_code = ToKeyCode(keys.First()),
                HandleKeyPress = HandleRegisteredKeyPress,                
                isSingleKeyRegistered = singleKey,
                action = singleKey? action : null
            };

            if (comboKeys)
                keyRecord.RegisterKeyCombo(hotkeys, vk_code, action, vkCodeCombo0, vkCodeCombo1, vkCodeCombo2);

            registeredHotkeys.Add(ToKeyCode(keys.First()), keyRecord);
        }

        public static void UnregisterHotkey(string hotkey, string previousHotkey, Action<string> action)
        {
            var hotkeyToCheck = hotkey;

            if (!registeredHotkeys.TryGetValue(ToKeyCode(SplitHotkeyReversed(hotkeyToCheck).First()), out var existingKeyRecord))
            {
                hotkeyToCheck= previousHotkey;
                if (!registeredHotkeys.TryGetValue(ToKeyCode(SplitHotkeyReversed(hotkeyToCheck).First()), out var existingPrevKeyRecord))
                    return;

                existingKeyRecord = existingPrevKeyRecord;
            }

            if (existingKeyRecord.isSingleKeyRegistered && !existingKeyRecord.AreKeyCombosRegistered() && !hotkeyToCheck.Contains('+'))
            {
                existingKeyRecord.action -= existingKeyRecord.action;
                registeredHotkeys.Remove(existingKeyRecord.vk_code);
                return;
            }

            var comboRecord = existingKeyRecord.KeyComboRecords.FirstOrDefault(x => x.comboRaw == hotkeyToCheck);

            // There is a single key press still registered, no need to remove anything from registeredHotkeys.
            if (comboRecord is null)
                return;

            comboRecord.action -= comboRecord.action;
            existingKeyRecord.KeyComboRecords.RemoveAll(x => x.comboRaw == hotkeyToCheck);
            if (!existingKeyRecord.isSingleKeyRegistered && !existingKeyRecord.AreKeyCombosRegistered())
                registeredHotkeys.Remove(existingKeyRecord.vk_code);
        }

        private static int ToKeyCode(string key)
        {
            return KeyInterop.VirtualKeyFromKey((Key)Enum.Parse(typeof(Key), key));
        }

        private static string ConvertIncorrectKeyString(string hotkey)
        {
            var keys = hotkey.Split("+", StringSplitOptions.RemoveEmptyEntries);

            var newHotkey = string.Empty;
            foreach (var key in keys)
            {
                if (!string.IsNullOrEmpty(newHotkey))
                    newHotkey += "+";

                switch (key.ToLower())
                {
                    case "alt":
                        newHotkey += "LeftAlt";
                        break;
                    case "ctrl":
                        newHotkey += "LeftCtrl";
                        break;
                    case "shift":
                        newHotkey += "LeftShift";
                        break;
                    case "win":
                        newHotkey += "LWin";
                        break;
                    default:
                        newHotkey += key;
                        break;
                }
            }

            return newHotkey;
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && !_isSimulatingKeyPress)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                var keyRecord = !registeredHotkeys.TryGetValue(vkCode, out KeyPressActionRecord registeredKeyRecord)
                                ? nonRegisteredKeyRecord
                                : registeredKeyRecord;

                var blockKeyPress = keyRecord.HandleKeyPress(wParam, vkCode,keyRecord);

                if (blockKeyPress)
                    return (IntPtr)1;
            }
            
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private static bool HandleRegisteredKeyPress(IntPtr wParam, int vkCode, KeyPressActionRecord keyRecord)
        {
            if (nonRegisteredKeyDown)
                cancelAction = true;

            if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
            {
                registeredKeyDown = true;

                if (vkCode == VK_LWIN || vkCode == VK_RWIN)
                    isLWinKeyDown = true;

                if (keyRecord.AreKeyCombosRegistered())
                {
                    var triggerCombo = false;

                    KeyComboRecord comboFound = null;
                    for (var index = 0; index < keyRecord.KeyComboRecords.Count; index++)
                    {
                        if (keyRecord.KeyComboRecords[index].AreComboKeysHeldDown())
                        {
                            comboFound = keyRecord.KeyComboRecords[index];
                            triggerCombo = true;

                            break;
                        }
                    }
                        

                    if (triggerCombo)
                    {
                        comboFound.action?.Invoke("");
                        cancelAction = false;

                        return false;
                    }
                }
            }

            if (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP)
            {
                registeredKeyDown = false;

                if (vkCode == VK_LWIN || vkCode == VK_RWIN)
                {
                    if (!cancelAction && isLWinKeyDown)
                    {
                        SendAltKeyDown();
                        SendLWinKeyUp();
                        isLWinKeyDown = false;
                        SendAltKeyUp();

                        keyRecord.action?.Invoke("LWin key remapped");

                        return true;
                    }

                    isLWinKeyDown = false;
                    cancelAction = false;

                    return false;
                }

                if (!cancelAction)
                {
                    if (keyRecord.isSingleKeyRegistered)
                        keyRecord.action?.Invoke("");
                }

                cancelAction = false;
            }

            return false;
        }

        private static bool HandleNonRegisteredKeyPress(IntPtr wParam, int vkCode, KeyPressActionRecord registeredKeyRecord)
        {
            if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
                nonRegisteredKeyDown = true;

            if (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP)
                nonRegisteredKeyDown = false;

            // Handles instance where non-registered key is up while single registered key is still down,
            // e.g. registered Ctrl down, unregistered Esc down & up, registered Ctrl up
            if (registeredKeyDown)
                cancelAction = true;

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
