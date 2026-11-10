using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

/// <summary>
/// Мод скинов и плащей: перед запуском сборки кладём в её mods/ тот
/// джарник, который подходит версии Minecraft. У Mojang нет ни своего
/// плаща, ни места для скина офлайн-игрока, поэтому и то и другое
/// показывает этот мод — видят его только пользователи лаунчера.
///
/// Мод не вшит в лаунчер: он лежит на сервисе четырьмя сборками
/// (1.21-1.21.1, 1.21.2-1.21.8, 1.21.9-1.21.11, 26.3), качается один раз
/// и кэшируется в "Luxe Visuals\mods".
///
/// Если сервис недоступен — просто ничего не кладём: игра запустится
/// как обычно, просто без скинов и плащей.
/// </summary>
static class CapeMod
{
    // версия Minecraft -> имя файла. Обновляется из сервиса (GET /api/mods),
    // а если сервис молчит — работает запасной список ниже.
    static Dictionary<string, string> map = null;
    static DateTime mapAt = DateTime.MinValue;

    // имя файла -> sha256 из сервиса: проверяем, что скачалось то, что нужно
    static Dictionary<string, string> shas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Где кэшируем джарники — рядом с остальными файлами лаунчера.</summary>
    public static string CacheDir()
    {
        string root = Cfg.DataDir;
        if (root == null || root == "") root = Cfg.BaseDir;
        return Path.Combine(root, "mods");
    }

    /// <summary>Какая сборка мода нужна этой версии Minecraft. null — не знаем.</summary>
    public static string FileFor(string mc)
    {
        if (mc == null || mc == "") return null;
        // если нужная сборка уже качалась — в сеть не идём совсем
        string b = BuiltinFile(mc);
        if (b != null && GoodCached(b)) return b;
        if (map == null || (DateTime.Now - mapAt).TotalMinutes > 30) RefreshMap();
        if (map != null)
        {
            string f;
            if (map.TryGetValue(mc, out f) && !string.IsNullOrEmpty(f)) return f;
        }
        return b;
    }

    /// <summary>Спросить у сервиса, какие сборки есть сейчас.</summary>
    static void RefreshMap()
    {
        mapAt = DateTime.Now;
        try
        {
            string txt = Get("/api/mods", 4000);
            if (txt == null) return;
            Dictionary<string, object> root =
                new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(txt);
            if (root == null) return;
            object list;
            if (!root.TryGetValue("mods", out list)) return;
            ArrayList arr = list as ArrayList;
            if (arr == null) return;
            Dictionary<string, string> m = new Dictionary<string, string>();
            foreach (object item in arr)
            {
                Dictionary<string, object> e = item as Dictionary<string, object>;
                if (e == null) continue;
                object fo;
                string file = e.TryGetValue("file", out fo) ? Convert.ToString(fo) : "";
                if (string.IsNullOrEmpty(file)) continue;
                object so;
                string sha = e.TryGetValue("sha256", out so) ? Convert.ToString(so) : "";
                if (!string.IsNullOrEmpty(sha)) shas[file] = sha;
                object vs;
                if (!e.TryGetValue("versions", out vs)) continue;
                ArrayList varr = vs as ArrayList;
                if (varr == null) continue;
                foreach (object v in varr)
                {
                    string s = Convert.ToString(v);
                    if (!string.IsNullOrEmpty(s)) m[s] = file;
                }
            }
            if (m.Count > 0) map = m;
        }
        catch { }
    }

