using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Input;
using static ChefKeys.NativeMethods;
using static ChefKeys.Constants.KeyboardKeys;

namespace ChefKeys
{
    public static class ChefKeysManager
    {
        #region setup

        private static IntPtr _hookID = IntPtr.Zero;
        private static LowLevelKeyboardProc _proc;
        private static bool _isSimulatingKeyPress = false;
        
        private static readonly Dictionary<int, KeyRecord> registeredHotkeys;
        private static readonly KeyRecord nonRegisteredKeyRecord;

        static ChefKeysManager()
        {
            registeredHotkeys = new Dictionary<int, KeyRecord>();
            nonRegisteredKeyRecord = new KeyRecord { vk_code = 0, HandleKeyPress = HandleNonRegisteredKeyPress };
            
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

        #endregion setup


        #region key press logic

        private static bool isLWinKeyDown = false;
        private static bool nonRegisteredKeyDown = false;
        private static bool cancelAction = false;
        private static bool registeredKeyDown = false;

        public static bool StartMenuEnableBlocking = false;
        public static bool StartMenuBlocked = false;
        public static string StartMenuSimulatedKey = "LeftAlt";

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && !_isSimulatingKeyPress)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                var isNotRegisteredkey = !registeredHotkeys.TryGetValue(vkCode, out KeyRecord registeredKeyRecord);

                var keyRecord = isNotRegisteredkey ? nonRegisteredKeyRecord : registeredKeyRecord;

                // Create a one-off win key record when blocking is enabled
                if (StartMenuEnableBlocking && (vkCode == VK_LWIN || vkCode == VK_RWIN))
                {
                    keyRecord = new KeyRecord
                    {
                        vk_code = vkCode,
                        HandleKeyPress = HandleRegisteredKeyPress,
                        isSingleKeyRegistered = true,
                        action = null
                    };
                }

                var blockKeyPress = keyRecord.HandleKeyPress(wParam, vkCode, keyRecord);

                if (blockKeyPress)
                    return (IntPtr)1;
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private static bool HandleRegisteredKeyPress(IntPtr wParam, int vkCode, KeyRecord keyRecord)
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

                StartMenuBlocked = false;
                if (vkCode == VK_LWIN || vkCode == VK_RWIN)
                {
                    if (!cancelAction && isLWinKeyDown)
                    {
                        BlockWindowsStartMenu();

                        StartMenuBlocked = true;

                        keyRecord.action?.Invoke("");

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

        private static bool HandleNonRegisteredKeyPress(IntPtr wParam, int vkCode, KeyRecord registeredKeyRecord)
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

        private static void BlockWindowsStartMenu()
        {
            _isSimulatingKeyPress = true;
            keybd_event(VK_LALT, 0, 0, UIntPtr.Zero);
            _isSimulatingKeyPress = false;
            _isSimulatingKeyPress = true;
            keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            _isSimulatingKeyPress = false;
            isLWinKeyDown = false;
            _isSimulatingKeyPress = true;
            keybd_event(VK_LALT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            _isSimulatingKeyPress = false;
        }

        #endregion key press logic


        #region key management

        public static void RegisterHotkey(string hotkeys, Action<string> action) => RegisterHotkey(hotkeys, hotkeys, action);

        public static void RegisterHotkey(string hotkeys, string previousHotkey, Action<string> action)
        {
            hotkeys = ConvertIncorrectKeyString(hotkeys);
            previousHotkey = ConvertIncorrectKeyString(previousHotkey);

            UnregisterHotkey(previousHotkey);

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

            var keyRecord = new KeyRecord
            {
                vk_code = ToKeyCode(keys.First()),
                HandleKeyPress = HandleRegisteredKeyPress,
                isSingleKeyRegistered = singleKey,
                action = singleKey ? action : null
            };

            if (comboKeys)
                keyRecord.RegisterKeyCombo(hotkeys, vk_code, action, vkCodeCombo0, vkCodeCombo1, vkCodeCombo2);

            registeredHotkeys.Add(ToKeyCode(keys.First()), keyRecord);
        }

        public static void UnregisterHotkey(string hotkey)
        {
            var existingKeyRecord = GetRegisteredKeyRecord(hotkey);

            if (existingKeyRecord is null)
                return;

            if (!IsComboString(hotkey))
            {
                existingKeyRecord.action -= existingKeyRecord.action;
                existingKeyRecord.isSingleKeyRegistered = false;

                if (!existingKeyRecord.AreKeyCombosRegistered())
                    registeredHotkeys.Remove(existingKeyRecord.vk_code);

                return;
            }

            var comboRecord = existingKeyRecord.KeyComboRecords.FirstOrDefault(x => x.comboRaw == hotkey);
            comboRecord.action -= comboRecord.action;
            existingKeyRecord.KeyComboRecords.RemoveAll(x => x.comboRaw == hotkey);

            if (!existingKeyRecord.isSingleKeyRegistered && !existingKeyRecord.AreKeyCombosRegistered())
                registeredHotkeys.Remove(existingKeyRecord.vk_code);
        }

        internal static KeyRecord GetRegisteredKeyRecord(string hotkey)
        {
            if (!registeredHotkeys.TryGetValue(ToKeyCode(SplitHotkeyReversed(hotkey).First()), out var existingKeyRecord))
                return null;

            if (IsComboString(hotkey) && !existingKeyRecord.KeyComboRecords.Exists(x => x.comboRaw == hotkey))
                return null;

            return existingKeyRecord;
        }

        private static int ToKeyCode(string key)
        {
            return KeyInterop.VirtualKeyFromKey((Key)Enum.Parse(typeof(Key), key));
        }
        
        private static IEnumerable<string> SplitHotkeyReversed(string hotkeys)
            => hotkeys.Split("+", StringSplitOptions.RemoveEmptyEntries).Reverse();

        private static string ConvertIncorrectKeyString(string hotkey)
        {
            var keys = hotkey.Split("+", StringSplitOptions.RemoveEmptyEntries);

            var newHotkey = string.Empty;
            foreach (var key in keys)
            {
                if (!string.IsNullOrEmpty(newHotkey))
                    newHotkey += "+";
                // TODO: convert lowercase string passed in e.g. "win", "lwin", "leftalt+z" ??
                switch (key.ToLower())
                {
                    case "alt":
                        newHotkey += "LeftAlt";
                        break;
                    case "lalt":
                        newHotkey += "LeftAlt";
                        break;
                    case "ralt":
                        newHotkey += "RightAlt";
                        break;
                    case "ctrl":
                        newHotkey += "LeftCtrl";
                        break;
                    case "lctrl":
                        newHotkey += "LeftCtrl";
                        break;
                    case "rctrl":
                        newHotkey += "RightCtrl";
                        break;
                    case "shift":
                        newHotkey += "LeftShift";
                        break;
                    case "lshift":
                        newHotkey += "LeftShift";
                        break;
                    case "rshift":
                        newHotkey += "RightShift";
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

        private static bool IsComboString(string hotkey) => hotkey.Contains('+');

        #endregion key management
    }
}
