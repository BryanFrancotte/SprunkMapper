using System;

namespace CodeWalker.GameFiles
{
    /// <summary>
    /// Relocates a standalone (non-entity-owned) collision Bound as a rigid whole -
    /// something CodeWalker has no existing support for. A root Bound's Transform is
    /// never serialized and never applied by anything (see Bounds.cs: Transform is only
    /// ever assigned by BoundComposite.Read() for its own children), so moving a
    /// standalone .ybn requires touching different fields depending on whether the root
    /// is a BoundComposite (move each child's Transform, then resync the root's own
    /// outer bounds - CodeWalker's BuildBVH() never does this second part) or a bare
    /// BoundGeometry/BoundBVH (Transform is permanently identity, so translation shifts
    /// CenterGeom directly and rotation has to be baked into the vertex data itself).
    /// </summary>
    public static class BoundsRelocator
    {
        public static void Translate(Bounds root, SharpDX.Vector3 delta)
        {
            if (root == null) return;
            if (delta == SharpDX.Vector3.Zero) return;

            if (root is BoundComposite comp)
            {
                TranslateComposite(comp, delta);
            }
            else if (root is BoundGeometry geom) //BoundBVH : BoundGeometry, so this covers both
            {
                TranslateBareGeometry(geom, delta);
            }
            else
            {
                throw new NotSupportedException(
                    $"BoundsRelocator: relocating a bare '{root.GetType().Name}' root (Box/Sphere/Capsule/Cylinder/Disc with no composite wrapper) is not supported.");
            }
        }

        public static void Rotate(Bounds root, SharpDX.Vector3 pivot, SharpDX.Quaternion delta)
        {
            if (root == null) return;
            if (IsIdentity(delta)) return;

            if (root is BoundComposite comp)
            {
                RotateComposite(comp, pivot, delta);
            }
            else if (root is BoundGeometry geom)
            {
                RotateBareGeometry(geom, pivot, delta);
            }
            else
            {
                throw new NotSupportedException(
                    $"BoundsRelocator: relocating a bare '{root.GetType().Name}' root (Box/Sphere/Capsule/Cylinder/Disc with no composite wrapper) is not supported.");
            }
        }


        private static bool IsIdentity(SharpDX.Quaternion q)
        {
            var i = SharpDX.Quaternion.Identity;
            return (q.X == i.X) && (q.Y == i.Y) && (q.Z == i.Z) && (q.W == i.W);
        }


        // ---------------------------------------------------------------
        // BoundComposite: move each child's Transform only. Local shape data
        // (BoxMin/Max/SphereCenter/CenterGeom/Quantum/vertices/inner BVH) on every
        // child is left completely untouched - it's already correct in whatever
        // frame the child's own Transform maps into composite space.
        // ---------------------------------------------------------------

        private static void TranslateComposite(BoundComposite comp, SharpDX.Vector3 delta)
        {
            var children = comp.Children?.data_items;
            if (children != null)
            {
                foreach (var child in children)
                {
                    if (child == null) continue;
                    child.Position = child.Position + delta;
                }
            }
            ResyncCompositeRootBounds(comp);
        }

