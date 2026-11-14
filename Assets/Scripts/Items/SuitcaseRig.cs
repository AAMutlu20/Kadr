using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Items
{
    /// <summary>
    /// The 3D suitcase scene: the inventory camera, the lid animation and the list of grid zones. It is pure
    /// presentation. It knows nothing about items or input.
    ///
    /// Set it up once, placed far from the level (for example at 2000, 0, 2000) and on its own layer, so the world
    /// never lights it and the main camera never sees it:
    ///
    ///   InventoryRig             (this component)
    ///     Suitcase               the model, with an Animator that opens the lid
    ///       ...lid mesh          has a child InventoryZone (Permanent) and the album hotspot
    ///     ConsumablesZone        InventoryZone (Consumable), on the floor of the bottom half
    ///     InventoryCamera        a camera that looks at the opened suitcase
    ///     Light                  one light over the suitcase
    ///
    /// The lid is animated with a normal Unity Animator that has a bool parameter (see below), so there is no
    /// code driven lid.
    /// </summary>
    public class SuitcaseRig : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Camera inventoryCamera;
        [Tooltip("The grid areas: one Consumable and one Permanent.")]
        [SerializeField] private InventoryZone[] zones;

        [Header("Lid animation")]
        [Tooltip("Needs a bool parameter (named below) that opens and closes the lid.")]
        [SerializeField] private Animator lidAnimator;
        [SerializeField] private string openBool = "Open";
        [Tooltip("Objects hidden while the case is open (for example the clasp).")]
        [SerializeField] private GameObject[] hideWhenOpen;

        [Header("Layer")]
        [Tooltip("The layer the whole rig is moved to. The inventory camera only draws this layer. " +
                 "Create it in Project Settings > Tags and Layers, and untick it on the main camera.")]
        [SerializeField] private string layerName = "Inventory";

        private int _openHash;
        private Coroutine _cameraOff;

        public Camera Camera => inventoryCamera;
        public IReadOnlyList<InventoryZone> Zones => zones;
        public int Layer { get; private set; }

        private void Awake()
        {
            if (!inventoryCamera)
            {
                Debug.LogError("[SuitcaseRig] Inventory Camera is not assigned.", this);
                enabled = false;
                return;
            }

            Layer = LayerMask.NameToLayer(layerName);
            if (Layer < 0)
            {
                Debug.LogError($"[SuitcaseRig] Layer '{layerName}' does not exist. Create it in Tags and Layers.", this);
                Layer = gameObject.layer;
            }

            SetLayerRecursive(gameObject, Layer);
            inventoryCamera.cullingMask = 1 << Layer;
            inventoryCamera.enabled = false; // only on while the inventory is open

            _openHash = Animator.StringToHash(openBool);

            // The game is paused while the inventory is open, so the lid must animate on unscaled time.
            if (lidAnimator) lidAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
        }

        /// <summary>Opens or closes the suitcase. The camera goes off after a delay so the closing animation is seen.</summary>
        public void SetOpen(bool open, float cameraOffDelay = 0f)
        {
            foreach (var go in hideWhenOpen)
            {
                if (go) go.SetActive(!open);
            }

            if (_cameraOff != null)
            {
                StopCoroutine(_cameraOff);
                _cameraOff = null;
            }

            if (open)
            {
                inventoryCamera.enabled = true;
            }
            else if (cameraOffDelay > 0f && isActiveAndEnabled)
            {
                _cameraOff = StartCoroutine(CameraOffAfter(cameraOffDelay));
            }
            else
            {
                inventoryCamera.enabled = false;
            }

            if (lidAnimator && !string.IsNullOrEmpty(openBool)) lidAnimator.SetBool(_openHash, open);
        }

        private IEnumerator CameraOffAfter(float delay)
        {
            yield return new WaitForSecondsRealtime(delay); // realtime: the game may be paused
            inventoryCamera.enabled = false;
            _cameraOff = null;
        }

        public static void SetLayerRecursive(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
        }
    }
}