# SprunkMapper 1.3.0

## New: lock files in place

You can now lock a project's `.ymap` or `.ybn` so it can't be moved by accident while you work on
the rest of the map:

- Right-click a ymap or ybn in the **Project Explorer** and choose **Lock file in place**. With
  several files selected, the menu locks or unlocks them all at once.
- Locked files show as **[locked]** and greyed out in the Project Explorer.
- **Select All Props** leaves locked files out. The confirmation tells you how many were skipped.
- Anything inside a locked file can still be selected and inspected, but the move widget is hidden
  and it can't be moved, rotated or scaled. If a multi-selection contains even one locked item, the
  whole selection stays put.
- Locks are saved in the project file, so they're still there next time you open it, including
  after renaming a file or using **Save As**.
- To change a locked file again, right-click it and choose **Unlock file**.

Note: once a file is locked, undoing a move you made to it *before* locking does nothing. Unlock it
first if you need to undo that move.

## New: custom backdrops

Besides Roxwood and Las Venturas, you can now load any other mapping resource as a reference
backdrop:

- Click the new **Custom ▾** button next to **Show Roxwood** on the **World** tab.
- **Add folder...** picks a resource folder, adds it to the list and shows it straight away. Tick or untick a folder in the
  menu to show or hide it, and use **Remove** to take it off the list.
- Custom backdrops behave like Roxwood and Las Venturas: they're visual only (no collision), drawn
  for reference, and can't be selected or edited.
- Your list of folders is remembered between sessions. Like Roxwood and Las Venturas, each
  backdrop starts hidden when you launch the app.

---

*Since 1.2.1.*
