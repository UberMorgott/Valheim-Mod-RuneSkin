using TMPro;
using UnityEngine;

namespace RuneSkin.PreWorld
{
    // Input placeholders of the start-game panel come bracketed from vanilla ("[Filter]" unlocalized in the server
    // filter, "[Пусто]" in the world password): the brackets go, and the untranslated "Filter" takes the current
    // language's word (vanilla ships no key for it). Checked when the text changes (localisation writes it after
    // Awake); Revert puts the vanilla text back.
    [DisallowMultipleComponent]
    public sealed class PlaceholderText : MonoBehaviour, Restyle.IRevertible
    {
        private TMP_Text _text;
        private string _orig, _set;

        private void LateUpdate()
        {
            if (!_text && !TryGetComponent(out _text)) return;
            var s = _text.text;
            if (ReferenceEquals(s, _set) || s == _set) return;
            var clean = PreWorldMath.CleanPlaceholder(s, Localization.instance != null ? Localization.instance.GetSelectedLanguage() : null);
            if (clean == s) { _set = s; return; }
            _orig = s;
            _text.text = _set = clean;
        }

        public void Revert()
        {
            if (_text && _orig != null && _text.text == _set) _text.text = _orig;
            Destroy(this);
        }
    }
}
