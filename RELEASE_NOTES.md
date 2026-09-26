# SprunkMapper 1.2.0

## New: Collision mover

Standalone static collision (`.ybn`) files can now be moved and rotated directly in the World view,
instead of staying fixed at their original position:

- Select a `.ybn` and drag it with the move widget - the whole collision mesh translates with it,
  bounding boxes update immediately, and undo/redo work as expected.
- Use **Select All Props** to grab a ymap's entities together with their loaded exterior collision, then
  rotate the whole group as one - collision rotates in sync with the props around them. A single `.ybn`
  on its own can't be rotated (it has no saved orientation to rotate from), and no `.ybn` can be scaled;
  the tool switches back to the move widget and explains why if you try.
- Interior collision that belongs to an MLO is left out of Select All Props automatically, since it
  already follows its MLO entity and must not be moved on its own.
- Moving or rotating a `.ybn` this way now correctly marks its project file as changed, so it gets
  saved.

---

*Since 1.1.0.*
