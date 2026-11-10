// ============================================================
//  ПОИСК В ИНТЕРНЕТЕ ДЛЯ ПОМОЩНИКА
//
//  Скины и сборки берём не из локального каталога, а из сети:
//  скин по нику (mc-heads / minotar), сборки — Modrinth API.
//  Плащи живут на сервисе Luxe (Social.cs), здесь только
//  текстовый подбор по названию.
//
//  Все методы сетевые: вызывать из фонового потока.
// ============================================================
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

static class WebSkins
{
    // Известные ники: их скины узнают все — на них и опирается
    // «найди популярный скин», когда игрок не назвал конкретный.
    public static readonly string[] Popular = new string[] {
        "Dream", "Technoblade", "Notch", "Herobrine", "jeb_", "Dinnerbone",
        "CaptainSparklez", "DanTDM", "Skeppy", "BadBoyHalo",
        "GeorgeNotFound", "Sapnap", "TommyInnit", "WilburSoot"
    };

    public static string LastError = "";

    // Скин любого игрока по нику. Возвращает 64x64 текстуру или null.
    public static Bitmap Fetch(string nick)
    {
        LastError = "";
        if (string.IsNullOrEmpty(nick) || nick.Trim().Length < 2)
        {
            LastError = "нужен ник игрока";
            return null;
        }
        string n = nick.Trim().TrimStart('@');
        Bitmap raw = Download("https://mc-heads.net/skin/" + Uri.EscapeDataString(n));
        if (raw == null) raw = Download("https://minotar.net/skin/" + Uri.EscapeDataString(n));
        if (raw == null)
        {
            if (LastError == "") LastError = "не нашёл скин «" + n + "»";
            return null;
        }
        try
        {
            string err;
            Bitmap tex = SkinCanvas.FromImage(raw, out err);
            raw.Dispose();
            if (tex == null) LastError = err ?? "файл не похож на скин";
            return tex;
        }
        catch (Exception ex)
        {
            try { raw.Dispose(); } catch { }
            LastError = ex.Message;
            return null;
        }
    }

    static Bitmap Download(string url)
    {
        try
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 12000;
            req.ReadWriteTimeout = 12000;
            req.UserAgent = "LuxeLauncher/" + Cfg.AppVersion;
            req.Accept = "image/png";
            using (WebResponse resp = req.GetResponse())
            using (Stream s = resp.GetResponseStream())
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] buf = new byte[16384];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                if (ms.Length < 100) return null;
                byte[] data = ms.ToArray();
                using (MemoryStream copy = new MemoryStream(data))
                using (Image img = Image.FromStream(copy))
                    return new Bitmap(img);
            }
        }
        catch (WebException we)
        {
            LastError = we.Response is HttpWebResponse
                ? "ник не найден (" + (int)((HttpWebResponse)we.Response).StatusCode + ")"
                : "нет связи с сервисом скинов";
            return null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
    }
}

// ============================================================
//  СБОРКИ MODRINTH
// ============================================================
static class WebPacks
{
    const string Api = "https://api.modrinth.com/v2/";
    static readonly JavaScriptSerializer ser = new JavaScriptSerializer();
    public static string LastError = "";

    // Поиск сборок по названию. Сначала под текущую версию игры,
    // если там пусто — по всем версиям (сборка старая, но годная).
    public static List<ModInfo> Search(string query, string mcVer, int limit)
    {
        LastError = "";
        List<ModInfo> res = SearchRaw(query, mcVer, "fabric", limit);
        if (res.Count == 0) res = SearchRaw(query, mcVer, "", limit);
        if (res.Count == 0) res = SearchRaw(query, "", "", limit);
        return res;
    }

    static List<ModInfo> SearchRaw(string query, string mcVer, string loader, int limit)
    {
        List<ModInfo> res = new List<ModInfo>();
        try
        {
            StringBuilder fb = new StringBuilder("[[\"project_type:modpack\"]");
            if (!string.IsNullOrEmpty(mcVer)) fb.Append(",[\"versions:" + mcVer + "\"]");
            if (!string.IsNullOrEmpty(loader)) fb.Append(",[\"categories:" + loader + "\"]");
            fb.Append("]");
            string url = Api + "search?limit=" + limit + "&index=relevance";
            if (!string.IsNullOrEmpty(query)) url += "&query=" + Uri.EscapeDataString(query);
            url += "&facets=" + Uri.EscapeDataString(fb.ToString());

            string txt = Modrinth.Raw(url);
            Dictionary<string, object> root = ser.DeserializeObject(txt) as Dictionary<string, object>;
            if (root == null) return res;
            object ho;
            ArrayList hits = root.TryGetValue("hits", out ho) ? Modrinth.AsList(ho) : null;
            if (hits == null) return res;
            foreach (object it in hits)
            {
                Dictionary<string, object> h = it as Dictionary<string, object>;
                if (h == null) continue;
                ModInfo m = new ModInfo();
                m.id = Modrinth.Str(h, "project_id", "");
                if (m.id.Length == 0) continue;
                m.slug = Modrinth.Str(h, "slug", m.id);
                m.title = Modrinth.Str(h, "title", m.slug);
                m.desc = Modrinth.Str(h, "description", "");
                m.author = Modrinth.Str(h, "author", "");
                m.iconUrl = Modrinth.Str(h, "icon_url", null);
                try { m.downloads = Convert.ToInt64(Modrinth.Str(h, "downloads", "0")); } catch { }
                res.Add(m);
            }
        }
        catch (Exception ex) { LastError = ex.Message; }
        return res;
    }

