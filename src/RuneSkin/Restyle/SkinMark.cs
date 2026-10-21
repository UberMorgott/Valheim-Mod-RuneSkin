using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RuneSkin.Restyle
{
    public enum Outcome
    {
        Styled,     // role applied
        Degraded,   // role known, its asset is missing -> left vanilla, reported
        Content,    // explained: content graphic kept as is (icons, bar fills)
        Skipped,    // explained: deliberately untouched (blur, gamepad map)
        Unknown     // unexplained: untouched + reported
    }

    // Per-graphic marker: the original classification (the vanilla sprite name is gone after the first restyle) and a
    // journal of exactly the properties RuneSkin wrote, with their original and last written values. Public fields are
    // serialized, so Instantiate copies the mark with its clone (clone of a styled template keeps the true originals);
    // BoundInstance tells a copied mark from the object's own.
    [DisallowMultipleComponent]
    public sealed class SkinMark : MonoBehaviour
    {
        public string Role;
        public string Reason;
        public Outcome Outcome;
        public string OriginalSprite;
        public int ThemeRevision;
        public int BoundInstance;
        public bool Internal; // graphic created by RuneSkin (keeps its role, never re-classified)

        // Journal flags
        public bool HasSprite, HasType, HasImageColor, HasPpu, HasPreserve, HasFill, HasFont, HasTextColor, HasStates, HasSpriteStates;

        public Sprite OrigSprite, SetSprite;
        public Image.Type OrigType, SetType;
        public Color OrigImageColor, SetImageColor;
        public float OrigPpu, SetPpu;
        public bool OrigPreserve, SetPreserve;
        public bool OrigFill, SetFill;
        public SpriteState OrigSpriteState, SetSpriteState;
        public TMP_FontAsset OrigFont, SetFont;
        public Material OrigFontMaterial, SetFontMaterial;
        public Color OrigTextColor, SetTextColor;
        public ColorBlock OrigColors, SetColors;
        public Selectable.Transition OrigTransition;

        public bool IsOwnMark => BoundInstance == gameObject.GetInstanceID();

        public void ClearJournal()
        {
            HasSprite = HasType = HasImageColor = HasPpu = HasPreserve = HasFill = HasFont = HasTextColor = HasStates = HasSpriteStates = false;
        }
    }

    // Marks a root registered with a screen id, so an ancestor's pass skips it (one owner per subtree).
    [DisallowMultipleComponent]
    public sealed class RootMark : MonoBehaviour
    {
        public string ScreenId;
        public string Owner;
    }

    // Journal of the SkinScale written to an owned panel's content root.
    [DisallowMultipleComponent]
    public sealed class ScaleMark : MonoBehaviour
    {
        public Vector3 Original;
        public Vector3 Written;
        public int BoundInstance;
    }
}
