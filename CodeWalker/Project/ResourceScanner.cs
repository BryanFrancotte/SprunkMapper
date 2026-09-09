using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeWalker.Project
{
    public static class ResourceScanner
    {
        public static string[] FindMappingFiles(string folder)
        {
            var scanRoot = Directory.Exists(Path.Combine(folder, "stream"))
                ? Path.Combine(folder, "stream") : folder;

            var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".ymap", ".ytyp", ".ybn" };

            return Directory.GetFiles(scanRoot, "*", SearchOption.AllDirectories)
                .Where(f => exts.Contains(Path.GetExtension(f)))
                .ToArray();
        }
    }
}
