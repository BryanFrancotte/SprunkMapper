using CodeWalker.GameFiles;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

namespace CodeWalker.Project.Panels
{
    public partial class RelocateResourcePanel : ProjectPanel, IGroupWidgetTarget
    {
        public ProjectForm ProjectForm { get; set; }
        public ProjectFile CurrentProjectFile { get; set; }

        //all the actual relocation logic lives here - this panel is just its UI
        private RelocateStagingSet Staged = new RelocateStagingSet();

        private bool previewing = false;
        private bool populatingui = false;

        public RelocateResourcePanel(ProjectForm projectForm)
        {
            ProjectForm = projectForm;
            InitializeComponent();
            Tag = "RelocateResourcePanel";

            DockStateChanged += RelocateResourcePanel_DockStateChanged;

            Staged.YbnGraphicsRefresh = RefreshYbnGraphics;

            if (ProjectForm?.WorldForm == null)
            {
                //no world view in this startup mode - the fields and Apply still work, just no gizmo
                PreviewCheckBox.Enabled = false;
                UpdateStatus("Viewport preview unavailable - World View is not open.");
            }
        }

        public void SetProject(ProjectFile project)
        {
            CurrentProjectFile = project;
        }

        private void RelocateResourcePanel_DockStateChanged(object sender, EventArgs e)
        {
            if (DockState == DockState.Hidden)
            {
                StopPreview(); //don't leave the world view's widget hijacked by a hidden panel
            }
        }

        private void RefreshYbnGraphics(YbnFile ybn)
        {
            var b = ybn?.Bounds;
            if (b == null) return;

            var wf = ProjectForm?.WorldForm;
            if (wf != null)
            {
                wf.UpdateCollisionBoundsGraphics(b); //rebuilds the BVH and invalidates the cached render mesh
            }
            else
            {
                if (b is BoundBVH bvh) bvh.BuildBVH();
                else if (b is BoundComposite bc) bc.BuildBVH();
            }
        }


        // ---------------------------------------------------------------
        // Loading
        // ---------------------------------------------------------------

        private void LoadFolderButton_Click(object sender, EventArgs e)
        {
            if (ProjectForm == null) return;

            using (var fbd = new FolderBrowserDialog())
            {
                if (fbd.ShowDialogNew() != DialogResult.OK) return;
                var folder = fbd.SelectedPath;

                string[] files;
                try
                {
                    files = ResourceScanner.FindMappingFiles(folder);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Unable to scan folder: " + ex.Message);
                    return;
                }

                if (files.Length == 0)
                {
                    UpdateStatus("No .ymap/.ytyp/.ybn files found in:\n" + folder);
                    return;
                }

                StopPreview(); //the staged set is about to change, so any in-progress preview is void

                var preYmaps = new HashSet<YmapFile>(ProjectForm.CurrentProjectFile?.YmapFiles ?? new List<YmapFile>());
                var preYtyps = new HashSet<YtypFile>(ProjectForm.CurrentProjectFile?.YtypFiles ?? new List<YtypFile>());
                var preYbns = new HashSet<YbnFile>(ProjectForm.CurrentProjectFile?.YbnFiles ?? new List<YbnFile>());

                ProjectForm.OpenFiles(files);

                CurrentProjectFile = ProjectForm.CurrentProjectFile;
                if (CurrentProjectFile == null) return;

                var newYmaps = CurrentProjectFile.YmapFiles.Where(f => !preYmaps.Contains(f)).ToList();
                var newYtyps = CurrentProjectFile.YtypFiles.Where(f => !preYtyps.Contains(f)).ToList();
                var newYbns = CurrentProjectFile.YbnFiles.Where(f => !preYbns.Contains(f)).ToList();

                Staged.Add(newYmaps, newYtyps, newYbns);
                Staged.TakeBaselines();
                ResetTransformFields();

                UpdateStatus(
                    $"Loaded from: {folder}\n" +
                    $"New: {newYmaps.Count} ymap, {newYtyps.Count} ytyp, {newYbns.Count} ybn.\n" +
                    $"Staged: {Staged.Ymaps.Count} ymap ({Staged.EntityCount} entities), {Staged.Ybns.Count} ybn. Group centre: {FloatUtil.GetVector3String(Staged.Pivot)}");
            }
        }


