using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Viol3.PrefabBrush.Painting;

namespace Viol3.PrefabBrush.UI
{
    public class PrefabBrushWindow : EditorWindow
    {
        const string ProfilePrefKey = "Viol3.PrefabBrush.LastProfile";
        const float CellSize = 76f;

        static readonly GUIContent[] ModeContents =
        {
            new GUIContent("Paint", "Scatter prefabs inside the brush. Hold Shift to erase."),
            new GUIContent("Place", "Place one prefab per click. Drag to rotate, Ctrl+Scroll rotates the preview."),
            new GUIContent("Erase", "Remove prefab instances inside the brush."),
            new GUIContent("Select", "Select prefab instances inside the brush. Hold Shift to add to the selection."),
            new GUIContent("Replace", "Swap prefab instances inside the brush with prefabs from the palette."),
        };

        static readonly Color SelectionColor = new Color(0.24f, 0.5f, 0.95f);

        [SerializeField] BrushProfile profile;
        [SerializeField] BrushProfile scratchProfile;
        [SerializeField] BrushMode mode;
        [SerializeField] bool brushActive;
        [SerializeField] Tool previousTool = Tool.Move;
        [SerializeField] Transform customParent;
        [SerializeField] List<int> selectedEntries = new List<int>();
        [SerializeField] Vector2 scroll;
        [SerializeField] bool showPalette = true;
        [SerializeField] bool showBrush = true;
        [SerializeField] bool showSurface = true;
        [SerializeField] bool showTransform = true;
        [SerializeField] bool showOutput = true;
        [SerializeField] bool showHelp;

        PrefabBrushTool tool;
        SerializedObject serializedProfile;

        public BrushProfile Profile
        {
            get
            {
                if (profile != null)
                    return profile;
                if (scratchProfile == null)
                {
                    scratchProfile = CreateInstance<BrushProfile>();
                    scratchProfile.name = "Unsaved Profile";
                    scratchProfile.hideFlags = HideFlags.DontSave;
                }
                return scratchProfile;
            }
        }

        public BrushMode Mode => mode;
        public bool BrushActive => brushActive;
        public Transform CustomParent => customParent;
        public bool HasEntrySelection => selectedEntries.Count > 0;
        public bool IsEntrySelected(int index) => selectedEntries.Contains(index);

        [MenuItem("Tools/Viol3's Prefab Brush")]
        public static void Open()
        {
            GetWindow<PrefabBrushWindow>().Show();
        }

        void OnEnable()
        {
            titleContent = new GUIContent("Prefab Brush", EditorGUIUtility.FindTexture("Grid.PaintTool"));
            if (profile == null)
                profile = LoadLastProfile();

            tool = new PrefabBrushTool(this);
            tool.Enable();
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        void OnDisable()
        {
            tool.Disable();
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        void OnDestroy()
        {
            SetBrushActive(false);
            if (scratchProfile != null)
                DestroyImmediate(scratchProfile);
        }

        void OnUndoRedo()
        {
            tool.ProfileChanged();
            Repaint();
        }

        void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
                SetBrushActive(false);
        }

        public void SetBrushActive(bool active)
        {
            if (brushActive == active)
                return;

            brushActive = active;
            if (active)
            {
                if (Tools.current != Tool.None)
                    previousTool = Tools.current;
                Tools.current = Tool.None;
            }
            else
            {
                tool?.Reset();
                if (Tools.current == Tool.None)
                    Tools.current = previousTool;
            }

            SceneView.RepaintAll();
            Repaint();
        }

        static BrushProfile LoadLastProfile()
        {
            string guid = EditorPrefs.GetString(ProfilePrefKey, string.Empty);
            return string.IsNullOrEmpty(guid)
                ? null
                : AssetDatabase.LoadAssetAtPath<BrushProfile>(AssetDatabase.GUIDToAssetPath(guid));
        }

        void SetProfile(BrushProfile newProfile)
        {
            profile = newProfile;
            selectedEntries.Clear();
            string path = newProfile != null ? AssetDatabase.GetAssetPath(newProfile) : string.Empty;
            EditorPrefs.SetString(ProfilePrefKey, string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path));
            tool.ProfileChanged();
            SceneView.RepaintAll();
        }

