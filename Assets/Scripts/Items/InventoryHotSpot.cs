using System;
using UnityEngine;
using UnityEngine.Events;

namespace Items
{
    /// <summary>
    /// Something in the suitcase you can click that is not a grid item: the photo album that opens the upgrades tab.
    /// Needs a Collider (the album's box). It is found by a ray from the inventory camera, so the collider only has
    /// to exist, it does not need a Rigidbody.
    ///
    /// Hooking it up: either subscribe to <see cref="Clicked"/> in code, or use the On Clicked list in the Inspector.
    /// An optional highlight object (an outline mesh, a light) is shown while the mouse is over it.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class InventoryHotspot : MonoBehaviour
    {
        [SerializeField] private string label = "Photo upgrades";
        [Tooltip("Optional. Switched on while the mouse is over the hotspot.")]
        [SerializeField] private GameObject hoverHighlight;
        [SerializeField] private UnityEvent onClicked;

        public string Label => label;

        public event Action Clicked;

        private void Awake()
        {
            SetHover(false);
        }

        public void SetHover(bool hovered)
        {
            if (hoverHighlight) hoverHighlight.SetActive(hovered);
        }

        public void Click()
        {
            Clicked?.Invoke();
            onClicked.Invoke();
        }
    }
}