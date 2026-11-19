using Photography;

namespace Events
{
    /// <summary>
    /// A photo was taken. Listeners: the viewfinder (flash and score popup), quests ("photograph a crawler" reads
    /// the subject ids), later the album overlay. Kept = it is in the album (false if it scored nothing, or was the
    /// lowest of a full album).
    /// </summary>
    public readonly struct PhotoTakenEvent : IGameEvent
    {
        public readonly PhotoData Photo;
        public readonly bool Kept;

        public PhotoTakenEvent(PhotoData photo, bool kept)
        {
            Photo = photo;
            Kept = kept;
        }
    }
}