using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Viol3.PrefabBrush.Painting
{
    internal struct SurfaceHit
    {
        public Vector3 point;
        public Vector3 normal;
        public GameObject gameObject;
        public float distance;
    }

    internal struct MeshTarget
    {
        public MeshFilter filter;
        public Renderer renderer;
        public bool hasCollider;
    }

    // Raycasts against colliders and, optionally, plain meshes that have no collider at all.
    internal static class SurfaceRaycaster
    {
        static readonly MethodInfo IntersectRayMeshMethod = typeof(HandleUtility).GetMethod(
            "IntersectRayMesh", BindingFlags.Static | BindingFlags.NonPublic);

        static readonly RaycastHit[] HitBuffer = new RaycastHit[64];
        static readonly object[] MeshArgs = new object[4];

        public static bool MeshRaycastSupported => IntersectRayMeshMethod != null;

        public static bool Raycast(Ray ray, float maxDistance, BrushProfile profile, PhysicsScene physics,
            IReadOnlyList<MeshTarget> meshes, Func<GameObject, bool> isIgnored, out SurfaceHit hit)
        {
            hit = default;
            hit.distance = float.MaxValue;
            bool found = false;

            int count = physics.Raycast(ray.origin, ray.direction, HitBuffer, maxDistance,
                profile.surfaceLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = HitBuffer[i];
                if (h.distance >= hit.distance || isIgnored(h.collider.gameObject))
                    continue;

                hit = new SurfaceHit { point = h.point, normal = h.normal, gameObject = h.collider.gameObject, distance = h.distance };
                found = true;
            }

            if (profile.surfaceMode != SurfaceMode.CollidersAndMeshes || !MeshRaycastSupported)
                return found;

            for (int i = 0; i < meshes.Count; i++)
            {
                MeshTarget target = meshes[i];
                if (target.filter == null || target.hasCollider)
                    continue;

                GameObject go = target.filter.gameObject;
                if ((profile.surfaceLayers & (1 << go.layer)) == 0)
                    continue;
                if (!target.renderer.bounds.IntersectRay(ray, out float boundsDistance) ||
                    boundsDistance > hit.distance || boundsDistance > maxDistance)
                    continue;
                if (isIgnored(go))
                    continue;

                MeshArgs[0] = ray;
                MeshArgs[1] = target.filter.sharedMesh;
                MeshArgs[2] = target.filter.transform.localToWorldMatrix;
                MeshArgs[3] = null;
                if (!(bool)IntersectRayMeshMethod.Invoke(null, MeshArgs))
                    continue;

                var h = (RaycastHit)MeshArgs[3];
                if (h.distance >= hit.distance || h.distance > maxDistance)
                    continue;

                hit = new SurfaceHit { point = h.point, normal = h.normal.normalized, gameObject = go, distance = h.distance };
                found = true;
            }

            return found;
        }
    }
}
