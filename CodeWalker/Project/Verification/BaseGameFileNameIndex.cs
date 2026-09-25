using CodeWalker.GameFiles;
using System.Collections.Generic;

namespace CodeWalker.Project.Verification
{
    public static class BaseGameFileNameIndex
    {
        /// <summary>
        /// Returns the lowercase filenames (with extension) of every base-game (non-DLC, non-mod)
        /// RPF entry matching the given lowercase extension, e.g. ".ydr".
        /// </summary>
        public static HashSet<string> GetBaseGameFileNames(RpfManager rpfMan, string extensionLower)
        {
            var names = new HashSet<string>();
            if (rpfMan?.BaseRpfs == null) return names;

            foreach (var rpf in rpfMan.BaseRpfs)
            {
                if (rpf?.AllEntries == null) continue;
                foreach (var entry in rpf.AllEntries)
                {
                    if (entry is RpfFileEntry fentry && fentry.NameLower != null && fentry.NameLower.EndsWith(extensionLower))
                    {
                        names.Add(fentry.NameLower);
                    }
                }
            }
            return names;
        }
    }
}
