using System;
using System.IO;
using System.Linq;
using CodeWalker;
using CodeWalker.GameFiles;
using SharpDX;

int pass = 0, fail = 0;

void Check(string name, bool ok, string detail = null)
{
    if (ok) { pass++; Console.WriteLine($"  [PASS] {name}"); }
    else { fail++; Console.WriteLine($"  [FAIL] {name}" + (detail != null ? $"  -- {detail}" : "")); }
}

bool VecClose(Vector3 a, Vector3 b, float eps = 0.0005f) => (a - b).Length() < eps;
bool FloatClose(float a, float b, float eps = 0.0005f) => Math.Abs(a - b) < eps;

byte[] SaveReload(YbnFile ybn, out YbnFile reloaded)
{
    var data = ybn.Save();
    reloaded = new YbnFile();
    reloaded.Load(data);
    return data;
}

void TestFixture(string label, string path)
{
    Console.WriteLine("=====================================================");
    Console.WriteLine("FIXTURE: " + label);

    var ybn = new YbnFile();
    ybn.Load(File.ReadAllBytes(path));
    var comp = ybn.Bounds as BoundComposite;
    if (comp == null) { Console.WriteLine("  not a BoundComposite root, skipping"); return; }

    var children = comp.Children?.data_items?.Where(c => c != null).ToArray() ?? Array.Empty<Bounds>();
    Console.WriteLine($"  {children.Length} children, root BVH != null: {comp.BVH != null}");

    // ---- snapshot local (per-child) data that MUST be untouched by a composite move ----
    var snapBoxMin = children.Select(c => c.BoxMin).ToArray();
    var snapBoxMax = children.Select(c => c.BoxMax).ToArray();
    var snapBoxCenter = children.Select(c => c.BoxCenter).ToArray();
    var snapSphereCenter = children.Select(c => c.SphereCenter).ToArray();
    var snapSphereRadius = children.Select(c => c.SphereRadius).ToArray();
    var snapCenterGeom = children.Select(c => (c as BoundGeometry)?.CenterGeom ?? Vector3.Zero).ToArray();
    var snapQuantum = children.Select(c => (c as BoundGeometry)?.Quantum ?? Vector3.Zero).ToArray();
    var snapVertCount = children.Select(c => (c as BoundGeometry)?.Vertices?.Length ?? -1).ToArray();
    var snapVert0 = children.Select(c => (c as BoundGeometry)?.Vertices?.FirstOrDefault() ?? Vector3.Zero).ToArray();
    var snapPos = children.Select(c => c.Position).ToArray();
    var snapOri = children.Select(c => c.Orientation).ToArray();

    var delta = new Vector3(1500f, -800f, 25f);

    Console.WriteLine("-- Translate --");
    BoundsRelocator.Translate(comp, delta);

    for (int i = 0; i < children.Length; i++)
    {
        var c = children[i];
        Check($"child[{i}] Position shifted by delta", VecClose(c.Position, snapPos[i] + delta));
        Check($"child[{i}] Orientation unchanged", c.Orientation == snapOri[i]);
        Check($"child[{i}] local BoxMin unchanged", VecClose(c.BoxMin, snapBoxMin[i]));
        Check($"child[{i}] local BoxMax unchanged", VecClose(c.BoxMax, snapBoxMax[i]));
        Check($"child[{i}] local BoxCenter unchanged", VecClose(c.BoxCenter, snapBoxCenter[i]));
        Check($"child[{i}] local SphereCenter unchanged", VecClose(c.SphereCenter, snapSphereCenter[i]));
        Check($"child[{i}] SphereRadius unchanged", FloatClose(c.SphereRadius, snapSphereRadius[i]));
        if (c is BoundGeometry bg)
        {
            Check($"child[{i}] CenterGeom unchanged (local, translation-invariant)", VecClose(bg.CenterGeom, snapCenterGeom[i]));
            Check($"child[{i}] Quantum unchanged", VecClose(bg.Quantum, snapQuantum[i]));
            Check($"child[{i}] vertex count unchanged", bg.Vertices?.Length == snapVertCount[i]);
            Check($"child[{i}] raw vertex[0] unchanged (no vertex rebake)", VecClose(bg.Vertices?.FirstOrDefault() ?? Vector3.Zero, snapVert0[i]));
        }
    }

    // independently re-derive expected root box as union of transformed children boxes
    var emin = new Vector3(float.MaxValue); var emax = new Vector3(float.MinValue);
    foreach (var c in children)
    {
        var wb = new BoundingBox(c.BoxMin, c.BoxMax).Transform(c.Transform);
        emin = Vector3.Min(emin, wb.Minimum); emax = Vector3.Max(emax, wb.Maximum);
    }
    Check("root BoxMin == independently re-derived union", VecClose(comp.BoxMin, emin));
    Check("root BoxMax == independently re-derived union", VecClose(comp.BoxMax, emax));
    Check("root BoxCenter == midpoint of resynced box", VecClose(comp.BoxCenter, (emin + emax) * 0.5f));

    Console.WriteLine("-- Round trip through Save()/Load() --");
    var savedPositions = children.Select(c => c.Position).ToArray();
    var savedOrientations = children.Select(c => c.Orientation).ToArray();
    var savedRootBoxMin = comp.BoxMin;
    var savedRootBoxMax = comp.BoxMax;

    SaveReload(ybn, out var reloaded);
    var rcomp = reloaded.Bounds as BoundComposite;
    var rchildren = rcomp.Children?.data_items?.Where(c => c != null).ToArray() ?? Array.Empty<Bounds>();
    Check("reloaded child count matches", rchildren.Length == children.Length);
    for (int i = 0; i < Math.Min(rchildren.Length, children.Length); i++)
    {
        Check($"reloaded child[{i}] Position matches saved", VecClose(rchildren[i].Position, savedPositions[i]));
        Check($"reloaded child[{i}] Orientation matches saved", QuatClose(rchildren[i].Orientation, savedOrientations[i]));
    }
    Check("reloaded root BoxMin matches saved", VecClose(rcomp.BoxMin, savedRootBoxMin));
    Check("reloaded root BoxMax matches saved", VecClose(rcomp.BoxMax, savedRootBoxMax));

    Console.WriteLine("-- XML round trip --");
    var sb = new System.Text.StringBuilder();
    Bounds.WriteXmlNode(comp, sb, 0);
    var doc = new System.Xml.XmlDocument();
    doc.LoadXml("<Root>" + sb.ToString() + "</Root>");
    var bnode = doc.DocumentElement.FirstChild;
    var xcomp = Bounds.ReadXmlNode(bnode, null) as BoundComposite;
    Check("XML round trip root BoxMin matches", VecClose(xcomp.BoxMin, savedRootBoxMin));
    Check("XML round trip root BoxMax matches", VecClose(xcomp.BoxMax, savedRootBoxMax));
    var xchildren = xcomp.Children?.data_items?.Where(c => c != null).ToArray() ?? Array.Empty<Bounds>();
    for (int i = 0; i < Math.Min(xchildren.Length, children.Length); i++)
    {
        Check($"XML child[{i}] Position matches", VecClose(xchildren[i].Position, savedPositions[i]));
    }

    Console.WriteLine("-- Rotate (fresh reload, 37 deg about pivot = original root center) --");
    var ybn2 = new YbnFile();
    ybn2.Load(File.ReadAllBytes(path));
    var comp2 = (BoundComposite)ybn2.Bounds;
    var children2 = comp2.Children.data_items.Where(c => c != null).ToArray();
    var pivot = comp2.BoxCenter;
    var rot = Quaternion.RotationAxis(Vector3.UnitZ, MathUtil.DegreesToRadians(37f));

    var preLocalBoxMin = children2.Select(c => c.BoxMin).ToArray();
    var prePos = children2.Select(c => c.Position).ToArray();
    var preOri = children2.Select(c => c.Orientation).ToArray();

    BoundsRelocator.Rotate(comp2, pivot, rot);

    for (int i = 0; i < children2.Length; i++)
    {
        var c = children2[i];
        var expectedPos = pivot + rot.Multiply(prePos[i] - pivot);
        var expectedOri = Quaternion.Normalize(Quaternion.Multiply(rot, preOri[i]));
        Check($"child[{i}] rotated Position matches hand-derived expectation", VecClose(c.Position, expectedPos));
        Check($"child[{i}] rotated Orientation matches hand-derived expectation", QuatClose(c.Orientation, expectedOri));
        Check($"child[{i}] local BoxMin still unchanged after rotate", VecClose(c.BoxMin, preLocalBoxMin[i]));
    }
    var emin2 = new Vector3(float.MaxValue); var emax2 = new Vector3(float.MinValue);
    foreach (var c in children2)
    {
        var wb = new BoundingBox(c.BoxMin, c.BoxMax).Transform(c.Transform);
        emin2 = Vector3.Min(emin2, wb.Minimum); emax2 = Vector3.Max(emax2, wb.Maximum);
    }
    Check("rotated root BoxMin == independently re-derived union", VecClose(comp2.BoxMin, emin2));
    Check("rotated root BoxMax == independently re-derived union", VecClose(comp2.BoxMax, emax2));
}

