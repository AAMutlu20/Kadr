using UnityEngine;
using UnityEngine.UI;

namespace Menus
{
    /// <summary>
    /// The game's two fonts, loaded once for every screen:
    ///  - Body: Special Elite, the typewriter. Titles, menu lines, prompts, notes, inventory text.
    ///  - Mono: Share Tech Mono, the instrument. Numbers, values, key names, timecodes, small labels.
    ///
    /// Both are found by name in any Resources/Fonts folder (Assets/Resources/Fonts/SpecialElite-Regular.ttf and
    /// ShareTechMono-Regular.ttf). Tools > Kadr > Move Fonts To Resources does the move for you. Without them everything
    /// falls back to Unity's default font and the Console says so once.
    ///
    /// Special Elite has no Cyrillic, so text with letters a font lacks is drawn in the fallback font by Fit().
    /// </summary>
    public static class KadrFont
    {
        private static bool _loaded;
        private static Font _body;
        private static Font _mono;
        private static Font _fallback;

        public static Font Body { get { Load(); return _body; } }
        public static Font Mono { get { Load(); return _mono; } }
        public static Font Fallback { get { Load(); return _fallback; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _loaded = false;
            _body = null;
            _mono = null;
            _fallback = null;
        }

        private static void Load()
        {
            if (_loaded && _fallback) return;
            _loaded = true;

            _fallback = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _body = null;
            _mono = null;

            foreach (var font in Resources.LoadAll<Font>("Fonts"))
            {
                var name = font.name.ToLowerInvariant();
                if (!_body && (name.Contains("elite") || name.Contains("special"))) _body = font;
                else if (!_mono && (name.Contains("share") || name.Contains("mono"))) _mono = font;
            }

            if (!_body && !_mono)
            {
                Debug.LogWarning("[KadrFont] No fonts in a Resources/Fonts folder. Use Tools > Kadr > Move Fonts To Resources, or put " +
                                 "SpecialElite-Regular.ttf and ShareTechMono-Regular.ttf in Assets/Resources/Fonts. Using the default font for now.");
            }

            if (!_body) _body = _mono ? _mono : _fallback;
            if (!_mono) _mono = _body;
        }

        /// <summary>True when the font is one of ours (they have one weight only, so bold would be fake and blurry).</summary>
        public static bool IsCustom(Font font) => font && font != Fallback;

        /// <summary>Sets the text, in the preferred font when it has every letter, else in the fallback font.</summary>
        public static void Fit(Text label, Font preferred, string text)
        {
            if (!label) return;

            label.text = text;
            label.font = Supports(preferred, text) ? preferred : Fallback;
        }

        private static bool Supports(Font font, string text)
        {
            if (!font) return false;
            if (string.IsNullOrEmpty(text)) return true;

            font.RequestCharactersInTexture(text, 32);
            foreach (var c in text)
            {
                if (c <= ' ' || c == ' ') continue;
                if (!font.HasCharacter(c)) return false;
            }

            return true;
        }
    }
}
