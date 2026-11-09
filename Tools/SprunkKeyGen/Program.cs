using CodeWalker.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SprunkKeyGen
{
    /// <summary>
    /// SprunkKeyGen init            create the signing key (once) and print the public key for LicenseManager.cs
    /// SprunkKeyGen pubkey          print the public key again
    /// SprunkKeyGen issue "Name"    make a key for a friend
    /// SprunkKeyGen list            show every key issued (id, name, date) - use the id to revoke one
    /// SprunkKeyGen check KEY       check a key against this signing key
    ///
    /// Files live in %USERPROFILE%\.sprunkmapper (or %SPRUNKKEYGEN_DIR%):
    ///   signing-key.bin   the PRIVATE key. Back it up; never share or commit it.
    ///   issued-keys.csv   a log of the keys issued.
    /// </summary>
    static class Program
    {
        static string Dir
        {
            get
            {
                var env = Environment.GetEnvironmentVariable("SPRUNKKEYGEN_DIR");
                if (!string.IsNullOrWhiteSpace(env)) return env;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".sprunkmapper");
            }
        }
        static string KeyFile { get { return Path.Combine(Dir, "signing-key.bin"); } }
        static string LogFile { get { return Path.Combine(Dir, "issued-keys.csv"); } }

        static int Main(string[] args)
        {
            try
            {
                var cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "";
                switch (cmd)
                {
                    case "init": return Init();
                    case "pubkey": return PubKey();
                    case "issue": return Issue(string.Join(" ", args.Skip(1)).Trim());
                    case "list": return List();
                    case "check": return Check(string.Join("", args.Skip(1)));
                    default:
                        Console.WriteLine("Usage: SprunkKeyGen init | pubkey | issue \"Name\" | list | check KEY");
                        Console.WriteLine("Key folder: " + Dir);
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error: " + ex.Message);
                return 2;
            }
        }

        static int Init()
        {
            if (File.Exists(KeyFile))
            {
                Console.Error.WriteLine("A signing key already exists: " + KeyFile);
                Console.Error.WriteLine("Not overwriting it: a new key would invalidate every key already issued.");
                return 1;
            }
            Directory.CreateDirectory(Dir);
            ECParameters p;
            using (var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256))
            {
                p = ecdsa.ExportParameters(true);
            }
            var blob = new byte[LicenseToken.CoordinateLength * 3];
            Buffer.BlockCopy(p.D, 0, blob, 0, 32);
            Buffer.BlockCopy(p.Q.X, 0, blob, 32, 32);
            Buffer.BlockCopy(p.Q.Y, 0, blob, 64, 32);
            File.WriteAllBytes(KeyFile, blob);
            Console.WriteLine("Signing key created: " + KeyFile);
            Console.WriteLine("BACK IT UP somewhere safe and never commit or share it.");
            Console.WriteLine();
            return PubKey();
        }

        static int PubKey()
        {
            var p = LoadKey();
            Console.WriteLine("Paste into CodeWalker/Utils/LicenseManager.cs:");
            Console.WriteLine("        const string PublicKeyX = \"" + Convert.ToBase64String(p.Q.X) + "\";");
            Console.WriteLine("        const string PublicKeyY = \"" + Convert.ToBase64String(p.Q.Y) + "\";");
            return 0;
        }

        static int Issue(string name)
        {
            if (name.Length == 0)
            {
                Console.Error.WriteLine("Give the friend's name: SprunkKeyGen issue \"Name\"");
                return 1;
            }
            if (name.Contains(",") || name.Contains("\"")) throw new ArgumentException("The name can't contain commas or quotes.");
            var p = LoadKey();

            var used = new HashSet<string>(ReadLog().Select(r => r[0]));
            uint id;
            using (var rng = RandomNumberGenerator.Create())
            {
                var b = new byte[4];
                do
                {
                    rng.GetBytes(b);
                    id = BitConverter.ToUInt32(b, 0);
                } while (id == 0 || used.Contains(id.ToString("x8")));
            }

            var now = DateTime.UtcNow;
            var payload = LicenseToken.BuildPayload(id, name, now);
            byte[] sig;
            using (var ecdsa = ECDsa.Create(p))
            {
                sig = ecdsa.SignData(payload, HashAlgorithmName.SHA256);
            }
            var key = LicenseToken.Encode(payload, sig);

            //self-check with the public half, the same way the app does
            LicenseInfo info;
            var err = LicenseToken.Validate(key, p.Q.X, p.Q.Y, out info);
            if (err != null) throw new Exception("The new key failed its own check: " + err);

            if (!File.Exists(LogFile)) File.WriteAllText(LogFile, "id,name,issued_utc\r\n", Encoding.UTF8);
            File.AppendAllText(LogFile, info.IdHex + "," + name + "," + now.ToString("yyyy-MM-dd HH:mm") + "\r\n", Encoding.UTF8);

            Console.WriteLine("Key for " + info.Name + " (id " + info.IdHex + "):");
            Console.WriteLine();
            Console.WriteLine(key);
            return 0;
        }

        static int List()
        {
            var rows = ReadLog();
            if (rows.Count == 0)
            {
                Console.WriteLine("No keys issued yet.");
                return 0;
            }
            Console.WriteLine("id        issued (UTC)      name");
            foreach (var r in rows)
            {
                Console.WriteLine(r[0] + "  " + (r.Length > 2 ? r[2] : "").PadRight(16) + "  " + (r.Length > 1 ? r[1] : ""));
            }
            Console.WriteLine();
            Console.WriteLine("To revoke a key, add its id to revoked-keys.txt on master and push.");
            return 0;
        }

        static int Check(string key)
        {
            var p = LoadKey();
            LicenseInfo info;
            var err = LicenseToken.Validate(key, p.Q.X, p.Q.Y, out info);
            if (err != null)
            {
                Console.WriteLine("INVALID: " + err);
                return 1;
            }
            Console.WriteLine("Valid: id " + info.IdHex + ", issued to " + info.Name + " on " + info.Issued.ToString("yyyy-MM-dd"));
            return 0;
        }

        static ECParameters LoadKey()
        {
            if (!File.Exists(KeyFile)) throw new FileNotFoundException("No signing key at " + KeyFile + ". Run: SprunkKeyGen init");
            var blob = File.ReadAllBytes(KeyFile);
            if (blob.Length != 96) throw new InvalidDataException("The signing key file is damaged: " + KeyFile);
            return new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                D = blob.Take(32).ToArray(),
                Q = new ECPoint { X = blob.Skip(32).Take(32).ToArray(), Y = blob.Skip(64).Take(32).ToArray() },
            };
        }

        static List<string[]> ReadLog()
        {
            var rows = new List<string[]>();
            if (!File.Exists(LogFile)) return rows;
            foreach (var line in File.ReadAllLines(LogFile).Skip(1))
            {
                if (line.Trim().Length == 0) continue;
                rows.Add(line.Split(','));
            }
            return rows;
        }
    }
}
