using SharpDX;

namespace CodeWalker.GameFiles
{
    public static class BoundsTranslator
    {
        //Moves a whole standalone bounds hierarchy (a ybn root) by delta, in world space.
        //Identity-transform geometries are moved through CenterGeom so their quantized
        //vertices stay byte-identical; anything already carrying a transform is moved
        //through that transform, which keeps whatever box convention the file was saved with.
        public static bool Translate(Bounds root, Vector3 delta)
        {
            if (root == null || root.Parent != null) return false;

            if (root is BoundComposite comp)
            {
                var children = comp.Children?.data_items;
                if (children != null)
                {
                    foreach (var child in children)
                    {
                        if (child == null) continue;
                        if (child is BoundComposite) return false; //not found in real ybns, not supported
                    }
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
            if (root is BoundGeometry geom)
            {
                ShiftGeometry(geom, delta);
                return true;
            }
            return false;
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
    }
}
