using System;
using System.Collections.Generic;
using UnityEngine;

namespace Photography
{
    /// <summary>
    /// The photos of one run. Plain model, no scene objects: the camera adds to it, the overlay will read it.
    ///
    /// Rules:
    ///  - The best 12 photos are paid (pinned photos first, then by score). The rest are "extra".
    ///  - It holds at most 24 photos. When it is full, the lowest scoring UNPINNED photo is dropped.
    ///  - Repeat penalty: every photo that contains an enemy type counts as one shot of that type. The next photo
    ///    of that type is worth 1, 0.5, 0.25, then 0.1 of its normal score. The counter never goes back down,
    ///    even if the earlier photo was discarded.
    /// </summary>
    public sealed class PhotoAlbum
    {
        public const int PaidSlots = 12;
        public const int ExtraSlots = 12;

        /// <summary>Value of the 1st, 2nd, 3rd... photo of one type. The last entry holds for every later photo. Upgrades change it.</summary>
        public float[] RepeatTable { get; set; } = { 1f, 0.5f, 0.25f, 0.1f };

        private readonly List<PhotoData> _photos = new();
        private readonly Dictionary<string, int> _shotsOfType = new();
        private int _shotsTaken;

        public IReadOnlyList<PhotoData> Photos => _photos;

        /// <summary>For saving: how many photos of each subject type were taken this run.</summary>
        public IReadOnlyDictionary<string, int> ShotsOfType => _shotsOfType;

        /// <summary>For saving: how many photos were taken this run (the next photo number).</summary>
        public int ShotsTaken => _shotsTaken;

        /// <summary>For loading a save: replaces the album with these photos.</summary>
        public void Restore(IEnumerable<PhotoData> photos, IReadOnlyDictionary<string, int> shots, int shotsTaken)
        {
            foreach (var photo in _photos)
            {
                if (photo.Image) UnityEngine.Object.Destroy(photo.Image);
            }

            _photos.Clear();
            _shotsOfType.Clear();

            if (photos != null) _photos.AddRange(photos);
            if (shots != null)
            {
                foreach (var pair in shots) _shotsOfType[pair.Key] = pair.Value;
            }

            _shotsTaken = Mathf.Max(0, shotsTaken);
            Changed?.Invoke();
        }

        /// <summary>Raised after the album changed (a photo kept, dropped, pinned or discarded).</summary>
        public event Action Changed;

        /// <summary>What a photo of this type is worth now, as a fraction of its normal score.</summary>
        public float RepeatMultiplier(string subjectId)
        {
            _shotsOfType.TryGetValue(subjectId, out var shots);
            if (RepeatTable == null || RepeatTable.Length == 0) return 1f;
            return RepeatTable[Mathf.Min(shots, RepeatTable.Length - 1)];
        }

        /// <summary>
        /// Records a shot. Photos with no score are not kept. Returns true if the photo is in the album afterwards
        /// (false if it scored nothing, or was the lowest of a full album).
        /// </summary>
        public bool RegisterShot(PhotoData photo)
        {
            photo.Number = ++_shotsTaken;

            var counted = new HashSet<string>();
            foreach (var subject in photo.Subjects)
            {
                if (!counted.Add(subject.Id)) continue; // two zombies in one photo are one shot of "zombie"
                _shotsOfType.TryGetValue(subject.Id, out var shots);
                _shotsOfType[subject.Id] = shots + 1;
            }

            if (photo.Score <= 0f) return false;

            _photos.Add(photo);
            while (_photos.Count > PaidSlots + ExtraSlots) DropWorstUnpinned();

            Changed?.Invoke();
            return _photos.Contains(photo);
        }

        public int PinnedCount
        {
            get
            {
                var count = 0;
                foreach (var photo in _photos)
                {
                    if (photo.Pinned) count++;
                }

                return count;
            }
        }

        /// <summary>A new run: deletes every photo (and its picture) and forgets the repeat penalties.</summary>
        public void Reset()
        {
            foreach (var photo in _photos)
            {
                if (photo.Image) UnityEngine.Object.Destroy(photo.Image);
            }

            _photos.Clear();
            _shotsOfType.Clear();
            _shotsTaken = 0;
            Changed?.Invoke();
        }

        public bool Contains(PhotoData photo) => _photos.Contains(photo);

        /// <summary>Locks a photo into the paid 12. At most 12 photos can be pinned.</summary>
        public bool Pin(PhotoData photo)
        {
            if (photo == null || photo.Pinned || !_photos.Contains(photo) || PinnedCount >= PaidSlots) return false;

            photo.Pinned = true;
            Changed?.Invoke();
            return true;
        }

        public void Unpin(PhotoData photo)
        {
            if (photo == null || !photo.Pinned) return;

            photo.Pinned = false;
            Changed?.Invoke();
        }

        /// <summary>Deletes a photo for good. The repeat penalty for its subjects stays.</summary>
        public void Discard(PhotoData photo)
        {
            if (photo == null || !_photos.Remove(photo)) return;

            if (photo.Image) UnityEngine.Object.Destroy(photo.Image);
            Changed?.Invoke();
        }

        /// <summary>The photos that count: pinned ones first, then the best of the rest, at most 12.</summary>
        public List<PhotoData> GetPaid()
        {
            var sorted = new List<PhotoData>(_photos);
            sorted.Sort(CompareForPayment);
            if (sorted.Count > PaidSlots) sorted.RemoveRange(PaidSlots, sorted.Count - PaidSlots);
            return sorted;
        }

        public bool IsPaid(PhotoData photo) => GetPaid().Contains(photo);

        public float EstimatedPayout()
        {
            var total = 0f;
            foreach (var photo in GetPaid()) total += photo.Score;
            return total;
        }

        private void DropWorstUnpinned()
        {
            PhotoData worst = null;
            foreach (var photo in _photos)
            {
                if (photo.Pinned) continue;
                if (worst == null || photo.Score < worst.Score) worst = photo;
            }

            // Only possible if every photo were pinned, and pinning is capped at 12.
            if (worst == null) worst = _photos[0];

            _photos.Remove(worst);
            if (worst.Image) UnityEngine.Object.Destroy(worst.Image);
        }

        private static int CompareForPayment(PhotoData a, PhotoData b)
        {
            if (a.Pinned != b.Pinned) return a.Pinned ? -1 : 1;
            return b.Score.CompareTo(a.Score);
        }
    }
}