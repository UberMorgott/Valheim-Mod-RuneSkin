using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RuneSkin.PlayerHud
{
    // Raycast-only layer of the strip (coordinator decision 2026-10-23): the strip's visuals keep the HUD's sort, so every
    // window, info box, TopOverlay and ModalLayer renders above them; while the inventory is open this sibling of the
    // strip (same parent, mirrored transform) sorts above the GUI canvas with one invisible hit area per hotbar / potion
    // cell, so a press or drop on a strip cell still reaches StripInput (forwarded by StripHit) instead of InventoryGui's
    // full-screen root/dropButton (run 20261022-194551). Nothing is drawn: HitArea builds no mesh and is fully clear.
    internal sealed class StripHits
    {
        private RectTransform _layer;
        private Canvas _canvas;
        private readonly List<StripInput> _inputs = new List<StripInput>(16);
        private readonly List<RectTransform> _hits = new List<RectTransform>(16);
        private readonly Vector3[] _corners = new Vector3[4];
        private int _collectAt;

        public RectTransform Layer => _layer;

        public void Tick(RectTransform strip, bool open, int order, int sortingLayerId)
        {
            if (!open || !strip || !strip.parent)
            {
                if (_layer && _layer.gameObject.activeSelf) _layer.gameObject.SetActive(false);
                return;
            }
            Ensure(strip);
            if (!_layer.gameObject.activeSelf)
            {
                _layer.gameObject.SetActive(true);
                _collectAt = 0;
            }
            if (!_canvas.overrideSorting) _canvas.overrideSorting = true;
            if (_canvas.sortingOrder != order) _canvas.sortingOrder = order;
            if (_canvas.sortingLayerID != sortingLayerId) _canvas.sortingLayerID = sortingLayerId;
            Mirror(strip);
            if (Time.frameCount >= _collectAt)
            {
                _collectAt = Time.frameCount + 10; // potion cells come and go with the EAQS slot count
                _inputs.Clear();
                strip.GetComponentsInChildren(false, _inputs);
                for (var i = _inputs.Count - 1; i >= 0; i--)
                    if (_inputs[i].Hotbar < 0 && _inputs[i].Potion < 0) _inputs.RemoveAt(i);
            }
            for (var i = 0; i < _inputs.Count; i++)
            {
                var src = _inputs[i];
                var hit = Hit(i);
                if (!src || !src.isActiveAndEnabled || !(src.transform is RectTransform cell))
                {
                    if (hit.gameObject.activeSelf) hit.gameObject.SetActive(false);
                    continue;
                }
                if (!hit.gameObject.activeSelf) hit.gameObject.SetActive(true);
                hit.GetComponent<StripHit>().Target = src;
                cell.GetWorldCorners(_corners);
                Vector2 bl = _layer.InverseTransformPoint(_corners[0]), tr = _layer.InverseTransformPoint(_corners[2]);
                if ((hit.anchoredPosition - bl).sqrMagnitude > 0.01f) hit.anchoredPosition = bl;
                var size = tr - bl;
                if ((hit.sizeDelta - size).sqrMagnitude > 0.01f) hit.sizeDelta = size;
            }
            for (var i = _inputs.Count; i < _hits.Count; i++)
                if (_hits[i].gameObject.activeSelf) _hits[i].gameObject.SetActive(false);
        }

        private void Ensure(RectTransform strip)
        {
            if (_layer) return;
            var go = new GameObject("RuneSkinStripHits", typeof(RectTransform));
            _layer = (RectTransform)go.transform;
            _layer.SetParent(strip.parent, false);
            _layer.SetSiblingIndex(strip.GetSiblingIndex() + 1);
            _canvas = go.AddComponent<Canvas>();
            go.AddComponent<GraphicRaycaster>();
        }

        private void Mirror(RectTransform strip)
        {
            if (_layer.parent != strip.parent) _layer.SetParent(strip.parent, false);
            if (_layer.anchorMin != strip.anchorMin) _layer.anchorMin = strip.anchorMin;
            if (_layer.anchorMax != strip.anchorMax) _layer.anchorMax = strip.anchorMax;
            if (_layer.pivot != strip.pivot) _layer.pivot = strip.pivot;
            if (_layer.sizeDelta != strip.sizeDelta) _layer.sizeDelta = strip.sizeDelta;
            if (_layer.localPosition != strip.localPosition) _layer.localPosition = strip.localPosition;
            if (_layer.localRotation != strip.localRotation) _layer.localRotation = strip.localRotation;
            if (_layer.localScale != strip.localScale) _layer.localScale = strip.localScale;
        }

        private RectTransform Hit(int i)
        {
            while (_hits.Count <= i)
            {
                var go = new GameObject("Hit" + _hits.Count, typeof(RectTransform), typeof(HitArea), typeof(StripHit));
                var rt = (RectTransform)go.transform;
                rt.SetParent(_layer, false);
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                rt.pivot = Vector2.zero;
                var area = go.GetComponent<HitArea>();
                area.color = Color.clear;
                area.raycastTarget = true;
                _hits.Add(rt);
            }
            return _hits[i];
        }
    }

    // Raycast target that draws nothing (empty mesh, clear colour).
    public sealed class HitArea : Graphic
    {
        protected override void OnPopulateMesh(VertexHelper vh) => vh.Clear();
    }

    // Hit area of one strip cell: pointer events go to that cell's StripInput.
    public sealed class StripHit : MonoBehaviour, IPointerDownHandler, IDropHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public StripInput Target;

        public void OnPointerDown(PointerEventData eventData) { if (Target) Target.OnPointerDown(eventData); }
        public void OnDrop(PointerEventData eventData) { if (Target) Target.OnDrop(eventData); }
        public void OnBeginDrag(PointerEventData eventData) { if (Target) Target.OnBeginDrag(eventData); }
        public void OnDrag(PointerEventData eventData) { if (Target) Target.OnDrag(eventData); }
        public void OnEndDrag(PointerEventData eventData) { if (Target) Target.OnEndDrag(eventData); }
    }
}
