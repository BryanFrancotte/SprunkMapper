using CodeWalker.GameFiles;
using SharpDX;
using System.Collections.Generic;

namespace CodeWalker.Project.Verification
{
    public class YmapEntityRadiusRule : IVerificationRule
    {
        private const float MaxRadius = 1000.0f;

        public string Name => "Ymap entity radius";

        public IEnumerable<VerificationIssue> Run(ProjectFile project, GameFileCache gameFileCache)
        {
            var issues = new List<VerificationIssue>();
            if (project?.YmapFiles == null) return issues;

            foreach (var ymap in project.YmapFiles)
            {
                var entities = ymap?.AllEntities;
                if (entities == null || entities.Length == 0) continue;

                var fileName = ymap.RpfFileEntry?.NameLower ?? ymap.Name;

                var center = Vector3.Zero;
                for (int i = 0; i < entities.Length; i++)
                {
                    center += entities[i].Position;
                }
                center /= entities.Length;

                foreach (var entity in entities)
                {
                    var dist = Vector3.Distance(entity.Position, center);
                    if (dist > MaxRadius)
                    {
                        var entityName = entity.Archetype?.Name ?? "unknown archetype";
                        issues.Add(new VerificationIssue
                        {
                            Severity = VerificationSeverity.Warning,
                            RuleName = Name,
                            FileName = fileName,
                            Message = $"Entity '{entityName}' is {dist:N0}m from the ymap's center, outside the {MaxRadius:N0}m radius."
                        });
                    }
                }
            }

            return issues;
        }
    }
}
