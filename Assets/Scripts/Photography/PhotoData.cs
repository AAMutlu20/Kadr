using System.Collections.Generic;
using UnityEngine;

namespace Photography
{
    /// <summary>How one subject in a photo was scored. The overlay shows these as the breakdown.</summary>
    public sealed class PhotoSubjectScore
    {
        public string Id;
        public string Name;
        public float Rarity;
        public float Danger;
        public float Coverage;  // 0..1, the share of the subject that is in the photo and not behind a wall
        public float Framing;   // 0..1, size and centring
        public float State;     // 1 normally, more while it attacks
        public float Repeat;    // 1 for the first photo of this type in the run, then less
        public float Score;
    }

    /// <summary>One photo: the picture, what was in it and what it is worth.</summary>
    public sealed class PhotoData
    {
        public Texture2D Image;
        public readonly List<PhotoSubjectScore> Subjects = new();
        public float Score;
        public bool Pinned;
        public int Number; // 1 for the first photo of the run
    }
}