        void OnGUI()
        {
            BrushProfile current = Profile;
            if (serializedProfile == null || serializedProfile.targetObject != current)
                serializedProfile = new SerializedObject(current);
            serializedProfile.Update();
            selectedEntries.RemoveAll(i => i < 0 || i >= current.palette.Count);

            DrawProfileBar();
            DrawActivation();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawPaletteSection();
            DrawBrushSection();
            DrawSurfaceSection();
            DrawTransformSection();
            DrawOutputSection();
            DrawHelpSection();
            EditorGUILayout.EndScrollView();

            if (serializedProfile.ApplyModifiedProperties())
            {
                tool.ProfileChanged();
                SceneView.RepaintAll();
            }

            if (AssetPreview.IsLoadingAssetPreviews())
                Repaint();
        }

        #region Header

        void DrawProfileBar()
        {
            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                var newProfile = (BrushProfile)EditorGUILayout.ObjectField("Profile", profile, typeof(BrushProfile), false);
                if (EditorGUI.EndChangeCheck())
                {
                    SetProfile(newProfile);
                    GUIUtility.ExitGUI();
                }

                if (GUILayout.Button(new GUIContent("New", "Create an empty profile asset."), EditorStyles.miniButtonLeft, GUILayout.Width(44f)))
                    SaveProfileAsset(null);
                if (GUILayout.Button(new GUIContent("Save As", "Save a copy of the current profile as a new asset."), EditorStyles.miniButtonRight, GUILayout.Width(56f)))
                    SaveProfileAsset(Profile);
            }

            if (profile == null)
                EditorGUILayout.HelpBox("Using an unsaved profile. Click Save As to keep your palette and settings.", MessageType.Info);
        }

        void SaveProfileAsset(BrushProfile source)
        {
            string path = EditorUtility.SaveFilePanelInProject("Save Brush Profile", "BrushProfile", "asset", "Choose where to save the brush profile.");
            if (string.IsNullOrEmpty(path))
                GUIUtility.ExitGUI();

            BrushProfile asset = source != null ? Instantiate(source) : CreateInstance<BrushProfile>();
            asset.hideFlags = HideFlags.None;
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            SetProfile(asset);
            GUIUtility.ExitGUI();
        }

        void DrawActivation()
        {
            EditorGUILayout.Space(4f);
            Color previousColor = GUI.backgroundColor;
            if (brushActive)
                GUI.backgroundColor = new Color(0.45f, 1f, 0.55f);

            string label = brushActive ? "Brush Active  (Esc to stop)" : "Activate Brush";
            if (GUILayout.Button(label, GUILayout.Height(30f)))
                SetBrushActive(!brushActive);
            GUI.backgroundColor = previousColor;

            EditorGUI.BeginChangeCheck();
            mode = (BrushMode)GUILayout.Toolbar((int)mode, ModeContents, GUILayout.Height(24f));
            if (EditorGUI.EndChangeCheck())
            {
                tool.Reset();
                SetBrushActive(true);
                SceneView.RepaintAll();
            }
            EditorGUILayout.Space(4f);
        }

        #endregion

        #region Palette