        /// <summary>
        /// Stages everything already open in the project window, so a resource that's been loaded
        /// by any other means (project file, drag-drop, previous session) can be relocated without
        /// re-scanning its folder. Files already staged are skipped rather than double-added.
        /// </summary>
        private void StageProjectButton_Click(object sender, EventArgs e)
        {
            if (ProjectForm == null) return;

            CurrentProjectFile = ProjectForm.CurrentProjectFile;
            if (CurrentProjectFile == null)
            {
                UpdateStatus("No project is open - open or create a project first, or use Load Resource Folder.");
                return;
            }

            var already = new HashSet<object>();
            foreach (var y in Staged.Ymaps) already.Add(y);
            foreach (var t in Staged.Ytyps) already.Add(t);
            foreach (var b in Staged.Ybns) already.Add(b);

            //locked backdrop ymaps can't be added to a project in the first place, but skip them
            //defensively - they must never become relocatable
            var newYmaps = CurrentProjectFile.YmapFiles.Where(f => (f != null) && !already.Contains(f) && !f.IsLockedBackdrop).ToList();
            var newYtyps = CurrentProjectFile.YtypFiles.Where(f => (f != null) && !already.Contains(f)).ToList();
            var newYbns = CurrentProjectFile.YbnFiles.Where(f => (f != null) && !already.Contains(f)).ToList();

            if ((newYmaps.Count == 0) && (newYbns.Count == 0) && (newYtyps.Count == 0))
            {
                if (Staged.IsEmpty)
                {
                    UpdateStatus("The project has no ymap/ytyp/ybn files open to stage.");
                }
                else
                {
                    UpdateStatus($"Everything open in the project is already staged: {Staged.Ymaps.Count} ymap ({Staged.EntityCount} entities), {Staged.Ybns.Count} ybn.");
                    SelectStagedInWorld();
                }
                return;
            }

            StopPreview(); //the staged set is about to change, so any in-progress preview is void

            Staged.Add(newYmaps, newYtyps, newYbns);
            Staged.TakeBaselines();
            ResetTransformFields();

            SelectStagedInWorld();

            UpdateStatus(
                $"Staged from the project window: +{newYmaps.Count} ymap, +{newYtyps.Count} ytyp, +{newYbns.Count} ybn.\n" +
                $"Now staged: {Staged.Ymaps.Count} ymap ({Staged.EntityCount} entities), {Staged.Ybns.Count} ybn.\n" +
                $"Group centre: {FloatUtil.GetVector3String(Staged.Pivot)}. Use Go to / select group to find it.");
        }

        /// <summary>
        /// Drops everything staged without touching the files - lets the user start a fresh
        /// selection without closing the panel.
        /// </summary>
        private void ClearStagedButton_Click(object sender, EventArgs e)
        {
            if (Staged.IsEmpty)
            {
                UpdateStatus("Nothing staged.");
                return;
            }

            StopPreview();

            var wf = ProjectForm?.WorldForm;
            if (wf != null)
            {
                lock (wf.RenderSyncRoot)
                {
                    Staged.RestoreBaselines(); //don't leave a half-previewed move behind
                }
            }
            else
            {
                Staged.RestoreBaselines();
            }

            Staged = new RelocateStagingSet();
            Staged.YbnGraphicsRefresh = RefreshYbnGraphics;

            ResetTransformFields();
            UpdateStatus("Cleared - nothing staged. The files are back where they were.");
        }


        // ---------------------------------------------------------------
        // Viewport gizmo
        // ---------------------------------------------------------------

        private void StartPreview()
        {
            var wf = ProjectForm?.WorldForm;
            if (wf == null)
            {
                UpdateStatus("Viewport preview unavailable - World View is not open.");
                SetPreviewCheckBox(false);
                return;
            }
            if (Staged.IsEmpty)
            {
                UpdateStatus("Nothing staged yet - use Load Resource Folder first.");
                SetPreviewCheckBox(false);
                return;
            }

            previewing = true;
            MoveWidgetButton.Enabled = true;
            RotateWidgetButton.Enabled = true;

            SelectStagedInWorld();      //green boxes, so it's obvious what's about to move
            GoToGroup();                //otherwise the gizmo can easily be off-screen
            wf.ShowGroupWidget(this, Staged.WidgetPosition, false);
            UpdatePreviewLocked();

            UpdateStatus($"Previewing {Staged.EntityCount} entities and {Staged.Ybns.Count} ybn(s), centred on {FloatUtil.GetVector3String(Staged.WidgetPosition)}.\n" +
                         "Drag the widget in the World View to move the group, then press Apply to commit.\n" +
                         "Use the Move/Rotate buttons to switch between translation and yaw.");
        }

        private void StopPreview()
        {
            previewing = false;

            ProjectForm?.WorldForm?.HideGroupWidget(this);

            if (MoveWidgetButton != null) MoveWidgetButton.Enabled = false;
            if (RotateWidgetButton != null) RotateWidgetButton.Enabled = false;
            SetPreviewCheckBox(false);
        }

