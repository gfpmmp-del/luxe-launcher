using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

// ================================================================
//  ВХОД В MICROSOFT — лицензия, скины, плащи
//
//  Лаунчер всегда запускал игру офлайн: токен "0" и офлайн-UUID.
//  Здесь настоящий вход, как в официальном лаунчере:
//
//      код с сайта Microsoft -> Xbox Live -> XSTS -> Minecraft
//
//  Пароль до лаунчера НЕ доходит: он вводится на сайте Microsoft,
//  лаунчер только показывает код и ждёт, пока вход подтвердят.
//
//  Идентификатор приложения (client id) встроен — официальный id лаунчера
//  Minecraft: свои приложения Mojang пускает только после одобрения, а этот
//  уже пущен (так входят Modrinth, MCAuthLib, MiniLauncher и другие).
//  Свой id можно положить в clientid.txt рядом с лаунчером — он перекроет
//  встроенный и пойдёт через Azure (login.microsoftonline.com).
//
//  Токены лежат в папке данных: Luxe Visuals\account.json.
// ================================================================

public class SkinEntry
{
    public string Id = "";
    public string State = "";
    public string Url = "";
    public string Variant = "";
    public string Alias = "";
}

public class CapeEntry
{
    public string Id = "";
    public string State = "";
    public string Url = "";
    public string Alias = "";
}

static class Auth
{
    public static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

    // ---- состояние входа ----
    public static string ClientId = "";
    public static string AccessToken = "";
    public static string RefreshToken = "";
    public static long ExpiresAt = 0;      // unix-время, когда протухает токен Minecraft
    public static string Name = "";
    public static string Uuid = "";
    public static string Xuid = "";
    public static string LastSkinVariant = "CLASSIC";

    public static List<SkinEntry> Skins = new List<SkinEntry>();
    public static List<CapeEntry> Capes = new List<CapeEntry>();

    // Официальный client id лаунчера Minecraft. Свои приложения Mojang пускает
    // только после одобрения, а этот уже пущен — так входят Modrinth, MCAuthLib,
    // MiniLauncher и другие открытые лаунчеры. Свой id (когда одобрят) кладётся
    // в clientid.txt рядом с лаунчером и перекрывает встроенный.
    public const string DefaultClientId = "00000000402b5328";

    // v1 (встроенный id, live.com): токен — готовый RPS-тикет, в Xbox Live
    //   идёт БЕЗ «d=», иначе XBL отвечает 401.
    // v2 (свой id из clientid.txt): токен Azure-AD, в Xbox Live идёт С «d=».
    const string ScopeV1 = "service::user.auth.xboxlive.com::MBI_SSL";
    const string ScopeV2 = "XboxLive.signin offline_access";
    const string DeviceUrlV1 = "https://login.live.com/oauth20_connect.srf";
    const string TokenUrlV1 = "https://login.live.com/oauth20_token.srf";
    const string DeviceUrlV2 = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
    const string TokenUrlV2 = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
    const string RedirectV1 = "https://login.live.com/oauth20_desktop.srf";

    static bool V1 { get { return ClientId == DefaultClientId; } }
    static string DeviceUrl { get { return V1 ? DeviceUrlV1 : DeviceUrlV2; } }
    static string TokenUrl { get { return V1 ? TokenUrlV1 : TokenUrlV2; } }
    static string Scope { get { return V1 ? ScopeV1 : ScopeV2; } }

    public static bool LoggedIn
    {
        get { return AccessToken.Length > 0 && Uuid.Length > 0; }
    }

    public static string ClientIdPath
    {
        get { return Path.Combine(Cfg.BaseDir, "clientid.txt"); }
    }

    static string StorePath
    {
        get
        {
            // токены храним в папке данных (как и всё остальное), а не рядом с exe
            string root = string.IsNullOrEmpty(Cfg.DataDir) ? Cfg.BaseDir : Cfg.DataDir;
            return Path.Combine(root, "account.json");
        }
    }

    // старое место account.json — рядом с exe; читаем, только если нового ещё нет
    static string LegacyStorePath
    {
        get { return Path.Combine(Cfg.BaseDir, "account.json"); }
    }

    // ============================================================
    //  ХРАНЕНИЕ
    // ============================================================