        void DrawPaletteSection()
        {
            SerializedProperty palette = serializedProfile.FindProperty(nameof(BrushProfile.palette));
            showPalette = EditorGUILayout.BeginFoldoutHeaderGroup(showPalette, $"Palette ({palette.arraySize})");
            if (showPalette)
            {
                EditorGUILayout.PropertyField(serializedProfile.FindProperty(nameof(BrushProfile.pickMode)),
                    new GUIContent("Pick Mode", "Weighted: random by weight from all enabled prefabs.\nSelected Only: use only the prefabs selected in the grid."));

                DrawPaletteGrid(palette);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Add Selection", "Add the prefabs selected in the Project window or Hierarchy.")))
                        AddPrefabs(CollectPrefabs(Selection.objects));
                    if (GUILayout.Button("Select All"))
                    {
                        selectedEntries.Clear();
                        for (int i = 0; i < palette.arraySize; i++)
                            selectedEntries.Add(i);
                    }
                    using (new EditorGUI.DisabledScope(palette.arraySize == 0))
                    {
                        if (GUILayout.Button("Clear") && EditorUtility.DisplayDialog("Clear Palette", "Remove every prefab from the palette?", "Clear", "Cancel"))
                        {
                            ModifyProfile("Clear Palette", p => p.palette.Clear());
                            selectedEntries.Clear();
                        }
                    }
                }

                DrawEntryDetails(palette);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        void DrawPaletteGrid(SerializedProperty palette)
        {
            int count = palette.arraySize;
            float width = EditorGUIUtility.currentViewWidth - 28f;
            int columns = Mathf.Max(1, Mathf.FloorToInt(width / CellSize));
            int rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)columns));

            Rect area = GUILayoutUtility.GetRect(width, rows * CellSize + 6f, GUILayout.ExpandWidth(true));
            GUI.Box(area, GUIContent.none, EditorStyles.helpBox);

            if (count == 0)
                GUI.Label(area, "Drag prefabs or folders here", EditorStyles.centeredGreyMiniLabel);

            for (int i = 0; i < count; i++)
            {
                var cell = new Rect(area.x + 3f + (i % columns) * CellSize, area.y + 3f + (i / columns) * CellSize, CellSize - 4f, CellSize - 4f);
                DrawPaletteCell(cell, palette.GetArrayElementAtIndex(i), i);
            }

            HandlePaletteDrop(area);
        }

        void DrawPaletteCell(Rect cell, SerializedProperty entry, int index)
        {
            SerializedProperty prefabProperty = entry.FindPropertyRelative(nameof(PaletteEntry.prefab));
            SerializedProperty enabledProperty = entry.FindPropertyRelative(nameof(PaletteEntry.enabled));
            var prefab = prefabProperty.objectReferenceValue as GameObject;
            Event e = Event.current;

            if (e.type == EventType.Repaint)
            {
                bool selected = selectedEntries.Contains(index);
                EditorGUI.DrawRect(cell, selected ? SelectionColor : new Color(0f, 0f, 0f, 0.2f));
                Rect inner = new Rect(cell.x + 2f, cell.y + 2f, cell.width - 4f, cell.height - 4f);
                EditorGUI.DrawRect(inner, EditorGUIUtility.isProSkin ? new Color(0.16f, 0.16f, 0.16f) : new Color(0.78f, 0.78f, 0.78f));

                Texture texture = null;
                if (prefab != null)
                    texture = AssetPreview.GetAssetPreview(prefab) ?? AssetPreview.GetMiniThumbnail(prefab);

                Color previousColor = GUI.color;
                if (!enabledProperty.boolValue)
                    GUI.color = new Color(1f, 1f, 1f, 0.3f);
                if (texture != null)
                    GUI.DrawTexture(new Rect(inner.x + 4f, inner.y + 4f, inner.width - 8f, inner.height - 20f), texture, ScaleMode.ScaleToFit);
                GUI.color = previousColor;

                GUI.Label(new Rect(inner.x + 2f, inner.yMax - 15f, inner.width - 4f, 14f),
                    prefab != null ? prefab.name : "Missing", EditorStyles.miniLabel);
            }

            var toggleRect = new Rect(cell.x + 4f, cell.y + 4f, 16f, 16f);
            EditorGUI.BeginChangeCheck();
            bool enabled = EditorGUI.Toggle(toggleRect, enabledProperty.boolValue);
            if (EditorGUI.EndChangeCheck())
                enabledProperty.boolValue = enabled;

            if (e.type != EventType.MouseDown || !cell.Contains(e.mousePosition))
                return;

            if (e.button == 0)
            {
                if (EditorGUI.actionKey)
                {
                    if (!selectedEntries.Remove(index))
                        selectedEntries.Add(index);
                }
                else if (e.shift && selectedEntries.Count > 0)
                {
                    int anchor = selectedEntries[selectedEntries.Count - 1];
                    for (int i = Mathf.Min(anchor, index); i <= Mathf.Max(anchor, index); i++)
                    {
                        if (!selectedEntries.Contains(i))
                            selectedEntries.Add(i);
                    }
                }
                else
                {
                    selectedEntries.Clear();
                    selectedEntries.Add(index);
                }

                if (e.clickCount == 2 && prefab != null)
                    EditorGUIUtility.PingObject(prefab);
                tool.ProfileChanged();
            }
            else if (e.button == 1)
            {
                if (!selectedEntries.Contains(index))
                {
                    selectedEntries.Clear();
                    selectedEntries.Add(index);
                }
                ShowEntryMenu(prefab);
            }

            e.Use();
            Repaint();
        }

        void ShowEntryMenu(GameObject prefab)
        {
            var targets = new List<int>(selectedEntries);
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Enable"), false, () => SetEntriesEnabled(targets, true));
            menu.AddItem(new GUIContent("Disable"), false, () => SetEntriesEnabled(targets, false));
            menu.AddItem(new GUIContent("Enable Only These"), false, () =>
                ModifyProfile("Enable Only", p =>
                {
                    for (int i = 0; i < p.palette.Count; i++)
                        p.palette[i].enabled = targets.Contains(i);
                }));
            menu.AddSeparator(string.Empty);
            if (prefab != null)
                menu.AddItem(new GUIContent("Ping Prefab"), false, () => EditorGUIUtility.PingObject(prefab));
            menu.AddItem(new GUIContent("Remove"), false, () => RemoveEntries(targets));
            menu.ShowAsContext();
        }

        void DrawEntryDetails(SerializedProperty palette)
        {
            if (selectedEntries.Count == 0)
                return;

            EditorGUILayout.Space(4f);
            if (selectedEntries.Count > 1)
            {
                EditorGUILayout.LabelField($"{selectedEntries.Count} prefabs selected", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Enable"))
                        SetEntriesEnabled(new List<int>(selectedEntries), true);
                    if (GUILayout.Button("Disable"))
                        SetEntriesEnabled(new List<int>(selectedEntries), false);
                    if (GUILayout.Button("Remove"))
                        RemoveEntries(new List<int>(selectedEntries));
                }
                return;
            }

            SerializedProperty entry = palette.GetArrayElementAtIndex(selectedEntries[0]);
            EditorGUILayout.LabelField("Selected Prefab", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(entry.FindPropertyRelative(nameof(PaletteEntry.prefab)));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative(nameof(PaletteEntry.enabled)));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative(nameof(PaletteEntry.weight)),
                new GUIContent("Weight", "Relative chance of being picked compared to the other prefabs."));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative(nameof(PaletteEntry.rotationOffset)),
                new GUIContent("Rotation Offset", "Fixed rotation applied on top of the random rotation. Useful for models exported with the wrong up axis."));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative(nameof(PaletteEntry.scaleMultiplier)),
                new GUIContent("Scale Multiplier", "Multiplies the random scale for this prefab only."));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative(nameof(PaletteEntry.surfaceOffset)),
                new GUIContent("Surface Offset", "Extra offset along the surface normal. Negative values sink the prefab into the ground."));
            if (GUILayout.Button("Remove From Palette"))
                RemoveEntries(new List<int>(selectedEntries));
        }

        void HandlePaletteDrop(Rect area)
        {
            Event e = Event.current;
            if ((e.type != EventType.DragUpdated && e.type != EventType.DragPerform) || !area.Contains(e.mousePosition))
                return;

            List<GameObject> prefabs = CollectPrefabs(DragAndDrop.objectReferences);
            if (prefabs.Count == 0)
                return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                AddPrefabs(prefabs);
            }
            e.Use();
        }

        // Accepts prefab assets, scene instances (mapped back to their asset) and folders.
        static List<GameObject> CollectPrefabs(Object[] objects)
        {
            var result = new List<GameObject>();
            foreach (Object obj in objects)
            {
                if (obj is GameObject go)
                {
                    GameObject asset = null;
                    if (EditorUtility.IsPersistent(go))
                        asset = go.transform.root.gameObject;
                    else if (PrefabUtility.IsPartOfPrefabInstance(go))
                        asset = PrefabUtility.GetCorrespondingObjectFromSource(PrefabUtility.GetOutermostPrefabInstanceRoot(go));

                    if (asset != null && PrefabUtility.IsPartOfPrefabAsset(asset) && !result.Contains(asset))
                        result.Add(asset);
                    continue;
                }

                string path = AssetDatabase.GetAssetPath(obj);
                if (!AssetDatabase.IsValidFolder(path))
                    continue;
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { path }))
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                    if (prefab != null && !result.Contains(prefab))
                        result.Add(prefab);
                }
            }
            return result;
        }

        void AddPrefabs(List<GameObject> prefabs)
        {
            ModifyProfile("Add Prefabs", p =>
            {
                foreach (GameObject prefab in prefabs)
                {
                    if (!p.palette.Exists(entry => entry.prefab == prefab))
                        p.palette.Add(new PaletteEntry { prefab = prefab });
                }
            });
        }

        void RemoveEntries(List<int> indices)
        {
            indices.Sort();
            ModifyProfile("Remove Prefabs", p =>
            {
                for (int i = indices.Count - 1; i >= 0; i--)
                    p.palette.RemoveAt(indices[i]);
            });
            selectedEntries.Clear();
        }

        void SetEntriesEnabled(List<int> indices, bool enabled)
        {
            ModifyProfile(enabled ? "Enable Prefabs" : "Disable Prefabs", p =>
            {
                foreach (int i in indices)
                    p.palette[i].enabled = enabled;
            });
        }

        // Direct edits used by buttons and context menus; keeps the SerializedObject in sync around them.
        void ModifyProfile(string undoName, System.Action<BrushProfile> action)
        {
            BrushProfile target = Profile;
            serializedProfile?.ApplyModifiedProperties();
            Undo.RecordObject(target, undoName);
            action(target);
            EditorUtility.SetDirty(target);
            serializedProfile?.Update();
            tool.ProfileChanged();
            Repaint();
        }

        #endregion

        #region Settings

        void DrawBrushSection()
        {
            showBrush = EditorGUILayout.BeginFoldoutHeaderGroup(showBrush, "Brush");
            if (showBrush)
            {
                Slider(nameof(BrushProfile.radius), 0.1f, 50f, "Radius", "Brush radius in world units. Shortcut: [ ] or Ctrl+Scroll.");
                Slider(nameof(BrushProfile.density), 0f, 5f, "Density", "Prefabs per square meter placed by each stamp.");
                Slider(nameof(BrushProfile.spacing), 0.05f, 2f, "Spacing", "Distance between stamps while dragging, relative to the radius.");
                Slider(nameof(BrushProfile.minDistance), 0f, 20f, "Min Distance", "Minimum distance between painted prefabs. Prevents overlaps.");
                Slider(nameof(BrushProfile.strength), 0f, 1f, "Strength", "Chance that Erase and Replace affect each instance under the brush.");
                EditorGUILayout.PropertyField(serializedProfile.FindProperty(nameof(BrushProfile.targetFilter)),
                    new GUIContent("Erase/Select Target", "Palette Only: only instances of prefabs in this palette.\nAll Prefab Instances: any prefab instance in the scene."));
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        void DrawSurfaceSection()
        {
            showSurface = EditorGUILayout.BeginFoldoutHeaderGroup(showSurface, "Surface");
            if (showSurface)
            {
                SerializedProperty surfaceMode = serializedProfile.FindProperty(nameof(BrushProfile.surfaceMode));
                EditorGUILayout.PropertyField(surfaceMode,
                    new GUIContent("Paint On", "Colliders And Meshes also paints on meshes without a collider."));
                if (surfaceMode.enumValueIndex == (int)SurfaceMode.CollidersAndMeshes && !SurfaceRaycaster.MeshRaycastSupported)
                    EditorGUILayout.HelpBox("Mesh raycasts are not available in this Unity version. Only colliders will be painted on.", MessageType.Warning);

                EditorGUILayout.PropertyField(serializedProfile.FindProperty(nameof(BrushProfile.surfaceLayers)), new GUIContent("Layers"));
                EditorGUILayout.PropertyField(serializedProfile.FindProperty(nameof(BrushProfile.ignorePaintedObjects)),
                    new GUIContent("Ignore Painted", "Don't paint on top of prefabs from this palette."));
                MinMax(nameof(BrushProfile.slopeRange), 0f, 90f, "Slope", "Only paint where the surface angle (in degrees) is within this range.");

                SerializedProperty useHeight = serializedProfile.FindProperty(nameof(BrushProfile.useHeightRange));
                EditorGUILayout.PropertyField(useHeight, new GUIContent("Height Filter", "Only paint between these world heights."));
                if (useHeight.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(serializedProfile.FindProperty(nameof(BrushProfile.heightRange)), new GUIContent("Min / Max"));
                    EditorGUI.indentLevel--;
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        void DrawTransformSection()
        {
            showTransform = EditorGUILayout.BeginFoldoutHeaderGroup(showTransform, "Transform");
            if (showTransform)
            {
                Slider(nameof(BrushProfile.alignToNormal), 0f, 1f, "Align To Surface", "0 keeps prefabs upright, 1 aligns them fully to the surface normal.");
                MinMax(nameof(BrushProfile.rotationX), -180f, 180f, "Rotation X", "Random rotation range around X.");
                MinMax(nameof(BrushProfile.rotationY), 0f, 360f, "Rotation Y", "Random rotation range around Y.");
                MinMax(nameof(BrushProfile.rotationZ), -180f, 180f, "Rotation Z", "Random rotation range around Z.");

                SerializedProperty scaleType = serializedProfile.FindProperty(nameof(BrushProfile.scaleType));
                EditorGUILayout.PropertyField(scaleType, new GUIContent("Scale"));
                EditorGUI.indentLevel++;
                if (scaleType.enumValueIndex == (int)ScaleType.Uniform)
                {
                    MinMax(nameof(BrushProfile.uniformScale), 0.05f, 5f, "Range", "Random uniform scale range.");
                }
                else
                {
                    MinMax(nameof(BrushProfile.scaleX), 0.05f, 5f, "X", "Random scale range on X.");
                    MinMax(nameof(BrushProfile.scaleY), 0.05f, 5f, "Y", "Random scale range on Y.");
                    MinMax(nameof(BrushProfile.scaleZ), 0.05f, 5f, "Z", "Random scale range on Z.");
                }
                EditorGUI.indentLevel--;

                MinMax(nameof(BrushProfile.surfaceOffset), -2f, 2f, "Surface Offset", "Random offset along the surface normal. Negative values sink prefabs into the ground.");
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        void DrawOutputSection()
        {
            showOutput = EditorGUILayout.BeginFoldoutHeaderGroup(showOutput, "Output");
            if (showOutput)
            {
                SerializedProperty parentMode = serializedProfile.FindProperty(nameof(BrushProfile.parentMode));
                EditorGUILayout.PropertyField(parentMode, new GUIContent("Parent",
                    "Auto Group: one group per prefab under a root object.\nCustom: a Transform you choose.\nSurface Object: the object that was painted on.\nNone: scene root."));

                EditorGUI.indentLevel++;
                switch ((ParentMode)parentMode.enumValueIndex)
                {
                    case ParentMode.AutoGroup:
                        EditorGUILayout.PropertyField(serializedProfile.FindProperty(nameof(BrushProfile.groupName)), new GUIContent("Root Name"));
                        break;
                    case ParentMode.Custom:
                        customParent = (Transform)EditorGUILayout.ObjectField("Transform", customParent, typeof(Transform), true);
                        break;
                }
                EditorGUI.indentLevel--;

                SerializedProperty overrideLayer = serializedProfile.FindProperty(nameof(BrushProfile.overrideLayer));
                EditorGUILayout.PropertyField(overrideLayer, new GUIContent("Override Layer"));
                if (overrideLayer.boolValue)
                {
                    SerializedProperty layer = serializedProfile.FindProperty(nameof(BrushProfile.layer));
                    EditorGUI.indentLevel++;
                    layer.intValue = EditorGUILayout.LayerField("Layer", layer.intValue);
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.PropertyField(serializedProfile.FindProperty(nameof(BrushProfile.markStatic)),
                    new GUIContent("Mark Static", "Mark painted objects and their children as static."));
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        void DrawHelpSection()
        {
            showHelp = EditorGUILayout.BeginFoldoutHeaderGroup(showHelp, "Shortcuts");
            if (showHelp)
            {
                EditorGUILayout.HelpBox(
                    "Left Click / Drag   Use the current mode\n" +
                    "Shift (Paint)        Erase\n" +
                    "Shift (Select)       Add to selection\n" +
                    "[  ]  or Ctrl+Scroll   Brush radius\n" +
                    "Place: Drag            Rotate the placed prefab\n" +
                    "Place: Ctrl+Scroll   Rotate the preview\n" +
                    "Alt + Drag             Orbit the camera\n" +
                    "Esc                       Stop the brush",
                    MessageType.None);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        void Slider(string propertyName, float min, float max, string label, string tooltip)
        {
            EditorGUILayout.Slider(serializedProfile.FindProperty(propertyName), min, max, new GUIContent(label, tooltip));
        }

        void MinMax(string propertyName, float min, float max, string label, string tooltip)
        {
            const float fieldWidth = 48f;
            SerializedProperty property = serializedProfile.FindProperty(propertyName);
            Vector2 value = property.vector2Value;

            Rect rect = EditorGUI.PrefixLabel(EditorGUILayout.GetControlRect(), new GUIContent(label, tooltip));
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            EditorGUI.BeginChangeCheck();
            value.x = EditorGUI.FloatField(new Rect(rect.x, rect.y, fieldWidth, rect.height), value.x);
            EditorGUI.MinMaxSlider(new Rect(rect.x + fieldWidth + 4f, rect.y, rect.width - 2f * (fieldWidth + 4f), rect.height), ref value.x, ref value.y, min, max);
            value.y = EditorGUI.FloatField(new Rect(rect.xMax - fieldWidth, rect.y, fieldWidth, rect.height), value.y);
            if (EditorGUI.EndChangeCheck())
                property.vector2Value = new Vector2(value.x, Mathf.Max(value.x, value.y));

            EditorGUI.indentLevel = indent;
        }

        #endregion
    }
}