        private static void RotateComposite(BoundComposite comp, SharpDX.Vector3 pivot, SharpDX.Quaternion delta)
        {
            var children = comp.Children?.data_items;
            if (children != null)
            {
                foreach (var child in children)
                {
                    if (child == null) continue;

                    var oldPos = child.Position;

                    var newPos = pivot + delta.Multiply(oldPos - pivot);

                    //IMPORTANT: do NOT use `child.Orientation = newOri` (nor read `child.Scale`)
                    //here. Bounds.Orientation's setter does `m.ScaleVector = Transform.ScaleVector`
                    //to preserve scale, but SharpDX's Matrix.ScaleVector getter/setter are both
                    //diagonal-only (M11/M22/M33) - correct only for an axis-aligned matrix. For any
                    //child whose Transform already contains a real rotation, the getter returns a
                    //bogus "scale" derived from rotation terms, and the setter then stamps that
                    //bogus value back onto the diagonal, corrupting the rotation entirely (verified
                    //empirically - see BoundsRelocatorVerification, both the direct
                    //Bounds.Orientation-setter test and the initial version of this method that
                    //tried to read/reapply Scale). This is a pre-existing bug in the codebase's
                    //Matrix Scale property, not something introduced here.
                    //
                    //The robust fix is to never decompose scale at all: compose the rigid `delta`
                    //rotation directly against the existing transform matrix via matrix
                    //multiplication, which preserves whatever was truly baked into Transform's
                    //linear part (rotation, and any real scale) exactly, with no decomposition.
                    //Row-vector convention (v' = v*M, matching this codebase's own Matrix.Multiply
                    //extension): M_a*M_b means "apply M_a first, then M_b" - so "apply the child's
                    //existing local->world transform first, then apply delta on top in world space"
                    //is oldLinear * deltaMat, in that order.
                    var oldLinear = child.Transform;
                    oldLinear.TranslationVector = SharpDX.Vector3.Zero; //isolate the rotation(+scale) part
                    var deltaMat = delta.ToMatrix();
                    var newLinear = oldLinear * deltaMat;
                    newLinear.TranslationVector = newPos;

                    child.Transform = newLinear;
                    child.TransformInv = SharpDX.Matrix.Invert(newLinear);
                }
            }
            ResyncCompositeRootBounds(comp);
        }

        /// <summary>
        /// The gap CodeWalker never fills: BoundComposite.BuildBVH() only ever assigns
        /// this.BVH from its children's transformed boxes - it never touches the
        /// composite's own BoxMin/BoxMax/BoxCenter/SphereCenter/SphereRadius. This
        /// reuses the exact same per-child "local box -> world box" transform BuildBVH
        /// already trusts (SharpDX's BoundingBox.Transform, which handles all 8 corners,
        /// not just the two corner points) to union all children into the root's bounds.
        /// </summary>
        private static void ResyncCompositeRootBounds(BoundComposite comp)
        {
            comp.BuildBVH(); //refresh the composite's own internal BVH tree/pointer (safe when null - composites under 6 children never get one)

            var children = comp.Children?.data_items;
            if (children == null) return;

            var min = new SharpDX.Vector3(float.MaxValue);
            var max = new SharpDX.Vector3(float.MinValue);
            bool any = false;

            foreach (var child in children)
            {
                if (child == null) continue;

                var localBox = new SharpDX.BoundingBox(child.BoxMin, child.BoxMax);
                var worldBox = localBox.Transform(child.Transform); //correct 8-corner AABB transform, same call BuildBVH() already makes

                min = SharpDX.Vector3.Min(min, worldBox.Minimum);
                max = SharpDX.Vector3.Max(max, worldBox.Maximum);
                any = true;
            }

            if (!any)
            {
                min = SharpDX.Vector3.Zero;
                max = SharpDX.Vector3.Zero;
            }

            comp.BoxMin = min;
            comp.BoxMax = max;
            comp.BoxCenter = (min + max) * 0.5f;

            //Composite root SphereCenter/SphereRadius are already observed to be
            //inconsistent with BoxCenter in real files (the engine doesn't appear to lean
            //on them precisely for composites) - keep them self-consistent with the
            //resynced box rather than guess at an undocumented original formula.
            comp.SphereCenter = comp.BoxCenter;
            comp.SphereRadius = (max - comp.BoxCenter).Length();
        }


        // ---------------------------------------------------------------
        // Bare root (BoundGeometry/BoundBVH, no composite wrapper): Transform is
        // permanently identity here (nothing ever applies it for a root), so the
        // "placement" data IS the local shape data.
        // ---------------------------------------------------------------

