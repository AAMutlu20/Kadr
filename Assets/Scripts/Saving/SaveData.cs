using System;
using System.Collections.Generic;

namespace Saving
{
    // The shapes that go into a save file (JsonUtility). Plain data, no Unity objects.

    [Serializable]
    public class StackSave
    {
        public string id;
        public int count;
        public int x;
        public int y;
        public bool rotated;
    }

    [Serializable]
    public class SubjectSave
    {
        public string id;
        public string name;
        public float rarity;
        public float danger;
        public float coverage;
        public float framing;
        public float state;
        public float special;
        public float repeat;
        public float score;
    }

    [Serializable]
    public class PhotoSave
    {
        public string file;
        public float score;
        public bool pinned;
        public int number;
        public List<SubjectSave> subjects = new();
    }

    [Serializable]
    public class ShotCount
    {
        public string id;
        public int shots;
    }

    /// <summary>What a slot keeps for good: everything that survives a death, plus what the player carries.</summary>
    [Serializable]
    public class ProgressSave
    {
        public int roubles;

        public int xpProgress;
        public int xpPoints;
        public int xpPointsEarned;
        public List<string> discovered = new();

        public int cameraBranch;
        public int cameraTier;
        public int cameraEquipped;
        public int[] abilityLevels = new int[5];

        public int floorsCompleted;
        public int deaths;

        public List<StackSave> consumables = new();
        public List<StackSave> permanent = new();
    }

    [Serializable]
    public class EnemySave
    {
        public string prefab;
        public float x;
        public float y;
        public float z;
        public float yaw;
        public float health;
    }

    [Serializable]
    public class DoorSave
    {
        public string key;
        public bool open;
        public bool locked;
        public float sign;
    }

    [Serializable]
    public class PickupSave
    {
        public string key;
        public int count;
    }

    [Serializable]
    public class CrateSave
    {
        public string key;
        public bool unlocked;
        public List<StackSave> stacks = new();
    }

    /// <summary>The boss fight and the cage: whether it started or ended, and where the boss stands.</summary>
    [Serializable]
    public class BossSave
    {
        public bool hasArena;
        public bool started;
        public bool ended;

        public bool hasBoss;
        public float x;
        public float y;
        public float z;
        public float yaw;
        public bool quadruped;

        public bool hasCage;
        public bool cageOpen;
    }

    [Serializable]
    public class QuestSave
    {
        public bool hasQuest;
        public bool clearActive;
        public bool cleared;
        public bool cageOpen;
        public bool photoDone;
        public bool escaped;
    }

    /// <summary>
    /// A telephone save: enough to put the player back in the same floor at the same spot. The floor is rebuilt from
    /// its seed. Enemies, doors and the boss start fresh.
    /// </summary>
    [Serializable]
    public class CheckpointSave
    {
        public string scene;
        public bool hasSeed;
        public int seed;

        public float x;
        public float y;
        public float z;
        public float yaw;
        public float health;

        public List<StackSave> consumables = new();
        public List<StackSave> permanent = new();

        public List<PhotoSave> photos = new();
        public List<ShotCount> shots = new();
        public int shotsTaken;

        public List<string> usedPhones = new();

        // The state of the floor. Saves from before this existed have hasWorld = false and load as a fresh floor.
        public bool hasWorld;
        public List<EnemySave> enemies = new();
        public List<DoorSave> doors = new();
        public List<PickupSave> pickups = new();
        public List<CrateSave> crates = new();
        public BossSave boss = new();
        public QuestSave quest = new();
    }

    [Serializable]
    public class SaveFile
    {
        public int version = 1;
        public string savedAt = "";
        public ProgressSave progress = new();
        public bool hasCheckpoint;
        public CheckpointSave checkpoint;
    }
}