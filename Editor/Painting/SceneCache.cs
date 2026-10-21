using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Viol3.PrefabBrush.Painting
{
    // Keeps track of prefab instances and paintable meshes so per-stamp queries don't walk the whole hierarchy.
    internal class SceneCache
    {
        readonly List<GameObject> paletteInstances = new List<GameObject>();
        readonly List<GameObject> allInstances = new List<GameObject>();
        readonly List<MeshTarget> meshes = new List<MeshTarget>();
        readonly HashSet<GameObject> paletteAssets = new HashSet<GameObject>();
        bool dirty = true;

        public IReadOnlyList<MeshTarget> Meshes => meshes;

        public void Enable()
        {
            EditorApplication.hierarchyChanged += MarkDirty;
            Undo.undoRedoPerformed += MarkDirty;
        }

        public void Disable()
        {
            EditorApplication.hierarchyChanged -= MarkDirty;
            Undo.undoRedoPerformed -= MarkDirty;
        }

        public void MarkDirty() => dirty = true;

        public void Refresh(BrushProfile profile, bool force = false)
        {
            if (!dirty && !force)
                return;
            dirty = false;

            paletteAssets.Clear();
            foreach (PaletteEntry entry in profile.palette)
            {
                if (entry != null && entry.prefab != null)
                    paletteAssets.Add(entry.prefab);
            }

            paletteInstances.Clear();
            allInstances.Clear();
            meshes.Clear();

            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                Collect(stage.prefabContentsRoot.transform);
                return;
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                    Collect(root.transform);
            }
        }

        void Collect(Transform transform)
        {
            GameObject go = transform.gameObject;
            if (!go.activeInHierarchy || (go.hideFlags & HideFlags.HideInHierarchy) != 0)
                return;

            if (PrefabUtility.IsOutermostPrefabInstanceRoot(go))
            {
                allInstances.Add(go);
                if (paletteAssets.Contains(PrefabUtility.GetCorrespondingObjectFromSource(go)))
                    paletteInstances.Add(go);
            }

            if (go.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null &&
                go.TryGetComponent(out MeshRenderer renderer) && renderer.enabled)
            {
                bool hasCollider = go.TryGetComponent(out Collider collider) && collider.enabled && !collider.isTrigger;
                meshes.Add(new MeshTarget { filter = filter, renderer = renderer, hasCollider = hasCollider });
            }

            for (int i = 0; i < transform.childCount; i++)
                Collect(transform.GetChild(i));
        }

        public bool IsPaletteInstance(GameObject go)
        {
            GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            return root != null && paletteAssets.Contains(PrefabUtility.GetCorrespondingObjectFromSource(root));
        }

        public bool AnyPaletteInstanceWithin(Vector3 point, float distance)
        {
            float sqrDistance = distance * distance;
            foreach (GameObject go in paletteInstances)
            {
                if (go != null && (go.transform.position - point).sqrMagnitude < sqrDistance)
                    return true;
            }
            return false;
        }

        public void CollectWithin(TargetFilter filter, Vector3 center, float radius, List<GameObject> results)
        {
            results.Clear();
            List<GameObject> source = filter == TargetFilter.PaletteOnly ? paletteInstances : allInstances;
            float sqrRadius = radius * radius;
            for (int i = source.Count - 1; i >= 0; i--)
            {
                GameObject go = source[i];
                if (go == null)
                {
                    source.RemoveAt(i);
                    continue;
                }
                if ((go.transform.position - center).sqrMagnitude <= sqrRadius)
                    results.Add(go);
            }
        }

        public void Add(GameObject go)
        {
            allInstances.Add(go);
            if (paletteAssets.Contains(PrefabUtility.GetCorrespondingObjectFromSource(go)))
                paletteInstances.Add(go);
        }

        public void Remove(GameObject go)
        {
            allInstances.Remove(go);
            paletteInstances.Remove(go);
        }
    }
}
