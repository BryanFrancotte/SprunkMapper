using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodeWalker.Utils
{
    /// <summary>
    /// Friends-only gate: SprunkMapper needs a personal licence key, made with Tools/SprunkKeyGen.
    /// Keys are checked offline against the public key below. Leaked keys are revoked by listing their id
    /// in revoked-keys.txt on master, which is fetched at startup (and cached for offline starts).
    /// </summary>
    public static class LicenseManager
    {
        //public half of %USERPROFILE%\.sprunkmapper\signing-key.bin (SprunkKeyGen pubkey)
        const string PublicKeyX = "HaKpM4Thu3gOOsF3b4k0QKER8jSfz705yyGFbqa3jiY=";
        const string PublicKeyY = "dx50YvCBm8quTp4BJvT8FoIKJcUfLJ11eRwMijTTrGk=";

        const string RevokedUrl = "https://raw.githubusercontent.com/BryanFrancotte/SprunkMapper/master/revoked-keys.txt";
        static readonly TimeSpan RevokedTimeout = TimeSpan.FromSeconds(3);

        /// <summary>The licence SprunkMapper is running under, once EnsureLicensed() has returned true.</summary>
        public static LicenseInfo Current { get; private set; }

        //outside user.config (its folder changes with exe path and version) and outside the Velopack install folder
        static string Folder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SprunkMapper"); } }
        static string KeyFile { get { return Path.Combine(Folder, "license.key"); } }
        static string RevokedCacheFile { get { return Path.Combine(Folder, "revoked-cache.txt"); } }

        /// <summary>
        /// Call at startup, before any window opens. Returns true when a valid key is saved or was just entered;
        /// false when the user quit the activation window (the app should exit).
        /// </summary>
        public static bool EnsureLicensed()
        {
            var revoked = LoadRevokedIds();

            string reason = null;
            var saved = ReadSavedKey();
            if (saved != null)
            {
                LicenseInfo info;
                reason = Validate(saved, revoked, out info);
                if (reason == null)
                {
                    Current = info;
                    return true;
                }
            }

            using (var form = new LicenseForm(text =>
            {
                LicenseInfo info;
                var err = Validate(text, revoked, out info);
                return Tuple.Create(info, err);
            }, reason))
            {
                if (form.ShowDialog() != DialogResult.OK || form.License == null) return false;
                Current = form.License;
                SaveKey(form.KeyText);
                return true;
            }
        }

        /// <summary>Null when the key is valid and not revoked, otherwise the reason it isn't.</summary>
        static string Validate(string text, HashSet<string> revoked, out LicenseInfo info)
        {
            var err = LicenseToken.Validate(text, Convert.FromBase64String(PublicKeyX), Convert.FromBase64String(PublicKeyY), out info);
            if (err != null) return err;
            if (revoked.Contains(info.IdHex))
            {
                info = null;
                return "This key has been revoked, ask Bryan for a new one.";
            }
            return null;
        }

        static string ReadSavedKey()
        {
            try
            {
                if (!File.Exists(KeyFile)) return null;
                var text = LicenseToken.Normalize(File.ReadAllText(KeyFile));
                return text.Length > 0 ? text : null;
            }
            catch
            {
                return null;
            }
        }

        static void SaveKey(string text)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(KeyFile, LicenseToken.Normalize(text), Encoding.ASCII);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Your key is valid, but it couldn't be saved, so you'll be asked again next time:\n" + ex.Message,
                    "SprunkMapper", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// The revoked key ids: fresh from GitHub when it answers in time (and cached), else the cached copy,
        /// else none. Never throws - being offline must not lock anyone out.
        /// </summary>
        static HashSet<string> LoadRevokedIds()
        {
            string text = null;
            try
            {
                var fetch = Task.Run(() => Download());
                if (fetch.Wait(RevokedTimeout)) text = fetch.Result;
            }
            catch
            {
                text = null;
            }

            if (text != null)
            {
                try
                {
                    Directory.CreateDirectory(Folder);
                    File.WriteAllText(RevokedCacheFile, text, Encoding.UTF8);
                }
                catch { }
            }
            else
            {
                try
                {
                    if (File.Exists(RevokedCacheFile)) text = File.ReadAllText(RevokedCacheFile);
                }
                catch { }
            }
            return ParseRevoked(text);
        }

        static string Download()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var req = (HttpWebRequest)WebRequest.Create(RevokedUrl);
            req.Timeout = (int)RevokedTimeout.TotalMilliseconds;
            req.ReadWriteTimeout = (int)RevokedTimeout.TotalMilliseconds;
            req.UserAgent = "SprunkMapper";
            req.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
            using (var resp = (HttpWebResponse)req.GetResponse()) //throws on 404 etc, which counts as "no answer"
            using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        /// <summary>One 8-digit hex id per line; anything after # is a comment.</summary>
        static HashSet<string> ParseRevoked(string text)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(text)) return ids;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw;
                var hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash);
                line = line.Trim().ToLowerInvariant();
                if (line.Length == 8) ids.Add(line);
            }
            return ids;
        }
    }
}
