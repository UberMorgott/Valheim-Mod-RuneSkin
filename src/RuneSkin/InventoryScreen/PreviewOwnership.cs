using System.Collections.Generic;

namespace RuneSkin.InventoryScreen
{
    // Which runtime materials / meshes the character preview's clone owns (CharacterPreview). Baseline = the instance ids
    // of every material and mesh that existed right before the clone was built (assets, the real player's instances,
    // the world's). Every material / mesh later seen on the clone's renderers that is not in the baseline was created
    // for the clone - Renderer.material(s) / MeshFilter.mesh copies by VisEquipment or item scripts, new Material(...)
    // copies whatever their name - and is the clone's to destroy: Unity never frees them with the GameObject. Ids are
    // never reused within a session. Engine-internal (DontSave) objects are never claimed. Managed-only (unit tested).
    public sealed class PreviewOwnership
    {
        private readonly HashSet<int> _baseline = new HashSet<int>();
        private readonly HashSet<int> _owned = new HashSet<int>();

        public bool HasBaseline { get; private set; }
        public int Owned => _owned.Count;

        public void SetBaseline(IEnumerable<int> ids)
        {
            _baseline.Clear();
            _owned.Clear();
            foreach (var id in ids) _baseline.Add(id);
            HasBaseline = true;
        }

        // True the first time a runtime object (not in the baseline, not engine-internal) is seen: the caller owns it.
        public bool Claim(int id, bool engineInternal)
        {
            if (!HasBaseline || engineInternal || _baseline.Contains(id)) return false;
            return _owned.Add(id);
        }

        public bool IsOwned(int id) => _owned.Contains(id);

        public void Reset()
        {
            _baseline.Clear();
            _owned.Clear();
            HasBaseline = false;
        }
    }
}
