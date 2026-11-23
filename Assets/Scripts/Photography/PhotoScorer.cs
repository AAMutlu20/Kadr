using System;
using System.Collections.Generic;
using UnityEngine;

namespace Photography
{
    [Serializable]
    public class ScoringSettings
    {
        [Tooltip("Money for one common, harmless subject in a perfect frame.")]
        public float baseValue = 50f;
        [Tooltip("Subjects farther than this are not counted.")]
        public float maxDistance = 30f;
        [Tooltip("Below this share of the subject in the photo (and not behind a wall) it does not count at all.")]
        [Range(0f, 0.6f)] public float minCoverage = 0.15f;
        [Tooltip("The subject's height as a share of the frame height that counts as big enough. Smaller subjects score less.")]
        [Range(0.2f, 0.9f)] public float idealFill = 0.6f;
        [Tooltip("Multiplier while the subject is in its attack.")]
        public float attackBonus = 1.5f;
        [Tooltip("Extra worth per additional subject in the same photo, up to the maximum.")]
        public float groupBonusPerExtra = 0.1f;
        public float groupBonusMax = 0.3f;
        [Tooltip("Multiplier for a subject that is fully visible and well framed. 1 = no bonus (the Masterpiece upgrade raises it).")]
        public float perfectFramingBonus = 1f;
        [Tooltip("Multiplier for a subject that is on fire when photographed.")]
        public float burningBonus = 1.25f;
        [Tooltip("Multiplier for a subject that is turned against the others (disoriented).")]
        public float disorientedBonus = 1.5f;
    }

    /// <summary>
    /// Scores a shot. Runs once per photo, never per frame. The frame is a rectangle in viewport coordinates.
    ///
    /// Per subject: base value x rarity x danger x coverage x framing x state x repeat.
    ///  - Coverage is the share of the subject that is in the photo: all of it = 1, head and torso only = about 0.6.
    ///    It also drops for the parts hidden behind a wall. Linear, so half the enemy pays half.
    ///  - Framing is only about size and centring: a subject that is too small in the frame, or far from the middle
    ///    of what IS in the frame, scores less. Being cut off is already paid for by coverage, not punished twice.
    /// Several subjects add up, with a small bonus for the group. Light (a lit room) is not part of it yet.
    /// </summary>
    public static class PhotoScorer
    {
        private static readonly List<Vector3> Points = new();

        public static void Evaluate(Camera cam, Rect frame, LayerMask blockers, PhotoAlbum album,
            ScoringSettings settings, PhotoData into)
        {
            var origin = cam.transform.position;
            var total = 0f;

            foreach (var subject in Photographable.All)
            {
                if (!subject || !subject.CanBePhotographed) continue;

                var bounds = subject.WorldBounds;
                if (Vector3.Distance(origin, bounds.center) > settings.maxDistance) continue;

                // How much of the subject is in the frame (the area of its outline that falls inside it).
                if (!Measure(cam, frame, bounds, out var clipped, out var inFrame)) continue;

                // How much of the subject is not hidden behind a wall (head, chest, legs checked separately).
                subject.GetAnchorPoints(Points);
                if (Points.Count == 0) continue;

                var open = 0;
                foreach (var point in Points)
                {
                    if (IsOpen(origin, point, blockers, subject)) open++;
                }

                var coverage = inFrame * open / Points.Count;
                if (coverage < settings.minCoverage) continue;

                var framing = Framing(cam, frame, clipped, settings.idealFill);
                var state = subject.IsAttacking ? settings.attackBonus : 1f;
                var repeat = album.RepeatMultiplier(subject.SubjectId);

                var special = 1f;
                if (subject.IsBurning) special *= settings.burningBonus;
                if (subject.IsDisoriented) special *= settings.disorientedBonus;
                if (settings.perfectFramingBonus > 1f && coverage >= 0.97f && framing >= 0.85f) special *= settings.perfectFramingBonus;

                var score = settings.baseValue * subject.Rarity * subject.Danger * coverage * framing * state * special * repeat;

                into.Subjects.Add(new PhotoSubjectScore
                {
                    Id = subject.SubjectId, Name = subject.DisplayName, Rarity = subject.Rarity,
                    Danger = subject.Danger, Coverage = coverage, Framing = framing, State = state,
                    Special = special, Source = subject, Repeat = repeat, Score = score
                });
                total += score;
            }

            var extra = Mathf.Min(settings.groupBonusMax, settings.groupBonusPerExtra * Mathf.Max(0, into.Subjects.Count - 1));
            into.Score = Mathf.Round(total * (1f + extra));
        }

        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        /// <summary>
        /// True when nothing but the subject itself is between the camera and the point. The subject's own colliders must
        /// not count as a wall: the points sit inside its body, so a plain Linecast would hit the body and call it hidden.
        /// </summary>
        private static bool IsOpen(Vector3 origin, Vector3 point, LayerMask blockers, Photographable subject)
        {
            var delta = point - origin;
            var distance = delta.magnitude;
            if (distance < 0.01f) return true;

            var count = Physics.RaycastNonAlloc(origin, delta / distance, Hits, distance, blockers, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                if (Hits[i].collider.GetComponentInParent<Photographable>() != subject) return false;
            }

            return true;
        }

        /// <summary>
        /// Projects the subject's bounds onto the screen. 'clipped' is the part of that rectangle inside the frame,
        /// 'inFrame' the share of the whole rectangle that is inside (0..1). False if it is behind the camera or
        /// entirely outside the frame.
        /// </summary>
        private static bool Measure(Camera cam, Rect frame, Bounds bounds, out Rect clipped, out float inFrame)
        {
            clipped = default;
            inFrame = 0f;

            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);

            for (var i = 0; i < 8; i++)
            {
                var corner = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var vp = cam.WorldToViewportPoint(corner);

                if (vp.z <= 0f) return false; // so close that part of it is behind the lens

                min = Vector2.Min(min, vp);
                max = Vector2.Max(max, vp);
            }

            var full = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            clipped = Rect.MinMaxRect(
                Mathf.Max(full.xMin, frame.xMin), Mathf.Max(full.yMin, frame.yMin),
                Mathf.Min(full.xMax, frame.xMax), Mathf.Min(full.yMax, frame.yMax));

            var area = full.width * full.height;
            if (clipped.width <= 0f || clipped.height <= 0f || area <= 0f) return false;

            inFrame = clipped.width * clipped.height / area;
            return true;
        }

        /// <summary>0..1. Big enough in the frame, and near the middle of the visible part.</summary>
        private static float Framing(Camera cam, Rect frame, Rect clipped, float idealFill)
        {
            var fill = clipped.height / frame.height;
            var size = Mathf.Min(1f, fill / idealFill);

            // Distance from the middle of the frame, measured in frame heights so it is not stretched by the aspect.
            var aspect = (float)cam.pixelWidth / cam.pixelHeight;
            var offset = new Vector2((clipped.center.x - frame.center.x) * aspect, clipped.center.y - frame.center.y);
            var centre = 1f - Mathf.Clamp01(offset.magnitude / (frame.height * 0.5f));

            return Mathf.Max(0.05f, size * Mathf.Lerp(0.5f, 1f, centre));
        }
    }
}