    // Версия сборки под нашу игру (fabric/quilt). null — не нашлось.
    public static Dictionary<string, object> BestVersion(string projectId, string mcVer, out string kind)
    {
        kind = "";
        string[] loaders = new string[] { "fabric", "quilt" };
        foreach (string ld in loaders)
        {
            try
            {
                List<Dictionary<string, object>> vs = Modrinth.Versions(projectId, mcVer, ld);
                if (vs.Count == 0) continue;
                Dictionary<string, object> best = null;
                long bestDl = -1;
                foreach (Dictionary<string, object> v in vs)
                {
                    string vt = Modrinth.Str(v, "version_type", "release");
                    if (vt == "alpha") continue;
                    object dl;
                    long n = 0;
                    if (v.TryGetValue("downloads", out dl) && dl != null) { try { n = Convert.ToInt64(dl); } catch { } }
                    if (Modrinth.PrimaryFile(v) == null) continue;
                    if (n > bestDl) { bestDl = n; best = v; }
                }
                if (best != null) { kind = ld; return best; }
            }
            catch { }
        }
        return null;
    }

    // Уже стоит такая сборка? (id = slug + загрузчик)
    public static string InstalledId(string slug)
    {
        foreach (string k in new string[] { "fabric", "quilt" })
        {
            string id = SafeId(slug) + "-" + k;
            if (IsInstalled(id)) return id;
        }
        return null;
    }

    public static bool IsInstalled(string id)
    {
        try
        {
            string p = Path.Combine(Path.Combine(Path.Combine(Cfg.McDir, "versions"), id), id + ".json");
            return File.Exists(p);
        }
        catch { return false; }
    }