bool QuatClose(Quaternion a, Quaternion b, float eps = 0.0005f)
{
    // q and -q represent the same rotation
    var d1 = Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z) + Math.Abs(a.W - b.W);
    var d2 = Math.Abs(a.X + b.X) + Math.Abs(a.Y + b.Y) + Math.Abs(a.Z + b.Z) + Math.Abs(a.W + b.W);
    return Math.Min(d1, d2) < eps;
}

void TestSyntheticRotatedChild()
{
    Console.WriteLine("=====================================================");
    Console.WriteLine("SYNTHETIC: composite child with a genuinely non-identity transform (real fixtures only have identity)");

    var comp = new BoundComposite();
    var child = new BoundBox();
    child.BoxMin = new Vector3(-1, -1, -1);
    child.BoxMax = new Vector3(1, 1, 1);
    child.BoxCenter = Vector3.Zero;
    child.SphereCenter = Vector3.Zero;
    child.SphereRadius = 1.7320508f;

    // child starts rotated 30 deg about X and offset -- deliberately a DIFFERENT axis
    // than the group rotation below, so a wrong quaternion multiplication order would
    // actually be caught (same-axis rotations commute and would hide that bug).
    var initialOri = Quaternion.RotationAxis(Vector3.UnitX, MathUtil.DegreesToRadians(30f));
    var initialPos = new Vector3(10f, 0f, 0f);
    var initM = initialOri.ToMatrix();
    initM.TranslationVector = initialPos;
    child.Transform = initM;
    child.TransformInv = Matrix.Invert(initM);
    child.Parent = comp;
    comp.Children = new ResourcePointerArray64<Bounds> { data_items = new Bounds[] { child } };

    // an arbitrary point fixed in the child's OWN local frame -- tracking where this
    // physical point ends up in world space is a convention-independent ground truth
    // that doesn't depend on guessing SharpDX's quaternion multiplication order.
    var localLandmark = new Vector3(1f, 0f, 0f);
    Vector3 WorldLandmark() => child.Position + child.Orientation.Multiply(localLandmark);

    Console.WriteLine("-- Translate --");
    var oldWorldLandmark = WorldLandmark();
    var oldOri = child.Orientation;
    var delta = new Vector3(5f, -2f, 1f);
    BoundsRelocator.Translate(comp, delta);
    Check("landmark moved by exactly delta", VecClose(WorldLandmark(), oldWorldLandmark + delta));
    Check("child rotation untouched by translate", QuatClose(child.Orientation, oldOri));

    Console.WriteLine("-- Rotate about an off-center pivot --");
    oldWorldLandmark = WorldLandmark();
    var pivot = new Vector3(3f, -1f, 2f); // off-center on purpose
    var rot = Quaternion.RotationAxis(Vector3.UnitZ, MathUtil.DegreesToRadians(45f));
    BoundsRelocator.Rotate(comp, pivot, rot);

    var expectedWorldLandmark = pivot + rot.Multiply(oldWorldLandmark - pivot);
    Check("rotated landmark lands at the geometrically correct world position " +
          "(validates position formula AND quaternion composition order together, " +
          "independent of any assumed multiplication convention)",
        VecClose(WorldLandmark(), expectedWorldLandmark, 0.001f),
        $"actual={WorldLandmark()} expected={expectedWorldLandmark}");
    Check("synthetic child local BoxMin/Max untouched", VecClose(child.BoxMin, new Vector3(-1, -1, -1)) && VecClose(child.BoxMax, new Vector3(1, 1, 1)));
}

