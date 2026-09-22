using CodeWalker.Properties;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Shell;
using Velopack;

namespace CodeWalker
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            //must be first: handles Velopack's install/update/uninstall hooks (and exits for those),
            //and applies an update that was downloaded but not yet installed.
            VelopackApp.Build().Run();

            bool menumode = false;
            bool explorermode = false;
            bool projectmode = false;
            bool vehiclesmode = false;
            bool pedsmode = false;
            if ((args != null) && (args.Length > 0))
            {
                foreach (string arg in args)
                {
                    string argl = arg.ToLowerInvariant();
                    if (argl == "menu")
                    {
                        menumode = true;
                    }
                    if (argl == "explorer")
                    {
                        explorermode = true;
                    }
                    if (argl == "project")
                    {
                        projectmode = true;
                    }
                    if (argl == "vehicles")
                    {
                        vehiclesmode = true;
                    }
                    if (argl == "peds")
                    {
                        pedsmode = true;
                    }
                }
            }

            UpgradeSettings(); //must run before anything reads or saves a setting

            EnsureJumpList();

            //Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Utils.AppUpdater.CheckForUpdates(args); //before any window opens, so a restart can't lose work


            // Always check the GTA folder first thing
            if (!GTAFolder.UpdateGTAFolder(Properties.Settings.Default.RememberGTAFolder))
            {
                MessageBox.Show("Could not load SprunkMapper because no valid GTA 5 folder was selected. SprunkMapper will now exit.", "GTA 5 Folder Not Found", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                return;
            }
#if !DEBUG
            try
            {
#endif
                if (menumode)
                {
                    Application.Run(new MenuForm());
                }
                else if (explorermode)
                {
                    Application.Run(new ExploreForm());
                }
                else if (projectmode)
                {
                    Application.Run(new Project.ProjectForm());
                }
                else if (vehiclesmode)
                {
                    Application.Run(new VehicleForm());
                }
                else if (pedsmode)
                {
                    Application.Run(new PedsForm());
                }
                else
                {
                    Application.Run(new WorldForm());
                }
#if !DEBUG
            }
            catch (Exception ex)
            {
                MessageBox.Show("An unexpected error was encountered!\n" + ex.ToString());
                //this can happen if folder wasn't chosen, or in some other catastrophic error. meh.
            }
#endif
        }


        static void UpgradeSettings()
        {
            //.NET keeps user settings in a folder per assembly version, so every new version starts
            //from defaults. UpgradeRequired is true only in a fresh folder: copy the previous
            //version's settings across once. (Upgrade() only looks at LOWER version folders.)
            ImportSettingsFromOtherLocation();
            if (!Settings.Default.UpgradeRequired) return;
            try
            {
                Settings.Default.Upgrade();
            }
            catch { } //a damaged older user.config shouldn't stop the app from starting
            Settings.Default.UpgradeRequired = false;
            Settings.Default.Save();
        }

        static void ImportSettingsFromOtherLocation()
        {
            //the settings folder is also named after a hash of the exe's PATH:
            //  %LOCALAPPDATA%\dexyfex_software\SprunkMapper.exe_Url_<hash>\<version>\user.config
            //so an exe in a new place (the Velopack install, or a copied build folder) starts from an
            //empty folder that Upgrade() can't see out of. if this location has never saved settings,
            //copy the most recently used settings file from any other SprunkMapper.exe location.
            //must run before Settings.Default is first touched.
            try
            {
                var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal);
                var target = config.FilePath;
                var locationDir = Directory.GetParent(target)?.Parent;
                var companyDir = locationDir?.Parent;
                if ((companyDir == null) || !companyDir.Exists) return;
                if (locationDir.Exists && (locationDir.GetFiles("user.config", SearchOption.AllDirectories).Length > 0)) return; //has its own settings

                var parts = locationDir.Name.Split('_'); //"<exe name>_<evidence type>_<hash>"
                if (parts.Length < 3) return;
                var prefix = string.Join("_", parts.Take(parts.Length - 2)) + "_";
                var current = Assembly.GetEntryAssembly().GetName().Version;

                FileInfo best = null;
                foreach (var dir in companyDir.GetDirectories(prefix + "*"))
                {
                    if (dir.Name == locationDir.Name) continue;
                    foreach (var file in dir.GetFiles("user.config", SearchOption.AllDirectories))
                    {
                        if (!Version.TryParse(file.Directory.Name, out var ver) || (ver > current)) continue; //never import from a newer version
                        if ((best == null) || (file.LastWriteTimeUtc > best.LastWriteTimeUtc)) best = file;
                    }
                }
                if (best == null) return;

                Directory.CreateDirectory(Path.GetDirectoryName(target));
                best.CopyTo(target, false);
            }
            catch { } //worst case the app starts with default settings, as it would have anyway
        }

        static void EnsureJumpList()
        {
            if (Settings.Default.JumpListInitialised) return;

            try
            {
                var cwpath = Assembly.GetEntryAssembly().Location;
                var cwdir = Path.GetDirectoryName(cwpath);

                var jtWorld = new JumpTask();
                jtWorld.ApplicationPath = cwpath;
                jtWorld.IconResourcePath = cwpath;
                jtWorld.WorkingDirectory = cwdir;
                jtWorld.Arguments = "";
                jtWorld.Title = "World View";
                jtWorld.Description = "Display the GTAV World";
                jtWorld.CustomCategory = "Launch Options";

                var jtExplorer = new JumpTask();
                jtExplorer.ApplicationPath = cwpath;
                jtExplorer.IconResourcePath = Path.Combine(cwdir, "CodeWalker RPF Explorer.exe");
                jtExplorer.WorkingDirectory = cwdir;
                jtExplorer.Arguments = "explorer";
                jtExplorer.Title = "RPF Explorer";
                jtExplorer.Description = "Open RPF Explorer";
                jtExplorer.CustomCategory = "Launch Options";

                var jtVehicles = new JumpTask();
                jtVehicles.ApplicationPath = cwpath;
                jtVehicles.IconResourcePath = Path.Combine(cwdir, "CodeWalker Vehicle Viewer.exe");
                jtVehicles.WorkingDirectory = cwdir;
                jtVehicles.Arguments = "vehicles";
                jtVehicles.Title = "Vehicle Viewer";
                jtVehicles.Description = "Open Vehicle Viewer";
                jtVehicles.CustomCategory = "Launch Options";

                var jtPeds = new JumpTask();
                jtPeds.ApplicationPath = cwpath;
                jtPeds.IconResourcePath = Path.Combine(cwdir, "CodeWalker Ped Viewer.exe");
                jtPeds.WorkingDirectory = cwdir;
                jtPeds.Arguments = "peds";
                jtPeds.Title = "Ped Viewer";
                jtPeds.Description = "Open Ped Viewer";
                jtPeds.CustomCategory = "Launch Options";

                var jumpList = new JumpList();

                jumpList.JumpItems.Add(jtWorld);
                jumpList.JumpItems.Add(jtExplorer);
                jumpList.JumpItems.Add(jtVehicles);
                jumpList.JumpItems.Add(jtPeds);

                jumpList.Apply();

                Settings.Default.JumpListInitialised = true;
                Settings.Default.Save();
            }
            catch
            { }
        }
    }
}
