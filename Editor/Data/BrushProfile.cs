using System;
using System.Collections.Generic;
using UnityEngine;

namespace Viol3.PrefabBrush
{
    public enum BrushMode { Paint, Place, Erase, Select, Replace }
    public enum PickMode { Weighted, SelectedOnly }
    public enum ScaleType { Uniform, PerAxis }
    public enum ParentMode { AutoGroup, Custom, SurfaceObject, None }
    public enum TargetFilter { PaletteOnly, AllPrefabInstances }
    public enum SurfaceMode { CollidersAndMeshes, CollidersOnly }

    [Serializable]
    public class PaletteEntry
    {
        public GameObject prefab;
        public bool enabled = true;
        [Min(0f)] public float weight = 1f;
        public Vector3 rotationOffset;
        [Min(0.001f)] public float scaleMultiplier = 1f;
        public float surfaceOffset;
    }

    // All brush settings live here so they can be saved as presets and shared between projects.
    [CreateAssetMenu(fileName = "BrushProfile", menuName = "Viol3's Prefab Brush/Brush Profile")]
    public class BrushProfile : ScriptableObject
    {
        public List<PaletteEntry> palette = new List<PaletteEntry>();
        public PickMode pickMode;

        [Min(0.05f)] public float radius = 5f;
        [Min(0f)] public float density = 0.25f;
        [Range(0.05f, 2f)] public float spacing = 0.5f;
        [Min(0f)] public float minDistance = 1f;
        [Range(0f, 1f)] public float strength = 1f;
        public TargetFilter targetFilter;

        public SurfaceMode surfaceMode;
        public LayerMask surfaceLayers = ~0;
        public bool ignorePaintedObjects = true;
        public Vector2 slopeRange = new Vector2(0f, 180f);
        public bool useHeightRange;
        public Vector2 heightRange = new Vector2(-100f, 100f);

        [Range(0f, 1f)] public float alignToNormal;
        public Vector2 rotationX;
        public Vector2 rotationY = new Vector2(0f, 360f);
        public Vector2 rotationZ;
        public ScaleType scaleType;
        public Vector2 uniformScale = Vector2.one;
        public Vector2 scaleX = Vector2.one;
        public Vector2 scaleY = Vector2.one;
        public Vector2 scaleZ = Vector2.one;
        public Vector2 surfaceOffset;

        public ParentMode parentMode;
        public string groupName = "Prefab Brush";
        public bool overrideLayer;
        public int layer;
        public bool markStatic;
    }
}
