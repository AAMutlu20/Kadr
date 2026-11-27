using System;
using System.Collections.Generic;
using Core;
using Events;
using Generation;
using UnityEngine;

namespace Gameplay
{
    /// <summary>
    /// Counts what happens during one run (time, kills, photos, XP, rooms entered) and turns it into a RunSummary at the
    /// moment of death. Plain class: DeathFlow owns it, creates a new one for every run and calls Tick() each frame.
    /// </summary>
    public sealed class RunTracker : IDisposable
    {
        private readonly Transform _player;
        private readonly LevelGenerator _generator;
        private readonly EventBus _bus;
        private readonly HashSet<int> _visited = new();

        private readonly float _startTime;
        private GeneratedLevel _level;

        public int Kills { get; private set; }
        public int PhotosTaken { get; private set; }
        public int PhotosKept { get; private set; }
        public int XpGained { get; private set; }

        public RunTracker(Transform player, LevelGenerator generator)
        {
            _player = player;
            _generator = generator;
            _startTime = Time.time;

            if (GameServices.TryGet(out EventBus bus))
            {
                _bus = bus;
                _bus.Subscribe<EnemyKilledEvent>(OnKilled);
                _bus.Subscribe<PhotoTakenEvent>(OnPhoto);
                _bus.Subscribe<XpChangedEvent>(OnXp);
            }
        }

        public void Dispose()
        {
            if (_bus == null) return;

            _bus.Unsubscribe<EnemyKilledEvent>(OnKilled);
            _bus.Unsubscribe<PhotoTakenEvent>(OnPhoto);
            _bus.Unsubscribe<XpChangedEvent>(OnXp);
        }

        private void OnKilled(EnemyKilledEvent e) => Kills++;

        private void OnPhoto(PhotoTakenEvent e)
        {
            PhotosTaken++;
            if (e.Kept) PhotosKept++;
        }

        private void OnXp(XpChangedEvent e)
        {
            if (e.Delta > 0) XpGained += e.Delta; // spending a point is not a loss of progress here
        }

        /// <summary>Notes which room the player is standing in. Cheap: a dozen box tests.</summary>
        public void Tick()
        {
            if (!_generator || !_player) return;

            _level = _generator.Current;
            if (_level == null) return;

            var id = RoomAt(_player.position);
            if (id >= 0) _visited.Add(id);
        }

        private int RoomAt(Vector3 point)
        {
            foreach (var pair in _level.Rooms)
            {
                if (!pair.Value) continue;

                var box = pair.Value.GetWorldBox();
                var local = Quaternion.Inverse(box.Rotation) * (point - box.Center);
                if (Mathf.Abs(local.x) <= box.HalfExtents.x
                    && Mathf.Abs(local.y) <= box.HalfExtents.y
                    && Mathf.Abs(local.z) <= box.HalfExtents.z)
                {
                    return pair.Key;
                }
            }

            return -1;
        }

        /// <summary>Fills in the parts of the summary that this tracker knows. DeathFlow adds the rest.</summary>
        public void FillSummary(RunSummary summary)
        {
            Tick();

            summary.Seconds = Time.time - _startTime;
            summary.Kills = Kills;
            summary.PhotosTaken = PhotosTaken;
            summary.PhotosKept = PhotosKept;
            summary.XpGained = XpGained;
            summary.RoomsVisited = _visited.Count;

            BuildRoute(summary);
        }

        // The level graph as a left-to-right tree: depth is X, and the leaves are spread over Y. Every room sits on the
        // row of its critical child, so the way to the boss stays one straight line with the branches hanging off it.
        private void BuildRoute(RunSummary summary)
        {
            summary.Route.Clear();
            summary.RoomsTotal = 0;

            var graph = _level?.Graph;
            if (graph == null || graph.Nodes.Count == 0) return;

            var deathRoom = _player ? RoomAt(_player.position) : -1;
            var nodes = new Dictionary<int, RouteNode>();
            foreach (var node in graph.Nodes)
            {
                nodes[node.Id] = new RouteNode
                {
                    Id = node.Id,
                    Parent = node.Parent,
                    OnCriticalPath = node.OnCriticalPath,
                    IsBoss = node.Id == graph.BossId,
                    Visited = _visited.Contains(node.Id),
                    DeathRoom = node.Id == deathRoom
                };
            }

            var rows = new Dictionary<int, float>();
            var nextRow = 0;
            var maxDepth = 0;
            var depths = new Dictionary<int, int>();
            Layout(graph, LevelGraph.StartId, 0, rows, depths, ref nextRow, ref maxDepth);

            foreach (var node in graph.Nodes)
            {
                var route = nodes[node.Id];
                route.X = maxDepth > 0 ? depths[node.Id] / (float)maxDepth : 0.5f;
                route.Y = nextRow > 1 ? rows[node.Id] / (nextRow - 1f) : 0.5f;
                summary.Route.Add(route);
            }

            summary.RoomsTotal = graph.Nodes.Count;
        }

        private static void Layout(LevelGraph graph, int id, int depth, Dictionary<int, float> rows,
            Dictionary<int, int> depths, ref int nextRow, ref int maxDepth)
        {
            var node = graph.Nodes[id];
            depths[id] = depth;
            if (depth > maxDepth) maxDepth = depth;

            if (node.Children.Count == 0)
            {
                rows[id] = nextRow++;
                return;
            }

            // Critical child first, so it takes the top row of its subtree.
            var children = new List<int>(node.Children);
            children.Sort((a, b) => graph.Nodes[b].OnCriticalPath.CompareTo(graph.Nodes[a].OnCriticalPath));

            foreach (var child in children) Layout(graph, child, depth + 1, rows, depths, ref nextRow, ref maxDepth);

            rows[id] = rows[children[0]];
        }
    }
}