using System.Collections.Generic;
using Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;
using Weapons;

namespace Items
{
    /// <summary>
    /// The 3D Resident Evil style inventory. TAB opens and closes it.
    ///
    /// Controls while open:
    ///   Left click   pick up the item under the cursor (it follows the mouse), click again to put it down.
    ///                Dropping onto a stack of the same item merges them.
    ///   R            rotate the held item (the ghost on the grid is green when it fits, red when it does not)
    ///   Right click  use the item under the cursor, or put back the item you are holding
    ///   Esc / Tab    close
    ///
    /// Model and view are separate. <see cref="InventoryGrid"/> decides what is where. This class only draws it as
    /// 3D objects inside <see cref="SuitcaseRig"/> and turns mouse input into grid calls (TryMove, TryMerge, TryUse).
    /// Cells are found by a ray against the grid plane plus maths, not by trigger colliders, so there is no physics
    /// timing to go wrong and the cost does not grow with the number of cells.
    ///
    /// The world keeps running while it is open (real time). Only the player's own input is switched off.
    /// </summary>
    public class InventoryView3D : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Inventory inventory;
        [SerializeField] private SuitcaseRig rig;
        [Tooltip("Optional. Health, XP and the item text on screen.")]
        [SerializeField] private InventoryHud hud;

        [Header("Feel")]
        [Tooltip("Seconds after opening before clicks work, so the lid has time to open.")]
        [SerializeField] private float openDelay = 0.6f;
        [Tooltip("Seconds the camera stays on after closing, so the lid is seen closing.")]
        [SerializeField] private float closeDelay = 0.4f;
        [Tooltip("Height of an item resting in the suitcase, in the rig's units.")]
        [SerializeField] private float restLift = 0.005f;
        [Tooltip("Height of the item you are holding. Higher = clearly above the other items.")]
        [SerializeField] private float heldLift = 0.06f;
        [Tooltip("How fast items glide to their target. Higher = snappier.")]
        [SerializeField] private float moveSharpness = 18f;

        [Header("Materials")]
        [Tooltip("A transparent Unlit material for the green/red placement ghost. Empty = a built-in fallback.")]
        [SerializeField] private Material ghostMaterial;
        [Tooltip("Used for items that have no model yet (a flat tinted block). Empty = the default material.")]
        [SerializeField] private Material fallbackItemMaterial;

        [Header("Stack count text")]
        [SerializeField] private float countTextSize = 0.012f;
        [SerializeField] private float countTextHeight = 0.05f;

        private static readonly Color GhostOk = new(0.3f, 1f, 0.4f, 0.4f);
        private static readonly Color GhostBad = new(1f, 0.25f, 0.25f, 0.4f);

        private class ItemView
        {
            public ItemStack Stack;
            public Transform Root;   // sits at the centre of the footprint, never rotates
            public Transform Pivot;  // holds the model and turns 90 degrees when rotated
            public Transform Label;
            public TextMesh LabelText;
            public Vector3 TargetPos;
            public Quaternion TargetRot = Quaternion.identity;
        }

        private readonly Dictionary<ItemStack, ItemView> _views = new();

        private PlayerController _player;
        private WeaponController _weapons;
        private Health _health;
        private Font _font;

        private Transform _ghost;
        private Material _ghostMaterial;

        private bool _open;
        private float _readyAt;
        private ItemStack _held;
        private bool _heldRotated;

        private void Start()
        {
            if (!inventory || !rig)
            {
                Debug.LogError("[InventoryView3D] Inventory or Rig is not assigned.", this);
                enabled = false;
                return;
            }

            if (!rig.Matches(inventory.Grid))
            {
                Debug.LogWarning("[InventoryView3D] The rig's columns/rows do not match the Inventory's width/height. " +
                                 "Cells will be misaligned.", this);
            }

            _player = inventory.GetComponent<PlayerController>();
            _health = inventory.GetComponent<Health>();
            _weapons = FindAnyObjectByType<WeaponController>();
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            BuildGhost();
            inventory.Grid.Changed += Sync;
            Sync();
        }

        private void OnDestroy()
        {
            if (inventory && inventory.Grid != null) inventory.Grid.Changed -= Sync;
            if (_ghostMaterial) Destroy(_ghostMaterial);
        }

