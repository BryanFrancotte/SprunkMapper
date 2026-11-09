using CodeWalker.GameFiles;
using SharpDX;
using System;
using System.Collections.Generic;
using System.IO;

namespace CodeWalker.Project.Verification
{
    //finds what an issue points at in the world, so the results list can take you there
    public static class VerificationTargets
    {
        public static YmapEntityDef[] FindEntities(ProjectFile project, Func<YmapEntityDef, bool> match)
        {
            var list = new List<YmapEntityDef>();
            if (project?.YmapFiles == null) return list.ToArray();

            foreach (var ymap in project.YmapFiles)
            {
                var entities = ymap?.AllEntities;
                if (entities == null) continue;
                foreach (var ent in entities)
                {
                    if ((ent != null) && match(ent)) list.Add(ent);
                }
            }
            return list.ToArray();
        }

        //project entities placing the archetype with this name hash
        public static YmapEntityDef[] FindEntitiesUsingArchetype(ProjectFile project, uint archetypeHash)
        {
            return FindEntities(project, ent => ent._CEntityDef.archetypeName.Hash == archetypeHash);
        }

        public static uint FileNameHash(string fileName)
        {
            return JenkHash.GenHash(Path.GetFileNameWithoutExtension(fileName ?? string.Empty).ToLowerInvariant());
        }

        public static BoundingBox? GetBounds(YmapEntityDef[] entities)
        {
            if ((entities == null) || (entities.Length == 0)) return null;
            var bbmin = new Vector3(float.MaxValue);
            var bbmax = new Vector3(float.MinValue);
            foreach (var ent in entities)
            {
                bbmin = Vector3.Min(bbmin, ent.Position);
                bbmax = Vector3.Max(bbmax, ent.Position);
                if (ent.Archetype != null) //BBMin/BBMax are only valid once the archetype is resolved
                {
                    bbmin = Vector3.Min(bbmin, ent.BBMin);
                    bbmax = Vector3.Max(bbmax, ent.BBMax);
                }
            }
            return new BoundingBox(bbmin, bbmax);
        }
    }
}
