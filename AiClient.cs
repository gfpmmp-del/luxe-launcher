// ============================================================
//  Luxe Launcher - клиент настоящей языковой модели
//
//  Ассистент больше не набор правил: запрос уходит в LLM по
//  OpenAI-совместимому протоколу. По умолчанию — бесплатный
//  эндпоинт без ключа, но в настройках можно указать свой
//  (OpenAI, OpenRouter, локальная Ollama и т.п.).
//
//  Сеть никогда не блокирует UI: Ask вызывается из фонового
//  потока, ответ приходит на форму через SocialPost.
// ============================================================
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

static class AiConfig
{
    public const string DefaultEndpoint = "https://text.pollinations.ai/openai";
    public const string DefaultModel = "openai";

    public static string Endpoint = DefaultEndpoint;
    public static string Model = DefaultModel;
    public static string Key = "";

    static bool loaded;
    static JavaScriptSerializer ser = new JavaScriptSerializer();

    static string PathFile
    {
        get { return Path.Combine(Cfg.DataDir, "ai.json"); }
    }

    public static bool IsDefault
    {
        get { return Endpoint == DefaultEndpoint && Key.Length == 0; }
    }

    public static void Ensure()
    {
        if (loaded) return;
        loaded = true;
        Load();
    }

    public static void Load()
    {
        try
        {
            if (!File.Exists(PathFile)) return;
            string t = File.ReadAllText(PathFile, Encoding.UTF8);
            Dictionary<string, object> d = ser.Deserialize<Dictionary<string, object>>(t);
            if (d == null) return;
            Endpoint = Str(d, "endpoint", DefaultEndpoint);
            Model = Str(d, "model", DefaultModel);
            Key = Str(d, "key", "");
            if (Endpoint.Trim().Length == 0) Endpoint = DefaultEndpoint;
            if (Model.Trim().Length == 0) Model = DefaultModel;
        }
        catch { }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Cfg.DataDir);
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["endpoint"] = Endpoint;
            d["model"] = Model;
            d["key"] = Key;
            File.WriteAllText(PathFile, ser.Serialize(d), Encoding.UTF8);
            loaded = true;
        }
        catch { }
    }

    public static void Reset()
    {
        Endpoint = DefaultEndpoint;
        Model = DefaultModel;
        Key = "";
        Save();
    }

    static string Str(Dictionary<string, object> d, string k, string def)
    {
        object o;
        if (d.TryGetValue(k, out o) && o != null) return o.ToString();
        return def;
    }
}

static class AiClient
{
    static readonly JavaScriptSerializer ser = new JavaScriptSerializer();
    public static string LastError = "";

    // История: пары роль/текст. Возвращает ответ модели или null.
    // Вызывать только из фонового потока.
    //
    // Бесплатный шлюз периодически отвечает 503 «на лимите», поэтому
    // одного запроса мало: пробуем POST, короткий повтор и резервный
    // GET-путь того же сервиса (у него отдельный счётчик лимита).
    public static string Ask(List<KeyValuePair<string, string>> messages, string system)
    {
        LastError = "";
        AiConfig.Ensure();

        string r = TryPost(messages, system);
        if (r != null) return r;

        string firstErr = LastError;
        // 503/таймаут обычно отпускает через несколько секунд
        try { Thread.Sleep(2500); } catch { }
        r = TryPost(messages, system);
        if (r != null) return r;

        // Резервный путь есть только у Pollinations: /openai -> корень
        if (IsPollinations() && string.IsNullOrEmpty(AiConfig.Key))
        {
            r = TryGet(messages, system);
            if (r != null) return r;
        }

        // Если резерв тоже не смог — показываем первую (самую понятную) ошибку
        if (string.IsNullOrEmpty(LastError)) LastError = firstErr;
        return null;
    }

