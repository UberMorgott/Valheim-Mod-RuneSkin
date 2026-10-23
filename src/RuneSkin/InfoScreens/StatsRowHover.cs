using UnityEngine;
using UnityEngine.EventSystems;

namespace RuneSkin.InfoScreens
{
    // Pointer enter / exit on a stats row plate: highlights the plate and fills its breakdown tooltip (added before the
    // UITooltip on the same object, so the text is there when the tooltip starts its hover).
    internal sealed class StatsRowHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        internal StatsWindow Window;
        internal StatsWindow.Row Row;
        internal int Half;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (Window) Window.Hover(Row, Half, true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (Window) Window.Hover(Row, Half, false);
        }

        private void OnDisable()
        {
            if (Window) Window.Hover(Row, Half, false);
        }
    }
}