        private void UpdatePreviewLocked()
        {
            var wf = ProjectForm?.WorldForm;
            if (wf != null)
            {
                lock (wf.RenderSyncRoot) //the renderer reads this data every frame
                {
                    Staged.UpdatePreview();
                }
            }
            else
            {
                Staged.UpdatePreview();
            }
        }

        public void OnGroupWidgetPositionChange(Vector3 newpos, Vector3 oldpos)
        {
            //called on the render thread, which already holds the render lock
            if (!previewing) return;

            Staged.Offset = newpos - Staged.Pivot; //the widget sits on the transformed pivot: pivot + offset
            Staged.UpdatePreview();
            SyncFieldsToUI();
        }

        public void OnGroupWidgetRotationChange(Quaternion newrot, Quaternion oldrot)
        {
            //called on the render thread, which already holds the render lock
            if (!previewing) return;

            Staged.YawDegrees = RelocateStagingSet.YawDegreesFromQuaternion(newrot);
            Staged.UpdatePreview();
            SyncFieldsToUI();
        }

        /// <summary>
        /// Selects everything staged in the world view, so the user can see exactly what the gizmo
        /// is about to move. This is visual only - the actual move is handled by the group widget
        /// callbacks, since CodeWalker's own multi-selection move can't relocate a standalone ybn root.
        /// </summary>
        private void SelectStagedInWorld()
        {
            var wf = ProjectForm?.WorldForm;
            if (wf == null) return;

            var objs = Staged.GetSelectableObjects();
            if (objs.Length == 0) return;

            //build the multi-selection in one pass rather than N SelectObject calls
            var ms = MapSelection.FromProjectObject(wf, objs);
            wf.SelectItem(ms, false, false, false); //notifyProject:false - don't make the project window swap panels
        }

        private void GoToGroup()
        {
            var wf = ProjectForm?.WorldForm;
            if (wf == null) return;
            if (Staged.IsEmpty) return;

            var size = Staged.ComputeGroupSize();
            wf.GoToPosition(Staged.WidgetPosition, size);
        }

        private void GoToButton_Click(object sender, EventArgs e)
        {
            if (Staged.IsEmpty)
            {
                UpdateStatus("Nothing staged yet - use Load Resource Folder first.");
                return;
            }

            SelectStagedInWorld();
            GoToGroup();
            UpdateStatus($"Camera moved to the staged group at {FloatUtil.GetVector3String(Staged.WidgetPosition)}\n" +
                         $"({Staged.EntityCount} entities, {Staged.Ybns.Count} ybn, size {FloatUtil.GetVector3String(Staged.ComputeGroupSize())}).");
        }

        private void MoveWidgetButton_Click(object sender, EventArgs e)
        {
            if (!previewing) return;
            ProjectForm?.WorldForm?.ShowGroupWidget(this, Staged.WidgetPosition, false);
        }

        private void RotateWidgetButton_Click(object sender, EventArgs e)
        {
            if (!previewing) return;
            ProjectForm?.WorldForm?.ShowGroupWidget(this, Staged.WidgetPosition, true);
        }

        private void PreviewCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (populatingui) return;

            if (PreviewCheckBox.Checked) StartPreview();
            else
            {
                StopPreview();
                UpdateStatus("Preview off. The staged files keep their previewed position until you press Apply or Reset.");
            }
        }


        // ---------------------------------------------------------------
        // Fields
        // ---------------------------------------------------------------

        private void OffsetTextBox_TextChanged(object sender, EventArgs e)
        {
            if (populatingui) return;
            Staged.Offset = FloatUtil.ParseVector3String(OffsetTextBox.Text);
            OnFieldsEdited();
        }

        private void PivotTextBox_TextChanged(object sender, EventArgs e)
        {
            if (populatingui) return;
            Staged.Pivot = FloatUtil.ParseVector3String(PivotTextBox.Text);
            OnFieldsEdited();
        }

        private void YawAngleTextBox_TextChanged(object sender, EventArgs e)
        {
            if (populatingui) return;
            Staged.YawDegrees = FloatUtil.Parse(YawAngleTextBox.Text);
            OnFieldsEdited();
        }

