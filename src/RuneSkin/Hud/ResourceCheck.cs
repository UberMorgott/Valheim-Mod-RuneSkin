using RuneSkin.API;

namespace RuneSkin.PlayerHud
{
    // Provider snapshot sanity (ApiVersion 2 contract on ResourceState): anything NaN, negative or inconsistent
    // hides the extension for that frame. Pure (unit-tested).
    public static class ResourceCheck
    {
        private const float Tolerance = 0.01f;

        public static bool IsSane(in ResourceState s, bool usesCurrent)
        {
            if (!s.Valid) return false;
            if (!Finite(s.Max) || !Finite(s.Available) || !Finite(s.ReservedAuras) || !Finite(s.ReservedSummons) ||
                !Finite(s.PhaseSeconds))
                return false;
            if (s.Max < 0f || s.Available < 0f || s.ReservedAuras < 0f || s.ReservedSummons < 0f || s.PhaseSeconds < 0f)
                return false;
            if (s.Available > s.Max + Tolerance) return false;
            if (s.ReservedAuras + s.ReservedSummons > s.Max - s.Available + Tolerance + 0.001f * s.Max) return false;
            if (usesCurrent && (!Finite(s.Current) || s.Current < 0f || s.Current > s.Max + Tolerance)) return false;
            return true;
        }

        public static bool IsSane(in CapacityState c) => c.Valid && c.Count >= 0 && c.Count <= 64;

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
