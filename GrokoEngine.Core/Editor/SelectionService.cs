using System;
using System.Collections.Generic;
using System.Linq;
using MiMotor.Mathematics;

namespace GrokoEngine
{
    internal sealed class SelectionService
    {
        public enum SelectionMode
        {
            Replace,
            Add,
            Toggle,
            Remove
        }

        private readonly EditorSceneGraph sceneGraph;
        private readonly List<GameObject> selected = new List<GameObject>();

        public SelectionService(EditorSceneGraph sceneGraph)
        {
            this.sceneGraph = sceneGraph;
        }

        public GameObject? Current { get; private set; }
        public IReadOnlyList<GameObject> Selected => selected;
        public bool HasSelection => selected.Count > 0 || Current != null;

        public void SelectSingle(GameObject? obj)
        {
            selected.Clear();
            Current = obj;
            if (obj != null) selected.Add(obj);
        }

        public void SelectFromViewport(GameObject obj, bool additive)
        {
            Select(obj, additive ? SelectionMode.Toggle : SelectionMode.Replace);
        }

        public void Select(GameObject obj, SelectionMode mode)
        {
            switch (mode)
            {
                case SelectionMode.Replace:
                    SelectSingle(obj);
                    return;
                case SelectionMode.Add:
                    if (!selected.Contains(obj))
                        selected.Add(obj);
                    break;
                case SelectionMode.Remove:
                    selected.Remove(obj);
                    break;
                case SelectionMode.Toggle:
                    if (selected.Contains(obj))
                        selected.Remove(obj);
                    else
                        selected.Add(obj);
                    break;
            }

            Current = selected.LastOrDefault();
        }

        public void SelectMany(IEnumerable<GameObject> objects, SelectionMode mode)
        {
            if (mode == SelectionMode.Replace)
                selected.Clear();

            foreach (var obj in objects)
                Select(obj, mode == SelectionMode.Replace ? SelectionMode.Add : mode);

            Current = selected.LastOrDefault();
        }

        public void SelectBox(
            float left,
            float right,
            float top,
            float bottom,
            bool additive,
            Func<Vector3, Vector2> worldToScreen)
        {
            if (!additive) selected.Clear();

            foreach (var obj in sceneGraph.Flatten())
            {
                Vector3 worldPos = obj.GlobalPosition;
                Vector2 screen = worldToScreen(worldPos);
                if (screen.X >= left && screen.X <= right &&
                    screen.Y >= top && screen.Y <= bottom &&
                    !selected.Contains(obj))
                {
                    selected.Add(obj);
                }
            }

            Current = selected.LastOrDefault();
        }

        public void Clear()
        {
            selected.Clear();
            Current = null;
        }

        public List<GameObject> GetTargets()
        {
            if (selected.Count > 0) return selected.ToList();
            return Current != null ? new List<GameObject> { Current } : new List<GameObject>();
        }

        public List<string> CaptureSelectedIds() =>
            selected.Select(o => o.EditorId).ToList();

        public void RestoreSelectedIds(IEnumerable<string> ids)
        {
            selected.Clear();
            foreach (var id in ids)
            {
                var obj = sceneGraph.FindById(id);
                if (obj != null && !selected.Contains(obj))
                    selected.Add(obj);
            }

            // Bug 22: no buscar Current por separado — podría añadirlo dos veces
            // si ya estaba en ids, o añadir un objeto no solicitado si Current
            // tenía un EditorId distinto a los ids de la lista.
            Current = selected.LastOrDefault();
        }

        public void RemoveMissing()
        {
            selected.RemoveAll(o => sceneGraph.FindById(o.EditorId) == null);
            Current = Current != null ? sceneGraph.FindById(Current.EditorId) : selected.LastOrDefault();
        }
    }
}