    public static void Init()
    {
        // по умолчанию — встроенный официальный id: он уже пущен Mojang'ом
        ClientId = DefaultClientId;
        // clientid.txt: первая непустая строка, без пробелов (перекрывает встроенный)
        try
        {
            if (File.Exists(ClientIdPath))
            {
                foreach (string l in File.ReadAllLines(ClientIdPath, Encoding.UTF8))
                {
                    string s = l.Trim();
                    if (s.Length == 0 || s.StartsWith("#")) continue;
                    ClientId = s;
                    break;
                }
            }
        }
        catch { }
        try
        {
            string path = StorePath;
            if (!File.Exists(path) && File.Exists(LegacyStorePath)) path = LegacyStorePath;
            if (!File.Exists(path)) return;
            Dictionary<string, object> d = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
            if (d == null) return;
            RefreshToken = Str(d, "refresh_token");
            AccessToken = Str(d, "access_token");
            ExpiresAt = Long(d, "expires_at");
            Name = Str(d, "name");
            Uuid = Str(d, "uuid");
            Xuid = Str(d, "xuid");
        }
        catch { }
    }

    public static void Save()
    {
        try
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["refresh_token"] = RefreshToken;
            d["access_token"] = AccessToken;
            d["expires_at"] = ExpiresAt;
            d["name"] = Name;
            d["uuid"] = Uuid;
            d["xuid"] = Xuid;
            File.WriteAllText(StorePath, Json.Serialize(d), Encoding.UTF8);
        }
        catch { }
    }

    public static void Logout()
    {
        AccessToken = ""; RefreshToken = ""; ExpiresAt = 0;
        Name = ""; Uuid = ""; Xuid = "";
        Skins.Clear(); Capes.Clear();
        try { if (File.Exists(StorePath)) File.Delete(StorePath); } catch { }
    }

    // ============================================================
    //  ВХОД
    // ============================================================

    // status  - строка прогресса для экрана
    // onCode  - (код, ссылка) чтобы показать крупно
    // done    - (успех, сообщение)
    public static void LoginAsync(Action<string> status, Action<string, string> onCode, Action<bool, string> done)
    {
        Thread t = new Thread((ThreadStart)delegate
        {
            try
            {
                if (ClientId.Length == 0)
                {
                    done(false, "Нет clientid.txt рядом с лаунчером — вход некуда адресовать.");
                    return;
                }
                status("Запрашиваю код у Microsoft...");
                int st;
                string body = Send("POST", DeviceUrl, "application/x-www-form-urlencoded",
                    "client_id=" + Uri.EscapeDataString(ClientId) + "&scope=" + Uri.EscapeDataString(Scope) +
                    (V1 ? "&response_type=device_code" : ""),
                    null, out st);
                Dictionary<string, object> d = Dict(body);
                if (st != 200 || Str(d, "device_code").Length == 0)
                {
                    done(false, "Microsoft не выдал код: " + ErrText(d, body));
                    return;
                }

                string dev = Str(d, "device_code");
                string user = Str(d, "user_code");
                string uri = Str(d, "verification_uri");
                if (uri.Length == 0) uri = "https://microsoft.com/link";
                int interval = 5;
                int expires = 900;
                try { interval = Convert.ToInt32(d["interval"]); } catch { }
                try { expires = Convert.ToInt32(d["expires_in"]); } catch { }
                if (interval < 2) interval = 2;

                if (onCode != null) onCode(user, uri);
                status("Открой " + uri + " и введи код " + user);

                DateTime until = DateTime.UtcNow.AddSeconds(expires);
                string msToken = "";
                while (DateTime.UtcNow < until)
                {
                    Thread.Sleep(interval * 1000);
                    string pb = Send("POST", TokenUrl, "application/x-www-form-urlencoded",
                        "grant_type=urn:ietf:params:oauth:grant-type:device_code" +
                        "&client_id=" + Uri.EscapeDataString(ClientId) +
                        "&device_code=" + Uri.EscapeDataString(dev),
                        null, out st);
                    Dictionary<string, object> pd = Dict(pb);
                    string err = Str(pd, "error");
                    if (err == "authorization_pending") { status("Жду подтверждения на сайте Microsoft..."); continue; }
                    if (err == "slow_down") { interval += 5; continue; }
                    if (err == "expired_token") { done(false, "Код устарел — начни вход заново."); return; }
                    if (err == "authorization_declined") { done(false, "Вход отменили на сайте Microsoft."); return; }
                    if (err.Length > 0) { done(false, "Ошибка входа: " + err + " — " + Str(pd, "error_description")); return; }
                    msToken = Str(pd, "access_token");
                    RefreshToken = Str(pd, "refresh_token");
                    break;
                }
                if (msToken.Length == 0) { done(false, "Время ожидания истекло — начни вход заново."); return; }

                status("Вход подтверждён, иду в Xbox Live...");
                Chain(msToken, status);
                Save();
                done(true, "Вход выполнен: " + Name);
            }
            catch (Exception ex)
            {
                done(false, ex.Message);
            }
        });
        t.IsBackground = true;
        t.Start();
    }

    // Обновление по сохранённому refresh-токену. Возвращает false, если
    // refresh протух (Microsoft даёт 90 дней) — тогда нужен вход заново.
    public static bool Refresh()
    {
        if (RefreshToken.Length == 0) return false;
        int st;
        string body = Send("POST", TokenUrl, "application/x-www-form-urlencoded",
            "grant_type=refresh_token&client_id=" + Uri.EscapeDataString(ClientId) +
            "&refresh_token=" + Uri.EscapeDataString(RefreshToken) +
            "&scope=" + Uri.EscapeDataString(Scope) +
            (V1 ? "&redirect_uri=" + Uri.EscapeDataString(RedirectV1) : ""),
            null, out st);
        Dictionary<string, object> d = Dict(body);
        string tok = Str(d, "access_token");
        if (tok.Length == 0)
        {
            // invalid_grant — refresh больше не годится, чистим вход
            if (Str(d, "error") == "invalid_grant") Logout();
            return false;
        }
        string nr = Str(d, "refresh_token");
        if (nr.Length > 0) RefreshToken = nr;
        Chain(tok, null);
        Save();
        return true;
    }

    // Токен Minecraft, годный прямо сейчас (обновляем заранее, не в притык).
    public static string EnsureToken()
    {
        if (!LoggedIn) return "";
        if (Now() >= ExpiresAt - 300)
        {
            if (!Refresh()) return "";
        }
        return AccessToken;
    }

    // ============================================================
    //  ЦЕПОЧКА MICROSOFT -> XBOX -> MINECRAFT
    // ============================================================

    static void Chain(string msToken, Action<string> status)
    {
        int st;

        // 1. Xbox Live
        // «d=» нужен только токену Azure-AD (свой id); live.com-тикет идёт как есть
        string rps = V1 ? Esc(msToken) : ("d=" + Esc(msToken));
        string xbl = "{\"Properties\":{\"AuthMethod\":\"RPS\",\"SiteName\":\"user.auth.xboxlive.com\"," +
                     "\"RpsTicket\":\"" + rps + "\"}," +
                     "\"RelyingParty\":\"http://auth.xboxlive.com\",\"TokenType\":\"JWT\"}";
        string b1 = Send("POST", "https://user.auth.xboxlive.com/user/authenticate", "application/json", xbl, null, out st);
        Dictionary<string, object> d1 = Dict(b1);
        string xblToken = Str(d1, "Token");
        string uhs = Xui(d1, "uhs");
        string xid = Xui(d1, "xid");
        if (xblToken.Length == 0) throw new Exception("Xbox Live отклонил вход: " + ErrText(d1, b1));

        // 2. XSTS
        string xsts = "{\"Properties\":{\"SandboxId\":\"RETAIL\",\"UserTokens\":[\"" + Esc(xblToken) + "\"]}," +
                      "\"RelyingParty\":\"rp://api.minecraftservices.com/\",\"TokenType\":\"JWT\"}";
        string b2 = Send("POST", "https://xsts.auth.xboxlive.com/xsts/authorize", "application/json", xsts, null, out st);
        Dictionary<string, object> d2 = Dict(b2);
        string xstsToken = Str(d2, "Token");
        string uhs2 = Xui(d2, "uhs");
        if (uhs2.Length > 0) uhs = uhs2;
        if (xstsToken.Length == 0) throw new Exception(XstsError(d2, b2));

        // 3. Minecraft
        if (status != null) status("Вход в Minecraft...");
        string mc = "{\"identityToken\":\"XBL3.0 x=" + Esc(uhs) + ";" + Esc(xstsToken) + "\"}";
        string b3 = Send("POST", "https://api.minecraftservices.com/authentication/login_with_xbox",
            "application/json", mc, null, out st);
        Dictionary<string, object> d3 = Dict(b3);
        string tok = Str(d3, "access_token");
        if (tok.Length == 0) throw new Exception("Minecraft отклонил вход: " + ErrText(d3, b3));

        AccessToken = tok;
        Xuid = xid;
        long exp = 86400;
        try { exp = Convert.ToInt64(d3["expires_in"]); } catch { }
        ExpiresAt = Now() + exp - 120;

        // 4. Профиль — заодно проверяем лицензию
        if (status != null) status("Читаю профиль...");
        Profile();
    }

    // Профиль: имя, uuid, скины, плащи. Бросает исключение, если нет лицензии.
    static void Profile()
    {
        int st;
        string b = Send("GET", "https://api.minecraftservices.com/minecraft/profile", null, null, AccessToken, out st);
        Dictionary<string, object> d = Dict(b);

        if (st == 401)
        {
            AccessToken = ""; ExpiresAt = 0;
            throw new Exception("Токен Minecraft не принят (401) — войди заново.");
        }
        if (st == 403)
            throw new Exception("Mojang не пускает это приложение к Minecraft API (403). " +
                                "Приложению нужно разрешение api.minecraftservices.com — заявка на aka.ms/mce-reviewappid.");
        if (st != 200) throw new Exception("Профиль не получен: " + ErrText(d, b));

        string id = Str(d, "id");
        if (id.Length == 0)
            throw new Exception("У этого аккаунта нет лицензии Minecraft: " + ErrText(d, b));

        Uuid = Hyphen(id);
        Name = Str(d, "name");
        Skins.Clear();
        Capes.Clear();
        ReadSkins(d);
        ReadCapes(d);
        if (Skins.Count > 0)
        {
            LastSkinVariant = Skins[0].Variant;
            if (LastSkinVariant.Length == 0) LastSkinVariant = "CLASSIC";
        }
    }

    static void ReadSkins(Dictionary<string, object> d)
    {
        object o;
        if (!d.TryGetValue("skins", out o)) return;
        object[] arr = o as object[];
        if (arr == null) return;
        foreach (object it in arr)
        {
            Dictionary<string, object> s = it as Dictionary<string, object>;
            if (s == null) continue;
            SkinEntry e = new SkinEntry();
            e.Id = Str(s, "id");
            e.State = Str(s, "state");
            e.Url = Str(s, "url");
            e.Variant = Str(s, "variant");
            e.Alias = Str(s, "alias");
            Skins.Add(e);
        }
    }

    static void ReadCapes(Dictionary<string, object> d)
    {
        object o;
        if (!d.TryGetValue("capes", out o)) return;
        object[] arr = o as object[];
        if (arr == null) return;
        foreach (object it in arr)
        {
            Dictionary<string, object> s = it as Dictionary<string, object>;
            if (s == null) continue;
            CapeEntry e = new CapeEntry();
            e.Id = Str(s, "id");
            e.State = Str(s, "state");
            e.Url = Str(s, "url");
            e.Alias = Str(s, "alias");
            Capes.Add(e);
        }
    }

    // Обновить профиль асинхронно (для экрана аккаунта)
    public static void RefreshProfileAsync(Action<bool, string> done)
    {
        Thread t = new Thread((ThreadStart)delegate
        {
            try
            {
                string tok = EnsureToken();
                if (tok.Length == 0) { done(false, "Нужен вход в Microsoft."); return; }
                Profile();
                Save();
                done(true, "Скинов: " + Skins.Count + ", плащей: " + Capes.Count);
            }
            catch (Exception ex) { done(false, ex.Message); }
        });
        t.IsBackground = true;
        t.Start();
    }

    // ============================================================
    //  СКИНЫ
    // ============================================================

    // Отправка скина на аккаунт. png - кусок PNG-файла.
    public static bool UploadSkin(byte[] png, string variant, out string err)
    {
        err = "";
        try
        {
            string tok = EnsureToken();
            if (tok.Length == 0) { err = "Нужен вход в Microsoft."; return false; }

            Dictionary<string, object> d = new Dictionary<string, object>();
            d["variant"] = (variant == "SLIM") ? "slim" : "classic";
            d["file"] = Convert.ToBase64String(png);

            int st;
            string b = Send("POST", "https://api.minecraftservices.com/minecraft/profile/skins",
                "application/json", Json.Serialize(d), tok, out st);
            Dictionary<string, object> r = Dict(b);
            if (st != 200 && st != 204)
            {
                err = SkinError(st, r, b);
                return false;
            }
            Profile();
            Save();
            return true;
        }
        catch (Exception ex) { err = ex.Message; return false; }
    }

    // Смена модели (широкий/тонкий) без загрузки картинки
    public static bool SetVariant(string variant, out string err)
    {
        err = "";
        try
        {
            string tok = EnsureToken();
            if (tok.Length == 0) { err = "Нужен вход в Microsoft."; return false; }
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["variant"] = (variant == "SLIM") ? "slim" : "classic";
            int st;
            string b = Send("PUT", "https://api.minecraftservices.com/minecraft/profile/skins",
                "application/json", Json.Serialize(d), tok, out st);
            Dictionary<string, object> r = Dict(b);
            if (st != 200 && st != 204) { err = SkinError(st, r, b); return false; }
            Profile();
            Save();
            return true;
        }
        catch (Exception ex) { err = ex.Message; return false; }
    }

    // Вернуть стандартный скин
    public static bool ResetSkin(out string err)
    {
        err = "";
        try
        {
            string tok = EnsureToken();
            if (tok.Length == 0) { err = "Нужен вход в Microsoft."; return false; }
            int st;
            string b = Send("DELETE", "https://api.minecraftservices.com/minecraft/profile/skins/active",
                null, null, tok, out st);
            if (st != 200 && st != 204)
            {
                err = SkinError(st, Dict(b), b);
                return false;
            }
            Profile();
            Save();
            return true;
        }
        catch (Exception ex) { err = ex.Message; return false; }
    }

    // Ссылка на активный скин из уже загруженного профиля
    public static string CurrentSkinUrl()
    {
        try
        {
            for (int i = 0; i < Skins.Count; i++)
                if (Skins[i].State == "ACTIVE" && Skins[i].Url.Length > 0) return Skins[i].Url;
            if (Skins.Count > 0) return Skins[0].Url;
        }
        catch { }
        return "";
    }

    // Скачать картинку скина в память (PNG — не текст, поэтому не Send)
    public static byte[] DownloadSkin(string url, out string err)
    {
        err = "";
        try
        {
            Tls();
            HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
            r.Method = "GET";
            r.Timeout = 30000;
            r.ReadWriteTimeout = 30000;
            r.UserAgent = "LuxeLauncher/" + Cfg.AppVersion;
            using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse())
            using (Stream s = resp.GetResponseStream())
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] buf = new byte[8192];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                return ms.ToArray();
            }
        }
        catch (Exception ex) { err = "Скин не скачался: " + ex.Message; return null; }
    }

    static string SkinError(int st, Dictionary<string, object> d, string body)
    {
        if (st == 401) { AccessToken = ""; ExpiresAt = 0; return "Токен не принят — войди заново."; }
        if (st == 403) return "Mojang не пускает приложение к скинам (403) — нет разрешения api.minecraftservices.com.";
        if (st == 400) return "Скин не принят: " + ErrText(d, body) + " (нужен PNG 64x64 или 64x32)";
        if (st == 429) return "Слишком часто меняешь скин — подожди пару минут.";
        return "Ошибка " + st + ": " + ErrText(d, body);
    }

    // ============================================================
    //  СЕТЬ
    // ============================================================

    static void Tls()
    {
        // .NET Framework по умолчанию может стучаться по старому TLS,
        // Microsoft такое соединение рвёт. 3072 = TLS 1.2
        try { ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | (SecurityProtocolType)3072; }
        catch { }
    }

    // Всегда возвращает тело ответа: и при успехе, и при ошибке.
    // Код ответа отдаём через status, чтобы вызывающий сам решал.
    static string Send(string method, string url, string contentType, string payload, string bearer, out int status)
    {
        Tls();
        HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
        r.Method = method;
        r.Timeout = 30000;
        r.ReadWriteTimeout = 30000;
        r.UserAgent = "LuxeLauncher/" + Cfg.AppVersion;
        r.Accept = "application/json";
        if (contentType != null) r.ContentType = contentType;
        if (!string.IsNullOrEmpty(bearer)) r.Headers["Authorization"] = "Bearer " + bearer;
        if (payload != null)
        {
            byte[] data = Encoding.UTF8.GetBytes(payload);
            r.ContentLength = data.Length;
            using (Stream s = r.GetRequestStream()) s.Write(data, 0, data.Length);
        }
        try
        {
            using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse())
            {
                status = (int)resp.StatusCode;
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
        }
        catch (WebException ex)
        {
            HttpWebResponse resp = ex.Response as HttpWebResponse;
            if (resp == null) throw new Exception("Сеть недоступна: " + ex.Message);
            status = (int)resp.StatusCode;
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }
    }

    // ============================================================
    //  МЕЛОЧИ
    // ============================================================

    static Dictionary<string, object> Dict(string json)
    {
        try { return Json.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>(); }
        catch { return new Dictionary<string, object>(); }
    }

    static string Str(Dictionary<string, object> d, string key)
    {
        object v;
        if (d != null && d.TryGetValue(key, out v) && v != null) return v.ToString();
        return "";
    }

    static long Long(Dictionary<string, object> d, string key)
    {
        object v;
        if (d != null && d.TryGetValue(key, out v) && v != null)
        {
            try { return Convert.ToInt64(v); } catch { }
        }
        return 0;
    }

    static long Now()
    {
        return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
    }

    static string Esc(string s)
    {
        if (s == null) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    static string Xui(Dictionary<string, object> d, string key)
    {
        try
        {
            Dictionary<string, object> dc = d["DisplayClaims"] as Dictionary<string, object>;
            object[] xui = dc["xui"] as object[];
            Dictionary<string, object> first = xui[0] as Dictionary<string, object>;
            object v;
            if (first.TryGetValue(key, out v) && v != null) return v.ToString();
        }
        catch { }
        return "";
    }

    // Ответ, где вместо деталей только код — показываем хотя бы его
    static string ErrText(Dictionary<string, object> d, string body)
    {
        string e = Str(d, "error");
        string m = Str(d, "errorMessage");
        if (m.Length == 0) m = Str(d, "error_description");
        string s = e;
        if (m.Length > 0) s += (s.Length > 0 ? " — " : "") + m;
        if (s.Length == 0) s = (body ?? "").Trim();
        if (s.Length > 300) s = s.Substring(0, 300);
        return s;
    }

    // Коды XSTS: без расшифровки они выглядят как «ошибка 2148916238»
    static string XstsError(Dictionary<string, object> d, string body)
    {
        long x = 0;
        try { x = Convert.ToInt64(d["XErr"]); } catch { }
        if (x == 2148916227) return "Этот аккаунт заблокирован в Xbox Live.";
        if (x == 2148916233) return "У аккаунта нет профиля Xbox — зайди один раз на minecraft.net и повтори.";
        if (x == 2148916235) return "В стране аккаунта Xbox Live недоступен.";
        if (x == 2148916236 || x == 2148916237) return "Аккаунту нужна проверка возраста на сайте Xbox.";
        if (x == 2148916238) return "Аккаунт детский: его должен добавить в семейную группу взрослый.";
        return "XSTS не выдал токен: " + ErrText(d, body);
    }

    // Профиль отдаёт uuid без дефисов, а игре нужен с дефисами
    public static string Hyphen(string id)
    {
        if (id == null) return "";
        string s = id.Replace("-", "");
        if (s.Length != 32) return id;
        return s.Substring(0, 8) + "-" + s.Substring(8, 4) + "-" + s.Substring(12, 4) + "-" +
               s.Substring(16, 4) + "-" + s.Substring(20, 12);
    }

    public static void OpenBrowser(string url)
    {
        Os.OpenUrl(url);
    }
}
