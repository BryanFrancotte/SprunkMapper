# SprunkMapper 1.1.0

## New: Verification Service

A new **Tools → Verification Service...** panel checks the currently open project against the base
game before you ship a map, catching two common causes of broken installs:

- **Base game conflicts** - flags any custom `.ydr`, `.ytd`, `.ybn`, `.ymap`, or archetype (`.ytyp`)
  in your project that shares a name with a base game file, which would otherwise silently override
  or clash with it at runtime.
- **Ymap entity radius check** - warns when an entity in a `.ymap` sits more than 1000m from that
  ymap's center, which usually indicates a misplaced prop or a map that should be split up.

Run it from the Tools menu; results are listed with severity (Warning/Error), the rule that fired,
the affected file, and a description of the issue.

## New: safer moving and selection tools

- **Block vanilla prop select** - a new checkbox in the World panel excludes base game props from
  mouse selection, so you can only select entities that belong to the current project's ymaps.
  Useful when moving/editing props near vanilla map geometry without accidentally grabbing it.
- **Select All Props** now warns before moving anything if the project has static collision (`.ybn`)
  files loaded, since moving props does not move their collision - it stays behind at its original
  position.

---

*Since 1.0.0.*