    /// <summary>
    /// Запасной список на случай, если сервис молчит: сборки 1.21.x и 26.3,
    /// которые уже собраны. Совсем новые версии (26.4 и дальше) он не знает —
    /// их возьмёт из списка сервиса.
    /// </summary>
    static string BuiltinFile(string mc)
    {
        try
        {
            // 26.3: новая нумерация Minecraft, отдельная сборка мода
            if (mc == "26.3") return "luxecape-1.2.0-26.3.jar";
            if (mc == "1.21") return "luxecape-1.1.0-1.21-1.21.1.jar";
            string[] p = mc.Split('.');
            if (p.Length != 3 || p[0] != "1" || p[1] != "21") return null;
            int patch;
            if (!int.TryParse(p[2], out patch)) return null;
            if (patch <= 1) return "luxecape-1.1.0-1.21-1.21.1.jar";
            if (patch <= 8) return "luxecape-1.1.0-1.21.2-1.21.8.jar";
            if (patch <= 11) return "luxecape-1.1.0-1.21.9-1.21.11.jar";
            return null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Положить мод в mods/ этой сборки. Молча ничего не делает, если
    /// версия не подходит или сервис недоступен — запуск важнее.
    /// </summary>
    public static void Ensure(string ver, string gameDir, Action<string, bool> status)
    {
        try
        {
            string kind = Mc.KindOf(ver);
            if (kind != "fabric" && kind != "quilt") return;  // мод скинов и плащей — только для Fabric
            if (gameDir == null || gameDir == "") return;
            string file = FileFor(Mc.BaseMcOf(ver));
            if (file == null) return;

            string cache = Path.Combine(CacheDir(), file);
            if (!GoodCached(file))
            {
                if (status != null) status("Скачиваю мод скинов и плащей...", true);
                Directory.CreateDirectory(CacheDir());
                string tmp = cache + ".tmp";
                bool got = Download(file, tmp);
                // полоса прогресса в витрине не должна остаться на последнем %
                if (Mc.Progress != null) { try { Mc.Progress(-1, -1, null); } catch { } }
                if (!got) return;
                // если сервис сказал sha256 — сверяем: битый джарник в mods/ не нужен
                string want;
                if (shas.TryGetValue(file, out want) && want.Length > 0 &&
                    !string.Equals(want, Sha256(tmp), StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(tmp); } catch { }
                    return;
                }
                if (File.Exists(cache)) File.Delete(cache);
                File.Move(tmp, cache);
                try { File.WriteAllText(cache + ".sha256", Sha256(cache)); } catch { }
            }
            if (!Good(cache)) return;

            string mods = Path.Combine(gameDir, "mods");
            Directory.CreateDirectory(mods);
            CleanOld(mods, file);
            string dest = Path.Combine(mods, file);
            if (!Same(dest, cache)) File.Copy(cache, dest, true);
        }
        catch { }
    }

    /// <summary>Убираем другие сборки мода: после смены версии их не должно быть две.</summary>
    static void CleanOld(string mods, string keep)
    {
        try
        {
            string[] found = Directory.GetFiles(mods, "luxecape-*.jar");
            foreach (string f in found)
            {
                if (string.Equals(Path.GetFileName(f), keep, StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(f); } catch { }
            }
        }
        catch { }
    }

    static bool Same(string a, string b)
    {
        try
        {
            if (!File.Exists(a) || !File.Exists(b)) return false;
            return new FileInfo(a).Length == new FileInfo(b).Length;
        }
        catch { return false; }
    }

    static bool Good(string path)
    {
        try { return File.Exists(path) && new FileInfo(path).Length > 0; }
        catch { return false; }
    }

    /// <summary>Кэш на месте и не побился: рядом лежит контрольная сумма.</summary>
    static bool GoodCached(string file)
    {
        string path = Path.Combine(CacheDir(), file);
        if (!Good(path)) return false;
        try
        {
            string side = path + ".sha256";
            if (!File.Exists(side)) return true;
            string want = File.ReadAllText(side).Trim();
            return want == "" || string.Equals(want, Sha256(path), StringComparison.OrdinalIgnoreCase);
        }
        catch { return true; }
    }

    static string Sha256(string path)
    {
        try
        {
            using (FileStream f = File.OpenRead(path))
            using (System.Security.Cryptography.SHA256 h = System.Security.Cryptography.SHA256.Create())
            {
                byte[] d = h.ComputeHash(f);
                StringBuilder sb = new StringBuilder();
                foreach (byte b in d) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
        catch { return ""; }
    }

    static bool Download(string file, string tmp)
    {
        try
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Luxe.Host() + "/mods/" + file);
            req.UserAgent = "LuxeLauncher/" + Cfg.AppVersion;
            // ждать сервис долго нельзя: игра должна запуститься и без мода
            req.Timeout = 8000;
            req.ReadWriteTimeout = 8000;
            using (HttpWebResponse r = (HttpWebResponse)req.GetResponse())
            using (Stream s = r.GetResponseStream())
            using (FileStream f = File.Create(tmp))
            {
                long total = r.ContentLength;
                long got = 0;
                int last = -1;
                byte[] buf = new byte[8192];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    f.Write(buf, 0, n);
                    got += n;
                    // джарник небольшой, но в мобильной сети и он не мгновенный
                    if (Mc.Progress != null)
                    {
                        int pct = total > 0 ? (int)(got * 100 / total) : -1;
                        if (pct != last) { last = pct; Mc.Progress(got, total, "Мод скинов и плащей"); }
                    }
                }
            }
            return Good(tmp);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            return false;
        }
    }

    /// <summary>Короткий GET: на запуске игры долго ждать сервис нельзя.</summary>
    static string Get(string path, int timeoutMs)
    {
        try
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Luxe.Host() + path);
            req.UserAgent = "LuxeLauncher/" + Cfg.AppVersion;
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            using (HttpWebResponse r = (HttpWebResponse)req.GetResponse())
            using (StreamReader sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }
        catch { return null; }
    }
}
