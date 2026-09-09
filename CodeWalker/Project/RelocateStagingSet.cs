using CodeWalker.GameFiles;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeWalker.Project
{
    /// <summary>
    /// The relocation engine behind the Relocate Resource panel, kept free of any UI so it can be
    /// tested on its own. It holds a baseline snapshot of the staged content as it was loaded, and
    /// every preview is "restore the baseline, then apply the current transform once" - so dragging
    /// a widget can never accumulate drift, and what you see previewed is exactly what Apply commits.
    /// </summary>
    public class RelocateStagingSet
    {
        public List<YmapFile> Ymaps { get; } = new List<YmapFile>();
        public List<YtypFile> Ytyps { get; } = new List<YtypFile>();
        public List<YbnFile> Ybns { get; } = new List<YbnFile>();

        public Vector3 Pivot { get; set; }
        public Vector3 Offset { get; set; }
        public float YawDegrees { get; set; }
        public bool AllowBareRootRotate { get; set; }

        public int EntitiesMoved { get; private set; }
        public int YbnsMoved { get; private set; }
        public int YbnsSkippedBareRotate { get; private set; }

        public int EntityCount => EntityBaselines.Count;
        public bool IsEmpty => (EntityBaselines.Count == 0) && (YbnBaselines.Count == 0);

        /// <summary>
        /// Called whenever a ybn's bounds have been changed, so the host can rebuild its BVH and
        /// invalidate the cached render mesh. Left null outside the world view.
        /// </summary>
        public Action<YbnFile> YbnGraphicsRefresh { get; set; }

        private readonly List<EntityBaseline> EntityBaselines = new List<EntityBaseline>();
        private readonly List<YbnBaseline> YbnBaselines = new List<YbnBaseline>();

        public void Add(IEnumerable<YmapFile> ymaps, IEnumerable<YtypFile> ytyps, IEnumerable<YbnFile> ybns)
        {
            if (ymaps != null) Ymaps.AddRange(ymaps);
            if (ytyps != null) Ytyps.AddRange(ytyps);
            if (ybns != null) Ybns.AddRange(ybns);
        }

        /// <summary>
        /// Snapshots the staged content's current placement as the new starting point, and resets
        /// the pivot to the group's centre.
        /// </summary>
        public void TakeBaselines()
        {
            EntityBaselines.Clear();
            YbnBaselines.Clear();

            foreach (var ymap in Ymaps)
            {
                if (ymap?.AllEntities == null) continue;
                foreach (var ent in ymap.AllEntities)
                {
                    if (ent == null) continue;
                    if (ent.MloParent != null) continue; //interior children are re-derived from their owner - never moved directly
                    EntityBaselines.Add(new EntityBaseline { Entity = ent, Position = ent.Position, Orientation = ent.Orientation });
                }
            }

            foreach (var ybn in Ybns)
            {
                var b = ybn?.Bounds;
                if (b == null) continue;

                var bl = new YbnBaseline
                {
                    Ybn = ybn,
                    BoxMin = b.BoxMin,
                    BoxMax = b.BoxMax,
                    BoxCenter = b.BoxCenter,
                    SphereCenter = b.SphereCenter,
                    SphereRadius = b.SphereRadius,
                };

                if (b is BoundComposite comp)
                {
                    var children = comp.Children?.data_items?.Where(c => c != null).ToArray() ?? new Bounds[0];
                    bl.Children = children;
                    bl.ChildTransforms = children.Select(c => c.Transform).ToArray();
                }
                else if (b is BoundGeometry geom)
                {
                    //a bare root has no Transform to absorb a move, so its shape data IS its placement
                    //data - it has to be snapshotted in full to be restorable
                    bl.CenterGeom = geom.CenterGeom;
                    bl.Quantum = geom.Quantum;
                    bl.Vertices = (Vector3[])geom.Vertices?.Clone();
                    bl.VerticesShrunk = (Vector3[])geom.VerticesShrunk?.Clone();
                }

                YbnBaselines.Add(bl);
            }

            Pivot = ComputeGroupCenter();
        }

        public void RestoreBaselines()
        {
            foreach (var eb in EntityBaselines)
            {
                if (eb.Entity == null) continue;
                eb.Entity.SetPosition(eb.Position);
                eb.Entity.SetOrientation(eb.Orientation);
            }

            foreach (var bl in YbnBaselines)
            {
                var b = bl.Ybn?.Bounds;
                if (b == null) continue;

                if ((bl.Children != null) && (bl.ChildTransforms != null))
                {
                    for (int i = 0; i < bl.Children.Length; i++)
                    {
                        var child = bl.Children[i];
                        if (child == null) continue;
                        child.Transform = bl.ChildTransforms[i];
                        child.TransformInv = Matrix.Invert(bl.ChildTransforms[i]);
                    }
                }
                else if (b is BoundGeometry geom)
                {
                    geom.CenterGeom = bl.CenterGeom;
                    geom.Quantum = bl.Quantum;
                    if (bl.Vertices != null) geom.Vertices = (Vector3[])bl.Vertices.Clone();
                    if (bl.VerticesShrunk != null) geom.VerticesShrunk = (Vector3[])bl.VerticesShrunk.Clone();
                }

                b.BoxMin = bl.BoxMin;
                b.BoxMax = bl.BoxMax;
                b.BoxCenter = bl.BoxCenter;
                b.SphereCenter = bl.SphereCenter;
                b.SphereRadius = bl.SphereRadius;

                YbnGraphicsRefresh?.Invoke(bl.Ybn);
            }
        }

        /// <summary>
        /// Puts the staged content into exactly the state the current Pivot/Offset/YawDegrees
        /// describe. Safe to call as often as a widget drag fires.
        /// </summary>
        public void UpdatePreview()
        {
            if (IsEmpty) return;

            RestoreBaselines();
            ApplyTransform();
        }

        private void ApplyTransform()
        {
            EntitiesMoved = 0;
            YbnsMoved = 0;
            YbnsSkippedBareRotate = 0;

            var pivot = Pivot;
            var offset = Offset;
            var rot = YawQuaternion();
            var rotating = rot != Quaternion.Identity;

            foreach (var eb in EntityBaselines)
            {
                var ent = eb.Entity;
                if (ent == null) continue;

                //derived from the baseline, not the current state, so this is the same result no
                //matter how many times it runs
                var newPos = pivot + rot.Multiply(eb.Position - pivot) + offset;
                var newOri = Quaternion.Normalize(Quaternion.Multiply(rot, eb.Orientation));

                ent.SetPosition(newPos);
                ent.SetOrientation(newOri);

                EntitiesMoved++;
            }

            foreach (var bl in YbnBaselines)
            {
                var b = bl.Ybn?.Bounds;
                if (b == null) continue;

                if (rotating && !(b is BoundComposite) && !AllowBareRootRotate)
                {
                    //standalone (non-composite) collision root - rotation rebakes vertex data, so it's
                    //gated behind an explicit opt-in. The translation below still applies.
                    YbnsSkippedBareRotate++;
                }
                else if (rotating)
                {
                    BoundsRelocator.Rotate(b, pivot, rot);
                }

                BoundsRelocator.Translate(b, offset);

                YbnGraphicsRefresh?.Invoke(bl.Ybn);

                YbnsMoved++;
            }
        }

        /// <summary>Where the gizmo belongs: the pivot point after the current transform.</summary>
        public Vector3 WidgetPosition => Pivot + Offset;

        public Quaternion YawQuaternion()
        {
            return (YawDegrees == 0.0f)
                ? Quaternion.Identity
                : Quaternion.RotationAxis(Vector3.UnitZ, MathUtil.DegreesToRadians(YawDegrees));
        }

        /// <summary>Yaw in degrees from a Z-axis-only rotation, normalised to (-180, 180].</summary>
        public static float YawDegreesFromQuaternion(Quaternion q)
        {
            var deg = MathUtil.RadiansToDegrees((float)(2.0 * Math.Atan2(q.Z, q.W)));
            while (deg > 180.0f) deg -= 360.0f;
            while (deg <= -180.0f) deg += 360.0f;
            return deg;
        }

        public Vector3 ComputeGroupCenter()
        {
            ComputeGroupBox(out var min, out var max, out var any);
            return any ? (min + max) * 0.5f : Vector3.Zero;
        }

        /// <summary>Size of the staged content, for framing the camera on it.</summary>
        public Vector3 ComputeGroupSize()
        {
            ComputeGroupBox(out var min, out var max, out var any);
            return any ? (max - min) : Vector3.Zero;
        }

        private void ComputeGroupBox(out Vector3 min, out Vector3 max, out bool any)
        {
            min = new Vector3(float.MaxValue);
            max = new Vector3(float.MinValue);
            any = false;

            foreach (var eb in EntityBaselines)
            {
                min = Vector3.Min(min, eb.Position);
                max = Vector3.Max(max, eb.Position);
                any = true;
            }
            foreach (var bl in YbnBaselines)
            {
                min = Vector3.Min(min, bl.BoxMin);
                max = Vector3.Max(max, bl.BoxMax);
                any = true;
            }
        }

        /// <summary>The staged entities and collision roots, for selecting them in the world view.</summary>
        public object[] GetSelectableObjects()
        {
            var objs = new List<object>();
            foreach (var eb in EntityBaselines)
            {
                if (eb.Entity != null) objs.Add(eb.Entity);
            }
            foreach (var bl in YbnBaselines)
            {
                if (bl.Ybn?.Bounds != null) objs.Add(bl.Ybn.Bounds);
            }
            return objs.ToArray();
        }


        private class EntityBaseline
        {
            public YmapEntityDef Entity;
            public Vector3 Position;
            public Quaternion Orientation;
        }

        private class YbnBaseline
        {
            public YbnFile Ybn;
            public Vector3 BoxMin;
            public Vector3 BoxMax;
            public Vector3 BoxCenter;
            public Vector3 SphereCenter;
            public float SphereRadius;

            public Bounds[] Children;         //composite roots
            public Matrix[] ChildTransforms;

            public Vector3 CenterGeom;        //bare geometry roots
            public Vector3 Quantum;
            public Vector3[] Vertices;
            public Vector3[] VerticesShrunk;
        }
    }
}
