using UnityEngine;

namespace RuneSkin.Restyle
{
    // Allocation-free child iteration: a foreach over a Transform itself allocates Transform's enumerator object on every loop
    // (per frame in the screen layouts; perf audit B2). `foreach (var c in Children.Of(t))` walks GetChild(i) through
    // a struct enumerator with the same order and semantics (index based, like Transform's own enumerator).
    public readonly struct Children
    {
        private readonly Transform _parent;

        private Children(Transform parent) => _parent = parent;

        public static Children Of(Transform parent) => new Children(parent);

        public Enumerator GetEnumerator() => new Enumerator(_parent);

        public struct Enumerator
        {
            private readonly Transform _parent;
            private int _index;

            public Enumerator(Transform parent)
            {
                _parent = parent;
                _index = -1;
            }

            public Transform Current => _parent.GetChild(_index);

            public bool MoveNext() => _parent && ++_index < _parent.childCount;
        }
    }
}
