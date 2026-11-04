using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Generation
{
    /// <summary>
    /// Represents what the generator hands back (graph, seed and spawned rooms by node id).
    /// </summary>
    public class GeneratedLevel
    {
        public int Seed;
        public int Attempt;
        public LevelGraph Graph;
        public Transform Root;
        public readonly Dictionary<int, RoomInstance> Rooms = new Dictionary<int, RoomInstance>();
    }

    /// <summary>
    /// Builds one level: graph -> tags -> room choice and physical placement (with backtracking) -> instantiate -> plugs.
    /// Nothing is instantiated until a complete valid layout exists, so failed attempts cost no scene objects.
    /// NavMesh baking is not done here; hook it to the Generated event.
    /// </summary>
    public class LevelGenerator : MonoBehaviour
    {
        [Header("Rooms")]
        [SerializeField] private RoomDefinition startRoom;
        [SerializeField] private List<RoomDefinition> roomCatalog = new();
        [Tooltip("5x5x1 slab with its origin on the socket point, extending local z -1 to 0. Optional while testing.")]
        [SerializeField] private GameObject plugPrefab;

        [Header("Level shape")]
        [SerializeField, Range(1, 3)] private int depth = 1;
        [SerializeField, Min(4)] private int pathLengthMin = 5;
        [SerializeField, Min(4)] private int pathLengthMax = 7;
        [SerializeField, Min(0)] private int branchesMin = 1;
        [SerializeField, Min(0)] private int branchesMax = 2;
        [SerializeField, Min(1)] private int branchLengthMax = 2;

        [Header("Seed and limits")]
        [SerializeField] private bool useFixedSeed;
        [SerializeField] private int fixedSeed = 12345;
        [SerializeField, Min(1)] private int maxGraphAttempts = 20;
        [SerializeField, Min(1)] private int maxPlacementTries = 5000;
        [SerializeField] private bool generateOnStart = true;

        /// <summary>
        /// Raised after a level is fully built. Later becomes a bus event.
        /// </summary>
        public event System.Action<GeneratedLevel> Generated;

        public GeneratedLevel Current { get; private set; }

        // Search state, valid only while Generate runs.
        private class Placement
        {
            public RoomDefinition Def;
            public Vector3 Position;
            public Quaternion Rotation;
            public Bounds Box;
            public bool[] Used;
        }

        private Placement[] _placed;
        private readonly List<Bounds> _boxes = new();
        private readonly HashSet<string> _warned = new();
        private int _tries;

        private void Start()
        {
            if (generateOnStart)
            {
                Generate();
            }
        }

        [ContextMenu("Generate")]
        public GeneratedLevel Generate()
        {
            Clear();
            _warned.Clear();

            if (!ValidateSetup())
            {
                return null;
            }

            var seed = useFixedSeed ? fixedSeed : System.Environment.TickCount;
            Debug.Log($"[LevelGenerator] Seed {seed}, depth {depth}");

            for (var attempt = 0; attempt < maxGraphAttempts; attempt++)
            {
                // Each attempt has its own deterministic stream, so seed + attempt number reproduces a level.
                var rng = new System.Random(unchecked(seed + attempt * 7919));

                var graph = LevelGraphBuilder.Build(
                    rng, pathLengthMin, pathLengthMax, branchesMin, branchesMax, branchLengthMax);
                EncounterTagger.Tag(graph, depth, rng);

                if (TryPlace(graph, rng))
                {
                    Current = Build(graph, seed, attempt);
                    Debug.Log($"[LevelGenerator] Success on attempt {attempt} " +
                              $"with {graph.Nodes.Count} rooms ({_tries} placement tries).");
                    LogLayout(Current);
                    Generated?.Invoke(Current);
                    return Current;
                }

                Debug.Log($"[LevelGenerator] Attempt {attempt} failed after {_tries} tries, rebuilding the graph.");
            }

            Debug.LogError($"[LevelGenerator] No valid layout after {maxGraphAttempts} attempts (seed {seed}). " +
                           "Check the catalog covers every encounter type and socket count, or raise the limits.");
            return null;
        }

        [ContextMenu("Clear")]
        public void Clear()
        {
            if (Current != null && Current.Root)
            {
                if (Application.isPlaying)
                {
                    Destroy(Current.Root.gameObject);
                }
                else
                {
                    DestroyImmediate(Current.Root.gameObject);
                }
            }

            Current = null;
        }

        // Placement search

        private bool TryPlace(LevelGraph graph, System.Random rng)
        {
            _placed = new Placement[graph.Nodes.Count];
            _boxes.Clear();
            _tries = 0;

            var startPrefab = startRoom.RoomPrefab;
            var root = new Placement
            {
                Def = startRoom,
                Position = Vector3.zero,
                Rotation = Quaternion.identity,
                Used = new bool[startPrefab.SocketCount]
            };
            root.Box = RoomSnap.GetPlacedBounds(startPrefab, root.Position, root.Rotation);

            _placed[0] = root;
            _boxes.Add(root.Box);

            return PlaceNode(graph, 1, rng);
        }

        /// <summary>
        /// Places node k, then recurses into k + 1. Returns false when no option for k leads to a complete layout,
        /// which makes the caller undo its own choice and try the next one (backtracking).
        /// </summary>
        private bool PlaceNode(LevelGraph graph, int k, System.Random rng)
        {
            if (k >= graph.Nodes.Count)
            {
                return true;
            }

            var node = graph.Nodes[k];
            var parent = _placed[node.Parent];
            var parentPrefab = parent.Def.RoomPrefab;

            var candidates = GetCandidates(node.Encounter, node.Degree, rng);
            var parentSockets = FreeSockets(parent);
            RngUtil.Shuffle(parentSockets, rng);

            foreach (var parentSocket in parentSockets)
            {
                RoomSnap.GetSocketWorldPose(parentPrefab, parentSocket, parent.Position, parent.Rotation,
                    out var parentPos, out var parentForward);

                foreach (var def in candidates)
                {
                    var prefab = def.RoomPrefab;

                    var childSockets = new List<int>();
                    for (var s = 0; s < prefab.SocketCount; s++)
                    {
                        childSockets.Add(s);
                    }
                    RngUtil.Shuffle(childSockets, rng);

                    foreach (var childSocket in childSockets)
                    {
                        if (_tries >= maxPlacementTries)
                        {
                            return false;
                        }

                        _tries++;

                        RoomSnap.ComputePlacement(parentPos, parentForward, prefab, childSocket,
                            out var position, out var rotation);
                        var box = RoomSnap.GetPlacedBounds(prefab, position, rotation);

                        if (RoomSnap.OverlapsAny(box, _boxes))
                        {
                            continue;
                        }

                        // Commit this choice.
                        var placement = new Placement
                        {
                            Def = def,
                            Position = position,
                            Rotation = rotation,
                            Box = box,
                            Used = new bool[prefab.SocketCount]
                        };
                        placement.Used[childSocket] = true;
                        parent.Used[parentSocket] = true;
                        _placed[k] = placement;
                        _boxes.Add(box);

                        if (PlaceNode(graph, k + 1, rng))
                        {
                            return true;
                        }

                        // Undo and try the next option.
                        _boxes.RemoveAt(_boxes.Count - 1);
                        _placed[k] = null;
                        parent.Used[parentSocket] = false;
                    }
                }
            }

            return false;
        }

        private static List<int> FreeSockets(Placement p)
        {
            var free = new List<int>();
            for (var i = 0; i < p.Used.Length; i++)
            {
                if (!p.Used[i])
                {
                    free.Add(i);
                }
            }

            return free;
        }

        // Room resolution

        // If the kit has no room for a tag (or for that many doorways), try these tags instead.
        private static EncounterType[] FallbackFor(EncounterType type)
        {
            return type switch
            {
                EncounterType.Boss or EncounterType.Elite or EncounterType.Ambush or EncounterType.Patrol => new[]
                {
                    EncounterType.Combat, EncounterType.Transit
                },
                EncounterType.Safe => new[] { EncounterType.Transit, EncounterType.Combat },
                EncounterType.Combat => new[] { EncounterType.Transit },
                _ => new[] { EncounterType.Combat }
            };
        }

        private List<RoomDefinition> GetCandidates(EncounterType type, int degree, System.Random rng)
        {
            var pool = Filter(type, degree);

            if (pool.Count != 0) return WeightedOrder(pool, rng);
            foreach (var fallback in FallbackFor(type))
            {
                pool = Filter(fallback, degree);
                if (pool.Count <= 0) continue;
                WarnOnce($"No {type} room with {degree}+ sockets on depth {depth}; using {fallback} rooms instead.");
                break;
            }

            return WeightedOrder(pool, rng);
        }

        private List<RoomDefinition> Filter(EncounterType type, int degree)
        {
            return roomCatalog.Where(def => def && def != startRoom && def.RoomPrefab 
                                            && def.RoomPrefab.Bounds).Where(def => def.EncounterType == type 
                && def.Weight > 0f && def.AllowedOnDepth(depth) && def.SocketCount >= degree).ToList();
        }

        // Weighted random order without replacement (key = u^(1/w), highest first).
        private static List<RoomDefinition> WeightedOrder(List<RoomDefinition> pool, System.Random rng)
        {
            var keyed =
                (from def in pool let key = System.Math.Pow(rng.NextDouble(), 1.0 / def.Weight) 
                    select new KeyValuePair<double, RoomDefinition>(key, def)).ToList();

            keyed.Sort((a, b) 
                => b.Key.CompareTo(a.Key));

            return keyed.Select(pair => pair.Value).ToList();
        }

        // Instantiate

        private GeneratedLevel Build(LevelGraph graph, int seed, int attempt)
        {
            var rootObject = new GameObject("GeneratedLevel");
            var level = new GeneratedLevel
            {
                Seed = seed,
                Attempt = attempt,
                Graph = graph,
                Root = rootObject.transform
            };

            for (var i = 0; i < graph.Nodes.Count; i++)
            {
                var p = _placed[i];

                var roomObject = Instantiate(p.Def.Prefab, p.Position, p.Rotation, level.Root);
                roomObject.name = $"{i:00}_{p.Def.name}_{graph.Nodes[i].Encounter}";

                var room = roomObject.GetComponent<RoomInstance>();
                level.Rooms[i] = room;

                for (var s = 0; s < room.SocketCount; s++)
                {
                    var socket = room.Sockets[s];
                    socket.IsConnected = p.Used[s];

                    if (!p.Used[s])
                    {
                        SpawnPlug(socket, roomObject.transform);
                    }
                }
            }

            return level;
        }

        private void SpawnPlug(Socket socket, Transform parent)
        {
            if (!plugPrefab)
            {
                WarnOnce("No plug prefab assigned: unused doorways are left open.");
                return;
            }

            var rotation = Quaternion.LookRotation(socket.WorldForward, Vector3.up);
            Instantiate(plugPrefab, socket.WorldPosition, rotation, parent);
        }

        // Checks and logging

        private bool ValidateSetup()
        {
            if (!startRoom || !startRoom.RoomPrefab || !startRoom.RoomPrefab.Bounds)
            {
                Debug.LogError("[LevelGenerator] Start room is missing, or its prefab has no RoomInstance / RoomBounds.", this);
                return false;
            }

            if (startRoom.SocketCount < 1)
            {
                Debug.LogError("[LevelGenerator] Start room has no sockets.", this);
                return false;
            }

            if (roomCatalog.Count == 0)
            {
                Debug.LogError("[LevelGenerator] Room catalog is empty.", this);
                return false;
            }

            foreach (var def in roomCatalog
                         .Where(def => def && (!def.RoomPrefab || !def.RoomPrefab.Bounds)))
            {
                Debug.LogError($"[LevelGenerator] {def.name}: prefab missing, has no RoomInstance, or has no RoomBounds.", def);
                return false;
            }

            return true;
        }

        private void WarnOnce(string message)
        {
            if (_warned.Add(message))
            {
                Debug.LogWarning($"[LevelGenerator] {message}", this);
            }
        }

        private static void LogLayout(GeneratedLevel level)
        {
            foreach (var node in level.Graph.Nodes)
            {
                var room = level.Rooms[node.Id];
                var parentText = node.Parent >= 0 ? node.Parent.ToString() : "-";
                var pathText = node.OnCriticalPath ? "path" : "branch";

                Debug.Log($"[LevelGenerator]   node {node.Id} ({pathText}, parent {parentText}): " +
                          $"{node.Encounter} -> {room.name} at {room.transform.position}, yaw {room.transform.eulerAngles.y:0}");
            }
        }
    }
}