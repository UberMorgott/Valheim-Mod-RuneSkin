using TMPro;
using UnityEngine;

namespace RuneSkin.Restyle
{
    // Marker: this text's size and box are set by its owner (a layout that fits it itself, e.g. the inventory side column
    // values). The generic text fitters (TextInset) keep off it - two fitters on one text alternate their writes and the
    // text blinks (user 2026-10-26: side column numbers; same root as the window title flicker).
    [DisallowMultipleComponent]
    public sealed class OwnFit : MonoBehaviour
    {
        public static void Claim(TMP_Text text)
        {
            if (!text || text.TryGetComponent<OwnFit>(out _)) return;
            if (text.TryGetComponent<TextInset>(out var inset)) inset.Revert();
            text.gameObject.AddComponent<OwnFit>().hideFlags = HideFlags.HideInInspector;
        }

        public static bool Owns(TMP_Text text) => text && text.TryGetComponent<OwnFit>(out _);
    }
}
