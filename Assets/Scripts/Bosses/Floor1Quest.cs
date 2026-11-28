using Core;
using Enemies;
using Events;
using UnityEngine;

namespace Bosses
{
    /// <summary>
    /// The objectives of floor 1, driven by what happens (events), one scene object:
    ///
    ///   1  "Document the caged subject"            the main task. Set this text on the KadrHud (Start Title / Start Text) too,
    ///                                               so it is the first thing shown.
    ///   2  "Clear the floor (N left)"               starts when the player uses the terminal while threats are left.
    ///      then back to "Open the cage at the terminal".
    ///   3  "Photograph the subject"                 after the cage opens. Only a photo taken after that counts, so the
    ///                                               boss cannot be photographed through the bars to skip the fight.
    ///   4  "Reach the elevator"                     after the photo, until the fight ends.
    ///
    /// The photo is recognised by the subject id of the boss's Photographable (Boss Subject Id below).
    /// </summary>
    public class Floor1Quest : MonoBehaviour
    {
        [SerializeField] private BossArena arena;
        [Tooltip("The subject id on the boss's Photographable component.")]
        [SerializeField] private string bossSubjectId = "treehead";

        [Header("Main task")]
        [SerializeField] private string mainTitle = "OBJECTIVE 01";
        [SerializeField] private string mainText = "Document the caged subject";
        [SerializeField] private string openCageText = "Open the cage at the terminal";
        [SerializeField] private string photographText = "Photograph the subject";
        [SerializeField] private string escapeText = "Reach the elevator";

        [Header("Clear the floor")]
        [SerializeField] private string clearTitle = "OBJECTIVE 02";
        [SerializeField] private string clearText = "Clear the floor";
        [SerializeField] private string clearDoneText = "Floor clear";

        private EventBus _bus;
        private bool _clearActive;
        private bool _cageOpen;
        private bool _photoDone;
        private bool _cleared;
        private bool _escaped;
        private int _lastCount = -1;
        private float _nextPoll;

        private void OnEnable()
        {
            if (!GameServices.TryGet(out _bus)) return;

            _bus.Subscribe<CageBlockedEvent>(OnBlocked);
            _bus.Subscribe<CageOpenedEvent>(OnCageOpened);
            _bus.Subscribe<PhotoTakenEvent>(OnPhoto);
            _bus.Subscribe<BossEncounterEvent>(OnEncounter);
        }

        private void OnDisable()
        {
            if (_bus == null) return;

            _bus.Unsubscribe<CageBlockedEvent>(OnBlocked);
            _bus.Unsubscribe<CageOpenedEvent>(OnCageOpened);
            _bus.Unsubscribe<PhotoTakenEvent>(OnPhoto);
            _bus.Unsubscribe<BossEncounterEvent>(OnEncounter);
        }

        private void Update()
        {
            if (!_clearActive || Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 0.5f;

            var count = ThreatTracker.Count;
            if (count == _lastCount) return;
            _lastCount = count;

            if (count > 0)
            {
                _bus.Publish(new ObjectiveEvent(clearTitle, $"{clearText} ({count} left)"));
                return;
            }

            // The floor is clear: close the side task, point back at the terminal.
            _clearActive = false;
            _cleared = true;
            _bus.Publish(new ObjectiveEvent(clearTitle, clearDoneText, true));
            _bus.Publish(new ObjectiveEvent(mainTitle, openCageText));
        }

        /// <summary>For saving: where the objectives are.</summary>
        public void Export(out bool clearActive, out bool cleared, out bool cageOpen, out bool photoDone, out bool escaped)
        {
            clearActive = _clearActive;
            cleared = _cleared;
            cageOpen = _cageOpen;
            photoDone = _photoDone;
            escaped = _escaped;
        }

        /// <summary>
        /// For loading a save: sets the flags and publishes the objectives again, in order, so the HUD and the suitcase's
        /// orders note show where the player was.
        /// </summary>
        public void Import(bool clearActive, bool cleared, bool cageOpen, bool photoDone, bool escaped)
        {
            _clearActive = clearActive;
            _cleared = cleared;
            _cageOpen = cageOpen;
            _photoDone = photoDone;
            _escaped = escaped;
            _lastCount = -1;
            _nextPoll = 0f;

            if (_bus == null) return;

            if (cleared) _bus.Publish(new ObjectiveEvent(clearTitle, clearDoneText, true));
            else if (clearActive)
            {
                _lastCount = ThreatTracker.Count;
                _bus.Publish(new ObjectiveEvent(clearTitle, $"{clearText} ({_lastCount} left)"));
            }

            if (photoDone)
            {
                _bus.Publish(new ObjectiveEvent(mainTitle, mainText, true));
                _bus.Publish(new ObjectiveEvent(clearTitle, escapeText, escaped));
            }
            else if (cageOpen) _bus.Publish(new ObjectiveEvent(mainTitle, photographText));
            else if (cleared) _bus.Publish(new ObjectiveEvent(mainTitle, openCageText));
        }

        private void OnBlocked(CageBlockedEvent e)
        {
            if (_clearActive || _cageOpen) return;

            _clearActive = true;
            _lastCount = e.Threats;
            _nextPoll = Time.unscaledTime + 0.5f;
            _bus.Publish(new ObjectiveEvent(clearTitle, $"{clearText} ({e.Threats} left)"));
        }

        private void OnCageOpened(CageOpenedEvent e)
        {
            _cageOpen = true;
            _clearActive = false;
            _bus.Publish(new ObjectiveEvent(mainTitle, photographText));
        }

        private void OnPhoto(PhotoTakenEvent e)
        {
            if (_photoDone || !_cageOpen || e.Photo == null) return;
            if (arena && !arena.Started) return;

            foreach (var subject in e.Photo.Subjects)
            {
                if (subject.Id != bossSubjectId) continue;

                _photoDone = true;
                _bus.Publish(new ObjectiveEvent(mainTitle, mainText, true));
                _bus.Publish(new ObjectiveEvent(clearTitle, escapeText));
                return;
            }
        }

        private void OnEncounter(BossEncounterEvent e)
        {
            // The fight ended (the player reached the elevator): nothing left to show.
            if (!e.Started && _photoDone) _escaped = true;
            if (!e.Started && _photoDone) _bus.Publish(new ObjectiveEvent(clearTitle, escapeText, true));
        }
    }
}