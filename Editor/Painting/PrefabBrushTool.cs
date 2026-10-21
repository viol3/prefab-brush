using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Viol3.PrefabBrush.UI;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace Viol3.PrefabBrush.Painting
{
    // Handles all Scene view interaction: hover raycasts, strokes, the place preview and the brush gizmo.
    internal class PrefabBrushTool
    {
        const string UndoName = "Prefab Brush";
        const int MaxPerStamp = 256;
        static readonly int ControlHint = "Viol3.PrefabBrush".GetHashCode();

        struct Variation
        {
            public Quaternion rotation;
            public Vector3 scale;
            public float offset;
        }

        readonly PrefabBrushWindow window;
        readonly SceneCache cache = new SceneCache();
        readonly Func<GameObject, bool> ignoreFilter;
        readonly List<GameObject> queryBuffer = new List<GameObject>();
        readonly List<PaletteEntry> candidates = new List<PaletteEntry>();
        readonly HashSet<GameObject> strokeObjects = new HashSet<GameObject>();

        bool hasHover;
        SurfaceHit hover;

        bool stroking;
        BrushMode strokeMode;
        int undoGroup;
        Vector3 lastStampPoint;

        GameObject preview;
        PaletteEntry previewEntry;
        Variation previewVariation;
        float placeAngle;
        GameObject placing;
        SurfaceHit placingHit;
        float placingStartAngle;
        float dragStartX;

        public PrefabBrushTool(PrefabBrushWindow window)
        {
            this.window = window;
            ignoreFilter = IsIgnored;
        }

        public void Enable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            cache.Enable();
        }

        public void Disable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            cache.Disable();
            Reset();
        }

        public void Reset()
        {
            if (stroking)
            {
                EndStroke();
                GUIUtility.hotControl = 0;
            }
            hasHover = false;
            DestroyPreview();
        }

        public void ProfileChanged()
        {
            cache.MarkDirty();
            previewEntry = null;
            DestroyPreview();
        }

        void OnSceneGUI(SceneView view)
        {
            if (!window.BrushActive)
                return;
            BrushProfile profile = window.Profile;
            if (profile == null)
                return;

            // Picking another transform tool hands control back to Unity.
            if (Tools.current != Tool.None)
            {
                window.SetBrushActive(false);
                return;
            }

            view.wantsMouseMove = true;
            Event e = Event.current;
            int id = GUIUtility.GetControlID(ControlHint, FocusType.Passive);
            BrushMode mode = stroking ? strokeMode : CurrentMode(e);
            if (!stroking)
                cache.Refresh(profile);

            switch (e.GetTypeForControl(id))
            {
                case EventType.Layout:
                    HandleUtility.AddDefaultControl(id);
                    break;

                case EventType.MouseMove:
                    UpdateHover(e, profile);
                    UpdatePreview(mode, profile);
                    view.Repaint();
                    break;

                case EventType.MouseLeaveWindow:
                    hasHover = false;
                    UpdatePreview(mode, profile);
                    view.Repaint();
                    break;

                case EventType.MouseDown:
                    if (e.button != 0 || e.alt)
                        break;
                    UpdateHover(e, profile);
                    GUIUtility.hotControl = id;
                    BeginStroke(mode, profile, e);
                    e.Use();
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id)
                        break;
                    if (mode == BrushMode.Place)
                    {
                        RotatePlacing(e, profile);
                    }
                    else
                    {
                        UpdateHover(e, profile);
                        ContinueStroke(profile);
                    }
                    e.Use();
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != id || e.button != 0)
                        break;
                    GUIUtility.hotControl = 0;
                    EndStroke();
                    UpdateHover(e, profile);
                    UpdatePreview(CurrentMode(e), profile);
                    e.Use();
                    break;

                case EventType.ScrollWheel:
                    if (!EditorGUI.actionKey)
                        break;
                    OnScroll(e, mode, profile);
                    e.Use();
                    break;

                case EventType.KeyDown:
                    OnKeyDown(e, profile);
                    view.Repaint();
                    break;

                case EventType.KeyUp:
                    view.Repaint();
                    break;

                case EventType.Repaint:
                    DrawBrush(mode, profile, e);
                    break;
            }
        }

        BrushMode CurrentMode(Event e)
        {
            return window.Mode == BrushMode.Paint && e.shift ? BrushMode.Erase : window.Mode;
        }

        void UpdateHover(Event e, BrushProfile profile)
        {
            Physics.SyncTransforms();
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            hasHover = Raycast(ray, Mathf.Infinity, profile, out hover);
        }

        bool Raycast(Ray ray, float distance, BrushProfile profile, out SurfaceHit hit)
        {
            return SurfaceRaycaster.Raycast(ray, distance, profile, GetPhysicsScene(), cache.Meshes, ignoreFilter, out hit);
        }

        bool IsIgnored(GameObject go)
        {
            if (preview != null && go.transform.IsChildOf(preview.transform))
                return true;
            if (placing != null && go.transform.IsChildOf(placing.transform))
                return true;
            return window.Profile.ignorePaintedObjects && cache.IsPaletteInstance(go);
        }

        static PhysicsScene GetPhysicsScene()
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            return stage != null ? stage.scene.GetPhysicsScene() : Physics.defaultPhysicsScene;
        }

        #region Strokes

        void BeginStroke(BrushMode mode, BrushProfile profile, Event e)
        {
            cache.Refresh(profile, force: true);
            Undo.IncrementCurrentGroup();
            undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName($"{UndoName} {mode}");

            stroking = true;
            strokeMode = mode;
            strokeObjects.Clear();

            if (mode == BrushMode.Select && !e.shift)
                Selection.objects = new Object[0];
            if (!hasHover)
                return;

            if (mode == BrushMode.Place)
            {
                BeginPlace(profile, e);
                return;
            }

            Stamp(mode, profile, true);
            lastStampPoint = hover.point;
        }

        void ContinueStroke(BrushProfile profile)
        {
            if (!hasHover)
                return;

            float step = Mathf.Max(0.01f, profile.spacing * profile.radius);
            if ((hover.point - lastStampPoint).sqrMagnitude < step * step)
                return;

            Stamp(strokeMode, profile, false);
            lastStampPoint = hover.point;
        }

        void EndStroke()
        {
            if (!stroking)
                return;

            stroking = false;
            placing = null;
            strokeObjects.Clear();
            Undo.CollapseUndoOperations(undoGroup);

            // Roll the next prefab so consecutive clicks in Place mode give variety.
            if (strokeMode == BrushMode.Place)
                previewEntry = null;
        }

        void Stamp(BrushMode mode, BrushProfile profile, bool firstStamp)
        {
            Physics.SyncTransforms();
            switch (mode)
            {
                case BrushMode.Paint: PaintStamp(profile, firstStamp); break;
                case BrushMode.Erase: EraseStamp(profile); break;
                case BrushMode.Select: SelectStamp(profile); break;
                case BrushMode.Replace: ReplaceStamp(profile); break;
            }
        }

        void PaintStamp(BrushProfile profile, bool firstStamp)
        {
            if (!CollectCandidates(profile))
                return;

            float radius = profile.radius;
            float expected = profile.density * Mathf.PI * radius * radius;
            int count = Mathf.FloorToInt(expected);
            if (Random.value < expected - count)
                count++;
            // A single click should always place something, even with a tiny brush.
            if (firstStamp)
                count = Mathf.Max(1, count);
            count = Mathf.Min(count, MaxPerStamp);

            Quaternion disc = Quaternion.FromToRotation(Vector3.up, hover.normal);
            float castHeight = Mathf.Max(radius, 1f) * 2f;

            for (int i = 0; i < count; i++)
            {
                Vector2 offset = count == 1 && firstStamp ? Vector2.zero : Random.insideUnitCircle * radius;
                Vector3 sample = hover.point + disc * new Vector3(offset.x, 0f, offset.y);
                var ray = new Ray(sample + hover.normal * castHeight, -hover.normal);

                if (!Raycast(ray, castHeight * 2f, profile, out SurfaceHit hit) || !PassesFilters(hit, profile))
                    continue;
                if (profile.minDistance > 0f && cache.AnyPaletteInstanceWithin(hit.point, profile.minDistance))
                    continue;

                Spawn(PickEntry(), hit, RandomVariation(profile), 0f, profile);
            }
        }

        void EraseStamp(BrushProfile profile)
        {
            cache.CollectWithin(profile.targetFilter, hover.point, profile.radius, queryBuffer);
            foreach (GameObject go in queryBuffer)
            {
                if (go == null || Random.value > profile.strength)
                    continue;
                cache.Remove(go);
                Undo.DestroyObjectImmediate(go);
            }
        }

        void SelectStamp(BrushProfile profile)
        {
            cache.CollectWithin(profile.targetFilter, hover.point, profile.radius, queryBuffer);
            if (queryBuffer.Count == 0)
                return;

            var selection = new List<Object>(Selection.objects);
            foreach (GameObject go in queryBuffer)
            {
                if (!selection.Contains(go))
                    selection.Add(go);
            }
            Selection.objects = selection.ToArray();
        }

        void ReplaceStamp(BrushProfile profile)
        {
            if (!CollectCandidates(profile))
                return;

            cache.CollectWithin(profile.targetFilter, hover.point, profile.radius, queryBuffer);
            foreach (GameObject go in queryBuffer)
            {
                if (go == null || strokeObjects.Contains(go) || Random.value > profile.strength)
                    continue;

                PaletteEntry entry = PickEntry();
                var replacement = PrefabUtility.InstantiatePrefab(entry.prefab, go.scene) as GameObject;
                if (replacement == null)
                    continue;
                Undo.RegisterCreatedObjectUndo(replacement, UndoName);

                Transform source = go.transform;
                Transform target = replacement.transform;
                target.SetParent(source.parent, false);
                target.SetSiblingIndex(source.GetSiblingIndex());
                target.SetPositionAndRotation(source.position, source.rotation);
                target.localScale = source.localScale;
                ApplyOutput(replacement, profile);

                cache.Remove(go);
                Undo.DestroyObjectImmediate(go);
                cache.Add(replacement);
                strokeObjects.Add(replacement);
            }
        }

        static bool PassesFilters(SurfaceHit hit, BrushProfile profile)
        {
            float slope = Vector3.Angle(Vector3.up, hit.normal);
            if (slope < profile.slopeRange.x || slope > profile.slopeRange.y)
                return false;
            if (profile.useHeightRange && (hit.point.y < profile.heightRange.x || hit.point.y > profile.heightRange.y))
                return false;
            return true;
        }

        #endregion

        #region Place mode

        void BeginPlace(BrushProfile profile, Event e)
        {
            if (previewEntry == null || previewEntry.prefab == null)
                return;

            placingHit = hover;
            placingStartAngle = placeAngle;
            dragStartX = e.mousePosition.x;
            DestroyPreview();
            placing = Spawn(previewEntry, hover, previewVariation, placeAngle, profile);
        }

        void RotatePlacing(Event e, BrushProfile profile)
        {
            if (placing == null)
                return;

            placeAngle = placingStartAngle + (e.mousePosition.x - dragStartX) * 0.5f;
            ApplyPose(placing.transform, previewEntry, placingHit, previewVariation, placeAngle, profile);
        }

        void UpdatePreview(BrushMode mode, BrushProfile profile)
        {
            if (mode != BrushMode.Place || !hasHover || stroking)
            {
                if (preview != null)
                    preview.SetActive(false);
                return;
            }

            if (previewEntry == null || previewEntry.prefab == null || !profile.palette.Contains(previewEntry))
            {
                DestroyPreview();
                if (!CollectCandidates(profile))
                    return;
                previewEntry = PickEntry();
                previewVariation = RandomVariation(profile);
            }

            if (preview == null)
                preview = CreatePreview(previewEntry.prefab, hover.gameObject.scene);
            preview.SetActive(true);
            ApplyPose(preview.transform, previewEntry, hover, previewVariation, placeAngle, profile);
        }

        static GameObject CreatePreview(GameObject prefab, Scene scene)
        {
            GameObject go = Object.Instantiate(prefab);
            go.name = "Prefab Brush Preview";
            if (scene.IsValid() && go.scene != scene)
                SceneManager.MoveGameObjectToScene(go, scene);

            // The preview must never be hit by our own raycasts or run gameplay code.
            foreach (Collider collider in go.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (MonoBehaviour behaviour in go.GetComponentsInChildren<MonoBehaviour>(true))
                behaviour.enabled = false;

            go.hideFlags = HideFlags.HideAndDontSave;
            return go;
        }

        void DestroyPreview()
        {
            if (preview != null)
                Object.DestroyImmediate(preview);
            preview = null;
        }

        #endregion

        #region Spawning

        GameObject Spawn(PaletteEntry entry, SurfaceHit hit, Variation variation, float angle, BrushProfile profile)
        {
            var go = PrefabUtility.InstantiatePrefab(entry.prefab, hit.gameObject.scene) as GameObject;
            if (go == null)
                return null;
            Undo.RegisterCreatedObjectUndo(go, UndoName);

            Transform parent = ResolveParent(entry, hit, profile);
            if (parent != null)
                go.transform.SetParent(parent, false);

            ApplyPose(go.transform, entry, hit, variation, angle, profile);
            ApplyOutput(go, profile);

            cache.Add(go);
            strokeObjects.Add(go);
            return go;
        }

        static void ApplyPose(Transform transform, PaletteEntry entry, SurfaceHit hit, Variation variation, float angle, BrushProfile profile)
        {
            Vector3 up = Vector3.Slerp(Vector3.up, hit.normal, profile.alignToNormal).normalized;
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, up)
                * Quaternion.AngleAxis(angle, Vector3.up)
                * variation.rotation
                * Quaternion.Euler(entry.rotationOffset);

            transform.SetPositionAndRotation(hit.point + up * (variation.offset + entry.surfaceOffset), rotation);
            transform.localScale = Vector3.Scale(entry.prefab.transform.localScale, variation.scale) * entry.scaleMultiplier;
        }

        static void ApplyOutput(GameObject go, BrushProfile profile)
        {
            if (!profile.overrideLayer && !profile.markStatic)
                return;

            foreach (Transform child in go.GetComponentsInChildren<Transform>(true))
            {
                if (profile.overrideLayer)
                    child.gameObject.layer = profile.layer;
                if (profile.markStatic)
                    child.gameObject.isStatic = true;
            }
        }

        Transform ResolveParent(PaletteEntry entry, SurfaceHit hit, BrushProfile profile)
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            Transform stageRoot = stage != null ? stage.prefabContentsRoot.transform : null;

            switch (profile.parentMode)
            {
                case ParentMode.Custom:
                    return window.CustomParent != null ? window.CustomParent : stageRoot;
                case ParentMode.SurfaceObject:
                    return hit.gameObject.transform;
                case ParentMode.AutoGroup:
                    Scene scene = hit.gameObject.scene;
                    Transform group = FindOrCreateGroup(stageRoot, profile.groupName, scene);
                    return FindOrCreateGroup(group, entry.prefab.name, scene);
                default:
                    return stageRoot;
            }
        }

        static Transform FindOrCreateGroup(Transform parent, string name, Scene scene)
        {
            if (string.IsNullOrEmpty(name))
                return parent;

            if (parent != null)
            {
                Transform existing = parent.Find(name);
                if (existing != null)
                    return existing;
            }
            else
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.name == name)
                        return root.transform;
                }
            }

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, UndoName);
            SceneManager.MoveGameObjectToScene(go, parent != null ? parent.gameObject.scene : scene);
            if (parent != null)
                go.transform.SetParent(parent, false);
            return go.transform;
        }

        bool CollectCandidates(BrushProfile profile)
        {
            candidates.Clear();
            bool onlySelected = profile.pickMode == PickMode.SelectedOnly && window.HasEntrySelection;
            for (int i = 0; i < profile.palette.Count; i++)
            {
                PaletteEntry entry = profile.palette[i];
                if (entry == null || !entry.enabled || entry.prefab == null || entry.weight <= 0f)
                    continue;
                if (onlySelected && !window.IsEntrySelected(i))
                    continue;
                candidates.Add(entry);
            }
            return candidates.Count > 0;
        }

        PaletteEntry PickEntry()
        {
            float total = 0f;
            foreach (PaletteEntry entry in candidates)
                total += entry.weight;

            float roll = Random.value * total;
            foreach (PaletteEntry entry in candidates)
            {
                roll -= entry.weight;
                if (roll <= 0f)
                    return entry;
            }
            return candidates[candidates.Count - 1];
        }

        static Variation RandomVariation(BrushProfile profile)
        {
            Variation variation;
            variation.rotation = Quaternion.Euler(Range(profile.rotationX), Range(profile.rotationY), Range(profile.rotationZ));
            variation.scale = profile.scaleType == ScaleType.Uniform
                ? Vector3.one * Range(profile.uniformScale)
                : new Vector3(Range(profile.scaleX), Range(profile.scaleY), Range(profile.scaleZ));
            variation.offset = Range(profile.surfaceOffset);
            return variation;
        }

        static float Range(Vector2 range) => Random.Range(range.x, range.y);

        #endregion

        #region Input and drawing

        void OnScroll(Event e, BrushMode mode, BrushProfile profile)
        {
            float direction = e.delta.y > 0f ? -1f : 1f;
            if (mode == BrushMode.Place)
            {
                placeAngle += direction * 15f;
                UpdatePreview(mode, profile);
                return;
            }
            ScaleRadius(profile, 1f + direction * 0.1f);
        }

        void OnKeyDown(Event e, BrushProfile profile)
        {
            switch (e.keyCode)
            {
                case KeyCode.Escape:
                    window.SetBrushActive(false);
                    e.Use();
                    break;
                case KeyCode.LeftBracket:
                    ScaleRadius(profile, 1f / 1.15f);
                    e.Use();
                    break;
                case KeyCode.RightBracket:
                    ScaleRadius(profile, 1.15f);
                    e.Use();
                    break;
            }
        }

        void ScaleRadius(BrushProfile profile, float factor)
        {
            Undo.RecordObject(profile, "Change Brush Radius");
            profile.radius = Mathf.Clamp(profile.radius * factor, 0.05f, 500f);
            window.Repaint();
        }

        void DrawBrush(BrushMode mode, BrushProfile profile, Event e)
        {
            if (!hasHover)
                return;

            Color color = ModeColor(mode);
            Vector3 point = hover.point;
            Vector3 normal = hover.normal;

            using (new Handles.DrawingScope(color))
            {
                if (mode == BrushMode.Place)
                {
                    float size = HandleUtility.GetHandleSize(point) * 0.2f;
                    Handles.DrawWireDisc(point, normal, size, 2f);
                    Handles.DrawLine(point, point + normal * size * 2f, 2f);
                }
                else
                {
                    float radius = profile.radius;
                    Handles.color = new Color(color.r, color.g, color.b, 0.1f);
                    Handles.DrawSolidDisc(point, normal, radius);
                    Handles.color = color;
                    Handles.DrawWireDisc(point, normal, radius, 2f);
                    Handles.DrawLine(point, point + normal * Mathf.Min(radius * 0.3f, HandleUtility.GetHandleSize(point)), 2f);

                    // Show exactly which instances an erase/select/replace stamp would affect.
                    if (mode != BrushMode.Paint)
                    {
                        cache.CollectWithin(profile.targetFilter, point, radius, queryBuffer);
                        foreach (GameObject go in queryBuffer)
                        {
                            Vector3 position = go.transform.position;
                            Handles.DrawWireDisc(position, go.transform.up, HandleUtility.GetHandleSize(position) * 0.08f, 2f);
                        }
                    }
                }
            }

            string label = mode == BrushMode.Place
                ? $"Place  ·  {(previewEntry != null && previewEntry.prefab != null ? previewEntry.prefab.name : "-")}"
                : $"{mode}  ·  R {profile.radius:0.##}";
            var content = new GUIContent(label);
            Vector2 labelSize = EditorStyles.helpBox.CalcSize(content);

            Handles.BeginGUI();
            GUI.Label(new Rect(e.mousePosition + new Vector2(16f, 16f), labelSize), content, EditorStyles.helpBox);
            Handles.EndGUI();
        }

        static Color ModeColor(BrushMode mode)
        {
            switch (mode)
            {
                case BrushMode.Erase: return new Color(1f, 0.35f, 0.3f);
                case BrushMode.Select: return new Color(1f, 0.85f, 0.2f);
                case BrushMode.Replace: return new Color(0.75f, 0.45f, 1f);
                case BrushMode.Place: return new Color(0.35f, 1f, 0.45f);
                default: return new Color(0.25f, 0.75f, 1f);
            }
        }

        #endregion
    }
}