        private void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            if (_open && _health && _health.IsDead)
            {
                Close();
                return;
            }

            if (kb.tabKey.wasPressedThisFrame)
            {
                if (_open) Close();
                else Open();
                return;
            }

            AnimateViews();

            if (!_open) return;

            if (kb.escapeKey.wasPressedThisFrame)
            {
                Close();
                return;
            }

            if (Time.time < _readyAt) return;

            var screen = mouse.position.ReadValue();
            var hasPoint = rig.TryRaycast(screen, 0f, out var local);
            var over = hasPoint && rig.TryGetCell(local, inventory.Grid, out _, out _);
            var cx = 0;
            var cy = 0;
            if (over) rig.TryGetCell(local, inventory.Grid, out cx, out cy);

            // R: rotate the held item. A square item has no point in turning.
            if (kb.rKey.wasPressedThisFrame && _held != null && _held.Definition.Width != _held.Definition.Height)
            {
                _heldRotated = !_heldRotated;
                ApplyTargets(_views[_held]);
            }

            // The held item follows the mouse, floating above the grid.
            if (_held != null && rig.TryRaycast(screen, heldLift, out var heldLocal))
            {
                _views[_held].TargetPos = heldLocal;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (_held == null)
                {
                    if (over) PickUp(cx, cy);
                }
                else if (hasPoint)
                {
                    Drop(local, over, cx, cy);
                }
            }

            if (mouse.rightButton.wasPressedThisFrame)
            {
                if (_held != null) CancelHold();
                else if (over) inventory.TryUse(inventory.Grid.GetAt(cx, cy));
            }

