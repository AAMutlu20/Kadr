using System;
using Saving;

namespace Menus
{
    /// <summary>
    /// The three save slots, as pages of the ViewfinderMenu. Pages are rebuilt every time they are shown, so the info
    /// (roubles, deaths, date) is always current.
    ///
    ///   An empty slot  -> starts a new game at once.
    ///   A used slot    -> a page with CONTINUE, NEW GAME (asks first), DELETE (asks first), BACK.
    ///
    /// The menu does not start the game itself: it calls onStart(slot, newGame) and the caller (MainMenu) does the loading.
    /// </summary>
    public class SaveSlotMenu
    {
        private readonly ViewfinderMenu _menu;
        private readonly Action _back;
        private readonly Action<int, bool> _onStart;

        public SaveSlotMenu(ViewfinderMenu menu, Action back, Action<int, bool> onStart)
        {
            _menu = menu;
            _back = back;
            _onStart = onStart;
        }

        public void Show() => _menu.Show(BuildSlots());

        private MenuPage BuildSlots()
        {
            var page = new MenuPage { Heading = "SELECT ROLL", Compact = true, Width = 1100f, Back = _back };

            for (var i = 0; i < SaveSystem.SlotCount; i++)
            {
                var slot = i;
                var file = SaveSystem.Read(slot);

                page.Entries.Add(new MenuEntry
                {
                    Label = "ROLL " + (slot + 1).ToString("00"),
                    Sub = file == null ? "EMPTY" : Describe(file),
                    Select = () =>
                    {
                        if (SaveSystem.Read(slot) == null) _onStart(slot, true);
                        else _menu.Show(BuildSlot(slot));
                    }
                });
            }

            page.Entries.Add(new MenuEntry { Label = "BACK", Select = _back });
            return page;
        }

        private MenuPage BuildSlot(int slot)
        {
            var file = SaveSystem.Read(slot);
            var heading = "ROLL " + (slot + 1).ToString("00");
            var page = new MenuPage { Heading = heading, Compact = true, Back = Show };

            page.Entries.Add(new MenuEntry
            {
                Label = "CONTINUE",
                Sub = file != null && file.hasCheckpoint ? "AT THE PHONE" : "LOBBY",
                Select = () => _onStart(slot, false)
            });
            page.Entries.Add(new MenuEntry { Label = "NEW GAME", Select = () => _menu.Show(Confirm("ERASE ROLL " + (slot + 1).ToString("00") + "?", "NEW GAME", () => _onStart(slot, true))) });
            page.Entries.Add(new MenuEntry
            {
                Label = "DELETE",
                Select = () => _menu.Show(Confirm("DELETE ROLL " + (slot + 1).ToString("00") + "?", "DELETE", () =>
                {
                    SaveSystem.Delete(slot);
                    Show();
                }))
            });
            page.Entries.Add(new MenuEntry { Label = "BACK", Select = Show });
            return page;
        }

        // CANCEL first, so the focus starts on the safe choice.
        private MenuPage Confirm(string heading, string yesLabel, Action yes)
        {
            var page = new MenuPage { Heading = heading, Compact = true, Back = Show };
            page.Entries.Add(new MenuEntry { Label = "CANCEL", Select = Show });
            page.Entries.Add(new MenuEntry { Label = yesLabel, Select = yes });
            return page;
        }

        private static string Describe(SaveFile file)
        {
            var p = file.progress;
            var text = $"{p.roubles} RUB  ·  {p.deaths} {(p.deaths == 1 ? "DEATH" : "DEATHS")}";
            if (file.hasCheckpoint) text += "  ·  PHONE";
            if (!string.IsNullOrEmpty(file.savedAt)) text += "  ·  " + file.savedAt;
            return text;
        }
    }
}
