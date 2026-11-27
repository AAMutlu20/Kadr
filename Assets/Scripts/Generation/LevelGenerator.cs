using System.Collections.Generic;
using System.Linq;
using Core;
using Events;
using Gameplay;
using Items;
using UnityEngine;

namespace Generation
{
    /// <summary>
    /// One doorway between two rooms: the parent node, the child node and the door leaf standing in it
    /// (null when no door prefab is assigned).
    /// </summary>
    public class RoomConnection
    {
        public int ParentNode;
        public int ChildNode;
        public Door Door;
    }

    /// <summary>
    /// Represents what the generator hands back (graph, seed, spawned rooms by node id and the doors between them).
    /// </summary>
    public class GeneratedLevel
    {
        public int Seed;
        public int Attempt;
        public LevelGraph Graph;
        public Transform Root;
        public readonly Dictionary<int, RoomInstance> Rooms = new Dictionary<int, RoomInstance>();
        public readonly List<RoomConnection> Connections = new List<RoomConnection>();

        /// <summary>The dead-end room that holds the flashlight (null if the level has no branch).</summary>
        public RoomInstance QuestRoom;

        /// <summary>The door a player walks through to enter this node from its parent (null for the start room).</summary>
        public Door DoorInto(int nodeId)
        {
            foreach (var connection in Connections)
            {
                if (connection.ChildNode == nodeId)
                {
                    return connection.Door;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Builds one level: graph -> tags -> room choice and physical placement (with backtracking) -> instantiate -> doors (unused doorways get a sealed one).
    /// Nothing is instantiated until a complete valid layout exists, so failed attempts cost no scene objects.
    /// NavMesh baking is not done here; hook it to the Generated event.
    /// </summary>
    public class LevelGenerator : MonoBehaviour
    {
        [Header("Rooms")]
        [SerializeField] private RoomDefinition startRoom;
        [SerializeField] private List<RoomDefinition> roomCatalog = new();

        [Header("Doors")]
        [Tooltip("A complete door (frame and leaf) spawned once per connection, on the socket point where two rooms meet. " +
                 "The room prefabs only have a bare opening. The Door component sits on the hinge object inside the prefab. Optional while testing.")]
        [SerializeField] private GameObject doorPrefab;
        [Tooltip("World height the door prefab's pivot is placed at. The socket's own height is ignored.")]
        [SerializeField] private float doorY = 5.1f;
        [Tooltip("Shown when the player tries a sealed door: a doorway that leads nowhere (a dead end). It uses the same door prefab, locked for good.")]
        [SerializeField] private string sealedMessage = "It won't open.";
        [Tooltip("Locks every door when the level is built. Door.UnlockGroup(Door Group) opens them again (floor 2: the breaker).")]
        [SerializeField] private bool lockDoors;
        [SerializeField] private string doorGroup = "Level";

        [Header("Quest items")]
        [Tooltip("The flashlight, as an ItemPickup prefab. Placed in the dead-end room, at its QuestItemSpot.")]
        [SerializeField] private GameObject flashlightPickupPrefab;

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

        public GeneratedLevel Current { get; private set; }

        // Search state, valid only while Generate runs.
        private class Placement
        {
            public RoomDefinition Def;
            public Vector3 Position;
            public Quaternion Rotation;
            public Bounds Box;
            public bool[] Used;
            public int ParentSocket; // which socket of the parent room this room is attached to
            public int OwnSocket;    // which of its own sockets faces the parent
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
                    if (GameServices.TryGet(out EventBus bus))
                    {
                        bus.Publish(new LevelGeneratedEvent(Current));
                    }
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
                            Used = new bool[prefab.SocketCount],
                            ParentSocket = parentSocket,
                            OwnSocket = childSocket
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

                // When the kit had no room for the tag, a fallback room was used (e.g. Ambush -> Combat prefab).
                // The room is what it physically is, so the node takes over its type and the spawner treats it as that.
                // Boss and Safe keep their tag: a fallback Safe room must not get enemies, nor must the boss room.
                var node = graph.Nodes[i];
                var actual = p.Def.EncounterType;
                if (actual != node.Encounter && node.Encounter != EncounterType.Boss && node.Encounter != EncounterType.Safe)
                {
                    Debug.Log($"[LevelGenerator] Node {i}: no {node.Encounter} room available, it became {actual}.");
                    node.Encounter = actual;
                }

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
                        SpawnSealedDoor(socket, roomObject.transform, i * 7 + s);
                    }
                }
            }

            // One door leaf per connection, after all rooms exist so both sides can be told about it.
            for (var i = 1; i < graph.Nodes.Count; i++)
            {
                var placement = _placed[i];
                var parentNode = graph.Nodes[i].Parent;
                var socket = level.Rooms[parentNode].Sockets[placement.ParentSocket];

                var door = SpawnDoor(socket, level.Root, i);
                level.Connections.Add(new RoomConnection { ParentNode = parentNode, ChildNode = i, Door = door });

                if (!door)
                {
                    continue;
                }

                level.Rooms[parentNode].AddDoor(door);
                level.Rooms[i].AddDoor(door);
            }

            SpawnQuestItem(level);

            return level;
        }

        /// <summary>Puts the flashlight in the dead-end room, at its QuestItemSpot (or the middle of the room without one).</summary>
        private void SpawnQuestItem(GeneratedLevel level)
        {
            var id = level.Graph.QuestRoomId;
            if (id < 0)
            {
                WarnOnce("This level has no dead-end branch, so the flashlight has no room. Set Branches Min to at least 1.");
                return;
            }

            level.QuestRoom = level.Rooms[id];

            if (!flashlightPickupPrefab)
            {
                WarnOnce("No flashlight pickup prefab assigned: the dead-end room stays empty.");
                return;
            }

            var room = level.QuestRoom;
            var spot = room.GetComponentInChildren<QuestItemSpot>();

            Vector3 position;
            Quaternion rotation;
            if (spot)
            {
                position = spot.transform.position;
                rotation = spot.transform.rotation;
            }
            else
            {
                WarnOnce($"{room.name} has no QuestItemSpot: the flashlight is placed in the middle of the room.");
                var bounds = room.Bounds.bounds;
                position = new Vector3(bounds.center.x, bounds.min.y + 0.1f, bounds.center.z);
                rotation = Quaternion.identity;
            }

            Instantiate(flashlightPickupPrefab, position, rotation, room.transform);
        }

        /// <summary>
        /// Spawns the door prefab (frame and leaf) on the socket point. Both rooms' sockets sit on the same point, so one
        /// door serves both. Every other door is turned 180 degrees, which moves its hinge to the other side, so a row
        /// of rooms does not look copy-pasted.
        /// </summary>
        private Door SpawnDoor(Socket socket, Transform parent, int nodeId)
        {
            return SpawnDoorAt(socket, parent, nodeId, $"Door_{nodeId:00}");
        }

        private Door SpawnDoorAt(Socket socket, Transform parent, int variety, string objectName)
        {
            if (!doorPrefab)
            {
                WarnOnce("No door prefab assigned: connections are left as open doorways.");
                return null;
            }

            var rotation = Quaternion.LookRotation(socket.WorldForward, Vector3.up);
            if ((variety & 1) == 1)
            {
                rotation *= Quaternion.Euler(0f, 180f, 0f);
            }

            var position = socket.WorldPosition;
            position.y = doorY;

            var instance = Instantiate(doorPrefab, position, rotation, parent);
            instance.name = objectName;

            var door = instance.GetComponentInChildren<Door>(true);
            if (!door)
            {
                WarnOnce("The door prefab has no Door component (put it on the hinge object inside the prefab).");
                return null;
            }

            door.Configure(doorGroup, lockDoors);
            return door;
        }

        /// <summary>
        /// An unused doorway gets the same door prefab, locked for good: it shows "Locked" like any other locked door,
        /// but no unlock call (Door.UnlockGroup, the boss arena) ever opens it.
        /// </summary>
        private void SpawnSealedDoor(Socket socket, Transform parent, int variety)
        {
            var door = SpawnDoorAt(socket, parent, variety, $"SealedDoor_{variety:000}");
            if (door)
            {
                door.Seal(sealedMessage);
            }
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