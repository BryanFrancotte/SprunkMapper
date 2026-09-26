using SharpDX;
using System.Collections.Generic;

namespace CodeWalker.GameFiles
{
    //Everything BoundsTransformer changes, so a drag can restore the drag-start state each frame and
    //apply the total transform once: stacking per-frame rotations drifts by centimetres at world coordinates.
    public sealed class BoundsSnapshot
    {
        private sealed class Entry
        {
            public Bounds Bounds;
            public Matrix Transform, TransformInv;
            public Vector3 BoxMin, BoxMax, BoxCenter, SphereCenter;
            public float SphereRadius;
            public Vector3 CenterGeom, Quantum;
            public Vector3[] Vertices, VerticesShrunk;
            public BoundGeomOctants Octants; //Rotate replaces the object, so keeping the reference restores it
            public Vector4 BvhMin, BvhMax, BvhCenter;
        }

        private readonly List<Entry> entries = new List<Entry>();

        public Bounds Root { get; }

        public BoundsSnapshot(Bounds root)
        {
            Root = root;
            Add(root);
            if (root is BoundComposite comp && comp.Children?.data_items != null)
            {
                foreach (var child in comp.Children.data_items)
                {
                    if (child != null) Add(child);
                }
            }
        }

        private void Add(Bounds b)
        {
            var e = new Entry
            {
                Bounds = b,
                Transform = b.Transform,
                TransformInv = b.TransformInv,
                BoxMin = b.BoxMin,
                BoxMax = b.BoxMax,
                BoxCenter = b.BoxCenter,
                SphereCenter = b.SphereCenter,
                SphereRadius = b.SphereRadius,
            };
            if (b is BoundGeometry g)
            {
                e.CenterGeom = g.CenterGeom;
                e.Quantum = g.Quantum;
                e.Octants = g.Octants;
                e.Vertices = (Vector3[])g.Vertices?.Clone();
                e.VerticesShrunk = (Vector3[])g.VerticesShrunk?.Clone();
            }
            var bvh = (b as BoundBVH)?.BVH;
            if (bvh != null)
            {
                e.BvhMin = bvh.BoundingBoxMin;
                e.BvhMax = bvh.BoundingBoxMax;
                e.BvhCenter = bvh.BoundingBoxCenter;
            }
            entries.Add(e);
        }

        public void Restore()
        {
            foreach (var e in entries)
            {
                var b = e.Bounds;
                b.Transform = e.Transform;
                b.TransformInv = e.TransformInv;
                b.BoxMin = e.BoxMin;
                b.BoxMax = e.BoxMax;
                b.BoxCenter = e.BoxCenter;
                b.SphereCenter = e.SphereCenter;
                b.SphereRadius = e.SphereRadius;
                if (b is BoundGeometry g)
                {
                    g.CenterGeom = e.CenterGeom;
                    g.Quantum = e.Quantum;
                    g.Octants = e.Octants;
                    if ((e.Vertices != null) && (g.Vertices?.Length == e.Vertices.Length)) e.Vertices.CopyTo(g.Vertices, 0);
                    if ((e.VerticesShrunk != null) && (g.VerticesShrunk?.Length == e.VerticesShrunk.Length)) e.VerticesShrunk.CopyTo(g.VerticesShrunk, 0);
                }
                var bvh = (b as BoundBVH)?.BVH;
                if (bvh != null)
                {
                    bvh.BoundingBoxMin = e.BvhMin;
                    bvh.BoundingBoxMax = e.BvhMax;
                    bvh.BoundingBoxCenter = e.BvhCenter;
                }
            }
        }
    }

    //Moves or rotates a whole standalone bounds hierarchy (a ybn root) in world space.
    //Children without a transform stay without one: they are moved through CenterGeom and, for
    //rotations, by rotating their CenterGeom-relative vertices. Children that already carry a
    //transform are moved through it, which keeps whatever box convention the file was saved with.
    public static class BoundsTransformer
    {
        public static bool CanTransform(Bounds root)
        {
            if (root == null || root.Parent != null) return false;
            if (root is BoundComposite comp)
            {
                var children = comp.Children?.data_items;
                if (children != null)
                {
                    foreach (var child in children)
                    {
                        if (child is BoundComposite) return false; //not found in real ybns, not supported
                    }
                }
                return true;
            }
            return root is BoundGeometry;
        }

        public static bool Translate(Bounds root, Vector3 delta)
        {
            if (!CanTransform(root)) return false;

            if (root is BoundComposite comp)
            {
                var children = comp.Children?.data_items;
                if (children != null)
                {
                    foreach (var child in children)
                    {
                        if (child == null) continue;
                        if ((child is BoundGeometry cgeom) && (child.Transform == Matrix.Identity))
                        {
                            ShiftGeometry(cgeom, delta);
                        }
                        else
                        {
                            child.Position = child.Position + delta;
                        }
                    }
                }
                ShiftBVH(comp.BVH, delta);
                ShiftBox(comp, delta);
                return true;
            }
            ShiftGeometry((BoundGeometry)root, delta);
            return true;
        }

