using UnityEngine;

namespace Gameplay
{
    /// <summary>
    /// Something the player can use with the Interact key by looking at it (a door, a lever, a terminal).
    /// PickupInteractor finds it with its ray, shows Prompt on the HUD and calls Interact when the key is pressed.
    /// Things that go into the suitcase use IPickupTarget instead.
    /// </summary>
    public interface IInteractTarget
    {
        /// <summary>What the HUD says while the player looks at it. Null or empty = no prompt.</summary>
        string Prompt { get; }

        /// <summary>The player pressed Interact. interactor is the player's camera (position and facing).</summary>
        void Interact(Transform interactor);
    }
}