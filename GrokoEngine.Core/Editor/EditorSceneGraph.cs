using System;
using System.Collections.Generic;
using System.Linq;

namespace GrokoEngine
{
    internal sealed class EditorSceneGraph
    {
        private readonly List<GameObject> roots;
        private readonly PhysicsEngine physicsEngine;

        public EditorSceneGraph(List<GameObject> roots, PhysicsEngine physicsEngine)
        {
            this.roots = roots;
            this.physicsEngine = physicsEngine;
        }

        public IEnumerable<GameObject> Flatten() =>
            roots.SelectMany(Flatten);

        public IEnumerable<GameObject> Flatten(GameObject obj)
        {
            yield return obj;
            foreach (var child in obj.Children)
                foreach (var nested in Flatten(child))
                    yield return nested;
        }

        public GameObject? FindById(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            return Flatten().FirstOrDefault(o => o.EditorId == id);
        }

        public int IndexOf(GameObject obj)
        {
            return obj.Parent != null
                ? obj.Parent.Children.IndexOf(obj)
                : roots.IndexOf(obj);
        }

        public bool IsDescendantOf(GameObject possibleChild, GameObject possibleParent)
        {
            var current = possibleChild.Parent;
            while (current != null)
            {
                if (current == possibleParent) return true;
                current = current.Parent;
            }

            return false;
        }

        public void Attach(GameObject obj, GameObject? parent, int index)
        {
            if (obj == parent || (parent != null && IsDescendantOf(parent, obj)))
                throw new InvalidOperationException("Cannot parent an object under itself.");

            int oldIndex = IndexOf(obj);
            if (obj.Parent == parent && oldIndex >= 0 && oldIndex < index)
                index--;

            if (obj.Parent != null) obj.Parent.Children.Remove(obj);
            else roots.Remove(obj);

            var list = parent != null ? parent.Children : roots;
            int safeIndex = Math.Max(0, Math.Min(index, list.Count));
            obj.Parent = parent;
            list.Remove(obj);
            list.Insert(safeIndex, obj);
            RegisterColliders(obj);
        }

        public void Detach(GameObject obj)
        {
            UnregisterColliders(obj);
            if (obj.Parent != null) obj.Parent.Children.Remove(obj);
            else roots.Remove(obj);
            obj.Parent = null;
        }

        private void RegisterColliders(GameObject obj)
        {
            foreach (var comp in obj.Components)
                if (comp is Collider collider) physicsEngine.RegisterCollider(collider);
            foreach (var child in obj.Children)
                RegisterColliders(child);
        }

        private void UnregisterColliders(GameObject obj)
        {
            foreach (var comp in obj.Components)
                if (comp is Collider collider) physicsEngine.UnregisterCollider(collider);
            foreach (var child in obj.Children)
                UnregisterColliders(child);
        }
    }
}