        //Rotates by rot about pivot. Polygon BVHs are left stale (their boxes are updated) - call
        //RebuildBVH afterwards when ray/sphere queries against the result are needed.
        public static bool Rotate(Bounds root, Quaternion rot, Vector3 pivot)
        {
            if (!CanTransform(root)) return false;

            var about = Matrix.Translation(-pivot) * rot.ToMatrix() * Matrix.Translation(pivot);

            if (root is BoundComposite comp)
            {
                var children = comp.Children?.data_items;
                if (children == null) return true;

                var min = new Vector3(float.MaxValue);
                var max = new Vector3(float.MinValue);
                var pad = Vector3.Zero;
                foreach (var child in children)
                {
                    if (child == null) continue;
                    if ((child is BoundGeometry cgeom) && (child.Transform == Matrix.Identity))
                    {
                        RotateGeometry(cgeom, rot, pivot);
                        pad = Vector3.Max(pad, cgeom.Quantum * 0.5f);
                    }
                    else
                    {
                        var m = child.Transform * about;
                        child.Transform = m;
                        child.TransformInv = Matrix.Invert(m);
                    }
                    GetParentSpaceBox(child, ref min, ref max);
                }
                if (min.X <= max.X)
                {
                    //saving quantizes the rotated vertices, moving them up to half a quantum - the root box
                    //is the in-game streaming key and is never recomputed on save, so it must allow for that
                    SetBox(comp, min - pad, max + pad);
                }
                comp.BuildBVH(); //cheap, one item per child
                return true;
            }
            RotateGeometry((BoundGeometry)root, rot, pivot);
            return true;
        }

        public static void RebuildBVH(Bounds root)
        {
            if (root is BoundComposite comp)
            {
                var children = comp.Children?.data_items;
                if (children != null)
                {
                    foreach (var child in children)
                    {
                        if (child is BoundBVH bvh) bvh.BuildBVH(false);
                    }
                }
                comp.BuildBVH();
            }
            else if (root is BoundBVH bvh)
            {
                bvh.BuildBVH(false);
            }
        }


        private static void ShiftGeometry(BoundGeometry geom, Vector3 delta)
        {
            geom.CenterGeom += delta;
            ShiftBox(geom, delta);
            if (geom is BoundBVH bvh)
            {
                ShiftBVH(bvh.BVH, delta);
            }
        }

        private static void ShiftBox(Bounds b, Vector3 delta)
        {
            b.BoxMin += delta;
            b.BoxMax += delta;
            b.BoxCenter += delta;
            b.SphereCenter += delta;
        }

        //BVH nodes are quantized relative to BoundingBoxCenter, so moving the box moves the whole tree.
        private static void ShiftBVH(BVH bvh, Vector3 delta)
        {
            if (bvh == null) return;
            var d = new Vector4(delta, 0.0f);
            bvh.BoundingBoxMin += d;
            bvh.BoundingBoxMax += d;
            bvh.BoundingBoxCenter += d;
        }

        private static void RotateGeometry(BoundGeometry geom, Quaternion rot, Vector3 pivot)
        {
            geom.CenterGeom = rot.Multiply(geom.CenterGeom - pivot) + pivot;

            var verts = geom.Vertices;
            if (verts != null)
            {
                for (int i = 0; i < verts.Length; i++) verts[i] = rot.Multiply(verts[i]);
            }
            var shrunk = geom.VerticesShrunk;
            if (shrunk != null)
            {
                for (int i = 0; i < shrunk.Length; i++) shrunk[i] = rot.Multiply(shrunk[i]);
            }
            geom.CalculateQuantum(); //the same call the save makes - keeps the existing quantum when it still fits

            //the same box BoundBVH.BuildBVH computes on save: the union of the polygon boxes
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            var polys = geom.Polygons;
            if (polys != null)
            {
                foreach (var poly in polys)
                {
                    if (poly == null) continue;
                    min = Vector3.Min(min, poly.BoxMin);
                    max = Vector3.Max(max, poly.BoxMax);
                }
            }
            if ((min.X > max.X) && (verts != null))
            {
                for (int i = 0; i < verts.Length; i++)
                {
                    var p = geom.GetVertexPos(i);
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
            }
            if (min.X <= max.X)
            {
                SetBox(geom, min, max);
            }

            if (geom.Type == BoundsType.Geometry)
            {
                geom.CalculateOctants(); //octants partition the vertices by direction, so they change with a rotation
            }
        }

        private static void SetBox(Bounds b, Vector3 min, Vector3 max)
        {
            b.BoxMin = min;
            b.BoxMax = max;
            b.BoxCenter = (min + max) * 0.5f;
            b.SphereCenter = b.BoxCenter;
            b.SphereRadius = (max - b.BoxCenter).Length();
        }

        private static void GetParentSpaceBox(Bounds child, ref Vector3 min, ref Vector3 max)
        {
            if (child is BoundGeometry geom)
            {
                //polygon boxes come from GetVertexPos, which already includes the child's transform
                var polys = geom.Polygons;
                if (polys != null)
                {
                    foreach (var poly in polys)
                    {
                        if (poly == null) continue;
                        min = Vector3.Min(min, poly.BoxMin);
                        max = Vector3.Max(max, poly.BoxMax);
                    }
                    return;
                }
            }
            var box = new BoundingBox(child.BoxMin, child.BoxMax).Transform(child.Transform);
            min = Vector3.Min(min, box.Minimum);
            max = Vector3.Max(max, box.Maximum);
        }
    }
}