            UpdateGhostAndInfo(hasPoint, local, over, cx, cy);
        }

        // ---------- open and close ----------

        private void Open()
        {
            if (_health && _health.IsDead) return;

            _open = true;
            inventory.IsOpen = true;
            _readyAt = Time.time + openDelay;

            rig.SetOpen(true);
            if (hud) hud.SetVisible(true);

            // Freeze the player's own input but not the world: enemies still come for you.
            if (_player) _player.InputEnabled = false;
            if (_weapons) _weapons.InputEnabled = false;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Close()
        {
            _open = false;
            inventory.IsOpen = false;

            // Whatever was held goes back to its spot. It never left the grid, so nothing is lost.
            var wasHeld = _held;
            _held = null;
            if (wasHeld != null && _views.TryGetValue(wasHeld, out var view)) ApplyTargets(view);

            rig.SetOpen(false, closeDelay);
            if (hud) hud.SetVisible(false);
            if (_ghost) _ghost.gameObject.SetActive(false);

            // A dead player keeps input off, so closing must not switch anything back on.
            var dead = _health && _health.IsDead;
            if (_player && !dead) _player.InputEnabled = true;
            if (_weapons && !dead) _weapons.InputEnabled = true;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // ---------- picking up and putting down ----------

        private void PickUp(int cx, int cy)
        {
            var stack = inventory.Grid.GetAt(cx, cy);
            if (stack == null) return;

            _held = stack;
            _heldRotated = stack.Rotated;
            ApplyTargets(_views[stack]);
        }

        private void Drop(Vector3 local, bool over, int cx, int cy)
        {
            var w = _heldRotated ? _held.Definition.Height : _held.Definition.Width;
            var h = _heldRotated ? _held.Definition.Width : _held.Definition.Height;
            rig.AnchorFromPoint(local, w, h, out var ax, out var ay);

            var stack = _held;

            if (inventory.Grid.TryMove(stack, ax, ay, _heldRotated))
            {
                _held = null;
                if (_views.TryGetValue(stack, out var view)) ApplyTargets(view);
                return;
            }

            // Not free: if the cell under the cursor holds the same stackable item, pour into it.
            if (!over) return;

            var other = inventory.Grid.GetAt(cx, cy);
            if (other != null && inventory.Grid.TryMerge(stack, other))
            {
                _held = null;

                // A partial merge leaves the stack alive with fewer items: send it back to its cells.
                if (_views.TryGetValue(stack, out var view)) ApplyTargets(view);
            }
        }

        /// <summary>
        /// Right click while holding. The held stack never left the grid (it only moves on a successful drop),
        /// so putting it back just forgets the pending move and the rotation.
        /// </summary>
        private void CancelHold()
        {
            var stack = _held;
            _held = null;
            if (stack != null && _views.TryGetValue(stack, out var view)) ApplyTargets(view);
        }

        // ---------- the 3D items ----------

        /// <summary>Makes the views match the grid: creates the new ones, removes the ones whose stack is gone.</summary>
        private void Sync()
        {
            var grid = inventory.Grid;

            foreach (var stack in grid.Stacks)
            {
                if (_views.TryGetValue(stack, out var existing))
                {
                    ApplyTargets(existing);
                }
                else
                {
                    var view = CreateView(stack);
                    _views[stack] = view;
                    ApplyTargets(view, true);
                }
            }

            List<ItemStack> stale = null;
            foreach (var pair in _views)
            {
                if (!ContainsStack(pair.Key))
                {
                    stale ??= new List<ItemStack>();
                    stale.Add(pair.Key);
                }
            }

            if (stale == null) return;

            foreach (var stack in stale)
            {
                Destroy(_views[stack].Root.gameObject);
                _views.Remove(stack);
                if (_held == stack) _held = null;
            }
        }

        private bool ContainsStack(ItemStack stack)
        {
            foreach (var s in inventory.Grid.Stacks)
            {
                if (s == stack) return true;
            }

            return false;
        }

        /// <summary>Sets where a view wants to be and how it wants to be turned. snap = jump there at once.</summary>
        private void ApplyTargets(ItemView v, bool snap = false)
        {
            var s = v.Stack;
            var held = s == _held;
            var rotated = held ? _heldRotated : s.Rotated;
            var w = rotated ? s.Definition.Height : s.Definition.Width;
            var h = rotated ? s.Definition.Width : s.Definition.Height;
            var cell = rig.CellSize;

            v.TargetRot = Quaternion.Euler(0f, rotated ? 90f : 0f, 0f);

            // The held item's position comes from the mouse every frame, so only resting items get a target here.
            if (!held) v.TargetPos = rig.FootprintCenter(s.X, s.Y, w, h, restLift);

            if (snap)
            {
                v.Root.localPosition = v.TargetPos;
                v.Pivot.localRotation = v.TargetRot;
            }

            var showCount = s.Count > 1;
            v.Label.gameObject.SetActive(showCount);
            if (showCount)
            {
                v.LabelText.text = s.Count.ToString();
                // Lower right corner of the footprint, in the root's own (unrotated) space.
                v.Label.localPosition = new Vector3(w * cell * 0.5f - cell * 0.1f, countTextHeight, -h * cell * 0.5f + cell * 0.1f);
            }
        }

        private void AnimateViews()
        {
            var t = 1f - Mathf.Exp(-moveSharpness * Time.deltaTime);

            foreach (var view in _views.Values)
            {
                view.Root.localPosition = Vector3.Lerp(view.Root.localPosition, view.TargetPos, t);
                view.Pivot.localRotation = Quaternion.Slerp(view.Pivot.localRotation, view.TargetRot, t);
            }
        }

        private ItemView CreateView(ItemStack stack)
        {
            var def = stack.Definition;

            var root = new GameObject(def.DisplayName).transform;
            root.SetParent(rig.GridOrigin, false);

            var pivot = new GameObject("Pivot").transform;
            pivot.SetParent(root, false);

            BuildModel(def, pivot);

            var labelGo = new GameObject("Count");
            labelGo.transform.SetParent(root, false);
            labelGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // lie flat, readable from above

            var text = labelGo.AddComponent<TextMesh>();
            text.font = _font;
            text.fontSize = 100;
            text.characterSize = countTextSize;
            text.anchor = TextAnchor.LowerRight;
            text.alignment = TextAlignment.Right;
            text.color = Color.white;
            labelGo.GetComponent<MeshRenderer>().sharedMaterial = _font.material;

            SuitcaseRig.SetLayerRecursive(root.gameObject, rig.Layer);

            return new ItemView { Stack = stack, Root = root, Pivot = pivot, Label = labelGo.transform, LabelText = text };
        }

        /// <summary>
        /// Puts the item's model under the pivot, scaled to fill its footprint. The scale is worked out on a copy at
        /// the world origin, so the suitcase's own position and rotation can never skew the measurement.
        /// </summary>
        private void BuildModel(ItemDefinition def, Transform pivot)
        {
            var targetW = def.Width * rig.CellSize;
            var targetD = def.Height * rig.CellSize;

            if (def.InventoryModel)
            {
                var model = Instantiate(def.InventoryModel, Vector3.zero, Quaternion.identity);

                foreach (var c in model.GetComponentsInChildren<Collider>()) Destroy(c);

                var renderers = model.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    var bounds = renderers[0].bounds;
                    for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

                    // Uniform scale: fit the model inside the footprint along its length (X) and width (Z).
                    var fit = Mathf.Min(targetW / Mathf.Max(bounds.size.x, 0.0001f),
                        targetD / Mathf.Max(bounds.size.z, 0.0001f)) * 0.95f;

                    model.transform.SetParent(pivot, false);
                    model.transform.localScale *= fit;

                    // Centre it on the footprint, with its underside on the surface.
                    model.transform.localPosition = new Vector3(
                        -bounds.center.x * fit, -bounds.min.y * fit, -bounds.center.z * fit);
                    return;
                }

                Destroy(model);
            }

            // No model yet: a flat tinted block, so the system is testable before any art exists.
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(block.GetComponent<Collider>());
            block.transform.SetParent(pivot, false);
            block.transform.localScale = new Vector3(targetW * 0.92f, 0.012f, targetD * 0.92f);
            block.transform.localPosition = new Vector3(0f, 0.006f, 0f);

            var rend = block.GetComponent<MeshRenderer>();
            if (fallbackItemMaterial) rend.sharedMaterial = fallbackItemMaterial;

            var hash = Mathf.Abs(def.Id.GetHashCode());
            var tint = Color.HSVToRGB((hash % 100) / 100f, 0.45f, 0.65f);
            var block2 = new MaterialPropertyBlock();
            block2.SetColor("_BaseColor", tint); // URP Lit and Unlit
            block2.SetColor("_Color", tint);     // built in shaders
            rend.SetPropertyBlock(block2);
        }

        // ---------- the placement ghost ----------

        private void BuildGhost()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());

            _ghost = quad.transform;
            _ghost.SetParent(rig.GridOrigin, false);
            _ghost.localRotation = Quaternion.Euler(90f, 0f, 0f); // a quad faces -Z, this lays it face up

            _ghostMaterial = ghostMaterial
                ? new Material(ghostMaterial)
                : new Material(Shader.Find("Sprites/Default"));

            quad.GetComponent<MeshRenderer>().sharedMaterial = _ghostMaterial;
            SuitcaseRig.SetLayerRecursive(quad, rig.Layer);
            quad.SetActive(false);
        }

        private void UpdateGhostAndInfo(bool hasPoint, Vector3 local, bool over, int cx, int cy)
        {
            var shown = _held ?? (over ? inventory.Grid.GetAt(cx, cy) : null);

            if (hud)
            {
                if (shown != null)
                {
                    var def = shown.Definition;
                    var title = def.DisplayName + (shown.Count > 1 ? "  x" + shown.Count : "");
                    var body = def.Description + (def.Effect ? "\n<color=#8fbf8f>Right click: use</color>" : "");
                    hud.ShowInfo(title, body);
                }
                else
                {
                    hud.ShowInfo("", "");
                }
            }

            if (_held == null || !hasPoint || !over)
            {
                _ghost.gameObject.SetActive(false);
                return;
            }

            var w = _heldRotated ? _held.Definition.Height : _held.Definition.Width;
            var h = _heldRotated ? _held.Definition.Width : _held.Definition.Height;
            rig.AnchorFromPoint(local, w, h, out var ax, out var ay);

            var fits = inventory.Grid.CanPlace(_held.Definition, ax, ay, _heldRotated, _held);
            var mergeable = !fits && inventory.Grid.GetAt(cx, cy) is { } o && o != _held
                            && o.Definition == _held.Definition && o.Count < o.Definition.MaxStack;

            var color = fits || mergeable ? GhostOk : GhostBad;
            _ghostMaterial.color = color;

            _ghost.gameObject.SetActive(true);
            _ghost.localPosition = rig.FootprintCenter(ax, ay, w, h, 0.002f);
            _ghost.localScale = new Vector3(w * rig.CellSize, h * rig.CellSize, 1f);
        }
    }
}