    // Установка сборки из .mrpack: профиль загрузчика, файлы модов
    // по списку Modrinth, затем overrides (конфиги, ресурспаки).
    public static bool Install(string slug, string title, Dictionary<string, object> ver, string kind,
        out string installedId, Action<string> status, Action<int, int> prog)
    {
        LastError = "";
        installedId = "";
        string tmp = Path.Combine(Path.GetTempPath(), "luxe_pack_" + Guid.NewGuid().ToString("N") + ".mrpack");
        try
        {
            Dictionary<string, object> file = Modrinth.PrimaryFile(ver);
            if (file == null) { LastError = "у сборки нет файла"; return false; }
            string url = Modrinth.Str(file, "url", "");
            if (url.Length == 0) { LastError = "нет ссылки на файл"; return false; }

            status("Скачиваю сборку «" + title + "»...");
            Modrinth.Download(url, tmp, "");

            Dictionary<string, object> index = null;
            List<string[]> jobs = new List<string[]>();
            List<string[]> overrides = new List<string[]>();
            int done = 0;
            using (ZipArchive zip = ZipFile.OpenRead(tmp))
            {
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    string name = e.FullName.Replace('\\', '/');
                    if (name.Equals("modrinth.index.json", StringComparison.OrdinalIgnoreCase))
                    {
                        using (StreamReader sr = new StreamReader(e.Open(), Encoding.UTF8))
                            index = ser.DeserializeObject(sr.ReadToEnd()) as Dictionary<string, object>;
                        continue;
                    }
                    // конфиги и ресурсы сборки кладём в папку игры
                    if (name.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase))
                        overrides.Add(new string[] { name.Substring(10), e.FullName });
                    else if (name.StartsWith("client-overrides/", StringComparison.OrdinalIgnoreCase))
                        overrides.Add(new string[] { name.Substring(17), e.FullName });
                }
                if (index == null) { LastError = "в файле нет modrinth.index.json"; return false; }

                string mc = Dep(index, "minecraft");
                if (mc.Length == 0) { LastError = "в сборке не указана версия игры"; return false; }
                string loaderVer = kind == "quilt" ? Dep(index, "quilt-loader") : Dep(index, "fabric-loader");

                // id версии обязан содержать загрузчик: по нему лаунчер
                // понимает, как собирать classpath и чем запускать игру
                string id = SafeId(slug) + "-" + kind;
                installedId = id;

                status("Ставлю " + kind + " для " + mc + "...");
                Dictionary<string, object> profile = LoadProfile(kind, mc, loaderVer);
                if (profile == null) { LastError = "загрузчик " + kind + " не ответил"; return false; }

                string vdir = Path.Combine(Path.Combine(Cfg.McDir, "versions"), id);
                Directory.CreateDirectory(vdir);
                profile["id"] = id;
                File.WriteAllText(Path.Combine(vdir, id + ".json"), ser.Serialize(profile), Encoding.UTF8);
                File.WriteAllText(Path.Combine(vdir, "base.txt"), mc, Encoding.UTF8);
                File.WriteAllText(Path.Combine(vdir, "name.txt"), title, Encoding.UTF8);
                File.WriteAllText(Path.Combine(vdir, "type.txt"), "visual", Encoding.UTF8);
                File.WriteAllText(Path.Combine(vdir, "gamedir.txt"), ".minecraft-" + id, Encoding.UTF8);

                // Базовая игра на этапе установки не нужна: PrepareLaunch сам
                // докачает её при первом запуске. Здесь важно только разложить
                // файлы сборки — иначе установка ждёт сотни мегабайт ассетов.
                string gameDir = Mc.GameDirFor(id);

                // файлы модов
                object fo;
                ArrayList files = index.TryGetValue("files", out fo) ? Modrinth.AsList(fo) : null;
                int total = files == null ? 0 : files.Count;
                int i = 0;
                if (files != null)
                {
                    foreach (object o in files)
                    {
                        i++;
                        Dictionary<string, object> f = o as Dictionary<string, object>;
                        if (f == null) continue;
                        Dictionary<string, object> env = null;
                        object eo;
                        if (f.TryGetValue("env", out eo)) env = eo as Dictionary<string, object>;
                        // мод только для сервера — клиенту он не нужен
                        if (env != null &&
                            Modrinth.Str(env, "client", "").Equals("unsupported", StringComparison.OrdinalIgnoreCase))
                            continue;
                        string path = Modrinth.Str(f, "path", "");
                        if (path.Length == 0) continue;
                        string link = "";
                        object dlo;
                        if (f.TryGetValue("downloads", out dlo))
                        {
                            ArrayList dl = Modrinth.AsList(dlo);
                            if (dl != null && dl.Count > 0) link = dl[0].ToString();
                        }
                        if (link.Length == 0) continue;
                        string sha1 = "";
                        object ho;
                        if (f.TryGetValue("hashes", out ho))
                        {
                            Dictionary<string, object> hh = ho as Dictionary<string, object>;
                            if (hh != null) sha1 = Modrinth.Str(hh, "sha1", "");
                        }
                        string dest = Path.Combine(gameDir, path.Replace('/', '\\'));
                        try
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(dest));
                            if (!File.Exists(dest)) { Modrinth.Download(link, dest, sha1); done++; }
                        }
                        catch { }
                        if (i % 5 == 0 || i == total)
                            status("Моды: " + i + " / " + total);
                    }
                }

                // конфиги поверх
                foreach (string[] ov in overrides)
                {
                    try
                    {
                        string dest = Path.Combine(gameDir, ov[0].Replace('/', '\\'));
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        zip.GetEntry(ov[1]).ExtractToFile(dest, true);
                    }
                    catch { }
                }

                Dictionary<string, object> pj = Mc.LoadVerJson(id);
                if (pj == null) { LastError = "не записался профиль версии"; return false; }
            }
            status("Готово: " + title + " (" + done + " файлов)");
            return true;
        }
        catch (Exception ex)
        {
            LastError = Short(ex.Message);
            return false;
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    // Профиль загрузчика: по возможности именно той версии, что в сборке.
    static Dictionary<string, object> LoadProfile(string kind, string mc, string loaderVer)
    {
        try
        {
            if (string.IsNullOrEmpty(loaderVer))
                loaderVer = Mc.ResolveLoader(kind, mc, delegate(string s) { });
            string url = kind == "quilt"
                ? "https://meta.quiltmc.org/v3/versions/loader/" + mc + "/" + loaderVer + "/profile/json"
                : "https://meta.fabricmc.net/v2/versions/loader/" + mc + "/" + loaderVer + "/profile/json";
            return Mc.GetJson(url);
        }
        catch { return null; }
    }

    static string Dep(Dictionary<string, object> index, string key)
    {
        try
        {
            object o;
            if (!index.TryGetValue("dependencies", out o)) return "";
            Dictionary<string, object> d = o as Dictionary<string, object>;
            if (d == null) return "";
            if (d.TryGetValue(key, out o) && o != null) return o.ToString();
        }
        catch { }
        return "";
    }

    static string Short(string s)
    {
        if (string.IsNullOrEmpty(s)) return "не получилось";
        return s.Length > 110 ? s.Substring(0, 110) + "..." : s;
    }


    // Slug Modrinth в имя папки: только буквы, цифры и дефис
    static string SafeId(string s)
    {
        StringBuilder sb = new StringBuilder();
        foreach (char c in (s ?? "").ToLower())
        {
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_') sb.Append(c);
            else if (c == ' ' || c == '.') sb.Append('-');
        }
        string res = sb.ToString().Trim('-');
        return res.Length == 0 ? "pack" : res;
    }
}