        private static void TranslateBareGeometry(BoundGeometry geom, SharpDX.Vector3 delta)
        {
            //GetVertexPos(i) = Transform . (Vertices[i] + CenterGeom); Transform is
            //identity for a root, so shifting CenterGeom alone moves every vertex for
            //free. Vertices[]/VerticesShrunk[] are relative to CenterGeom and are
            //translation-invariant - left untouched. Quantum depends only on
            //(BoxMax-BoxMin), also translation-invariant - left untouched.
            geom.CenterGeom = geom.CenterGeom + delta;
            geom.BoxCenter = geom.BoxCenter + delta;
            geom.SphereCenter = geom.SphereCenter + delta;
            geom.BoxMin = geom.BoxMin + delta;
            geom.BoxMax = geom.BoxMax + delta;

            if (geom is BoundBVH bvh)
            {
                bvh.BuildBVH(false); //cheap safety-net resync of the polygon tree + box/sphere fields; updateParent:false since this bound has no owning composite to notify
            }
        }

        private static void RotateBareGeometry(BoundGeometry geom, SharpDX.Vector3 pivot, SharpDX.Quaternion delta)
        {
            //No Transform slot to absorb this - has to be baked into the actual vertex
            //data. Every vertex's WORLD position rotates rigidly about the pivot; each
            //vertex is then re-expressed relative to the (also rotated) new CenterGeom.
            var oldCenterGeom = geom.CenterGeom;
            var newCenterGeom = pivot + delta.Multiply(oldCenterGeom - pivot);

            RotateVertexArrayInPlace(geom.Vertices, oldCenterGeom, newCenterGeom, pivot, delta);
            RotateVertexArrayInPlace(geom.VerticesShrunk, oldCenterGeom, newCenterGeom, pivot, delta);

            geom.CenterGeom = newCenterGeom;

            if (geom is BoundBVH bvh)
            {
                //Rebuilds the polygon BVH tree AND re-derives BoxMin/Max/BoxCenter/
                //SphereCenter/SphereRadius from GetVertexPos (Bounds.cs ~2673-2677) -
                //BoundPolygon.BoxMin/BoxMax are computed live from vertex positions
                //(abstract get-only properties, not cached fields), so this is correct
                //as long as it runs AFTER the vertex rebake above, which it does.
                bvh.BuildBVH(false);
            }
            else
            {
                //Plain BoundGeometry (Type == Geometry) has no BuildBVH - resync by hand
                //from the rebaked vertices.
                var min = new SharpDX.Vector3(float.MaxValue);
                var max = new SharpDX.Vector3(float.MinValue);
                var verts = geom.Vertices;
                if (verts != null)
                {
                    foreach (var v in verts)
                    {
                        var wp = v + newCenterGeom;
                        min = SharpDX.Vector3.Min(min, wp);
                        max = SharpDX.Vector3.Max(max, wp);
                    }
                }
                else
                {
                    min = newCenterGeom;
                    max = newCenterGeom;
                }

                var margin = new SharpDX.Vector3(geom.Margin);
                geom.BoxMin = min - margin;
                geom.BoxMax = max + margin;
                geom.BoxCenter = (geom.BoxMin + geom.BoxMax) * 0.5f;
                geom.SphereCenter = geom.BoxCenter;
                geom.SphereRadius = (geom.BoxMax - geom.BoxCenter).Length();
            }

            geom.CalculateQuantum(); //public, Bounds.cs:2101 - safe any time after BoxMin/Max are updated. Rotating an AABB can change its half-extents even though it preserves every pairwise vertex distance, so this can legitimately change and must run before Save() re-quantizes Vertices[] using it.
        }

        private static void RotateVertexArrayInPlace(SharpDX.Vector3[] verts, SharpDX.Vector3 oldCenterGeom, SharpDX.Vector3 newCenterGeom, SharpDX.Vector3 pivot, SharpDX.Quaternion delta)
        {
            if (verts == null) return;
            for (int i = 0; i < verts.Length; i++)
            {
                var worldPos = verts[i] + oldCenterGeom; //== GetVertexPos(i), Transform is identity for a root
                var newWorldPos = pivot + delta.Multiply(worldPos - pivot);
                verts[i] = newWorldPos - newCenterGeom;
            }
        }
    }
}