void TestSyntheticBareRoot()
{
    Console.WriteLine("=====================================================");
    Console.WriteLine("SYNTHETIC: bare (non-composite) BoundGeometry root -- no real fixture has one");

    BoundGeometry MakeGeom()
    {
        var g = new BoundGeometry();
        g.CenterGeom = new Vector3(100f, 200f, 10f);
        g.Vertices = new[] {
            new Vector3(-1f, -1f, -1f), new Vector3(1f, -1f, -1f),
            new Vector3(1f, 1f, -1f),  new Vector3(-1f, 1f, 2f),
        };
        g.BoxMin = new Vector3(-1f, -1f, -1f);
        g.BoxMax = new Vector3(1f, 1f, 2f);
        g.BoxCenter = (g.BoxMin + g.BoxMax) * 0.5f;
        g.SphereCenter = g.BoxCenter;
        g.SphereRadius = (g.BoxMax - g.BoxCenter).Length();
        g.Margin = 0.01f;
        g.CalculateQuantum();
        return g;
    }

    Console.WriteLine("-- Translate --");
    var geom = MakeGeom();
    var oldWorld = Enumerable.Range(0, geom.Vertices.Length).Select(i => geom.GetVertexPos(i)).ToArray();
    var oldQuantum = geom.Quantum;
    var delta = new Vector3(500f, -300f, 12f);
    BoundsRelocator.Translate(geom, delta);
    for (int i = 0; i < geom.Vertices.Length; i++)
        Check($"bare geom vertex[{i}] world position shifted by exactly delta", VecClose(geom.GetVertexPos(i), oldWorld[i] + delta));
    Check("bare geom BoxMin shifted by delta", VecClose(geom.BoxMin, new Vector3(-1f, -1f, -1f) + delta));
    Check("bare geom Quantum unchanged by pure translation", VecClose(geom.Quantum, oldQuantum));

    Console.WriteLine("-- Rotate about an off-center pivot --");
    var geom2 = MakeGeom();
    var oldWorld2 = Enumerable.Range(0, geom2.Vertices.Length).Select(i => geom2.GetVertexPos(i)).ToArray();
    var pivot = geom2.CenterGeom + new Vector3(5f, 5f, 0f); // deliberately off the geometry's own center
    var rot = Quaternion.RotationAxis(Vector3.UnitY, MathUtil.DegreesToRadians(60f));
    BoundsRelocator.Rotate(geom2, pivot, rot);

    var newWorld2 = Enumerable.Range(0, geom2.Vertices.Length).Select(i => geom2.GetVertexPos(i)).ToArray();
    for (int i = 0; i < oldWorld2.Length; i++)
    {
        var expected = pivot + rot.Multiply(oldWorld2[i] - pivot);
        Check($"bare geom rotated vertex[{i}] lands at the geometrically correct world position", VecClose(newWorld2[i], expected, 0.001f));
    }
    var emin = new Vector3(float.MaxValue); var emax = new Vector3(float.MinValue);
    foreach (var wp in newWorld2) { emin = Vector3.Min(emin, wp); emax = Vector3.Max(emax, wp); }
    var margin = new Vector3(geom2.Margin);
    Check("bare geom rotated BoxMin matches independently re-derived vertex union (- margin)", VecClose(geom2.BoxMin, emin - margin, 0.01f));
    Check("bare geom rotated BoxMax matches independently re-derived vertex union (+ margin)", VecClose(geom2.BoxMax, emax + margin, 0.01f));
}

TestSyntheticRotatedChild();
TestSyntheticBareRoot();

TestFixture("hei_nteamcarrier_1.ybn (8-child composite, has root BVH)",
    @"C:\Users\franc\source\repos\Workspace\TestMappings\cfx-nteam-acrt\stream\hei_nteamcarrier_1.ybn");

TestFixture("nteam_boat_interiorshell1col.ybn (1-child composite, no root BVH)",
    @"C:\Users\franc\source\repos\Workspace\TestMappings\cfx-nteam-acrt\stream\nteam_boat_interiorshell1col.ybn");

Console.WriteLine("=====================================================");
Console.WriteLine($"TOTAL: {pass} passed, {fail} failed");
Environment.Exit(fail == 0 ? 0 : 1);
