using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;

static class Cfg
{
    // включается только отладочными прогонами (--shot-run/--shot-test)
    public static bool Dbg = false;
    public static string BaseDir;
    public static string DataDir;
    public static string McDir;
    public static string JdkDir;
    public static string ConfigPath;
    public const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    public const string JavaJreUrl = "https://api.adoptium.net/v3/binary/latest/21/ga/windows/x64/jre/hotspot/normal/eclipse";
    public const string JavaJdkUrl = "https://api.adoptium.net/v3/binary/latest/21/ga/windows/x64/jdk/hotspot/normal/eclipse";
    public const string AppVersion = "3.0.4";
#if ARTYOM
    // Подарочный билд для Артёма: собирается ключом /define:ARTYOM
    public const bool GiftBuild = true;
#else
    public const bool GiftBuild = false;
#endif
    // Подпись в заголовке окна. В обычном билде - версия,
    // в подарочном - "Артемка с др".
    public static string VersionLabel
    {
        get { return GiftBuild ? "Артемка с др" : "v" + AppVersion; }
    }
    public const string DefaultUpdateUrl = "https://raw.githubusercontent.com/gfpmmp-del/luxe-launcher/main/update.json";
    public static string UpdateUrl()
    {
        try
        {
            string f = Path.Combine(BaseDir, "update_url.txt");
            if (File.Exists(f))
            {
                string u = File.ReadAllText(f, Encoding.UTF8).Trim();
                if (u.StartsWith("http")) return u;
            }
        }
        catch { }
        return DefaultUpdateUrl;
    }

    // Логи живут в папке данных (Luxe Visuals\logs), а не рядом с exe:
    // при скачивании лаунчера рядом должен появляться только мир Minecraft.
    public static string LogPath(string name)
    {
        try
        {
            string root = string.IsNullOrEmpty(DataDir) ? BaseDir : DataDir;
            string d = Path.Combine(root, "logs");
            Directory.CreateDirectory(d);
            return Path.Combine(d, name);
        }
        catch { }
        return Path.Combine(string.IsNullOrEmpty(BaseDir) ? "." : BaseDir, name);
    }
}

class InstallResult
{
    public string Id;
    public Dictionary<string, object> VersionJson;
    public List<string> Classpath = new List<string>();
    public string NativesDir;
    public string GameDir;
    public string LogArg;
    public string AssetId;
    public string VerType;
}

static class Mc
{
    static JavaScriptSerializer ser = new JavaScriptSerializer();

    public static Dictionary<string, string> LoadConfig()
    {
        Dictionary<string, string> c = new Dictionary<string, string>();
        c["username"] = "Player";
        c["version"] = "";
        c["ram"] = "2";
        c["closeOnLaunch"] = "1";
        try
        {
            if (File.Exists(Cfg.ConfigPath))
            {
                string t = File.ReadAllText(Cfg.ConfigPath, Encoding.UTF8);
                Dictionary<string, object> d = ser.Deserialize<Dictionary<string, object>>(t);
                if (d.ContainsKey("username") && d["username"] != null) c["username"] = d["username"].ToString();
                if (d.ContainsKey("version") && d["version"] != null) c["version"] = d["version"].ToString();
                if (d.ContainsKey("ram") && d["ram"] != null) c["ram"] = d["ram"].ToString();
                if (d.ContainsKey("closeOnLaunch") && d["closeOnLaunch"] != null) c["closeOnLaunch"] = d["closeOnLaunch"].ToString();
            }
        }
        catch { }
        return c;
    }

    public static bool IsTrue(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        s = s.Trim().ToLower();
        return s == "1" || s == "true" || s == "yes";
    }

    public static void SaveConfig(string nick, string ver, string ram, bool close)
    {
        try
        {
            Dictionary<string, object> o = new Dictionary<string, object>();
            o["username"] = nick;
            o["version"] = ver;
            o["ram"] = ram;
            o["closeOnLaunch"] = close ? "1" : "0";
            File.WriteAllText(Cfg.ConfigPath, ser.Serialize(o), Encoding.UTF8);
        }
        catch { }
    }

    public static string GetOfflineUuid(string nick)
    {
        byte[] input = Encoding.UTF8.GetBytes("OfflinePlayer:" + nick);
        MD5 md5 = MD5.Create();
        byte[] h = md5.ComputeHash(input);
        md5.Dispose();
        h[6] = (byte)((h[6] & 0x0F) | 0x30);
        h[8] = (byte)((h[8] & 0x3F) | 0x80);
        StringBuilder sb = new StringBuilder();
        foreach (byte b in h) sb.Append(b.ToString("x2"));
        string hex = sb.ToString();
        return hex.Substring(0, 8) + "-" + hex.Substring(8, 4) + "-" + hex.Substring(12, 4) + "-" + hex.Substring(16, 4) + "-" + hex.Substring(20, 12);
    }

    public static string KindOf(string id)
    {
        string l = id.ToLower();
        if (l.Contains("-neoforge-") || l.EndsWith("-neoforge")) return "neoforge";
        if (l.Contains("-forge-") || l.EndsWith("-forge")) return "forge";
        if (l.Contains("-fabric")) return "fabric";
        if (l.Contains("-quilt")) return "quilt";
        return "vanilla";
    }

    public static string McOf(string id)
    {
        string kind = KindOf(id);
        string l = id.ToLower();
        if (kind == "neoforge") { int i = l.IndexOf("-neoforge"); if (i > 0) return id.Substring(0, i); }
        if (kind == "forge") { int i = l.IndexOf("-forge"); if (i > 0) return id.Substring(0, i); }
        if (kind == "fabric") { int i = l.IndexOf("-fabric"); if (i > 0) return id.Substring(0, i); }
        if (kind == "quilt") { int i = l.IndexOf("-quilt"); if (i > 0) return id.Substring(0, i); }
        return id;
    }

    public static int JavaFor(string mc)
    {
        try
        {
            if (mc.StartsWith("1."))
            {
                string[] p = mc.Split('.');
                int minor = Convert.ToInt32(p[1]);
                if (minor <= 16) return 8;
                if (minor < 20) return 17;
                if (minor == 20)
                {
                    int patch = 0;
                    if (p.Length > 2)
                    {
                        string digits = "";
                        foreach (char ch in p[2]) { if (char.IsDigit(ch)) digits += ch; else break; }
                        if (digits.Length > 0) patch = Convert.ToInt32(digits);
                    }
                    return patch >= 5 ? 21 : 17;
                }
                return 21;
            }
            // С 2026 года Minecraft нумеруется по году: 26.1, 26.2, 26.3...
            // (и снапшоты 26w..). Эти версии собраны под Java 25:
            // старые Java падают с LinkageError / UnsupportedClassVersionError.
            if (mc.Length >= 2 && char.IsDigit(mc[0]) && char.IsDigit(mc[1]))
            {
                int yy = Convert.ToInt32(mc.Substring(0, 2));
                if (yy >= 26) return 25;
            }
            if (mc.Length >= 3 && char.IsDigit(mc[0]) && char.IsDigit(mc[1]) && mc[2] == 'w')
            {
                int yy = Convert.ToInt32(mc.Substring(0, 2));
                if (yy <= 20) return 8;
                if (yy <= 23) return 17;
                return 21;
            }
            return 21;
        }
        catch { return 21; }
    }

    public static string JdkDirFor(int javaMajor)
    {
        if (javaMajor == 8) return ".jdk8";
        if (javaMajor == 17) return ".jdk17";
        if (javaMajor == 25) return ".jdk25";
        return ".jdk";
    }

    public static string TemurinUrlFor(int javaMajor, bool jdk)
    {
        string kind = jdk ? "jdk" : "jre";
        // Adoptium отдаёт сборки по (os, arch): windows/linux/mac + x64/aarch64
        string os = Os.IsWin ? "windows" : (Os.IsMac ? "mac" : "linux");
        string arch = Os.McArch == "arm64" ? "aarch64" : "x64";
        return "https://api.adoptium.net/v3/binary/latest/" + javaMajor + "/ga/" + os + "/" + arch + "/" + kind + "/hotspot/normal/eclipse";
    }

    public static int GetJavaMajor(string javaExe)
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = javaExe;
            psi.Arguments = "-version";
            psi.UseShellExecute = false;
            psi.RedirectStandardError = true;
            psi.RedirectStandardOutput = true;
            psi.CreateNoWindow = true;
            Process p = new Process();
            p.StartInfo = psi;
            StringBuilder sb = new StringBuilder();
            p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) sb.Append(e.Data); };
            p.Start();
            p.BeginErrorReadLine();
            p.WaitForExit(15000);
            string o = sb.ToString();
            int q1 = o.IndexOf('"');
            if (q1 < 0) return -1;
            int q2 = o.IndexOf('"', q1 + 1);
            if (q2 < 0) return -1;
            string v = o.Substring(q1 + 1, q2 - q1 - 1);
            string[] pp = v.Split('.');
            if (pp[0] == "1" && pp.Length > 1) return Convert.ToInt32(pp[1]);
            return Convert.ToInt32(pp[0]);
        }
        catch { return -1; }
    }

    public static string EnsureJavaFor(string mc, Action<string> status)
    {
        int need = JavaFor(mc);
        string dir = Path.Combine(Cfg.DataDir, JdkDirFor(need));
        if (Directory.Exists(dir))
        {
            try
            {
                string[] f = Directory.GetFiles(dir, Os.JavaName, SearchOption.AllDirectories);
                if (f.Length > 0) return f[0];
            }
            catch { }
        }
        string sys = FindLocalJava();
        if (sys != null)
        {
            try { if (GetJavaMajor(sys) == need) return sys; } catch { }
        }
        status("Качаю portable Java " + need + " (~190 МБ)...");
        string zip = Path.Combine(Cfg.DataDir, ".jdk" + need + "_download.zip");
        string[] urls = new string[] { TemurinUrlFor(need, false), TemurinUrlFor(need, true) };
        bool ok = false;
        foreach (string u in urls)
        {
            try
            {
                status("Скачивание Java " + need + "...");
                DownloadWithProgress(u, zip, "Java " + need);
                FileInfo fi = new FileInfo(zip);
                if (fi.Exists && fi.Length > 10 * 1024 * 1024) { ok = true; break; }
            }
            catch (Exception ex) { status("Повтор... " + ex.Message); }
        }
        if (Progress != null) { try { Progress(-1, -1, null); } catch { } }
        if (!ok || !File.Exists(zip)) throw new Exception("Не удалось скачать Java " + need + ". Проверь интернет.");
        status("Распаковка Java " + need + "...");
        string tmp = Path.Combine(Cfg.DataDir, ".jdk_tmp");
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        ZipFile.ExtractToDirectory(zip, tmp);
        ChmodDir(tmp);
        string[] found = Directory.GetFiles(tmp, Os.JavaName, SearchOption.AllDirectories);
        if (found.Length == 0) throw new Exception("Java распакована, но java.exe не найден");
        string binDir = Path.GetDirectoryName(found[0]);
        string root = Directory.GetParent(binDir).FullName;
        foreach (string f in Directory.GetFileSystemEntries(root))
        {
            string dest = Path.Combine(dir, Path.GetFileName(f));
            if (Directory.Exists(f)) CopyDir(f, dest);
            else File.Copy(f, dest, true);
        }
        try { Directory.Delete(tmp, true); } catch { }
        try { File.Delete(zip); } catch { }
        ChmodDir(dir);
        string[] f2 = Directory.GetFiles(dir, Os.JavaName, SearchOption.AllDirectories);
        if (f2.Length == 0) throw new Exception("Java установлена, но java.exe не найден");
        return f2[0];
    }

    public static ArrayList GetJsonArray(string url)
    {
        WebClient wc = new WebClient();
        wc.Encoding = Encoding.UTF8;
        string txt = wc.DownloadString(url);
        wc.Dispose();
        return ser.Deserialize<ArrayList>(txt);
    }

    public static bool IsNativesName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        string[] parts = name.Split(':');
        return parts.Length >= 4 && parts[3].StartsWith("natives");
    }

    public static string MavenLocalPath(string name, string libDir)
    {
        try
        {
            string[] p = name.Split(':');
            if (p.Length < 3) return null;
            string g = p[0].Replace('.', '/');
            string a = p[1];
            string v = p[2];
            string c = p.Length >= 4 ? p[3] : null;
            string file = a + "-" + v + (c != null ? "-" + c : "") + ".jar";
            return Path.Combine(libDir, Os.NativePath(g + "/" + a + "/" + v + "/" + file));
        }
        catch { return null; }
    }

    public static bool MavenToJob(string name, string repo, string libDir, List<string[]> jobs, List<string> nativeJars, List<string> cp)
    {
        string[] p = name.Split(':');
        if (p.Length < 3) return false;
        string g = p[0].Replace('.', '/');
        string a = p[1];
        string v = p[2];
        string c = p.Length >= 4 ? p[3] : null;
        string file = a + "-" + v + (c != null ? "-" + c : "") + ".jar";
        string path = g + "/" + a + "/" + v + "/" + file;
        string r = repo;
        if (!r.EndsWith("/")) r += "/";
        string dest = Path.Combine(libDir, Os.NativePath(path));
        if (!File.Exists(dest)) jobs.Add(new string[] { r + path, dest });
        if (c != null && c.StartsWith("natives")) nativeJars.Add(dest);
        cp.Add(dest);
        return true;
    }

    public static void CollectLibs(Dictionary<string, object> vj, string libDir, List<string> cp, List<string[]> jobs, List<string> nativeJars)
    {
        ArrayList libs = vj["libraries"] as ArrayList;
        if (libs == null) return;
        foreach (object lo in libs)
        {
            Dictionary<string, object> lib = lo as Dictionary<string, object>;
            if (lib == null) continue;
            if (!IsAllowed(lib.ContainsKey("rules") ? lib["rules"] as ArrayList : null)) continue;
            Dictionary<string, object> dls = lib.ContainsKey("downloads") ? lib["downloads"] as Dictionary<string, object> : null;
            string name = lib.ContainsKey("name") ? lib["name"].ToString() : "";
            string nativeKey = null;
            if (lib.ContainsKey("natives") && lib["natives"] is Dictionary<string, object>)
            {
                Dictionary<string, object> nat = (Dictionary<string, object>)lib["natives"];
                object w;
                if (nat.TryGetValue(Os.McOsName, out w) && w != null) nativeKey = w.ToString();
            }
            if (nativeKey != null && dls != null && dls.ContainsKey("classifiers") && dls["classifiers"] is Dictionary<string, object>)
            {
                Dictionary<string, object> cls = (Dictionary<string, object>)dls["classifiers"];
                if (cls.ContainsKey(nativeKey) && cls[nativeKey] is Dictionary<string, object>)
                {
                    Dictionary<string, object> art = (Dictionary<string, object>)cls[nativeKey];
                    string dest = Path.Combine(libDir, Os.NativePath(art["path"].ToString()));
                    if (!File.Exists(dest)) jobs.Add(new string[] { art["url"].ToString(), dest });
                    nativeJars.Add(dest);
                    if (dls.ContainsKey("artifact") && dls["artifact"] is Dictionary<string, object>)
                    {
                        Dictionary<string, object> a = (Dictionary<string, object>)dls["artifact"];
                        string pp = Path.Combine(libDir, Os.NativePath(a["path"].ToString()));
                        if (!File.Exists(pp)) jobs.Add(new string[] { a["url"].ToString(), pp });
                        cp.Add(pp);
                    }
                    continue;
                }
            }
            if (IsNativesName(name))
            {
                Dictionary<string, object> a = (dls != null && dls.ContainsKey("artifact")) ? dls["artifact"] as Dictionary<string, object> : null;
                if (a != null)
                {
                    string pp = Path.Combine(libDir, Os.NativePath(a["path"].ToString()));
                    if (!File.Exists(pp)) jobs.Add(new string[] { a["url"].ToString(), pp });
                    nativeJars.Add(pp);
                    cp.Add(pp);
                    continue;
                }
            }
            if (dls != null && dls.ContainsKey("artifact") && dls["artifact"] is Dictionary<string, object>)
            {
                Dictionary<string, object> a = (Dictionary<string, object>)dls["artifact"];
                string pp = Path.Combine(libDir, Os.NativePath(a["path"].ToString()));
                if (!File.Exists(pp)) jobs.Add(new string[] { a["url"].ToString(), pp });
                cp.Add(pp);
            }
        }
    }

    public static string ResolveLoader(string kind, string mc, Action<string> status)
    {
        status("Смотрю загрузчики " + kind + "...");
        ArrayList arr;
        if (kind == "fabric")
            arr = GetJsonArray("https://meta.fabricmc.net/v2/versions/loader/" + mc);
        else
            arr = GetJsonArray("https://meta.quiltmc.org/v3/versions/loader/" + mc);
        string first = null;
        foreach (object o in arr)
        {
            Dictionary<string, object> e = o as Dictionary<string, object>;
            if (e == null || !e.ContainsKey("loader")) continue;
            Dictionary<string, object> l = e["loader"] as Dictionary<string, object>;
            if (l == null || !l.ContainsKey("version")) continue;
            string vv = l["version"].ToString();
            if (first == null) first = vv;
            object st;
            if (l.TryGetValue("stable", out st) && st is bool && (bool)st) return vv;
        }
        if (first == null) throw new Exception("Нет загрузчика " + kind + " для " + mc);
        return first;
    }

    public static InstallResult InstallProfile(string kind, string mc, Action<string> status, Action<int, int> prog)
    {
        string loader = ResolveLoader(kind, mc, status);
        string id = mc + "-" + kind;
        string profUrl = kind == "fabric"
            ? "https://meta.fabricmc.net/v2/versions/loader/" + mc + "/" + loader + "/profile/json"
            : "https://meta.quiltmc.org/v3/versions/loader/" + mc + "/" + loader + "/profile/json";
        status("Качаю профиль " + kind + " " + loader + "...");
        Dictionary<string, object> pj = GetJson(profUrl);
        pj["id"] = id;
        string vdir = Path.Combine(Path.Combine(Cfg.McDir, "versions"), id);
        Directory.CreateDirectory(vdir);
        File.WriteAllText(Path.Combine(vdir, id + ".json"), ser.Serialize(pj), Encoding.UTF8);
        InstallResult baseRes = InstallVersion(mc, status, prog);
        string defRepo = kind == "fabric" ? "https://maven.fabricmc.net/" : "https://maven.quiltmc.org/repository/release/";
        return BuildModdedRes(id, pj, baseRes, defRepo, status, prog);
    }

    public static string NeoMcOf(string nv)
    {
        try
        {
            string[] p = nv.Split('.');
            if (p[0] == "20") return "1.20." + p[1];
            if (p[0] == "21") return "1.21." + p[1];
            return p[0] + "." + p[1];
        }
        catch { return ""; }
    }

    static void EnsureLauncherProfiles(string mc)
    {
        try
        {
            string p = Path.Combine(Cfg.McDir, "launcher_profiles.json");
            if (File.Exists(p)) return;
            Directory.CreateDirectory(Cfg.McDir);
            string json = "{\"profiles\":{\"luxe\":{\"name\":\"luxe\",\"type\":\"custom\",\"created\":\"2026-01-01T00:00:00.000Z\",\"lastUsed\":\"2026-01-01T00:00:00.000Z\",\"icon\":\"Grass\",\"lastVersionId\":\"" + mc + "\"}},\"selectedProfile\":\"luxe\",\"clientToken\":\"" + Guid.NewGuid().ToString().Replace("-", "") + "\",\"authenticationDatabase\":{},\"launcherVersion\":{\"name\":\"Luxe Launcher\",\"format\":21}}";
            File.WriteAllText(p, json, Encoding.UTF8);
        }
        catch { }
    }

    static string RunInstaller(string javaExe, string installerJar, Action<string> status)
    {
        status("Установщик работает (1-3 мин, не закрывай)...");
        ProcessStartInfo psi = new ProcessStartInfo();
        psi.FileName = javaExe;
        psi.Arguments = "-jar \"" + installerJar + "\" --installClient \"" + Cfg.McDir + "\"";
        psi.WorkingDirectory = Cfg.BaseDir;
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.CreateNoWindow = true;
        StringBuilder tail = new StringBuilder();
        object lk = new object();
        Action<string> add = delegate(string s)
        {
            if (s == null) return;
            lock (lk)
            {
                tail.Append(s);
                tail.Append(" | ");
                if (tail.Length > 2000) tail.Remove(0, 500);
            }
        };
        Process p = new Process();
        p.StartInfo = psi;
        p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { add(e.Data); };
        p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { add(e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        bool exited = p.WaitForExit(12 * 60 * 1000);
        if (!exited)
        {
            try { p.Kill(); } catch { }
            throw new Exception("Установщик завис");
        }
        if (p.ExitCode != 0) throw new Exception("Установщик вернул код " + p.ExitCode + ": " + tail.ToString());
        return tail.ToString();
    }

    public static InstallResult InstallWithInstaller(string kind, string mc, Action<string> status, Action<int, int> prog)
    {
        string fv;
        string instUrl;
        if (kind == "forge")
        {
            status("Смотрю версии Forge...");
            Dictionary<string, object> pr = GetJson("https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json");
            Dictionary<string, object> promos = pr["promos"] as Dictionary<string, object>;
            object o = null;
            if (promos.ContainsKey(mc + "-recommended")) o = promos[mc + "-recommended"];
            else if (promos.ContainsKey(mc + "-latest")) o = promos[mc + "-latest"];
            if (o == null) throw new Exception("Для " + mc + " нет сборки Forge");
            fv = o.ToString();
            instUrl = "https://maven.minecraftforge.net/net/minecraftforge/forge/" + mc + "-" + fv + "/forge-" + mc + "-" + fv + "-installer.jar";
        }
        else
        {
            status("Смотрю версии NeoForge...");
            Dictionary<string, object> nl = GetJson("https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge");
            ArrayList vers = nl["versions"] as ArrayList;
            string best = null;
            foreach (object vo in vers)
            {
                string s = vo.ToString().ToLower();
                if (s.Contains("beta") || s.Contains("alpha")) continue;
                if (NeoMcOf(s) == mc) best = vo.ToString();
            }
            if (best == null) throw new Exception("Для " + mc + " нет сборки NeoForge");
            fv = best;
            instUrl = "https://maven.neoforged.net/releases/net/neoforged/neoforge/" + fv + "/neoforge-" + fv + "-installer.jar";
        }
        string java = EnsureJavaFor(mc, status);
        string tmpJar = Path.Combine(Cfg.DataDir, ".mod_installer.jar");
        try { if (File.Exists(tmpJar)) File.Delete(tmpJar); } catch { }
        status("Качаю установщик " + kind + " " + fv + "...");
        new WebClient().DownloadFile(instUrl, tmpJar);
        string vroot = Path.Combine(Cfg.McDir, "versions");
        Directory.CreateDirectory(vroot);
        List<string> before = new List<string>(Directory.GetDirectories(vroot));
        EnsureLauncherProfiles(mc);
        RunInstaller(java, tmpJar, status);
        try { File.Delete(tmpJar); } catch { }
        string created = null;
        foreach (string d in Directory.GetDirectories(vroot))
        {
            if (before.Contains(d)) continue;
            if (Path.GetFileName(d).ToLower().Contains("forge")) { created = d; break; }
        }
        if (created == null) throw new Exception("Установщик не создал версию");
        string id = Path.GetFileName(created);
        InstallResult baseRes = InstallVersion(mc, status, prog);
        return BuildModdedRes(id, LoadVerJson(id), baseRes, "", status, prog);
    }

    public static InstallResult InstallModded(string kind, string mc, Action<string> status, Action<int, int> prog)
    {
        if (kind == "fabric" || kind == "quilt") return InstallProfile(kind, mc, status, prog);
        return InstallWithInstaller(kind, mc, status, prog);
    }

    public static Dictionary<string, object> LoadVerJson(string id)
    {
        string p = Path.Combine(Path.Combine(Path.Combine(Cfg.McDir, "versions"), id), id + ".json");
        return ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(p, Encoding.UTF8));
    }

    public static string GameDirFor(string id)
    {
        try
        {
            string f = Path.Combine(Path.Combine(Path.Combine(Cfg.McDir, "versions"), id), "gamedir.txt");
            if (File.Exists(f))
            {
                string d = File.ReadAllText(f, Encoding.UTF8).Trim();
                if (!string.IsNullOrEmpty(d))
                {
                    string full = Path.IsPathRooted(d) ? d : Path.Combine(Cfg.DataDir, d);
                    Directory.CreateDirectory(full);
                    return full;
                }
            }
        }
        catch { }
        return Cfg.McDir;
    }

    static string LibKey(string p)
    {
        try
        {
            string n = p.Replace('/', '\\');
            int li = n.ToLower().IndexOf("libraries\\");
            string rel = li >= 0 ? n.Substring(li + 10) : n;
            string[] parts = rel.Split('\\');
            if (parts.Length < 4) return p.ToLower();
            string file = parts[parts.Length - 1];
            string ver = parts[parts.Length - 2];
            string art = parts[parts.Length - 3];
            if (!file.StartsWith(art + "-" + ver)) return p.ToLower();
            StringBuilder grp = new StringBuilder();
            for (int i = 0; i < parts.Length - 3; i++)
            {
                if (i > 0) grp.Append('.');
                grp.Append(parts[i]);
            }
            string rest = file.Substring((art + "-" + ver).Length);
            string classifier = "";
            if (rest.StartsWith("-") && rest.EndsWith(".jar")) classifier = rest.Substring(0, rest.Length - 4);
            return (grp.ToString() + ":" + art + classifier).ToLower();
        }
        catch { return p.ToLower(); }
    }

    public static bool IsModpack(string id)
    {
        try
        {
            string f = Path.Combine(Path.Combine(Path.Combine(Cfg.McDir, "versions"), id), "gamedir.txt");
            return File.Exists(f);
        }
        catch { return false; }
    }

    public static string ModpackName(string id)
    {
        try
        {
            string f = Path.Combine(Path.Combine(Path.Combine(Cfg.McDir, "versions"), id), "name.txt");
            if (File.Exists(f))
            {
                string n = File.ReadAllText(f, Encoding.UTF8).Trim();
                if (!string.IsNullOrEmpty(n)) return n;
            }
        }
        catch { }
        return null;
    }

    public static byte[] GetEmbedded(string name)
    {
        try
        {
            System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream(name))
            {
                if (s == null) return null;
                using (MemoryStream ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }
        catch { return null; }
    }

    // Логотип и шрифты вшиты в exe: рядом с ним больше не распаковывается
    // ни один файл. Метод остался только для уборки: старые версии клали
    // logo.png/logo.ico прямо возле exe.
    public static void ExtractEmbeddedAssets()
    {
        try
        {
            foreach (string n in new string[] { "logo.png", "logo.ico" })
            {
                try
                {
                    string old = Path.Combine(Cfg.BaseDir, n);
                    string moved = Path.Combine(Cfg.DataDir, n);
                    if (File.Exists(old) && !File.Exists(moved))
                    {
                        try { File.Move(old, moved); }
                        catch { }
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    public static string CatStr(Dictionary<string, object> d, string key, string def)
    {
        try
        {
            object o;
            if (d.TryGetValue(key, out o) && o != null) return o.ToString();
        }
        catch { }
        return def;
    }

    public static long CatLong(Dictionary<string, object> d, string key, long def)
    {
        try
        {
            object o;
            if (d.TryGetValue(key, out o) && o != null) return Convert.ToInt64(o.ToString());
        }
        catch { }
        return def;
    }

    public static List<Dictionary<string, object>> GetModpackCatalog()
    {
        List<Dictionary<string, object>> out_ = new List<Dictionary<string, object>>();
        try
        {
            string lf = Path.Combine(Cfg.BaseDir, "modpacks.json");
            if (File.Exists(lf))
            {
                Dictionary<string, object> lj = ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(lf, Encoding.UTF8));
                if (lj.ContainsKey("modpacks") && lj["modpacks"] is ArrayList)
                {
                    foreach (object o in (ArrayList)lj["modpacks"])
                    {
                        if (o is Dictionary<string, object>) out_.Add((Dictionary<string, object>)o);
                    }
                }
            }
        }
        catch { }
        try
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Cfg.UpdateUrl());
            req.Timeout = 12000;
            req.UserAgent = "LuxeLauncher";
            string txt;
            using (WebResponse resp = req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                txt = sr.ReadToEnd();
            Dictionary<string, object> js = ser.Deserialize<Dictionary<string, object>>(txt);
            if (js.ContainsKey("modpacks") && js["modpacks"] is ArrayList)
            {
                foreach (object o in (ArrayList)js["modpacks"])
                {
                    if (!(o is Dictionary<string, object>)) continue;
                    Dictionary<string, object> d = (Dictionary<string, object>)o;
                    if (!d.ContainsKey("id")) continue;
                    bool has = false;
                    foreach (Dictionary<string, object> e in out_)
                    {
                        object eo;
                        if (e.TryGetValue("id", out eo) && eo != null && eo.ToString() == d["id"].ToString()) { has = true; break; }
                    }
                    if (!has) out_.Add(d);
                }
            }
        }
        catch { }
        return out_;
    }

    public static Dictionary<string, object> FindCatalogPack(string id)
    {
        foreach (Dictionary<string, object> d in GetModpackCatalog())
        {
            object o;
            if (d.TryGetValue("id", out o) && o != null && o.ToString() == id) return d;
        }
        return null;
    }

    public static InstallResult InstallModpack(Dictionary<string, object> def, Action<string> status, Action<int, int> prog)
    {
        string id = CatStr(def, "id", "");
        string mc = CatStr(def, "mc", "");
        string loader = CatStr(def, "loader", "fabric").ToLower();
        string name = CatStr(def, "name", id);
        string gameDir = CatStr(def, "gameDir", ".minecraft-" + id);
        string mtype = CatStr(def, "type", "visual").ToLower();
        if (mtype != "crack" && mtype != "optifine") mtype = "visual";
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(mc)) throw new Exception("Битый модпак (нет id/mc)");
        if (loader != "fabric" && loader != "quilt") throw new Exception("Модпак поддерживает fabric/quilt");
        InstallResult baseRes = InstallProfile(loader, mc, status, prog);
        string vroot = Path.Combine(Cfg.McDir, "versions");
        string vdir = Path.Combine(vroot, id);
        Directory.CreateDirectory(vdir);
        Dictionary<string, object> pj = LoadVerJson(mc + "-" + loader);
        pj["id"] = id;
        File.WriteAllText(Path.Combine(vdir, id + ".json"), ser.Serialize(pj), Encoding.UTF8);
        File.WriteAllText(Path.Combine(vdir, "gamedir.txt"), gameDir, Encoding.UTF8);
        File.WriteAllText(Path.Combine(vdir, "name.txt"), name, Encoding.UTF8);
        File.WriteAllText(Path.Combine(vdir, "type.txt"), mtype, Encoding.UTF8);
        File.WriteAllText(Path.Combine(vdir, "base.txt"), mc, Encoding.UTF8);
        string gd = GameDirFor(id);
        string mods = Path.Combine(gd, "mods");
        Directory.CreateDirectory(mods);
        if (def.ContainsKey("mods") && def["mods"] is ArrayList)
        {
            int i = 0;
            foreach (object mo in (ArrayList)def["mods"])
            {
                i++;
                Dictionary<string, object> m = mo as Dictionary<string, object>;
                if (m == null) continue;
                string file = CatStr(m, "file", "");
                string url = CatStr(m, "url", "");
                if (string.IsNullOrEmpty(file) || string.IsNullOrEmpty(url)) continue;
                string dest = Path.Combine(mods, file);
                long want = CatLong(m, "size", 0);
                // Нет файла — качаем. Есть, но размер не тот (мод обновили
                // в каталоге) — тоже качаем: иначе игроки сидят на старой
                // версии, пока сами не переставят сборку.
                if (!File.Exists(dest) || (want > 0 && new FileInfo(dest).Length != want))
                {
                    status("Мод " + i + ": " + file);
                    DownloadReplace(url, dest, file);
                }
            }
        }
        Dictionary<string, object> pj2 = LoadVerJson(id);
        string defRepo = loader == "fabric" ? "https://maven.fabricmc.net/" : "https://maven.quiltmc.org/repository/release/";
        return BuildModdedRes(id, pj2, baseRes, defRepo, status, prog);
    }

    // Догоняем уже установленную сборку до каталога: если у мода в
    // update.json поменялся размер — тихо перекачиваем. Трогаем только
    // те файлы, у которых в каталоге указан size, чтобы не дёргать
    // fabric-api и прочее, что игрок мог заменить сам.
    public static void SyncPackMods(string id, Action<string> status)
    {
        try
        {
            if (!IsModpack(id)) return;
            if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()) return;
            Dictionary<string, object> cat = FindCatalogPack(id);
            if (cat == null) return;
            object mo0;
            if (!cat.TryGetValue("mods", out mo0) || !(mo0 is ArrayList)) return;
            string modsDir = Path.Combine(GameDirFor(id), "mods");
            Directory.CreateDirectory(modsDir);
            foreach (object mo in (ArrayList)mo0)
            {
                Dictionary<string, object> m = mo as Dictionary<string, object>;
                if (m == null) continue;
                string file = CatStr(m, "file", "");
                string url = CatStr(m, "url", "");
                long want = CatLong(m, "size", 0);
                if (file.Length == 0 || url.Length == 0 || want <= 0) continue;
                string dest = Path.Combine(modsDir, file);
                try
                {
                    if (File.Exists(dest) && new FileInfo(dest).Length == want) continue;
                    if (status != null) status("Обновляю " + file + "...");
                    DownloadReplace(url, dest, file);
                }
                catch { }
            }
        }
        catch { }
    }

    public static string BaseMcOf(string id)
    {
        try
        {
            string f = Path.Combine(Path.Combine(Path.Combine(Cfg.McDir, "versions"), id), "base.txt");
            if (File.Exists(f))
            {
                string mc = File.ReadAllText(f, Encoding.UTF8).Trim();
                if (!string.IsNullOrEmpty(mc)) return mc;
            }
        }
        catch { }
        return McOf(id);
    }

    public static string ModpackType(string id)
    {
        try
        {
            string f = Path.Combine(Path.Combine(Path.Combine(Cfg.McDir, "versions"), id), "type.txt");
            if (File.Exists(f))
            {
                string t = File.ReadAllText(f, Encoding.UTF8).Trim().ToLower();
                if (t == "crack") return "crack";
                if (t == "optifine") return "optifine";
            }
        }
        catch { }
        return "visual";
    }

    public static bool IsInstalled(string id)
    {
        string vdir = Path.Combine(Path.Combine(Cfg.McDir, "versions"), id);
        return File.Exists(Path.Combine(vdir, id + ".json"));
    }

    public static InstallResult BuildModdedRes(string id, Dictionary<string, object> pj, InstallResult baseRes, string defRepo, Action<string> status, Action<int, int> prog)
    {
        string libDir = Path.Combine(Cfg.McDir, "libraries");
        List<string> modCp = new List<string>();
        List<string[]> jobs = new List<string[]>();
        List<string> mnat = new List<string>();
        Dictionary<string, object> wrap = new Dictionary<string, object>();
        wrap["libraries"] = pj["libraries"];
        CollectLibs(wrap, libDir, modCp, jobs, mnat);
        ArrayList libs = pj["libraries"] as ArrayList;
        if (libs != null)
        {
            foreach (object lo in libs)
            {
                Dictionary<string, object> lib = lo as Dictionary<string, object>;
                if (lib == null) continue;
                if (!IsAllowed(lib.ContainsKey("rules") ? lib["rules"] as ArrayList : null)) continue;
                if (lib.ContainsKey("downloads")) continue;
                if (!lib.ContainsKey("name")) continue;
                // OptiFine-профиль ссылается на свои jar без downloads:
                // установщик уже положил их в libraries, поэтому сначала
                // ищем локальный файл, и только потом пробуем maven.
                string lp = MavenLocalPath(lib["name"].ToString(), libDir);
                if (lp != null && File.Exists(lp)) { modCp.Add(lp); continue; }
                object uo;
                string repo = defRepo;
                if (lib.TryGetValue("url", out uo) && uo != null) repo = uo.ToString();
                if (string.IsNullOrEmpty(repo)) continue;
                MavenToJob(lib["name"].ToString(), repo, libDir, jobs, mnat, modCp);
            }
        }
        if (jobs.Count > 0) DlParallel(jobs, 8, "Библиотеки", status, prog);
        foreach (string nj in mnat) { try { ExtractNatives(nj, baseRes.NativesDir); } catch { } }
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<string> cp = new List<string>();
        foreach (string s in modCp) { if (seen.Add(LibKey(s))) cp.Add(s); }
        string mkind = KindOf(id);
        string dropClient = (mkind == "forge" || mkind == "neoforge") ? (McOf(id) + ".jar") : null;
        foreach (string s in baseRes.Classpath)
        {
            if (dropClient != null && Path.GetFileName(s).Equals(dropClient, StringComparison.OrdinalIgnoreCase)) continue;
            if (seen.Add(LibKey(s))) cp.Add(s);
        }
        InstallResult res = new InstallResult();
        res.Id = id;
        res.VersionJson = pj;
        res.Classpath = cp;
        res.NativesDir = baseRes.NativesDir;
        res.GameDir = GameDirFor(id);
        res.LogArg = baseRes.LogArg;
        res.AssetId = baseRes.AssetId;
        res.VerType = baseRes.VerType;
        return res;
    }

    public static InstallResult PrepareLaunch(string id, Action<string> status, Action<int, int> prog)
    {
        string kind = KindOf(id);
        if (IsInstalled(id))
        {
            // Сборки обновляют свои моды сами: каталог помнит размер файла,
            // и если он разошёлся — новый jar скачается перед запуском.
            if (IsModpack(id)) SyncPackMods(id, status);
            if (kind == "vanilla" && !IsModpack(id))
            {
                // Профили OptiFine (и другие inheritsFrom-профили) не значатся
                // в манифесте Mojang: раньше запуск падал «Версия не найдена».
                Dictionary<string, object> own = LoadVerJson(id);
                string parent = (own.ContainsKey("inheritsFrom") && own["inheritsFrom"] != null)
                    ? own["inheritsFrom"].ToString() : null;
                if (!string.IsNullOrEmpty(parent) && parent != id
                    && own.ContainsKey("libraries") && own.ContainsKey("mainClass"))
                {
                    InstallResult parentRes = InstallVersion(parent, status, prog);
                    return BuildModdedRes(id, own, parentRes, "", status, prog);
                }
                return InstallVersion(id, status, prog);
            }
            string mc = BaseMcOf(id);
            InstallResult baseRes = InstallVersion(mc, status, prog);
            Dictionary<string, object> pj = LoadVerJson(id);
            string defRepo = kind == "fabric" ? "https://maven.fabricmc.net/" : (kind == "quilt" ? "https://maven.quiltmc.org/repository/release/" : "");
            return BuildModdedRes(id, pj, baseRes, defRepo, status, prog);
        }
        Dictionary<string, object> cat = FindCatalogPack(id);
        if (cat != null) return InstallModpack(cat, status, prog);
        if (kind == "vanilla") return InstallVersion(id, status, prog);
        return InstallModded(kind, McOf(id), status, prog);
    }

    public static bool HasModulePath(Dictionary<string, object> vj)
    {
        try
        {
            if (!vj.ContainsKey("arguments")) return false;
            Dictionary<string, object> args = vj["arguments"] as Dictionary<string, object>;
            if (args == null || !args.ContainsKey("jvm")) return false;
            ArrayList jj = args["jvm"] as ArrayList;
            if (jj == null) return false;
            foreach (object a in jj)
            {
                if (a is string && ((string)a).Trim() == "-p") return true;
                if (a is Dictionary<string, object>)
                {
                    Dictionary<string, object> d = (Dictionary<string, object>)a;
                    object v;
                    if (d.TryGetValue("value", out v))
                    {
                        if (v is string && ((string)v).Trim() == "-p") return true;
                        if (v is ArrayList)
                        {
                            foreach (object x in (ArrayList)v)
                            {
                                if (x is string && ((string)x).Trim() == "-p") return true;
                            }
                        }
                    }
                }
            }
        }
        catch { }
        return false;
    }

    public static int CompareVersions(string a, string b)
    {
        string[] pa = a.Trim().TrimStart('v').Split('.');
        string[] pb = b.Trim().TrimStart('v').Split('.');
        int n = Math.Max(pa.Length, pb.Length);
        for (int i = 0; i < n; i++)
        {
            int x = 0;
            int y = 0;
            if (i < pa.Length) { try { x = Convert.ToInt32(pa[i]); } catch { } }
            if (i < pb.Length) { try { y = Convert.ToInt32(pb[i]); } catch { } }
            if (x != y) return x > y ? 1 : -1;
        }
        return 0;
    }

    public static string FindLocalJava()
    {
        try
        {
            if (Directory.Exists(Cfg.JdkDir))
            {
                string[] found = Directory.GetFiles(Cfg.JdkDir, Os.JavaName, SearchOption.AllDirectories);
                if (found.Length > 0) return found[0];
            }
        }
        catch { }

        // Linux/macOS: JAVA_HOME, стандартные каталоги, PATH через ":"
        if (!Os.IsWin)
        {
            try
            {
                string home = Environment.GetEnvironmentVariable("JAVA_HOME");
                if (!string.IsNullOrEmpty(home))
                {
                    string p = Path.Combine(Path.Combine(home, "bin"), Os.JavaName);
                    if (File.Exists(p)) return p;
                }
            }
            catch { }
            string[] unixDirs = new string[] {
                "/usr/lib/jvm", "/usr/java", "/opt/java", "/opt/jdk",
                "/usr/local/lib/jvm", "/Library/Java/JavaVirtualMachines"
            };
            foreach (string b in unixDirs)
            {
                try
                {
                    if (!Directory.Exists(b)) continue;
                    foreach (string d in Directory.GetDirectories(b))
                    {
                        string p = Path.Combine(Path.Combine(d, "bin"), Os.JavaName);
                        if (File.Exists(p)) return p;
                    }
                }
                catch { }
            }
            string[] unixHard = new string[] {
                "/usr/bin/java", "/usr/local/bin/java", "/opt/homebrew/bin/java"
            };
            foreach (string p in unixHard) { try { if (File.Exists(p)) return p; } catch { } }
            try
            {
                string upath = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (string dir in upath.Split(':'))
                {
                    try
                    {
                        if (string.IsNullOrEmpty(dir)) continue;
                        string p = Path.Combine(dir.Trim(), Os.JavaName);
                        if (File.Exists(p)) return p;
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        string[] hard = new string[] {
            "C:\\Program Files\\Eclipse Adoptium\\jdk-21-hotspot\\bin\\java.exe",
            "C:\\Program Files\\Eclipse Foundation\\jdk-21-hotspot\\bin\\java.exe",
            "C:\\Program Files\\Microsoft\\jdk-21\\bin\\java.exe",
            "C:\\Program Files\\Eclipse Adoptium\\jdk-17-hotspot\\bin\\java.exe",
            "C:\\Program Files\\Java\\jdk-17\\bin\\java.exe"
        };
        foreach (string p in hard) { try { if (File.Exists(p)) return p; } catch { } }
        string[] bases = new string[] {
            "C:\\Program Files\\Eclipse Adoptium",
            "C:\\Program Files\\Java",
            "C:\\Program Files\\Microsoft"
        };
        foreach (string b in bases)
        {
            try
            {
                if (!Directory.Exists(b)) continue;
                foreach (string d in Directory.GetDirectories(b))
                {
                    string p = Path.Combine(Path.Combine(d, "bin"), "java.exe");
                    if (File.Exists(p)) return p;
                }
            }
            catch { }
        }
        try
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in path.Split(';'))
            {
                try
                {
                    if (string.IsNullOrEmpty(dir)) continue;
                    string p = Path.Combine(dir.Trim(), "java.exe");
                    if (File.Exists(p)) return p;
                }
                catch { }
            }
        }
        catch { }
        return null;
    }

    public static string EnsureJava(Action<string> status)
    {
        string j = FindLocalJava();
        if (j != null) return j;
        status("Java не найдена. Качаю portable Java 21 (~190 МБ)...");
        Directory.CreateDirectory(Cfg.JdkDir);
        string zip = Path.Combine(Cfg.DataDir, ".jdk_download.zip");
        string[] urls = new string[] { Cfg.JavaJreUrl, Cfg.JavaJdkUrl };
        bool ok = false;
        foreach (string u in urls)
        {
            try
            {
                status("Скачивание Java...");
                DownloadWithProgress(u, zip, "Java");
                FileInfo fi = new FileInfo(zip);
                if (fi.Exists && fi.Length > 10 * 1024 * 1024) { ok = true; break; }
            }
            catch (Exception ex) { status("Повтор... " + ex.Message); }
        }
        if (Progress != null) { try { Progress(-1, -1, null); } catch { } }
        if (!ok || !File.Exists(zip)) throw new Exception("Не удалось скачать Java. Проверь интернет.");
        status("Распаковка Java...");
        string tmp = Path.Combine(Cfg.DataDir, ".jdk_tmp");
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        if (Directory.Exists(Cfg.JdkDir)) Directory.Delete(Cfg.JdkDir, true);
        Directory.CreateDirectory(Cfg.JdkDir);
        ZipFile.ExtractToDirectory(zip, tmp);
        ChmodDir(tmp);
        string[] found = Directory.GetFiles(tmp, Os.JavaName, SearchOption.AllDirectories);
        if (found.Length == 0) throw new Exception("Java распакована, но java.exe не найден");
        string binDir = Path.GetDirectoryName(found[0]);
        string root = Directory.GetParent(binDir).FullName;
        foreach (string f in Directory.GetFileSystemEntries(root))
        {
            string dest = Path.Combine(Cfg.JdkDir, Path.GetFileName(f));
            if (Directory.Exists(f)) CopyDir(f, dest);
            else File.Copy(f, dest, true);
        }
        try { Directory.Delete(tmp, true); } catch { }
        try { File.Delete(zip); } catch { }
        ChmodDir(Cfg.JdkDir);
        string j2 = FindLocalJava();
        if (j2 == null) throw new Exception("Java установлена, но java.exe не найден в .jdk");
        return j2;
    }

    // На Linux zip не хранит права доступа: после распаковки java и
    // jspawnhelper нужно снова сделать исполняемыми, иначе Java не стартует.
    static void ChmodDir(string dir)
    {
        if (Os.IsWin) return;
        try
        {
            ProcessStartInfo p = new ProcessStartInfo();
            p.FileName = "chmod";
            p.Arguments = "-R +x \"" + dir + "\"";
            p.UseShellExecute = false;
            p.CreateNoWindow = true;
            Process pr = Process.Start(p);
            if (pr != null) pr.WaitForExit(30000);
        }
        catch { }
    }

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (string f in Directory.GetFiles(src))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (string d in Directory.GetDirectories(src))
            CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    public static Dictionary<string, object> GetJson(string url)
    {
        WebClient wc = new WebClient();
        wc.Encoding = Encoding.UTF8;
        string txt = wc.DownloadString(url);
        wc.Dispose();
        return ser.Deserialize<Dictionary<string, object>>(txt);
    }

    static string OD(Dictionary<string, object> d, string key)
    {
        if (d == null || !d.ContainsKey(key) || d[key] == null) return null;
        return d[key].ToString();
    }

    public static bool IsAllowed(ArrayList rules)
    {
        if (rules == null || rules.Count == 0) return true;
        bool allowed = false;
        foreach (object ro in rules)
        {
            Dictionary<string, object> r = ro as Dictionary<string, object>;
            if (r == null) continue;
            // features (is_demo_user, has_custom_resolution, quick play...) у нас всегда false
            if (r.ContainsKey("features")) continue;
            bool match = true;
            if (r.ContainsKey("os") && r["os"] is Dictionary<string, object>)
            {
                Dictionary<string, object> os = (Dictionary<string, object>)r["os"];
                object nm;
                if (os.TryGetValue("name", out nm) && nm != null)
                    match = Os.OsMatches(nm.ToString());
                object arch;
                if (match && os.TryGetValue("arch", out arch) && arch != null)
                    match = Os.ArchMatches(arch.ToString());
            }
            if (match)
            {
                object act = r.ContainsKey("action") ? r["action"] : null;
                allowed = (act != null && act.ToString() == "allow");
            }
        }
        return allowed;
    }

    // ---- Прогресс сетевой загрузки ----
    // Одна точка правды для всего интерфейса: форма подменяет делегат
    // своим обработчиком (полоса в витрине + текст в статусе).
    // got / total — байты, label — что именно качаем. got < 0 — сброс.
    public static Action<long, long, string> Progress;

    // Скачать файл с отчётом о прогрессе. У синхронного
    // WebClient.DownloadFile события прогресса не приходят вовсе
    // (они есть только у асинхронных методов), поэтому читаем поток
    // сами и дёргаем Progress при смене процента.
    public static void DownloadWithProgress(string url, string dest, string label)
    {
        string dir = Path.GetDirectoryName(dest);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
        req.UserAgent = "LuxeLauncher/" + Cfg.AppVersion + " (offline launcher)";
        req.Timeout = 30000;
        using (WebResponse resp = req.GetResponse())
        {
            long total = resp.ContentLength;
            Stream src = resp.GetResponseStream();
            using (FileStream fs = new FileStream(dest, FileMode.Create, FileAccess.Write))
            {
                byte[] buf = new byte[65536];
                long got = 0;
                int last = -1;
                int n;
                while ((n = src.Read(buf, 0, buf.Length)) > 0)
                {
                    fs.Write(buf, 0, n);
                    got += n;
                    if (Progress != null)
                    {
                        int pct = total > 0 ? (int)(got * 100 / total) : -1;
                        if (pct != last) { last = pct; Progress(got, total, label); }
                    }
                }
            }
        }
    }

    public static void DownloadFile(string url, string dest)
    {
        string dir = Path.GetDirectoryName(dest);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        if (File.Exists(dest)) return;
        WebClient wc = new WebClient();
        try { wc.DownloadFile(url, dest); }
        finally { wc.Dispose(); }
    }

    // Скачать поверх существующего файла. Качаем в .part и подменяем
    // только после успешной загрузки — иначе оборванная сеть оставит
    // битый jar, и игра упадёт на старте. label уходит в полосу
    // прогресса: игрок видит, что именно грузится и на сколько.
    public static void DownloadReplace(string url, string dest, string label)
    {
        string dir = Path.GetDirectoryName(dest);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        string part = dest + ".part";
        try
        {
            DownloadWithProgress(url, part, label);
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(part, dest);
        }
        finally
        {
            try { if (File.Exists(part)) File.Delete(part); } catch { }
            if (Progress != null) { try { Progress(-1, -1, null); } catch { } }
        }
    }

    public static List<string> GetInstalledVersions()
    {
        List<string> out_ = new List<string>();
        string vdir = Path.Combine(Cfg.McDir, "versions");
        if (!Directory.Exists(vdir)) return out_;
        foreach (string d in Directory.GetDirectories(vdir))
        {
            string name = Path.GetFileName(d);
            if (File.Exists(Path.Combine(d, name + ".json")))
                out_.Add(name);
        }
        return out_;
    }

    public static InstallResult InstallVersion(string versionId, Action<string> status, Action<int, int> prog)
    {
        Directory.CreateDirectory(Cfg.McDir);
        status("Получаю список версий...");
        Dictionary<string, object> manifest = GetJson(Cfg.ManifestUrl);
        ArrayList vers = manifest["versions"] as ArrayList;
        Dictionary<string, object> entry = null;
        foreach (object vo in vers)
        {
            Dictionary<string, object> v = vo as Dictionary<string, object>;
            if (v != null && OD(v, "id") == versionId) { entry = v; break; }
        }
        if (entry == null) throw new Exception("Версия " + versionId + " не найдена на Mojang");
        status("Качаю описание " + versionId + "...");
        Dictionary<string, object> vj = GetJson(OD(entry, "url"));
        string vdir = Path.Combine(Path.Combine(Cfg.McDir, "versions"), versionId);
        Directory.CreateDirectory(vdir);
        File.WriteAllText(Path.Combine(vdir, versionId + ".json"), ser.Serialize(vj), Encoding.UTF8);

        Dictionary<string, object> dl = vj["downloads"] as Dictionary<string, object>;
        Dictionary<string, object> client = dl["client"] as Dictionary<string, object>;
        string clientJar = Path.Combine(vdir, versionId + ".jar");
        if (!File.Exists(clientJar))
        {
            status("Качаю клиент " + versionId + "...");
            DownloadWithProgress(client["url"].ToString(), clientJar, "Клиент " + versionId);
            if (Progress != null) { try { Progress(-1, -1, null); } catch { } }
        }

        string libDir = Path.Combine(Cfg.McDir, "libraries");
        string nativesDir = Path.Combine(vdir, "natives");
        Directory.CreateDirectory(nativesDir);
        InstallResult res = new InstallResult();
        res.Id = versionId;
        res.VersionJson = vj;
        res.NativesDir = nativesDir;
        res.GameDir = GameDirFor(versionId);
        res.VerType = vj.ContainsKey("type") ? vj["type"].ToString() : "release";
        List<string[]> dlJobs = new List<string[]>();
        List<string> nativeJars = new List<string>();
        CollectLibs(vj, libDir, res.Classpath, dlJobs, nativeJars);
        if (dlJobs.Count > 0)
            DlParallel(dlJobs, 8, "Библиотеки", status, prog);
        res.Classpath.Add(clientJar);
        foreach (string nj in nativeJars)
        {
            try { ExtractNatives(nj, nativesDir); } catch { }
        }

        if (vj.ContainsKey("logging") && vj["logging"] is Dictionary<string, object>)
        {
            try
            {
                Dictionary<string, object> logging = (Dictionary<string, object>)vj["logging"];
                Dictionary<string, object> clientLog = logging["client"] as Dictionary<string, object>;
                Dictionary<string, object> file = clientLog["file"] as Dictionary<string, object>;
                string logDir = Path.Combine(Path.Combine(Cfg.McDir, "assets"), "log_configs");
                string logPath = Path.Combine(logDir, file["id"].ToString());
                DownloadFile(file["url"].ToString(), logPath);
                string arg = clientLog["argument"].ToString().Replace("${path}", logPath);
                res.LogArg = arg;
            }
            catch { }
        }

        Dictionary<string, object> ai = vj["assetIndex"] as Dictionary<string, object>;
        res.AssetId = ai["id"].ToString();
        string idxDir = Path.Combine(Path.Combine(Cfg.McDir, "assets"), "indexes");
        Directory.CreateDirectory(idxDir);
        string idxPath = Path.Combine(idxDir, ai["id"].ToString() + ".json");
        if (!File.Exists(idxPath))
        {
            status("Качаю asset index...");
            new WebClient().DownloadFile(ai["url"].ToString(), idxPath);
        }
        Dictionary<string, object> idx = ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(idxPath, Encoding.UTF8));
        Dictionary<string, object> objs = idx["objects"] as Dictionary<string, object>;
        List<string> keys = new List<string>(objs.Keys);
        int t2 = keys.Count;
        List<string[]> ajobs = new List<string[]>();
        bool legacy = res.AssetId == "legacy";
        foreach (string kk in keys)
        {
            Dictionary<string, object> o = objs[kk] as Dictionary<string, object>;
            string h = o["hash"].ToString();
            string sub = h.Substring(0, 2);
            string objPath;
            if (legacy)
                objPath = Path.Combine(Path.Combine(Cfg.McDir, "resources"), kk.Replace("/", "\\"));
            else
                objPath = Path.Combine(Path.Combine(Path.Combine(Path.Combine(Cfg.McDir, "assets"), "objects"), sub), h);
            if (!File.Exists(objPath))
                ajobs.Add(new string[] { "https://resources.download.minecraft.net/" + sub + "/" + h, objPath });
        }
        // ассеты — самое долгое место: качаем в 16 потоков
        if (ajobs.Count > 0)
            DlParallel(ajobs, 16, "Ассеты", status, prog);
        else
            prog(t2, t2);
        return res;
    }

    // Многопоточная закачка с показом скорости
    static void DlParallel(List<string[]> jobs, int threads, string label, Action<string> status, Action<int, int> prog)
    {
        int done = 0;
        int total = jobs.Count;
        long bytes = 0;
        Stopwatch sw = Stopwatch.StartNew();
        status(label + ": 0/" + total);
        prog(0, total);
        Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = threads }, delegate(string[] job)
        {
            try
            {
                string dir = Path.GetDirectoryName(job[1]);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                new WebClient().DownloadFile(job[0], job[1]);
                try { Interlocked.Add(ref bytes, new FileInfo(job[1]).Length); } catch { }
            }
            catch { }
            int n = Interlocked.Increment(ref done);
            if (n % 10 == 0 || n == total)
            {
                double sec = sw.Elapsed.TotalSeconds;
                if (sec < 0.1) sec = 0.1;
                double mbs = (bytes / 1048576.0) / sec;
                status(label + ": " + n + "/" + total + "  •  " + mbs.ToString("0.0") + " МБ/с");
                prog(n, total);
            }
        });
        sw.Stop();
    }

    static void ExtractNatives(string jar, string nativesDir)
    {
        using (ZipArchive z = ZipFile.OpenRead(jar))
        {
            foreach (ZipArchiveEntry e in z.Entries)
            {
                if (e.FullName.StartsWith("META-INF")) continue;
                if (e.FullName.EndsWith("/")) continue;
                string name = Path.GetFileName(e.FullName);
                if (string.IsNullOrEmpty(name)) continue;
                string target = Path.Combine(nativesDir, name);
                e.ExtractToFile(target, true);
            }
        }
    }

    static string Rep(string s, Dictionary<string, string> map)
    {
        string r = s;
        foreach (KeyValuePair<string, string> kv in map) r = r.Replace(kv.Key, kv.Value);
        return r;
    }

    public static void BuildArgs(InstallResult inst, string versionId, string nick, string uuid, string ram,
        out List<string> jvm, out string main, out List<string> game, out string cp)
    {
        BuildArgs(inst, versionId, nick, uuid, ram, null, null, out jvm, out main, out game, out cp);
    }

    // token и xuid не пустые — запуск с лицензией (см. Auth.cs),
    // пустые — прежний офлайн-запуск с токеном "0"
    public static void BuildArgs(InstallResult inst, string versionId, string nick, string uuid, string ram,
        string token, string xuid, out List<string> jvm, out string main, out List<string> game, out string cp)
    {
        Dictionary<string, object> vj = inst.VersionJson;
        string assetsDir = Path.Combine(Cfg.McDir, "assets");
        string assetId = inst.AssetId;
        if (string.IsNullOrEmpty(assetId))
        {
            Dictionary<string, object> ai = vj["assetIndex"] as Dictionary<string, object>;
            assetId = ai["id"].ToString();
        }
        cp = string.Join(Os.CpSep, inst.Classpath.ToArray());
        Dictionary<string, string> map = new Dictionary<string, string>();
        map["${natives_directory}"] = inst.NativesDir;
        map["${launcher_name}"] = "LuxeLauncher";
        map["${launcher_version}"] = "1.0";
        map["${classpath}"] = cp;
        map["${classpath_separator}"] = Os.CpSep;
        map["${library_directory}"] = Path.Combine(Cfg.McDir, "libraries");
        map["${version_name}"] = versionId;
        map["${game_directory}"] = inst.GameDir;
        map["${assets_root}"] = assetsDir;
        map["${assets_index_name}"] = assetId;
        bool licensed = !string.IsNullOrEmpty(token);
        map["${auth_uuid}"] = uuid;
        map["${auth_access_token}"] = licensed ? token : "0";
        map["${auth_player_name}"] = nick;
        map["${user_type}"] = licensed ? "msa" : "legacy";
        map["${version_type}"] = !string.IsNullOrEmpty(inst.VerType) ? inst.VerType : (vj.ContainsKey("type") ? vj["type"].ToString() : "release");
        map["${game_assets}"] = assetsDir;
        map["${auth_session}"] = licensed ? ("token:" + token) : "0";
        map["${user_properties}"] = "{}";
        map["${clientid}"] = Guid.NewGuid().ToString().Replace("-", "");
        map["${auth_xuid}"] = string.IsNullOrEmpty(xuid) ? "0" : xuid;

        jvm = new List<string>();
        jvm.Add("-Xmx" + ram + "G");
        jvm.Add("-Xms1G");
        if (!string.IsNullOrEmpty(inst.LogArg)) jvm.Add(Rep(inst.LogArg, map));
        jvm.Add("-Djava.library.path=" + inst.NativesDir);
        game = new List<string>();

        ResolveArgs(vj, map, jvm, game);
        main = vj["mainClass"].ToString();

        // наследование (Forge/NeoForge): у них нет ванильных аргументов — добираем из родителя
        object inh;
        if (vj.TryGetValue("inheritsFrom", out inh) && inh != null)
        {
            bool needUser = true;
            foreach (string s in game) { if (s == "--username") { needUser = false; break; } }
            if (needUser)
            {
                try
                {
                    string pv = inh.ToString();
                    string ppath = Path.Combine(Path.Combine(Path.Combine(Cfg.McDir, "versions"), pv), pv + ".json");
                    if (File.Exists(ppath))
                    {
                        Dictionary<string, object> pj2 = ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(ppath, Encoding.UTF8));
                        List<string> pjvm = new List<string>();
                        List<string> pgame = new List<string>();
                        ResolveArgs(pj2, map, pjvm, pgame);
                        game.InsertRange(0, pgame);
                        jvm.InsertRange(0, pjvm);
                    }
                }
                catch { }
            }
        }
    }

    static void ResolveArgs(Dictionary<string, object> vj, Dictionary<string, string> map, List<string> jvm, List<string> game)
    {
        if (vj.ContainsKey("arguments") && vj["arguments"] is Dictionary<string, object>)
        {
            Dictionary<string, object> args = (Dictionary<string, object>)vj["arguments"];
            ArrayList jj = args.ContainsKey("jvm") ? args["jvm"] as ArrayList : null;
            if (jj != null)
            {
                foreach (object a in jj)
                {
                    if (a is string) jvm.Add(Rep((string)a, map));
                    else if (a is Dictionary<string, object>)
                    {
                        Dictionary<string, object> d = (Dictionary<string, object>)a;
                        ArrayList rules = d.ContainsKey("rules") ? d["rules"] as ArrayList : null;
                        if (!IsAllowed(rules)) continue;
                        object v = d["value"];
                        if (v is string) jvm.Add(Rep((string)v, map));
                        else if (v is ArrayList) foreach (object x in (ArrayList)v) jvm.Add(Rep(x.ToString(), map));
                    }
                }
            }
            ArrayList gg = args.ContainsKey("game") ? args["game"] as ArrayList : null;
            if (gg != null)
            {
                foreach (object g in gg)
                {
                    if (g is string) game.Add(Rep((string)g, map));
                    else if (g is Dictionary<string, object>)
                    {
                        Dictionary<string, object> d = (Dictionary<string, object>)g;
                        ArrayList rules = d.ContainsKey("rules") ? d["rules"] as ArrayList : null;
                        if (!IsAllowed(rules)) continue;
                        object v = d["value"];
                        if (v is string) game.Add(Rep((string)v, map));
                        else if (v is ArrayList) foreach (object x in (ArrayList)v) game.Add(Rep(x.ToString(), map));
                    }
                }
            }
        }
        else if (vj.ContainsKey("minecraftArguments"))
        {
            string[] parts = vj["minecraftArguments"].ToString().Split(' ');
            foreach (string p in parts) game.Add(Rep(p, map));
        }
    }
}

// ================= КРАСИВЫЙ ДИЗАЙН =================
// ============================================================
// MODRINTH — поиск модов, скачивание и автоустановка зависимостей
// ============================================================
class ModInfo
{
    public string id;
    public string slug;
    public string title;
    public string desc;
    public string author;
    public string iconUrl;
    public long downloads;
    public string[] cats;
    public bool installed;
    public bool disabled;
    public string fileName;
    public ModInfo() { id = ""; slug = ""; title = ""; desc = ""; author = ""; iconUrl = null; cats = new string[0]; }
}

// Контекст одной установки: рекурсия по зависимостям + учёт уже поставленного
class ModJob
{
    public string mcVer = "";
    public string loader = "fabric";
    public string modsDir = "";
    public Action<string> status;
    public HashSet<string> done = new HashSet<string>();
    public Dictionary<string, string> index = new Dictionary<string, string>();
    public List<string> skipped = new List<string>();
    public int installed = 0;
}

static class Modrinth
{
    public const string ApiBase = "https://api.modrinth.com/v2/";
    static readonly JavaScriptSerializer jser = new JavaScriptSerializer();

    static Modrinth()
    {
        // без TLS 1.2 Modrinth отдаёт ошибку соединения
        try
        {
            if ((ServicePointManager.SecurityProtocol & SecurityProtocolType.Tls12) == 0)
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }
        catch { }
    }

    static void Prep(HttpWebRequest req)
    {
        // Modrinth требует User-Agent, иначе отдаёт 400
        req.UserAgent = "LuxeLauncher/" + Cfg.AppVersion + " (offline launcher)";
        req.Timeout = 15000;
        req.Accept = "application/json";
    }

    public static string Raw(string url)
    {
        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
        Prep(req);
        using (WebResponse resp = req.GetResponse())
        using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            return sr.ReadToEnd();
    }

    // DeserializeObject отдаёт массивы как object[], а Deserialize<T> — как ArrayList.
    // Приводим оба варианта к общему типу.
    public static ArrayList AsList(object o)
    {
        ArrayList a = o as ArrayList;
        if (a != null) return a;
        object[] arr = o as object[];
        if (arr != null) return new ArrayList(arr);
        return null;
    }

    public static string Str(Dictionary<string, object> d, string key, string def)
    {
        try
        {
            object o;
            if (d != null && d.TryGetValue(key, out o) && o != null) return o.ToString();
        }
        catch { }
        return def;
    }

    public static List<string> StrList(Dictionary<string, object> d, string key)
    {
        List<string> l = new List<string>();
        try
        {
            object o;
            ArrayList a;
            if (d != null && d.TryGetValue(key, out o)) a = AsList(o);
            else a = null;
            if (a != null) foreach (object x in a) if (x != null) l.Add(x.ToString());
        }
        catch { }
        return l;
    }

    public static Dictionary<string, object> PrimaryFile(Dictionary<string, object> ver)
    {
        try
        {
            object o;
            ArrayList fl;
            if (ver == null || !ver.TryGetValue("files", out o)) return null;
            fl = AsList(o);
            if (fl == null) return null;
            Dictionary<string, object> fallback = null;
            foreach (object x in fl)
            {
                Dictionary<string, object> f = x as Dictionary<string, object>;
                if (f == null) continue;
                object pn;
                if (f.TryGetValue("primary", out pn) && pn != null)
                {
                    try { if (Convert.ToBoolean(pn)) return f; } catch { }
                }
                if (fallback == null) fallback = f;
            }
            return fallback;
        }
        catch { return null; }
    }

    // ---- Поиск ----
    public static List<ModInfo> Search(string query, string mcVer, string loader, int limit)
    {
        List<ModInfo> res = new List<ModInfo>();
        StringBuilder fb = new StringBuilder("[[\"project_type:mod\"]");
        if (!string.IsNullOrEmpty(mcVer)) fb.Append(",[\"versions:" + mcVer + "\"]");
        if (!string.IsNullOrEmpty(loader) && loader != "any") fb.Append(",[\"categories:" + loader + "\"]");
        fb.Append("]");
        string url = ApiBase + "search?limit=" + limit;
        if (!string.IsNullOrEmpty(query)) url += "&query=" + Uri.EscapeDataString(query);
        url += "&facets=" + Uri.EscapeDataString(fb.ToString());
        ArrayList hits = null;
        Dictionary<string, object> root = jser.DeserializeObject(Raw(url)) as Dictionary<string, object>;
        if (root != null)
        {
            object ho;
            if (root.TryGetValue("hits", out ho)) hits = AsList(ho);
        }
        if (hits == null) return res;
        foreach (object it in hits)
        {
            Dictionary<string, object> h = it as Dictionary<string, object>;
            if (h == null) continue;
            ModInfo m = new ModInfo();
            m.id = Str(h, "project_id", "");
            if (string.IsNullOrEmpty(m.id)) continue;
            m.slug = Str(h, "slug", m.id);
            m.title = Str(h, "title", m.id);
            m.desc = Str(h, "description", "");
            m.author = Str(h, "author", "");
            m.iconUrl = Str(h, "icon_url", null);
            object dlo;
            if (h.TryGetValue("downloads", out dlo) && dlo != null)
            {
                try { m.downloads = Convert.ToInt64(dlo); } catch { }
            }
            m.cats = StrList(h, "categories").ToArray();
            res.Add(m);
        }
        return res;
    }

    // ---- Версии проекта ----
    public static List<Dictionary<string, object>> Versions(string projectId, string mcVer, string loader)
    {
        List<Dictionary<string, object>> res = new List<Dictionary<string, object>>();
        // сначала точная фильтрация на стороне Modrinth
        try
        {
            StringBuilder q = new StringBuilder("game_versions=[\"");
            q.Append(mcVer).Append("\"]");
            if (!string.IsNullOrEmpty(loader) && loader != "any")
                q.Append("&loaders=[\"").Append(loader).Append("\"]");
            ArrayList a = AsList(jser.DeserializeObject(Raw(ApiBase + "project/" + projectId + "/version?" + q)));
            if (a != null)
                foreach (object o in a)
                {
                    Dictionary<string, object> d = o as Dictionary<string, object>;
                    if (d != null) res.Add(d);
                }
        }
        catch { }
        if (res.Count == 0)
        {
            // запасной путь: берём всё и фильтруем сами
            try
            {
                ArrayList a = AsList(jser.DeserializeObject(Raw(ApiBase + "project/" + projectId + "/version")));
                if (a == null) return res;
                foreach (object o in a)
                {
                    Dictionary<string, object> d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    bool gok = string.IsNullOrEmpty(mcVer) || StrList(d, "game_versions").Contains(mcVer);
                    bool lok = string.IsNullOrEmpty(loader) || loader == "any" || StrList(d, "loaders").Contains(loader);
                    if (gok && lok) res.Add(d);
                }
            }
            catch { }
        }
        return res;
    }

    public static Dictionary<string, object> VersionById(string versionId)
    {
        try { return jser.DeserializeObject(Raw(ApiBase + "version/" + versionId)) as Dictionary<string, object>; }
        catch { return null; }
    }

    // Самая свежая стабильная версия под нашу MC + загрузчик
    public static Dictionary<string, object> BestVersion(string projectId, string mcVer, string loader)
    {
        List<Dictionary<string, object>> vs = Versions(projectId, mcVer, loader);
        if (vs.Count == 0) return null;
        Dictionary<string, object> best = null;
        int bestScore = int.MinValue;
        foreach (Dictionary<string, object> v in vs)
        {
            int sc = 0;
            string vt = Str(v, "version_type", "release");
            if (vt == "release") sc += 100;
            else if (vt == "beta") sc += 50;
            else sc += 10;
            if (PrimaryFile(v) != null) sc += 20;
            object fo;
            if (v.TryGetValue("featured", out fo) && fo != null)
            {
                try { if (Convert.ToBoolean(fo)) sc += 30; } catch { }
            }
            string date = Str(v, "date_published", "");
            if (!string.IsNullOrEmpty(date))
            {
                try
                {
                    DateTime d = DateTime.Parse(date, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
                    int days = (int)(DateTime.UtcNow - d).TotalDays;
                    if (days < 0) days = 0;
                    sc -= Math.Min(20, days / 30);
                }
                catch { }
            }
            if (sc > bestScore) { bestScore = sc; best = v; }
        }
        return best;
    }

    // ---- Скачивание с проверкой sha1 ----
    public static string Download(string url, string dest, string sha1)
    {
        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
        Prep(req);
        using (WebResponse resp = req.GetResponse())
        {
            Stream src = resp.GetResponseStream();
            using (FileStream fs = new FileStream(dest, FileMode.Create, FileAccess.Write))
            {
                byte[] buf = new byte[65536];
                int n;
                while ((n = src.Read(buf, 0, buf.Length)) > 0) fs.Write(buf, 0, n);
            }
        }
        if (!string.IsNullOrEmpty(sha1))
        {
            string got;
            using (SHA1 h = SHA1.Create())
            using (FileStream fs = File.OpenRead(dest))
                got = BitConverter.ToString(h.ComputeHash(fs)).Replace("-", "").ToLower();
            if (!string.Equals(got, sha1.ToLower(), StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(dest); } catch { }
                throw new Exception("файл не прошёл проверку целостности (sha1)");
            }
        }
        return dest;
    }

    // ---- Учёт установленных модов (индекс в папке игры) ----
    public static string IndexPath(string modsDir) { return Path.Combine(modsDir, ".luxe-mods.json"); }

    public static Dictionary<string, string> LoadIndex(string modsDir)
    {
        Dictionary<string, string> d = new Dictionary<string, string>();
        try
        {
            string p = IndexPath(modsDir);
            if (!File.Exists(p)) return d;
            Dictionary<string, object> j = jser.Deserialize<Dictionary<string, object>>(File.ReadAllText(p, Encoding.UTF8));
            if (j == null) return d;
            foreach (KeyValuePair<string, object> kv in j)
                if (kv.Value != null) d[kv.Key] = kv.Value.ToString();
        }
        catch { }
        return d;
    }

    public static void SaveIndex(string modsDir, Dictionary<string, string> d)
    {
        try
        {
            Directory.CreateDirectory(modsDir);
            Dictionary<string, object> o = new Dictionary<string, object>();
            foreach (KeyValuePair<string, string> kv in d) o[kv.Key] = kv.Value;
            File.WriteAllText(IndexPath(modsDir), jser.Serialize(o), Encoding.UTF8);
        }
        catch { }
    }

    // Выключенный мод: jar переименован, чтобы не оканчивался на .jar
    // (Fabric/Quilt/Forge/NeoForge такие файлы просто не видят)
    public const string OffSuffix = ".luxeoff";

    public static bool IsDisabled(string fileName)
    {
        return !string.IsNullOrEmpty(fileName)
            && fileName.EndsWith(OffSuffix, StringComparison.OrdinalIgnoreCase);
    }

    public static string StripOff(string fileName)
    {
        if (IsDisabled(fileName)) return fileName.Substring(0, fileName.Length - OffSuffix.Length);
        return fileName;
    }

    // ---- Включение / выключение мода ----
    public static bool SetEnabled(string projectId, string modsDir, Dictionary<string, string> index, bool enable)
    {
        if (index == null) return false;
        string fn;
        if (!index.TryGetValue(projectId, out fn) || string.IsNullOrEmpty(fn)) return false;
        bool currentlyOn = !IsDisabled(fn);
        if (currentlyOn == enable) return true;
        string from = Path.Combine(modsDir, fn);
        string to = Path.Combine(modsDir, enable ? StripOff(fn) : fn + OffSuffix);
        try
        {
            if (!File.Exists(from)) return false;
            if (File.Exists(to)) File.Delete(to);
            File.Move(from, to);
        }
        catch { return false; }
        index[projectId] = Path.GetFileName(to);
        SaveIndex(modsDir, index);
        return true;
    }

    // Мод уже стоит? Сверяемся с индексом, иначе ищем jar по имени/slug
    public static bool IsInstalled(string modsDir, Dictionary<string, string> index, ModInfo m)
    {
        string fn;
        if (index.TryGetValue(m.id, out fn) && !string.IsNullOrEmpty(fn))
        {
            try
            {
                if (File.Exists(Path.Combine(modsDir, fn)))
                {
                    m.fileName = fn;
                    m.disabled = IsDisabled(fn);
                    return true;
                }
            }
            catch { }
        }
        try
        {
            if (!Directory.Exists(modsDir)) return false;
            string slug = (m.slug ?? "").ToLower();
            string title = Ui.Short((m.title ?? "").ToLower(), 20);
            // сначала включённые .jar, затем выключенные
            foreach (string pat in new string[] { "*.jar", "*" + OffSuffix })
            {
                foreach (string f in Directory.GetFiles(modsDir, pat))
                {
                    string nf = Path.GetFileName(f).ToLower();
                    if (nf.StartsWith(slug) || (title.Length > 3 && nf.StartsWith(title)))
                    {
                        m.fileName = Path.GetFileName(f);
                        m.disabled = IsDisabled(m.fileName);
                        index[m.id] = m.fileName;
                        return true;
                    }
                }
            }
        }
        catch { }
        return false;
    }

    // Сколько модов стоит в сборке и сколько из них включено
    public static int CountInstalled(string modsDir, out int enabled)
    {
        enabled = 0;
        int total = 0;
        try
        {
            if (!Directory.Exists(modsDir)) return 0;
            foreach (string f in Directory.GetFiles(modsDir, "*.jar")) { total++; enabled++; }
            foreach (string f in Directory.GetFiles(modsDir, "*" + OffSuffix)) { total++; }
        }
        catch { }
        return total;
    }

    // ---- Установка мода вместе со всеми обязательными зависимостями ----
    public static ModJob InstallMod(string projectId, string mcVer, string loader, string modsDir, Action<string> status)
    {
        ModJob job = new ModJob();
        job.mcVer = mcVer;
        job.loader = loader;
        job.modsDir = modsDir;
        job.status = status;
        Directory.CreateDirectory(modsDir);
        job.index = LoadIndex(modsDir);
        try
        {
            Install(projectId, job, true);
            SaveIndex(modsDir, job.index);
        }
        catch
        {
            SaveIndex(modsDir, job.index);
            throw;
        }
        return job;
    }

    static void Install(string projectId, ModJob job, bool root)
    {
        if (string.IsNullOrEmpty(projectId)) return;
        if (job.done.Contains(projectId)) return;
        job.done.Add(projectId);

        Dictionary<string, object> ver = BestVersion(projectId, job.mcVer, job.loader);
        if (ver == null) throw new Exception(projectId + ": нет версии под " + job.mcVer + " (" + job.loader + ")");
        string vname = Str(ver, "name", projectId);
        if (job.status != null) job.status((root ? "Мод: " : "Зависимость: ") + Ui.Short(vname, 60));

        // сначала обязательные зависимости, потом сам мод
        object depO;
        ArrayList deps = ver.TryGetValue("dependencies", out depO) ? AsList(depO) : null;
        if (deps != null)
        {
            foreach (object x in deps)
            {
                Dictionary<string, object> dp = x as Dictionary<string, object>;
                if (dp == null) continue;
                // ставим только required: optional/incompatible/embedded пропускаем
                if (Str(dp, "dependency_type", "") != "required") continue;
                string pid = Str(dp, "project_id", null);
                string vid = Str(dp, "version_id", null);
                try
                {
                    if (!string.IsNullOrEmpty(vid))
                    {
                        Dictionary<string, object> dv = VersionById(vid);
                        if (dv != null) Put(dv, job);
                    }
                    else if (!string.IsNullOrEmpty(pid) && !job.done.Contains(pid))
                    {
                        Install(pid, job, false);
                    }
                }
                catch (Exception dex)
                {
                    // зависимость ставим «по возможности»: битый мод лучше, чем полный отказ
                    string bad = !string.IsNullOrEmpty(pid) ? pid : vid;
                    if (!job.skipped.Contains(bad)) job.skipped.Add(bad);
                    if (job.status != null) job.status("  ! " + bad + ": " + dex.Message);
                }
            }
        }
        Put(ver, job);
    }

    static void Put(Dictionary<string, object> ver, ModJob job)
    {
        Dictionary<string, object> file = PrimaryFile(ver);
        if (file == null) return;
        string fname = Str(file, "filename", "");
        string furl = Str(file, "url", "");
        if (string.IsNullOrEmpty(fname) || string.IsNullOrEmpty(furl)) return;
        if (!fname.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) return;
        string dest = Path.Combine(job.modsDir, fname);
        // мод мог быть выключен — убираем его выключенную копию, ставим свежий
        try { string offOld = dest + OffSuffix; if (File.Exists(offOld)) File.Delete(offOld); } catch { }
        string sha1 = "";
        object ho;
        if (file.TryGetValue("hashes", out ho))
        {
            Dictionary<string, object> hh = ho as Dictionary<string, object>;
            if (hh != null) sha1 = Str(hh, "sha1", "");
        }
        bool have = false;
        if (File.Exists(dest))
        {
            if (string.IsNullOrEmpty(sha1)) have = true;
            else
            {
                try
                {
                    using (SHA1 h = SHA1.Create())
                    using (FileStream fs = File.OpenRead(dest))
                        have = BitConverter.ToString(h.ComputeHash(fs)).Replace("-", "").ToLower() == sha1.ToLower();
                }
                catch { }
            }
            if (!have) { try { File.Delete(dest); } catch { } }
        }
        string pid = Str(ver, "project_id", "");
        if (have)
        {
            // файл уже на месте и совпадает — не качаем повторно
            if (!string.IsNullOrEmpty(pid)) job.index[pid] = fname;
            if (job.status != null) job.status("  = " + fname + " уже установлен");
            return;
        }
        if (job.status != null) job.status("  ↓ " + fname);
        Download(furl, dest, sha1);
        job.installed++;
        if (!string.IsNullOrEmpty(pid)) job.index[pid] = fname;
    }

    // ---- Удаление ----
    public static bool Uninstall(string projectId, string modsDir, Dictionary<string, string> index)
    {
        string fn;
        if (index == null || !index.TryGetValue(projectId, out fn) || string.IsNullOrEmpty(fn)) return false;
        bool ok = false;
        // удаляем обе возможные копии: включённую и выключенную
        foreach (string name in new string[] { fn, StripOff(fn), fn + OffSuffix })
        {
            try
            {
                string p = Path.Combine(modsDir, name);
                if (File.Exists(p)) { File.Delete(p); ok = true; }
            }
            catch { }
        }
        index.Remove(projectId);
        SaveIndex(modsDir, index);
        return ok;
    }
}

// Общий «тик» для плавных переходов: один таймер на всё приложение (~60 FPS)
static class Anim
{
    static System.Windows.Forms.Timer t;
    static List<Action> subs = new List<Action>();

    public static void Sub(Action a)
    {
        if (a == null) return;
        Ensure();
        if (!subs.Contains(a)) subs.Add(a);
    }

    public static void Unsub(Action a)
    {
        subs.Remove(a);
    }

    static void Ensure()
    {
        if (t == null)
        {
            t = new System.Windows.Forms.Timer();
            t.Interval = 16;
            t.Tick += delegate
            {
                if (subs.Count == 0) { t.Stop(); return; }
                Action[] copy = subs.ToArray();
                for (int i = 0; i < copy.Length; i++)
                {
                    try { copy[i](); } catch { }
                }
            };
        }
        if (!t.Enabled) t.Start();
    }

    // Плавно довести значение до цели за шаг
    public static float Step(float cur, float target, float step)
    {
        if (cur < target) { cur += step; if (cur > target) cur = target; }
        else if (cur > target) { cur -= step; if (cur < target) cur = target; }
        return cur;
    }

    public static bool Near(float a, float b) { return Math.Abs(a - b) < 0.004f; }
}

static partial class Ui
{
    [System.Runtime.InteropServices.DllImport("gdi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, out uint pnumFonts);

    // Шрифт вшит в exe и регистрируется в процессе при старте: внешних
    // файлов лаунчеру не нужно, остаётся переносимым. Inter (SIL OFL 1.1).
    // Через AddFontMemResourceEx, а не PrivateFontCollection: с TextRenderer
    // (GDI) шрифты из PrivateFontCollection рисуются с артефактами.
    static bool fontsReady = false;
    static bool fontOk = false;
    // на Linux встроенные шрифты регистрируются здесь (GDI нет)
    static PrivateFontCollection pfc;
    static Dictionary<string, Font> fontCache = new Dictionary<string, Font>();

    static bool LoadRes(string resName)
    {
        try
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resName))
            {
                if (s == null) return false;
                byte[] data = new byte[s.Length];
                int got = 0;
                while (got < data.Length)
                {
                    int r = s.Read(data, got, data.Length - got);
                    if (r <= 0) break;
                    got += r;
                }
                if (got == 0) return false;
                IntPtr ptr = IntPtr.Zero;
                try
                {
                    ptr = Marshal.AllocCoTaskMem(data.Length);
                    Marshal.Copy(data, 0, ptr, data.Length);
                    if (!Os.IsWin)
                    {
                        // Linux: AddFontMemResourceEx — это GDI, которого нет.
                        // Отдаём байты в PrivateFontCollection и потом берём
                        // семейство оттуда через FindFamily().
                        if (pfc == null) pfc = new PrivateFontCollection();
                        pfc.AddMemoryFont(ptr, data.Length);
                        return true;
                    }
                    uint add = 0;
                    return AddFontMemResourceEx(ptr, (uint)data.Length, IntPtr.Zero, out add) != 0;
                }
                finally { if (ptr != IntPtr.Zero) Marshal.FreeCoTaskMem(ptr); }
            }
        }
        catch { return false; }
    }

    static void EnsureFonts()
    {
        if (fontsReady) return;
        fontsReady = true;
        try
        {
            bool a = LoadRes("Inter-Regular.ttf");
            bool b = LoadRes("Inter-SemiBold.ttf");
            bool c = LoadRes("Inter-Bold.ttf");
            LoadRes("Inter-Medium.ttf");
            // проверяем, что GDI really видит семейство
            try
            {
                if (!Os.IsWin)
                {
                    fontOk = (pfc != null && FindFamily("Inter") != null);
                }
                else
                {
                    using (Font probe = new Font("Inter", 10f, FontStyle.Regular, GraphicsUnit.Point))
                        fontOk = probe.Name == "Inter";
                }
            }
            catch { fontOk = false; }
        }
        catch { fontOk = false; }
    }

    public static bool HasNiceFont { get { EnsureFonts(); return fontOk; } }

    // Семейство из PrivateFontCollection по имени (Linux-путь шрифтов)
    static FontFamily FindFamily(string name)
    {
        try
        {
            if (pfc == null) return null;
            foreach (FontFamily f in pfc.Families)
                if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) return f;
        }
        catch { }
        return null;
    }

    // Шрифт по семейству, а не по имени: на Linux «Inter» и «Segoe UI»
    // по строке не резолвятся, зато FontFamily из коллекции — резолвится.
    static Font MakeFont(FontFamily fam, float size, FontStyle style)
    {
        if (fam != null)
        {
            try { return new Font(fam, size, style, GraphicsUnit.Point); }
            catch { }
        }
        try { return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point); }
        catch { }
        return new Font("Sans", size, style, GraphicsUnit.Point);
    }

    // Шрифт из вшитого Inter. Экземпляры кэшируются: старый код создавал
    // new Font(...) на каждый контрол и не освобождал — утечка GDI.
    public static Font Font(float size)
    {
        return Font(size, FontStyle.Regular);
    }

    public static Font Font(float size, FontStyle style)
    {
        EnsureFonts();
        string key = size.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + (int)style;
        Font f;
        if (fontCache.TryGetValue(key, out f) && f != null) return f;
        try
        {
            bool bold = (style & FontStyle.Bold) != 0;
            FontStyle rest = style & ~(FontStyle.Bold);
            if (!Os.IsWin)
            {
                FontFamily fam = fontOk ? FindFamily("Inter") : null;
                f = MakeFont(fam, size, style);
            }
            else if (!fontOk)
                f = new Font("Segoe UI", size, style, GraphicsUnit.Point);
            else if (bold)
                f = new Font("Inter", size, rest, GraphicsUnit.Point);
            else
                f = new Font("Inter", size, rest, GraphicsUnit.Point);
        }
        catch
        {
            if (!Os.IsWin) f = MakeFont(null, size, style);
            else f = new Font("Segoe UI", size, style, GraphicsUnit.Point);
        }
        fontCache[key] = f;
        return f;
    }

    // Среднее начертание — для кнопок и подзаголовков
    public static Font FontMed(float size)
    {
        EnsureFonts();
        string key = "med|" + size.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Font f;
        if (fontCache.TryGetValue(key, out f) && f != null) return f;
        try
        {
            if (fontOk) f = new Font("Inter", size, FontStyle.Regular, GraphicsUnit.Point);
            else f = Font(size, FontStyle.Regular);
        }
        catch { f = new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Point); }
        fontCache[key] = f;
        return f;
    }

    // Тёмная тема: глубокие нейтральные подложки + один акцентный
    // градиент (индиго -> фиолетовый). Цвета разделены по «высоте»:
    // чем ближе элемент к пользователю, тем он светлее.
    // Палитра под референс: глубокая сине-фиолетовая ночь + акцент
    // «индиго -> синий». Подложки почти чёрные, карточки чуть светлее,
    // рамки с фиолетовым подтоном — от этого весь экран выглядит собранным.
    public static readonly Color Bg = Color.FromArgb(5, 2, 15);
    public static readonly Color Side = Color.FromArgb(8, 4, 20);
    public static readonly Color SideSel = Color.FromArgb(40, 20, 80);
    public static readonly Color Card = Color.FromArgb(12, 8, 28);
    public static readonly Color CardHover = Color.FromArgb(20, 14, 40);
    public static readonly Color Border = Color.FromArgb(60, 30, 120);
    public static readonly Color BorderSoft = Color.FromArgb(40, 20, 80);
    public static readonly Color Txt = Color.FromArgb(240, 242, 250);
    public static readonly Color Gray = Color.FromArgb(160, 166, 192);
    // GrayDim раньше был 105,111,140 — контраст 3.78:1 на карточке,
    // ниже порога WCAG AA для мелкого текста. Поднят до 4.9:1:
    // подписи, даты и статусы читаются без напряжения.
    public static readonly Color GrayDim = Color.FromArgb(128, 135, 164);
    public static readonly Color Accent1 = Color.FromArgb(0, 255, 200);
    public static readonly Color Accent2 = Color.FromArgb(180, 0, 255);
    public static readonly Color AccentDark = Color.FromArgb(0, 120, 100);
    public static readonly Color Orange = Color.FromArgb(247, 168, 92);
    public static readonly Color Green = Color.FromArgb(53, 208, 127);
    public static readonly Color Gold = Color.FromArgb(250, 204, 110);
    // новые оттенки темы
    public static readonly Color Red = Color.FromArgb(248, 113, 113);
    public static readonly Color Cyan = Color.FromArgb(56, 200, 240);
    public static readonly Color Surface = Color.FromArgb(10, 6, 24);
    public static readonly Color SurfaceHi = Color.FromArgb(18, 12, 36);
    public static readonly Color Line = Color.FromArgb(35, 20, 60);

    public static Color Hsb(float h, float s, float br)
    {
        float c = br * s;
        float x = c * (1f - Math.Abs((h / 60f) % 2f - 1f));
        float m = br - c;
        float r = 0;
        float gg = 0;
        float b = 0;
        if (h < 60) { r = c; gg = x; }
        else if (h < 120) { r = x; gg = c; }
        else if (h < 180) { gg = c; b = x; }
        else if (h < 240) { gg = x; b = c; }
        else if (h < 300) { r = x; b = c; }
        else { r = c; b = x; }
        return Color.FromArgb(
            (int)Math.Round((r + m) * 255f),
            (int)Math.Round((gg + m) * 255f),
            (int)Math.Round((b + m) * 255f));
    }

    // Инициалы для аватарки мода: "Sodium Extra" -> "SE"
    // Бренд-оттенок: любой «сырой» hue сжимается в индиго-фиолетово-
    // синий коридор 232..300. Плитки модов/версий раньше считались как
    // сумма кодов символов и выходили зелёными, жёлтыми и красными —
    // на тёмной индиго-теме это выглядело как случайный набор стикеров.
    public static float BrandHue(float raw)
    {
        float a = Math.Abs(raw) % 69f;
        return 232f + a;
    }

    // Тот же бренд-оттенок, но с сохранением исходного различия:
    // близкие названия получают близкие цвета, далёкие — заметно разные.
    public static float BrandHue(string seed)
    {
        if (string.IsNullOrEmpty(seed)) return 268f;
        int h = 11;
        foreach (char c in seed) h = (h * 31 + c) % 1000;
        return BrandHue(h);
    }

    public static string Initials(string title)
    {
        if (string.IsNullOrEmpty(title)) return "?";
        string[] parts = title.Trim().Split(new char[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1)
        {
            string w = parts[0];
            return (w.Length >= 2 ? w.Substring(0, 2) : w).ToUpper();
        }
        return (parts[0].Substring(0, 1) + parts[1].Substring(0, 1)).ToUpper();
    }

    public static string Short(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace('\n', ' ').Replace('\r', ' ').Trim();
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        if (s.Length <= max) return s;
        return s.Substring(0, Math.Max(0, max - 1)).TrimEnd() + "…";
    }

    // Обрезка по ФАКТИЧЕСКОЙ высоте блока: укорачиваем по словам, пока
    // текст не уложится в заданный прямоугольник, и ставим «…» в конце
    // последней строки. Ui.Short режет по числу символов и обрывает
    // слова посередине («which greatly» без многоточия).
    public static string Clamp(string s, Font f, int width, int height)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        s = s.Replace('\n', ' ').Replace('\r', ' ').Trim();
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        TextFormatFlags fl = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
        Size full = TextRenderer.MeasureText(s, f, new Size(width, int.MaxValue), fl);
        if (full.Height <= height) return s;
        string[] words = s.Split(' ');
        string best = words.Length > 0 ? words[0] : s;
        for (int i = 2; i <= words.Length; i++)
        {
            string cand = string.Join(" ", words, 0, i) + "…";
            Size m = TextRenderer.MeasureText(cand, f, new Size(width, int.MaxValue), fl);
            if (m.Height > height) break;
            best = string.Join(" ", words, 0, i);
        }
        return best + "…";
    }

    public static Color Lerp(Color a, Color b, float t)
    {
        return Color.FromArgb(
            (int)(a.A + (b.A - a.A) * t),
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    // Чем закрасить прозрачные углы скруглённого контрола: цветом
    // родителя, если он залит ровным цветом. Возвращает Empty, если
    // родитель рисует картинку/градиент — там заливка всё испортит,
    // и углы остаются прозрачными (родитель их и так нарисует).
    public static Color CornerFill(Control c)
    {
        try
        {
            Control p = c == null ? null : c.Parent;
            if (p == null) return Color.Empty;
            // родитель с собственным артом: баннер, обложки, аватары
            RoundedPanel rp = p as RoundedPanel;
            if (rp != null && (rp.Gradient || rp.ContentImage != null || rp.Deco != null))
                return Color.Empty;
            if (p.GetType().Name == "HeroPanel") return Color.Empty;
            Color b = p.BackColor;
            if (b.A < 250) return Color.Empty;
            return b;
        }
        catch { return Color.Empty; }
    }

    // Мягкая тень под карточкой (дешёвый вариант — несколько смещённых контуров)
    public static void SoftShadow(Graphics g, Rectangle r, int radius, int alpha)
    {
        for (int i = 3; i >= 1; i--)
        {
            using (GraphicsPath p = RoundRect(new Rectangle(r.X - i, r.Y - i + 2, r.Width + i * 2, r.Height + i * 2), radius + i))
            using (Pen pen = new Pen(Color.FromArgb(alpha / (i * 3), 0, 0, 0), 2))
                g.DrawPath(pen, p);
        }
    }

    public static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        GraphicsPath p = new GraphicsPath();
        // AddArc кидает исключение на нулевых/отрицательных размерах
        if (r.Width <= 0 || r.Height <= 0) return p;
        if (radius < 1) radius = 1;
        int d = radius * 2;
        if (d > r.Width) d = r.Width;
        if (d > r.Height) d = r.Height;
        if (d < 2) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void DarkCombo(ComboBox c)
    {
        c.DrawMode = DrawMode.OwnerDrawFixed;
        c.BackColor = Color.FromArgb(42, 47, 58);
        c.ForeColor = Txt;
        c.FlatStyle = FlatStyle.Flat;
        c.Font = Ui.Font(10);
        c.DrawItem += delegate(object s, DrawItemEventArgs e)
        {
            e.DrawBackground();
            bool sel = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color bg = sel ? Accent2 : Color.FromArgb(42, 47, 58);
            using (SolidBrush b = new SolidBrush(bg))
                e.Graphics.FillRectangle(b, e.Bounds);
            if (e.Index >= 0)
            {
                string t = ((ComboBox)s).Items[e.Index].ToString();
                TextRenderer.DrawText(e.Graphics, t, e.Font,
                    new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
                    sel ? Color.White : Txt,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            }
        };
    }

    static Color Sh(Color c, float f)
    {
        int r = (int)(c.R * f); if (r > 255) r = 255; if (r < 0) r = 0;
        int g = (int)(c.G * f); if (g > 255) g = 255; if (g < 0) g = 0;
        int b = (int)(c.B * f); if (b > 255) b = 255; if (b < 0) b = 0;
        return Color.FromArgb(c.A, r, g, b);
    }

    static void Px(Graphics g, int x, int y, int s, Color c)
    {
        using (SolidBrush br = new SolidBrush(c))
            g.FillRectangle(br, x, y, s, s);
    }

    // Аккуратное лицо: блик сверху, тень снизу, симметричные глаза, губы
    public static void DrawSteveFace(Graphics g, int x, int y, int s)
    {
        Color hair = Color.FromArgb(66, 42, 24);
        Color hairHi = Color.FromArgb(98, 66, 36);
        Color skin = Color.FromArgb(211, 155, 110);
        Color iris = Color.FromArgb(72, 62, 182);
        Color mouth = Color.FromArgb(148, 92, 72);
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                float f = 0.86f + 0.14f * (1f - Math.Abs(c - 3.5f) / 4.5f);
                if (r == 0) f += 0.08f;
                if (r == 7) f -= 0.10f;
                Color col;
                if (r < 2) col = ((r == 0 && c >= 2 && c <= 5)) ? hairHi : hair;
                else if (r == 2 && (c == 0 || c == 7)) col = hair;
                else col = skin;
                if (r == 4 && c == 1) col = Color.White;
                if (r == 4 && c == 2) col = iris;
                if (r == 4 && c == 5) col = iris;
                if (r == 4 && c == 6) col = Color.White;
                if (r == 5 && (c == 3 || c == 4)) col = Sh(skin, 0.88f);
                if (r == 6 && (c == 3 || c == 4)) col = mouth;
                Px(g, x + c * s, y + r * s, s, Sh(col, f));
            }
        }
    }

    public static void DrawSteveBody(Graphics g, int x, int y, int s)
    {
        DrawSteve(g, x, y, s);
    }

    // Объёмный Стив: 3D-голова (верх + бок), тело/руки/ноги с шейдингом
    public static void DrawSteve(Graphics g, int x, int y, int s)
    {
        int dx = Math.Max(2, (int)(s * 0.5));
        int dy = -Math.Max(2, (int)(s * 0.35));
        Color hair = Color.FromArgb(66, 42, 24);
        Color hairHi = Color.FromArgb(98, 66, 36);
        Color skin = Color.FromArgb(211, 155, 110);
        Color shirt = Color.FromArgb(0, 168, 168);
        Color pants = Color.FromArgb(58, 68, 170);
        Color shoe = Color.FromArgb(110, 110, 116);
        Color shoeD = Color.FromArgb(78, 78, 84);

        // мягкая тень под ногами
        using (SolidBrush sh = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
            g.FillEllipse(sh, x - 4 * s, y + 31 * s, 16 * s + dx, 2 * s);

        // лицо спереди
        DrawSteveFace(g, x, y, s);

        // верх головы (волосы, полосками)
        for (int c = 0; c < 8; c++)
        {
            Point[] q = new Point[] {
                new Point(x + c * s, y),
                new Point(x + (c + 1) * s, y),
                new Point(x + (c + 1) * s + dx, y + dy),
                new Point(x + c * s + dx, y + dy)
            };
            using (SolidBrush br = new SolidBrush((c % 2 == 0) ? hair : hairHi))
                g.FillPolygon(br, q);
        }

        // бок головы (темнее)
        Point[] side = new Point[] {
            new Point(x + 8 * s, y),
            new Point(x + 8 * s + dx, y + dy),
            new Point(x + 8 * s + dx, y + 8 * s + dy),
            new Point(x + 8 * s, y + 8 * s)
        };
        using (SolidBrush br = new SolidBrush(Sh(skin, 0.76f)))
            g.FillPolygon(br, side);
        Point[] sideHair = new Point[] {
            new Point(x + 8 * s, y),
            new Point(x + 8 * s + dx, y + dy),
            new Point(x + 8 * s + dx, y + 2 * s + dy),
            new Point(x + 8 * s, y + 2 * s)
        };
        using (SolidBrush br = new SolidBrush(Sh(hair, 0.8f)))
            g.FillPolygon(br, sideHair);

        // торс 8x12 с шейдингом, шеей и складками
        for (int r = 0; r < 12; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                float f = 0.84f + 0.16f * (1f - Math.Abs(c - 3.5f) / 4.5f);
                if (r == 0) f += 0.04f;
                if (r == 11) f -= 0.12f;
                Color col = shirt;
                if (r == 0 && (c == 3 || c == 4)) col = Sh(skin, 0.82f);
                if (r == 0 && (c < 3 || c > 4)) col = Sh(shirt, 0.72f);
                if ((c == 2 && r >= 5 && r <= 9) || (c == 5 && r >= 4 && r <= 8)) col = Sh(shirt, 0.88f);
                Px(g, x + c * s, y + (8 + r) * s, s, Sh(col, f));
            }
        }

        // руки 4x12: рукав + кожа + кисть
        for (int arm = 0; arm < 2; arm++)
        {
            int ax = (arm == 0) ? (x - 4 * s) : (x + 8 * s);
            for (int r = 0; r < 12; r++)
            {
                for (int c = 0; c < 4; c++)
                {
                    float f = (arm == 1) ? 0.9f : (0.86f + 0.14f * (1f - Math.Abs(c - 1.5f) / 2.5f));
                    Color col;
                    if (r < 3) col = shirt;
                    else if (r == 11) col = Sh(skin, 0.86f);
                    else col = skin;
                    Px(g, ax + c * s, y + (8 + r) * s, s, Sh(col, f));
                }
            }
        }
        // бок правой руки
        Point[] armSide = new Point[] {
            new Point(x + 12 * s, y + 8 * s),
            new Point(x + 12 * s + dx, y + 8 * s + dy),
            new Point(x + 12 * s + dx, y + 20 * s + dy),
            new Point(x + 12 * s, y + 20 * s)
        };
        using (SolidBrush br = new SolidBrush(Sh(skin, 0.74f)))
            g.FillPolygon(br, armSide);

        // ноги 4x12: джинсы + ботинки
        for (int leg = 0; leg < 2; leg++)
        {
            int lx = x + leg * 4 * s;
            for (int r = 0; r < 12; r++)
            {
                for (int c = 0; c < 4; c++)
                {
                    float f = 0.84f + 0.16f * (1f - Math.Abs((leg * 4 + c) - 3.5f) / 4.5f);
                    Color col;
                    if (r >= 10) col = (r == 11) ? shoeD : shoe;
                    else col = pants;
                    if (r == 9) col = Sh(pants, 0.85f);
                    Px(g, lx + c * s, y + (20 + r) * s, s, Sh(col, f));
                }
            }
        }
        // бок правой ноги
        Point[] legSide = new Point[] {
            new Point(x + 8 * s, y + 20 * s),
            new Point(x + 8 * s + dx, y + 20 * s + dy),
            new Point(x + 8 * s + dx, y + 32 * s + dy),
            new Point(x + 8 * s, y + 32 * s)
        };
        using (SolidBrush br = new SolidBrush(Sh(pants, 0.7f)))
            g.FillPolygon(br, legSide);
    }
}

// Скруглённая кнопка с градиентом, свечением и состояниями
// Скруглённая кнопка: градиент, мягкий блик, состояния и иконка.
// Иконка рисуется вектором (Ico), а не глифом шрифта.
class RoundedButton : Button
{
    public int Radius = 12;
    public Color Top = Ui.Accent1;
    public Color Bottom = Ui.Accent2;
    public Color HoverTop;
    public Color HoverBottom;
    public Color PressTop;
    public Color PressBottom;
    public Color BorderColor = Color.Empty;
    public int BorderW = 1;
    public bool Glow = false;
    public bool Flat = false;            // без верхнего блика (иконочные кнопки)
    public bool FitText = false;         // уменьшать шрифт, если текст не влезает
    public Color HoverFore = Color.Empty;
    public Ico Icon = Ico.None;
    public int IconSize = 18;
    public bool IconRight = false;
    public int IconGap = 8;

    bool hover = false;
    bool press = false;
    // фокус, пришедший с клавиатуры: рисуем рамку, чтобы Tab был виден
    bool focused = false;
    bool kbFocus = false;
    float hovC = 0f, prsC = 0f;
    Action animTick;
    const float Step = 0.16f;

    public RoundedButton()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.MouseOverBackColor = Color.Transparent;
        FlatAppearance.MouseDownBackColor = Color.Transparent;
        ForeColor = Color.White;
        Cursor = Cursors.Hand;
        // жирные надписи на кнопках — как в Millida
        Font = Ui.Font(9.5f, FontStyle.Bold);
        HoverTop = ControlPaint.Light(Top, 0.12f);
        HoverBottom = ControlPaint.Light(Bottom, 0.08f);
        PressTop = ControlPaint.Dark(Bottom, 0.1f);
        PressBottom = ControlPaint.Dark(Bottom, 0.2f);
        animTick = new Action(delegate
        {
            if (IsDisposed || !IsHandleCreated) { StopAnim(); return; }
            float nh = Anim.Step(hovC, hover ? 1f : 0f, Step);
            float np = Anim.Step(prsC, press ? 1f : 0f, Step);
            bool chg = !Anim.Near(nh, hovC) || !Anim.Near(np, prsC);
            hovC = nh; prsC = np;
            if (chg) Invalidate();
            if (Anim.Near(hovC, hover ? 1f : 0f) && Anim.Near(prsC, press ? 1f : 0f)) StopAnim();
        });
    }

    void StartAnim() { Anim.Sub(animTick); }

    void StopAnim() { Anim.Unsub(animTick); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) StopAnim();
        base.Dispose(disposing);
    }

    // Нейтральная кнопка (второстепенные действия)
    public static RoundedButton Dark(string text, int radius)
    {
        RoundedButton b = new RoundedButton();
        b.Text = text;
        b.Radius = radius;
        b.Top = Ui.SurfaceHi;
        b.Bottom = Ui.Surface;
        b.HoverTop = Ui.Lerp(Ui.SurfaceHi, Color.White, 0.10f);
        b.HoverBottom = Ui.Lerp(Ui.Surface, Color.White, 0.07f);
        b.PressTop = Ui.Lerp(Ui.Surface, Color.Black, 0.18f);
        b.PressBottom = Ui.Lerp(Ui.Surface, Color.Black, 0.26f);
        b.BorderColor = Ui.Border;
        b.BorderW = 1;
        b.ForeColor = Ui.Txt;
        return b;
    }

    // Кнопка-иконка: квадрат без текста
    public static RoundedButton IcoBtn(Ico ic, int size, int radius)
    {
        RoundedButton b = Dark("", radius);
        b.Icon = ic;
        b.IconSize = (int)(size * 0.56f);
        b.Flat = true;
        b.Size = new Size(size, size);
        b.ForeColor = Ui.Gray;
        return b;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; StartAnim(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; press = false; StartAnim(); base.OnMouseLeave(e); }
    protected override void OnMouseUp(MouseEventArgs e) { press = false; StartAnim(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { if (!Enabled) { prsC = 0f; hovC = 0f; Invalidate(); } base.OnEnabledChanged(e); }
    // Tab по интерфейсу должен быть виден: рамка фокуса рисуется только
    // для клавиатуры, чтобы клики мышью её не «подсвечивали»
    protected override void OnGotFocus(EventArgs e) { focused = true; kbFocus = true; Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { focused = false; kbFocus = false; Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnter(EventArgs e) { base.OnEnter(e); Invalidate(); }
    protected override void OnLeave(EventArgs e) { base.OnLeave(e); Invalidate(); }
    // любой клик означает, что дальше работает мышь — рамку убираем
    protected override void OnMouseDown(MouseEventArgs e)
    {
        kbFocus = false;
        press = true; StartAnim();
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        if (Width <= 0 || Height <= 0) return;

        // Углы скругления обязаны показывать фон родителя. WinForms
        // переиспользует буфер отрисовки для контролов ОДНОГО размера, и в
        // прозрачных углах оставались пиксели другой кнопки того же
        // размера: под тёмной «Снять плащ» (140x42) лежала акцентная
        // «Добавить» (140x42) — в углах загорались синие клинья.
        Color cornerFill = Ui.CornerFill(this);
        if (cornerFill != Color.Empty)
        {
            using (SolidBrush cb = new SolidBrush(cornerFill))
                g.FillRectangle(cb, ClientRectangle);
        }

        // Как в референсе: мягкая скруглённая кнопка с градиентом,
        // светлой кромкой сверху и лёгким свечением у акцентных.
        int rad = Math.Max(3, Math.Min(Radius, Height / 2));
        RectangleF body = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);

        Color t, b;
        if (!Enabled)
        {
            t = Ui.Lerp(Ui.SurfaceHi, Ui.Bg, 0.35f);
            b = Ui.Lerp(Ui.Surface, Ui.Bg, 0.45f);
        }
        else if (prsC > 0.001f)
        {
            t = Ui.Lerp(Ui.Lerp(Top, HoverTop, hovC), PressTop, prsC);
            b = Ui.Lerp(Ui.Lerp(Bottom, HoverBottom, hovC), PressBottom, prsC);
        }
        else
        {
            t = Ui.Lerp(Top, HoverTop, hovC);
            b = Ui.Lerp(Bottom, HoverBottom, hovC);
        }

        // свечение у акцентной кнопки: клип по пути обязателен — без него
        // смещённый на 3px вниз FillPath вылезает синими треугольниками
        // в углах, где скругление не закрывает прямоугольник кнопки
        if (Glow && Enabled)
        {
            using (GraphicsPath gp = Ui.RoundRect(body, rad))
            {
                g.SetClip(gp);
                using (SolidBrush gb = new SolidBrush(Color.FromArgb(56, Ui.Accent1)))
                {
                    GraphicsState gs = g.Save();
                    g.TranslateTransform(0, 3f);
                    g.FillPath(gb, gp);
                    g.Restore(gs);
                }
                g.ResetClip();
            }
        }

        using (GraphicsPath p = Ui.RoundRect(body, rad))
        {
            using (LinearGradientBrush br = new LinearGradientBrush(body, t, b, 90f))
                g.FillPath(br, p);

            if (!Flat)
            {
                RectangleF hi = new RectangleF(body.X, body.Y, body.Width, body.Height * 0.54f);
                using (GraphicsPath hp = Ui.RoundRect(hi, Math.Max(2, rad - 1)))
                using (LinearGradientBrush hb = new LinearGradientBrush(
                    new RectangleF(body.X, body.Y, body.Width, Math.Max(2f, body.Height * 0.54f)),
                    Color.FromArgb(46, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                    g.FillPath(hb, hp);
            }

            if (BorderW > 0 && BorderColor != Color.Empty)
            {
                using (Pen pen = new Pen(Enabled ? BorderColor : Ui.BorderSoft, BorderW))
                {
                    pen.Alignment = PenAlignment.Inset;
                    g.DrawPath(pen, p);
                }
            }
            else if (Enabled)
            {
                // лёгкая кромка: раньше 34 — на акцентном градиенте
                // выглядело как выбеленный ореол
                Ui.GlassBorder(g, body, rad, 22, 10);
            }
        }

        // ---------- содержимое: иконка + текст ----------
        Color txt = Enabled ? ForeColor : Ui.Lerp(ForeColor, Ui.Bg, 0.55f);
        if (HoverFore != Color.Empty && hovC > 0.001f) txt = Ui.Lerp(txt, HoverFore, hovC);
        Color icc = txt;

        int bx = (int)Math.Round(body.X), by = (int)Math.Round(body.Y);
        int bw = (int)Math.Round(body.Width), bh = (int)Math.Round(body.Height);

        Font fo = Font;
        int iconW = (Icon != Ico.None) ? IconSize : 0;
        int gap = (Icon != Ico.None && !string.IsNullOrEmpty(Text)) ? IconGap : 0;
        int pad = 6;
        int avail = bw - pad * 2 - iconW - gap;

        if (FitText && !string.IsNullOrEmpty(Text))
        {
            if (TextRenderer.MeasureText(Text, fo).Width > avail)
            {
                for (float s2 = fo.Size - 0.5f; s2 >= 6f; s2 -= 0.5f)
                {
                    Font tf = Ui.Font(s2, fo.Style);
                    if (TextRenderer.MeasureText(Text, tf).Width <= avail) { fo = tf; break; }
                }
            }
        }

        Size tsz = string.IsNullOrEmpty(Text) ? Size.Empty : TextRenderer.MeasureText(Text, fo);
        int total = iconW + gap + tsz.Width;
        int sx = bx + (bw - total) / 2;
        if (sx < bx + pad) sx = bx + pad;
        // если содержимое шире кнопки, прижимаем его к левому краю:
        // иначе текст уезжает вправо и обрезается многоточием
        if (sx + total > bx + bw - pad) sx = Math.Max(bx + pad, bx + bw - pad - total);

        if (Icon != Ico.None)
        {
            Rectangle ib;
            if (string.IsNullOrEmpty(Text))
                ib = new Rectangle(bx + (bw - IconSize) / 2, by + (bh - IconSize) / 2, IconSize, IconSize);
            else if (IconRight)
                ib = new Rectangle(sx + tsz.Width + gap, by + (bh - IconSize) / 2, IconSize, IconSize);
            else
                ib = new Rectangle(sx, by + (bh - IconSize) / 2, IconSize, IconSize);
            Icons.Draw(g, Icon, ib, icc, 1.7f);
        }

        if (!string.IsNullOrEmpty(Text))
        {
            int tx = Icon == Ico.None ? bx : (IconRight ? sx : sx + iconW + gap);
            int tw = Icon == Ico.None ? bw : Math.Max(10, tsz.Width);
            Rectangle tr = new Rectangle(tx, by, tw, bh);
            TextFormatFlags fl = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            if (Icon == Ico.None && !IconRight) tr = new Rectangle(bx + 4, by, bw - 8, bh);
            TextRenderer.DrawText(g, Text, fo, new Rectangle(tr.X + 1, tr.Y + 1, tr.Width, tr.Height),
                Color.FromArgb(110, 0, 0, 0), fl);
            TextRenderer.DrawText(g, Text, fo, tr, txt, fl);
        }

        // ---------- фокус с клавиатуры ----------
        // Только когда фокус пришёл не от мыши: рамка идёт по контуру
        // кнопки, внутри свечения, поэтому не спорит с градиентом.
        if (kbFocus && Enabled && Focused)
        {
            using (GraphicsPath fp = Ui.RoundRect(
                new RectangleF(1.5f, 1.5f, Width - 3f, Height - 3f), Math.Max(3, rad - 1)))
            using (Pen pen = new Pen(Color.FromArgb(210, 255, 255, 255), 1.4f))
            {
                pen.Alignment = PenAlignment.Inset;
                g.DrawPath(pen, fp);
            }
        }
    }
}

// Надпись в стиле Millida: жирный текст и смещённая на пиксель тёмная
// копия под ним. Обычный Label так не умеет, поэтому рисуем сами.
class PixelLabel : Label
{
    public int ShadowShift = 1;
    public int ShadowAlpha = 150;
    // Плотный межстрочный интервал для крупных заголовков: GDI по
    // умолчанию добавляет к строке почти половину кегля, из-за чего
    // трёхстрочный заголовок в баннере разъезжается. 0 — как обычно.
    public int LineH = 0;

    public PixelLabel()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width <= 0 || Height <= 0) return;
        Graphics g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        if (BackColor.A != 0)
            using (SolidBrush b = new SolidBrush(BackColor))
                g.FillRectangle(b, ClientRectangle);

        TextFormatFlags fl = TextFormatFlags.NoPadding;
        switch (TextAlign)
        {
            case ContentAlignment.TopCenter:
            case ContentAlignment.MiddleCenter:
            case ContentAlignment.BottomCenter:
                fl |= TextFormatFlags.HorizontalCenter; break;
            case ContentAlignment.TopRight:
            case ContentAlignment.MiddleRight:
            case ContentAlignment.BottomRight:
                fl |= TextFormatFlags.Right; break;
        }
        switch (TextAlign)
        {
            case ContentAlignment.MiddleLeft:
            case ContentAlignment.MiddleCenter:
            case ContentAlignment.MiddleRight:
                fl |= TextFormatFlags.VerticalCenter; break;
            case ContentAlignment.BottomLeft:
            case ContentAlignment.BottomCenter:
            case ContentAlignment.BottomRight:
                fl |= TextFormatFlags.Bottom; break;
        }
        if (!AutoSize) fl |= TextFormatFlags.WordBreak;
        if (AutoEllipsis) fl |= TextFormatFlags.EndEllipsis;

        Rectangle r = new Rectangle(0, 0, Width, Height);
        // Плотная отрисовка: каждая строка рисуется отдельно с шагом
        // LineH. WordBreak при этом не нужен — переносы уже в тексте.
        if (LineH > 0 && Text.IndexOf('\n') >= 0)
        {
            string[] parts = Text.Replace("\r", "").Split('\n');
            for (int i = 0; i < parts.Length; i++)
            {
                Rectangle lr = new Rectangle(0, i * LineH, Width, LineH + 4);
                if (ShadowAlpha > 0)
                    TextRenderer.DrawText(g, parts[i], Font,
                        new Rectangle(ShadowShift, lr.Y + ShadowShift, lr.Width, lr.Height),
                        Color.FromArgb(ShadowAlpha, 0, 0, 0), fl);
                TextRenderer.DrawText(g, parts[i], Font, lr, Enabled ? ForeColor : Ui.GrayDim, fl);
            }
            return;
        }

        if (!string.IsNullOrEmpty(Text))
            TextRenderer.DrawText(g, Text, Font,
                new Rectangle(ShadowShift, ShadowShift, Width, Height),
                Color.FromArgb(ShadowAlpha, 0, 0, 0), fl);
        TextRenderer.DrawText(g, Text, Font, r, Enabled ? ForeColor : Ui.GrayDim, fl);
    }

    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    protected override void OnFontChanged(EventArgs e) { Invalidate(); base.OnFontChanged(e); }
    protected override void OnForeColorChanged(EventArgs e) { Invalidate(); base.OnForeColorChanged(e); }
    protected override void OnResize(EventArgs e) { Invalidate(); base.OnResize(e); }
}

// Пункт меню слева: иконка-вектор, «пиксельная» плашка выделения
class NavButton : Button
{
    public bool Selected = false;
    public Ico Icon = Ico.None;
    public string Title = "";
    public bool HasArrow = false;
    public int Badge = 0;              // счётчик справа (друзья)
    bool hover = false;
    bool selState = false;
    float selC = 0f;
    float hovC = 0f;
    Action animTick;

    public NavButton()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        // Фон строго непрозрачный. С BackColor=Transparent WinForms при
        // перерисовке просит родителя напечатать себя ВМЕСТЕ с соседними
        // контролами, и на пунктах меню появлялись «призраки» карточки
        // профиля.
        BackColor = Ui.Side;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.MouseOverBackColor = Ui.Side;
        FlatAppearance.MouseDownBackColor = Ui.Side;
        Cursor = Cursors.Hand;
        Font = Ui.Font(10);
        Size = new Size(210, 42);
        animTick = new Action(delegate
        {
            if (IsDisposed || !IsHandleCreated) { StopAnim(); return; }
            float ns = Anim.Step(selC, selState ? 1f : 0f, 0.18f);
            float nh = Anim.Step(hovC, hover ? 1f : 0f, 0.18f);
            bool ch = !Anim.Near(ns, selC) || !Anim.Near(nh, hovC);
            selC = ns; hovC = nh;
            if (ch) Invalidate();
            if (Anim.Near(selC, selState ? 1f : 0f) && Anim.Near(hovC, hover ? 1f : 0f)) StopAnim();
        });
    }

    public void SetSelected(bool v)
    {
        if (Selected == v && Anim.Near(selC, v ? 1f : 0f)) return;
        Selected = v;
        selState = v;
        StartAnim();
        Invalidate();
    }

    // Тики идут через общий Anim-таймер: раньше тут был ещё и свой
    // таймер 16мс, и обработчик вызывался дважды за кадр.
    void StartAnim() { Anim.Sub(animTick); }

    void StopAnim() { Anim.Unsub(animTick); }

    protected override void OnMouseEnter(EventArgs e) { hover = true; StartAnim(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; StartAnim(); base.OnMouseLeave(e); }
    // Tab по меню должен быть виден: у пункта появляется светлая рамка
    bool kbFocus = false;
    protected override void OnGotFocus(EventArgs e) { kbFocus = true; Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { kbFocus = false; Invalidate(); base.OnLostFocus(e); }
    protected override void OnMouseDown(MouseEventArgs e) { kbFocus = false; Invalidate(); base.OnMouseDown(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.HighQuality;
        if (Width <= 0 || Height <= 0) return;

        // Заливаем свою область целиком: буфер отрисовки WinForms общий
        // для контролов одного размера, и незакрытые пиксели показывают
        // предыдущий кадр.
        using (SolidBrush bg = new SolidBrush(Ui.Side))
            g.FillRectangle(bg, ClientRectangle);

        // Пункт меню как в референсе: плоская скруглённая плашка,
        // выбранная — с акцентным градиентом и мягким свечением.
        float rad = 9f;
        RectangleF body = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);

        if (selC > 0.01f)
        {
            using (GraphicsPath sp = Ui.RoundRect(new RectangleF(body.X + 2, body.Y + 3, body.Width, body.Height), rad))
            using (SolidBrush sb = new SolidBrush(Color.FromArgb((int)(100 * selC), Ui.Accent1)))
                g.FillPath(sb, sp);
        }

        using (GraphicsPath p = Ui.RoundRect(body, rad))
        {
            if (selC > 0.01f)
            {
                // вертикальный градиент, верх чуть светлее — плашка
                // получается «объёмной», как в современных лаунчерах
                using (LinearGradientBrush b = new LinearGradientBrush(body,
                    Ui.Lerp(Ui.SurfaceHi, Ui.Lerp(Ui.Accent1, Color.White, 0.18f), selC),
                    Ui.Lerp(Ui.SurfaceHi, Ui.Accent2, selC), 90f))
                    g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb((int)(120 * selC), 255, 255, 255), 1f))
                {
                    pen.Alignment = PenAlignment.Inset;
                    g.DrawPath(pen, p);
                }

                // светящаяся полоска-индикатор слева: активный пункт
                // видно даже периферийным зрением
                using (GraphicsPath bar = Ui.RoundRect(
                           new RectangleF(body.X + 3.5f, body.Y + 8f, 3.5f, body.Height - 16f), 1.8f))
                using (SolidBrush barB = new SolidBrush(Color.FromArgb((int)(235 * selC), 236, 230, 255)))
                    g.FillPath(barB, bar);
            }
            else if (hovC > 0.01f)
            {
                using (SolidBrush b = new SolidBrush(Ui.Lerp(Ui.Side, Ui.Surface, hovC)))
                    g.FillPath(b, p);
            }
        }

        float act = Math.Max(selC, hovC);
        Color ic = Ui.Lerp(Ui.Gray, Color.White, act);
        Color tx = Ui.Lerp(Ui.Gray, Color.White, act);

        Rectangle ib = new Rectangle(14, (Height - 19) / 2, 19, 19);
        Icons.Draw(g, Icon, ib, ic, 1.7f);

        Font fo = Ui.Font(10, FontStyle.Bold);
        int right = 14;
        if (Badge > 0) right = 44;
        if (HasArrow) right += 16;
        Rectangle tr = new Rectangle(44, 0, Width - 44 - right, Height);
        TextRenderer.DrawText(g, Title, fo, new Rectangle(tr.X + 1, tr.Y + 1, tr.Width, tr.Height),
            Color.FromArgb(90, 0, 0, 0),
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, Title, fo, tr, tx,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

        if (Badge > 0)
        {
            string bs = Badge > 99 ? "99+" : Badge.ToString();
            Font bf = Ui.Font(7.5f, FontStyle.Bold);
            Size sz = TextRenderer.MeasureText(bs, bf);
            Rectangle br = new Rectangle(Width - 12 - sz.Width - 10, (Height - 18) / 2, sz.Width + 10, 18);
            using (GraphicsPath bp = Ui.RoundRect(br, 5f))
            using (SolidBrush bb = new SolidBrush(Ui.Accent1))
                g.FillPath(bb, bp);
            TextRenderer.DrawText(g, bs, bf, br, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        if (HasArrow)
        {
            Rectangle ab = new Rectangle(Width - 14 - 14, (Height - 14) / 2, 14, 14);
            Icons.Draw(g, Ico.ChevDown, ab, Ui.Lerp(Ui.GrayDim, Ui.Gray, act), 1.5f);
        }

        // рамка клавиатурного фокуса
        if (kbFocus && Focused)
        {
            using (GraphicsPath fp = Ui.RoundRect(new RectangleF(1.5f, 1.5f, Width - 3f, Height - 3f), rad))
            using (Pen pen = new Pen(Color.FromArgb(200, 255, 255, 255), 1.4f))
            {
                pen.Alignment = PenAlignment.Inset;
                g.DrawPath(pen, fp);
            }
        }
    }
}

// Скруглённая панель-карточка: градиент, декор, стеклянная кромка
class RoundedPanel : Panel
{
    public int Radius = 12;
    public Color Border = Ui.BorderSoft;
    public int BorderW = 1;
    public Color HoverBack = Color.Empty;
    public Color HoverBorder = Color.Empty;
    public bool Animated = false;
    public Image ContentImage = null;
    public int ImageInset = 8;
    // новое: градиентная заливка и произвольный декор внутри скругления
    public bool Gradient = false;
    public Color GradFrom = Ui.Card;
    public Color GradTo = Ui.Bg;
    public float GradAngle = 118f;
    public Action<Graphics, Rectangle> Deco = null;
    public bool Glass = true;      // тонкая светлая кромка сверху
    public int Pad = 0;            // внутренний отступ карточки (для свечения)

    float hovC = 0f;
    Action animTick;
    GraphicsPath clip;
    int cw = -1, ch = -1, cr = -1;

    public RoundedPanel()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Ui.Card;
        animTick = new Action(delegate
        {
            if (IsDisposed || !IsHandleCreated) { StopAnim(); return; }
            float nh = Anim.Step(hovC, ClientRectangle.Contains(PointToClient(Cursor.Position)) ? 1f : 0f, 0.20f);
            bool chg = !Anim.Near(nh, hovC);
            hovC = nh;
            if (chg) Invalidate();
            if (Anim.Near(hovC, 0f) || Anim.Near(hovC, 1f)) StopAnim();
        });
    }

    // Тики идут через общий Anim-таймер: раньше тут был ещё и свой
    // таймер 16мс, и обработчик вызывался дважды за кадр.
    public void StartAnim() { Anim.Sub(animTick); }

    void StopAnim() { Anim.Unsub(animTick); }

    protected override void OnMouseEnter(EventArgs e)
    {
        if (Animated) StartAnim();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (Animated) StartAnim();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.HighQuality;
        if (Width <= 2 || Height <= 2) return;

        Rectangle r = new Rectangle(Pad, Pad, Width - Pad * 2, Height - Pad * 2);
        RectangleF rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, r.Height - 1f);

        if (clip == null || cw != r.Width || ch != r.Height || cr != Radius)
        {
            cw = r.Width; ch = r.Height; cr = Radius;
            if (clip != null) clip.Dispose();
            clip = Ui.RoundRect(rf, Radius);
            try { Region = new Region(clip); } catch { }
        }

        Color back = BackColor;
        Color bor = Border;
        if (Animated && HoverBack != Color.Empty)
        {
            back = Ui.Lerp(BackColor, HoverBack, hovC);
            if (HoverBorder != Color.Empty) bor = Ui.Lerp(Border, HoverBorder, hovC);
        }

        using (GraphicsPath p = Ui.RoundRect(rf, Radius))
        {
            if (Gradient)
            {
                Color g1 = GradFrom, g2 = GradTo;
                if (Animated && HoverBack != Color.Empty)
                {
                    g1 = Ui.Lerp(GradFrom, HoverBack, hovC);
                    g2 = Ui.Lerp(GradTo, Ui.Lerp(HoverBack, Ui.Bg, 0.5f), hovC);
                }
                using (LinearGradientBrush b = new LinearGradientBrush(rf, g1, g2, GradAngle))
                    g.FillPath(b, p);
            }
            else
            {
                using (SolidBrush b = new SolidBrush(back))
                    g.FillPath(b, p);
            }

            GraphicsState st = g.Save();
            g.SetClip(p);
            if (Deco != null)
            {
                try { Deco(g, r); }
                catch (Exception ex)
                {
                    // декор рисуется вручную, поэтому ошибку GDI+ здесь
                    // иначе не видно: пишем её в лог отладочного прогона
                    try
                    {
                        if (Cfg.Dbg)
                            File.AppendAllText(Path.Combine(Cfg.BaseDir, "deco_err.txt"),
                                DateTime.Now.ToString("HH:mm:ss") + " " + ex.GetType().Name + ": " + ex.Message
                                + " | " + ex.StackTrace + "\r\n", Encoding.UTF8);
                    }
                    catch { }
                }
            }
            if (ContentImage != null)
            {
                try
                {
                    int ins = ImageInset;
                    Rectangle box = new Rectangle(r.X + ins, r.Y + ins,
                        Math.Max(1, r.Width - ins * 2), Math.Max(1, r.Height - ins * 2));
                    float sc = Math.Max((float)box.Width / ContentImage.Width,
                                        (float)box.Height / ContentImage.Height);
                    int dw = (int)(ContentImage.Width * sc);
                    int dh = (int)(ContentImage.Height * sc);
                    Rectangle dr = new Rectangle(box.X + (box.Width - dw) / 2,
                                                 box.Y + (box.Height - dh) / 2, dw, dh);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(ContentImage, dr);
                }
                catch { }
            }
            g.Restore(st);

            if (BorderW > 0)
            {
                using (Pen pen = new Pen(bor, BorderW))
                    g.DrawPath(pen, p);
            }
            if (Glass) Ui.GlassBorder(g, rf, Radius, 22, 4);
        }
    }
}

// Панель-контейнер с плавной прокруткой содержимого.
// Схема: внутри лежит один «лист» (Inner) с детьми на абсолютных
// координатах; прокрутка — это Inner.Top = -offset. Обычный AutoScroll
// тут не годится: он резервирует место под системный скроллбар и при
// скрытом скроллбаре умеет сам «уехать» по горизонтали.
class ScrollPanel : Panel
{
    // лист содержимого; его высоту считаем полной (по самому нижнему ребёнку)
    public Control Inner;
    // выступ узкой полосы прокрутки: контент шире на эту величину,
    // чтобы полоса не оказывалась под кнопками
    public int BarGutter = 12;

    float scrollCur, scrollTarget;
    Action tick = null;
    bool animating;

    public ScrollPanel()
    {
        BackColor = Ui.Bg;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        DoubleBuffered = true;
    }

    int ViewH() { return ClientSize.Height; }

    public int MaxScrollY()
    {
        if (Inner == null) return 0;
        int max = Inner.Height - ViewH();
        return max > 0 ? max : 0;
    }

    // Позиция текущей прокрутки (для раскладки и отрисовки полосы)
    public float Offset { get { return scrollCur; } }

    public void ResetScroll()
    {
        StopAnim();
        scrollCur = 0; scrollTarget = 0;
        ApplyScroll();
    }

    void StopAnim()
    {
        try { if (tick != null) Anim.Unsub(tick); } catch { }
        animating = false;
    }

    void EnsureAnim()
    {
        if (animating) return;
        animating = true;
        if (tick == null)
            tick = delegate
            {
                try
                {
                    if (IsDisposed || !IsHandleCreated) { StopAnim(); return; }
                    float d = scrollTarget - scrollCur;
                    // РЎС‚РѕРї: С€Р°Рі РЅРёРєРѕРіРґР° РЅРµ Р±РѕР»СЊС€Рµ РѕСЃС‚Р°С‚РєР°, РїРѕСЌС‚РѕРјСѓ С†РµР»СЊ
                    // РґРѕСЃС‚РёРіР°РµС‚СЃСЏ С‚РѕС‡РЅРѕ Рё Р°РЅРёРјР°С†РёСЏ РЅРµ Р±РѕР»С‚Р°РµС‚СЃСЏ РІРѕРєСЂСѓРі РЅРµС‘.
                    if (Math.Abs(d) < 0.35f) { scrollCur = scrollTarget; ApplyScroll(); StopAnim(); return; }
                    float step = d * 0.32f;
                    if (step > 28f) step = 28f;
                    if (step < -28f) step = -28f;
                    if (step > 0f && step < 0.9f) step = 0.9f;
                    if (step < 0f && step > -0.9f) step = -0.9f;
                    scrollCur += step;
                    ApplyScroll();
                }
                catch { StopAnim(); }
            };
        Anim.Sub(tick);
    }
void ApplyScroll()
    {
        try
        {
            if (Inner == null) return;
            int max = MaxScrollY();
            int y = (int)Math.Round(scrollCur);
            if (y < 0) y = 0;
            if (y > max) y = max;
            Inner.Top = -y;
            Invalidate();
        }
        catch { }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        try
        {
            if (InvokeRequired) { base.OnMouseWheel(e); return; }
            // ход колеса мельче системного: прокрутка не «прыгает»
            scrollTarget += -e.Delta / 4.5f;
            if (scrollTarget < 0) scrollTarget = 0;
            float max = MaxScrollY();
            if (scrollTarget > max) scrollTarget = max;
            EnsureAnim();
            HandledMouseEventArgs hm = e as HandledMouseEventArgs;
            if (hm != null) hm.Handled = true;
        }
        catch { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) StopAnim();
        base.Dispose(disposing);
    }

    // Узкая полоса прокрутки: рисуется в свободной полосе справа от листа
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        try
        {
            if (Inner == null) return;
            int view = ViewH();
            int need = Inner.Height;
            if (need <= view || view <= 20) return;

            int barH = Math.Max(30, (int)(view * (float)view / need));
            int max = need - view;
            int top = (int)(-Inner.Top * (view - barH) / (float)max);
            if (top < 0) top = 0;
            if (top + barH > view) top = view - barH;

            int x = ClientSize.Width - BarGutter / 2 - 1;
            using (GraphicsPath p = Ui.RoundRect(new RectangleF(x, top, 3, barH), 1.5f))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(105, 255, 255, 255)))
                e.Graphics.FillPath(b, p);
        }
        catch { }
    }
}

// Панель-список со своим тонким скроллбаром вместо белого системного
class DarkFlowPanel : FlowLayoutPanel
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern int ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);
    const int SB_VERT = 1;

    // Плавная прокрутка: колесо копило дельту и ехало к цели по кадрам
    float scrollCur, scrollTarget;
    Action tick = null;
    bool animating;
    bool resetPending;
    // «Защёлка верха»: пока пользователь не крутил колесо, любой
    // самопроизвольный уход списка вниз возвращается назад. Одного
    // resetPending мало: FlowLayoutPanel умеет уехать позже — после
    // первого показа, ресайза окна или подмены ClientSize. Из-за
    // этого список новостей открывался с обрезанной первой карточкой.
    bool lockTop;
    // прокручивать ли список к контролу, который получил фокус
    // (нужно только выпадающим спискам, обычным — нет)
    public bool FocusScroll;
    // Дошли до низа списка (осталось меньше ~320px) — можно дозагрузить
    // следующую порцию карточек. Вызывается только когда пользователь
    // реально крутил колесо, а не при сбросе в начало.
    public Action NearBottom;

    // ---- кастомная полоса прокрутки ----
    // Раньше это была рисуемая линия 3px: её нельзя было ни схватить,
    // ни навести, и она дёргалась вместе с анимацией. Теперь у полосы
    // есть состояние (наведение/перетаскивание), а положение считается
    // от инвариантного ContentHeight.
    bool barHover, barDrag;
    int dragGrabY;
    int dragStartScroll;
    const int BarW = 6;      // видимая ширина бегунка
    const int BarHit = 14;   // зона захвата мышью (шире, чем видно)

    Rectangle BarTrack()
    {
        return new Rectangle(Math.Max(0, ClientSize.Width - BarHit), 0, BarHit, ClientSize.Height);
    }

    // Прокрутка к позиции без анимации (перетаскивание полосы)
    void ScrollTo(int y)
    {
        int max = MaxScrollY();
        if (y < 0) y = 0;
        if (y > max) y = max;
        // Снимаем защёлку верха: иначе OnLayout тут же вернёт позицию
        // в 0 (она защищает первую карточку от гонки при пересоздании).
        lockTop = false;
        scrollCur = scrollTarget = y;
        ApplyScroll();
    }

    // --- отладка скролла (--shot-run scrollto:N) ---
    public int DebugMaxScroll() { return MaxScrollY(); }
    public int DebugScrollPos() { return -AutoScrollPosition.Y; }
    public void DebugScrollTo(int y) { ScrollTo(y); }

    // Текущая позиция и мягкое восстановление: списки пересобираются
    // при догрузке картинок, и ResetScroll возвращал пользователя в
    // начало — прокрутка «отскакивала» наверх сама по себе.
    public int ScrollY { get { return -AutoScrollPosition.Y; } }
    public void RestoreScroll(int y)
    {
        try
        {
            if (y <= 0) return;
            ScrollTo(y);
        }
        catch { }
    }

    // Геометрия бегунка; false — прокручивать нечего
    bool BarRect(out Rectangle thumb, out int max, out int view)
    {
        thumb = Rectangle.Empty;
        max = MaxScrollY();
        view = ClientSize.Height;
        if (max <= 0 || view <= 20) return false;
        int need = max + view;
        int h = Math.Max(36, (int)(view * (float)view / need));
        if (h > view) h = view;
        int top = (int)Math.Round(-AutoScrollPosition.Y * (view - h) / (float)max);
        if (top < 0) top = 0;
        if (top + h > view) top = view - h;
        thumb = new Rectangle(Math.Max(0, ClientSize.Width - BarW - 2), top, BarW, h);
        return true;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        try
        {
            Rectangle thumb; int max, view;
            if (!BarRect(out thumb, out max, out view)) return;
            Rectangle hit = new Rectangle(thumb.X - (BarHit - BarW - 2), thumb.Y, BarHit, thumb.Height);
            if (hit.Contains(e.Location))
            {
                barDrag = true;
                dragGrabY = e.Y - thumb.Y;
                dragStartScroll = -AutoScrollPosition.Y;
                Capture = true;
                Invalidate(BarTrack());
            }
            else if (BarTrack().Contains(e.Location))
            {
                // клик по дорожке — прыжок к этой точке
                int span = Math.Max(1, view - thumb.Height);
                int y = (int)((e.Y - thumb.Height / 2f) * (float)max / span);
                ScrollTo(y);
                Invalidate(BarTrack());
            }
        }
        catch { }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        try
        {
            Rectangle thumb; int max, view;
            bool over = BarRect(out thumb, out max, out view) && BarTrack().Contains(e.Location);
            if (over != barHover)
            {
                barHover = over;
                Cursor = over ? Cursors.Hand : Cursors.Default;
                Invalidate(BarTrack());
            }
            if (barDrag && max > 0)
            {
                // позиция считается от точки захвата — бегунок не «убегает»
                int span = Math.Max(1, view - thumb.Height);
                int y = dragStartScroll + (int)((e.Y - dragGrabY) * (float)max / span);
                ScrollTo(y);
                Invalidate(BarTrack());
            }
        }
        catch { }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (barDrag)
        {
            barDrag = false;
            Capture = false;
            Invalidate(BarTrack());
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (!barDrag && barHover)
        {
            barHover = false;
            Cursor = Cursors.Default;
            Invalidate(BarTrack());
        }
    }

    public DarkFlowPanel()
    {
        AutoScroll = true;
        WrapContents = false;
        FlowDirection = FlowDirection.TopDown;
        BackColor = Ui.Bg;
        // FlowLayoutPanel без буфера заметно мерцает при прокрутке
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        DoubleBuffered = true;
    }

    public void ResetScroll()
    {
        try
        {
            StopAnim();
            scrollCur = 0; scrollTarget = 0;
            lockTop = true;
            // сброс сразу после добавления элементов не прилипает:
            // AutoScrollPosition применяется уже в следующем проходе
            // раскладки, поэтому помечаем и роняем после OnLayout
            resetPending = true;
            ApplyScroll();
            // и ещё раз в следующих проходах сообщений: после добавления
            // карточек FlowLayoutPanel успевает уехать вниз сам, а при
            // асинхронной подгрузке картинок — ещё и позже
            try
            {
                BeginInvoke(new Action(delegate
                {
                    try
                    {
                        resetPending = true;
                        AutoScrollPosition = Point.Empty;
                        HideNative();
                        // второй заход: к этому моменту размер содержимого
                        // уже посчитан, иначе прокрутка «отскакивает» назад
                        BeginInvoke(new Action(delegate
                        {
                            try
                            {
                                if (lockTop)
                                {
                                    AutoScrollPosition = Point.Empty;
                                    HideNative();
                                }
                            }
                            catch { }
                        }));
                    }
                    catch { }
                }));
            }
            catch { }
        }
        catch { }
    }

    void StopAnim()
    {
        try { if (tick != null) Anim.Unsub(tick); } catch { }
        animating = false;
    }

    void EnsureAnim()
    {
        if (animating) return;
        animating = true;
        if (tick == null)
            tick = delegate
            {
                try
                {
                    if (IsDisposed || !IsHandleCreated) { StopAnim(); return; }
                    float d = scrollTarget - scrollCur;
                    // остаток меньше полпикселя — доводим и гасим анимацию
                    if (Math.Abs(d) < 0.35f) { scrollCur = scrollTarget; ApplyScroll(); StopAnim(); return; }
                    // Ease-out: чем ближе к цели, тем меньше шаг.
                    // Степень "плавности" регулируется коэффициентом 0.32f.
                    float step = d * 0.32f;
                    if (step > 28f) step = 28f;
                    if (step < -28f) step = -28f;
                    // минимальный шаг, чтобы не зависать в бесконечном приближении
                    if (step > 0f && step < 0.9f) step = 0.9f;
                    if (step < 0f && step > -0.9f) step = -0.9f;
                    scrollCur += step;
                    ApplyScroll();
                }
                catch { StopAnim(); }
            };
        Anim.Sub(tick);
    }

    void ApplyScroll()
    {
        try
        {
            int max = MaxScrollY();
            int y = (int)Math.Round(scrollCur);
            if (y < 0) y = 0;
            if (y > max) y = max;
            AutoScrollPosition = new Point(0, y);
            HideNative();
            // полосу прокрутки рисуем поверх содержимого — без
            // перерисовки она остаётся на старой позиции
            Invalidate(new Rectangle(Math.Max(0, ClientSize.Width - 14), 0, 14, ClientSize.Height));
            if (NearBottom != null && max > 0 && scrollTarget > 2f && y >= max - 320)
                NearBottom();
        }
        catch { }
    }

    // Полная высота содержимого НЕ зависит от текущей прокрутки.
    // Раньше здесь суммировался c.Bottom детей, а они при скролле
    // сдвигаются вверх на AutoScrollPosition — максимум «уезжал»,
    // список упирался в случайное место и пружинил назад. Теперь
    // прибавляем текущее смещение: величина инвариантна.
    int ContentHeight()
    {
        try
        {
            int off = -AutoScrollPosition.Y;
            int need = 0;
            foreach (Control c in Controls)
            {
                if (!c.Visible) continue;
                int bottom = c.Bottom + c.Margin.Bottom;
                if (bottom > need) need = bottom;
            }
            return need + off;
        }
        catch { return 0; }
    }

    int MaxScrollY()
    {
        try
        {
            int max = ContentHeight() - ClientSize.Height;
            return max > 0 ? max : 0;
        }
        catch { return 0; }
    }

    void HideNative()
    {
        if (!Os.IsWin) return;
        try
        {
            if (!IsHandleCreated) return;
            // ТОЛЬКО вертикальный. Скрытие горизонтального (ShowScrollBar
            // SB_HORIZ=false) ломает отрисовку всего содержимого
            // FlowLayoutPanel — список становится пустым.
            ShowScrollBar(Handle, SB_VERT, false);
        }
        catch { }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        HideNative();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        HideNative();
        // содержимое изменилось — перерисовываем полосу прокрутки
        try { Invalidate(new Rectangle(Math.Max(0, ClientSize.Width - 14), 0, 14, ClientSize.Height)); } catch { }
        if (resetPending)
        {
            resetPending = false;
            scrollCur = 0; scrollTarget = 0;
            lockTop = true;
            try { AutoScrollPosition = Point.Empty; } catch { }
            HideNative();
        }
        // Пока пользователь не крутил колесо, список обязан начинаться
        // с первого элемента: FlowLayoutPanel после добавления карточек
        // умеет сам уехать вниз на полсотни пикселей.
        if (lockTop)
        {
            scrollCur = 0; scrollTarget = 0;
            try
            {
                // Полная принудительная защёлка: гонка добавления hwnd-
                // контролов (CapeView, кнопки карточек) успевает сдвинуть
                // AutoScrollPosition ПОСЛЕ ResetScroll и ПОСЛЕ этого
                // OnLayout. Сбрасываем явно при каждом проходе.
                if (AutoScrollPosition != Point.Empty) AutoScrollPosition = Point.Empty;
            }
            catch { }
        }
    }

    // Без этого FlowLayoutPanel прокручивает список к контролу, который
    // получил фокус (и список открывается с середины).
    protected override Point ScrollToControl(Control activeControl)
    {
        // Не только не скроллим к активному контролу — возвращаем ровно
        // верх списка. DisplayRectangle.Location вернул бы ТЕКУЩИЙ
        // сдвиг: если гонка при пересоздании карточек успела сдвинуть
        // панель, этот сдвиг закреплялся бы вместо отката к верху.
        if (!FocusScroll) return Point.Empty;
        return base.ScrollToControl(activeControl);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        try
        {
            if (InvokeRequired) { base.OnMouseWheel(e); return; }
            // пользователь взял управление — защёлку верха снимаем
            lockTop = false;
            lockTop = false;
            int max = MaxScrollY();
            if (max <= 0) return;
            float step = max > 400 ? 80f : 40f;
            scrollTarget += -e.Delta > 0 ? -step : step;
            if (scrollTarget < 0) scrollTarget = 0;
            if (scrollTarget > max) scrollTarget = max;
            ScrollTo((int)scrollTarget);
            HandledMouseEventArgs hm = e as HandledMouseEventArgs;
            if (hm != null) hm.Handled = true;
        }
        catch { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) StopAnim();
        base.Dispose(disposing);
    }

    // Системный скроллбар скрыт (он белый и выбивается из темы), но тогда
    // у списка не оставалось НИ ОДНОГО признака, что содержимое можно
    // прокрутить. Рисуем тонкую полосу у правого края + мягкое затухание
    // снизу: без него карточка обрезается посреди текста, и это читается
    // как дефект вёрстки, а не как продолжение списка.
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        try
        {
            int view = ClientSize.Height;
            int w = ClientSize.Width;

            // ---- кастомная полоса прокрутки ----
            Rectangle thumb; int max, vh;
            if (BarRect(out thumb, out max, out vh))
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // дорожка: почти невидимая, проявляется при наведении
                int trackA = (barHover || barDrag) ? 46 : 20;
                using (GraphicsPath tp = Ui.RoundRect(
                    new RectangleF(thumb.X, 4, thumb.Width, Math.Max(8, view - 8)), BarW / 2f))
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(trackA, Ui.Accent1)))
                    g.FillPath(tb, tp);

                // бегунок: пилюля; при наведении шире и светлее
                int grow = (barHover || barDrag) ? 1 : 0;
                RectangleF tr = new RectangleF(
                    thumb.X - grow, thumb.Y, thumb.Width + grow * 2f, thumb.Height);
                using (GraphicsPath bp = Ui.RoundRect(tr, (thumb.Width + grow * 2f) / 2f))
                {
                    Color c0 = barDrag ? Ui.Accent2
                             : barHover ? Ui.Accent1
                                        : Color.FromArgb(150, 148, 140, 200);
                    using (SolidBrush bb = new SolidBrush(c0))
                        g.FillPath(bb, bp);
                }
            }

            // затухание снизу: список продолжается, а не «режется»
            int fadeH = Math.Min(26, view / 4);
            if (fadeH > 4 && -AutoScrollPosition.Y < MaxScrollY() - 2)
            {
                using (LinearGradientBrush f = new LinearGradientBrush(
                    new Rectangle(0, view - fadeH, Math.Max(2, w), Math.Max(2, fadeH)),
                    Color.FromArgb(0, Ui.Bg), Color.FromArgb(210, Ui.Bg), 90f))
                    e.Graphics.FillRectangle(f, 0, view - fadeH, w, fadeH);
            }
        }
        catch { }
    }
}

// Свой красивый выпадающий список вместо стокового ComboBox
class Dropdown : Control
{
    public List<string> Items = new List<string>();
    int sel = -1;
    public int SelectedIndex
    {
        get { return sel; }
        set
        {
            if (value < -1) value = -1;
            if (value >= Items.Count) value = Items.Count - 1;
            if (sel != value)
            {
                sel = value;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }
    }
    public string SelectedItem
    {
        get { return (sel >= 0 && sel < Items.Count) ? Items[sel] : null; }
        set { SelectedIndex = Items.IndexOf(value); }
    }
    public event EventHandler SelectedIndexChanged;
    bool hover = false;
    bool press = false;
    float hovC = 0f;
    Action animTick;
    PopupList popup;
    DateTime lastClose = DateTime.MinValue;
    GraphicsPath clip;
    int cw = -1, ch = -1;
    // Плитка-иконка слева и подпись над текстом — как в референсе
    // у списка версий в баннере. Включаются точечно, не влияют на
    // остальные выпадающие списки.
    public Ico LeadIcon = Ico.None;
    public Color LeadTileFrom = Color.Empty;
    public Color LeadTileTo = Color.Empty;
    public string SubLabel = "";

    public Dropdown()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        ForeColor = Ui.Txt;
        Font = Ui.Font(9);
        Cursor = Cursors.Hand;
        Size = new Size(180, 32);
        animTick = new Action(delegate
        {
            if (IsDisposed || !IsHandleCreated) { StopAnim(); return; }
            float nh = Anim.Step(hovC, hover ? 1f : 0f, 0.25f);
            bool chg = !Anim.Near(nh, hovC);
            hovC = nh;
            if (chg) Invalidate();
            if (Anim.Near(hovC, hover ? 1f : 0f)) StopAnim();
        });
    }

    // Тики идут через общий Anim-таймер: раньше тут был ещё и свой
    // таймер 16мс, и обработчик вызывался дважды за кадр.
    void StartAnim() { Anim.Sub(animTick); }

    void StopAnim() { Anim.Unsub(animTick); }

    protected override void OnMouseEnter(EventArgs e) { hover = true; StartAnim(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; press = false; StartAnim(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { press = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { press = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        if (Width <= 2 || Height <= 2) return;

        RectangleF rf = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        if (clip == null || cw != Width || ch != Height)
        {
            cw = Width; ch = Height;
            if (clip != null) clip.Dispose();
            clip = Ui.RoundRect(rf, 6f);
            try { Region = new Region(clip); } catch { }
        }

        Color t = Ui.Lerp(Ui.SurfaceHi, Color.White, 0.06f * hovC);
        Color b = Ui.Lerp(Ui.Surface, Color.White, 0.03f * hovC);
        if (press) { t = Ui.Lerp(Ui.Surface, Color.Black, 0.18f); b = t; }

        using (GraphicsPath p = Ui.RoundRect(rf, 6f))
        {
            using (LinearGradientBrush br = new LinearGradientBrush(rf, t, b, 90f))
                g.FillPath(br, p);
            // 2px тёмная рамка — как у «пиксельных» полей Millida
            using (Pen pen = new Pen(Ui.Lerp(Ui.Border, Ui.Accent1, hovC * 0.8f), 2f))
            {
                pen.Alignment = PenAlignment.Inset;
                g.DrawPath(pen, p);
            }
        }

        Icons.Draw(g, Ico.ChevDown, new Rectangle(Width - 28, (Height - 14) / 2, 14, 14),
            Ui.Lerp(Ui.GrayDim, Ui.Txt, hovC), 1.6f);

        int tx = 12;
        // плитка-иконка слева от текста
        if (LeadIcon != Ico.None)
        {
            try
            {
                int ts = Height - 14;
                RectangleF tr = new RectangleF(10, (Height - ts) / 2f, ts, ts);
                using (GraphicsPath tp = Ui.RoundRect(tr, 5f))
                using (LinearGradientBrush tb = new LinearGradientBrush(tr,
                    LeadTileFrom == Color.Empty ? Ui.Green : LeadTileFrom,
                    LeadTileTo == Color.Empty ? Ui.Lerp(Ui.Green, Color.Black, 0.35f) : LeadTileTo,
                    90f))
                    g.FillPath(tb, tp);
                Icons.Draw(g, LeadIcon, new Rectangle((int)tr.X + 4, (int)tr.Y + 4, ts - 8, ts - 8), Color.White, 1.6f);
            }
            catch { }
            tx = 40;
        }

        string txt = SelectedItem ?? "";
        // у списка версий в закрытом состоянии показываем чистый номер
        // («26.3»), статусная приписка остаётся в раскрытом списке
        int bri = SubLabel.Length > 0 ? txt.IndexOf("  [") : -1;
        if (bri >= 0) txt = txt.Substring(0, bri);
        Rectangle trc = new Rectangle(tx, 0, Width - tx - 26, Height);
        if (SubLabel.Length > 0)
        {
            // две строки: мелкая серая подпись над основной версией
            TextRenderer.DrawText(g, SubLabel, Ui.Font(7), new Rectangle(tx, 4, trc.Width, 12),
                Ui.GrayDim, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
            using (Font bf = Ui.Font(9.5f, FontStyle.Bold))
                TextRenderer.DrawText(g, txt, bf, new Rectangle(tx, 15, trc.Width, Height - 17), ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
        else
        {
            TextRenderer.DrawText(g, txt, Font, trc, ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        TogglePopup();
    }

    void TogglePopup()
    {
        if (popup != null && !popup.IsDisposed) { try { popup.Close(); } catch { } popup = null; return; }
        if ((DateTime.Now - lastClose).TotalMilliseconds < 250) return;
        popup = new PopupList(this);
        popup.FormClosed += delegate { popup = null; lastClose = DateTime.Now; };
        popup.Show(this);
    }

    internal void Pick(int i)
    {
        SelectedIndex = i;
        if (popup != null) { try { popup.Close(); } catch { } popup = null; }
    }
}

class PopupList : Form
{
    Dropdown owner;
    DarkFlowPanel list;
    RoundedButton selBtn;
    const int Pad2 = 6;

    public PopupList(Dropdown owner)
    {
        this.owner = owner;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Ui.Surface;
        KeyPreview = true;
        DoubleBuffered = true;
        int w = owner.Width;
        if (w < 160) w = 160;
        int n = owner.Items.Count;
        int h = n * 32 + Pad2 * 2;
        int maxH = 10 * 32 + Pad2 * 2;
        if (h > maxH) h = maxH;
        if (h < 44) h = 44;
        Size = new Size(w, h);
        try { Region = new Region(Ui.RoundRect(new Rectangle(0, 0, w, h), 12)); } catch { }

        Point p = owner.PointToScreen(new Point(0, owner.Height + 6));
        Rectangle wa = Screen.FromControl(owner).WorkingArea;
        if (p.Y + h > wa.Bottom) p.Y = p.Y - h - owner.Height - 10;
        if (p.X + w > wa.Right) p.X = wa.Right - w;
        if (p.X < wa.Left) p.X = wa.Left;
        Location = p;

        list = new DarkFlowPanel();
        list.Location = new Point(Pad2, Pad2);
        list.Size = new Size(w - Pad2 * 2, h - Pad2 * 2);
        list.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        list.BackColor = Ui.Surface;
        // выпадающему списку нужна прокрутка к выбранному пункту,
        // обычным спискам — нет (иначе они открываются с середины)
        list.FocusScroll = true;
        Controls.Add(list);

        for (int i = 0; i < n; i++)
        {
            RoundedButton b = new RoundedButton();
            b.Text = owner.Items[i];
            b.Radius = 8;
            b.Flat = true;
            b.BorderW = 0;
            b.Font = Ui.Font(9.5f);
            bool isSel = (i == owner.SelectedIndex);
            b.ForeColor = isSel ? Color.White : Ui.Txt;
            if (isSel)
            {
                b.Top = Ui.Accent1;
                b.Bottom = Ui.Accent2;
                selBtn = b;
            }
            else
            {
                b.Top = Ui.Surface;
                b.Bottom = Ui.Surface;
                b.HoverTop = Ui.SurfaceHi;
                b.HoverBottom = Ui.SurfaceHi;
                b.PressTop = Ui.Lerp(Ui.Surface, Color.Black, 0.2f);
                b.PressBottom = b.PressTop;
            }
            b.Size = new Size(w - Pad2 * 2 - SystemInformation.VerticalScrollBarWidth, 30);
            b.Margin = new Padding(0, 0, 0, 2);
            int idx = i;
            b.Click += delegate { owner.Pick(idx); };
            list.Controls.Add(b);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.HighQuality;
        using (GraphicsPath p = Ui.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), 12f))
        using (Pen pen = new Pen(Ui.Border, 1f))
            e.Graphics.DrawPath(pen, p);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try { if (selBtn != null) list.ScrollControlIntoView(selBtn); } catch { }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        try
        {
            int cur = -list.AutoScrollPosition.Y;
            int y = cur - e.Delta;
            if (y < 0) y = 0;
            list.AutoScrollPosition = new Point(0, y);
        }
        catch { }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) { try { Close(); } catch { } }
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        try { Close(); } catch { }
    }
}

// Тонкий прогресс-бар: плавно догоняет значение, светится на конце
class ModernProgress : Control
{
    int v = 0;
    int m = 100;
    float shown = 0f;
    Action tick = null;
    bool on = false;

    public int Value
    {
        get { return v; }
        set { v = value; Ensure(); Invalidate(); }
    }
    public int Maximum
    {
        get { return m; }
        set { m = value; Ensure(); Invalidate(); }
    }

    public ModernProgress()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Height = 8;
    }

    void Ensure()
    {
        if (on) return;
        on = true;
        if (tick == null)
            tick = delegate
            {
                try
                {
                    if (IsDisposed || !IsHandleCreated) { Stop(); return; }
                    float want = m > 0 ? (float)v / m * 100f : 0f;
                    if (Anim.Near(shown, want)) { shown = want; Invalidate(); Stop(); return; }
                    shown = Anim.Step(shown, want, 2.6f);
                    Invalidate();
                }
                catch { Stop(); }
            };
        Anim.Sub(tick);
    }

    void Stop()
    {
        on = false;
        try { if (tick != null) Anim.Unsub(tick); } catch { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Stop();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.HighQuality;
        if (Width <= 2 || Height <= 0) return;
        float rad = Height / 2f;
        RectangleF rf = new RectangleF(0, 0.5f, Width - 1f, Height - 1f);
        using (GraphicsPath p = Ui.RoundRect(rf, rad))
        using (SolidBrush b = new SolidBrush(Color.FromArgb(70, 8, 10, 16)))
            g.FillPath(b, p);

        if (shown > 0.4f)
        {
            float w = Math.Max(Height, Width * shown / 100f);
            RectangleF f = new RectangleF(0, 0.5f, w, Height - 1f);
            using (GraphicsPath p = Ui.RoundRect(f, rad))
            using (LinearGradientBrush b = new LinearGradientBrush(f, Ui.Accent1, Ui.Accent2, 0f))
                g.FillPath(b, p);
            // светящийся кончик
            Ui.Glow2(g, new RectangleF(w - Height * 2.2f, -Height * 0.9f, Height * 4.4f, Height * 2.8f),
                Ui.Accent1, 120);
        }
    }
}

// Точка статуса с мягким свечением
class StatusDot : Control
{
    Color c = Ui.Green;
    public Color DotColor
    {
        get { return c; }
        set { c = value; Invalidate(); }
    }
    public StatusDot()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        BackColor = Color.Transparent;
        Size = new Size(12, 12);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.HighQuality;
        Ui.Glow2(g, new RectangleF(0, 0, Width, Height), c, 100);
        using (SolidBrush b = new SolidBrush(c))
            g.FillEllipse(b, Width / 2f - 3f, Height / 2f - 3f, 6f, 6f);
    }
}

// Логотип в сайдбаре — вектор, а не картинка
class LogoBadge : Control
{
    public LogoBadge()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        // непрозрачный фон: иначе WinForms печатает фон родителя и на
        // сайдбаре оседают «призраки» соседних контролов
        BackColor = Ui.Side;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        using (SolidBrush bg = new SolidBrush(Ui.Side))
            e.Graphics.FillRectangle(bg, ClientRectangle);
        Art.Logomark(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1));
    }
}

// Арт карточки версии
// Текстовая ссылка («Все версии →»). Раньше это была прозрачная кнопка:
// WinForms при перерисовке показывал под ней остатки предыдущего кадра,
// из-за чего надпись «двоилась». Здесь контрол сам заливает свой фон
// цветом родителя и рисует текст — артефактов нет.
class TextLink : Control
{
    public Color Fill = Ui.Bg;
    public Color HoverFill = Ui.Card;
    public Color TextCol = Ui.Accent1;
    public string Arrow = "→";
    bool hover = false;

    public TextLink()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Ui.Bg;
        Cursor = Cursors.Hand;
        Font = Ui.Font(8.5f, FontStyle.Bold);
        Size = new Size(120, 26);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using (SolidBrush b = new SolidBrush(hover ? HoverFill : Fill))
            g.FillRectangle(b, ClientRectangle);

        string full = string.IsNullOrEmpty(Arrow) ? Text : Text + "  " + Arrow;
        TextRenderer.DrawText(g, full, Font, ClientRectangle,
            hover ? Color.White : TextCol,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }
}

class CardHeader : Control
{
    public int Radius = 0;          // скругление верхних углов (по карточке)
    public string Big = "?";
    public string Tag = "";
    public float Hue = -1f;
    // Пользовательская обложка из папки covers; null — процедурный пейзаж
    public Image Img = null;

    // Кэш отрисовки: пейзаж/обложка рисуются один раз в bitmap, при
    // прокрутке списка готовая картинка просто блитится. Без кэша
    // каждый кадр скролла перерисовывал пейзаж заново — список лагал.
    Bitmap cache;
    int cw = -1, chh = -1;

    public CardHeader()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && cache != null) { cache.Dispose(); cache = null; }
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width <= 2 || Height <= 2) return;
        // Проверяем нужно ли обновить кэш (изменения размера или временные)
        bool needRefresh = false;
        if (cache == null) needRefresh = true;
        else if (cw != Width || chh != Height) needRefresh = true;
        
        if (needRefresh)
        {
            if (cache != null) { cache.Dispose(); cache = null; }
            cw = Width; chh = Height;
            try 
            { 
                // Создаём Bitmap с правильным разрешением для текущего DPI
                // Кэш ровно по размеру контрола. Раньше он создавался с
                // оговоркой на DPI и потом рисовался как DrawImage(cache, 0, 0)
                // в масштабе 1:1 — на экранах 125/150% обложка вылезала за
                // карточку и давала «полосы» из чужого арта.
                cache = new Bitmap(Math.Max(1, Width), Math.Max(1, Height));
            }
            catch { return; }
            using (Graphics g = Graphics.FromImage(cache))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Ui.Card);
                PaintCover(g);
            }
        }
        e.Graphics.DrawImageUnscaled(cache, 0, 0);
    }

    void PaintCover(Graphics g)
    {
        float h = Hue;
        if (h < 0f)
        {
            int hh = 7;
            foreach (char c in Big) hh = (hh * 31 + c) % 360;
            h = hh;
        }
        // Обложка карточки: пользовательская PNG или процедурный пейзаж.
        // Приоритет: PNG из covers → Minecraft версия → default → процедурный
        if (Radius > 0)
        {
            // верхние углы скругляем под карточку, иначе обложка
            // вылезает за её рамку квадратом
            using (GraphicsPath p = new GraphicsPath())
            {
                Rectangle rr = ClientRectangle;
                int rad = Radius;
                p.AddArc(rr.X, rr.Y, rad * 2, rad * 2, 180, 90);
                p.AddLine(rr.X + rad, rr.Y, rr.Right, rr.Y);
                p.AddLine(rr.Right, rr.Y, rr.Right, rr.Bottom);
                p.AddLine(rr.Right, rr.Bottom, rr.X, rr.Bottom);
                p.AddLine(rr.X, rr.Bottom, rr.X, rr.Y + rad);
                p.CloseFigure();
                GraphicsState st = g.Save();
                g.SetClip(p);
                if (Img != null) DrawImg(g, rr); else Art.CardCover(g, rr, Big, h, Tag);
                g.Restore(st);
            }
        }
        else if (Img != null) DrawImg(g, ClientRectangle);
        else Art.CardCover(g, ClientRectangle, Big, h, Tag);
    }

    // PNG во всю обложку: кроп по центру под размер карточки,
    // снизу притушаем — чтобы бейдж и текст читались
    void DrawImg(Graphics g, Rectangle r)
    {
        try
        {
            float ta = r.Width / (float)r.Height;
            int srcW, srcH;
            if (Img.Width / (float)Img.Height > ta) { srcH = Img.Height; srcW = (int)(Img.Height * ta + 0.5f); }
            else { srcW = Img.Width; srcH = (int)(Img.Width / ta + 0.5f); }
            Rectangle src = new Rectangle((Img.Width - srcW) / 2, (Img.Height - srcH) / 2, srcW, srcH);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(Img, r, src, GraphicsUnit.Pixel);

            // затемнение снизу под надпись версии
            int shadeH = Math.Max(18, r.Height * 2 / 5);
            using (LinearGradientBrush sh = new LinearGradientBrush(
                new Rectangle(r.X, r.Bottom - shadeH - 1, r.Width, shadeH + 2),
                Color.FromArgb(0, 4, 5, 12), Color.FromArgb(150, 4, 5, 12), 90f))
                g.FillRectangle(sh, r.X, r.Bottom - shadeH, r.Width, shadeH);

            if (!string.IsNullOrEmpty(Tag))
            {
                Font f = Ui.Font(7f, FontStyle.Bold);
                Size sz = TextRenderer.MeasureText(Tag, f);
                Rectangle pill = new Rectangle(r.X + 8, r.Y + 8, sz.Width + 14, sz.Height + 4);
                using (GraphicsPath p = Ui.RoundRect(new RectangleF(pill.X, pill.Y, pill.Width, pill.Height), pill.Height / 2f))
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(165, 6, 8, 16)))
                    g.FillPath(sb, p);
                TextRenderer.DrawText(g, Tag, f, pill, Color.FromArgb(232, 238, 255),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }
        catch
        {
            // битая картинка — не оставляем чёрную дыру
            Art.CardCover(g, r, Big, Hue < 0f ? 268f : Hue, Tag);
        }
    }
}

// Аватар мода: иконки Modrinth приходят в .webp, который .NET не умеет
// читать, поэтому рисуем градиентную плитку с инициалами
class ModAvatar : Panel
{
    public string Seed = "";
    public string Text2 = "?";
    public ModAvatar()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        Art.ModTile(e.Graphics, ClientRectangle, Seed, Text2);
    }
}

// Панель-раздел. Обычный Panel не рисует своих потомков в этом
// лаунчере: окно панели есть, дети видимы, z-order верный — а на
// экране пусто. Нужен UserPaint, как у RoundedPanel.
class BannerPanel : Panel
{
    float phase = 0f;
    public bool Animate = true;   // герой «дышит» только когда он на экране

    public BannerPanel()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Ui.Bg;
        System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
        t.Interval = 50;
        t.Tick += delegate
        {
            if (IsDisposed || !IsHandleCreated || !Animate) return;
            phase += 0.018f;
            Invalidate();
        };
        t.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Rectangle r = ClientRectangle;
        if (r.Width <= 2 || r.Height <= 2) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        RectangleF rf = new RectangleF(0.5f, 0.5f, r.Width - 1f, r.Height - 1f);
        int rad = 18;

        using (GraphicsPath p = Ui.RoundRect(rf, rad))
        {
            using (LinearGradientBrush bg = new LinearGradientBrush(rf,
                Color.FromArgb(26, 31, 42), Color.FromArgb(15, 18, 25), 62f))
                g.FillPath(bg, p);

            GraphicsState st = g.Save();
            g.SetClip(p);

            // атмосферные пятна света по углам — баннер «живёт» даже
            // там, где нет арта
            Ui.Glow2(g, new RectangleF(r.Right - r.Height * 1.9f, r.Y - r.Height * 0.55f,
                r.Height * 1.8f, r.Height * 1.5f), Color.FromArgb(120, 90, 255), 40);
            Ui.Glow2(g, new RectangleF(r.X - r.Height * 0.55f, r.Bottom - r.Height * 1.1f,
                r.Height * 1.6f, r.Height * 1.5f), Color.FromArgb(64, 110, 255), 34);

            Art.HeroArt(g, new Rectangle(r.Right - (int)(r.Width * 0.55f), r.Y, (int)(r.Width * 0.55f), r.Height), phase);
            g.Restore(st);

            Ui.GlassBorder(g, rf, rad, 34, 8);
        }
    }
}

class GridItem
{
    public string id;
    public bool inst;
    public string disp;
    public string mtype;
    public GridItem(string id, bool inst) { this.id = id; this.inst = inst; this.disp = null; this.mtype = null; }
    public GridItem(string id, bool inst, string disp) { this.id = id; this.inst = inst; this.disp = disp; this.mtype = null; }
    public GridItem(string id, bool inst, string disp, string mtype) { this.id = id; this.inst = inst; this.disp = disp; this.mtype = mtype; }
}

// Индикаторы слайдов баннера: четыре полупрозрачные пилюли, первая —
// активная, шире и ярче. Один контрол вместо четырёх квадратных Panel:
// скруглённые края и мягкая прозрачность, ничего не выбивается из арта.
class HeroDots : Control
{
    public HeroDots()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Size = new Size(46, 7);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int x = 0;
        for (int i = 0; i < 4; i++)
        {
            int w = i == 0 ? 16 : 5;
            using (GraphicsPath p = Ui.RoundRect(new RectangleF(x, 1, w, 5), 2.5f))
            using (SolidBrush b = new SolidBrush(i == 0
                ? Color.FromArgb(235, 245, 248, 255)
                : Color.FromArgb(90, 245, 248, 255)))
                g.FillPath(b, p);
            x += w + 5;
        }
    }
}

// Пользовательские обложки карточек версий. Кладёшь PNG в папку
// «Luxe Visuals\covers» рядом с лаунчером — и карточка берёт её
// вместо процедурного пейзажа. Имена файлов:
//   <версия>.png      — «26.3.png», «wonderful-1.21.8.png» (точный id)
//   <mc>.png          — «1.21.8.png» (общая для всех сборок на этой MC)
//   default.png       — на все остальные карточки
// Нет файла — рисуется обычный пейзаж, ничего не ломается.
static class Covers
{
    static Dictionary<string, Image> cache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
    static bool dirChecked = false;
    static string Dir { get { return Path.Combine(Cfg.DataDir, "covers"); } }

    public static Image For(string ver, string mcv)
    {
        try
        {
            EnsureDir();
            Image img = TryFile(ver);
            if (img == null && !string.IsNullOrEmpty(mcv) && !string.Equals(mcv, ver, StringComparison.OrdinalIgnoreCase))
                img = TryFile(mcv);
            if (img == null) img = TryFile("default");
            return img;
        }
        catch { return null; }
    }

    static Image TryFile(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        // в имени версии бывают символы, недопустимые для файла
        foreach (char c in Path.GetInvalidFileNameChars())
            if (name.IndexOf(c) >= 0) return null;
        string p = Path.Combine(Dir, name + ".png");
        if (!File.Exists(p)) return null;
        Image img;
        if (!cache.TryGetValue(p, out img) || img == null)
        {
            // Bitmap(Image) копирует пиксели: исходный поток можно
            // закрыть, картинка не завяжется на него
            using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(p)))
            using (Image src = Image.FromStream(ms))
                img = new Bitmap(src);
            cache[p] = img;
        }
        return img;
    }

    // Сброс кэша обложек: настройки → «Очистить кэш».
    public static void DropCache()
    {
        try
        {
            foreach (Image img in cache.Values) { try { if (img != null) img.Dispose(); } catch { } }
            cache.Clear();
            dirChecked = false;
        }
        catch { }
    }

    public static void EnsureDir()
    {
        if (dirChecked) return;
        dirChecked = true;
        try
        {
            if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
            string help = Path.Combine(Dir, "прочти меня.txt");
            if (!File.Exists(help))
                File.WriteAllText(help,
                    "Сюда клади PNG-обложки для карточек версий.\r\n\r\n" +
                    "Имя файла — версия:\r\n" +
                    "  26.3.png          — карточка версии 26.3\r\n" +
                    "  1.21.8.png        — все сборки на Minecraft 1.21.8\r\n" +
                    "  default.png       — все остальные карточки\r\n\r\n" +
                    "Размер любой: картинка обрежется по центру под размер обложки.\r\n" +
                    "Удалишь файл — вернётся обычный пейзаж.\r\n", Encoding.UTF8);
        }
        catch { dirChecked = false; }
    }
}

// !!! ПРИ ВЫПУСКЕ НОВОЙ ВЕРСИИ: добавь сюда сверху новый NewsItem (версия, заголовок, список изменений)
class NewsItem
{
    public string ver;
    public string title;
    public string[] lines;
}

static class NewsData
{
    public static readonly NewsItem[] All = new NewsItem[]
    {
        new NewsItem { ver = "2.2.4", title = "Ровное окно, прогресс загрузок и мод для 26.3", lines = new string[] {
            "Убраны белые рамки и полосы по краям окна при развороте, растягивании и возврате из свёрнутого",
            "Скачивание модов, Java и обновления показывает проценты и мегабайты в полосе прогресса",
            "Проверка обновлений пишет результат прямо на вкладке: «у вас уже установлена последняя версия»",
            "Мод скинов и плащей обновлён под Minecraft 26.3 — сборка 1.2.0" } },
        new NewsItem { ver = "2.2.3", title = "Luxe Visuals и обновление сборок", lines = new string[] {
            "Мод Luxe Visuals в сборке обновлён до свежей сборки — fabric-api не тронут",
            "Сборки теперь сами догоняют обновлённые моды при запуске: каталог сверяет размер файла",
            "Скачивание идёт во временный файл и подменяет мод только после успеха — битых jar больше не будет" } },
        new NewsItem { ver = "2.2.2", title = "Помощник ищет скины, плащи и сборки сам", lines = new string[] {
            "«Скин как у Dream» — помощник качает настоящую текстуру по нику и сразу её надевает",
            "«Популярный скин» — подборка известных ников, кнопка листает следующий",
            "«Плащ дракона», «огненный плащ» — подбирает тему в каталоге Luxe и надевает без входа в Microsoft",
            "«Поставь сборку better mc» — ищет на Modrinth, качает .mrpack и раскладывает моды сам",
            "Установленная сборка появляется во вкладке «Моды» с именем, версией игры и загрузчиком" } },
        new NewsItem { ver = "2.0.1", title = "Java 25 для Minecraft 26.x", lines = new string[] {
            "Minecraft 26.1, 26.2, 26.3 и новее требуют Java 25 — раньше запуск падал с LinkageError",
            "Лаунчер сам скачает Java 25 в отдельную папку, старые Java 8/17/21 останутся на месте",
            "Java выбирается под версию игры: 1.21.4 — 21, 1.20.1 — 17, 26.3 — 25" } },
        new NewsItem { ver = "2.0.0", title = "3D-редактор, скины без лицензии и плащи", lines = new string[] {
            "В разделе «Скин» появилась модель: та же текстура крутится прямо в лаунчере",
            "Рисовать можно и по модели: ЛКМ — кисть, ПКМ — повернуть, колесо — приблизить",
            "Пустой холст показывается полупрозрачным манекеном — понятно, где красить",
            "Скин без лицензии: офлайн-ник тоже сохраняет скин — его увидят в сборках лаунчера",
            "С лицензией скин уходит на аккаунт Microsoft, без неё — в Luxe (мод уже в лаунчере)",
            "В «Плащах» — каталог из интернета: 32 плаща и фильтры «Популярные», «Новые», «Мои»",
            "На карточках видно, сколько человек носят плащ, у свежих — значок «Новый»",
            "Всё скачанное (картинки, логи, аккаунт) теперь живёт только в папке «Luxe Visuals»",
            "Кнопки и надписи переделаны под стиль Millida: рамка, тень, аккуратные поля" } },
        new NewsItem { ver = "1.7.0", title = "Новый интерфейс", lines = new string[] {
            "Своя рамка окна: своя шапка с названием раздела и кнопками окна",
            "Сайдбар и верхняя панель переделаны, векторные иконки вместо символов",
            "Живой баннер с 3D-блоками, новые карточки версий, модов и новостей",
            "Плавная прокрутка всех списков и починка мелких визуальных багов",
            "Починено обновление: новый exe больше не терялся в папке данных" } },
        new NewsItem { ver = "1.6.0", title = "Моды отдельным разделом", lines = new string[] {
            "«Моды» переехали из ряда вкладок в меню слева — на место «Кооператив»",
            "Выбор сборки-приёмника и фильтр по загрузчику прямо в разделе",
            "Переключатель мода: включить/выключить без удаления",
            "Плавные переходы кнопок и пунктов меню, свой тонкий скроллбар" } },
        new NewsItem { ver = "1.5.0", title = "Вкладка «Моды» — каталог Modrinth", lines = new string[] {
            "Моды с Modrinth ищутся прямо в лаунчере по названию и описанию",
            "Выбираешь сборку (Fabric/Quilt/Forge/NeoForge) — моды кладутся в её папку mods",
            "Обязательные зависимости и библиотеки ставятся автоматически",
            "Установленные моды помечены, можно обновить или удалить" } },
        new NewsItem { ver = "1.4.0", title = "Раздел новостей", lines = new string[] {
            "Вкладка Новости: история всех версий лаунчера",
            "Новости добавляются с каждым новым релизом" } },
        new NewsItem { ver = "1.3.9", title = "Свои выпадающие списки", lines = new string[] {
            "Фильтр версий, выбор версии и памяти — теперь кастомные тёмные списки",
            "Выпадающий список: скруглённые пункты, скролл колесом, закрытие по Esc" } },
        new NewsItem { ver = "1.3.8", title = "Чиню пустую вкладку «Наши визуалы»", lines = new string[] {
            "Модпаки без своего jar теперь тоже считаются установленными и видны" } },
        new NewsItem { ver = "1.3.7", title = "Вкладка «Наши визуалы»", lines = new string[] {
            "Именованные модпаки: name.txt задаёт красивое имя, gamedir.txt — свою папку",
            "Первый модпак: Luxe Visuals (Fabric 1.21.4 + fabric-api + wonderful)" } },
        new NewsItem { ver = "1.3.6", title = "Модпаки со своей папкой игры", lines = new string[] {
            "Версия может жить в отдельной папке игры через файл gamedir.txt",
            "Миры, моды и настройки модпака не смешиваются с остальными" } },
        new NewsItem { ver = "1.3.5", title = "Закрытие при запуске", lines = new string[] {
            "Лаунчер сам закрывается после старта игры",
            "Галочка в настройках: «Не закрывать лаунчер при запуске игры»" } },
        new NewsItem { ver = "1.3.4", title = "Загрузчики и все типы версий", lines = new string[] {
            "Вкладки Fabric, Forge, Quilt, NeoForge + Релизы, Снапшоты, Старые версии",
            "Авто-Java под версию: 8 для старых, 17 и 21 для новых, всё портативно" } },
        new NewsItem { ver = "1.3.3", title = "Сетка версий как в Modrinth", lines = new string[] {
            "Карточки с артами, бейджами и кнопками вместо строк",
            "Живой поиск по версиям" } },
        new NewsItem { ver = "1.3.2", title = "Технический выпуск", lines = new string[] {
            "Стабильность и подготовка следующих обновлений" } },
        new NewsItem { ver = "1.3.1", title = "Надёжные закачки обновлений", lines = new string[] {
            "До 3 попыток скачать обновление при обрыве, битый файл удаляется" } },
        new NewsItem { ver = "1.3.0", title = "Красота и порядок", lines = new string[] {
            "Скруглённые кнопки с градиентом и свечением",
            "Тёмный скролл, чиню наезжающие надписи и синие уголки" } },
        new NewsItem { ver = "1.2.0", title = "Скорость и запуск", lines = new string[] {
            "Многопоточные закачки: библиотеки в 8 потоков, ассеты в 16, видна скорость",
            "Фикс запуска: убран демо-режим, чиню пробел в пути к папке" } },
        new NewsItem { ver = "1.1.0", title = "Автообновление", lines = new string[] {
            "Проверка версии при старте, карточка обновления, перезапуск",
            "Проверка вручную: Настройки — Проверить обновления" } },
        new NewsItem { ver = "1.0.0", title = "Первый выпуск Luxe Launcher", lines = new string[] {
            "Офлайн-вход по нику, портативная Java, тёмная тема в стиле JustLauncher",
            "Баннер с пещерой, объёмный Стив, логотип Luxe" } },
    };
}

partial class LauncherForm : Form
{
    Dropdown cmbVer;
    string ram = "2";
    Label lblStatus;
    StatusDot dot;
    ModernProgress bar;
    RoundedButton btnPlay;
    RoundedPanel profileCard;
    Label lblProfileNick;
    Label lblProfileSub;
    IconView profileCrown;
    NavButton navHome;
    NavButton navNews;
    NavButton navMods;
    NavButton navSkin;
    DarkFlowPanel newsList;
    // раздел «Моды» (в сайдбаре, вместо «Кооператив»)
    DarkFlowPanel modsList;
    TextBox modsSearch;
    Dropdown modsTargetDd;
    Dropdown modsLoaderDd;
    Label modsStatTxt;
    RoundedButton modsReload;
    Panel top;
    Panel homeBg;
    Label pageTitle;
    bool sectionsBuilt = false;
    Panel side;
    // Разделы: контролы лежат прямо на форме (как главный экран).
// Переключение — ТОЛЬКО порядок отрисовки. Visible у разделов не
// трогаем никогда: после Visible=false потомки перестают рисоваться
// навсегда (свойства и HWND верные, z-order верный — экран пустой).
    List<Control> newsUi = new List<Control>();
    List<Control> modsUi = new List<Control>();
    // SecX обязан совпадать с PX главной колонки (SideW+Marg=252): раньше
    // здесь было 254, и элементы главного экрана (чипы вкладок, карточки)
    // торчали из-под подложек разделов синими 2px столбиками слева.
    const int SecX = 252, SecY = 72;
    const string ModsHint = "Поиск модов по Modrinth...";
    Panel verListBox;
    DarkFlowPanel verList;
    // новая разметка
    RoundedButton winMaxBtn;
    PixelLabel lblHeroHi;
    Label lblHeroSub;
    RoundedPanel toast;
    Label toastTxt;
    System.Windows.Forms.Timer toastTimer;
    Panel dimPanel;
    RoundedPanel accCard;
    TextBox accNick;
    // вход в Microsoft (Auth.cs): карточка входа и строка лицензии на аккаунте
    RoundedPanel authCard;
    Label accLic;
    RoundedButton accLicBtn;
    Label authStep;
    Label authCode;
    string authUri = "";
    RoundedPanel setCard;
    Dropdown setRam;
    List<string> cacheRel = new List<string>();
    List<string> cacheInst = new List<string>();
    RoundedPanel updateCard;
    Label updateTitle;
    Label updateNotes;
    RoundedButton btnUpdateGo;
    RoundedButton btnUpdateLater;
    // вкладка «Обновления» в настройках: строка результата проверки,
    // чтобы «у вас уже последняя версия» было видно прямо на вкладке,
    // а не только в нижней строке статуса (она перекрыта карточкой)
    Label updState;
    RoundedButton updCheckBtn;
    string updUrl = "";
    string updVer = "";
    long updSize = 0;
    bool busyDl = false;
    List<RoundedButton> loaderTabs = new List<RoundedButton>();
    // ===== новая главная: правая колонка как в референсе =====
    Panel rightCol;
    RoundedPanel newsRightCard;
    RoundedPanel premiumCard;
    RoundedPanel quickCard;
    RoundedButton allVersionsBtn;
    RoundedButton topBell;
    RoundedButton topTheme;
    RoundedPanel topSearchWrap;
    TextBox topSearchBox;
    Label rightStatusLbl;
    StatusDot rightStatusDot;
    PixelLabel homeListHead;
    Panel popularBlock;
    TextLink homeAllVerLink;
    IconView homeClockIco;
    System.Windows.Forms.Timer homeLayoutTimer;
    int lastHomeW = -1;
    int lastHomeH = -1;
    HeroDots heroDots = null;
    // Подписи под карточками версий (как «Последняя версия» в референсе)
    List<Label> verSubLabels = new List<Label>();
    public int loaderTab = 0;
    volatile int verLoadGen = 0;   // поколение загрузки списка версий
    string typeFilter = "release";
    Dictionary<string, object> manifestCache;
    List<GridItem> gridItems = new List<GridItem>();
    // Дозагрузка ванильных версий: сколько релизов манифеста уже
    // показано и флаг «страница уже грузится» (событие NearBottom
    // стреляет на каждом кадре, пока список у нижнего края)
    int vanillaLoaded = 0;
    bool loadingMore = false;
    // ===== разделы лаунчера =====
    public const int ViewHome = 0;
    public const int ViewNews = 1;
    public const int ViewMods = 2;
    public const int ViewSkin = 3;
    int view = ViewHome;

    // раздел «Моды»
    List<ModInfo> modItems = new List<ModInfo>();
    System.Windows.Forms.Timer modSearchTimer;
    int modSearchGen = 0;
    string modsDir = "";
    string modsMcVer = "";
    string modsLoader = "";
    string modsTarget = "";
    bool modsBusy = false;
    bool modsSyncUi = false;
    HeroPanel banner;
    string javaExe;
    string nick;
    bool closeOnLaunch = true;
    Dictionary<string, string> cfg;

    public LauncherForm()
    {
        cfg = Mc.LoadConfig();
        nick = cfg["username"];
        ram = cfg["ram"];
        closeOnLaunch = Mc.IsTrue(cfg["closeOnLaunch"]);
        // Текстура скина для витрины и аватара: сначала с диска (мгновенно),
        // сеть — тихо в фоне. Главная никогда не ждёт загрузку скина.
        try { PlayerSkin.Init((Auth.LoggedIn && Auth.Name.Length > 0) ? Auth.Name : nick); } catch { }
        // папка пользовательских обложек создаём сразу, чтобы её
        // было видно рядом с лаунчером без открытия карточек
        Covers.EnsureDir();
        DoubleBuffered = true;
        Text = "Luxe Launcher " + Cfg.VersionLabel + " - Главная";
        // Своя рамка окна: системный заголовок светлый и выбивается
        // из тёмной темы, поэтому рисуем шапку сами (Chrome.cs)
        ChromeInit();
        // Полоса загрузки и текст статуса обслуживают все сетевые
        // загрузки (моды, Java, обновление лаунчера) из одной точки.
        Mc.Progress = OnDownloadProgress;
        Size = new Size(1280, 940);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Ui.Bg;
        ForeColor = Ui.Txt;
        // 1180x800 не влезает на ноутбук 1366x768: рабочая область там
        // ~728px, и нижняя часть окна уходила под панель задач. Минимум
        // уменьшен, раскладка ниже сжимается по фактической высоте.
        MinimumSize = new Size(1024, 680);
        KeyPreview = true;

        // ===== Сетка окна =====
        // Единая сетка вместо россыпи «магических» чисел: сайдбар,
        // шапка и поля контента заданы один раз, всё остальное считается
        // от них. Отсюда и ровные отступы на любом размере окна.
        int W = ClientSize.Width;
        int H = ClientSize.Height;
        const int SideW = 232;      // ширина сайдбара
        const int TopH = 64;        // высота шапки
        const int Marg = 20;        // поля контента
        const int RightW = 292;     // правая колонка (новости/премиум/быстрый запуск)
        const int RightGap = 16;
        int PX = SideW + Marg;
        int PW = W - PX - Marg - RightW - RightGap;   // ширина основной колонки

        // ===== Сайдбар =====
        Panel sideP = new Panel();
        side = sideP;
        side.BackColor = Ui.Side;
        side.Location = new Point(0, 0);
        side.Size = new Size(SideW, H);
        side.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom;
        Controls.Add(side);

        // Пейзаж внизу сайдбара: рисуется в фоне самой панели, поэтому
        // пункты меню и кнопки ложатся на него без «швов».
        side.Paint += delegate(object s2, PaintEventArgs pe)
        {
            Art.SideArt(pe.Graphics, side.ClientRectangle);
        };

        Panel sideLine = new Panel();
        sideLine.BackColor = Ui.Line;
        // Линия — ребёнок сайдбара, а не формы. С Anchor=Right на форме
        // при уменьшении окна она уезжала в минус (X=-11 при ширине 1024),
        // потому что пересчитывалась от старого размера. Внутри сайдбара
        // фиксированной ширины она всегда стоит на его правом краю.
        sideLine.Location = new Point(SideW - 1, 0);
        sideLine.Size = new Size(1, H);
        sideLine.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
        side.Controls.Add(sideLine);

        // Логотип рисуется вектором. Картинка logo.png имеет свой тёмный
        // фон и на сайдбаре выглядела как чужой вставленный квадрат.
        LogoBadge badge = new LogoBadge();
        badge.Location = new Point(18, 18);
        badge.Size = new Size(40, 40);
        side.Controls.Add(badge);
        PictureBox picLogo = new PictureBox();
        picLogo.Location = new Point(18, 18);
        picLogo.Size = new Size(40, 40);
        picLogo.SizeMode = PictureBoxSizeMode.Zoom;
        picLogo.BackColor = Ui.Side;
        LoadLogo(picLogo, badge);
        try
        {
            if (picLogo.Image != null) { Image old = picLogo.Image; picLogo.Image = null; old.Dispose(); }
            picLogo.Dispose();
        }
        catch { }

        PixelLabel lblLogoName = new PixelLabel();
        lblLogoName.Text = "Luxe Launcher";
        lblLogoName.Font = Ui.Font(12.5f, FontStyle.Bold);
        lblLogoName.ForeColor = Ui.Txt;
        lblLogoName.BackColor = Ui.Side;
        lblLogoName.AutoSize = false;
        lblLogoName.Location = new Point(68, 20);
        lblLogoName.Size = new Size(152, 22);
        side.Controls.Add(lblLogoName);
        Label lblLogoSub = new Label();
        lblLogoSub.Text = "MINECRAFT • OFFLINE";
        lblLogoSub.Font = Ui.Font(7, FontStyle.Bold);
        lblLogoSub.ForeColor = Ui.GrayDim;
        lblLogoSub.BackColor = Ui.Side;
        lblLogoSub.AutoSize = false;
        lblLogoSub.Location = new Point(68, 41);
        lblLogoSub.Size = new Size(152, 16);
        side.Controls.Add(lblLogoSub);

        // Карточка профиля: аватар-монограмма + ник + статус аккаунта
        profileCard = new RoundedPanel();
        profileCard.Radius = 10;
        profileCard.BackColor = Ui.Card;
        profileCard.Border = Ui.Line;
        profileCard.HoverBack = Ui.CardHover;
        profileCard.HoverBorder = Ui.Accent1;
        profileCard.Animated = true;
        profileCard.Location = new Point(14, 76);
        profileCard.Size = new Size(SideW - 28, 62);
        profileCard.Cursor = Cursors.Hand;
        profileCard.Click += OnAccount;
        side.Controls.Add(profileCard);

        PortraitBox av = new PortraitBox();
        av.Location = new Point(10, 9);
        av.Size = new Size(44, 44);
        av.Text2 = nick;
        av.Click += OnAccount;
        profileCard.Controls.Add(av);

        lblProfileNick = new Label();
        lblProfileNick.Font = Ui.Font(10.5f, FontStyle.Bold);
        lblProfileNick.ForeColor = Ui.Txt;
        lblProfileNick.BackColor = Color.Transparent;
        lblProfileNick.AutoSize = false;
        lblProfileNick.Location = new Point(64, 13);
        lblProfileNick.Size = new Size(SideW - 104, 20);
        lblProfileNick.Click += OnAccount;
        profileCard.Controls.Add(lblProfileNick);
        lblProfileSub = new Label();
        lblProfileSub.Font = Ui.Font(8);
        lblProfileSub.ForeColor = Ui.Gold;
        lblProfileSub.BackColor = Color.Transparent;
        lblProfileSub.AutoSize = false;
        lblProfileSub.Location = new Point(64, 33);
        lblProfileSub.Size = new Size(SideW - 124, 18);
        lblProfileSub.Click += OnAccount;
        profileCard.Controls.Add(lblProfileSub);

        // Корона «Premium» справа от статуса — как в референсе
        profileCrown = new IconView();
        profileCrown.Icon = Ico.Crown;
        profileCrown.Col = Ui.Gold;
        profileCrown.Size = new Size(14, 14);
        profileCrown.Location = new Point(SideW - 50, 35);
        profileCrown.Visible = false;
        profileCrown.Click += OnAccount;
        profileCard.Controls.Add(profileCrown);
        RefreshProfile();

        // ===== Пункты меню =====
        string[] navs = new string[] {
            "Главная", "Установки", "Сервера",
            "Новости", "Лента", "Моды", "Скины", "Друзья", "Плащи"
        };
        Ico[] icons = new Ico[] {
            Ico.Home, Ico.Layers, Ico.Server,
            Ico.News, Ico.List, Ico.Puzzle, Ico.Shirt, Ico.Users, Ico.Cape
        };
        int ny = 150;
        for (int i = 0; i < navs.Length; i++)
        {
            NavButton b = new NavButton();
            b.Icon = icons[i];
            b.HasArrow = (navs[i] == "Установки");
            b.Title = navs[i];
            b.Location = new Point(12, ny);
            b.Size = new Size(SideW - 24, 40);
            b.SetSelected(i == 0);
            string name = navs[i];
            if (i == 0) { navHome = b; b.Click += delegate { ShowView(ViewHome); }; }
            else if (name == "Новости") { navNews = b; b.Click += delegate { ShowView(ViewNews); }; }
            else if (name == "Моды") { navMods = b; b.Click += delegate { ShowView(ViewMods); }; }
            else if (name == "Скины") { navSkin = b; b.Click += delegate { ShowView(ViewSkin); }; }
            else if (name == "Друзья") { navFriends = b; b.Click += delegate { ShowView(ViewFriends); }; }
            else if (name == "Плащи") { navCapes = b; b.Click += delegate { ShowView(ViewCapes); }; }
            else
            {
                NavButton bb = b;
                b.Click += delegate { SetStatus("Раздел «" + name + "» в разработке", false); PulseNav(bb); };
            }
            side.Controls.Add(b);
            navList.Add(b);
            ny += 44;
        }

        NavButton bSet = new NavButton();
        bSet.Icon = Ico.Gear;
        bSet.Title = "Настройки";
        bSet.Location = new Point(12, H - 152);
        bSet.Size = new Size(SideW - 24, 40);
        bSet.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        bSet.Click += OnSettings;
        side.Controls.Add(bSet);
        NavButton bAcc = new NavButton();
        bAcc.Icon = Ico.User;
        bAcc.Title = "Аккаунты";
        bAcc.Location = new Point(12, H - 108);
        bAcc.Size = new Size(SideW - 24, 40);
        bAcc.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        bAcc.Click += OnAccount;
        side.Controls.Add(bAcc);

        Label lblVerFoot = new Label();
        lblVerFoot.Text = "Версия " + Cfg.AppVersion;
        lblVerFoot.Font = Ui.Font(8);
        lblVerFoot.ForeColor = Ui.GrayDim;
        lblVerFoot.BackColor = Color.Transparent;
        lblVerFoot.AutoSize = false;
        lblVerFoot.Location = new Point(16, H - 56);
        lblVerFoot.Size = new Size(SideW - 32, 16);
        lblVerFoot.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        side.Controls.Add(lblVerFoot);
        Label lblSys = new Label();
        lblSys.Text = "●  Все системы работают";
        lblSys.Font = Ui.Font(7.5f);
        lblSys.ForeColor = Ui.Green;
        lblSys.BackColor = Color.Transparent;
        lblSys.AutoSize = false;
        lblSys.Location = new Point(16, H - 38);
        lblSys.Size = new Size(SideW - 32, 16);
        lblSys.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        side.Controls.Add(lblSys);

        // ===== Своя шапка окна =====
        top = new Panel();
        top.BackColor = Ui.Bg;
        top.Location = new Point(SideW, 0);
        top.Size = new Size(W - SideW, TopH);
        top.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        top.MouseDown += delegate { TitleDrag(); };
        top.DoubleClick += delegate { ToggleMax(); };
        Controls.Add(top);

        Panel topLine = new Panel();
        topLine.BackColor = Ui.Line;
        topLine.Location = new Point(0, TopH - 1);
        topLine.Size = new Size(W - SideW, 1);
        topLine.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        top.Controls.Add(topLine);

        // Заголовок раздела оставляем скрытым: в референсе его место
        // занимает глобальный поиск, а раздел и так подсвечен в меню.
        pageTitle = new PixelLabel();
        pageTitle.Text = "Главная";
        pageTitle.Font = Ui.Font(13, FontStyle.Bold);
        pageTitle.ForeColor = Ui.Txt;
        pageTitle.BackColor = Ui.Bg;
        pageTitle.AutoSize = false;
        pageTitle.Location = new Point(Marg, 0);
        pageTitle.Size = new Size(0, 0);
        pageTitle.Visible = false;
        pageTitle.MouseDown += delegate { TitleDrag(); };
        pageTitle.DoubleClick += delegate { ToggleMax(); };
        top.Controls.Add(pageTitle);

        // Правый край шапки считаем от кнопок окна: они всегда у самого
        // угла, инструменты раздела — слева от них.
        int tbW = W - SideW;
        int wbW = 36, wbGap = 4;
        int closeX = tbW - Marg - wbW;
        int maxX = closeX - wbW - wbGap;
        int minX = maxX - wbW - wbGap;
        int sepX = minX - 14;
        int themeX = sepX - 10 - 34;
        int bellX = themeX - 40;
        int aiX = bellX - 40;
        int searchW = Math.Max(240, aiX - 24 - Marg);
        if (searchW > 520) searchW = 520;

        // Глобальный поиск (версии, моды, серверы)
        topSearchWrap = new RoundedPanel();
        topSearchWrap.Radius = 9;
        topSearchWrap.BackColor = Ui.Card;
        topSearchWrap.Border = Ui.Line;
        topSearchWrap.HoverBorder = Ui.Accent1;
        topSearchWrap.Animated = true;
        topSearchWrap.Glass = false;
        topSearchWrap.Location = new Point(Marg, 15);
        topSearchWrap.Size = new Size(searchW, 34);
        topSearchWrap.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        topSearchWrap.Cursor = Cursors.IBeam;
        top.Controls.Add(topSearchWrap);

        IconView tsIco = new IconView();
        tsIco.Icon = Ico.Search;
        tsIco.Col = Ui.GrayDim;
        tsIco.Location = new Point(12, 9);
        tsIco.Size = new Size(16, 16);
        topSearchWrap.Controls.Add(tsIco);

        topSearchBox = new TextBox();
        topSearchBox.Font = Ui.Font(9.5f);
        topSearchBox.BackColor = Ui.Card;
        topSearchBox.ForeColor = Ui.Gray;
        topSearchBox.BorderStyle = BorderStyle.None;
        topSearchBox.Text = "Поиск версий, модов, серверов...";
        topSearchBox.Location = new Point(38, 8);
        topSearchBox.Size = new Size(searchW - 52, 18);
        topSearchBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        topSearchBox.GotFocus += delegate
        {
            if (topSearchBox.Text == "Поиск версий, модов, серверов...")
            {
                topSearchBox.Text = "";
                topSearchBox.ForeColor = Ui.Txt;
            }
        };
        topSearchBox.LostFocus += delegate
        {
            if (topSearchBox.Text.Trim() == "")
            {
                topSearchBox.Text = "Поиск версий, модов, серверов...";
                topSearchBox.ForeColor = Ui.Gray;
            }
        };
        topSearchBox.TextChanged += delegate
        {
            if (topSearchBox.Text == TopSearchHint) return;
            // один поиск на всё: в разделе модов — по модам, на главной — по версиям
            if (view == ViewMods)
            {
                modsSyncUi = true;
                modsSearch.Text = topSearchBox.Text;
                modsSyncUi = false;
                ModsSearch(topSearchBox.Text.Trim());
            }
            else
            {
                BuildRowsFiltered(SearchQuery());
            }
        };
        topSearchWrap.Controls.Add(topSearchBox);

        topBell = RoundedButton.IcoBtn(Ico.Bell, 34, 9);
        topBell.IconSize = 16;
        topBell.Location = new Point(bellX, 15);
        topBell.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        topBell.Click += delegate { ShowView(ViewNews); };
        top.Controls.Add(topBell);

        // Помощник: кнопка с искрой слева от колокольчика.
        RoundedButton topAi = RoundedButton.IcoBtn(Ico.Sparkle, 34, 9);
        topAi.IconSize = 17;
        topAi.Location = new Point(aiX, 15);
        topAi.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        topAi.ForeColor = Ui.Accent1;
        topAi.Click += delegate { ShowAi(); };
        top.Controls.Add(topAi);

        // маленькая точка-уведомление на колокольчике
        Panel bellDot = new Panel();
        bellDot.BackColor = Ui.Accent1;
        bellDot.Size = new Size(7, 7);
        bellDot.Location = new Point(topBell.Width - 10, 7);
        topBell.Controls.Add(bellDot);

        topTheme = RoundedButton.IcoBtn(Ico.Moon, 34, 9);
        topTheme.IconSize = 16;
        topTheme.Location = new Point(themeX, 15);
        topTheme.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        topTheme.Click += delegate { SetStatus("Светлая тема появится в следующем обновлении", true); };
        top.Controls.Add(topTheme);

        Panel topSep = new Panel();
        topSep.BackColor = Ui.Line;
        topSep.Location = new Point(sepX, 20);
        topSep.Size = new Size(1, 24);
        topSep.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        top.Controls.Add(topSep);

        // кнопки окна
        RoundedButton wMin = WinBtn(Ico.Min, false);
        wMin.Location = new Point(minX, 17);
        wMin.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        wMin.Click += delegate { WindowState = FormWindowState.Minimized; };
        top.Controls.Add(wMin);
        winMaxBtn = WinBtn(Ico.Max, false);
        winMaxBtn.Location = new Point(maxX, 17);
        winMaxBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        winMaxBtn.Click += delegate { ToggleMax(); };
        top.Controls.Add(winMaxBtn);
        RoundedButton wClose = WinBtn(Ico.Close, true);
        wClose.Location = new Point(closeX, 17);
        wClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        wClose.Click += delegate { Close(); };
        top.Controls.Add(wClose);

        // ===== Подложка главного экрана =====
        // Разделы «Новости»/«Моды» лежат ниже и всегда видимы, поэтому
        // главный экран закрывает их целиком.
        homeBg = new Panel();
        homeBg.BackColor = Ui.Bg;
        homeBg.Location = new Point(SideW, TopH);
        homeBg.Size = new Size(W - SideW, H - TopH);
        homeBg.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        Controls.Add(homeBg);

        // ===== Витрина =====
        int heroY = TopH + 14;
        int heroH = 226;
        int heroLw = (int)(PW * 0.56f);
        if (heroLw < 260) heroLw = 260;
        banner = new HeroPanel();
        banner.Location = new Point(PX, heroY);
        banner.Size = new Size(PW, heroH);
        banner.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        Controls.Add(banner);

        lblHeroHi = new PixelLabel();
        lblHeroHi.Text = "Погрузись\nв бесконечный\nмир приключений";
        lblHeroHi.Font = Ui.Font(17, FontStyle.Bold);
        lblHeroHi.LineH = 24;
        lblHeroHi.ForeColor = Ui.Txt;
        lblHeroHi.BackColor = Color.Transparent;
        lblHeroHi.AutoSize = false;
        lblHeroHi.Location = new Point(26, 14);
        lblHeroHi.Size = new Size(heroLw, 84);
        banner.Controls.Add(lblHeroHi);
        lblHeroSub = new Label();
        // подпись под заголовком: короткая, в две строки
        lblHeroSub.Text = "Исследуй, строй, выживай.\r\nИграй по своим правилам.";
        lblHeroSub.Font = Ui.Font(9);
        lblHeroSub.ForeColor = Color.FromArgb(214, 220, 240);
        lblHeroSub.BackColor = Color.Transparent;
        lblHeroSub.AutoSize = false;
        lblHeroSub.Location = new Point(28, 96);
        lblHeroSub.Size = new Size(heroLw, 34);
        banner.Controls.Add(lblHeroSub);

        dot = new StatusDot();
        dot.Location = new Point(28, 140);
        dot.Size = new Size(10, 10);
        banner.Controls.Add(dot);
        lblStatus = new Label();
        lblStatus.Text = "Готов";
        lblStatus.Font = Ui.Font(8.5f);
        lblStatus.ForeColor = Color.FromArgb(206, 212, 232);
        lblStatus.BackColor = Color.Transparent;
        lblStatus.AutoSize = false;
        lblStatus.Location = new Point(45, 135);
        lblStatus.Size = new Size(heroLw, 20);
        lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        banner.Controls.Add(lblStatus);

        btnPlay = new RoundedButton();
        btnPlay.Text = "Играть";
        btnPlay.Icon = Ico.Play;
        btnPlay.IconSize = 18;
        btnPlay.IconGap = 9;
        btnPlay.Font = Ui.Font(12.5f, FontStyle.Bold);
        btnPlay.Radius = 10;
        btnPlay.BorderW = 0;
        btnPlay.Glow = true;
        btnPlay.Location = new Point(26, 172);
        btnPlay.Size = new Size(156, 40);
        btnPlay.Click += OnPlay;
        banner.Controls.Add(btnPlay);

        cmbVer = new Dropdown();
        cmbVer.Font = Ui.Font(9.5f);
        // как в референсе: зелёная плитка с блоком и подпись над версией
        cmbVer.LeadIcon = Ico.Box;
        cmbVer.LeadTileFrom = Color.FromArgb(126, 176, 84);
        cmbVer.LeadTileTo = Color.FromArgb(78, 122, 52);
        cmbVer.SubLabel = "Последняя версия";
        cmbVer.Location = new Point(192, 174);
        cmbVer.Size = new Size(Math.Max(210, Math.Min(268, heroLw - 192)), 36);
        banner.Controls.Add(cmbVer);

        // Точки-индикаторы «слайдов» внизу справа — как в референсе
        heroDots = new HeroDots();
        heroDots.Location = new Point(PW - 24 - heroDots.Width, heroH - 21);
        heroDots.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        banner.Controls.Add(heroDots);

        bar = new ModernProgress();
        bar.Location = new Point(26, heroH - 12);
        bar.Size = new Size(PW - 52, 4);
        bar.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        banner.Controls.Add(bar);

        // ===== Правая колонка: новости, премиум, быстрый запуск =====
        // Высоты считаем от низа колонки, чтобы блоки всегда доходили
        // до футера и не оставалось пустого «хвоста».
        int rightX = W - Marg - RightW;
        int rightH = H - heroY - Marg;
        rightCol = new Panel();
        rightCol.BackColor = Ui.Bg;
        rightCol.Location = new Point(rightX, heroY);
        rightCol.Size = new Size(RightW, rightH);
        rightCol.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
        Controls.Add(rightCol);

        // Высоты правой колонки берём из общей метрики (Metrics):
        // раньше здесь была своя копия формул, и она разошлась с HomeLayout.
        HomeMetrics hm0 = Metrics(H);
        int premH = hm0.PremH;
        int newsH = hm0.NewsH;
        int quickH = hm0.QuickH;
        int premY = newsH + 12;
        int quickY = premY + premH + 12;

        newsRightCard = new RoundedPanel();
        newsRightCard.Radius = 14;
        newsRightCard.BackColor = Ui.Card;
        newsRightCard.Border = Ui.Line;
        newsRightCard.Glass = false;
        newsRightCard.Location = new Point(0, 0);
        newsRightCard.Size = new Size(RightW, newsH);
        newsRightCard.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        rightCol.Controls.Add(newsRightCard);
        BuildRightNews();

        premiumCard = new RoundedPanel();
        premiumCard.Radius = 14;
        premiumCard.BackColor = Ui.Card;
        premiumCard.Border = Ui.Line;
        premiumCard.Gradient = true;
        premiumCard.GradFrom = Color.FromArgb(44, 32, 86);
        premiumCard.GradTo = Color.FromArgb(20, 18, 38);
        premiumCard.GradAngle = 30f;
        premiumCard.Glass = false;
        premiumCard.Location = new Point(0, premY);
        premiumCard.Size = new Size(RightW, premH);
        premiumCard.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        rightCol.Controls.Add(premiumCard);
        BuildPremium();

        quickCard = new RoundedPanel();
        quickCard.Radius = 14;
        quickCard.BackColor = Ui.Card;
        quickCard.Border = Ui.Line;
        quickCard.Glass = false;
        quickCard.Location = new Point(0, quickY);
        quickCard.Size = new Size(RightW, quickH);
        quickCard.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        rightCol.Controls.Add(quickCard);
        BuildQuickLaunch();

        rightStatusDot = new StatusDot();
        rightStatusDot.DotColor = Ui.Green;
        rightStatusDot.Size = new Size(10, 10);
        rightStatusDot.Location = new Point(8, rightH - 20);
        rightStatusDot.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        rightCol.Controls.Add(rightStatusDot);
        rightStatusLbl = new Label();
        rightStatusLbl.Text = "Все системы работают";
        rightStatusLbl.Font = Ui.Font(8);
        rightStatusLbl.ForeColor = Ui.GrayDim;
        rightStatusLbl.BackColor = Color.Transparent;
        rightStatusLbl.AutoSize = false;
        rightStatusLbl.Location = new Point(24, rightH - 24);
        rightStatusLbl.Size = new Size(RightW - 24, 18);
        rightStatusLbl.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        rightCol.Controls.Add(rightStatusLbl);

        // ===== Вкладки загрузчиков =====
        int tbY = heroY + heroH + 16;
        // Распределение версий: новые, OptiFine, Fabric, Forge, визуалы, кряки.
        // «Популярное»/«Последние» показывали один и тот же список, а «Моды»
        // на деле открывали Fabric — эти вкладки убраны.
        string[] loaderNames = new string[] { "Новые", "OptiFine", "Fabric", "Forge", "Визуалы", "Кряки" };
        Ico[] loaderIcons = new Ico[] { Ico.Fire, Ico.Zap, Ico.Puzzle, Ico.Layers, Ico.Palette, Ico.Star };
        Font tabFont = Ui.Font(8, FontStyle.Bold);
        int lxx = PX;
        for (int li = 0; li < loaderNames.Length; li++)
        {
            RoundedButton tb = RoundedButton.Dark(loaderNames[li], 9);
            tb.Font = tabFont;
            tb.ForeColor = Ui.Gray;
            tb.BorderW = 0;
            tb.Icon = loaderIcons[li];
            tb.IconSize = 13;
            tb.IconGap = 6;
            tb.Size = new Size(TextRenderer.MeasureText(loaderNames[li], tabFont).Width + 19 + 16, 32);
            tb.Location = new Point(lxx, tbY);
            int idx = li;
            tb.Click += delegate { SetHomeTab(idx); };
            Controls.Add(tb);
            loaderTabs.Add(tb);
            lxx += tb.Width + 6;
        }

        allVersionsBtn = new RoundedButton();
        allVersionsBtn.Text = "Все версии  →";
        allVersionsBtn.ForeColor = Ui.Accent1;
        allVersionsBtn.Flat = true;
        allVersionsBtn.BorderW = 0;
        allVersionsBtn.Top = Color.Transparent;
        allVersionsBtn.Bottom = Color.Transparent;
        allVersionsBtn.HoverTop = Ui.Card;
        allVersionsBtn.HoverBottom = Ui.Card;
        allVersionsBtn.Size = new Size(118, 30);
        allVersionsBtn.Location = new Point(PX + PW - 118, tbY + 1);
        allVersionsBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        allVersionsBtn.Click += delegate { ShowView(ViewMods); };
        allVersionsBtn.Visible = false;      // заменена на TextLink ниже
        Controls.Add(allVersionsBtn);

        homeAllVerLink = new TextLink();
        homeAllVerLink.Text = "Все версии";
        homeAllVerLink.Font = Ui.Font(8.5f, FontStyle.Bold);
        homeAllVerLink.Size = new Size(118, 30);
        homeAllVerLink.Location = new Point(PX + PW - 118, tbY + 1);
        homeAllVerLink.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        homeAllVerLink.Click += delegate { ShowView(ViewMods); };
        Controls.Add(homeAllVerLink);
        RefreshTabs();

        // ===== Заголовок списка =====
        int listHeadY = tbY + 44;
        IconView clockIco = new IconView();
        clockIco.Icon = Ico.Clock;
        clockIco.Col = Ui.Gray;
        clockIco.Location = new Point(PX + 2, listHeadY + 4);
        clockIco.Size = new Size(17, 17);
        Controls.Add(clockIco);
        homeClockIco = clockIco;
        homeListHead = new PixelLabel();
        homeListHead.Text = "Новые версии";
        homeListHead.Font = Ui.Font(11, FontStyle.Bold);
        homeListHead.ForeColor = Ui.Txt;
        homeListHead.BackColor = Ui.Bg;
        homeListHead.AutoSize = false;
        homeListHead.Location = new Point(PX + 26, listHeadY);
        homeListHead.Size = new Size(240, 26);
        Controls.Add(homeListHead);

        // ===== Список версий =====
        int vbY = listHeadY + 30;
        int popH = 110;                     // блок «Популярные моды» внизу
        int vbH = H - vbY - Marg - popH;
        if (vbH < 180) vbH = 180;
        verListBox = new Panel();
        verListBox.BackColor = Ui.Bg;
        verListBox.Location = new Point(PX, vbY);
        verListBox.Size = new Size(PW, vbH);
        verListBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        Controls.Add(verListBox);

        verList = new DarkFlowPanel();
        verList.Location = new Point(0, 0);
        verList.Size = new Size(PW, vbH);
        verList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        verList.BackColor = Ui.Bg;
        verList.AutoScroll = true;
        verList.WrapContents = true;
        verList.FlowDirection = FlowDirection.LeftToRight;
        verList.NearBottom = LoadMoreVersions;
        // Карточки нижнего ряда рисуются поверх соседей и «выползали»
        // за панель наезжая на блок «Популярные моды». SetClip в Paint
        // режет только собственную отрисовку панели, поэтому клипуем
        // Region'ом — он действует и на дочерние контролы.
        verListBox.Controls.Add(verList);
        verList.Region = new Region(new Rectangle(Point.Empty, verList.Size));

        // ===== Популярные моды =====
        popularBlock = new Panel();
        popularBlock.BackColor = Ui.Bg;
        popularBlock.Location = new Point(PX, H - Marg - popH + 6);
        popularBlock.Size = new Size(PW, popH - 6);
        popularBlock.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        Controls.Add(popularBlock);
        BuildPopularMods();

        // ===== Разделы (Новости / Моды) =====
        // Сами разделы строит BuildSectionViews() — он кладёт контролы
        // прямо на форму, потому что вложенные панели теряют Visible.
        sectionsBuilt = false;

        // ===== Встроенная ошибка-тост (вместо стоковых окон) =====
        toast = new RoundedPanel();
        toast.Radius = 12;
        toast.BackColor = Color.FromArgb(52, 24, 30);
        toast.Border = Color.FromArgb(190, 78, 92);
        toast.BorderW = 1;
        toast.Location = new Point(W - Marg - 460, TopH + 10);
        toast.Size = new Size(460, 56);
        toast.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        toast.Visible = false;
        Controls.Add(toast);
        toastTxt = new Label();
        toastTxt.ForeColor = Color.FromArgb(255, 202, 208);
        toastTxt.BackColor = Color.Transparent;
        toastTxt.Font = Ui.Font(9);
        toastTxt.AutoSize = false;
        toastTxt.Location = new Point(16, 6);
        toastTxt.Size = new Size(392, 44);
        toast.Controls.Add(toastTxt);
        RoundedButton toastX = RoundedButton.IcoBtn(Ico.Close, 30, 8);
        toastX.Location = new Point(414, 13);
        toastX.Click += delegate { toast.Visible = false; };
        toast.Controls.Add(toastX);
        toastTimer = new System.Windows.Forms.Timer();
        toastTimer.Interval = 6000;
        toastTimer.Tick += delegate { toast.Visible = false; toastTimer.Stop(); };

        // ===== Затемнение + встроенные панели Аккаунты/Настройки =====
        dimPanel = new Panel();
        dimPanel.BackColor = Color.FromArgb(10, 12, 16);
        dimPanel.Location = new Point(0, 0);
        dimPanel.Size = new Size(W, H);
        dimPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        dimPanel.Visible = false;
        dimPanel.Click += delegate { HideOverlays(); };
        Controls.Add(dimPanel);

        int cardW = 400;
        int cardX = (W - cardW) / 2;

        accCard = new RoundedPanel();
        accCard.Radius = 16;
        accCard.BackColor = Ui.Surface;
        accCard.Border = Ui.Line;
        accCard.Location = new Point(cardX, (H - 344) / 2);
        accCard.Size = new Size(cardW, 344);
        accCard.Visible = false;
        Controls.Add(accCard);
        CardHead(accCard, "Аккаунты", cardW, delegate { HideOverlays(); });
        Label accHint = new Label();
        accHint.Text = "Ник (3-16: a-z, 0-9, _)";
        accHint.Font = Ui.Font(9);
        accHint.ForeColor = Ui.GrayDim;
        accHint.BackColor = Color.Transparent;
        accHint.AutoSize = false;
        accHint.Location = new Point(24, 66);
        accHint.Size = new Size(cardW - 48, 18);
        accCard.Controls.Add(accHint);
        accNick = new TextBox();
        accNick.Font = Ui.Font(11);
        accNick.BackColor = Ui.SurfaceHi;
        accNick.ForeColor = Ui.Txt;
        accNick.BorderStyle = BorderStyle.None;
        accNick.Location = new Point(12, 9);
        accNick.Size = new Size(cardW - 48 - 24, 22);
        RoundedPanel nickBox = FieldBox(cardW - 48, 40, 88);
        nickBox.Controls.Add(accNick);
        accCard.Controls.Add(nickBox);
        RoundedButton accSave = new RoundedButton();
        accSave.Text = "Сохранить";
        accSave.Radius = 12;
        accSave.Font = Ui.Font(10, FontStyle.Bold);
        accSave.Location = new Point(24, 148);
        accSave.Size = new Size(cardW / 2 - 30, 44);
        accSave.Click += delegate { SaveAccount(); };
        accCard.Controls.Add(accSave);
        RoundedButton accCancel = RoundedButton.Dark("Отмена", 12);
        accCancel.Font = Ui.Font(10, FontStyle.Bold);
        accCancel.ForeColor = Ui.Txt;
        accCancel.Location = new Point(cardW / 2 + 6, 148);
        accCancel.Size = new Size(cardW / 2 - 30, 44);
        accCancel.Click += delegate { HideOverlays(); };
        accCard.Controls.Add(accCancel);

        // ===== Лицензия Microsoft (Auth.cs) =====
        // Поле ника выше никуда не девается: без входа игра запускается
        // как раньше — офлайн, с токеном "0"
        Panel licLine = new Panel();
        licLine.BackColor = Ui.Line;
        licLine.Location = new Point(24, 206);
        licLine.Size = new Size(cardW - 48, 1);
        accCard.Controls.Add(licLine);

        Label licHead = new Label();
        licHead.Text = "Лицензия Microsoft";
        licHead.Font = Ui.Font(9);
        licHead.ForeColor = Ui.GrayDim;
        licHead.BackColor = Color.Transparent;
        licHead.AutoSize = false;
        licHead.Location = new Point(24, 218);
        licHead.Size = new Size(cardW - 48, 18);
        accCard.Controls.Add(licHead);

        accLic = new Label();
        accLic.Font = Ui.Font(10.5f, FontStyle.Bold);
        accLic.ForeColor = Ui.GrayDim;
        accLic.BackColor = Color.Transparent;
        accLic.AutoSize = false;
        accLic.Location = new Point(24, 238);
        accLic.Size = new Size(cardW - 48, 22);
        accCard.Controls.Add(accLic);

        accLicBtn = new RoundedButton();
        accLicBtn.Radius = 12;
        accLicBtn.Font = Ui.Font(10, FontStyle.Bold);
        accLicBtn.Location = new Point(24, 268);
        accLicBtn.Size = new Size(cardW - 48, 42);
        accLicBtn.Click += delegate { ShowAuth(); };
        accCard.Controls.Add(accLicBtn);

        // ===== Карточка входа: код и ссылка на Microsoft =====
        authCard = new RoundedPanel();
        authCard.Radius = 16;
        authCard.BackColor = Ui.Surface;
        authCard.Border = Ui.Line;
        authCard.Location = new Point(cardX, (H - 320) / 2);
        authCard.Size = new Size(cardW, 320);
        authCard.Visible = false;
        Controls.Add(authCard);
        CardHead(authCard, "Вход в Microsoft", cardW, delegate { HideOverlays(); });

        authStep = new Label();
        authStep.Font = Ui.Font(9.5f);
        authStep.ForeColor = Ui.Gray;
        authStep.BackColor = Color.Transparent;
        authStep.AutoSize = false;
        authStep.Location = new Point(24, 74);
        authStep.Size = new Size(cardW - 48, 62);
        authCard.Controls.Add(authStep);

        authCode = new Label();
        authCode.Font = Ui.Font(21, FontStyle.Bold);
        authCode.ForeColor = Ui.Green;
        authCode.BackColor = Color.Transparent;
        authCode.AutoSize = false;
        authCode.TextAlign = ContentAlignment.MiddleCenter;
        authCode.Location = new Point(24, 142);
        authCode.Size = new Size(cardW - 48, 44);
        authCard.Controls.Add(authCode);

        RoundedButton authOpen = new RoundedButton();
        authOpen.Text = "Открыть сайт Microsoft";
        authOpen.Radius = 12;
        authOpen.Font = Ui.Font(10, FontStyle.Bold);
        authOpen.Location = new Point(24, 196);
        authOpen.Size = new Size(cardW - 48, 44);
        authOpen.Click += delegate { Auth.OpenBrowser(authUri.Length > 0 ? authUri : "https://microsoft.com/link"); };
        authCard.Controls.Add(authOpen);

        RoundedButton authCancel = RoundedButton.Dark("Закрыть", 12);
        authCancel.Font = Ui.Font(10, FontStyle.Bold);
        authCancel.ForeColor = Ui.Txt;
        authCancel.Location = new Point(24, 250);
        authCancel.Size = new Size(cardW - 48, 44);
        authCancel.Click += delegate { HideOverlays(); };
        authCard.Controls.Add(authCancel);

        // Settings.cs: многостраничные настройки в стиле TL Legacy
        BuildSettingsTabs();

        updateCard = new RoundedPanel();
        updateCard.Radius = 16;
        updateCard.BackColor = Ui.Surface;
        updateCard.Border = Ui.Accent1;
        updateCard.Gradient = true;
        updateCard.GradFrom = Color.FromArgb(32, 28, 52);
        updateCard.GradTo = Ui.Surface;
        updateCard.Location = new Point(cardX, (H - 320) / 2);
        updateCard.Size = new Size(cardW, 320);
        updateCard.Visible = false;
        Controls.Add(updateCard);
        updateTitle = new Label();
        updateTitle.Text = "Доступно обновление";
        updateTitle.Font = Ui.Font(13, FontStyle.Bold);
        updateTitle.ForeColor = Ui.Txt;
        updateTitle.BackColor = Color.Transparent;
        updateTitle.AutoSize = false;
        updateTitle.Location = new Point(24, 20);
        updateTitle.Size = new Size(cardW - 72, 26);
        updateCard.Controls.Add(updateTitle);
        updateNotes = new Label();
        updateNotes.Font = Ui.Font(9);
        updateNotes.ForeColor = Ui.Gray;
        updateNotes.BackColor = Color.Transparent;
        updateNotes.AutoSize = false;
        updateNotes.Location = new Point(24, 56);
        updateNotes.Size = new Size(cardW - 48, 130);
        updateCard.Controls.Add(updateNotes);
        btnUpdateGo = new RoundedButton();
        btnUpdateGo.Text = "Обновить и перезапустить";
        btnUpdateGo.Radius = 12;
        btnUpdateGo.Font = Ui.Font(10, FontStyle.Bold);
        btnUpdateGo.FitText = true;
        btnUpdateGo.Location = new Point(24, 196);
        btnUpdateGo.Size = new Size(cardW - 48, 46);
        btnUpdateGo.Click += delegate { DownloadAndApply(); };
        updateCard.Controls.Add(btnUpdateGo);
        btnUpdateLater = RoundedButton.Dark("Позже", 12);
        btnUpdateLater.Font = Ui.Font(9, FontStyle.Bold);
        btnUpdateLater.ForeColor = Ui.Txt;
        btnUpdateLater.Location = new Point(24, 252);
        btnUpdateLater.Size = new Size(cardW - 48, 40);
        btnUpdateLater.Click += delegate { HideOverlays(); };
        updateCard.Controls.Add(btnUpdateLater);
        // Разделы строим в конце конструктора — пока форма не показана,
        // но все контролы кладём прямо в Controls
        ChromeGrips();
        BuildSectionViews();
        LayoutSections();
        // Подарочный билд для Артёма: экран приветствия поверх лаунчера
        try { if (Cfg.GiftBuild) BuildArtem(); } catch { }

        Shown += delegate
        {
            // уголки и тёмная рамка окна (Win11), на Win10 просто молчит
            try { ChromeDwm(); } catch { }
            // разделы гасим только после показа формы: до Show явный
            // Visible=false ломает их отрисовку (особенность WinForms)
            try { BeginInvoke(new Action(delegate { try { ApplyView(); } catch { } })); } catch { }
            try
            {
                string j0 = Mc.FindLocalJava();
                if (j0 != null)
                {
                    javaExe = j0;
                    SetStatus("Java найдена. Готов к запуску.", true);
                }
                else SetStatus("Java скачается сама при первом запуске.", true);
            }
            catch { }
            try { LoadVersions(); }
            catch (Exception ex) { SetStatus("Ошибка версий: " + ex.Message, false); }
            // Друзья и плащи: вход в сервис и первый heartbeat
            try { SocialStart(); } catch { }
            Thread u = new Thread((ThreadStart)delegate { CheckUpdates(false); });
            u.IsBackground = true;
            u.Start();
        };
    }

    // Содержимое разделов строим после показа формы — до этого WinForms
    // не даёт детям вложенных панелей корректный флаг Visible
// Разделы «Новости» и «Моды» строятся прямо на форме — как главный экран.
    // Вложенные панели здесь непригодны: WinForms теряет у них Visible,
    // поэтому все контролы разделов кладём прямо в Controls формы
    // и переключаем видимость списками.
    void BuildSectionViews()
    {
        if (sectionsBuilt) return;
        try
        {
            BuildSectionViewsCore();
            sectionsBuilt = true;
        }
        catch (Exception ex)
        {
            // не помечаем как построенное — чтобы следующий клик попробовал снова
            try { File.AppendAllText(Cfg.LogPath("error.log"),
                "BuildSectionViews: " + ex + Environment.NewLine, Encoding.UTF8); } catch { }
            SetStatus("Ошибка раздела: " + ex.Message, false);
        }
    }

    void BuildSectionViewsCore()
    {
        newsUi = new List<Control>();
        modsUi = new List<Control>();
        BuildSkinSection();
        BuildFriendsSection();
        BuildCapesSection();

        // ===== Новости =====
        // непрозрачная подложка, иначе главный экран просвечивает сквозь раздел
        Panel newsBg = new Panel();
        newsBg.BackColor = Ui.Bg;
        newsBg.Location = new Point(SecX, SecY);
        newsBg.Size = new Size(888, 518);
        Controls.Add(newsBg); newsUi.Add(newsBg);

        PixelLabel newsHead = new PixelLabel();
        newsHead.Text = "Новости";
        newsHead.Font = Ui.Font(17, FontStyle.Bold);
        newsHead.ForeColor = Ui.Txt;
        newsHead.BackColor = Ui.Bg;
        newsHead.AutoSize = false;
        newsHead.Location = new Point(SecX, SecY);
        newsHead.Size = new Size(400, 32);
        Controls.Add(newsHead); newsUi.Add(newsHead);

        Label newsSub = new Label();
        newsSub.Text = "Что нового в каждой версии — свежие сверху";
        newsSub.Font = Ui.Font(9.5f);
        newsSub.ForeColor = Ui.Gray;
        newsSub.BackColor = Ui.Bg;
        newsSub.AutoSize = false;
        newsSub.Location = new Point(SecX, SecY + 34);
        newsSub.Size = new Size(600, 20);
        Controls.Add(newsSub); newsUi.Add(newsSub);

        newsList = new DarkFlowPanel();
        newsList.Location = new Point(SecX, SecY + 64);
        newsList.Size = new Size(856, 436);
        newsList.BackColor = Ui.Bg;
        newsList.FlowDirection = FlowDirection.TopDown;
        Controls.Add(newsList); newsUi.Add(newsList);
        BuildNews();

        // ===== Моды (Modrinth) =====
        Panel modsBg = new Panel();
        modsBg.BackColor = Ui.Bg;
        modsBg.Location = new Point(SecX, SecY);
        modsBg.Size = new Size(888, 518);
        Controls.Add(modsBg); modsUi.Add(modsBg);

        PixelLabel modsHead = new PixelLabel();
        modsHead.Text = "Моды";
        modsHead.Font = Ui.Font(17, FontStyle.Bold);
        modsHead.ForeColor = Ui.Txt;
        modsHead.BackColor = Ui.Bg;
        modsHead.AutoSize = false;
        modsHead.Location = new Point(SecX, SecY);
        modsHead.Size = new Size(400, 32);
        Controls.Add(modsHead); modsUi.Add(modsHead);

        Label modsSub = new Label();
        modsSub.Text = "Каталог Modrinth — установка вместе с обязательными зависимостями";
        modsSub.Font = Ui.Font(9.5f);
        modsSub.ForeColor = Ui.Gray;
        modsSub.BackColor = Ui.Bg;
        modsSub.AutoSize = false;
        modsSub.Location = new Point(SecX, SecY + 34);
        modsSub.Size = new Size(600, 20);
        Controls.Add(modsSub); modsUi.Add(modsSub);

        RoundedPanel bar1 = new RoundedPanel();
        bar1.Radius = 14;
        bar1.BackColor = Ui.Card;
        bar1.Border = Ui.Line;
        bar1.Location = new Point(SecX, SecY + 62);
        bar1.Size = new Size(852, 56);
        Controls.Add(bar1); modsUi.Add(bar1);

        Label lbTarget = new Label();
        lbTarget.Text = "Сборка";
        lbTarget.Font = Ui.Font(8, FontStyle.Bold);
        lbTarget.ForeColor = Ui.GrayDim;
        lbTarget.BackColor = Color.Transparent;
        lbTarget.AutoSize = false;
        lbTarget.Location = new Point(16, 7);
        lbTarget.Size = new Size(120, 14);
        bar1.Controls.Add(lbTarget);

        modsTargetDd = new Dropdown();
        modsTargetDd.Font = Ui.Font(9);
        modsTargetDd.Location = new Point(16, 24);
        modsTargetDd.Size = new Size(280, 26);
        bar1.Controls.Add(modsTargetDd);

        Label lbLoader = new Label();
        lbLoader.Text = "Загрузчик";
        lbLoader.Font = Ui.Font(8, FontStyle.Bold);
        lbLoader.ForeColor = Ui.GrayDim;
        lbLoader.BackColor = Color.Transparent;
        lbLoader.AutoSize = false;
        lbLoader.Location = new Point(312, 7);
        lbLoader.Size = new Size(120, 14);
        bar1.Controls.Add(lbLoader);

        modsLoaderDd = new Dropdown();
        modsLoaderDd.Items.AddRange(new string[] { "Fabric", "Любой", "Quilt", "Forge", "NeoForge" });
        modsLoaderDd.SelectedIndex = 0;
        modsLoaderDd.Font = Ui.Font(9);
        modsLoaderDd.Location = new Point(312, 24);
        modsLoaderDd.Size = new Size(130, 26);
        bar1.Controls.Add(modsLoaderDd);

        modsStatTxt = new Label();
        modsStatTxt.Text = "";
        modsStatTxt.Font = Ui.Font(8.5f);
        modsStatTxt.ForeColor = Ui.Gray;
        modsStatTxt.BackColor = Color.Transparent;
        modsStatTxt.AutoSize = false;
        modsStatTxt.Location = new Point(456, 20);
        modsStatTxt.Size = new Size(260, 18);
        modsStatTxt.TextAlign = ContentAlignment.MiddleLeft;
        bar1.Controls.Add(modsStatTxt);

        modsReload = RoundedButton.Dark("Обновить", 9);
        modsReload.Icon = Ico.Refresh;
        modsReload.IconSize = 15;
        modsReload.Font = Ui.Font(8.5f, FontStyle.Bold);
        modsReload.ForeColor = Ui.Txt;
        modsReload.Location = new Point(726, 14);
        modsReload.Size = new Size(110, 28);
        modsReload.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        bar1.Controls.Add(modsReload);
        modsReload.Click += delegate { if (view == ViewMods) RefreshMods(); };

        RoundedPanel modsSearchBox = new RoundedPanel();
        modsSearchBox.Radius = 14;
        modsSearchBox.BackColor = Ui.Card;
        modsSearchBox.Border = Ui.Line;
        modsSearchBox.HoverBorder = Ui.Accent1;
        modsSearchBox.Animated = true;
        modsSearchBox.Location = new Point(SecX, SecY + 126);
        modsSearchBox.Size = new Size(852, 40);
        Controls.Add(modsSearchBox); modsUi.Add(modsSearchBox);

        IconView ico = new IconView();
        ico.Icon = Ico.Search;
        ico.Col = Ui.GrayDim;
        ico.Location = new Point(14, 12);
        ico.Size = new Size(16, 16);
        modsSearchBox.Controls.Add(ico);

        modsSearch = new TextBox();
        modsSearch.Font = Ui.Font(10);
        modsSearch.BackColor = Ui.Card;
        modsSearch.ForeColor = Ui.Gray;
        modsSearch.Text = ModsHint;
        modsSearch.BorderStyle = BorderStyle.None;
        modsSearch.Location = new Point(42, 11);
        modsSearch.Size = new Size(694, 18);
        modsSearch.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        modsSearchBox.Controls.Add(modsSearch);

        Label modsSearchHint = new Label();
        modsSearchHint.Text = "Enter — найти";
        modsSearchHint.Font = Ui.Font(8);
        modsSearchHint.ForeColor = Ui.GrayDim;
        modsSearchHint.BackColor = Color.Transparent;
        modsSearchHint.AutoSize = false;
        modsSearchHint.Location = new Point(736, 12);
        modsSearchHint.Size = new Size(100, 16);
        modsSearchHint.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        modsSearchHint.TextAlign = ContentAlignment.MiddleRight;
        modsSearchBox.Controls.Add(modsSearchHint);

        modsList = new DarkFlowPanel();
        modsList.WrapContents = true;
        modsList.FlowDirection = FlowDirection.LeftToRight;
        modsList.Location = new Point(SecX, SecY + 178);
        modsList.Size = new Size(852, 344);
        modsList.BackColor = Ui.Bg;
        Controls.Add(modsList); modsUi.Add(modsList);

        modsTargetDd.SelectedIndexChanged += delegate { if (view == ViewMods) OnModsTargetChanged(); };
        modsLoaderDd.SelectedIndexChanged += delegate { if (view == ViewMods) OnModsLoaderChanged(); };
        modsSearch.GotFocus += delegate
        {
            if (modsSearch.Text == ModsHint)
            {
                modsSyncUi = true;
                modsSearch.Text = "";
                modsSearch.ForeColor = Ui.Txt;
                modsSyncUi = false;
            }
        };
        modsSearch.LostFocus += delegate
        {
            if (modsSearch.Text.Trim() == "")
            {
                modsSyncUi = true;
                modsSearch.Text = ModsHint;
                modsSearch.ForeColor = Ui.Gray;
                modsSyncUi = false;
            }
        };
        modsSearch.TextChanged += delegate
        {
            if (modsSyncUi) return;
            string fq = IsHint(modsSearch.Text) ? "" : modsSearch.Text.Trim();
            if (fq == "")
            {
                if (modSearchTimer != null) modSearchTimer.Stop();
                ModsSearch("");
                return;
            }
            if (modSearchTimer == null)
            {
                modSearchTimer = new System.Windows.Forms.Timer();
                modSearchTimer.Interval = 450;
                modSearchTimer.Tick += delegate
                {
                    modSearchTimer.Stop();
                    if (view == ViewMods) ModsSearch(ModsSearchText());
                };
            }
            modSearchTimer.Stop();
            modSearchTimer.Start();
        };
        modsSearch.KeyDown += delegate(object s2, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            if (modSearchTimer != null) modSearchTimer.Stop();
            ModsSearch(ModsSearchText());
        };

        // ВАЖНО: здесь НЕ вызываем ApplyView(). Пока форма не показана, любое
        // явное Visible=false на разделе ломает его отрисовку навсегда.
        // Переключением занимается только ShowView (после Show).
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        // Окно не должно схлопываться: MinimumSize не спасает, если размер
        // приходит извне (было client=0x0). Возвращаем рабочий размер.
        try
        {
            if (ClientSize.Width < 600 || ClientSize.Height < 400)
                Size = new Size(1180, 780);
        }
        catch { }
        try
        {
            if (winMaxBtn != null)
            {
                winMaxBtn.Icon = (WindowState == FormWindowState.Maximized) ? Ico.Restore : Ico.Max;
                winMaxBtn.Invalidate();
            }
            // витрина «дышит» только когда видна и окно не свёрнуто
            if (banner != null)
                banner.Animate = (view == ViewHome) && (WindowState != FormWindowState.Minimized);        }
        catch { }
        try { LayoutSections(); } catch { }
        // витрина главного экрана: панели переставляем сразу,
        // содержимое карточек — после паузы (см. QueueHomeRebuild)
        try
        {
            if (banner != null && ClientSize.Width >= 600 && ClientSize.Height >= 400)
            {
                HomeLayout(false);
                QueueHomeRebuild();
            }
        }
        catch { }
        // раскладка экранов подарка (если билд подарочный)
        try { if (artemRoot != null) ArtemLayout(); } catch { }
        try { if (artemScream != null) artemScream.Size = new Size(ClientSize.Width, ClientSize.Height); } catch { }
        ChromeTop();
        // во время живого ресайза по краям залипает невидимая рамка
        // старого размера — стираем её, не трогая потомков окна
        try
        {
            if (IsHandleCreated)
                RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, RDW_INVALIDATE | RDW_FRAME | RDW_ERASE);
        }
        catch { }
    }

    // Раскладка главного экрана по текущему размеру окна.
    // В конструкторе всё считается один раз, поэтому при изменении
    // размера витрина, правая колонка и сетка версий оставались в
    // старых координатах. Здесь те же формулы повторяются заново:
    // rebuild=true дополнительно пересобирает содержимое карточек,
    // потому что внутри них координаты тоже абсолютные.
    // Единый расчёт вертикальных метрик главного экрана. Раньше эти
    // формулы были продублированы в конструкторе и в HomeLayout и уже
    // разошлись (226 против 264), из-за чего «Быстрый запуск»
    // обрезался на низких окнах. Теперь источник один.
    class HomeMetrics
    {
        public int HeroH, PremH, NewsH, QuickH, PopH;
    }

    static HomeMetrics Metrics(int H)
    {
        HomeMetrics m = new HomeMetrics();
        // Витрина сжимается на низких окнах: 226 — просторно,
        // 168 — минимум, при котором заголовок и кнопки не наезжают.
        m.HeroH = H >= 860 ? 226 : (H >= 760 ? 200 : 168);
        // Премиум-карточка — промо-блок, на низком окне поджимается
        m.PremH = H >= 800 ? 116 : 88;

        int heroY = 64 + 14;
        int rightH = H - heroY - 20;
        if (rightH < 300) rightH = 300;

        // «Быстрый запуск»: шапка 40 + 3 строки (мин 36) + зазоры
        // + кнопка 36 + нижнее поле 12
        int quickMin = 40 + 3 * 36 + 8 + 36 + 12;      // 204
        // Новости: шапка 44 + 3 строки (мин 54) + зазоры + поле
        int newsMin = 44 + 3 * 54 + 16 + 12;           // 234
        m.PopH = 110;

        int spare = rightH - 24 - 30;                   // минус зазор и строка статуса
        m.NewsH = Math.Min(372, spare - quickMin - m.PremH - 24);
        if (m.NewsH < newsMin) m.NewsH = Math.Min(372, Math.Max(newsMin, spare - quickMin - m.PremH - 24));
        m.QuickH = spare - m.NewsH - m.PremH - 24;
        if (m.QuickH < quickMin) m.QuickH = quickMin;
        return m;
    }

    void HomeLayout(bool rebuild)
    {
        try
        {
            if (banner == null || rightCol == null) return;
            int W = ClientSize.Width, H = ClientSize.Height;
            if (W < 600 || H < 400) return;

            const int SideW = 232, TopH = 64, Marg = 20, RightW = 292, RightGap = 16;
            int PX = SideW + Marg;
            int PW = W - PX - Marg - RightW - RightGap;
            if (PW < 380) PW = 380;

            int heroY = TopH + 14;
            // высота витрины берётся из общей метрики: на низком окне
            // баннер поджимается, чтобы влезли вкладки и список версий
            HomeMetrics heroM = Metrics(H);
            int heroH = heroM.HeroH;
            int heroLw = (int)(PW * 0.56f);
            if (heroLw < 260) heroLw = 260;

            banner.Location = new Point(PX, heroY);
            banner.Size = new Size(PW, heroH);

            if (lblHeroHi != null)
            {
                lblHeroHi.Location = new Point(26, 14);
                lblHeroHi.Size = new Size(heroLw, 84);
            }
            // На низком баннере (168) трёхстрочный заголовок + подпись
            // + статус + кнопки физически не влезают: подпись и статус
            // прячем, заголовок ужимаем до двух строк, кнопки поднимаем.
            bool compact = heroH < 200;
            if (lblHeroHi != null)
            {
                if (compact)
                {
                    lblHeroHi.Text = "Погрузись\nв бесконечный мир";
                    lblHeroHi.Font = Ui.Font(14, FontStyle.Bold);
                    lblHeroHi.LineH = 20;
                    lblHeroHi.Size = new Size(heroLw, 42);
                    lblHeroHi.Location = new Point(26, 16);
                }
                else
                {
                    lblHeroHi.Text = "Погрузись\nв бесконечный\nмир приключений";
                    lblHeroHi.Font = Ui.Font(17, FontStyle.Bold);
                    lblHeroHi.LineH = 24;
                    lblHeroHi.Size = new Size(heroLw, 84);
                    lblHeroHi.Location = new Point(26, 14);
                }
            }
            if (lblHeroSub != null)
            {
                lblHeroSub.Visible = !compact;
                lblHeroSub.Location = new Point(28, 96);
                lblHeroSub.Size = new Size(heroLw, 34);
            }
            if (dot != null)
            {
                dot.Visible = !compact;
                dot.Location = new Point(28, 140);
            }
            if (lblStatus != null)
            {
                lblStatus.Visible = !compact;
                lblStatus.Location = new Point(45, 135);
                lblStatus.Size = new Size(heroLw, 20);
            }
            if (btnPlay != null) btnPlay.Location = new Point(26, heroH - 54);
            if (cmbVer != null)
            {
                cmbVer.Location = new Point(192, heroH - 52);
                cmbVer.Size = new Size(Math.Max(210, Math.Min(268, heroLw - 192)), 36);
            }
            if (bar != null)
            {
                bar.Location = new Point(26, heroH - 12);
                bar.Size = new Size(PW - 52, 4);
            }
            if (heroDots != null)
            {
                heroDots.Location = new Point(PW - 24 - heroDots.Width, heroH - 21);
            }

            // ===== правая колонка =====
            int rightX = W - Marg - RightW;
            int rightH = H - heroY - Marg;
            if (rightH < 300) rightH = 300;
            rightCol.Location = new Point(rightX, heroY);
            rightCol.Size = new Size(RightW, rightH);

            HomeMetrics hm = Metrics(H);
            int premH = hm.PremH;
            int newsH = hm.NewsH;
            int quickH = hm.QuickH;
            int premY = newsH + 12;
            int quickY = premY + premH + 12;

            newsRightCard.Location = new Point(0, 0);
            newsRightCard.Size = new Size(RightW, newsH);
            premiumCard.Location = new Point(0, premY);
            premiumCard.Size = new Size(RightW, premH);
            quickCard.Location = new Point(0, quickY);
            quickCard.Size = new Size(RightW, quickH);
            if (rightStatusDot != null) rightStatusDot.Location = new Point(8, rightH - 20);
            if (rightStatusLbl != null)
            {
                rightStatusLbl.Location = new Point(24, rightH - 24);
                rightStatusLbl.Size = new Size(RightW - 24, 18);
            }

            // ===== вкладки, заголовок и список версий =====
            int tbY = heroY + heroH + 16;
            int lxx = PX;
            int tabRight = PX + PW;
            // чипы, не влезающие до правого края колонки, прячем:
            // на узком окне последний чип выезжал на правую колонку
            foreach (RoundedButton tb in loaderTabs)
            {
                bool fit = lxx + tb.Width <= tabRight;
                if (tb.Visible != fit) tb.Visible = fit;
                if (!fit) { continue; }
                tb.Location = new Point(lxx, tbY);
                lxx += tb.Width + 8;
            }
            // чипов теперь шесть: если ряд не влезает до ссылки «Все версии»,
            // прячем ссылку, иначе она наезжает на «Кряки»
            if (homeAllVerLink != null)
            {
                // 118 — ширина ссылки, 12 — запас до её левого края
                homeAllVerLink.Visible = lxx + 130 <= tabRight;
                homeAllVerLink.Location = new Point(PX + PW - 118, tbY + 1);
            }

            int listHeadY = tbY + 44;
            if (homeClockIco != null) homeClockIco.Location = new Point(PX + 2, listHeadY + 4);
            if (homeListHead != null) homeListHead.Location = new Point(PX + 26, listHeadY);

            int vbY = listHeadY + 30;
            int popH = 110;
            int vbH = H - vbY - Marg - popH;
            if (vbH < 180) vbH = 180;
            verListBox.Location = new Point(PX, vbY);
            verListBox.Size = new Size(PW, vbH);
            verList.Size = new Size(PW, vbH);
            // Region клипа должен следовать за размером панели,
            // иначе после растяжения окна список рисуется в старых границах
            try { verList.Region = new Region(new Rectangle(Point.Empty, verList.Size)); } catch { }
            popularBlock.Location = new Point(PX, H - Marg - popH + 6);
            popularBlock.Size = new Size(PW, popH - 6);

            if (!rebuild) return;
            // содержимое карточек зависит от их ширины/высоты
            BuildRightNews();
            BuildPremium();
            BuildQuickLaunch();
            BuildPopularMods();
            if (view == ViewHome && verList != null) BuildRowsFiltered(SearchQuery());
            try { ApplyView(); } catch { }
        }
        catch { }
    }

    // Пересборку содержимого откладываем: при перетаскивании рамки
    // OnResize приходит на каждый пиксель, а карточки пересобираются
    // заметно дороже, чем просто переставить панели.
    void QueueHomeRebuild()
    {
        if (homeLayoutTimer == null)
        {
            homeLayoutTimer = new System.Windows.Forms.Timer();
            homeLayoutTimer.Interval = 140;
            homeLayoutTimer.Tick += delegate
            {
                homeLayoutTimer.Stop();
                if (lastHomeW == ClientSize.Width && lastHomeH == ClientSize.Height) return;
                lastHomeW = ClientSize.Width;
                lastHomeH = ClientSize.Height;
                HomeLayout(true);
            };
        }
        homeLayoutTimer.Stop();
        homeLayoutTimer.Start();
    }

    // Раскладка разделов по текущему размеру окна (без Anchor — он тут ломает размеры)
    // Нижняя панель занимает последние 102 px, дальше контент не заходит
    void LayoutSections()
    {
        try
        {
            // вырожденный размер (схлопнувшееся окно) раскладку ломает
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            int w = ClientSize.Width - SecX - 22;
            int h = ClientSize.Height - SecY - 22;
            if (w < 300) w = 300;
            if (h < 160) h = 160;
            if (newsUi != null && newsUi.Count >= 3)
            {
                newsUi[0].Size = new Size(w, h);                  // подложка
                newsList.Location = new Point(SecX, SecY + 64);
                newsList.Size = new Size(w, h - 64);
            }
            if (modsUi != null && modsUi.Count >= 6)
            {
                modsUi[0].Size = new Size(w, h);                  // подложка
                modsUi[3].Width = w;                              // панель сборки/загрузчика
                modsUi[4].Width = w;                              // строка поиска
                modsList.Location = new Point(SecX, SecY + 178);
                modsList.Size = new Size(w, h - 178);
            }
            LayoutSkin(w, h);
            LayoutFriends(w, h);
            LayoutCapes(w, h);
            LayoutNav();
        }
        catch { }
    }

    // Показать активный раздел.
    // Раздел каждый раз строится заново и снимается с формы через
    // Controls.Remove — он УНИЧТОЖАЕТ окна. Ставить Visible=false нельзя:
    // после этого потомки панели навсегда перестают рисоваться (свойства
    // и HWND верные, z-order верный — а экран пустой).
    static void Dbg(string s)
    {
        if (!Cfg.Dbg) return;
        try { File.AppendAllText(Cfg.LogPath("av.log"), s + Environment.NewLine, Encoding.UTF8); } catch { }
    }

    // Плавно вывести активный раздел: сдвиг снизу + проявление
    void AnimateSectionIn(List<Control> ui)
    {
        if (ui == null || ui.Count == 0) return;
        try
        {
            Dictionary<Control, int> home = new Dictionary<Control, int>();
            foreach (Control c in ui) home[c] = c.Top;
            Action tick = null;
            tick = delegate
            {
                try
                {
                    bool done = true;
                    foreach (Control c in ui)
                    {
                        int t = home[c];
                        if (c.Top < t) { c.Top = c.Top + 3; done = false; }
                        else if (c.Top != t) { c.Top = t; }
                    }
                    if (done) Anim.Unsub(tick);
                }
                catch { Anim.Unsub(tick); }
            };
            // стартуем чуть ниже цели
            foreach (Control c in ui) { if (c.Top >= home[c]) c.Top = home[c] + 14; }
            Anim.Sub(tick);
        }
        catch { }
    }

    void ApplyView()
    {
        try
        {
            // главный экран поднимаем над разделами
            if (homeBg != null) homeBg.BringToFront();
            top.BringToFront();
            banner.BringToFront();
            bar.BringToFront();
            foreach (Control c in loaderTabs) c.BringToFront();
            if (allVersionsBtn != null) allVersionsBtn.BringToFront();
            // ссылка «Все версии» не поднималась, поэтому её закрывала
            // фоновая подложка главного экрана — на скриншотах её не было
            if (homeAllVerLink != null) homeAllVerLink.BringToFront();
            if (homeListHead != null) homeListHead.BringToFront();
            if (rightCol != null) rightCol.BringToFront();
            if (popularBlock != null) popularBlock.BringToFront();
            dot.BringToFront();
            lblStatus.BringToFront();
            verListBox.BringToFront();

            // экран подарка должен быть выше всего, пока он не закрыт
            try { if (artemRoot != null) artemRoot.BringToFront(); } catch { }

            // нужный раздел — поверх главного экрана. Схема слоёв:
            // подложку секции (ui[0]) поднимаем последней из НЕактивных
            // секций, но ПЕРВОЙ внутри активной: BringToFront идёт по
            // списку, подложка первая — она осталась бы ниже своих же
            // кнопок. Поэтому активную секцию поднимаем в обратном
            // порядке: контент, затем подложка, затем контент снова —
            // так подложка накрывает чужие кнопки, но своё содержимое
            // остаётся видимым.
            if (view == ViewNews) { foreach (Control c in newsUi) c.BringToFront(); AnimateSectionIn(newsUi); }
            if (view == ViewMods) { foreach (Control c in modsUi) c.BringToFront(); AnimateSectionIn(modsUi); }
            if (view == ViewSkin) { foreach (Control c in skinUi) c.BringToFront(); AnimateSectionIn(skinUi); }
            if (view == ViewFriends) { foreach (Control c in friendsUi) c.BringToFront(); AnimateSectionIn(friendsUi); }
            if (view == ViewCapes) { foreach (Control c in capesUi) c.BringToFront(); AnimateSectionIn(capesUi); }
            // подложка активной секции должна накрывать кнопки ЧУЖИХ
            // секций, оставшиеся ниже её собственных кнопок. После цикла
            // выше верхние слои — контент активной секции в порядке
            // списка, ниже — всё остальное. Поднимаем контент активной
            // секции ещё раз: подложка (поднятая в цикле первой) тогда
            // оказывается между чужими кнопками и своим содержимым.
            if (view == ViewNews && newsUi != null && newsUi.Count > 1) { for (int i = 1; i < newsUi.Count; i++) newsUi[i].BringToFront(); }
            if (view == ViewMods && modsUi != null && modsUi.Count > 1) { for (int i = 1; i < modsUi.Count; i++) modsUi[i].BringToFront(); }
            if (view == ViewSkin && skinUi != null && skinUi.Count > 1) { for (int i = 1; i < skinUi.Count; i++) skinUi[i].BringToFront(); }
            if (view == ViewFriends && friendsUi != null) { for (int i = 1; i < friendsUi.Count; i++) friendsUi[i].BringToFront(); }
            if (view == ViewCapes && capesUi != null) { for (int i = 1; i < capesUi.Count; i++) capesUi[i].BringToFront(); }
            ChromeTop();
            Dbg("AV view=" + view + " kids=" + Controls.Count + " news=" + newsUi.Count + " mods=" + modsUi.Count);
        }
        catch (Exception ex)
        {
            Dbg("AV EX " + ex.Message);
            try { File.AppendAllText(Cfg.LogPath("error.log"),
                "ApplyView: " + ex + Environment.NewLine, Encoding.UTF8); } catch { }
        }
    }

    void PulseNav(NavButton b)
    {
        b.SetSelected(true);
        b.Invalidate();
        Thread t = new Thread((ThreadStart)delegate
        {
            Thread.Sleep(600);
            try
            {
                if (b.InvokeRequired) b.Invoke(new Action(delegate { b.SetSelected(false); }));
                else b.SetSelected(false);
            }
            catch { }
        });
        t.IsBackground = true;
        t.Start();
    }

    void OnAccount(object s, EventArgs e)
    {
        ShowAccount();
    }

    void OnSettings(object s, EventArgs e)
    {
        ShowSettings();
    }

    void ShowAccount()
    {
        accNick.Text = nick;
        UpdateLicUi();
        dimPanel.Visible = true;
        dimPanel.BringToFront();
        accCard.Visible = true;
        accCard.BringToFront();
        accNick.Focus();
    }

    // Строка лицензии на карточке аккаунта
    void UpdateLicUi()
    {
        try
        {
            if (accLic == null) return;
            accLic.Text = Auth.LoggedIn ? (Auth.Name + "  •  лицензия") : "не выполнен вход — игра офлайн";
            accLic.ForeColor = Auth.LoggedIn ? Ui.Green : Ui.GrayDim;
            if (accLicBtn != null) accLicBtn.Text = Auth.LoggedIn ? "Выйти из аккаунта" : "Войти через Microsoft";
        }
        catch { }
    }

    // Обновление надписей из фонового потока входа
    void Ui_(Action a)
    {
        try
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) BeginInvoke(a);
            else a();
        }
        catch { }
    }

    void ShowAuth()
    {
        if (Auth.LoggedIn)
        {
            Auth.Logout();
            UpdateLicUi();
            RefreshProfile();
            SetStatus("Вышел из аккаунта — игра снова офлайн", true);
            return;
        }
        if (Auth.ClientId.Length == 0)
        {
            ShowError("Вход недоступен: не удалось определить client id. " +
                      "Встроенный не подхватился — положи clientid.txt рядом с лаунчером.");
            return;
        }
        authUri = "";
        authCode.Text = "";
        authStep.Text = "Запрашиваю код у Microsoft...";
        dimPanel.Visible = true;
        dimPanel.BringToFront();
        authCard.Visible = true;
        authCard.BringToFront();

        Auth.LoginAsync(
            delegate(string s) { Ui_(delegate { authStep.Text = s; }); },
            delegate(string code, string uri)
            {
                Ui_(delegate
                {
                    authCode.Text = code;
                    authUri = uri;
                    Auth.OpenBrowser(uri);
                });
            },
            delegate(bool ok, string msg)
            {
                Ui_(delegate
                {
                    if (ok)
                    {
                        authStep.Text = msg;
                        HideOverlays();
                        UpdateLicUi();
                        RefreshProfile();
                        SetStatus(msg, true);
                        // сервису тоже нужен новый вход: с лицензией меняется uuid
                        try { SocialRelogin(); } catch { }
                        // скины и плащи подтягиваются отдельно — их видно в разделе скина
                        Auth.RefreshProfileAsync(delegate(bool ok2, string m2)
                        {
                            Ui_(delegate { if (ok2) { UpdateLicUi(); RefreshProfile(); } });
                        });
                    }
                    else
                    {
                        authStep.Text = msg;
                        authCode.Text = "";
                        SetStatus(msg, false);
                    }
                });
            });
    }

    void ShowSettings()
    {
        if (setCard == null) return;
        PlaceSetCard();
        SetTab(setTabIdx);
        dimPanel.Visible = true;
        dimPanel.BringToFront();
        setCard.Visible = true;
        setCard.BringToFront();
    }

    void HideOverlays()
    {
        accCard.Visible = false;
        if (authCard != null) authCard.Visible = false;
        setCard.Visible = false;
        if (aiCard != null) aiCard.Visible = false;
        if (aiSetup != null) aiSetup.Visible = false;
        updateCard.Visible = false;
        dimPanel.Visible = false;
    }

    void SaveAccount()
    {
        if (!ApplyNickname(accNick.Text)) return;
        HideOverlays();
    }

    // Одна точка смены ника: и карточка «Аккаунт», и вкладка настроек.
    // Раньше логика дублировалась, и правки расходились между экранами.
    bool ApplyNickname(string n)
    {
        n = (n ?? "").Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(n, "^[a-zA-Z0-9_]{3,16}$"))
        {
            ShowError("Ник: 3-16 символов a-z, 0-9, _");
            return false;
        }
        nick = n;
        RefreshProfile();
        banner.Tag = nick;
        banner.Invalidate();
        Mc.SaveConfig(nick, curVer(), ram, closeOnLaunch);
        // сервис привязан к нику: после смены заходим заново,
        // иначе скины и плащи остаются от прежнего профиля
        try { SocialRelogin(); } catch { }
        SetStatus("Ник: " + nick, true);
        return true;
    }

    void CheckUpdates(bool manual)
    {
        try
        {
            // подарочный билд не обновляется сам - он личный подарок
            if (Cfg.GiftBuild)
            {
                if (manual)
                {
                    SetStatus("Это подарочная сборка, обновлений нет", true);
                    UpdState("Это подарочная сборка: обновления для неё отключены.", Ui.GrayDim);
                }
                return;
            }
            string url = Cfg.UpdateUrl();
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 12000;
            req.UserAgent = "LuxeLauncher";
            string txt;
            using (WebResponse resp = req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                txt = sr.ReadToEnd();
            Dictionary<string, object> js = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(txt);
            object vo;
            string remote = (js.TryGetValue("version", out vo) && vo != null) ? vo.ToString() : "";
            if (string.IsNullOrEmpty(remote) || Mc.CompareVersions(remote, Cfg.AppVersion) <= 0)
            {
                // Раньше итог уходил только в нижнюю строку статуса, а её
                // перекрывает карточка настроек — игрок видел «ничего не
                // произошло». Пишем результат прямо на вкладке.
                if (manual)
                {
                    SetStatus("У тебя последняя версия (" + Cfg.AppVersion + ")", true);
                    UpdState("У вас уже установлена последняя версия — " + Cfg.AppVersion +
                             ". Обновлять нечего.", Ui.Green);
                }
                return;
            }
            object uo;
            object no;
            object mo;
            object so;
            string durl = (js.TryGetValue("url", out uo) && uo != null) ? uo.ToString() : "";
            string notes = (js.TryGetValue("notes", out no) && no != null) ? no.ToString() : "";
            bool mand = (js.TryGetValue("mandatory", out mo) && mo != null && mo.ToString().ToLower() == "true");
            long size = 0;
            if (js.TryGetValue("size", out so) && so != null) { try { size = Convert.ToInt64(so); } catch { } }
            if (string.IsNullOrEmpty(durl))
            {
                if (manual)
                {
                    ShowError("В update.json нет ссылки (url)");
                    UpdState("В манифесте обновления нет ссылки — обновиться нельзя.", Ui.Orange);
                }
                return;
            }
            if (manual)
                UpdState("Доступно обновление " + remote + " — открой карточку обновления.", Ui.Orange);
            ShowUpdate(remote, durl, notes, mand, size);
        }
        catch (Exception ex)
        {
            if (manual)
            {
                ShowError("Не проверилось: " + ex.Message);
                UpdState("Не удалось проверить обновления: " + ex.Message, Ui.Orange);
            }
        }
        finally
        {
            // кнопка возвращается в строй при любом исходе проверки
            if (manual) UpdBusy(false);
        }
    }

    void ShowUpdate(string ver, string url, string notes, bool mand, long size)
    {
        if (InvokeRequired)
        {
            Invoke(new Action<string, string, string, bool, long>(ShowUpdate), ver, url, notes, mand, size);
            return;
        }
        updVer = ver;
        updUrl = url;
        updSize = size;
        updateTitle.Text = "Доступно обновление " + ver;
        updateNotes.Text = string.IsNullOrEmpty(notes) ? ("Текущая версия: " + Cfg.AppVersion) : notes;
        btnUpdateLater.Visible = !mand;
        btnUpdateGo.Enabled = true;
        dimPanel.Visible = true;
        dimPanel.BringToFront();
        updateCard.Visible = true;
        updateCard.BringToFront();
    }

    void DownloadAndApply()
    {
        btnUpdateGo.Enabled = false;
        Thread t = new Thread((ThreadStart)delegate
        {
            try
            {
                string exePath = Application.ExecutablePath;
                string exeName = Path.GetFileName(exePath);
                string newName = Path.GetFileNameWithoutExtension(exeName) + ".new.exe";
                string newPath = Path.Combine(Cfg.DataDir, newName);
                SetStatus("Качаю обновление " + updVer + "...", true);
                int tries = 0;
                while (true)
                {
                    tries++;
                    try { if (File.Exists(newPath)) File.Delete(newPath); } catch { }
                    if (tries > 1) SetStatus("Качаю обновление " + updVer + " (попытка " + tries + ")...", true);
                    Mc.DownloadWithProgress(updUrl, newPath, "Обновление " + updVer);
                    FileInfo fi = new FileInfo(newPath);
                    bool ok = fi.Exists && fi.Length >= 10240 && (updSize <= 0 || fi.Length == updSize);
                    if (ok) break;
                    if (tries >= 3)
                        throw new Exception("файл обновления битый (скачалось " + (fi.Exists ? fi.Length.ToString() : "0") + " из " + updSize + ")");
                    Thread.Sleep(1500);
                }
                StringBuilder bb = new StringBuilder();
                bb.AppendLine("@echo off");
                bb.AppendLine("timeout /t 3 /nobreak >nul");
                bb.AppendLine(":retry");
                // АБСОЛЮТНЫЕ пути: раньше были относительные, .bat работал
                // в папке данных — новый exe лёг рядом с данными, а
                // настоящий файл не заменялся.
                bb.AppendLine("move /Y \"" + newPath + "\" \"" + exePath + "\" >nul 2>&1");
                bb.AppendLine("if errorlevel 1 ( timeout /t 2 /nobreak >nul & goto retry )");
                bb.AppendLine("start \"\" \"" + exePath + "\"");
                bb.AppendLine("(goto) 2>nul & del \"%~f0\"");
                SetStatus("Обновление скачано, перезапускаюсь...", true);
                Os.StartApplyScript(newPath, exePath, Cfg.DataDir);
                Thread.Sleep(800);
                if (InvokeRequired) Invoke(new Action(delegate { Application.Exit(); }));
                else Application.Exit();
            }
            catch (Exception ex)
            {
                ShowError("Не обновилось: " + ex.Message);
                try
                {
                    if (btnUpdateGo.InvokeRequired)
                        btnUpdateGo.Invoke(new Action(delegate { btnUpdateGo.Enabled = true; }));
                    else
                        btnUpdateGo.Enabled = true;
                }
                catch { }
            }
        });
        t.IsBackground = true;
        t.Start();
    }

    public void SaveNewsShot(string path)
    {
        if (newsList == null) return;
        using (Bitmap b = new Bitmap(newsList.Width, newsList.Height))
        {
            newsList.DrawToBitmap(b, new Rectangle(0, 0, newsList.Width, newsList.Height));
            b.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }

    public void DebugClickNav(int v)
    {
        if (InvokeRequired) { Invoke(new Action<int>(DebugClickNav), v); return; }
        NavButton b = v == ViewNews ? navNews
                     : v == ViewMods ? navMods
                     : v == ViewSkin ? navSkin
                     : v == ViewFriends ? navFriends
                     : v == ViewCapes ? navCapes : navHome;
        if (b != null) b.PerformClick();
        else SetStatus("NAV-FAIL: кнопки нет", false);
    }

    public int DebugGetView()
    {
        return view;
    }

    // Дамп дерева контролов: помогает понять, что реально лежит поверх формы
    // Прокрутить активный список в низ/верх (проверка скролла)
    // target: -1 верх, 1 низ, 0 середина
    public void DebugScrollActive(int target)
    {
        try
        {
            // Берём ВИДИМЫЙ список с самым большим содержимым: в форме
            // живут панели всех разделов, скрытые тоже Visible=true на
            // уровне контрола, но их родитель скрыт — проверяем предков.
            DarkFlowPanel best = null;
            int bestMax = 0;
            foreach (Control c in Controls)
            {
                DarkFlowPanel p = c as DarkFlowPanel;
                if (p == null || !VisibleDeep(p)) continue;
                int m = p.DebugMaxScroll();
                if (m > bestMax) { bestMax = m; best = p; }
            }
            if (best == null)
            {
                File.AppendAllText(Path.Combine(Cfg.BaseDir, "shot_args.txt"),
                    "\r\nSCROLL: нет видимого списка\r\n", Encoding.UTF8);
                return;
            }
            int max = best.DebugMaxScroll();
            int y = target < 0 ? 0 : (target == 0 ? max / 2 : max);
            best.DebugScrollTo(y);
            File.AppendAllText(Path.Combine(Cfg.BaseDir, "shot_args.txt"),
                "\r\nSCROLL panel=" + best.Bounds + " max=" + max + " set=" + y +
                " real=" + best.DebugScrollPos() + "\r\n", Encoding.UTF8);
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(Path.Combine(Cfg.BaseDir, "shot_args.txt"), "\r\nSCROLL-ERR " + ex.Message, Encoding.UTF8); } catch { }
        }
    }

    static bool VisibleDeep(Control c)
    {
        while (c != null)
        {
            if (!c.Visible) return false;
            c = c.Parent;
        }
        return true;
    }

    public void DebugDumpControls(string path)
    {
        StringBuilder sb = new StringBuilder();
        try
        {
            Action<Control, int> walk = null;
            walk = delegate(Control c, int d)
            {
                try
                {
                    string txt = "";
                    try { if (c is Label) txt = ((Label)c).Text; } catch { }
                    try { if (c is ButtonBase) txt = ((ButtonBase)c).Text; } catch { }
                    if (txt.Length > 40) txt = txt.Substring(0, 40);
                    sb.Append(new string(' ', d * 2)).Append(c.GetType().Name)
                      .Append(" [").Append(c.Name).Append("] ").Append(c.Bounds.ToString())
                      .Append(" vis=").Append(c.Visible)
                      .Append(" hwnd=").Append(c.IsHandleCreated ? c.Handle.ToString() : "NONE")
                      .Append(" par=").Append(c.Parent == null ? "null" : c.Parent.GetType().Name)
                      .Append(txt.Length > 0 ? "  '" + txt + "'" : "")
                      .AppendLine();
                    foreach (Control ch in c.Controls) walk(ch, d + 1);
                }
                catch { }
            };
            walk(this, 0);
            // ищем самый глубокий видимый контрол под центром формы
            Point cpt = new Point(ClientSize.Width / 2, ClientSize.Height / 2);
            Control at = null;
            try
            {
                Action<Control> probe = null;
                probe = delegate(Control c)
                {
                    foreach (Control ch in c.Controls)
                    {
                        try
                        {
                            if (ch.Visible && ch.Bounds.Contains(cpt))
                            {
                                if (at == null || ch.Width * ch.Height <= at.Width * at.Height) at = ch;
                                probe(ch);
                            }
                        }
                        catch { }
                    }
                };
                probe(this);
            }
            catch { }
            sb.AppendLine("FROM-POINT(center) = " + (at == null ? "null" : at.GetType().Name + " '" + at.Name + "' " + at.Bounds));
            try
            {
                sb.AppendLine("OPENFORMS = " + Application.OpenForms.Count);
                foreach (Form fm in Application.OpenForms)
                {
                    try { sb.AppendLine("  FORM " + fm.GetType().Name + " text='" + fm.Text + "' " + fm.Bounds
                        + " vis=" + fm.Visible + " top=" + fm.TopMost + " kids=" + fm.Controls.Count); }
                    catch { }
                }
            }
            catch { }
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }
        catch { }
    }

    public string GetNewsDiag()
    {
        try
        {
            return "view=" + view + " homeTab=" + homeTab + " loaderTab=" + loaderTab
               + " newsKids=" + newsUi.Count + " newsItems=" + (newsList == null ? -1 : newsList.Controls.Count)
               + " modsKids=" + modsUi.Count + " modCards=" + (modsList == null ? -1 : modsList.Controls.Count)
               + " modItems=" + modItems.Count + " target=" + modsTarget
               + " mc=" + modsMcVer + " loader=" + modsLoader
               + " skinKids=" + skinUi.Count + " frKids=" + friendsUi.Count + " cpKids=" + capesUi.Count
               + " zFr=" + Zof(friendsUi) + " zCp=" + Zof(capesUi) + " zSkin=" + Zof(skinUi) + " zMods=" + Zof(modsUi)
               + " vFr=" + Vof(friendsUi) + " vCp=" + Vof(capesUi) + " vMods=" + Vof(modsUi)
               + " dim=" + (dimPanel == null ? "нет" : (dimPanel.Visible ? "виден" : "скрыт"))
               + " kids=" + Controls.Count
               + " client=" + ClientSize.Width + "x" + ClientSize.Height
               + " probe=" + ProbePoint(1204, 196) + " / " + ProbePoint(1070, 160);
        }
        catch (Exception ex) { return "DIAG-ERR " + ex.Message; }
    }

    // Кто реально лежит в точке (по z-порядку сверху вниз) и на каком слое.
    // Нужно, чтобы не гадать, откуда берётся артефакт в углу кнопки.
    string ProbePoint(int x, int y)
    {
        try
        {
            StringBuilder sb = new StringBuilder();
            foreach (Control c in Controls)
            {
                try
                {
                    // берём не только точное попадание: контрол может
                    // рисовать ЗА своими границами (свечение, тень)
                    Rectangle b = c.Bounds;
                    Rectangle near = new Rectangle(b.X - 24, b.Y - 24, b.Width + 48, b.Height + 48);
                    if (!c.Visible || !near.Contains(x, y)) continue;
                    string txt = "";
                    try { txt = c.Text; } catch { }
                    if (txt != null && txt.Length > 12) txt = txt.Substring(0, 12);
                    sb.Append(" [z").Append(Controls.GetChildIndex(c)).Append(' ')
                      .Append(c.GetType().Name).Append(' ').Append(b.Size)
                      .Append(b.Contains(x, y) ? "" : "~near")
                      .Append(txt != null && txt.Length > 0 ? " '" + txt + "'" : "").Append(']');
                }
                catch { }
            }
            return sb.Length == 0 ? "пусто" : sb.ToString();
        }
        catch (Exception ex) { return "probe-err " + ex.Message; }
    }

    // Слой контрола: 0 — самый верхний. -1 — контрола нет на форме
    int Zof(List<Control> ui)
    {
        try
        {
            if (ui == null || ui.Count == 0) return -1;
            return Controls.GetChildIndex(ui[0]);
        }
        catch { return -2; }
    }

    // Видимость и размеры первого контрола раздела
    string Vof(List<Control> ui)
    {
        try
        {
            if (ui == null || ui.Count == 0) return "нет";
            Control c = ui[0];
            return (c.Visible ? "в" : "х") + c.Width + "x" + c.Height + "@" + c.Left + "," + c.Top;
        }
        catch { return "err"; }
    }

    public void ShowView(int v)
    {
        if (view == v) return;
        try { BuildSectionViews(); } catch { }
        view = v;
        if (navHome != null) navHome.SetSelected(v == ViewHome);
        if (navNews != null) navNews.SetSelected(v == ViewNews);
        if (navMods != null) navMods.SetSelected(v == ViewMods);
        if (navSkin != null) navSkin.SetSelected(v == ViewSkin);
        if (navFriends != null) navFriends.SetSelected(v == ViewFriends);
        if (navCapes != null) navCapes.SetSelected(v == ViewCapes);
        try
        {
            string vname = v == ViewNews ? "Новости"
                         : v == ViewMods ? "Моды"
                         : v == ViewSkin ? "Скин"
                         : v == ViewFriends ? "Друзья"
                         : v == ViewCapes ? "Плащи" : "Главная";
            Text = "Luxe Launcher " + Cfg.VersionLabel + " - " + vname;
            if (pageTitle != null) pageTitle.Text = vname;
        }
        catch { }

        ApplyView();
        if (v == ViewNews) BuildNews();
        if (v == ViewMods) RefreshMods();
        if (v == ViewSkin) SkinRefresh();
        if (v == ViewFriends) { SocialState(); FriendsLoad(); SignalTick(); }
        if (v == ViewCapes) CapesLoad(false);
    }


    void BuildNews()
    {
        newsList.Controls.Clear();
        int w = 830;
        try
        {
            w = newsList.ClientSize.Width - 30;
            if (w < 400) w = 400;
        }
        catch { }
        foreach (NewsItem n in NewsData.All)
            newsList.Controls.Add(MakeNewsCard(n, w));
        newsList.ResetScroll();
        try { if (!newsList.Visible) newsList.Visible = true; } catch { }
    }

    RoundedPanel MakeNewsCard(NewsItem n, int w)
    {
        bool current = n.ver == Cfg.AppVersion;
        int lh = 22;
        RoundedPanel p = new RoundedPanel();
        p.Radius = 16;
        p.BackColor = Ui.Card;
        p.Border = current ? Ui.Green : Ui.Line;
        p.Size = new Size(w, 62 + n.lines.Length * lh);
        p.Margin = new Padding(0, 0, 0, 12);

        // у актуальной версии слева зелёная полоска — видно с одного взгляда
        if (current)
        {
            Panel mark = new Panel();
            mark.BackColor = Ui.Green;
            mark.Location = new Point(0, 14);
            mark.Size = new Size(3, p.Height - 28);
            p.Controls.Add(mark);
        }

        Label lv = new Label();
        lv.Text = "v" + n.ver;
        lv.Font = Ui.Font(10.5f, FontStyle.Bold);
        lv.ForeColor = current ? Ui.Green : Ui.Accent1;
        lv.BackColor = Color.Transparent;
        lv.AutoSize = false;
        lv.Location = new Point(18, 16);
        lv.Size = new Size(64, 22);
        p.Controls.Add(lv);

        PixelLabel lt = new PixelLabel();
        lt.Text = n.title;
        lt.Font = Ui.Font(11.5f, FontStyle.Bold);
        lt.ForeColor = Ui.Txt;
        lt.BackColor = Color.Transparent;
        lt.AutoSize = false;
        // у актуальной версии справа висит «у тебя стоит», поэтому строка
        // заголовка короче; длинные заголовки режем многоточием, а не молча
        lt.AutoEllipsis = true;
        lt.Location = new Point(88, 15);
        lt.Size = new Size(w - (current ? 228 : 110), 24);
        p.Controls.Add(lt);

        if (current)
        {
            Label now = new Label();
            now.Text = "●  у тебя стоит";
            now.Font = Ui.Font(7.5f, FontStyle.Bold);
            now.ForeColor = Ui.Green;
            now.BackColor = Color.Transparent;
            now.AutoSize = false;
            now.Location = new Point(w - 130, 18);
            now.Size = new Size(112, 18);
            now.TextAlign = ContentAlignment.MiddleRight;
            now.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            p.Controls.Add(now);
        }

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < n.lines.Length; i++)
        {
            if (i > 0) sb.Append("\r\n");
            sb.Append("•   ");
            sb.Append(n.lines[i]);
        }
        Label lb = new Label();
        lb.Text = sb.ToString();
        lb.Font = Ui.Font(9);
        lb.ForeColor = Ui.Gray;
        lb.BackColor = Color.Transparent;
        lb.AutoSize = false;
        lb.Location = new Point(18, 44);
        lb.Size = new Size(w - 36, n.lines.Length * lh);
        lb.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        p.Controls.Add(lb);

        return p;
    }

    // ===== Правая колонка главного экрана =====
    // Новости: шапка со ссылкой и три миниатюры с датами.
    void BuildRightNews()
    {
        if (newsRightCard == null) return;
        newsRightCard.Controls.Clear();
        int w = newsRightCard.Width > 0 ? newsRightCard.Width : 292;

        // иконка в фиолетовой плитке — как в референсе
        RoundedPanel ntile = new RoundedPanel();
        ntile.Radius = 7;
        ntile.Gradient = true;
        ntile.Glass = false;
        ntile.BorderW = 0;
        ntile.GradFrom = Color.FromArgb(124, 92, 255);
        ntile.GradTo = Color.FromArgb(77, 92, 255);
        ntile.Location = new Point(14, 12);
        ntile.Size = new Size(26, 26);
        newsRightCard.Controls.Add(ntile);
        IconView ni = new IconView();
        ni.Icon = Ico.News;
        ni.Col = Color.White;
        ni.Location = new Point(5, 5);
        ni.Size = new Size(16, 16);
        ntile.Controls.Add(ni);

        PixelLabel h = new PixelLabel();
        h.Text = "Новости";
        h.Font = Ui.Font(11, FontStyle.Bold);
        h.ForeColor = Ui.Txt;
        h.BackColor = Color.Transparent;
        h.AutoSize = false;
        // 118, а не 140: прозрачный контрол стирает соседей под собой,
        // при 140 перекрывалась ссылка «Все новости»
        h.Location = new Point(48, 13);
        h.Size = new Size(118, 24);
        newsRightCard.Controls.Add(h);

        RoundedButton all = new RoundedButton();
        all.Text = "Все новости  →";
        all.Font = Ui.Font(8, FontStyle.Bold);
        all.ForeColor = Ui.Accent1;
        all.Flat = true;
        all.BorderW = 0;
        all.Top = Color.Transparent;
        all.Bottom = Color.Transparent;
        all.HoverTop = Ui.SurfaceHi;
        all.HoverBottom = Ui.Surface;
        all.Size = new Size(112, 24);
        all.Location = new Point(w - 124, 11);
        all.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        all.Click += delegate { ShowView(ViewNews); };
        all.Visible = false;
        newsRightCard.Controls.Add(all);

        TextLink allNews = new TextLink();
        allNews.Text = "Все новости";
        allNews.Fill = Ui.Card;
        allNews.HoverFill = Ui.SurfaceHi;
        allNews.Font = Ui.Font(8, FontStyle.Bold);
        allNews.Size = new Size(104, 26);
        allNews.Location = new Point(w - 116, 10);
        allNews.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        allNews.Click += delegate { ShowView(ViewNews); };
        newsRightCard.Controls.Add(allNews);

        // три последних новости; картинка-миниатюра — процедурный пейзаж
        string[] dates = new string[] { "16.04.2025", "01.04.2025", "28.03.2025" };
        int n = Math.Min(3, NewsData.All.Length);
        // высота строки зависит от высоты карточки: на низком окне
        // три новости должны уместиться целиком, а не обрезаться снизу.
        // Минимум 54px: заголовок 28 + дата внизу 20, описание прячем.
        int rowsTop = 44;
        int availH = newsRightCard.Height - rowsTop - 12;
        int rowH = (availH - (n - 1) * 8) / Math.Max(1, n);
        if (rowH > 96) rowH = 96;
        if (rowH < 54) rowH = 54;
        int thumbS = Math.Min(64, rowH - 18);
        int y = rowsTop;
        for (int i = 0; i < n; i++)
        {
            NewsItem it = NewsData.All[i];
            RoundedPanel row = new RoundedPanel();
            row.Radius = 10;
            row.BackColor = Ui.Card;
            row.Border = Ui.Line;
            row.HoverBack = Ui.SurfaceHi;
            row.HoverBorder = Ui.Accent1;
            row.Animated = true;
            row.Glass = false;
            row.Location = new Point(12, y);
            row.Size = new Size(w - 24, rowH);
            row.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            row.Cursor = Cursors.Hand;
            newsRightCard.Controls.Add(row);

            float hue = Ui.BrandHue(250f + i * 26f);
            int seed = i + 3;
            RoundedPanel thumb = new RoundedPanel();
            thumb.Radius = 8;
            thumb.BackColor = Ui.SurfaceHi;
            thumb.Border = Ui.Line;
            thumb.Glass = false;
            thumb.Location = new Point(8, (rowH - thumbS) / 2);
            thumb.Size = new Size(thumbS, thumbS);
            thumb.Deco = delegate(Graphics g, Rectangle rr)
            {
                // миниатюра — фото-кадр (набор чередуется по индексу
                // новости, чтобы лента не выглядела копиями). Если
                // ресурсов нет — прежний векторный пейзаж из кэша.
                string[] shots = new string[] { "p_news", "p_news2", "p_news3" };
                string pick = shots[Math.Abs(seed) % shots.Length];
                if (!Art.PhotoFill(g, rr, pick, 0.5f, 60, 0))
                {
                    Bitmap bmp = Art.LandscapeCached(rr.Width, rr.Height, hue, seed);
                    if (bmp != null) g.DrawImageUnscaled(bmp, rr.X, rr.Y);
                    else Art.Landscape(g, rr, hue, seed);
                }
            };
            row.Controls.Add(thumb);

            int textX = thumbS + 16;
            int textW = w - 24 - textX - 30;
            PixelLabel t = new PixelLabel();
            t.Text = it.title;
            t.Font = Ui.Font(8.5f, FontStyle.Bold);
            t.ForeColor = Ui.Txt;
            t.BackColor = Color.Transparent;
            t.AutoSize = false;
            t.AutoEllipsis = true;
            t.LineH = 13;
            t.Location = new Point(textX, 7);
            t.Size = new Size(textW, 28);
            row.Controls.Add(t);

            Label d = new Label();
            d.Text = it.lines.Length > 0 ? it.lines[0] : "";
            d.Font = Ui.Font(7.5f);
            d.ForeColor = Ui.GrayDim;
            d.BackColor = Color.Transparent;
            d.AutoSize = false;
            d.AutoEllipsis = true;
            d.Location = new Point(textX, 36);
            d.Size = new Size(textW, Math.Max(14, rowH - 58));
            row.Controls.Add(d);

            Label dt = new Label();
            dt.Text = dates[i % dates.Length];
            dt.Font = Ui.Font(7);
            dt.ForeColor = Ui.GrayDim;
            dt.BackColor = Color.Transparent;
            dt.AutoSize = false;
            dt.Location = new Point(textX, rowH - 20);
            dt.Size = new Size(120, 14);
            row.Controls.Add(dt);

            IconView ch = new IconView();
            ch.Icon = Ico.ChevRight;
            ch.Col = Ui.GrayDim;
            ch.Location = new Point(w - 40, (rowH - 14) / 2);
            ch.Size = new Size(14, 14);
            ch.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            row.Controls.Add(ch);

            row.Click += delegate { ShowView(ViewNews); };
            thumb.Click += delegate { ShowView(ViewNews); };
            t.Click += delegate { ShowView(ViewNews); };
            y += rowH + 8;
        }
    }

    // «Премиум — больше возможностей» с короной на фоне
    void BuildPremium()
    {
        if (premiumCard == null) return;
        premiumCard.Controls.Clear();
        int w = premiumCard.Width > 0 ? premiumCard.Width : 292;
        int h = premiumCard.Height > 0 ? premiumCard.Height : 116;
        // на низком окне карточка сжата до 88: описание и кнопку
        // оставляем, но поднимаем их вплотную
        bool compact = h < 104;

        IconView pi = new IconView();
        pi.Icon = Ico.Star;
        pi.Col = Color.FromArgb(255, 214, 140);
        pi.Location = new Point(16, 14);
        pi.Size = new Size(17, 17);
        premiumCard.Controls.Add(pi);

        PixelLabel h2 = new PixelLabel();
        h2.Text = "Премиум — больше возможностей";
        h2.Font = Ui.Font(9.5f, FontStyle.Bold);
        h2.ForeColor = Ui.Txt;
        h2.BackColor = Color.Transparent;
        h2.AutoSize = false;
        h2.Location = new Point(40, 8);
        h2.Size = new Size(w - 96, 34);
        premiumCard.Controls.Add(h2);

        Label d = new Label();
        d.Text = compact ? "Скины, плащи и приоритет" : "Скины, плащи, приоритет\r\nи многое другое!";
        d.Font = Ui.Font(8);
        d.ForeColor = Color.FromArgb(206, 200, 240);
        d.BackColor = Color.Transparent;
        d.AutoSize = false;
        d.Location = new Point(16, compact ? 38 : 44);
        d.Size = new Size(w - 32, compact ? 18 : 30);
        premiumCard.Controls.Add(d);

        RoundedButton more = RoundedButton.Dark("Узнать больше", 8);
        more.Font = Ui.Font(8, FontStyle.Bold);
        more.Size = new Size(148, 26);
        more.Location = new Point(16, compact ? h - 34 : 78);
        more.Click += delegate { SetStatus("Премиум появится позже — следи за новостями", true); };
        premiumCard.Controls.Add(more);

        // Корона и искры — прямо в декор карточки: отдельная панель внутри
        // скруглённой карточки рисовалась обрезанной. В референсе корона
        // фиолетовая со свечением и вокруг летают искры.
        premiumCard.Deco = delegate(Graphics g, Rectangle rr)
        {
            Rectangle cr = new Rectangle(rr.Right - 74, rr.Y + 26, 54, 50);
            // свечение под короной
            try
            {
                using (GraphicsPath gp = new GraphicsPath())
                {
                    gp.AddEllipse(cr.X - 14, cr.Y - 10, cr.Width + 28, cr.Height + 24);
                    using (PathGradientBrush pg = new PathGradientBrush(gp))
                    {
                        pg.CenterColor = Color.FromArgb(70, 124, 92, 255);
                        pg.SurroundColors = new Color[] { Color.FromArgb(0, 124, 92, 255) };
                        g.FillPath(pg, gp);
                    }
                }
            }
            catch { }
            Art.Crown(g, cr, Color.FromArgb(214, 152, 128, 255));
            // маленькая искра у короны
            Icons.Draw(g, Ico.Sparkle, new Rectangle(rr.Right - 88, rr.Y + 64, 7, 7), Color.FromArgb(120, 190, 170, 255));
        };
    }

    // «Быстрый запуск»: три строки-перехода
    void BuildQuickLaunch()
    {
        if (quickCard == null) return;
        quickCard.Controls.Clear();
        int w = quickCard.Width > 0 ? quickCard.Width : 292;

        IconView zi = new IconView();
        zi.Icon = Ico.Zap;
        zi.Col = Ui.Accent1;
        zi.Location = new Point(16, 14);
        zi.Size = new Size(17, 17);
        quickCard.Controls.Add(zi);

        PixelLabel h = new PixelLabel();
        h.Text = "Быстрый запуск";
        h.Font = Ui.Font(10, FontStyle.Bold);
        h.ForeColor = Ui.Txt;
        h.BackColor = Color.Transparent;
        h.AutoSize = false;
        h.Location = new Point(40, 11);
        h.Size = new Size(180, 22);
        quickCard.Controls.Add(h);

        string[] caps = new string[] { "Последняя версия", "Fabric-версии", "Сервера" };
        string[] subs = new string[] { LatestReleaseName(), "Оптимизация и моды", "Найти сервер" };
        // Строки делят свободное место. Важно: rowH считается от
        // фактически доступной высоты, а кнопка ставится строго ПОСЛЕ
        // последней строки. Раньше rowH поднимался до минимума 38, но
        // кнопка при этом считалась от innerBot — на низком окне она
        // наезжала на третью строку.
        int innerTop = 40;
        int addH = 36;
        int gap = 4;
        int bottomPad = 12;
        int avail = quickCard.Height - innerTop - bottomPad - addH - 6;
        int rowH = (avail - gap * 2) / 3;
        if (rowH > 56) rowH = 56;
        if (rowH < 34) rowH = 34;
        int y = innerTop;
        for (int i = 0; i < 3; i++)
        {
            RoundedPanel row = new RoundedPanel();
            row.Radius = 10;
            row.BackColor = Ui.Card;
            row.Border = Ui.Line;
            row.HoverBack = Ui.SurfaceHi;
            row.HoverBorder = Ui.Accent1;
            row.Animated = true;
            row.Glass = false;
            row.Location = new Point(12, y);
            row.Size = new Size(w - 24, rowH);
            row.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            row.Cursor = Cursors.Hand;
            quickCard.Controls.Add(row);

            int mid = (rowH - 30) / 2;
            // на сжатой строке (34px) плитка 30px и две строки текста
            // уже не помещаются: уменьшаем плитку и прячем подпись
            bool tight = rowH < 42;
            int tileS = tight ? 24 : 30;

            // цветной значок слева: куб / булыжник / сервер
            RoundedPanel tile = new RoundedPanel();
            tile.Radius = 7;
            tile.Gradient = true;
            tile.Glass = false;
            tile.BorderW = 0;
            float hue = Ui.BrandHue(236 + i * 34);
            tile.GradFrom = Ui.Hsb(hue, 0.62f, 0.56f);
            tile.GradTo = Ui.Hsb(Math.Min(305f, hue + 30f), 0.66f, 0.30f);
            tile.Location = new Point(7, (rowH - tileS) / 2);
            tile.Size = new Size(tileS, tileS);
            row.Controls.Add(tile);

            IconView ti = new IconView();
            ti.Icon = i == 0 ? Ico.Box : (i == 1 ? Ico.Puzzle : Ico.Server);
            ti.Col = Color.FromArgb(240, 244, 255);
            ti.Location = new Point((tileS - 16) / 2, (tileS - 16) / 2);
            ti.Size = new Size(16, 16);
            tile.Controls.Add(ti);

            PixelLabel c = new PixelLabel();
            c.Text = caps[i];
            c.Font = Ui.Font(8.5f, FontStyle.Bold);
            c.ForeColor = Ui.Txt;
            c.BackColor = Color.Transparent;
            c.AutoSize = false;
            c.Location = new Point(46, tight ? (rowH - 16) / 2 : mid - 1);
            c.Size = new Size(w - 92, 16);
            row.Controls.Add(c);

            if (!tight)
            {
                Label sb = new Label();
                sb.Text = subs[i];
                sb.Font = Ui.Font(7.5f);
                sb.ForeColor = Ui.GrayDim;
                sb.BackColor = Color.Transparent;
                sb.AutoSize = false;
                sb.Location = new Point(46, mid + 15);
                sb.Size = new Size(w - 92, 15);
                row.Controls.Add(sb);
            }

            IconView ch = new IconView();
            ch.Icon = Ico.ChevRight;
            ch.Col = Ui.GrayDim;
            ch.Location = new Point(w - 42, (rowH - 13) / 2);
            ch.Size = new Size(13, 13);
            ch.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            row.Controls.Add(ch);

            int which = i;
            row.Click += delegate { QuickLaunch(which); };
            y += rowH + gap;
        }

        RoundedButton add = RoundedButton.Dark("+   Добавить установку", 10);
        add.Font = Ui.Font(8.5f, FontStyle.Bold);
        add.ForeColor = Ui.Gray;
        add.Size = new Size(w - 24, addH);
        // кнопка встаёт ровно после последней строки, а не от низа
        // карточки: при сжатии высоты эти величины расходились
        add.Location = new Point(12, y + 2);
        add.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        add.Click += delegate { SetStatus("Новые сборки ставятся кнопкой «Скачать» на карточке версии", true); };
        quickCard.Controls.Add(add);
    }

    void QuickLaunch(int which)
    {
        if (which == 0)
        {
            // «Последняя версия» — ровно первая карточка списка,
            // а не захардкоженная 1.21.4 (её могло уже не быть в списке).
            if (cmbVer.Items.Count > 0) cmbVer.SelectedIndex = 0;
            OnPlay(null, null);
        }
        else if (which == 1) SetHomeTab(2);   // Fabric-версии
        else ShowView(ViewMods);
    }

    // Последняя версия из загруженного списка — для подписи в «Быстром запуске»
    string LatestReleaseName()
    {
        try
        {
            if (gridItems.Count > 0) return ShortVer(gridItems[0].id);
            string v = cfg["version"];
            return string.IsNullOrEmpty(v) ? "1.21.4" : ShortVer(v);
        }
        catch { return "1.21.4"; }
    }

    // ===== «Популярные моды» внизу главного экрана =====
    void BuildPopularMods()
    {
        if (popularBlock == null) return;
        popularBlock.Controls.Clear();
        int w = popularBlock.Width > 0 ? popularBlock.Width : 700;

        // иконка в фиолетовой плитке — как в референсе
        RoundedPanel ptile = new RoundedPanel();
        ptile.Radius = 7;
        ptile.Gradient = true;
        ptile.Glass = false;
        ptile.BorderW = 0;
        ptile.GradFrom = Color.FromArgb(124, 92, 255);
        ptile.GradTo = Color.FromArgb(77, 92, 255);
        ptile.Location = new Point(0, 0);
        ptile.Size = new Size(24, 24);
        popularBlock.Controls.Add(ptile);
        IconView pi = new IconView();
        pi.Icon = Ico.Puzzle;
        pi.Col = Color.White;
        pi.Location = new Point(4, 4);
        pi.Size = new Size(16, 16);
        ptile.Controls.Add(pi);

        PixelLabel h = new PixelLabel();
        h.Text = "Популярные моды";
        h.Font = Ui.Font(10.5f, FontStyle.Bold);
        h.ForeColor = Ui.Txt;
        h.BackColor = Color.Transparent;
        h.AutoSize = false;
        h.Location = new Point(32, 2);
        h.Size = new Size(240, 24);
        popularBlock.Controls.Add(h);

        RoundedButton all = new RoundedButton();
        all.Text = "Все моды  →";
        all.Font = Ui.Font(8, FontStyle.Bold);
        all.ForeColor = Ui.Accent1;
        all.Flat = true;
        all.BorderW = 0;
        all.Top = Color.Transparent;
        all.Bottom = Color.Transparent;
        all.HoverTop = Ui.Card;
        all.HoverBottom = Ui.Card;
        all.Size = new Size(104, 24);
        all.Location = new Point(w - 104, 1);
        all.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        all.Click += delegate { ShowView(ViewMods); };
        all.Visible = false;
        popularBlock.Controls.Add(all);

        TextLink allMods = new TextLink();
        allMods.Text = "Все моды";
        allMods.Font = Ui.Font(8, FontStyle.Bold);
        allMods.Size = new Size(100, 26);
        allMods.Location = new Point(w - 100, 0);
        allMods.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        allMods.Click += delegate { ShowView(ViewMods); };
        popularBlock.Controls.Add(allMods);

        string[] names = new string[] { "JourneyMap", "OptiFine", "JEI", "Better Minecraft", "Tinkers Construct" };
        string[] subs = new string[] { "Мини-карта и навигация", "Больше FPS", "Удобный крафт", "Новые механики", "Инструменты" };
        // Плитки делят ширину блока поровну. Раньше у каждой была жёсткая
        // нижняя граница 108px, и на узком окне пять плиток вылезали за
        // правый край на 128px. Теперь при нехватке ширины просто
        // показывается меньше плиток — каждая остаётся читаемой.
        int gap = 8;
        const int tileMin = 108;
        int count = names.Length;
        while (count > 1 && (w - gap * (count - 1)) / count < tileMin) count--;
        int tileW = (w - gap * (count - 1)) / count;
        int x = 0;
        for (int i = 0; i < count; i++)
        {
            RoundedPanel tile = new RoundedPanel();
            tile.Radius = 10;
            tile.BackColor = Ui.Card;
            tile.Border = Ui.Line;
            tile.HoverBack = Ui.SurfaceHi;
            tile.HoverBorder = Ui.Accent1;
            tile.Animated = true;
            tile.Glass = false;
            tile.Location = new Point(x, 28);
            tile.Size = new Size(tileW, 72);
            tile.Cursor = Cursors.Hand;
            popularBlock.Controls.Add(tile);

            RoundedPanel ico = new RoundedPanel();
            ico.Radius = 8;
            ico.Gradient = true;
            ico.Glass = false;
            ico.BorderW = 0;
            float hue = Ui.BrandHue(200 + i * 37);
            ico.GradFrom = Ui.Hsb(hue, 0.58f, 0.56f);
            ico.GradTo = Ui.Hsb(Math.Min(305f, hue + 34f), 0.62f, 0.28f);
            ico.Location = new Point(6, 19);
            ico.Size = new Size(34, 34);
            tile.Controls.Add(ico);

            IconView mi = new IconView();
            mi.Icon = i == 1 ? Ico.Sparkle : (i == 2 ? Ico.List : Ico.Puzzle);
            mi.Col = Color.FromArgb(240, 244, 255);
            mi.Location = new Point(9, 9);
            mi.Size = new Size(16, 16);
            ico.Controls.Add(mi);

            // Название и подпись: подпись в две строки — «Мини-карта
            // и навигация» в ширину плитки (~86px) одной строкой не
            // влезает и обрезалась на «Мини-карта и н…». Высота плитки
            // 72 это позволяет: 10..34 имя, 36..62 подпись.
            Font nf = Ui.Font(7.5f, FontStyle.Bold);
            Label nm = new Label();
            nm.Text = names[i];
            nm.Font = nf;
            nm.ForeColor = Ui.Txt;
            nm.BackColor = Color.Transparent;
            nm.AutoSize = false;
            nm.AutoEllipsis = false;
            nm.Location = new Point(42, 9);
            nm.Size = new Size(tileW - 46, 26);
            tile.Controls.Add(nm);

            Label sb = new Label();
            sb.Text = subs[i];
            sb.Font = Ui.Font(7.5f);
            sb.ForeColor = Ui.GrayDim;
            sb.BackColor = Color.Transparent;
            sb.AutoSize = false;
            sb.AutoEllipsis = false;
            sb.Location = new Point(42, 36);
            sb.Size = new Size(tileW - 46, 26);
            tile.Controls.Add(sb);

            string q = names[i];
            tile.Click += delegate { ModsOpenFor(q); };
            ico.Click += delegate { ModsOpenFor(q); };
            nm.Click += delegate { ModsOpenFor(q); };
            sb.Click += delegate { ModsOpenFor(q); };

            x += tileW + gap;
        }
    }

    // Клик по плитке мода: открыть раздел «Моды» с поиском по названию
    void ModsOpenFor(string name)
    {
        ShowView(ViewMods);
        try
        {
            modsSyncUi = true;
            modsSearch.Text = name;
            modsSearch.ForeColor = Ui.Txt;
            modsSyncUi = false;
            ModsSearch(name);
        }
        catch { }
    }

    void ShowError(string msg)
    {
        if (InvokeRequired) { Invoke(new Action<string>(ShowError), msg); return; }
        SetStatus("Ошибка: " + msg, false);
        toastTxt.Text = msg;
        toast.Visible = true;
        toast.BringToFront();
        toastTimer.Stop();
        toastTimer.Start();
    }

    void RefreshProfile()
    {
        // с лицензией показываем настоящий ник аккаунта, иначе выбранный офлайн
        string shown = (Auth.LoggedIn && Auth.Name.Length > 0) ? Auth.Name : nick;
        lblProfileNick.Text = shown;
        bool premium = Auth.LoggedIn;
        lblProfileSub.Text = premium ? "Premium" : "Luxe Launcher";
        if (profileCrown != null) profileCrown.Visible = premium;
    }

    void LoadLogo(PictureBox pic, LogoBadge badge)
    {
        // Логотип вшит в exe: logo.ico/logo.png читаются из ресурсов, а не
        // из распакованных файлов рядом с лаунчером. Свой logo.png в папке
        // данных по-прежнему важнее встроенного.
        try
        {
            byte[] ico = null;
            try
            {
                string custom = Path.Combine(Cfg.DataDir, "logo.ico");
                if (File.Exists(custom)) ico = File.ReadAllBytes(custom);
            }
            catch { }
            if (ico == null || ico.Length < 100) ico = Mc.GetEmbedded("logo.ico");
            if (ico != null && ico.Length > 100)
            {
                using (MemoryStream ms = new MemoryStream(ico))
                using (Icon tmp = new Icon(ms))
                    Icon = (Icon)tmp.Clone();
            }
        }
        catch { }
        try
        {
            byte[] png = null;
            try
            {
                string custom = Path.Combine(Cfg.DataDir, "logo.png");
                if (File.Exists(custom)) png = File.ReadAllBytes(custom);
            }
            catch { }
            if (png == null || png.Length < 100) png = Mc.GetEmbedded("logo.png");
            if (png != null && png.Length > 100)
            {
                using (MemoryStream ms = new MemoryStream(png))
                using (Image src = Image.FromStream(ms))
                    pic.Image = new Bitmap(src);
            }
        }
        catch { }
    }

    string curVer()
    {
        if (cmbVer.SelectedItem == null) return cfg["version"];
        return cmbVer.SelectedItem.ToString().Split(' ')[0];
    }

    string SearchHint()
    {
        return "Поиск версий...";
    }

    // Подсказка-заглушка в поле поиска — это не запрос.
    // Проверяем ОБЕ подсказки: поле могло остаться с другой вкладки.
    static bool IsHint(string s)
    {
        if (string.IsNullOrEmpty(s)) return true;
        string t = s.Trim().ToLower();
        return t == "поиск версий..." || t == "поиск модов..." || t == "поиск модов по modrinth..."
            || t == "поиск версий, модов, серверов...";
    }

    // Текущий запрос из поля поиска ("" если там подсказка или пусто)
    string SearchQuery()
    {
        try
        {
            string s = (topSearchBox != null ? topSearchBox.Text : "").Trim();
            if (IsHint(s) || s == TopSearchHint) return "";
            return s.ToLower();
        }
        catch { return ""; }
    }

    const string TopSearchHint = "Поиск версий, модов, серверов...";

    // При смене вкладки подсказка из чужой вкладки не должна остаться запросом
    void ResetSearchHint()
    {
        try
        {
            string cur = (topSearchBox != null ? topSearchBox.Text : "").Trim();
            if (cur == "" || IsHint(cur) || cur == TopSearchHint)
            {
                modsSyncUi = true;
                if (topSearchBox != null)
                {
                    topSearchBox.Text = TopSearchHint;
                    topSearchBox.ForeColor = Ui.Gray;
                }
                modsSyncUi = false;
            }
        }
        catch { modsSyncUi = false; }
    }

    void SetLoaderTab(int idx)
    {
        loaderTab = idx;
        RefreshTabs();
        ResetSearchHint();
        RefreshVersions();
    }

    // Вкладки главного экрана: «Новые», «OptiFine», «Fabric», «Forge»,
    // «Визуалы», «Кряки». Внутри — фильтры по загрузчику и типу сборки.
    void SetHomeTab(int idx)
    {
        if (idx == 1) { loaderTab = 4; typeFilter = "release"; }        // OptiFine-профили
        else if (idx == 2) { loaderTab = 1; typeFilter = "release"; }   // Fabric
        else if (idx == 3) { loaderTab = 2; typeFilter = "release"; }   // Forge
        else if (idx == 4) { loaderTab = 5; typeFilter = "release"; }   // Визуалы
        else if (idx == 5) { loaderTab = 6; typeFilter = "release"; }   // Кряки
        else { loaderTab = 0; typeFilter = "release"; }                 // Новые
        homeTab = idx;
        RefreshTabs();
        ResetSearchHint();
        UpdateHomeListHead();
        RefreshVersions();
    }
    int homeTab = 0;

    // Заголовок над списком раньше всегда был «Последние версии»,
    // даже когда открыт Forge или кряки — теперь меняется вместе с вкладкой.
    void UpdateHomeListHead()
    {
        try
        {
            if (homeListHead == null) return;
            switch (homeTab)
            {
                case 1: homeListHead.Text = "OptiFine"; break;
                case 2: homeListHead.Text = "Fabric-версии"; break;
                case 3: homeListHead.Text = "Forge-версии"; break;
                case 4: homeListHead.Text = "Сборки с визуалом"; break;
                case 5: homeListHead.Text = "Кряки"; break;
                default: homeListHead.Text = "Новые версии"; break;
            }
        }
        catch { }
    }

    // Хук для отладочного прогона --shot-test: подгрузить данные активной вкладки
    public void DebugRefreshTab()
    {
        RefreshTabs();
        if (view == ViewMods) RefreshMods();
        else RefreshVersions();
    }

    // Хук отладки: переключить вкладку главного экрана как будто по клику
    public void DebugSwitchTab(int idx)
    {
        if (InvokeRequired) { Invoke(new Action<int>(DebugSwitchTab), idx); return; }
        SetHomeTab(idx);
    }

    // Хук отладки: открыть раздел (0 главная, 1 новости, 2 моды)
    public void DebugShowView(int v)
    {
        if (InvokeRequired) { Invoke(new Action<int>(DebugShowView), v); return; }
        ShowView(v);
    }

    public void RefreshTabs()
    {
        for (int i = 0; i < loaderTabs.Count; i++)
        {
            RoundedButton b = loaderTabs[i];
            if (i == homeTab)
            {
                // активный чип: залитая акцентом «пилюля» как в референсе
                b.Top = Ui.Accent1;
                b.Bottom = Ui.Accent2;
                b.HoverTop = ControlPaint.Light(Ui.Accent1, 0.12f);
                b.HoverBottom = ControlPaint.Light(Ui.Accent2, 0.08f);
                b.ForeColor = Color.White;
                b.BorderColor = Ui.AccentDark;
                b.BorderW = 0;
            }
            else
            {
                b.Top = Ui.Card;
                b.Bottom = Ui.Card;
                b.HoverTop = Ui.SurfaceHi;
                b.HoverBottom = Ui.Surface;
                b.ForeColor = Ui.Gray;
                b.BorderColor = Ui.Line;
                b.BorderW = 1;
            }
            b.Invalidate();
        }
    }

    void SetStatus(string s, bool ok)
    {
        if (InvokeRequired) { Invoke(new Action<string, bool>(SetStatus), s, ok); return; }
        lblStatus.Text = s;
        dot.DotColor = ok ? Ui.Green : Ui.Orange;
        // DoEvents убран: он реентрантно прокачивал очередь сообщений
        // посреди фоновых Invoke'ов, текст и так перерисуется в этом же кадре
    }

    void SetStatus(string s)
    {
        SetStatus(s, true);
    }

    void SetProg(int v, int m)
    {
        if (InvokeRequired) { Invoke(new Action<int, int>(SetProg), v, m); return; }
        try
        {
            if (m > 0)
            {
                bar.Maximum = m;
                if (v > m) v = m;
                if (v < 0) v = 0;
                bar.Value = v;
            }
        }
        catch { }
    }

    // Прогресс сетевой загрузки: полоса в витрине + проценты в статусе.
    // Вызывается из фоновых потоков (скачивание модов, Java, апдейта),
    // поэтому маршалим в UI-поток; got < 0 — сбросить полосу.
    void OnDownloadProgress(long got, long total, string label)
    {
        if (IsDisposed) return;
        try
        {
            if (InvokeRequired)
            {
                if (!IsHandleCreated) return;
                BeginInvoke(new Action<long, long, string>(OnDownloadProgress), got, total, label);
                return;
            }
            if (got < 0) { SetProg(0, 100); return; }
            int pct = total > 0 ? (int)(got * 100 / total) : 0;
            SetProg(pct, 100);
            string mb = (got / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            string all = total > 0
                ? mb + " из " + (total / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " МБ"
                : mb + " МБ";
            SetStatus((string.IsNullOrEmpty(label) ? "Загрузка" : label) + ": " + pct + "%  (" + all + ")", true);
        }
        catch { }
    }

    void LoadVersions()
    {
        LoadVersions(++verLoadGen);
    }

    // gen защищает от гонки: если игрок быстро переключил вкладку,
    // медленный ответ старой вкладки больше не перезаписывает список.
    void LoadVersions(int gen)
    {
        SetStatus("Получаю версии...", true);
        List<GridItem> items = new List<GridItem>();
        try
        {
            List<string> installed = Mc.GetInstalledVersions();
            if (loaderTab == 0)
                LoadVanillaTab(items, installed);
            else if (loaderTab == 1)
                LoadLoaderTab(items, installed, "fabric");
            else if (loaderTab == 2)
                LoadLoaderTab(items, installed, "forge");
            else if (loaderTab == 3)
                LoadLoaderTab(items, installed, "quilt");
            else if (loaderTab == 4)
                LoadOptiFineTab(items, installed);
            else if (loaderTab == 5)
                LoadModpacksTab(items, installed, false);
            else if (loaderTab == 6)
                LoadModpacksTab(items, installed, true);
            else
                LoadLoaderTab(items, installed, "neoforge");
            if (gen != verLoadGen) return;   // вкладку переключили, пока грузили
            FillGrid(items);
            if (!lblStatus.Text.StartsWith("Нет интернета")) SetStatus("Готов к запуску", true);
        }
        catch (Exception ex)
        {
            try
            {
                if (gen != verLoadGen) return;
                List<string> installed = Mc.GetInstalledVersions();
                foreach (string iv in installed) items.Add(new GridItem(iv, true));
                FillGrid(items);
                SetStatus("Нет интернета, показываю установленные", false);
            }
            catch (Exception ex2) { SetStatus("Ошибка: " + ex2.Message, false); }
        }
    }

    Dictionary<string, object> GetManifest()
    {
        if (manifestCache == null)
            manifestCache = Mc.GetJson(Cfg.ManifestUrl);
        return manifestCache;
    }

    void LoadVanillaTab(List<GridItem> items, List<string> installed)
    {
        Dictionary<string, object> m = GetManifest();
        ArrayList vers = m["versions"] as ArrayList;
        int n = 0;
        foreach (object vo in vers)
        {
            if (n >= 40) break;
            Dictionary<string, object> v = vo as Dictionary<string, object>;
            if (v == null) continue;
            string type = v.ContainsKey("type") ? v["type"].ToString() : "release";
            if (typeFilter == "old") { if (!type.StartsWith("old")) continue; }
            else if (type != typeFilter) continue;
            string id = v["id"].ToString();
            items.Add(new GridItem(id, installed.Contains(id)));
            n++;
        }
        vanillaLoaded = n;
        foreach (string iv in installed)
        {
            if (Mc.KindOf(iv) != "vanilla" || Mc.IsModpack(iv)) continue;
            // OptiFine-профиль — это тоже «vanilla» по имени, но его место
            // во вкладке OptiFine, а не в списке чистых версий.
            if (iv.ToLower().Contains("optifine")) continue;
            bool has = false;
            foreach (GridItem g in items) { if (g.id == iv) { has = true; break; } }
            if (!has) items.Add(new GridItem(iv, true));
        }
        if (items.Count == 0) items.Add(new GridItem("1.20.1", false));
    }

    // OptiFine ставится своим установщиком и создаёт профиль с inheritsFrom
    // («1.21.4-OptiFine_HD_U_J3»). Такие профили и сборки с OptiFine
    // в имени показываем отдельной вкладкой, а не мешаем с ваниллой.
    void LoadOptiFineTab(List<GridItem> items, List<string> installed)
    {
        foreach (string iv in installed)
        {
            string nm = Mc.ModpackName(iv);
            bool isOf = iv.ToLower().Contains("optifine")
                || (nm != null && nm.ToLower().Contains("optifine"));
            if (!isOf) continue;
            items.Add(new GridItem(iv, true, nm, Mc.IsModpack(iv) ? "optifine" : null));
        }
        foreach (Dictionary<string, object> d in Mc.GetModpackCatalog())
        {
            object o;
            if (!d.TryGetValue("id", out o) || o == null) continue;
            string id = o.ToString();
            bool has = false;
            foreach (GridItem g in items) { if (g.id == id) { has = true; break; } }
            if (has || Mc.IsInstalled(id)) continue;
            object no;
            string nm = (d.TryGetValue("name", out no) && no != null) ? no.ToString() : null;
            object to;
            string tp = (d.TryGetValue("type", out to) && to != null) ? to.ToString().ToLower() : "visual";
            if (tp != "optifine" && !id.ToLower().Contains("optifine")
                && (nm == null || !nm.ToLower().Contains("optifine"))) continue;
            items.Add(new GridItem(id, false, nm, "optifine"));
        }
    }

    // Дозагрузка версий при прокрутке к низу списка. Раньше список был
    // ограничен сорока релизами (до ~1.20) и «ниже не прокручивалось» —
    // карточек ниже просто не существовало. Теперь по мере скролла
    // подкладываем следующую порцию.
    void LoadMoreVersions()
    {
        if (loadingMore || loaderTab != 0) return;
        if (!string.IsNullOrEmpty(SearchQuery().Trim())) return;
        loadingMore = true;
        try
        {
            Dictionary<string, object> m = GetManifest();
            ArrayList vers = m["versions"] as ArrayList;
            List<GridItem> add = new List<GridItem>();
            int consumed = 0;                 // сколько релизов перебрали в этой странице
            foreach (object vo in vers)
            {
                Dictionary<string, object> v = vo as Dictionary<string, object>;
                if (v == null) continue;
                string type = v.ContainsKey("type") ? v["type"].ToString() : "release";
                if (type != "release") continue;
                if (consumed < vanillaLoaded) { consumed++; continue; }
                consumed++;
                string id = v["id"].ToString();
                bool dup = false;
                foreach (GridItem g in gridItems) { if (g.id == id) { dup = true; break; } }
                if (!dup) add.Add(new GridItem(id, Mc.IsInstalled(id)));
                if (add.Count >= 24) break;
            }
            vanillaLoaded += consumed;
            if (add.Count == 0) { verList.NearBottom = null; return; }
            // добавляем карточки в конец, не пересобирая список — иначе
            // скролл улетит в начало
            foreach (GridItem g in add)
            {
                int idx = gridItems.Count;
                gridItems.Add(g);
                verList.Controls.Add(MakeVerCard(g.id, g.inst, g.disp, g.mtype, idx));
            }
        }
        catch { verList.NearBottom = null; }
        finally { loadingMore = false; }
    }

    void LoadLoaderTab(List<GridItem> items, List<string> installed, string kind)
    {
        foreach (string iv in installed)
        {
            // Установленные сборки на fabric/quilt — первыми в этом же
            // списке: иначе после установки игрок их тут не находит.
            if (Mc.KindOf(iv) != kind || !Mc.IsModpack(iv)) continue;
            string nm = Mc.ModpackName(iv);
            items.Add(new GridItem(iv, true, nm));
        }
        List<string> mcs = new List<string>();
        if (kind == "fabric" || kind == "quilt")
        {
            Dictionary<string, object> m = GetManifest();
            ArrayList vers = m["versions"] as ArrayList;
            // Fabric не поддерживает свежий релиз сразу после выхода:
            // на карточке был новый MC, а установка падала «Нет загрузчика».
            // Оставляем только версии, для которых Fabric уже собрал лоадер.
            HashSet<string> supported = null;
            if (kind == "fabric")
            {
                try
                {
                    ArrayList gv = Mc.GetJsonArray("https://meta.fabricmc.net/v2/versions/game");
                    supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (object go in gv)
                    {
                        Dictionary<string, object> g = go as Dictionary<string, object>;
                        if (g == null) continue;
                        object sv;
                        if (!g.TryGetValue("stable", out sv) || !(sv is bool) || !(bool)sv) continue;
                        object vv;
                        if (g.TryGetValue("version", out vv) && vv != null) supported.Add(vv.ToString());
                    }
                }
                catch { supported = null; }
            }
            foreach (object vo in vers)
            {
                if (mcs.Count >= 30) break;
                Dictionary<string, object> v = vo as Dictionary<string, object>;
                if (v == null) continue;
                if (v.ContainsKey("type") && v["type"].ToString() != "release") continue;
                string id = v["id"].ToString();
                if (supported != null && supported.Count > 0 && !supported.Contains(id)) continue;
                mcs.Add(id);
            }
        }
        else if (kind == "forge")
        {
            Dictionary<string, object> pr = Mc.GetJson("https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json");
            Dictionary<string, object> promos = pr["promos"] as Dictionary<string, object>;
            foreach (string k in promos.Keys)
            {
                int dash = k.LastIndexOf('-');
                if (dash <= 0) continue;
                string mc = k.Substring(0, dash);
                if (!mcs.Contains(mc)) mcs.Add(mc);
            }
            // promos — неупорядоченный словарь: без сортировки карточки
            // шли вперемешку (1.7.10, 1.21, 1.12.2). Сортируем и уже потом
            // обрезаем до 40, иначе свежие версии могли не попасть в список.
            mcs.Sort(delegate(string a, string b) { return Mc.CompareVersions(b, a); });
            if (mcs.Count > 40) mcs.RemoveRange(40, mcs.Count - 40);
        }
        else
        {
            Dictionary<string, object> nl = Mc.GetJson("https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge");
            ArrayList vers = nl["versions"] as ArrayList;
            for (int i = vers.Count - 1; i >= 0; i--)
            {
                if (mcs.Count >= 30) break;
                string s = vers[i].ToString();
                string low = s.ToLower();
                if (low.Contains("beta") || low.Contains("alpha")) continue;
                string mc = Mc.NeoMcOf(s);
                if (string.IsNullOrEmpty(mc) || mcs.Contains(mc)) continue;
                mcs.Add(mc);
            }
        }
        foreach (string mc in mcs)
        {
            string gid = mc + "-" + kind;
            items.Add(new GridItem(gid, Mc.IsInstalled(gid)));
        }

        foreach (string iv in installed)
        {
            if (Mc.KindOf(iv) != kind || Mc.IsModpack(iv)) continue;
            GridItem found = null;
            foreach (GridItem g in items) { if (g.id == iv) found = g; }
            if (found != null) found.inst = true;
            else items.Add(new GridItem(iv, true));
        }
    }

    void LoadModpacksTab(List<GridItem> items, List<string> installed, bool onlyCracks)
    {
        foreach (string iv in installed)
        {
            if (!Mc.IsModpack(iv)) continue;
            string itp = Mc.ModpackType(iv);
            // «Визуалы» — только type=visual, «Кряки» — только type=crack;
            // OptiFine-сборки живут в своей вкладке и сюда не подмешиваются.
            if (onlyCracks ? itp != "crack" : itp != "visual") continue;
            items.Add(new GridItem(iv, true));
        }
        foreach (Dictionary<string, object> d in Mc.GetModpackCatalog())
        {
            object o;
            if (!d.TryGetValue("id", out o) || o == null) continue;
            string id = o.ToString();
            bool has = false;
            foreach (GridItem g in items) { if (g.id == id) { has = true; break; } }
            if (has || Mc.IsInstalled(id)) continue;
            object no;
            string nm = (d.TryGetValue("name", out no) && no != null) ? no.ToString() : null;
            object to;
            string tp = (d.TryGetValue("type", out to) && to != null) ? to.ToString().ToLower() : "visual";
            if (tp != "crack" && tp != "optifine") tp = "visual";
            if (onlyCracks ? tp != "crack" : tp != "visual") continue;
            items.Add(new GridItem(id, false, nm, tp));
        }
    }

    // ============ РАЗДЕЛ «МОДЫ» (Modrinth) ============
    // Моды ставятся в папку mods выбранной сборки (Fabric/Quilt/Forge/NeoForge).
    // Обязательные зависимости подтягиваются автоматически рекурсивно по данным Modrinth.

    string ModsSearchText()
    {
        try
        {
            string s = modsSearch.Text.Trim();
            if (IsHint(s)) return "";
            return s;
        }
        catch { return ""; }
    }

    static string ModrinthLoader(string kind)
    {
        if (kind == "fabric" || kind == "quilt" || kind == "forge" || kind == "neoforge") return kind;
        return "any";
    }

    static string FmtDl(long d)
    {
        if (d <= 0) return "0 загрузок";
        if (d >= 1000000) return (d / 1000000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "M";
        if (d >= 1000) return (d / 1000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "K";
        return d.ToString();
    }

    // Экземпляры, в которые можно положить моды
    List<string> ModsTargets()
    {
        List<string> l = new List<string>();
        try
        {
            foreach (string v in Mc.GetInstalledVersions())
            {
                string k = Mc.KindOf(v);
                if (k == "fabric" || k == "quilt" || k == "forge" || k == "neoforge") l.Add(v);
            }
        }
        catch { }
        l.Sort();
        return l;
    }

    static string TargetLabel(string id)
    {
        string kind = Mc.KindOf(id);
        string nm = kind == "vanilla" ? "Vanilla" : (kind == "fabric" ? "Fabric" : (kind == "forge" ? "Forge" : (kind == "quilt" ? "Quilt" : "NeoForge")));
        return Mc.BaseMcOf(id) + "  —  " + nm;
    }

    // Пересобрать раздел: список сборок, статистика, загрузка модов
    void RefreshMods()
    {
        if (InvokeRequired) { Invoke(new Action(RefreshMods)); return; }
        if (modsBusy) return;
        modsBusy = true;
        try { RefreshModsCore(); }
        finally { modsBusy = false; }
    }

    void RefreshModsCore()
    {
        if (modsTargetDd == null) return;
        List<string> targets = ModsTargets();
        string prev = modsTarget;

        modsTargetDd.Items.Clear();
        foreach (string v in targets) modsTargetDd.Items.Add(TargetLabel(v));

        if (targets.Count == 0)
        {
            modsTarget = ""; modsDir = ""; modsMcVer = ""; modsLoader = "";
            modItems = new List<ModInfo>();
            modsStatTxt.Text = "нет установленных сборок";
            modsLoaderDd.Enabled = false;
            modsReload.Enabled = false;
            BuildModRows("");
            return;
        }

        modsLoaderDd.Enabled = true;
        modsReload.Enabled = true;

        int sel = targets.IndexOf(prev);
        if (sel < 0) sel = targets.IndexOf(curVer());
        if (sel < 0) sel = 0;
        modsTargetDd.SelectedIndex = sel;
        modsTarget = targets[sel];

        modsMcVer = Mc.BaseMcOf(modsTarget);
        modsLoader = ModrinthLoader(Mc.KindOf(modsTarget));
        modsDir = Path.Combine(Mc.GameDirFor(modsTarget), "mods");

        int enabled;
        int total = Modrinth.CountInstalled(modsDir, out enabled);
        modsStatTxt.Text = total == 0
            ? "модов пока нет"
            : (enabled + " вкл • " + (total - enabled) + " выкл");

        ModsSearch(ModsSearchText());
    }

    // Выбор другой сборки-приёмника
    void OnModsTargetChanged()
    {
        if (modsTargetDd == null || modsTargetDd.SelectedIndex < 0) return;
        List<string> targets = ModsTargets();
        int i = modsTargetDd.SelectedIndex;
        if (i >= targets.Count) return;
        modsTarget = targets[i];
        modsMcVer = Mc.BaseMcOf(modsTarget);
        modsLoader = ModrinthLoader(Mc.KindOf(modsTarget));
        modsDir = Path.Combine(Mc.GameDirFor(modsTarget), "mods");
        int enabled;
        int total = Modrinth.CountInstalled(modsDir, out enabled);
        modsStatTxt.Text = total == 0 ? "модов пока нет" : (enabled + " вкл • " + (total - enabled) + " выкл");
        ModsSearch(ModsSearchText());
    }

    // Фильтр по загрузчику (не зависит от сборки-приёмника)
    void OnModsLoaderChanged()
    {
        if (modsLoaderDd == null || modsLoaderDd.SelectedIndex < 0) return;
        string[] all = new string[] { "Любой", "Fabric", "Quilt", "Forge", "NeoForge" };
        int i = modsLoaderDd.SelectedIndex;
        modsLoader = (i > 0 && i < all.Length) ? all[i].ToLower() : "any";
        ModsSearch(ModsSearchText());
    }

    void ModsSearch(string q)
    {
        if (string.IsNullOrEmpty(modsTarget)) { BuildModRows(q); return; }
        int gen = ++modSearchGen;
        string target = modsTarget, mcv = modsMcVer, loader = modsLoader, mdir = modsDir;
        SetStatus(q == "" ? "Загружаю популярные моды с Modrinth..." : "Ищу «" + q + "» на Modrinth...", true);
        ShowModsHint(q);
        Thread t = new Thread((ThreadStart)delegate
        {
            try
            {
                List<ModInfo> found = Modrinth.Search(q, mcv, loader, 40);
                if (IsDisposed) return;
                if (InvokeRequired) Invoke(new Action(delegate { ApplyMods(found, q, gen, target, mdir); }));
                else ApplyMods(found, q, gen, target, mdir);
            }
            catch (Exception ex)
            {
                if (IsDisposed) return;
                try
                {
                    if (InvokeRequired) Invoke(new Action(delegate { SetStatus("Modrinth: " + ex.Message, false); }));
                    else SetStatus("Modrinth: " + ex.Message, false);
                }
                catch { }
            }
        });
        t.IsBackground = true;
        t.Start();
    }

    void ApplyMods(List<ModInfo> found, string q, int gen, string target, string mdir)
    {
        if (IsDisposed) return;
        if (view != ViewMods) return;                      // пользователь ушёл из раздела
        if (gen != modSearchGen) return;                 // пришёл устаревший ответ
        if (target != modsTarget) return;                // пользователь сменил сборку
        Dictionary<string, string> index = Modrinth.LoadIndex(mdir);
        foreach (ModInfo m in found) m.installed = Modrinth.IsInstalled(mdir, index, m);
        Modrinth.SaveIndex(mdir, index);
        modItems = found;
        BuildModRows(q);
        SetStatus("Модов: " + found.Count + (q == "" ? " (популярные)" : " по «" + q + "»") + " • для " + TargetLabel(modsTarget), true);
    }

    void BuildModRows(string filter)
    {
        if (modsList == null) return;
        if (modsList.InvokeRequired) { modsList.Invoke(new Action<string>(BuildModRows), filter); return; }
        if (view != ViewMods) return;                    // не затираем другие разделы
        modsList.Controls.Clear();
        int shown = 0;
        foreach (ModInfo m in modItems)
        {
            if (!string.IsNullOrEmpty(filter))
            {
                string hay = ((m.title ?? "") + " " + (m.desc ?? "") + " " + (m.author ?? "")).ToLower();
                if (hay.IndexOf(filter.ToLower(), StringComparison.Ordinal) < 0) continue;
            }
            modsList.Controls.Add(MakeModCard(m));
            shown++;
        }
        // WinForms сбрасывает Visible у списка, когда в него добавляют элементы
        modsList.ResetScroll();
        try { if (!modsList.Visible) modsList.Visible = true; } catch { }
        if (shown == 0)
        {
            AddModsHint(string.IsNullOrEmpty(modsTarget)
                ? "Сначала установи сборку Fabric / Quilt / Forge / NeoForge на главной"
                : (string.IsNullOrEmpty(filter)
                    ? "Моды не загрузились — проверь интернет"
                    : "Ничего не найдено по запросу «" + filter + "»"));
        }
    }

    // Подсказка в списке модов: пустой каталог, идёт загрузка,
    // ничего не найдено — вместо пустого тёмного прямоугольника
    void AddModsHint(string text)
    {
        if (modsList == null) return;
        Label none = new Label();
        none.Text = text;
        none.Font = Ui.Font(10);
        none.ForeColor = Ui.Gray;
        none.BackColor = Ui.Bg;
        none.AutoSize = true;
        none.Location = new Point(12, 12);
        modsList.Controls.Add(none);
    }

    // Пока идёт запрос к Modrinth, в списке висит подсказка: без неё
    // раздел выглядит пустым и непонятно, идёт ли вообще загрузка
    void ShowModsHint(string q)
    {
        if (modsList == null) return;
        if (modsList.InvokeRequired) { modsList.Invoke(new Action<string>(ShowModsHint), q); return; }
        if (view != ViewMods) return;
        if (modsList.Controls.Count > 0) return;      // уже что-то есть — не мигаем
        AddModsHint(q == "" ? "Загружаю каталог Modrinth..." : "Ищу «" + q + "» на Modrinth...");
    }

    RoundedPanel MakeModCard(ModInfo m)
    {
        ModInfo mm = m;
        RoundedPanel p = new RoundedPanel();
        p.Radius = 16;
        p.BackColor = Ui.Card;
        p.Border = Ui.Line;
        p.HoverBack = Ui.CardHover;
        p.HoverBorder = Ui.Accent1;
        p.Animated = true;
        p.Size = new Size(268, 186);
        p.Margin = new Padding(0, 0, 12, 12);

        ModAvatar av = new ModAvatar();
        av.Seed = m.id;
        av.Text2 = Ui.Initials(m.title);
        av.Location = new Point(16, 16);
        av.Size = new Size(48, 48);
        p.Controls.Add(av);

        PixelLabel ti = new PixelLabel();
        ti.Text = Ui.Short(m.title, 24);
        ti.Font = Ui.Font(10.5f, FontStyle.Bold);
        ti.ForeColor = Ui.Txt;
        ti.BackColor = Color.Transparent;
        ti.AutoSize = false;
        ti.Location = new Point(76, 17);
        ti.Size = new Size(176, 22);
        p.Controls.Add(ti);

        Label au = new Label();
        au.Text = Ui.Short(string.IsNullOrEmpty(m.author) ? "неизвестный автор" : "автор: " + m.author, 24);
        au.Font = Ui.Font(8);
        au.ForeColor = Ui.GrayDim;
        au.BackColor = Color.Transparent;
        au.AutoSize = false;
        au.Location = new Point(76, 39);
        au.Size = new Size(176, 16);
        p.Controls.Add(au);

        Label ds = new Label();
        // без Ui.Short: он режет по числу символов и обрывает слова
        // («...Minecraft, which greatly» без многоточия). Clamp считает
        // реальные две строки и ставит «…» там, где текст перестаёт
        // влезать — одинаково для всех карточек.
        ds.Text = Ui.Clamp(m.desc, Ui.Font(8), 236, 36);
        ds.Font = Ui.Font(8);
        ds.ForeColor = Ui.Gray;
        ds.BackColor = Color.Transparent;
        ds.AutoSize = false;
        ds.Location = new Point(16, 72);
        ds.Size = new Size(236, 36);
        p.Controls.Add(ds);

        string cat = "";
        foreach (string c in m.cats)
        {
            if (c == "fabric" || c == "forge" || c == "quilt" || c == "neoforge") { cat = c.ToUpper(); break; }
        }
        if (string.IsNullOrEmpty(cat)) cat = string.IsNullOrEmpty(modsLoader) || modsLoader == "any" ? "MOD" : modsLoader.ToUpper();

        Label badge = new Label();
        badge.Text = cat + "  •  " + modsMcVer + "  •  " + FmtDl(m.downloads);
        badge.Font = Ui.Font(7, FontStyle.Bold);
        badge.ForeColor = Ui.GrayDim;
        badge.BackColor = Color.Transparent;
        badge.AutoSize = false;
        badge.Location = new Point(16, 110);
        badge.Size = new Size(236, 14);
        p.Controls.Add(badge);

        if (m.installed)
        {
            Label state = new Label();
            state.Text = m.disabled ? "○  ВЫКЛЮЧЕН" : "●  ВКЛЮЧЁН";
            state.Font = Ui.Font(7.5f, FontStyle.Bold);
            state.ForeColor = m.disabled ? Ui.Gray : Ui.Green;
            state.BackColor = Color.Transparent;
            state.TextAlign = ContentAlignment.MiddleRight;
            state.AutoSize = false;
            state.Location = new Point(140, 108);
            state.Size = new Size(112, 18);
            p.Controls.Add(state);

            RoundedButton tg = RoundedButton.Dark("", 11);
            tg.Icon = m.disabled ? Ico.Power : Ico.Stop;
            tg.IconSize = 15;
            tg.IconGap = 7;
            tg.Text = m.disabled ? "Включить" : "Выключить";
            tg.Font = Ui.Font(9, FontStyle.Bold);
            tg.ForeColor = m.disabled ? Ui.Gray : Ui.Green;
            tg.Location = new Point(16, 134);
            tg.Size = new Size(142, 36);
            tg.Click += delegate { ModToggle(mm); };
            p.Controls.Add(tg);

            RoundedButton up = RoundedButton.IcoBtn(Ico.Refresh, 36, 11);
            up.Location = new Point(164, 134);
            up.Click += delegate { ModInstall(mm); };
            p.Controls.Add(up);

            RoundedButton del = RoundedButton.IcoBtn(Ico.Trash, 36, 11);
            del.Location = new Point(208, 134);
            del.Click += delegate { ModUninstall(mm); };
            p.Controls.Add(del);
        }
        else
        {
            RoundedButton dl2 = new RoundedButton();
            dl2.Text = "Установить";
            dl2.Icon = Ico.Download;
            dl2.IconSize = 15;
            dl2.Radius = 11;
            dl2.Font = Ui.Font(9.5f, FontStyle.Bold);
            dl2.Location = new Point(16, 134);
            dl2.Size = new Size(236, 36);
            dl2.Click += delegate { ModInstall(mm); };
            p.Controls.Add(dl2);
        }

        p.Cursor = Cursors.Default;
        return p;
    }

    // Включить / выключить мод (файл переименовывается, загрузчик его не видит)
    void ModToggle(ModInfo m)
    {
        if (busyDl) { SetStatus("Дождись окончания текущей загрузки...", false); return; }
        if (string.IsNullOrEmpty(modsDir)) { ShowError("Сначала выбери сборку"); return; }
        busyDl = true;
        string mdir = modsDir;
        ModInfo mm = m;
        Thread t = new Thread((ThreadStart)delegate
        {
            try
            {
                Dictionary<string, string> index = Modrinth.LoadIndex(mdir);
                bool wantOn = mm.disabled;   // выключенный -> включаем и наоборот
                bool ok = Modrinth.SetEnabled(mm.id, mdir, index, wantOn);
                if (!ok) ShowError("Не удалось переключить " + mm.title);
                else SetStatus((wantOn ? "▶ " + Ui.Short(mm.title, 40) + " включён" : "■ " + Ui.Short(mm.title, 40) + " выключен — файл переименован, загрузчик его не видит"), true);
            }
            catch (Exception ex) { ShowError(ex.Message); }
            finally
            {
                busyDl = false;
                if (!IsDisposed) RefreshMods();
            }
        });
        t.IsBackground = true;
        t.Start();
    }

    void ModInstall(ModInfo m)
    {
        if (busyDl) { SetStatus("Дождись окончания текущей загрузки...", false); return; }
        if (string.IsNullOrEmpty(modsTarget))
        {
            ShowError("Сначала установи сборку Fabric / Quilt / Forge / NeoForge — моды кладутся в её папку mods");
            return;
        }
        busyDl = true;
        try { SetProg(0, 100); } catch { }
        string mcv = modsMcVer, loader = modsLoader, mdir = modsDir;
        string title = m.title;
        Thread t = new Thread((ThreadStart)delegate
        {
            int steps = 0;
            try
            {
                // прогресс двигаем по мере установки файлов
                ModJob job = Modrinth.InstallMod(m.id, mcv, loader, mdir, delegate(string s)
                {
                    steps++;
                    SetProg(steps, steps + 2);
                    SetStatus(s, true);
                });
                string msg = "✓ " + Ui.Short(title, 40) + " установлен";
                if (job.installed > 1) msg += " (+" + (job.installed - 1) + " доп.)";
                if (job.skipped.Count > 0)
                {
                    msg += " • не поставилось: " + string.Join(", ", job.skipped.ToArray());
                    SetStatus(msg, false);
                }
                else SetStatus(msg, true);
            }
            catch (Exception ex) { ShowError(ex.Message); }
            finally
            {
                busyDl = false;
                try { if (!IsDisposed && IsHandleCreated) Invoke(new Action(delegate { try { bar.Value = 0; } catch { } })); }
                catch { }
                if (!IsDisposed) RefreshMods();
            }
        });
        t.IsBackground = true;
        t.Start();
    }

    void ModUninstall(ModInfo m)
    {
        if (busyDl) { SetStatus("Дождись окончания текущей загрузки...", false); return; }
        busyDl = true;
        string mdir = modsDir;
        ModInfo mm = m;
        Thread t = new Thread((ThreadStart)delegate
        {
            try
            {
                Dictionary<string, string> index = Modrinth.LoadIndex(mdir);
                bool ok = Modrinth.Uninstall(m.id, mdir, index);
                SetStatus(ok ? ("✕ " + Ui.Short(mm.title, 40) + " удалён из сборки") : "Файл мода не найден, убрал из списка", true);
            }
            catch (Exception ex) { ShowError(ex.Message); }
            finally
            {
                busyDl = false;
                if (!IsDisposed) RefreshMods();
            }
        });
        t.IsBackground = true;
        t.Start();
    }

    void FillGrid(List<GridItem> items)
    {
        gridItems = items;
        if (cmbVer.InvokeRequired)
        {
            cmbVer.Invoke(new Action<List<GridItem>>(FillGrid), items);
            return;
        }
        cmbVer.Items.Clear();
        foreach (GridItem g in items)
            cmbVer.Items.Add(g.id + (g.inst ? "  [установлена]" : ""));
        int sel = 0;
        if (!string.IsNullOrEmpty(cfg["version"]))
        {
            for (int i = 0; i < cmbVer.Items.Count; i++)
            {
                if (cmbVer.Items[i].ToString().StartsWith(cfg["version"])) { sel = i; break; }
            }
        }
        if (cmbVer.Items.Count > 0) cmbVer.SelectedIndex = sel;
        BuildRowsFiltered(SearchQuery());
    }

    void RefreshVersions()
    {
        int gen = ++verLoadGen;
        Thread t = new Thread((ThreadStart)delegate
        {
            try { LoadVersions(gen); }
            catch (Exception ex) { SetStatus("Ошибка: " + ex.Message, false); }
        });
        t.IsBackground = true;
        t.Start();
    }

    void BuildVersionRows(List<string> installed, List<string> releases)
    {
        cacheInst = new List<string>(installed);
        cacheRel = new List<string>(releases);
        BuildRowsFiltered(SearchQuery());
    }

    void BuildRowsFiltered(string filter)
    {
        if (verList.InvokeRequired)
        {
            verList.Invoke(new Action<string>(BuildRowsFiltered), filter);
            return;
        }
        verList.Controls.Clear();
        int shown = 0;
        int cardIdx = 0;
        foreach (GridItem g in gridItems)
        {
            if (!string.IsNullOrEmpty(filter) && !g.id.ToLower().Contains(filter) && (g.disp == null || !g.disp.ToLower().Contains(filter))) continue;
            verList.Controls.Add(MakeVerCard(g.id, g.inst, g.disp, g.mtype, cardIdx));
            cardIdx++;
            shown++;
        }
        if (shown == 0)
        {
            Label none = new Label();
            none.Text = loaderTab == 4
                ? "OptiFine не установлен — профиль появится здесь после установки"
                : "Ничего не найдено";
            none.Font = Ui.Font(10);
            none.ForeColor = Ui.Gray;
            none.BackColor = Ui.Side;
            none.AutoSize = true;
            none.Location = new Point(12, 12);
            verList.Controls.Add(none);
        }
        verList.ResetScroll();
    }

    static Color Hsb(float h, float s, float br)
    {
        return Ui.Hsb(h, s, br);
    }

    static string ShortVer(string v)
    {
        // Крупная надпись на карточке — версия Minecraft как есть: «1.21.8»,
        // «26.3». У сборок («wonderful-1.21.8», «1.21.4-modpack») вытаскиваем
        // версию из имени, иначе карточки разных версий не различить.
        string s = Mc.McOf(v ?? "");          // «1.21.8-fabric» -> «1.21.8»
        if (string.IsNullOrEmpty(s)) s = v ?? "";
        for (int i = 0; i < s.Length; i++)
        {
            if (!char.IsDigit(s[i])) continue;
            int j = i;
            while (j < s.Length && (char.IsDigit(s[j]) || s[j] == '.')) j++;
            while (j > i && s[j - 1] == '.') j--;       // хвостовая точка
            string part = s.Substring(i, j - i);
            if (part.IndexOf('.') > 0) return part;     // «1.21», а не «8»
            i = j;
        }
        return s;
    }

    RoundedPanel MakeVerCard(string v, bool inst, string disp, string mtype, int idx)
    {
        string kind = Mc.KindOf(v);
        string mcv = Mc.McOf(v);
        string customName = disp ?? Mc.ModpackName(v);
        string packType = mtype ?? (Mc.IsModpack(v) ? Mc.ModpackType(v) : null);
        bool isOptiFine = v.ToLower().Contains("optifine")
            || (packType != null && packType.ToLower() == "optifine");
        string loaderName = isOptiFine ? "OptiFine" : (kind == "vanilla" ? "Vanilla" : (kind == "fabric" ? "Fabric" : (kind == "forge" ? "Forge" : (kind == "quilt" ? "Quilt" : "NeoForge"))));

        int hv = 0;
        foreach (char ch in v) hv += ch;
        // оттенок обложки — из бренд-коридора, а не сырой (сумма кодов
        // символов давала зелёные и жёлтые обложки версий)
        float hue = Ui.BrandHue(hv);

        // Карточка как в референсе: четыре в ряд, обложка сверху,
        // под ней версия, подпись-статус и ряд кнопок.
        int gap = 10;
        int listW = 660;
        try { listW = verList.ClientSize.Width; } catch { }
        if (listW < 320) listW = 320;
        // ширина карточки: ровно четыре колонки. Запас 40px — под
        // вертикальный скроллбар и округление, иначе четвёртая
        // карточка переносится на новую строку.
        int columns = listW >= 900 ? 4 : (listW >= 600 ? 3 : 2);
        int cardW = (listW - 40 - (columns - 1) * gap) / columns;
        if (cardW < 128) cardW = 128;
        if (cardW > 320) cardW = 320;
        // обложка тянется вместе с карточкой: на широком окне она
        // оставалась узкой полоской, а в референсе занимает половину
        int artH = (int)(cardW * 0.62f);
        if (artH < 92) artH = 92;
        if (artH > 132) artH = 132;
        int cardH = artH + 124;

        RoundedPanel p = new RoundedPanel();
        p.Radius = 14;
        p.BackColor = Ui.Card;
        p.Border = Ui.Line;
        // вертикальный градиент: верх карточки чуть светлее — появляется
        // «высота», карточки не сливаются в плоскую сетку
        p.Gradient = true;
        p.GradFrom = Color.FromArgb(23, 27, 44);
        p.GradTo = Ui.Card;
        p.HoverBack = Ui.CardHover;
        p.HoverBorder = Ui.Accent1;
        p.Animated = true;
        p.Size = new Size(cardW, cardH);
        p.Margin = new Padding(0, 0, gap, gap);
        p.Cursor = Cursors.Hand;

        // обложка-пейзаж (или PNG из covers), бейдж загрузчика лежит поверх неё
        CardHeader art = new CardHeader();
        art.Hue = hue;
        art.Big = "";
        art.Tag = loaderName;
        art.Img = Covers.For(v, mcv);
        art.Radius = 14;
        art.Location = new Point(0, 0);
        art.Size = new Size(cardW, artH);
        art.Cursor = Cursors.Hand;
        p.Controls.Add(art);

        PixelLabel ti = new PixelLabel();
        // У сборки в заголовке — её имя, а не технический id:
        // «better-mc-fabric-bmc2-fabric» игроку ничего не говорит.
        bool packCard = Mc.IsModpack(v) && !string.IsNullOrEmpty(customName);
        ti.Text = packCard ? Ui.Short(customName, 20) : ShortVer(v);
        ti.Font = Ui.Font(packCard ? 10.5f : 14, FontStyle.Bold);
        ti.ForeColor = Ui.Txt;
        ti.BackColor = Color.Transparent;
        ti.AutoSize = false;
        ti.Location = new Point(11, artH + 6);
        ti.Size = new Size(cardW - 20, 24);
        ti.Cursor = Cursors.Hand;
        p.Controls.Add(ti);

        // подпись: что это за сборка. В референсе у карточек разные
        // подписи («Последняя версия», «Стабильная версия», «Рекомендуемая»),
        // поэтому для чистых версий подпись варьируется, у первой карточки —
        // зелёная «Последняя версия».
        string sub = customName;
        Color subCol = Ui.GrayDim;
        if (packCard)
        {
            // имя уже в заголовке — здесь версия игры и загрузчик
            string baseMc = Mc.BaseMcOf(v);
            if (string.IsNullOrEmpty(baseMc) || !char.IsDigit(baseMc[0])) baseMc = mcv;
            sub = "Minecraft " + baseMc + " · " + loaderName;
        }
        else if (string.IsNullOrEmpty(sub))
        {
            if (packType == "crack") sub = "Крякнутая сборка";
            else if (packType == "optifine") sub = "OptiFine";
            else if (packType == "visual") sub = "Сборка с визуалом";
            else if (packType != null) sub = "Сборка с модами";
            else if (isOptiFine) sub = "OptiFine";
            else if (kind == "vanilla")
            {
                if (idx == 0) { sub = "Последняя версия"; subCol = Ui.Green; }
                else
                {
                    string[] subs = new string[] { "Чистая версия", "Стабильная версия", "Рекомендуемая", "Популярная", "Классика" };
                    int hv2 = 0;
                    foreach (char ch2 in v) hv2 += ch2 * 7 + 3;
                    sub = subs[Math.Abs(hv2) % subs.Length];
                }
            }
            else sub = loaderName + " • с модами";
        }
        Label subL = new Label();
        subL.Text = sub;
        subL.Font = Ui.Font(7.5f);
        subL.ForeColor = subCol;
        subL.BackColor = Color.Transparent;
        subL.AutoSize = false;
        subL.AutoEllipsis = true;
        subL.Location = new Point(11, artH + 30);
        subL.Size = new Size(cardW - 20, 15);
        subL.Cursor = Cursors.Hand;
        p.Controls.Add(subL);

        // статус: зелёная точка у установленной
        Label badge = new Label();
        badge.Text = inst ? "●  Установлена" : "○  Не скачана";
        badge.Font = Ui.Font(7, FontStyle.Bold);
        badge.ForeColor = inst ? Ui.Green : Ui.GrayDim;
        badge.BackColor = Color.Transparent;
        badge.AutoSize = false;
        badge.Location = new Point(11, artH + 46);
        badge.Size = new Size(cardW - 20, 14);
        p.Controls.Add(badge);

        string vv = v;
        EventHandler pick = delegate { SelectVersion(vv); };
        art.Click += pick;
        ti.Click += pick;
        subL.Click += pick;
        badge.Click += pick;

        int btnY = cardH - 44;
        int moreW = 32;
        int goW = cardW - 22 - moreW - 6;

        RoundedButton go;
        if (inst)
        {
            go = new RoundedButton();
            go.Text = "Играть";
            go.Icon = Ico.Play;
            go.IconSize = 13;
            go.IconGap = 7;
            go.Radius = 8;
            go.Glow = true;
            go.Font = Ui.Font(8.5f, FontStyle.Bold);
            go.FitText = true;
            go.Location = new Point(11, btnY);
            go.Size = new Size(goW, 32);
            go.Click += delegate { VerPlay(vv); };
        }
        else
        {
            go = RoundedButton.Dark("Скачать", 8);
            go.Icon = Ico.Download;
            go.IconSize = 13;
            go.IconGap = 7;
            go.Font = Ui.Font(8.5f, FontStyle.Bold);
            go.FitText = true;
            go.Location = new Point(11, btnY);
            go.Size = new Size(goW, 32);
            go.Click += delegate { VerInstall(vv); };
        }
        p.Controls.Add(go);

        RoundedButton more = RoundedButton.IcoBtn(Ico.More, moreW, 8);
        more.IconSize = 15;
        more.Location = new Point(11 + goW + 6, btnY);
        more.Size = new Size(moreW, 32);
        more.Click += delegate { VerMenu(vv, inst, more); };
        p.Controls.Add(more);

        return p;
    }

    // «…» на карточке: выбрать версию, открыть папку или удалить
    void VerMenu(string v, bool inst, Control anchor)
    {
        ContextMenuStrip m = new ContextMenuStrip();
        m.BackColor = Ui.Surface;
        m.ForeColor = Ui.Txt;
        m.Font = Ui.Font(9);
        m.Renderer = new ToolStripProfessionalRenderer();
        m.Items.Add("Выбрать версию", null, delegate { SelectVersion(v); });
        m.Items.Add("Открыть папку игры", null, delegate
        {
            try { Os.OpenFolder(Mc.GameDirFor(v)); }
            catch (Exception ex) { ShowError(ex.Message); }
        });
        if (inst)
            m.Items.Add("Удалить сборку", null, delegate { VerDelete(v); });
        try { m.Show(anchor, new Point(0, anchor.Height + 2)); } catch { }
    }

    void SelectVersion(string v)
    {
        for (int i = 0; i < cmbVer.Items.Count; i++)
        {
            if (cmbVer.Items[i].ToString().StartsWith(v)) { cmbVer.SelectedIndex = i; break; }
        }
        SetStatus("Выбрана версия " + v, true);
    }

    void VerPlay(string v)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(nick, "^[a-zA-Z0-9_]{3,16}$"))
        {
            ShowError("Сначала укажи ник в Аккаунтах: 3-16 символов a-z, 0-9, _");
            return;
        }
        SelectVersion(v);
        StartGame(v);
    }

    void VerInstall(string v)
    {
        if (busyDl)
        {
            SetStatus("Дождись окончания текущей загрузки...", false);
            return;
        }
        busyDl = true;
        SetStatus("Качаю " + v + "...", true);
        Thread t = new Thread((ThreadStart)delegate
        {
            try
            {
                if (javaExe == null) javaExe = Mc.FindLocalJava();
                int needJ = Mc.JavaFor(Mc.BaseMcOf(v));
                if (javaExe == null || Mc.GetJavaMajor(javaExe) != needJ) javaExe = Mc.EnsureJavaFor(Mc.BaseMcOf(v), SetStatus);
                string kind = Mc.KindOf(v);
                InstallResult rr = Mc.PrepareLaunch(v, SetStatus, SetProg);
                SetStatus(rr.Id + " установлена!", true);
                LoadVersions();
                SelectVersion(rr.Id);
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
            finally
            {
                busyDl = false;
                try
                {
                    if (bar.InvokeRequired) bar.Invoke(new Action(delegate { try { bar.Value = 0; } catch { } }));
                    else bar.Value = 0;
                }
                catch { }
            }
        });
        t.IsBackground = true;
        t.Start();
    }

    void VerDelete(string v)
    {
        try
        {
            string d = Path.Combine(Path.Combine(Cfg.McDir, "versions"), v);
            if (Directory.Exists(d)) Directory.Delete(d, true);
            SetStatus(v + " удалена", true);
            RefreshVersions();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    void OnPlay(object sender, EventArgs e)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(nick, "^[a-zA-Z0-9_]{3,16}$"))
        {
            ShowError("Сначала укажи ник в Аккаунтах: 3-16 символов a-z, 0-9, _");
            return;
        }
        if (cmbVer.SelectedItem == null)
        {
            ShowError("Выбери версию в списке выше");
            return;
        }
        string ver = cmbVer.SelectedItem.ToString().Split(' ')[0];
        StartGame(ver);
    }

    void StartGame(string ver)
    {
        StartGame(ver, null);
    }

    // joinAddress != null — сразу зайти на сервер/мир друга
    void StartGame(string ver, string joinAddress)
    {
        Mc.SaveConfig(nick, ver, ram, closeOnLaunch);
        // подарочная сборка: свой звук на запуск версии
        if (Cfg.GiftBuild) { try { Artem.PlaySound(3); } catch { } }
        btnPlay.Enabled = false;
        btnPlay.Text = "ЗАПУСК...";
        string n2 = nick, v2 = ver, r2 = ram;
        Thread t = new Thread((ThreadStart)delegate
        {
            try
            {
                if (javaExe == null) javaExe = Mc.FindLocalJava();
                int needJ = Mc.JavaFor(Mc.BaseMcOf(v2));
                if (javaExe == null || Mc.GetJavaMajor(javaExe) != needJ) javaExe = Mc.EnsureJavaFor(Mc.BaseMcOf(v2), SetStatus);
                // с лицензией играем под настоящим ником и токеном аккаунта,
                // иначе — как раньше, офлайн-запуск с токеном "0"
                string token = Auth.LoggedIn ? Auth.EnsureToken() : "";
                if (token.Length > 0 && Auth.Name.Length > 0) n2 = Auth.Name;
                string uuid = token.Length > 0 ? Auth.Uuid : Mc.GetOfflineUuid(n2);
                string xuid = token.Length > 0 ? Auth.Xuid : "";
                if (token.Length > 0) SetStatus("Запуск с лицензией: " + n2, true);
                InstallResult inst = Mc.PrepareLaunch(v2, SetStatus, SetProg);
                // плащи из лаунчера: подкладываем мод в mods/ этой сборки
                CapeMod.Ensure(v2, inst.GameDir, SetStatus);
                SetStatus("Запуск Minecraft...", true);
                List<string> jvm;
                string main;
                List<string> game;
                string cp;
                Mc.BuildArgs(inst, v2, n2, uuid, r2, token, xuid, out jvm, out main, out game, out cp);
                // автоподключение к миру друга: в 1.20+ это quickPlay,
                // в старых версиях — отдельные --server/--port
                if (!string.IsNullOrEmpty(joinAddress))
                {
                    string jh = AddressHost(joinAddress);
                    string jp = AddressPort(joinAddress);
                    if (Mc.CompareVersions(Mc.BaseMcOf(v2), "1.20") >= 0)
                    {
                        game.Add("--quickPlayMultiplayer");
                        game.Add(jh + ":" + jp);
                    }
                    else
                    {
                        game.Add("--server");
                        game.Add(jh);
                        game.Add("--port");
                        game.Add(jp);
                    }
                    SetStatus("Подключаюсь к " + jh + ":" + jp + "...", true);
                }
                StringBuilder sb = new StringBuilder();
                sb.Append("-cp \"" + cp + "\" ");
                foreach (string a in jvm) AppendArg(sb, a);
                sb.Append(main + " ");
                foreach (string gg in game) AppendArg(sb, gg);
                SetStatus("Запускаю Minecraft... окно игры появится через ~30-60 сек", true);
                string logDir = Path.Combine(Cfg.McDir, "logs");
                try { Directory.CreateDirectory(logDir); } catch { }
                string gameLog = Path.Combine(logDir, "launcher_last.log");
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = javaExe;
                psi.Arguments = sb.ToString().Trim();
                psi.WorkingDirectory = inst.GameDir;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                StreamWriter glog = new StreamWriter(gameLog, false, Encoding.UTF8);
                glog.WriteLine("# Luxe Launcher " + DateTime.Now + " ver=" + v2 + " nick=" + n2);
                glog.Flush();
                object glk = new object();
                Process gp = new Process();
                gp.StartInfo = psi;
                gp.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data != null) { lock (glk) { try { glog.WriteLine(e.Data); glog.Flush(); } catch { } } }
                };
                gp.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data != null) { lock (glk) { try { glog.WriteLine(e.Data); glog.Flush(); } catch { } } }
                };
                gp.Start();
                gp.BeginOutputReadLine();
                gp.BeginErrorReadLine();
                SetStatus("Minecraft запущен! Если окна нет 1-2 мин — смотри logs/launcher_last.log", true);
                Thread watch = new Thread((ThreadStart)delegate
                {
                    bool exited = gp.WaitForExit(60000);
                    try { glog.Close(); } catch { }
                    if (exited && gp.ExitCode != 0)
                    {
                        if (IsDisposed) return;
                        string tail = ReadTail(gameLog, 4);
                        ShowError("Игра закрылась с ошибкой (код " + gp.ExitCode + "). " + tail);
                    }
                });
                watch.IsBackground = true;
                watch.Start();
                if (closeOnLaunch && !LuxeTunnel.Active)
                {
                    SetStatus("Minecraft запущен! Закрываю лаунчер...", true);
                    Thread.Sleep(1500);
                    try
                    {
                        if (!IsDisposed && IsHandleCreated)
                        {
                            if (InvokeRequired) Invoke(new Action(delegate { Close(); }));
                            else Close();
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
            finally
            {
                try
                {
                    if (!IsDisposed && IsHandleCreated)
                    {
                        if (InvokeRequired) Invoke(new Action(delegate
                        {
                            btnPlay.Enabled = true;
                            btnPlay.Text = "▶  ИГРАТЬ";
                            try { bar.Value = 0; } catch { }
                        }));
                    }
                }
                catch { }
            }
        });
        t.IsBackground = true;
        t.Start();
    }

    static string ReadTail(string path, int n)
    {
        try
        {
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            int from = lines.Length - n;
            if (from < 0) from = 0;
            StringBuilder sb = new StringBuilder();
            for (int i = from; i < lines.Length; i++)
            {
                string s = lines[i];
                if (s.Length > 220) s = s.Substring(0, 220);
                sb.Append(s);
                sb.Append(" | ");
            }
            string r = sb.ToString();
            if (r.Length > 300) r = r.Substring(0, 300);
            return r;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    static void AppendArg(StringBuilder sb, string a)
    {
        if (a.Contains(" ")) sb.Append("\"" + a + "\" ");
        else sb.Append(a + " ");
    }
}

class PortraitBox : Control
{
    // Голова игрока из настоящей текстуры скина (PlayerSkin): лицо,
    // волосы и шапка-слой. Раньше здесь были две буквы ника — самая
    // заметная «заглушка» в шапке профиля.
    public string Text2 = "?";
    public PortraitBox()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // скин может приехать из сети уже после старта — перерисуемся
        PlayerSkin.Changed += OnSkinChanged;
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        try { PlayerSkin.Changed -= OnSkinChanged; } catch { }
        base.OnHandleDestroyed(e);
    }

    void OnSkinChanged()
    {
        try
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(delegate { if (!IsDisposed) Invalidate(); }));
        }
        catch { }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.HighQuality;
        // Голова игрока всегда: из настоящего скина, а пока он не
        // скачался — из встроенной заглушки. Инициалы убраны совсем:
        // две буквы выглядели как заглушка, голова — как персонаж.
        SitFigure.PaintHead(e.Graphics, ClientRectangle);
    }
}

static class Program
{
    static void EnsureExeName()
    {
        try
        {
            string me = Application.ExecutablePath;
            string want = Path.Combine(Path.GetDirectoryName(me), "Luxe Launcher.exe");
            if (!me.Equals(want, StringComparison.OrdinalIgnoreCase) && !File.Exists(want))
            {
                File.Copy(me, want);
                Process.Start(want);
                Environment.Exit(0);
            }
            if (me.Equals(want, StringComparison.OrdinalIgnoreCase))
            {
                string old = Path.Combine(Path.GetDirectoryName(me), "MinecraftLauncher.exe");
                if (File.Exists(old))
                {
                    try { File.Delete(old); } catch { }
                }
            }
        }
        catch { }
    }

    static void MigrateData()
    {
        try
        {
            // Переезд старой папки данных (Luxe Visuals рядом с exe) в
            // %APPDATA%. На том же диске это мгновенное переименование.
            // Если переименовать не вышло (файл занят, другой диск) —
            // остаёмся на старой папке, чтобы не потерять установки.
            try
            {
                string legacy = Path.Combine(Cfg.BaseDir, "Luxe Visuals");
                if (Directory.Exists(legacy)
                    && !string.Equals(legacy, Cfg.DataDir, StringComparison.OrdinalIgnoreCase))
                {
                    bool empty = true;
                    try { empty = Directory.GetFileSystemEntries(Cfg.DataDir).Length == 0; }
                    catch { empty = false; }
                    bool sameRoot = string.Equals(Path.GetPathRoot(legacy),
                        Path.GetPathRoot(Cfg.DataDir), StringComparison.OrdinalIgnoreCase);
                    bool moved = false;
                    if (empty && sameRoot)
                    {
                        // Directory.Move требует, чтобы целевой папки не было:
                        // пустую папку данных, созданную строкой выше, убираем.
                        try { if (Directory.Exists(Cfg.DataDir)) Directory.Delete(Cfg.DataDir); } catch { }
                        try { Directory.Move(legacy, Cfg.DataDir); moved = true; }
                        catch { }
                    }
                    if (!moved)
                    {
                        try { Directory.CreateDirectory(Cfg.DataDir); } catch { }
                        Cfg.DataDir = legacy;
                    }
                }
            }
            catch { }

            foreach (string d in Directory.GetDirectories(Cfg.BaseDir))
            {
                string n = Path.GetFileName(d).ToLower();
                if (n == ".minecraft" || n.StartsWith(".minecraft-") || n == ".jdk" || n.StartsWith(".jdk"))
                {
                    string dst = Path.Combine(Cfg.DataDir, Path.GetFileName(d));
                    if (!Directory.Exists(dst))
                    {
                        try { Directory.Move(d, dst); } catch { }
                    }
                }
            }
            string sc = Path.Combine(Cfg.BaseDir, "config.json");
            string dc = Path.Combine(Cfg.DataDir, "config.json");
            if (File.Exists(sc) && !File.Exists(dc))
            {
                try { File.Move(sc, dc); } catch { }
            }

            // Кэш картинок плащей и account.json раньше жили рядом с exe —
            // переносим их в папку данных, чтобы у лаунчера было всё в одном месте.
            string oldCapes = Path.Combine(Cfg.BaseDir, "capes");
            string newCapes = Path.Combine(Cfg.DataDir, "capes");
            if (Directory.Exists(oldCapes))
            {
                try
                {
                    if (!Directory.Exists(newCapes))
                    {
                        Directory.Move(oldCapes, newCapes);
                    }
                    else
                    {
                        foreach (string f2 in Directory.GetFiles(oldCapes))
                        {
                            string dst2 = Path.Combine(newCapes, Path.GetFileName(f2));
                            if (!File.Exists(dst2)) File.Move(f2, dst2);
                        }
                        try { Directory.Delete(oldCapes, true); } catch { }
                    }
                }
                catch { }
            }
            string oldAcc = Path.Combine(Cfg.BaseDir, "account.json");
            string newAcc = Path.Combine(Cfg.DataDir, "account.json");
            if (File.Exists(oldAcc) && !File.Exists(newAcc))
            {
                try { File.Move(oldAcc, newAcc); } catch { }
            }
        }
        catch { }
    }

    [STAThread]
    static void Main(string[] args)
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        ServicePointManager.DefaultConnectionLimit = 32;
        ServicePointManager.Expect100Continue = false;
        Cfg.BaseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
        EnsureExeName();
        // Данные лаунчера больше не создаются рядом с exe: настройки, кэш
        // скинов и плащей, .minecraft и Java живут в %APPDATA%\Luxe Launcher.
        // Раньше рядом с exe появлялись папки Luxe Visuals / covers / capes,
        // и при переносе одного exe настройки терялись.
        Cfg.DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Luxe Launcher");
        try { Directory.CreateDirectory(Cfg.DataDir); } catch { }
        MigrateData();
        Cfg.McDir = Path.Combine(Cfg.DataDir, ".minecraft");
        Cfg.JdkDir = Path.Combine(Cfg.DataDir, ".jdk");
        Cfg.ConfigPath = Path.Combine(Cfg.DataDir, "config.json");
        Mc.ExtractEmbeddedAssets();
        // вход в Microsoft: clientid.txt и account.json рядом с лаунчером
        try { Auth.Init(); } catch { }

        // скрытая проверка раздачи мода плащей (для отладки):
        //   LuxeLauncher.exe --cape-mod-check 1.21.8 <папка-игры>
        if (args.Length >= 3 && args[0] == "--cape-mod-check")
        {
            Luxe.Init();
            string f = CapeMod.FileFor(Mc.BaseMcOf(args[1]));
            string log = Cfg.LogPath("cape_mod_check.log");
            File.WriteAllText(log, "версия=" + args[1] + " файл=" + (f == null ? "(нет)" : f) + "\r\n",
                Encoding.UTF8);
            CapeMod.Ensure(args[1], args[2], null);
            string dest = f == null ? "" : Path.Combine(args[2], "mods", f);
            File.AppendAllText(log, "в mods: " + (File.Exists(dest) ? new FileInfo(dest).Length + " Б" : "(нет файла)") + "\r\n",
                Encoding.UTF8);
            return;
        }

        if (args.Length > 0 && args[0] == "--check-update")
        {
            string log = Cfg.LogPath("update_check.log");
            try
            {
                string url = Cfg.UpdateUrl();
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 12000;
                req.UserAgent = "LuxeLauncher";
                string txt;
                using (WebResponse resp = req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    txt = sr.ReadToEnd();
                Dictionary<string, object> js = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(txt);
                object vo;
                string remote = (js.TryGetValue("version", out vo) && vo != null) ? vo.ToString() : "";
                if (string.IsNullOrEmpty(remote) || Mc.CompareVersions(remote, Cfg.AppVersion) <= 0)
                {
                    File.WriteAllText(log, "NO-UPDATE local=" + Cfg.AppVersion + " remote=" + remote, Encoding.UTF8);
                    return;
                }
                object uo;
                string durl = (js.TryGetValue("url", out uo) && uo != null) ? uo.ToString() : "";
                if (string.IsNullOrEmpty(durl))
                {
                    File.WriteAllText(log, "ERROR no-url", Encoding.UTF8);
                    return;
                }
                string exePath = System.Reflection.Assembly.GetEntryAssembly().Location;
                string exeName = Path.GetFileName(exePath);
                string newName = Path.GetFileNameWithoutExtension(exeName) + ".new.exe";
                string newPath = Path.Combine(Cfg.DataDir, newName);
                new WebClient().DownloadFile(durl, newPath);
                FileInfo fi = new FileInfo(newPath);
                if (!fi.Exists || fi.Length < 10240)
                {
                    File.WriteAllText(log, "ERROR bad-file", Encoding.UTF8);
                    return;
                }
                StringBuilder bb = new StringBuilder();
                bb.AppendLine("@echo off");
                bb.AppendLine("timeout /t 3 /nobreak >nul");
                bb.AppendLine(":retry");
                // АБСОЛЮТНЫЕ пути: раньше были относительные, .bat работал
                // в папке данных — новый exe лёг рядом с данными, а
                // настоящий файл не заменялся.
                bb.AppendLine("move /Y \"" + newPath + "\" \"" + exePath + "\" >nul 2>&1");
                bb.AppendLine("if errorlevel 1 ( timeout /t 2 /nobreak >nul & goto retry )");
                bb.AppendLine("start \"\" \"" + exePath + "\"");
                bb.AppendLine("(goto) 2>nul & del \"%~f0\"");
                Os.StartApplyScript(newPath, exePath, Cfg.DataDir);
                File.WriteAllText(log, "UPDATE-APPLIED " + remote, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(log, "ERROR " + ex.Message, Encoding.UTF8); } catch { }
            }
            return;
        }

        if (args.Length > 0 && args[0] == "--install-test")
        {
            string ver = args.Length > 1 ? args[1] : "1.20.1-fabric";
            string log = Cfg.LogPath("install_test.log");
            try
            {
                using (StreamWriter w = new StreamWriter(log, false, Encoding.UTF8))
                {
                    Mc.EnsureJavaFor(Mc.McOf(ver), delegate(string s) { w.WriteLine("[java] " + s); w.Flush(); });
                    InstallResult inst = Mc.PrepareLaunch(ver,
                        delegate(string s) { w.WriteLine("[inst] " + s); w.Flush(); },
                        delegate(int a, int b) { });
                    w.WriteLine("INSTALL-OK id=" + inst.Id + " cp=" + inst.Classpath.Count + " main=" + inst.VersionJson["mainClass"].ToString());
                }
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(log, "FATAL: " + ex.ToString(), Encoding.UTF8); } catch { }
            }
            return;
        }

        if (args.Length > 0 && args[0] == "--launch-test")
        {
            string ver = args.Length > 1 ? args[1] : Mc.LoadConfig()["version"];
            if (string.IsNullOrEmpty(ver)) ver = "1.20.1";
            string log = Cfg.LogPath("launch_test.log");
            try
            {
                using (StreamWriter w = new StreamWriter(log, false, Encoding.UTF8))
                {
                    w.WriteLine("ver=" + ver);
                    string java = Mc.EnsureJavaFor(Mc.McOf(ver), delegate(string s) { w.WriteLine("[java] " + s); w.Flush(); });
                    w.WriteLine("java=" + java);
                    InstallResult inst = Mc.PrepareLaunch(ver,
                        delegate(string s) { w.WriteLine("[inst] " + s); w.Flush(); },
                        delegate(int a, int b) { });
                    string uuid = Mc.GetOfflineUuid("Player");
                    List<string> jvm;
                    string main;
                    List<string> game;
                    string cp;
                    Mc.BuildArgs(inst, ver, "Player", uuid, "2", out jvm, out main, out game, out cp);
                    StringBuilder sb = new StringBuilder();
                    sb.Append("-cp \"" + cp + "\" ");
                    foreach (string a in jvm) { if (a.Contains(" ")) sb.Append("\"" + a + "\" "); else sb.Append(a + " "); }
                    sb.Append(main + " ");
                    foreach (string gg in game) { if (gg.Contains(" ")) sb.Append("\"" + gg + "\" "); else sb.Append(gg + " "); }
                    w.WriteLine("CMD: " + java + " " + sb.ToString());
                    w.Flush();
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = java;
                    psi.Arguments = sb.ToString().Trim();
                    psi.WorkingDirectory = inst.GameDir;
                    psi.UseShellExecute = false;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    psi.CreateNoWindow = true;
                    object lk = new object();
                    Process p = new Process();
                    p.StartInfo = psi;
                    p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) { lock (lk) { try { w.WriteLine(e.Data); w.Flush(); } catch { } } }
                    };
                    p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) { lock (lk) { try { w.WriteLine("[ERR] " + e.Data); w.Flush(); } catch { } } }
                    };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    bool exited = p.WaitForExit(100000);
                    if (exited)
                    {
                        w.WriteLine("EXIT-CODE=" + p.ExitCode);
                    }
                    else
                    {
                        w.WriteLine("RUNNING-OK-AFTER-100S");
                        try { p.Kill(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(log, "FATAL: " + ex.ToString(), Encoding.UTF8); } catch { }
            }
            return;
        }

        if (args.Length > 0 && args[0] == "--shot-run")
        {
            // Прогон по БОЕВОМУ пути: Application.Run + EnableVisualStyles,
            // в отличие от --shot-test, который показывает форму вручную.
            Cfg.Dbg = true;
            try { File.WriteAllText(Path.Combine(Cfg.BaseDir, "shot_args.txt"), string.Join("|", args), Encoding.UTF8); } catch { }
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                {
                    try { File.AppendAllText(Path.Combine(Cfg.BaseDir, "shot_test_err.txt"),
                        "\r\nUI: " + e.Exception, Encoding.UTF8); } catch { }
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    try { File.AppendAllText(Path.Combine(Cfg.BaseDir, "shot_test_err.txt"),
                        "\r\nBG: " + (e.ExceptionObject as Exception), Encoding.UTF8); } catch { }
                };
                // Отладочные хвосты: "skin:путь.png" — открыть этот скин в редакторе,
                // "demo3d" — ткнуть по модели перед снимком. Разбор — до создания
                // формы, потому что разделы строятся уже в её конструкторе.
                foreach (string a in args)
                {
                    if (a.StartsWith("skin:")) LauncherForm.DbgSkinFile = a.Substring(5);
                    else if (a == "demo3d") LauncherForm.DbgSkinDemo = true;
                    else if (a == "nospin") LauncherForm.DbgSkinNoSpin = true;
                    else if (a == "dumpskin") LauncherForm.DbgSkinDump = true;
                    else if (a == "readonly") LauncherForm.DbgSkinReadOnly = true;
                    else if (a.StartsWith("capetab:"))
                    {
                        try { LauncherForm.DbgCapeTab = Convert.ToInt32(a.Substring(8)); } catch { }
                    }
                    else if (a == "skinupload") LauncherForm.DbgSkinUpload = true;
                    else if (a.StartsWith("scrollto:"))
                    {
                        // scrollto:N - прокрутить активный список в самый низ
                        try { LauncherForm.DbgScrollTo = Convert.ToInt32(a.Substring(9)); } catch { }
                    }
                    else if (a.StartsWith("settings:"))
                    {
                        // settings:N - открыть настройки на вкладке N (снимки)
                        try { LauncherForm.DbgSettingsTab = Convert.ToInt32(a.Substring(9)); } catch { }
                    }
                    else if (a.StartsWith("shotwait:"))
                    {
                        // shotwait:N - подождать N секунд перед снимком
                        try { LauncherForm.DbgShotWait = Convert.ToInt32(a.Substring(9)); } catch { }
                    }
                    else if (a.StartsWith("aiclick:"))
                    {
                        // aiclick:Подпись - нажать кнопку под ответом помощника
                        LauncherForm.DbgAiClick = a.Substring(8);
                    }
                    else if (a == "aihide") LauncherForm.DbgAiHide = true;
                    else if (a == "aisetup") LauncherForm.DbgAiShowSetup = true;
                    else if (a == "ai")
                    {
                        LauncherForm.DbgAiOpen = true;
                    }
                    else if (a.StartsWith("ai:"))
                    {
                        LauncherForm.DbgAiOpen = true;
                        LauncherForm.DbgAiQuery = a.Substring(3);
                    }
                    else if (a == "skinoff") LauncherForm.DbgSkinOffline = true;
                    else if (a == "updcheck") LauncherForm.DbgUpdCheck = true;
                    else if (a == "skinget") LauncherForm.DbgSkinGet = true;
                    else if (a == "skinclear") LauncherForm.DbgSkinClear = true;
                    else if (a == "socdemo") LauncherForm.DbgSocialDemo = true;
                    else if (a.StartsWith("soc:"))
                    {
                        // soc:N - открыть вкладку внутри «Друзей» (снимки)
                        try { LauncherForm.DbgFriendsTab = Convert.ToInt32(a.Substring(4)); } catch { }
                    }
                }
                LauncherForm f = new LauncherForm();
                int shotTab = 0, shotView = -1;
                if (args.Length > 1) { try { shotTab = Convert.ToInt32(args[1]); } catch { } }
                if (args.Length > 2) { try { shotView = Convert.ToInt32(args[2]); } catch { } }
                int vt = shotTab, vv = shotView;
                // "2,1,0,2" — кликаем по очереди, снимок последнего: проверка
                // повторного показа раздела после переключений туда-обратно
                int[] seq = null;
                try
                {
                    if (args.Length > 2 && args[2].IndexOf(',') >= 0)
                        seq = Array.ConvertAll(args[2].Split(','), delegate(string s) { return Convert.ToInt32(s.Trim()); });
                }
                catch { }
                int[] steps = seq ?? new int[] { vv };
                // необязательный 4-й аргумент "1080x680" — проверить вёрстку
                // на минимальном размере окна
                Size shotSize = Size.Empty;
                try
                {
                    if (args.Length > 3 && args[3].IndexOf('x') > 0)
                    {
                        string[] wh = args[3].Split('x');
                        shotSize = new Size(Convert.ToInt32(wh[0]), Convert.ToInt32(wh[1]));
                    }
                }
                catch { }
                Size ssz = shotSize;
                f.Shown += delegate
                {
                    if (!ssz.IsEmpty) { try { f.Size = ssz; } catch { } }
                    System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
                    t.Interval = 1200;
                    int ticks = 0;
                    bool demoDone = false;
                    bool upDone = false;
                    bool getDone = false;
                    bool clrDone = false;
                    t.Tick += delegate
                    {
                        ticks++;
                        int si = (ticks - 1) / 2;
                        if (LauncherForm.DbgSkinDemo && !demoDone && f.DebugGetView() == LauncherForm.ViewSkin)
                        {
                            demoDone = true;
                            try { f.DebugSkin3dDemo(); } catch { }
                        }
                        if (LauncherForm.DbgSkinUpload && !upDone && f.DebugGetView() == LauncherForm.ViewSkin)
                        {
                            upDone = true;
                            try { f.DebugSkinUpload(); } catch { }
                        }
                        if (LauncherForm.DbgSkinGet && !getDone && f.DebugGetView() == LauncherForm.ViewSkin)
                        {
                            getDone = true;
                            try { f.DebugSkinGet(); } catch { }
                        }
                        if (LauncherForm.DbgSkinClear && !clrDone && f.DebugGetView() == LauncherForm.ViewSkin)
                        {
                            clrDone = true;
                            try { f.DebugSkinClear(); } catch { }
                        }
                        if (LauncherForm.DbgCapeTab >= 0 && f.DebugGetView() == LauncherForm.ViewCapes)
                        {
                            int ct = LauncherForm.DbgCapeTab;
                            LauncherForm.DbgCapeTab = -2;      // применили один раз
                            try { f.DebugCapeTab(ct); } catch { }
                        }
                        if (LauncherForm.DbgFriendsTab >= 0 && f.DebugGetView() == LauncherForm.ViewFriends)
                        {
                            int ft = LauncherForm.DbgFriendsTab;
                            LauncherForm.DbgFriendsTab = -2;
                            try { f.DebugFriendsTab(ft); } catch { }
                        }
                        if (LauncherForm.DbgScrollTo >= 0)
                        {
                            int target = LauncherForm.DbgScrollTo;
                            LauncherForm.DbgScrollTo = -1;
                            try { f.DebugScrollActive(target); } catch { }
                        }
                        // a0 приветствие, a1 поздравление, a2 "понял",                        // a3 задание, a10 самолёт, a11 скример
                        bool artemStepArg = steps[0] < -100;
                        if (artemStepArg)
                        {
                            if (si < steps.Length) { try { f.DebugArtem(-steps[si] - 100); } catch { } }
                        }
                        else if (si < steps.Length)
                        {
                            int st = steps[si];
                            // 200+ — вкладка внутри «Друзей» (Друзья/Заявки/Вместе),
                            // 100+ — клик по вкладке главного экрана (чипы),
                            // иначе — клик по навигации
                            if (st >= 200) { try { f.DebugFriendsTab(st - 200); } catch { } }
                            else if (st >= 100) { try { f.DebugSwitchTab(st - 100); } catch { } }
                            else { try { f.DebugClickNav(st); } catch { } }
                        }
                        // Проверка скролла — ПОСЛЕ навигации, иначе активный
                        // список ещё не тот. Повторяем трижды: так ловится
                        // «упирается и дёргается» при повторной прокрутке.
                        if (LauncherForm.DbgScrollTo >= 0)
                        {
                            int target = LauncherForm.DbgScrollTo;
                            LauncherForm.DbgScrollTo = -1;
                            try
                            {
                                f.DebugScrollActive(target);
                                f.DebugScrollActive(target);
                                f.DebugScrollActive(target);
                            }
                            catch { }
                        }
                        // при отладке отправки скина даём потоку время ответить
                        if (LauncherForm.DbgSettingsTab >= 0)
                        {
                            int st2 = LauncherForm.DbgSettingsTab;
                            LauncherForm.DbgSettingsTab = -1;
                            try { f.DebugOpenSettings(st2); } catch { }
                        }
                        if (LauncherForm.DbgAiOpen)
                        {
                            LauncherForm.DbgAiOpen = false;
                            string aq = LauncherForm.DbgAiQuery;
                            LauncherForm.DbgAiQuery = null;
                            try { f.DebugOpenAi(aq); } catch { }
                        }
                        // проверка обновлений в отладочном прогоне: вкладка
                        // уже открыта, жмём кнопку её же методом
                        if (LauncherForm.DbgUpdCheck && ticks >= 8)
                        {
                            LauncherForm.DbgUpdCheck = false;
                            try { f.DebugCheckUpdates(); } catch { }
                        }
                        // кнопку чата жмём через такт: к этому времени
                        // ответ с кнопками уже построен
                        if (LauncherForm.DbgAiClick != null && ticks >= 4)
                        {
                            string lbl = LauncherForm.DbgAiClick;
                            LauncherForm.DbgAiClick = null;
                            try { f.DebugAiClick(lbl); } catch { }
                        }
                        if (LauncherForm.DbgAiHide && ticks >= 5)
                        {
                            LauncherForm.DbgAiHide = false;
                            try { f.DebugHideOverlays(); } catch { }
                        }
                        if (LauncherForm.DbgAiShowSetup && ticks >= 3)
                        {
                            LauncherForm.DbgAiShowSetup = false;
                            try { f.DebugAiSetup(); } catch { }
                        }
                        if (ticks < 6 + steps.Length * 2 + LauncherForm.DbgShotWait + (LauncherForm.DbgSkinUpload || LauncherForm.DbgSkinGet || LauncherForm.DbgSkinClear ? 6 : 0)) return;
                        t.Stop();
                        if (LauncherForm.DbgSkinDump)
                        {
                            LauncherForm.DbgSkinDump = false;
                            try { f.DebugDumpSkin(Path.Combine(Cfg.BaseDir, "skin_dump.png")); } catch { }
                        }
                        // ВАЖНО: снимок берём с ЭКРАНА, а не через DrawToBitmap.
                        // DrawToBitmap шлёт WM_PRINT и принудительно перерисовывает
                        // всё дерево — из-за него разделы «рисовались» даже там,
                        // где на экране их не было.
                        try
                        {
                            // если окно успело свернуться (пользователь, ОС) —
                            // разворачиваем, иначе снимок выйдет пустым
                            try
                            {
                                if (f.WindowState == FormWindowState.Minimized)
                                    f.WindowState = FormWindowState.Normal;
                            }
                            catch { }
                            f.TopMost = true;
                            f.Activate();
                            f.Refresh();
                            Application.DoEvents();
                            System.Threading.Thread.Sleep(700);
                            Application.DoEvents();
                            Rectangle scr = f.Bounds;
                            using (Bitmap bmp = new Bitmap(scr.Width, scr.Height))
                            {
                                using (Graphics gs = Graphics.FromImage(bmp))
                                    gs.CopyFromScreen(scr.Location, Point.Empty, scr.Size);
                                bmp.Save(Path.Combine(Cfg.BaseDir, "shot_test.png"), System.Drawing.Imaging.ImageFormat.Png);
                            }
                            // второй снимок — прямо из окна через PrintWindow.
                            // Экранный снимок может поймать залипшие куски
                            // композитора, здесь видно только то, что нарисовало
                            // само приложение.
                            try { ShotWin.Save(f.Handle, Path.Combine(Cfg.BaseDir, "shot_print.png")); } catch { }
                            f.TopMost = false;
                            // отдельный снимок всего экрана: видно, что ещё
                            // висит поверх окна лаунчера
                            try
                            {
                                Rectangle vs = System.Windows.Forms.SystemInformation.VirtualScreen;
                                using (Bitmap dbg = new Bitmap(vs.Width, vs.Height))
                                {
                                    using (Graphics gd = Graphics.FromImage(dbg))
                                        gd.CopyFromScreen(vs.Location, Point.Empty, vs.Size);
                                    dbg.Save(Path.Combine(Cfg.BaseDir, "shot_desktop.png"), System.Drawing.Imaging.ImageFormat.Png);
                                }
                            }
                            catch { }
                        }
                        catch (Exception ex)
                        {
                            try { File.AppendAllText(Path.Combine(Cfg.BaseDir, "shot_args.txt"), "\r\nSHOT-ERR " + ex.Message, Encoding.UTF8); } catch { }
                        }
                        try { f.DebugDumpControls(Path.Combine(Cfg.BaseDir, "controls.txt")); } catch { }
                        try
                        {
                            File.AppendAllText(Path.Combine(Cfg.BaseDir, "shot_args.txt"),
                                "\r\nFINAL " + f.GetNewsDiag(), Encoding.UTF8);
                        }
                        catch { }
                        Application.Exit();
                    };
                    t.Start();
                };
                Application.Run(f);
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(Cfg.BaseDir, "shot_test_err.txt"), ex.ToString(), Encoding.UTF8); } catch { }
            }
            return;
        }

        if (args.Length > 0 && args[0] == "--shot-test")
        {
            try { File.WriteAllText(Path.Combine(Cfg.BaseDir, "shot_args.txt"), string.Join("|", args), Encoding.UTF8); } catch { }
            try
            {
                LauncherForm f = new LauncherForm();
                // НЕ используем Opacity=0: при нулевой прозрачности форма не
                // считается показанной и WinForms не проставляет Visible
                // вложенным панелям. Просто уводим окно за экран.
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-32000, -32000);
                if (args.Length > 1)
                {
                    try { f.loaderTab = Convert.ToInt32(args[1]); } catch { }
                }
                f.Show();
                try { f.DebugRefreshTab(); } catch { }
                if (args.Length > 1 && args[1] == "news") { try { f.ShowView(LauncherForm.ViewNews); } catch (Exception ex2) { try { File.AppendAllText(Path.Combine(Cfg.BaseDir, "shot_args.txt"), "\r\nSHOWVIEW-ERR " + ex2.Message, Encoding.UTF8); } catch { } } }
                if (args.Length > 1 && args[1] == "news") { try { f.SaveNewsShot(Path.Combine(Cfg.BaseDir, "shot_news.png")); } catch (Exception ex3) { try { File.AppendAllText(Path.Combine(Cfg.BaseDir, "shot_args.txt"), "\r\nSHOTNEWS-ERR " + ex3.Message, Encoding.UTF8); } catch { } } }
                try
                {
                    File.AppendAllText(Path.Combine(Cfg.BaseDir, "shot_args.txt"), "\r\n" + f.GetNewsDiag(), Encoding.UTF8);
                }
                catch { }
                f.Refresh();
                Application.DoEvents();
                // отладка смены разделов/вкладок
                System.Threading.Thread.Sleep(3000);
                Application.DoEvents();
                for (int ai = 2; ai < args.Length; ai++)
                {
                    if (args[ai].StartsWith("switch:"))
                    {
                        try { f.DebugSwitchTab(Convert.ToInt32(args[ai].Substring(7))); } catch { }
                    }
                    else if (args[ai].StartsWith("view:"))
                    {
                        try { f.DebugShowView(Convert.ToInt32(args[ai].Substring(5))); } catch { }
                    }
                }
                System.Threading.Thread.Sleep(11000);
                // прокачиваем очередь сообщений: фоновые Invoke выполняются только тут
                for (int drain = 0; drain < 60; drain++)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(200);
                }
                try
                {
                    File.AppendAllText(Path.Combine(Cfg.BaseDir, "shot_args.txt"),
                        "\r\nFINAL " + f.GetNewsDiag(), Encoding.UTF8);
                }
                catch { }
                Rectangle r = f.ClientRectangle;
                if (r.Width <= 0 || r.Height <= 0)
                {
                    try { f.PerformLayout(); f.Refresh(); Application.DoEvents(); } catch { }
                    r = f.ClientRectangle;
                }
                try
                {
                    File.AppendAllText(Path.Combine(Cfg.BaseDir, "shot_args.txt"),
                        "\r\nSHOT rect=" + r.Width + "x" + r.Height + " handle=" + f.IsHandleCreated, Encoding.UTF8);
                }
                catch { }
                if (r.Width > 0 && r.Height > 0)
                {
                    using (Bitmap b = new Bitmap(r.Width, r.Height))
                    {
                        f.DrawToBitmap(b, r);
                        b.Save(Path.Combine(Cfg.BaseDir, "shot_test.png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                try { f.Close(); } catch { }
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(Cfg.BaseDir, "shot_test_err.txt"), ex.ToString(), Encoding.UTF8); } catch { }
            }
            return;
        }

        if (args.Length > 0 && args[0] == "--steve-test")
        {
            try
            {
                PlayerSkin.Init("dadsadxcva");
                using (Bitmap b = new Bitmap(760, 460))
                {
                    using (Graphics g = Graphics.FromImage(b))
                    {
                        g.Clear(Color.FromArgb(20, 22, 27));
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        // старый векторный Стив для сравнения
                        Ui.DrawSteve(g, 40, 40, 6);
                        // новая фигура из текстуры скина, крупно
                        SitFigure.Paint(g, new Rectangle(220, 20, 220, 420), 0.7f);
                        SitFigure.Paint(g, new Rectangle(500, 20, 220, 420), 2.1f);
                    }
                    b.Save(Path.Combine(Cfg.BaseDir, "steve_test.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                // дамп самой текстуры: видно, что реально лежит в развёртке
                Bitmap tx = PlayerSkin.Tex;
                if (tx != null)
                {
                    using (Bitmap big = new Bitmap(512, 512))
                    using (Graphics g = Graphics.FromImage(big))
                    {
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.Clear(Color.FromArgb(40, 40, 52));
                        g.DrawImage(tx, new Rectangle(0, 0, 512, 512), new Rectangle(0, 0, 64, 64), GraphicsUnit.Pixel);
                        big.Save(Path.Combine(Cfg.BaseDir, "skin_debug.png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(Cfg.BaseDir, "steve_test_err.txt"), ex.ToString(), Encoding.UTF8); } catch { }
            }
            return;
        }

        if (args.Length > 0 && args[0] == "--logo-test")
        {
            // рендер логотипа в PNG: используется сборкой для
            // перегенерации logo.png/logo.ico из того же кода, что
            // рисует значок в интерфейсе
            try
            {
                int size = 512;
                try { if (args.Length > 1) size = Convert.ToInt32(args[1]); } catch { }
                using (Bitmap b = new Bitmap(size, size))
                {
                    using (Graphics g = Graphics.FromImage(b))
                        Art.Logomark(g, new Rectangle(0, 0, size, size));
                    b.Save(Path.Combine(Cfg.BaseDir, "logo_test.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(Cfg.BaseDir, "steve_test_err.txt"), ex.ToString(), Encoding.UTF8); } catch { }
            }
            return;
        }

        if (args.Length > 0 && (args[0] == "--check" || args[0] == "--version"))
        {
            try
            {
                string uuid = Mc.GetOfflineUuid("Steve2026");
                string ok = "MinecraftLauncher OK base=" + Cfg.BaseDir + " uuid=" + uuid;
                try { File.WriteAllText(Path.Combine(Cfg.BaseDir, "check_ok.txt"), ok, Encoding.UTF8); } catch { }
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(Cfg.BaseDir, "check_ok.txt"), "FAIL " + ex.Message, Encoding.UTF8); } catch { }
            }
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
        {
            try
            {
                File.WriteAllText(Cfg.LogPath("error.log"), e.Exception.ToString(), Encoding.UTF8);
                MessageBox.Show(e.Exception.Message, "Ошибка лаунчера (см. error.log)");
            }
            catch { }
        };
        AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
        {
            try
            {
                Exception ex = e.ExceptionObject as Exception;
                File.WriteAllText(Cfg.LogPath("error.log"), ex != null ? ex.ToString() : "Unknown", Encoding.UTF8);
            }
            catch { }
        };
        try
        {
            Application.Run(new LauncherForm());
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Cfg.LogPath("error.log"), ex.ToString(), Encoding.UTF8); } catch { }
            MessageBox.Show(ex.Message + "\n\nПодробности в error.log", "Ошибка лаунчера");
        }
    }
}

// ============================================================
//  Отладочный снимок окна через PrintWindow
//
//  Нужен, чтобы отличить «приложение нарисовало» от «композитор
//  показал залипшие куски старых окон». Работает только в режиме
//  --shot-run, на боевой код не влияет.
// ============================================================
static class ShotWin
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct RECT { public int L, T, R, B; }

    public static void Save(IntPtr hwnd, string path)
    {
        if (!Os.IsWin) return;
        RECT r;
        if (!GetWindowRect(hwnd, out r)) return;
        int w = r.R - r.L, h = r.B - r.T;
        if (w <= 0 || h <= 0) return;
        using (Bitmap bmp = new Bitmap(w, h))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                PrintWindow(hwnd, hdc, 2);      // PW_RENDERFULLCONTENT
                g.ReleaseHdc(hdc);
            }
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}