    static bool IsPollinations()
    {
        return AiConfig.Endpoint.IndexOf("pollinations.ai", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static string TryPost(List<KeyValuePair<string, string>> messages, string system)
    {
        try
        {
            ArrayList msgs = new ArrayList();
            if (!string.IsNullOrEmpty(system))
                msgs.Add(Msg("system", system));
            if (messages != null)
            {
                int from = Math.Max(0, messages.Count - 20);
                for (int i = from; i < messages.Count; i++)
                    msgs.Add(Msg(messages[i].Key, messages[i].Value));
            }

            Dictionary<string, object> body = new Dictionary<string, object>();
            body["model"] = AiConfig.Model;
            body["messages"] = msgs;
            body["temperature"] = 0.7;
            body["max_tokens"] = 900;
            body["private"] = true;
            body["referrer"] = "luxe-launcher";
            string json = ser.Serialize(body);

            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(AiConfig.Endpoint);
            req.Method = "POST";
            req.ContentType = "application/json; charset=utf-8";
            req.Accept = "application/json";
            req.UserAgent = "LuxeLauncher/" + Cfg.AppVersion;
            req.Timeout = 40000;
            req.ReadWriteTimeout = 40000;
            req.KeepAlive = false;
            if (!string.IsNullOrEmpty(AiConfig.Key))
                req.Headers["Authorization"] = "Bearer " + AiConfig.Key;

            byte[] data = Encoding.UTF8.GetBytes(json);
            req.ContentLength = data.Length;
            using (Stream s = req.GetRequestStream()) s.Write(data, 0, data.Length);

            string txt;
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                txt = sr.ReadToEnd();

            return Parse(txt);
        }
        catch (WebException we)
        {
            LastError = "Сеть: " + Describe(we);
            return null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
    }

    // Резерв Pollinations: текст вопроса прямо в пути, ответ — строка.
    static string TryGet(List<KeyValuePair<string, string>> messages, string system)
    {
        try
        {
            string last = "";
            if (messages != null && messages.Count > 0)
                last = messages[messages.Count - 1].Value;
            if (last.Trim().Length == 0) { LastError = "Пустой запрос"; return null; }

            string root = AiConfig.Endpoint.TrimEnd('/');
            if (root.EndsWith("/openai", StringComparison.OrdinalIgnoreCase))
                root = root.Substring(0, root.Length - 7);
            string url = root + "/" + Uri.EscapeDataString(last) +
                         "?model=" + Uri.EscapeDataString(AiConfig.Model) +
                         "&private=true&referrer=luxe-launcher";
            if (!string.IsNullOrEmpty(system))
                url += "&system=" + Uri.EscapeDataString(system);

            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Accept = "text/plain";
            req.UserAgent = "LuxeLauncher/" + Cfg.AppVersion;
            req.Timeout = 45000;
            req.ReadWriteTimeout = 45000;
            req.KeepAlive = false;

            string txt;
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                txt = sr.ReadToEnd();

            string res = Clean(txt);
            if (res.Length < 2) { LastError = "Пустой ответ"; return null; }
            return res;
        }
        catch (WebException we)
        {
            LastError = "Сеть: " + Describe(we);
            return null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
    }

    static Dictionary<string, object> Msg(string role, string content)
    {
        Dictionary<string, object> m = new Dictionary<string, object>();
        m["role"] = role;
        m["content"] = content;
        return m;
    }

    // Ответ бывает в трёх видах: OpenAI-объект, объект с "text",
    // либо просто строка (некоторые бесплатные шлюзы).
    static string Parse(string txt)
    {
        if (string.IsNullOrEmpty(txt)) { LastError = "Пустой ответ"; return null; }
        string t = txt.Trim();
        if (t.StartsWith("{"))
        {
            try
            {
                Dictionary<string, object> d = ser.Deserialize<Dictionary<string, object>>(t);
                if (d != null)
                {
                    if (d.ContainsKey("error"))
                    {
                        LastError = ErrorText(d["error"]);
                        return null;
                    }
                    object ch;
                    if (d.TryGetValue("choices", out ch) && ch is ArrayList && ((ArrayList)ch).Count > 0)
                    {
                        object first = ((ArrayList)ch)[0];
                        Dictionary<string, object> fd = first as Dictionary<string, object>;
                        if (fd != null)
                        {
                            object mo;
                            if (fd.TryGetValue("message", out mo))
                            {
                                Dictionary<string, object> md = mo as Dictionary<string, object>;
                                if (md != null && md.ContainsKey("content")) return Clean(md["content"].ToString());
                            }
                            object to;
                            if (fd.TryGetValue("text", out to) && to != null) return Clean(to.ToString());
                        }
                    }
                    object tx;
                    if (d.TryGetValue("text", out tx) && tx != null) return Clean(tx.ToString());
                    if (d.TryGetValue("content", out tx) && tx != null) return Clean(tx.ToString());
                    if (d.TryGetValue("response", out tx) && tx != null) return Clean(tx.ToString());
                }
            }
            catch { }
        }
        // не JSON — значит шлюз вернул готовый текст
        if (t.Length > 0 && t.IndexOf("<html", StringComparison.OrdinalIgnoreCase) < 0)
            return Clean(t);
        LastError = "Не удалось разобрать ответ";
        return null;
    }

    static string ErrorText(object o)
    {
        try
        {
            Dictionary<string, object> d = o as Dictionary<string, object>;
            if (d != null)
            {
                object m;
                if (d.TryGetValue("message", out m) && m != null) return m.ToString();
            }
            return o.ToString();
        }
        catch { return "ошибка сервиса"; }
    }

    static string Describe(WebException we)
    {
        try
        {
            if (we.Response != null)
            {
                HttpWebResponse r = we.Response as HttpWebResponse;
                using (StreamReader sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8))
                {
                    string t = sr.ReadToEnd();
                    int code = (int)r.StatusCode;
                    // 503 у бесплатного шлюза — это «очередь переполнена»,
                    // а не поломка: человеку нужен понятный текст
                    if (code == 503)
                        return "сервис перегружен (503) — попробую ещё раз";
                    if (code == 429)
                        return "слишком много запросов (429) — подожди минуту";
                    if (code == 401 || code == 403)
                        return "ключ не подошёл (" + code + ") — проверь модель в настройках";
                    if (t.Length > 200) t = t.Substring(0, 200);
                    return "HTTP " + code + " " + t.Trim();
                }
            }
        }
        catch { }
        return we.Message;
    }

    // Модели нравится markdown, а Label его не умеет: убираем разметку,
    // оставляя структуру (списки, переносы).
    public static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace("\r\n", "\n").Replace("\r", "\n");
        s = s.Replace("```", "");
        s = s.Replace("**", "").Replace("__", "").Replace("`", "");
        s = s.Replace("<br>", "\n").Replace("<br/>", "\n");
        StringBuilder sb = new StringBuilder(s.Length);
        string[] lines = s.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string l = lines[i];
            string tr = l.TrimStart();
            if (tr.StartsWith("### ")) l = "• " + tr.Substring(4);
            else if (tr.StartsWith("## ")) l = "• " + tr.Substring(3);
            else if (tr.StartsWith("# ")) l = "• " + tr.Substring(2);
            else if (tr.StartsWith("- ") || tr.StartsWith("* ")) l = "• " + tr.Substring(2);
            else if (tr.StartsWith("> ")) l = tr.Substring(2);
            sb.Append(l);
            if (i < lines.Length - 1) sb.Append('\n');
        }
        string res = sb.ToString().Trim();
        while (res.Contains("\n\n\n")) res = res.Replace("\n\n\n", "\n\n");
        return res;
    }
}