        private void RotateBareRootCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (populatingui) return;
            Staged.AllowBareRootRotate = RotateBareRootCheckBox.Checked;
            OnFieldsEdited();
        }

        private void OnFieldsEdited()
        {
            if (!previewing) return;

            UpdatePreviewLocked();
            ProjectForm?.WorldForm?.SetGroupWidgetTransform(this, Staged.WidgetPosition, Staged.YawQuaternion());
        }

        private void SyncFieldsToUI()
        {
            try
            {
                if (InvokeRequired)
                {
                    //never Invoke here - the render thread holds the render lock, and the UI thread
                    //may be waiting on it
                    BeginInvoke(new Action(SyncFieldsToUI));
                    return;
                }

                populatingui = true;
                try
                {
                    OffsetTextBox.Text = FloatUtil.GetVector3String(Staged.Offset);
                    PivotTextBox.Text = FloatUtil.GetVector3String(Staged.Pivot);
                    YawAngleTextBox.Text = FloatUtil.ToString(Staged.YawDegrees);
                }
                finally
                {
                    populatingui = false;
                }

                UpdateStatus($"Preview: moved {Staged.EntitiesMoved} entities and {Staged.YbnsMoved} ybn(s).\n" +
                             $"Offset {FloatUtil.GetVector3String(Staged.Offset)}, yaw {FloatUtil.ToString(Staged.YawDegrees)} deg about {FloatUtil.GetVector3String(Staged.Pivot)}.\n" +
                             "Press Apply to commit, or Reset to put it back.");
            }
            catch { }
        }

        private void ResetTransformFields()
        {
            Staged.Offset = Vector3.Zero;
            Staged.YawDegrees = 0.0f;

            populatingui = true;
            try
            {
                OffsetTextBox.Text = FloatUtil.GetVector3String(Staged.Offset);
                PivotTextBox.Text = FloatUtil.GetVector3String(Staged.Pivot);
                YawAngleTextBox.Text = "0";
            }
            finally
            {
                populatingui = false;
            }
        }

        private void SetPreviewCheckBox(bool ischecked)
        {
            if (PreviewCheckBox == null) return;
            if (PreviewCheckBox.Checked == ischecked) return;

            populatingui = true;
            try { PreviewCheckBox.Checked = ischecked; }
            finally { populatingui = false; }
        }


        // ---------------------------------------------------------------
        // Commit / revert
        // ---------------------------------------------------------------

        private void ApplyButton_Click(object sender, EventArgs e)
        {
            if (ProjectForm == null) return;

            if (Staged.IsEmpty)
            {
                UpdateStatus("Nothing staged yet - use Load Resource Folder first.");
                return;
            }

            UpdatePreviewLocked(); //make sure what gets committed is exactly what the fields say

            foreach (var ymap in Staged.Ymaps)
            {
                if (ymap == null) continue;
                ymap.HasChanged = true;
                ProjectForm.ProjectExplorer?.SetYmapHasChanged(ymap, true);
            }
            foreach (var ybn in Staged.Ybns)
            {
                if (ybn?.Bounds == null) continue;
                ybn.HasChanged = true;
                ProjectForm.ProjectExplorer?.SetYbnHasChanged(ybn, true);
            }

            ProjectForm.SetProjectHasChanged(true);

            var msg = $"Applied: moved {Staged.EntitiesMoved} entities across {Staged.Ymaps.Count} ymap(s), and {Staged.YbnsMoved} ybn(s).";
            if (Staged.YbnsSkippedBareRotate > 0) msg += $"\nSkipped rotation on {Staged.YbnsSkippedBareRotate} standalone collision root(s) - enable the experimental checkbox to include them.";
            msg += "\nSave the project to write the files to disk.";

            StopPreview();
            Staged.TakeBaselines(); //the committed state is the new starting point
            ResetTransformFields();

            UpdateStatus(msg);
        }

        private void ResetButton_Click(object sender, EventArgs e)
        {
            if (Staged.IsEmpty)
            {
                UpdateStatus("Nothing staged yet - use Load Resource Folder first.");
                return;
            }

            Staged.Offset = Vector3.Zero;
            Staged.YawDegrees = 0.0f;
            Staged.Pivot = Staged.ComputeGroupCenter();

            var wf = ProjectForm?.WorldForm;
            if (wf != null)
            {
                lock (wf.RenderSyncRoot)
                {
                    Staged.RestoreBaselines();
                }
            }
            else
            {
                Staged.RestoreBaselines();
            }

            ResetTransformFields();

            if (previewing)
            {
                wf?.SetGroupWidgetTransform(this, Staged.WidgetPosition, Quaternion.Identity);
            }

            UpdateStatus("Reset - the staged files are back where they were loaded.");
        }

        private void UpdateStatus(string text)
        {
            if (StatusLabel == null) return;
            StatusLabel.Text = text;
        }
    }
}
