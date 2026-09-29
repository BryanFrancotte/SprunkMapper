# SprunkMapper 1.2.1

## Fixed: Select All Props moved interior collision

In 1.2.0, **Select All Props** was meant to leave interior collision (the `.ybn` files belonging to an
MLO) out of the selection, but it never recognised them in a project. Moving the group moved that
collision twice: once with its MLO and once on its own. It then ended up offset from the interior.

- Interior `.ybn` files in a project are now matched to their MLO correctly and left out of
  **Select All Props**. They follow their MLO entity as intended.
- Saving a `.ybn` under a new name with **Save As** keeps that match working under the new name.

## New: version in the title bar

The main window's title now shows which version you're running (e.g. **SprunkMapper 1.2.1**), so
it's easy to check whether an update has been installed.

---

*Since 1.2.0.*
