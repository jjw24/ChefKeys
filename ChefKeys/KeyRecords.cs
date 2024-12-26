using static ChefKeys.Constants.KeyboardKeys;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ChefKeys
{
    internal record KeyRecord()
    {
        internal int vk_code { get; set; }

        internal List<KeyComboRecord> KeyComboRecords = new();

        internal bool isSingleKeyRegistered { get; set; } = false;

        internal Action<string> action;

        internal Func<IntPtr, int, KeyRecord, bool> HandleKeyPress { get; set; }

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

    internal record KeyComboRecord()
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

                if ((NativeMethods.GetAsyncKeyState(vkCodeCombo0) & 0x8000) == 0)
                    heldDown = false;
            }

            if (vkCodeCombo1 > 0)
            {
                comboKeys.Add(vkCodeCombo1, string.Empty);

                if ((NativeMethods.GetAsyncKeyState(vkCodeCombo1) & 0x8000) == 0)
                    heldDown = false;
            }

            if (vkCodeCombo2 > 0)
            {
                comboKeys.Add(vkCodeCombo2, string.Empty);

                if ((NativeMethods.GetAsyncKeyState(vkCodeCombo2) & 0x8000) == 0)
                    heldDown = false;
            }

            if (NonRegisteredModifierKeyPressed(comboKeys))
                heldDown = false;

            return heldDown;
        }

        private bool NonRegisteredModifierKeyPressed(Dictionary<int, string> comboKeys)
        {
            if (!comboKeys.ContainsKey(VK_LCTRL) && ((NativeMethods.GetAsyncKeyState(VK_LCTRL) & 0x8000) != 0))
                return true;

            if (!comboKeys.ContainsKey(VK_RCTRL) && ((NativeMethods.GetAsyncKeyState(VK_RCTRL) & 0x8000) != 0))
                return true;

            if (!comboKeys.ContainsKey(VK_LALT) && ((NativeMethods.GetAsyncKeyState(VK_LALT) & 0x8000) != 0))
                return true;

            if (!comboKeys.ContainsKey(VK_RALT) && ((NativeMethods.GetAsyncKeyState(VK_RALT) & 0x8000) != 0))
                return true;

            if (!comboKeys.ContainsKey(VK_LSHIFT) && ((NativeMethods.GetAsyncKeyState(VK_LSHIFT) & 0x8000) != 0))
                return true;

            if (!comboKeys.ContainsKey(VK_RSHIFT) && ((NativeMethods.GetAsyncKeyState(VK_RSHIFT) & 0x8000) != 0))
                return true;

            if (!comboKeys.ContainsKey(VK_LWIN) && ((NativeMethods.GetAsyncKeyState(VK_LWIN) & 0x8000) != 0))
                return true;

            if (!comboKeys.ContainsKey(VK_RWIN) && ((NativeMethods.GetAsyncKeyState(VK_RWIN) & 0x8000) != 0))
                return true;

            return false;
        }
    }
}
