# SprunkMapper 1.4.0

## New: Git tab

The floating panel on the right has a new **Git** tab, so you can share your work with the team
without leaving SprunkMapper. It works with a saved project, **Open Folder** and **Open Files**: it
finds the git repository the opened files are in and shows it at the top of the tab.

- **Pull** gets your teammates' latest work. Reload the project afterwards to see it.
- **Save to GitHub** sends your work in one click:
  - A window lists every changed file. Modified files and new mapping files (`.ymap`, `.ytyp`,
    `.ydr`, `.ytd`, `.ybn`, ...) are ticked; other new files, like backups or exports, aren't.
  - The message is filled in for you ("Update sprunk_joe.ymap, ..."), so you can just press Enter.
  - It then commits the ticked files, pulls your teammates' work and pushes. If a step fails, it
    stops and shows why.
  - New `.ymap` and `.ytyp` files you send are locked to you automatically, so you can keep editing
    them.
  - If a file still has unsaved changes in SprunkMapper, you're warned first.
- **Discard local changes...** throws away every uncommitted change to your files. It lists the
  files and asks for confirmation first, since this can't be undone.
- **Lock** / **Unlock** claims or releases the ymap picked in the list on GitHub, so only you can
  edit it. **Locks** shows who holds which file.

Everything git prints shows up in the box at the bottom of the tab.

Note: these git locks are separate from **Lock file in place** in the Project Explorer, which only
stops props from being moved inside SprunkMapper.

## Changed: Save All only saves edited files

**Save All** used to write every file in the project back to disk, even ones you hadn't touched.
In a shared repository, that made untouched files look changed and caused merge conflicts for the
whole team. It now only saves the files you actually edited.

## Changed: saving a file you haven't locked

In a repository that uses Git LFS locking, `.ymap` and `.ytyp` files are read-only until you lock
them. Saving one used to crash. Now SprunkMapper asks **"Lock it now and save?"**:

- **Yes** locks the file on GitHub and saves it.
- If a teammate already holds the lock, you're told and the file isn't saved. Your changes stay in
  SprunkMapper, so nothing is lost.
- **No** cancels the save, and the file stays marked as unsaved.

The Git tab needs [Git for Windows](https://git-scm.com/download/win) installed (it includes
Git LFS).

---

*Since 1.3.0.*
