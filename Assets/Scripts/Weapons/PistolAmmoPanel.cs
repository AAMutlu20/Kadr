using Items;
using UnityEngine;
using UnityEngine.UI;

namespace Weapons
{
    /// <summary>
    /// The ammo readout in the bottom right corner. Created by AmmoReserve, so nothing needs setting up.
    ///
    ///   big number   rounds in the current weapon / its magazine size (orange while incendiary rounds are loaded)
    ///   pistol only  how many normal and incendiary rounds are in the suitcase. The selected kind (X) has a ">" and
    ///                is bright: that is what the next reload loads.
    ///
    /// It reads the current weapon from the WeaponController every frame, so it follows weapon switches by itself.
    /// Hidden while the suitcase is open. If you already have a magazine counter on screen, this one shows the same
    /// number: remove the old one or this panel's big number.
    /// </summary>
    public class PistolAmmoPanel : MonoBehaviour
    {
        private static readonly Color Normal = new(1f, 1f, 1f, 0.95f);
        private static readonly Color Fire = new(1f, 0.55f, 0.15f, 1f);

        private AmmoReserve _reserve;
        private Inventory _inventory;
        private WeaponController _weapons;
        private GameObject _root;
        private Text _main;
        private Text _sub;
        private string _lastMain;
        private string _lastSub;

        public static PistolAmmoPanel Create(Transform parent, AmmoReserve reserve, Inventory inventory)
        {
            var go = new GameObject("PistolAmmoPanel");
            go.transform.SetParent(parent, false);

            var panel = go.AddComponent<PistolAmmoPanel>();
            panel._reserve = reserve;
            panel._inventory = inventory;
            panel.Build();
            return panel;
        }

        private void Build()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 15;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(canvasGo.transform, false);
            var rect = _root.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-40f, 40f);
            rect.sizeDelta = new Vector2(380f, 140f);

            _main = MakeText(font, "Main", 56, TextAnchor.LowerRight, new Vector2(0f, 62f), new Vector2(380f, 74f));
            _sub = MakeText(font, "Sub", 24, TextAnchor.LowerRight, Vector2.zero, new Vector2(380f, 62f));
            _sub.supportRichText = true;

            _root.SetActive(false);
        }

        private Text MakeText(Font font, string name, int size, TextAnchor anchor, Vector2 position, Vector2 sizeDelta)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline));
            go.transform.SetParent(_root.transform, false);

            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = FontStyle.Bold;
            text.alignment = anchor;
            text.raycastTarget = false;
            go.GetComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.85f);

            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = position;
            rect.sizeDelta = sizeDelta;
            return text;
        }

        private void Update()
        {
            if (!_weapons) _weapons = FindAnyObjectByType<WeaponController>();

            var weapon = _weapons ? _weapons.Current : null;
            var hidden = weapon == null || (_inventory && _inventory.IsOpen);
            if (_root.activeSelf == hidden) _root.SetActive(!hidden);
            if (hidden) return;

            var pistol = weapon as PistolStrategy;
            var loaded = pistol != null ? pistol.LoadedKind : AmmoKind.Normal;

            var main = weapon.Ammo + " / " + weapon.MaxAmmo;
            if (main != _lastMain)
            {
                _lastMain = main;
                _main.text = main;
            }

            _main.color = loaded == AmmoKind.Incendiary && pistol != null ? Fire : Normal;

            var sub = pistol != null ? BuildReserveLines(loaded) : "<color=#aaaaaa>" + weapon.Name.ToUpperInvariant() + "</color>";
            if (sub != _lastSub)
            {
                _lastSub = sub;
                _sub.text = sub;
            }
        }

        private string BuildReserveLines(AmmoKind loaded)
        {
            return Line(AmmoKind.Normal, loaded) + "\n" + Line(AmmoKind.Incendiary, loaded);
        }

        private string Line(AmmoKind kind, AmmoKind loaded)
        {
            var selected = _reserve.Selected == kind;
            var color = kind == AmmoKind.Incendiary ? (selected ? "#ff8c26" : "#a86a30") : (selected ? "#ffffff" : "#999999");
            var marker = selected ? "> " : "";
            var tag = loaded == kind ? "  (loaded)" : "";

            return "<color=" + color + ">" + marker + _reserve.Label(kind).ToUpperInvariant() + "  " + _reserve.Count(kind) + tag + "</color>";
        }
    }
}