using System.Collections.Generic;
using Gameplay;
using Menus;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Items
{
    /// <summary>
    /// The 3D Resident Evil style inventory. TAB opens and closes it.
    ///
    /// Controls while open:
    ///   Left click   pick up the item under the cursor (it follows the mouse), click again to put it down.
    ///                Dropping onto a stack of the same item merges them. On the album it opens the upgrades.
    ///   R            rotate the held item (the ghost is green when it fits, red when it does not)
    ///   Right click  use the item under the cursor, or put back the item you are holding
    ///   Esc / Tab    close
    ///
    /// Two compartments: consumables go in the bottom half of the case, permanent items in the lid. An item can only
    /// be placed in the grid of its own category, so a held potion over the lid shows a red ghost.
    ///
    /// Model and view are separate. The <see cref="InventoryGrid"/>s decide what is where. This class only draws it
    /// as 3D objects and turns mouse input into grid calls (TryMove, TryMerge, TryUse). Cells are found with a ray
    /// against each zone's plane plus maths, not with trigger colliders.
    ///
    /// Item objects are driven in WORLD space from their zone every frame, so items on the lid swing with the lid
    /// while it opens and closes.
    ///
    /// The game is PAUSED while it is open (Time.timeScale = 0) and resumes when it closes. Everything here therefore
    /// uses unscaled time. Pausing does not stop code that reads the mouse directly, so WeaponController must ignore
    /// input while Time.timeScale is 0 (one line at the top of its Update), or a click on an item would fire the gun.
    /// </summary>
    public class InventoryView3D : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Inventory inventory;
        [SerializeField] private SuitcaseRig rig;
        [Tooltip("Optional. Health, XP, orders and the item text.")]
        [SerializeField] private InventoryHud hud;

        [Header("Pause")]
        [Tooltip("Freeze the game (Time.timeScale = 0) while the inventory is open.")]
        [SerializeField] private bool pauseGame = true;

        [Header("Feel")]
        [Tooltip("Seconds after opening before clicks work, so the lid has time to open.")]
        [SerializeField] private float openDelay = 0.6f;
        [Tooltip("Seconds the camera stays on after closing, so the lid is seen closing.")]
        [SerializeField] private float closeDelay = 0.4f;
        [Tooltip("Height of an item resting on its surface, in the zone's units.")]
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
        [Tooltip("Height of the number above the item's surface.")]
        [SerializeField] private float countLift = 0f;
        [Tooltip("Scale of the number object. Tuned in play mode: 0.3 looked right.")]
        [SerializeField] private float countScale = 0.3f;
        [Tooltip("Seconds after opening before the stack numbers appear, so they don't show through the closed lid. 0 = at once, with the health and XP.")]
        [SerializeField] private float countRevealDelay = 0.3f;

        private static readonly Color GhostOk = new(0.3f, 1f, 0.4f, 0.4f);
        private static readonly Color GhostBad = new(1f, 0.25f, 0.25f, 0.4f);

        private class ItemView
        {
            public ItemStack Stack;
            public InventoryZone Zone;
            public Transform Root;   // driven in world space, at the centre of the footprint
            public Transform Pivot;  // holds the model and turns 90 degrees when rotated
            public Transform Label;
            public TextMesh LabelText;
            public bool Easing;      // gliding to a new place; otherwise it is simply glued to its zone
        }

        private readonly Dictionary<ItemStack, ItemView> _views = new();
        private readonly Dictionary<ItemCategory, InventoryZone> _zones = new();

        private PlayerController _player;
        private Health _health;
        private Font _font;

        private Transform _ghost;
        private Material _ghostMaterial;

        private InventoryHotspot _hotspot;

        /// <summary>
        /// While true (the photo album overlay is open) the suitcase ignores the mouse and Esc. Tab still closes it.
        /// </summary>
        public bool Blocked { get; set; }

        private bool _open;
        private bool _countsOn;
        private float _countsAt = float.MaxValue;
        private float _readyAt;
        private float _savedTimeScale = 1f;
        private ItemStack _held;
        private bool _heldRotated;
        private bool _heldDetached;       // the held stack is a piece split off another stack, on no grid yet
        private ItemStack _splitSource;   // the stack the piece came from, where it goes back to if cancelled
        private InventoryZone _heldZone;  // the zone the held item last hovered, for its tilt
        private Vector3 _heldWorld;       // where the held item floats, in world space

        private void Start()
        {
            if (!inventory || !rig)
            {
                Debug.LogError("[InventoryView3D] Inventory or Rig is not assigned.", this);
                enabled = false;
                return;
            }

            foreach (var zone in rig.Zones)
            {
                if (!zone) continue;

                if (_zones.ContainsKey(zone.Category))
                {
                    Debug.LogWarning($"[InventoryView3D] Two zones use the category {zone.Category}. Only the first is used.", zone);
                    continue;
                }

                if (!zone.Bind(inventory.GridFor(zone.Category)))
                {
                    Debug.LogWarning($"[InventoryView3D] Zone '{zone.name}' columns/rows do not match the Inventory's " +
                                     $"{zone.Category} grid size. Cells will be misaligned.", zone);
                }

                _zones[zone.Category] = zone;
                zone.BuildGridVisual(rig.Layer);
            }

            foreach (ItemCategory category in System.Enum.GetValues(typeof(ItemCategory)))
            {
                if (!_zones.ContainsKey(category))
                {
                    Debug.LogError($"[InventoryView3D] The rig has no zone for the {category} category.", this);
                }
            }

            _player = inventory.GetComponent<PlayerController>();
            _health = inventory.GetComponent<Health>();
            _font = KadrFont.Body;

            BuildGhost();

            foreach (var grid in inventory.Grids) grid.Changed += Sync;
            Sync();
        }

        private void OnDisable()
        {
            // Never leave the game frozen if this object is disabled or destroyed while the inventory is open.
            if (_open && pauseGame) Time.timeScale = _savedTimeScale;
        }

        private void OnDestroy()
        {
            if (inventory && inventory.Grids != null)
            {
                foreach (var grid in inventory.Grids) grid.Changed -= Sync;
            }

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

            if (KadrControls.Pressed(GameAction.Inventory))
            {
                if (_open) Close();
                else Open();
                return;
            }

            AnimateViews();

            if (_open && !_countsOn && Time.unscaledTime >= _countsAt)
            {
                _countsOn = true;
                RefreshCounts();
            }

            if (!_open) return;

            if (Blocked)
            {
                if (_hotspot) _hotspot.SetHover(false);
                _hotspot = null;
                return;
            }

            if (kb.escapeKey.wasPressedThisFrame)
            {
                Close();
                return;
            }

            if (Time.unscaledTime < _readyAt) return;

            var ray = rig.Camera.ScreenPointToRay(mouse.position.ReadValue());

            // Which zone is the pointer over, and which cell of it?
            var zone = FindZoneUnderPointer(ray, out var local);
            var overCell = false;
            var cx = 0;
            var cy = 0;
            if (zone) overCell = zone.TryGetCell(local, out cx, out cy);

            UpdateHotspot(ray);

            // Rotate key: turn the held item. A square item has no point in turning.
            if (KadrControls.Pressed(GameAction.RotateItem) && _held != null && _held.Definition.Width != _held.Definition.Height)
            {
                _heldRotated = !_heldRotated;
                ApplyLabelAndCount(_views[_held]);
            }

            // The held item follows the mouse, floating above the zone it is over.
            if (_held != null && zone && zone.TryRaycast(ray, heldLift, out var lifted))
            {
                _heldWorld = zone.transform.TransformPoint(lifted);
                _heldZone = zone;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (_held == null)
                {
                    if (_hotspot) _hotspot.Click();
                    else if (zone && overCell) PickUp(zone, cx, cy);
                }
                else if (zone)
                {
                    Drop(zone, local, overCell, cx, cy);
                }
            }

            // Middle click: take half of a stack (rounded down, at least one) and hold it.
            if (mouse.middleButton.wasPressedThisFrame && _held == null && zone && overCell)
            {
                SplitStack(zone, cx, cy);
            }

            if (mouse.rightButton.wasPressedThisFrame)
            {
                if (_held != null) CancelHold();
                else if (zone && overCell) inventory.TryUse(zone.Grid.GetAt(cx, cy));
            }

            UpdateGhostAndInfo(zone, local, overCell, cx, cy);
        }

        /// <summary>The first zone whose rectangle the pointer is over. Each zone is a plane in 3D, so each gets its own ray test.</summary>
        private InventoryZone FindZoneUnderPointer(Ray ray, out Vector3 local)
        {
            foreach (var zone in _zones.Values)
            {
                if (zone.TryRaycast(ray, 0f, out local) && zone.Contains(local)) return zone;
            }

            local = default;
            return null;
        }

        private void UpdateHotspot(Ray ray)
        {
            InventoryHotspot hit = null;

            // The lid is moved by the Animator, but colliders only follow their transform when physics runs, and
            // physics does not run while the game is paused. Sync by hand or the album's box stays where the lid was.
            Physics.SyncTransforms();

            // Not while carrying an item: clicking then always means "put it down".
            if (_held == null &&
                Physics.Raycast(ray, out var info, 100f, 1 << rig.Layer, QueryTriggerInteraction.Collide))
            {
                hit = info.collider.GetComponentInParent<InventoryHotspot>();
            }

            if (hit == _hotspot) return;

            if (_hotspot) _hotspot.SetHover(false);
            _hotspot = hit;
            if (_hotspot) _hotspot.SetHover(true);
        }

        // ---------- open and close ----------

        private void Open()
        {
            if (_health && _health.IsDead) return;

            _open = true;
            inventory.IsOpen = true;
            _readyAt = Time.unscaledTime + openDelay;
            _countsOn = false;
            _countsAt = Time.unscaledTime + countRevealDelay;

            rig.SetOpen(true);
            if (hud) hud.SetVisible(true);

            // Freeze the player's own input, and the world with it.
            if (_player) _player.InputEnabled = false;

            if (pauseGame)
            {
                _savedTimeScale = Time.timeScale;
                Time.timeScale = 0f;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Close()
        {
            _open = false;
            inventory.IsOpen = false;
            _countsOn = false;
            _countsAt = float.MaxValue;
            RefreshCounts();

            // Whatever was held goes back to its spot. It never left its grid, so nothing is lost.
            ReleaseHeld();

            if (_hotspot) _hotspot.SetHover(false);
            _hotspot = null;

            rig.SetOpen(false, closeDelay);
            if (hud) hud.SetVisible(false);
            if (_ghost) _ghost.gameObject.SetActive(false);

            if (pauseGame) Time.timeScale = _savedTimeScale;

            // A dead player keeps input off, so closing must not switch anything back on.
            var dead = _health && _health.IsDead;
            if (_player && !dead) _player.InputEnabled = true;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // ---------- picking up and putting down ----------

        private void PickUp(InventoryZone zone, int cx, int cy)
        {
            var stack = zone.Grid.GetAt(cx, cy);
            if (stack == null || !_views.TryGetValue(stack, out var view)) return;

            _held = stack;
            _heldRotated = stack.Rotated;
            _heldZone = view.Zone;
            _heldWorld = view.Root.position;
            view.Easing = true;
            ApplyLabelAndCount(view);
        }

        /// <summary>
        /// Middle click on a stack of two or more: half of it comes off and is held. The piece is a new stack that
        /// is on no grid yet. It lands on a free spot, pours into another stack of the same item, or goes back.
        /// </summary>
        private void SplitStack(InventoryZone zone, int cx, int cy)
        {
            var source = zone.Grid.GetAt(cx, cy);
            if (source == null || source.Count < 2 || !_views.TryGetValue(source, out var sourceView)) return;

            var piece = zone.Grid.Split(source, Mathf.Max(1, source.Count / 2));
            if (piece == null) return;

            var view = CreateView(piece, sourceView.Zone);
            _views[piece] = view;

            _held = piece;
            _heldDetached = true;
            _splitSource = source;
            _heldRotated = piece.Rotated;
            _heldZone = sourceView.Zone;
            _heldWorld = sourceView.Root.position;
            view.Easing = true;

            ApplyLabelAndCount(view);
            ApplyLabelAndCount(sourceView);
        }

        private void DropDetached(InventoryZone zone, Vector3 local, bool overCell, int cx, int cy)
        {
            var piece = _held;
            var w = _heldRotated ? piece.Definition.Height : piece.Definition.Width;
            var h = _heldRotated ? piece.Definition.Width : piece.Definition.Height;
            zone.AnchorFromPoint(local, w, h, out var ax, out var ay);

            // Free spot: the piece becomes a stack of its own.
            if (zone.Grid.TryPlaceDetached(piece, ax, ay, _heldRotated))
            {
                _heldDetached = false;
                _splitSource = null;
                ReleaseHeld(piece);
                return;
            }

            // Otherwise pour it into a stack of the same item under the cursor.
            if (!overCell) return;

            var other = zone.Grid.GetAt(cx, cy);
            if (other == null || zone.Grid.MergeDetached(piece, other) <= 0) return;

            if (piece.Count <= 0) DiscardHeldPiece();
            else if (_views.TryGetValue(piece, out var view)) ApplyLabelAndCount(view); // leftovers stay in the hand
        }

        /// <summary>Cancelled or closed while holding a piece: it pours back into the stack it came from.</summary>
        private void ReturnHeldPiece()
        {
            var piece = _held;
            var source = _splitSource;
            var grid = inventory.GridFor(piece.Definition.Category);

            if (source != null && grid.Contains(source)) grid.MergeDetached(piece, source);

            var rest = piece.Count;
            var def = piece.Definition;
            DiscardHeldPiece();

            // Should not happen (the source always has room), but never lose items. Grid.Add raises no pickup event.
            if (rest > 0) grid.Add(def, rest);
        }

        private void DiscardHeldPiece()
        {
            var piece = _held;
            _held = null;
            _heldDetached = false;
            _splitSource = null;

            if (piece != null && _views.TryGetValue(piece, out var view))
            {
                Destroy(view.Root.gameObject);
                _views.Remove(piece);
            }
        }

        private void Drop(InventoryZone zone, Vector3 local, bool overCell, int cx, int cy)
        {
            var stack = _held;

            // The wrong compartment: the ghost is red there, and nothing happens.
            if (zone.Category != stack.Definition.Category) return;

            if (_heldDetached)
            {
                DropDetached(zone, local, overCell, cx, cy);
                return;
            }

            var w = _heldRotated ? stack.Definition.Height : stack.Definition.Width;
            var h = _heldRotated ? stack.Definition.Width : stack.Definition.Height;
            zone.AnchorFromPoint(local, w, h, out var ax, out var ay);

            if (zone.Grid.TryMove(stack, ax, ay, _heldRotated))
            {
                ReleaseHeld();
                return;
            }

            // Not free: if the cell under the cursor holds the same stackable item, pour into it.
            if (!overCell) return;

            var other = zone.Grid.GetAt(cx, cy);
            if (other != null && zone.Grid.TryMerge(stack, other))
            {
                // A partial merge leaves the stack alive with fewer items: send it back to its cells.
                ReleaseHeld(stack);
            }
        }

        /// <summary>
        /// Right click while holding. The held stack never left its grid (it only moves on a successful drop),
        /// so putting it back just forgets the pending move and the rotation.
        /// </summary>
        private void CancelHold() => ReleaseHeld();

        private void ReleaseHeld() => ReleaseHeld(_held);

        private void ReleaseHeld(ItemStack stack)
        {
            if (_heldDetached && stack == _held)
            {
                ReturnHeldPiece();
                return;
            }

            _held = null;

            if (stack != null && _views.TryGetValue(stack, out var view))
            {
                view.Easing = true; // glide from the cursor to the cells
                ApplyLabelAndCount(view);
            }
        }

        // ---------- the 3D items ----------

        /// <summary>Makes the views match the grids: creates the new ones, removes the ones whose stack is gone.</summary>
        private void Sync()
        {
            foreach (var grid in inventory.Grids)
            {
                foreach (var stack in grid.Stacks)
                {
                    if (_views.TryGetValue(stack, out var existing))
                    {
                        ApplyLabelAndCount(existing);
                    }
                    else if (_zones.TryGetValue(stack.Definition.Category, out var zone))
                    {
                        var view = CreateView(stack, zone);
                        _views[stack] = view;
                        ApplyLabelAndCount(view);
                    }
                }
            }

            List<ItemStack> stale = null;
            foreach (var pair in _views)
            {
                if (!ContainsStack(pair.Key) && !(_heldDetached && pair.Key == _held)) // a held piece is on no grid yet
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
            foreach (var grid in inventory.Grids)
            {
                foreach (var s in grid.Stacks)
                {
                    if (s == stack) return true;
                }
            }

            return false;
        }

        private void RefreshCounts()
        {
            foreach (var view in _views.Values) ApplyLabelAndCount(view);
        }

        /// <summary>Updates the stack count text and its corner, which depend on the count and the rotation.</summary>
        private void ApplyLabelAndCount(ItemView v)
        {
            var s = v.Stack;
            var rotated = s == _held ? _heldRotated : s.Rotated;
            var w = rotated ? s.Definition.Height : s.Definition.Width;
            var h = rotated ? s.Definition.Width : s.Definition.Height;
            var cell = v.Zone.CellSize;

            var showCount = _countsOn && s.Count > 1;
            v.Label.gameObject.SetActive(showCount);
            if (!showCount) return;

            v.LabelText.text = s.Count.ToString();

            // Lower right corner of the footprint, in the root's own (unrotated) space.
            v.Label.localPosition = new Vector3(w * cell * 0.5f - cell * 0.1f, countLift, -h * cell * 0.5f + cell * 0.1f);
        }

        /// <summary>
        /// Moves every item to where it belongs, in world space, every frame. Resting items are glued to their zone,
        /// so they follow the lid while it swings. An item that was just dropped or put back glides there first.
        /// </summary>
        private void AnimateViews()
        {
            var t = 1f - Mathf.Exp(-moveSharpness * Time.unscaledDeltaTime); // unscaled: the game is paused

            foreach (var view in _views.Values)
            {
                var s = view.Stack;
                var held = s == _held;
                var zone = held && _heldZone ? _heldZone : view.Zone;
                var rotated = held ? _heldRotated : s.Rotated;

                Vector3 targetPos;
                if (held)
                {
                    targetPos = _heldWorld;
                }
                else
                {
                    var w = rotated ? s.Definition.Height : s.Definition.Width;
                    var h = rotated ? s.Definition.Width : s.Definition.Height;
                    targetPos = view.Zone.transform.TransformPoint(view.Zone.FootprintCenter(s.X, s.Y, w, h, restLift));
                }

                var targetRot = zone.transform.rotation;

                if (held || view.Easing)
                {
                    view.Root.position = Vector3.Lerp(view.Root.position, targetPos, t);
                    view.Root.rotation = Quaternion.Slerp(view.Root.rotation, targetRot, t);

                    if (!held && (view.Root.position - targetPos).sqrMagnitude < 1e-8f) view.Easing = false;
                }
                else
                {
                    view.Root.SetPositionAndRotation(targetPos, targetRot);
                }

                // The zone may sit under a scaled object (the lid), so the item takes on that scale.
                view.Root.localScale = zone.transform.lossyScale;

                var pivotRot = Quaternion.Euler(0f, rotated ? 90f : 0f, 0f);
                view.Pivot.localRotation = Quaternion.Slerp(view.Pivot.localRotation, pivotRot, t);
            }
        }

        private ItemView CreateView(ItemStack stack, InventoryZone zone)
        {
            var def = stack.Definition;

            var root = new GameObject(def.DisplayName).transform;
            root.SetParent(rig.transform, false);

            var pivot = new GameObject("Pivot").transform;
            pivot.SetParent(root, false);

            BuildModel(def, pivot, zone.CellSize);

            var labelGo = new GameObject("Count");
            labelGo.transform.SetParent(root, false);
            labelGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // lie flat, readable from above
            labelGo.transform.localScale = Vector3.one * countScale;

            var text = labelGo.AddComponent<TextMesh>();
            text.font = _font;
            text.fontSize = 100;
            text.characterSize = countTextSize;
            text.anchor = TextAnchor.LowerRight;
            text.alignment = TextAlignment.Right;
            text.color = Color.white;
            labelGo.GetComponent<MeshRenderer>().sharedMaterial = _font.material;

            SuitcaseRig.SetLayerRecursive(root.gameObject, rig.Layer);

            // Start in place: no glide from the middle of nowhere.
            var w = stack.Width;
            var h = stack.Height;
            root.SetPositionAndRotation(
                zone.transform.TransformPoint(zone.FootprintCenter(stack.X, stack.Y, w, h, restLift)),
                zone.transform.rotation);
            pivot.localRotation = Quaternion.Euler(0f, stack.Rotated ? 90f : 0f, 0f);

            return new ItemView { Stack = stack, Zone = zone, Root = root, Pivot = pivot, Label = labelGo.transform, LabelText = text };
        }

        /// <summary>
        /// Puts the item's model under the pivot, scaled to fill its footprint. The scale is worked out on a copy at
        /// the world origin, so the suitcase's own position and rotation can never skew the measurement.
        /// </summary>
        private void BuildModel(ItemDefinition def, Transform pivot, float cellSize)
        {
            var targetW = def.Width * cellSize;
            var targetD = def.Height * cellSize;

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
            var props = new MaterialPropertyBlock();
            props.SetColor("_BaseColor", tint); // URP Lit and Unlit
            props.SetColor("_Color", tint);     // built in shaders
            rend.SetPropertyBlock(props);
        }

        // ---------- the placement ghost ----------

        private void BuildGhost()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());

            _ghost = quad.transform;
            _ghost.SetParent(rig.transform, false);

            _ghostMaterial = ghostMaterial
                ? new Material(ghostMaterial)
                : new Material(Shader.Find("Sprites/Default"));

            quad.GetComponent<MeshRenderer>().sharedMaterial = _ghostMaterial;
            SuitcaseRig.SetLayerRecursive(quad, rig.Layer);
            quad.SetActive(false);
        }

        private void UpdateGhostAndInfo(InventoryZone zone, Vector3 local, bool overCell, int cx, int cy)
        {
            var shown = _held ?? (zone && overCell ? zone.Grid.GetAt(cx, cy) : null);

            if (hud)
            {
                if (shown != null)
                {
                    var def = shown.Definition;
                    var title = def.DisplayName + (shown.Count > 1 ? "  x" + shown.Count : "");
                    var body = def.Description + (def.Effect ? "\n<color=#8fbf8f>Right click: use</color>" : "");
                    hud.ShowInfo(title, body);
                }
                else if (_hotspot)
                {
                    hud.ShowInfo(_hotspot.Label, "");
                }
                else
                {
                    hud.ShowInfo("", "");
                }
            }

            if (_held == null || !zone)
            {
                _ghost.gameObject.SetActive(false);
                return;
            }

            var w = _heldRotated ? _held.Definition.Height : _held.Definition.Width;
            var h = _heldRotated ? _held.Definition.Width : _held.Definition.Height;
            zone.AnchorFromPoint(local, w, h, out var ax, out var ay);

            var rightCompartment = zone.Category == _held.Definition.Category;
            var fits = rightCompartment && zone.Grid.CanPlace(_held.Definition, ax, ay, _heldRotated, _held);
            var mergeable = rightCompartment && !fits && overCell && zone.Grid.GetAt(cx, cy) is { } o && o != _held
                            && o.Definition == _held.Definition && o.Count < o.Definition.MaxStack;

            _ghostMaterial.color = fits || mergeable ? GhostOk : GhostBad;

            var zt = zone.transform;
            _ghost.gameObject.SetActive(true);

            // A quad faces -Z, so turning it 90 degrees about X lays it face up on the zone's surface.
            _ghost.SetPositionAndRotation(
                zt.TransformPoint(zone.FootprintCenter(ax, ay, w, h, 0.002f)),
                zt.rotation * Quaternion.Euler(90f, 0f, 0f));
            _ghost.localScale = new Vector3(w * zone.CellSize * zt.lossyScale.x, h * zone.CellSize * zt.lossyScale.z, 1f);
        }
    }
}