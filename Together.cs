using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// ================================================================
//  ЗАЯВКИ В ДРУЗЬЯ И СОВМЕСТНАЯ ИГРА
//
//  Сервис Luxe умеет только односторонний список друзей: /friends/add
//  сразу добавляет ник в мой список, входящих заявок там нет. Поэтому
//  заявки и приглашения в мир идут отдельным слоем через публичный
//  ntfy.sh: у каждого ника свой «почтовый ящик»-топик, сообщения
//  подписаны id и разбираются локально. Друг попадает в список только
//  после «Принять» — до этого он лежит во вкладке «Заявки».
//
//  Совместная игра без своего хостинга — туннель bore.pub. Лаунчер
//  хоста замечает в latest.log строку «Local game hosted on port N»,
//  поднимает TCP-туннель и получает публичный адрес bore.pub:PORT.
//  Гость запускает игру с --quickPlayMultiplayer host:port (для старых
//  версий — --server/--port) и попадает прямо в мир.
// ================================================================

// ---------- сообщение из топика ----------
class SignalMsg
{
    public string NtfyId = "";    // id сообщения ntfy (дедупликация)
    public string Pid = "";       // id внутри письма (по нему отвечаем)
    public string Kind = "";
    public string From = "";
    public string To = "";
    public string Addr = "";
    public string Rid = "";
    public string Ver = "";       // версия Minecraft хоста (для автоподключения)       // на какую заявку ответ
    public double Time = 0;
}

// ---------- заявка / приглашение ----------
class SocialReq
{
    public string Id = "";
    public string Name = "";
    public double Time = 0;
}

class SocialInvite
{
    public string Name = "";
    public string Addr = "";
    public string Ver = "";
    public double Time = 0;
}

// ================================================================
//  ПОЧТА НА NTFY.SH
// ================================================================

static class LuxeSignal
{
    public const string Relay = "https://ntfy.sh";
    const string Agent = "LuxeLauncher/" + Cfg.AppVersion;

    public static string LastError = "";

    // Топик — это фактически пароль: кто знает ник, тот может писать.
    // Для заявок в друзья этого достаточно, секретов в письмах нет.
    public static string TopicFor(string nick)
    {
        string raw = nick == null ? "" : nick.Trim().ToLowerInvariant();
        StringBuilder sb = new StringBuilder();
        foreach (char c in raw)
        {
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-')
                sb.Append(c);
        }
        string s = sb.ToString();
        if (s.Length == 0) s = "p" + StableHash(raw);
        if (s.Length > 48) s = s.Substring(0, 48);
        return "luxe_sig_" + s;
    }

    // одинаковый хеш в разных запусках — встроенный GetHashCode для этого не годится
    static string StableHash(string s)
    {
        uint h = 2166136261;
        for (int i = 0; i < s.Length; i++) { h ^= (uint)s[i]; h *= 16777619; }
        return h.ToString("x8");
    }

    static HttpWebRequest Make(string url, string method, int seconds)
    {
        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
        req.Method = method;
        req.Timeout = seconds * 1000;
        req.ReadWriteTimeout = seconds * 1000;
        req.UserAgent = Agent;
        req.KeepAlive = false;
        return req;
    }

    // Отправить письмо. id нужен вызывающему: по нему приходит ответ.
    public static bool Publish(string from, string to, string kind, string addr,
        string rid, string id, out string err)
    {
        return Publish(from, to, kind, addr, rid, id, "", out err);
    }

    public static bool Publish(string from, string to, string kind, string addr,
        string rid, string id, string ver, out string err)
    {
        err = "";
        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) { err = "пустой адресат"; return false; }
        if (string.IsNullOrEmpty(id)) id = Guid.NewGuid().ToString("N");
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["k"] = kind;
        d["from"] = from;
        d["to"] = to;
        d["id"] = id;
        d["t"] = (long)LauncherForm.NowUnix();
        if (!string.IsNullOrEmpty(addr)) d["addr"] = addr;
        if (!string.IsNullOrEmpty(rid)) d["rid"] = rid;
        if (!string.IsNullOrEmpty(ver)) d["ver"] = ver;
        string body = Auth.Json.Serialize(d);
        try
        {
            HttpWebRequest req = Make(Relay + "/" + TopicFor(to), "POST", 15);
            req.ContentType = "text/plain; charset=utf-8";
            req.Headers["Title"] = "Luxe";
            byte[] b = Encoding.UTF8.GetBytes(body);
            req.ContentLength = b.Length;
            using (Stream s = req.GetRequestStream()) s.Write(b, 0, b.Length);
            using (HttpWebResponse r = (HttpWebResponse)req.GetResponse())
            {
                try { r.Close(); } catch { }
            }
            LastError = "";
            return true;
        }
        catch (Exception ex) { LastError = Cut(ex.Message); err = LastError; return false; }
    }

    // Забрать накопленное из своего топика. null — сеть недоступна.
    public static List<SignalMsg> Poll(string me, double since, int seconds)
    {
        string sinceArg = since <= 0 ? "all" : ((long)since).ToString();
        string url = Relay + "/" + TopicFor(me) + "/json?poll=1&since=" + sinceArg;
        try
        {
            HttpWebRequest req = Make(url, "GET", seconds);
            using (HttpWebResponse r = (HttpWebResponse)req.GetResponse())
            using (StreamReader sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8))
                return Parse(sr.ReadToEnd(), me);
        }
        catch (Exception ex) { LastError = Cut(ex.Message); return null; }
    }

    static List<SignalMsg> Parse(string text, string me)
    {
        List<SignalMsg> list = new List<SignalMsg>();
        if (string.IsNullOrEmpty(text)) return list;
        string[] lines = text.Replace("\r", "").Split('\n');
        foreach (string line in lines)
        {
            if (line.Trim().Length == 0) continue;
            Dictionary<string, object> env = null;
            try { env = Auth.Json.DeserializeObject(line) as Dictionary<string, object>; } catch { }
            if (env == null) continue;
            string ev = T(env, "event");
            if (ev.Length > 0 && ev != "message") continue;
            string msg = T(env, "message");
            if (msg.Length == 0) continue;
            Dictionary<string, object> d = null;
            try { d = Auth.Json.DeserializeObject(msg) as Dictionary<string, object>; } catch { }
            if (d == null) continue;

            SignalMsg m = new SignalMsg();
            m.NtfyId = T(env, "id");
            m.Pid = T(d, "id");
            m.Kind = T(d, "k");
            m.From = T(d, "from");
            m.To = T(d, "to");
            m.Addr = T(d, "addr");
            m.Rid = T(d, "rid");
            m.Ver = T(d, "ver");
            try { m.Time = Convert.ToDouble(env["time"]); } catch { }
            if (m.Kind.Length == 0 || m.From.Length == 0) continue;
            if (m.To.Length > 0 && me.Length > 0 &&
                !string.Equals(m.To, me, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(m.From, me, StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(m);
        }
        return list;
    }

    static string T(Dictionary<string, object> d, string k)
    {
        object o;
        if (d == null || !d.TryGetValue(k, out o) || o == null) return "";
        return o.ToString();
    }

    static string Cut(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length > 90 ? s.Substring(0, 90) + "..." : s;
    }
}

// ================================================================
//  ЛОКАЛЬНОЕ ХРАНИЛИЩЕ ЗАЯВОК
// ================================================================

static class SocialStore
{
    static readonly object Sync = new object();
    static bool loaded = false;

    static List<SocialReq> outReq = new List<SocialReq>();
    static List<SocialReq> inReq = new List<SocialReq>();
    static List<SocialInvite> invites = new List<SocialInvite>();
    static List<string> seen = new List<string>();
    static double lastPoll = 0;

    static string FilePath()
    {
        string root = string.IsNullOrEmpty(Cfg.DataDir) ? Cfg.BaseDir : Cfg.DataDir;
        return Path.Combine(root, "social.json");
    }

    public static void Init() { lock (Sync) { Load(); } }

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        try
        {
            string p = FilePath();
            if (!File.Exists(p)) return;
            Dictionary<string, object> d = Auth.Json.DeserializeObject(File.ReadAllText(p, Encoding.UTF8))
                as Dictionary<string, object>;
            if (d == null) return;
            outReq = ParseReq(Arr(d, "out"));
            inReq = ParseReq(Arr(d, "in"));
            invites = ParseInv(Arr(d, "invites"));
            seen.Clear();
            foreach (object o in Arr(d, "seen"))
            {
                string s = o == null ? "" : o.ToString();
                if (s.Length > 0) seen.Add(s);
            }
            try { if (d.ContainsKey("lastPoll")) lastPoll = Convert.ToDouble(d["lastPoll"]); } catch { }
        }
        catch { }
    }

    static object[] Arr(Dictionary<string, object> d, string k)
    {
        object o;
        if (d == null || !d.TryGetValue(k, out o) || o == null) return new object[0];
        object[] a = o as object[];
        return a == null ? new object[0] : a;
    }

    static List<SocialReq> ParseReq(object[] a)
    {
        List<SocialReq> list = new List<SocialReq>();
        foreach (object o in a)
        {
            Dictionary<string, object> d = o as Dictionary<string, object>;
            if (d == null) continue;
            SocialReq r = new SocialReq();
            r.Id = S(d, "id");
            r.Name = S(d, "name");
            try { r.Time = Convert.ToDouble(d["t"]); } catch { }
            if (r.Name.Length > 0) list.Add(r);
        }
        return list;
    }

    static List<SocialInvite> ParseInv(object[] a)
    {
        List<SocialInvite> list = new List<SocialInvite>();
        foreach (object o in a)
        {
            Dictionary<string, object> d = o as Dictionary<string, object>;
            if (d == null) continue;
            SocialInvite i = new SocialInvite();
            i.Name = S(d, "name");
            i.Addr = S(d, "addr");
            i.Ver = S(d, "ver");
            try { i.Time = Convert.ToDouble(d["t"]); } catch { }
            if (i.Name.Length > 0 && i.Addr.Length > 0) list.Add(i);
        }
        return list;
    }

    static string S(Dictionary<string, object> d, string k)
    {
        object o;
        if (d == null || !d.TryGetValue(k, out o) || o == null) return "";
        return o.ToString();
    }

    static void SaveLocked()
    {
        try
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["out"] = PackReq(outReq);
            d["in"] = PackReq(inReq);
            d["invites"] = PackInv(invites);
            d["seen"] = seen.ToArray();
            d["lastPoll"] = lastPoll;
            string p = FilePath();
            string dir = Path.GetDirectoryName(p);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(p, Auth.Json.Serialize(d), Encoding.UTF8);
        }
        catch { }
    }

    static object[] PackReq(List<SocialReq> list)
    {
        object[] a = new object[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["id"] = list[i].Id;
            d["name"] = list[i].Name;
            d["t"] = list[i].Time;
            a[i] = d;
        }
        return a;
    }

    static object[] PackInv(List<SocialInvite> list)
    {
        object[] a = new object[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["name"] = list[i].Name;
            d["addr"] = list[i].Addr;
            if (list[i].Ver.Length > 0) d["ver"] = list[i].Ver;
            d["t"] = list[i].Time;
            a[i] = d;
        }
        return a;
    }

    public static void Save() { lock (Sync) { Load(); SaveLocked(); } }

    // ---------- исходящие ----------

    public static string AddOut(string name)
    {
        lock (Sync)
        {
            Load();
            RemoveOutLocked(name);
            SocialReq r = new SocialReq();
            r.Id = Guid.NewGuid().ToString("N");
            r.Name = name;
            r.Time = LauncherForm.NowUnix();
            outReq.Add(r);
            SaveLocked();
            return r.Id;
        }
    }

    public static bool HasOut(string name)
    {
        lock (Sync)
        {
            Load();
            foreach (SocialReq r in outReq)
                if (Eq(r.Name, name)) return true;
            return false;
        }
    }

    public static List<SocialReq> OutCopy()
    {
        lock (Sync) { Load(); return new List<SocialReq>(outReq); }
    }

    public static int OutCount() { lock (Sync) { Load(); return outReq.Count; } }

    public static bool RemoveOutById(string id)
    {
        lock (Sync)
        {
            Load();
            if (string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < outReq.Count; i++)
            {
                if (outReq[i].Id == id) { outReq.RemoveAt(i); SaveLocked(); return true; }
            }
            return false;
        }
    }

    public static bool RemoveOutByName(string name)
    {
        lock (Sync)
        {
            Load();
            bool any = RemoveOutLocked(name);
            if (any) SaveLocked();
            return any;
        }
    }

    static bool RemoveOutLocked(string name)
    {
        bool any = false;
        for (int i = outReq.Count - 1; i >= 0; i--)
        {
            if (Eq(outReq[i].Name, name)) { outReq.RemoveAt(i); any = true; }
        }
        return any;
    }

    // ---------- входящие ----------

    public static bool AddIn(string id, string name)
    {
        lock (Sync)
        {
            Load();
            foreach (SocialReq r in inReq)
                if (Eq(r.Name, name) || (id.Length > 0 && r.Id == id)) return false;
            SocialReq n = new SocialReq();
            n.Id = id;
            n.Name = name;
            n.Time = LauncherForm.NowUnix();
            inReq.Add(n);
            SaveLocked();
            return true;
        }
    }

    public static List<SocialReq> InCopy()
    {
        lock (Sync) { Load(); return new List<SocialReq>(inReq); }
    }

    public static int InCount() { lock (Sync) { Load(); return inReq.Count; } }

    public static bool RemoveInById(string id)
    {
        lock (Sync)
        {
            Load();
            for (int i = 0; i < inReq.Count; i++)
            {
                if ((id.Length > 0 && inReq[i].Id == id) || inReq[i].Id.Length == 0)
                {
                    inReq.RemoveAt(i);
                    SaveLocked();
                    return true;
                }
            }
            return false;
        }
    }

    public static void RemoveInByName(string name)
    {
        lock (Sync)
        {
            Load();
            for (int i = inReq.Count - 1; i >= 0; i--)
                if (Eq(inReq[i].Name, name)) inReq.RemoveAt(i);
            SaveLocked();
        }
    }

    // ---------- приглашения в мир ----------

    public static void SetInvite(string name, string addr)
    {
        SetInvite(name, addr, "");
    }

    public static void SetInvite(string name, string addr, string ver)
    {
        lock (Sync)
        {
            Load();
            for (int i = invites.Count - 1; i >= 0; i--)
                if (Eq(invites[i].Name, name)) invites.RemoveAt(i);
            SocialInvite n = new SocialInvite();
            n.Name = name;
            n.Addr = addr;
            n.Ver = ver == null ? "" : ver;
            n.Time = LauncherForm.NowUnix();
            invites.Add(n);
            SaveLocked();
        }
    }

    public static List<SocialInvite> InvitesCopy()
    {
        lock (Sync) { Load(); return new List<SocialInvite>(invites); }
    }

    public static string InviteFor(string name)
    {
        lock (Sync)
        {
            Load();
            foreach (SocialInvite i in invites)
                if (Eq(i.Name, name)) return i.Addr;
            return "";
        }
    }

    public static string InviteVerFor(string name)
    {
        lock (Sync)
        {
            Load();
            foreach (SocialInvite i in invites)
                if (Eq(i.Name, name)) return i.Ver;
            return "";
        }
    }

    public static void RemoveInvite(string name)
    {
        lock (Sync)
        {
            Load();
            for (int i = invites.Count - 1; i >= 0; i--)
                if (Eq(invites[i].Name, name)) invites.RemoveAt(i);
            SaveLocked();
        }
    }

    // ---------- служебное ----------

    public static bool MarkSeen(string id)
    {
        lock (Sync)
        {
            Load();
            if (string.IsNullOrEmpty(id)) return true;
            if (seen.Contains(id)) return false;
            seen.Add(id);
            if (seen.Count > 500) seen.RemoveRange(0, seen.Count - 500);
            return true;
        }
    }

    public static double LastPollTime() { lock (Sync) { Load(); return lastPoll; } }

    public static void SetLastPoll(double t) { lock (Sync) { Load(); lastPoll = t; } }

    static bool Eq(string a, string b)
    {
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}

// ================================================================
//  ТУННЕЛЬ BORE.PUB
//
//  Протокол: TCP на bore.pub:7835, кадры JSON с нулевым байтом.
//    -> {"Hello":0}           <- {"Hello":<публичный порт>}
//    <- {"Connection":"uuid"}  (на этот uuid — новое TCP-соединение)
//    -> {"Accept":"uuid"}      и дальше просто байты в обе стороны
//  "Heartbeat" от релея игнорируем.
// ================================================================

class BoreTunnel
{
    public const string Relay = "bore.pub";
    public const int ControlPort = 7835;

    public volatile bool Running;
    public volatile int PublicPort;
    public int LocalPort;
    public string LastError = "";
    public Action Closed = null;

    TcpClient control;
    NetworkStream ctrl;
    Thread controlThread;
    readonly object wl = new object();
    readonly List<TcpClient> live = new List<TcpClient>();
    volatile bool stop;

    public bool Start(int localPort, out string err)
    {
        err = "";
        Stop();
        stop = false;
        LocalPort = localPort;
        try
        {
            control = Connect(Relay, ControlPort, 9000);
            ctrl = control.GetStream();
            ctrl.ReadTimeout = 9000;
            SendRaw(ctrl, "{\"Hello\":0}");
            string hello = ReadFrame(ctrl);
            if (hello == null) { err = "релей не ответил"; Stop(); return false; }
            int port = ParsePort(hello);
            if (port <= 0) { err = "релей: " + hello; Stop(); return false; }
            PublicPort = port;
            // 0 здесь НЕ «бесконечно»: .NET бросает ArgumentOutOfRangeException
            // («Возможное время ожидания - Infinite или положительное»).
            // Бесконечное ожидание задаётся через -1 (Timeout.Infinite).
            ctrl.ReadTimeout = -1;
            Running = true;
            controlThread = new Thread(ControlLoop);
            controlThread.IsBackground = true;
            controlThread.Start();
            return true;
        }
        catch (Exception ex)
        {
            // Полный текст ошибки пишем в лог — иначе Short() режет
            // имя параметра и стектрейс, по которым видно, откуда бросок.
            try
            {
                string logDir = string.IsNullOrEmpty(Cfg.DataDir) ? Cfg.BaseDir : Cfg.DataDir;
                System.IO.Directory.CreateDirectory(logDir);
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(logDir, "tunnel_error.log"),
                    ex.GetType().FullName + "\\r\\n" + ex.Message + "\\r\\n\\r\\n" + ex.StackTrace,
                    System.Text.Encoding.UTF8);
            }
            catch { }
            err = Short(ex.Message);
            Stop();
            return false;
        }
    }

    public void Stop()
    {
        stop = true;
        Running = false;
        try { if (control != null) control.Close(); } catch { }
        control = null;
        ctrl = null;
        lock (wl)
        {
            foreach (TcpClient c in live) { try { c.Close(); } catch { } }
            live.Clear();
        }
    }

    void ControlLoop()
    {
        try
        {
            while (!stop)
            {
                string f = ReadFrame(ctrl);
                if (f == null) break;
                int i = f.IndexOf("\"Connection\"");
                if (i < 0) continue;                    // Heartbeat и прочее
                string id = ParseId(f);
                if (id.Length == 0) continue;
                Thread h = new Thread(delegate() { Handle(id); });
                h.IsBackground = true;
                h.Start();
            }
        }
        catch { }
        if (!stop)
        {
            Running = false;
            LastError = "релей закрыл соединение";
            try { if (Closed != null) Closed(); } catch { }
        }
    }

    void Handle(string id)
    {
        TcpClient remote = null;
        TcpClient local = null;
        try
        {
            remote = Connect(Relay, ControlPort, 9000);
            NetworkStream rs = remote.GetStream();
            SendRaw(rs, "{\"Accept\":\"" + id + "\"}");
            local = Connect("127.0.0.1", LocalPort, 6000);
            NetworkStream ls = local.GetStream();
            lock (wl) { live.Add(remote); live.Add(local); }
            Thread a = new Thread(delegate() { Pipe(rs, ls, remote, local); });
            Thread b = new Thread(delegate() { Pipe(ls, rs, local, remote); });
            a.IsBackground = true;
            b.IsBackground = true;
            a.Start();
            b.Start();
        }
        catch
        {
            try { if (remote != null) remote.Close(); } catch { }
            try { if (local != null) local.Close(); } catch { }
        }
    }

    void Pipe(NetworkStream from, NetworkStream to, TcpClient ca, TcpClient cb)
    {
        try
        {
            byte[] buf = new byte[8192];
            while (true)
            {
                int n = from.Read(buf, 0, buf.Length);
                if (n <= 0) break;
                to.Write(buf, 0, n);
                to.Flush();
            }
        }
        catch { }
        try { ca.Close(); } catch { }
        try { cb.Close(); } catch { }
        lock (wl) { live.Remove(ca); live.Remove(cb); }
    }

    // Вместо WaitOne(int) (у некоторых сборок .NET он бросал
    // ArgumentOutOfRangeException про «время ожидания») подключаемся
    // через Task.Wait(ms) — он корректно работает с положительным таймаутом.
    static TcpClient Connect(string host, int port, int ms)
    {
        TcpClient c = new TcpClient();
        c.NoDelay = true;
        var task = System.Threading.Tasks.Task.Factory.FromAsync(
            c.BeginConnect, c.EndConnect, host, port, null);
        if (!task.Wait(ms))
        {
            try { c.Close(); } catch { }
            throw new IOException("таймаут подключения");
        }
        if (task.IsFaulted)
        {
            try { c.Close(); } catch { }
            throw task.Exception.InnerException;
        }
        return c;
    }

    void SendRaw(Stream s, string json)
    {
        byte[] b = Encoding.UTF8.GetBytes(json + "\0");
        lock (wl) { s.Write(b, 0, b.Length); s.Flush(); }
    }

    static string ReadFrame(Stream s)
    {
        List<byte> buf = new List<byte>(256);
        while (true)
        {
            int v = s.ReadByte();
            if (v < 0) return null;
            if (v == 0) break;
            if (buf.Count < 4096) buf.Add((byte)v);
        }
        return Encoding.UTF8.GetString(buf.ToArray());
    }

    static int ParsePort(string hello)
    {
        int c = hello.IndexOf(':');
        if (c < 0) return 0;
        int p = c + 1;
        while (p < hello.Length && hello[p] == ' ') p++;
        int v = 0, n = 0;
        while (p < hello.Length && hello[p] >= '0' && hello[p] <= '9' && n < 5)
        {
            v = v * 10 + (hello[p] - '0');
            p++;
            n++;
        }
        return n > 0 ? v : 0;
    }

    static string ParseId(string frame)
    {
        int a = frame.IndexOf("\"Connection\":\"");
        if (a < 0) return "";
        a += 14;
        int b = frame.IndexOf('"', a);
        if (b <= a) return "";
        return frame.Substring(a, b - a);
    }

    static string Short(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length > 80 ? s.Substring(0, 80) + "..." : s;
    }
}

// одна общая точка для всех: запуск/остановка/адрес
static class LuxeTunnel
{
    static BoreTunnel tunnel;
    static bool dbg;
    static int dbgPort;

    public static Action Closed = null;

    public static bool Active
    {
        get { return dbg || (tunnel != null && tunnel.Running); }
    }

    public static int PublicPort
    {
        get { return dbg ? dbgPort : (tunnel == null ? 0 : tunnel.PublicPort); }
    }

    public static string PublicAddress
    {
        get { return Active && PublicPort > 0 ? "bore.pub:" + PublicPort : ""; }
    }

    public static string LastError
    {
        get { return tunnel == null ? "" : tunnel.LastError; }
    }

    public static bool Start(int localPort, out string err)
    {
        Stop();
        tunnel = new BoreTunnel();
        tunnel.Closed = delegate { if (Closed != null) Closed(); };
        return tunnel.Start(localPort, out err);
    }

    public static void Stop()
    {
        dbg = false;
        if (tunnel != null)
        {
            try { tunnel.Stop(); } catch { }
            tunnel = null;
        }
    }

    // только для отладочных снимков
    public static void DebugOn(int port) { dbg = true; dbgPort = port; }
}

// ================================================================
//  ПОРТ ЛОКАЛЬНОГО МИРА ИЗ latest.log
// ================================================================

class LanWatcher
{
    public string LogPath = "";
    public Action<int> Found = null;      // 0 — мир закрылся

    volatile bool stop;
    Thread th;
    int port;
    string carry = "";

    public int Port { get { return port; } }

    public void Start()
    {
        stop = false;
        port = TailPort();
        th = new Thread(Loop);
        th.IsBackground = true;
        th.Start();
        if (port > 0 && Found != null) Found(port);
    }

    public void Stop()
    {
        stop = true;
    }

    // что было в конце файла до запуска слежения
    int TailPort()
    {
        try
        {
            if (LogPath == null || !File.Exists(LogPath)) return 0;
            using (FileStream fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                long len = fs.Length;
                long from = Math.Max(0, len - 196608);
                fs.Seek(from, SeekOrigin.Begin);
                byte[] b = new byte[len - from];
                int n = fs.Read(b, 0, b.Length);
                string t = Encoding.UTF8.GetString(b, 0, n);
                int at;
                int p = LastPort(t, out at);
                int stopAt = t.LastIndexOf("Stopping server");
                if (p > 0 && at > stopAt) return p;
            }
        }
        catch { }
        return 0;
    }

    void Loop()
    {
        long pos = -1;
        while (!stop)
        {
            try
            {
                if (LogPath != null && File.Exists(LogPath))
                {
                    using (FileStream fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        if (pos < 0 || fs.Length < pos) pos = fs.Length;
                        if (fs.Length > pos)
                        {
                            fs.Seek(pos, SeekOrigin.Begin);
                            long left = fs.Length - pos;
                            if (left > 262144) { fs.Seek(fs.Length - 65536, SeekOrigin.Begin); left = 65536; }
                            byte[] b = new byte[left];
                            int n = fs.Read(b, 0, b.Length);
                            pos = fs.Position;
                            string chunk = Encoding.UTF8.GetString(b, 0, n);
                            string t = carry + chunk;
                            carry = t.Length > 300 ? t.Substring(t.Length - 300) : t;

                            int at;
                            int p = LastPort(t, out at);
                            int stopAt = t.LastIndexOf("Stopping server");
                            if (p > 0 && p != port) { port = p; if (Found != null) Found(p); }
                            else if (port > 0 && stopAt > at) { port = 0; if (Found != null) Found(0); }
                        }
                    }
                }
            }
            catch { }
            for (int i = 0; i < 8 && !stop; i++) Thread.Sleep(150);
        }
    }

    static int LastPort(string t, out int at)
    {
        int a1;
        int p1 = PortAfter(t, "Local game hosted on port ", out a1);
        int a2;
        int p2 = PortAfter(t, "Started serving on ", out a2);
        if (p1 > 0 && (p2 <= 0 || a1 > a2)) { at = a1; return p1; }
        at = a2;
        return p2;
    }

    static int PortAfter(string t, string marker, out int at)
    {
        at = -1;
        int i = t.LastIndexOf(marker);
        if (i < 0) return 0;
        int p = i + marker.Length;
        while (p < t.Length && (t[p] == ' ' || t[p] == ':')) p++;
        int v = 0, n = 0;
        while (p < t.Length && t[p] >= '0' && t[p] <= '9' && n < 5)
        {
            v = v * 10 + (t[p] - '0');
            p++;
            n++;
        }
        if (n == 0) return 0;
        at = i;
        return v;
    }
}

// ================================================================
//  ИНТЕРФЕЙС: ЧИПЫ «ДРУЗЬЯ / ЗАЯВКИ / ВМЕСТЕ»
// ================================================================

partial class LauncherForm
{
    int friendsTab = 0;                       // 0 друзья, 1 заявки, 2 вместе
    RoundedButton friendsTabAll;
    RoundedButton friendsTabReq;
    RoundedButton friendsTabPlay;
    Label friendsReqSummary;

    LanWatcher lanWatch;
    int lanPort = 0;
    bool tunnelBusy = false;
    bool signalBusy = false;
    string signalErr = "";              // последняя ошибка опроса ntfy.sh
    System.Windows.Forms.Timer signalTimer;

    // отладка снимков
    public static bool DbgSocialDemo = false;
    public static int DbgFriendsTab = -1;

    // ---------- сборка ----------

    void BuildTogetherUi()
    {
        friendsTabAll = FriendChip("Друзья", 0, 92);
        friendsTabReq = FriendChip("Заявки", 1, 108);
        friendsTabPlay = FriendChip("Вместе", 2, 100);

        friendsReqSummary = new Label();
        friendsReqSummary.Text = "";
        friendsReqSummary.Font = Ui.Font(9);
        friendsReqSummary.ForeColor = Ui.Gray;
        friendsReqSummary.BackColor = Ui.Bg;
        friendsReqSummary.AutoSize = false;
        friendsReqSummary.Location = new Point(SecX + 4, SecY + 92);
        friendsReqSummary.Size = new Size(520, 20);
        friendsReqSummary.Visible = false;
        Controls.Add(friendsReqSummary);
        friendsUi.Add(friendsReqSummary);

        FriendsPaintTabs();
        FriendsApplyTab();
    }

    RoundedButton FriendChip(string text, int tab, int w)
    {
        RoundedButton b = new RoundedButton();
        b.Text = text;
        b.Radius = 6;
        b.Font = Ui.Font(9, FontStyle.Bold);
        b.FitText = true;
        b.Size = new Size(w, 32);
        b.Click += delegate { FriendsSelectTab(tab); };
        Controls.Add(b);
        friendsUi.Add(b);
        return b;
    }

    void LayoutTogetherTabs(int w)
    {
        int w1 = 92, w2 = 108, w3 = 100, gap = 6;
        int total = w1 + w2 + w3 + gap * 2;
        // 26 — запас под скроллбар, как в RowWidth: правый край чипов
        // совпадает с правым краем карточек, а не выпирает за него
        int innerW = Math.Max(320, w - 26);
        int x = SecX + Math.Max(380, innerW - total);
        if (friendsTabAll != null) friendsTabAll.Location = new Point(x, SecY + 1);
        if (friendsTabReq != null) friendsTabReq.Location = new Point(x + w1 + gap, SecY + 1);
        if (friendsTabPlay != null)
            friendsTabPlay.Location = new Point(x + w1 + gap + w2 + gap, SecY + 1);
        if (friendsReqSummary != null) friendsReqSummary.Width = Math.Max(200, innerW - 20);
    }

    void FriendsPaintTabs()
    {
        CapesPaintTab(friendsTabAll, friendsTab == 0);
        CapesPaintTab(friendsTabReq, friendsTab == 1);
        CapesPaintTab(friendsTabPlay, friendsTab == 2);
    }

    void FriendsApplyTab()
    {
        try
        {
            bool rq = friendsTab == 1;
            bool pl = friendsTab == 2;
            if (friendsAddBox != null) friendsAddBox.Visible = !rq;
            if (friendAddBtn != null)
            {
                friendAddBtn.Visible = !rq;
                string t = pl ? "Подключиться" : "Добавить";
                if (friendAddBtn.Text != t)
                {
                    friendAddBtn.Text = t;
                    friendAddBtn.Icon = pl ? Ico.Play : Ico.Plus;
                    friendAddBtn.Invalidate();
                }
            }
            if (friendsAddHint != null)
                friendsAddHint.Text = pl ? "bore.pub:25565" : "ник друга — и Enter";
            if (friendsReqSummary != null) friendsReqSummary.Visible = rq;
            if (rq && friendBox != null && friendBox.Focused && friendsTabReq != null)
                friendsTabReq.Focus();
        }
        catch { }
    }

    void FriendsSelectTab(int t)
    {
        if (t < 0 || t > 2) t = 0;
        bool was = friendsTab == t;
        friendsTab = t;
        FriendsPaintTabs();
        FriendsApplyTab();
        if (t == 1) SignalTick();
        if (t == 2) TogetherEnsureWatch();
        if (was) FriendsNote();
        else
        {
            FriendsRender();
            // при смене вкладки список начинается сверху: иначе позиция
            // прокрутки «Друзей» переносилась на «Заявки» и «Вместе»
            if (friendsList != null) friendsList.ResetScroll();
        }
    }

    // нижняя строка состояния и счётчик заявок на чипе
    void FriendsNote()
    {
        try
        {
            int inN = SocialStore.InCount();
            int outN = SocialStore.OutCount();
            if (friendsTabReq != null)
            {
                string t = inN > 0 ? "Заявки " + inN : "Заявки";
                if (friendsTabReq.Text != t)
                {
                    friendsTabReq.Text = t;
                    friendsTabReq.Invalidate();
                }
            }
            if (friendsTab == 0)
            {
                int on = 0, all = 0;
                if (friendsCache != null)
                {
                    all = friendsCache.Count;
                    foreach (LuxeFriend f in friendsCache) if (f.Online) on++;
                }
                FriendState(friendsCache == null ? "Загружаю список..." :
                    "Друзей: " + all + " • в лаунчере: " + on, true);
                FriendsBadge(on, all);
            }
            else if (friendsTab == 1)
            {
                FriendState("Входящие: " + inN + " • исходящие: " + outN, true);
                if (friendsReqSummary != null)
                {
                    // счётчики уже есть в строке состояния снизу — здесь
                    // только состояние связи, чтобы не дублировать цифры
                    friendsReqSummary.Text = signalErr.Length > 0
                        ? "Нет связи с сервером заявок: " + signalErr
                        : "Новые заявки проверяются каждые 12 секунд";
                    friendsReqSummary.ForeColor = signalErr.Length > 0 ? Ui.Gold : Ui.Gray;
                }
            }
            else
            {
                string s;
                if (LuxeTunnel.Active) s = "Туннель работает: " + LuxeTunnel.PublicAddress;
                else if (lanPort > 0) s = "Мир открыт на порту " + lanPort + " — включи туннель";
                else s = "Мир не открыт — в игре Esc → «Открыть для сети»";
                FriendState(s, true);
            }
        }
        catch { }
    }

    // основная кнопка строки ввода: «Добавить» или «Подключиться»
    void FriendsPrimary()
    {
        if (friendsTab == 2) TogetherConnect();
        else FriendAdd();
    }

    // ============================================================
    //  ЗАЯВКИ
    // ============================================================

    void RequestsRender()
    {
        List<SocialReq> inc = SocialStore.InCopy();
        List<SocialReq> outg = SocialStore.OutCopy();
        if (inc.Count == 0 && outg.Count == 0)
        {
            InviteHint("Заявок нет.\r\n\r\n"
                + "Вкладка «Друзья» → впиши ник → «Добавить». Человек увидит заявку здесь "
                + "и сможет принять или отклонить. В список друзей вы попадёте только после «Принять».");
            return;
        }
        if (inc.Count > 0)
        {
            friendsList.Controls.Add(SectionCaption("ВХОДЯЩИЕ"));
            foreach (SocialReq r in inc) friendsList.Controls.Add(RequestRow(r, true));
        }
        if (outg.Count > 0)
        {
            friendsList.Controls.Add(SectionCaption("ИСХОДЯЩИЕ"));
            foreach (SocialReq r in outg) friendsList.Controls.Add(RequestRow(r, false));
        }
    }

    Label SectionCaption(string text)
    {
        Label l = new Label();
        l.Text = text;
        l.Font = Ui.Font(8.5f, FontStyle.Bold);
        l.ForeColor = Ui.GrayDim;
        l.BackColor = Ui.Bg;
        l.AutoSize = false;
        l.TextAlign = ContentAlignment.MiddleLeft;
        l.Size = new Size(RowWidth(friendsList), 22);
        l.Margin = new Padding(2, 4, 0, 4);
        return l;
    }

    RoundedPanel RequestRow(SocialReq r, bool incoming)
    {
        SocialReq req = r;
        RoundedPanel p = new RoundedPanel();
        p.Radius = 14;
        p.BackColor = Ui.Card;
        p.Border = Ui.Line;
        p.HoverBack = Ui.CardHover;
        p.HoverBorder = incoming ? Ui.Accent1 : Ui.Border;
        p.Animated = true;
        p.Size = new Size(RowWidth(friendsList), 62);
        p.Margin = new Padding(0, 0, 0, 10);

        Label dot = new Label();
        dot.Text = "●";
        dot.Font = Ui.Font(13);
        dot.ForeColor = incoming ? Ui.Accent1 : Ui.GrayDim;
        dot.BackColor = Color.Transparent;
        dot.AutoSize = false;
        dot.TextAlign = ContentAlignment.MiddleCenter;
        dot.Location = new Point(10, 0);
        dot.Size = new Size(34, 62);
        p.Controls.Add(dot);

        int rightPad = incoming ? 240 : 132;

        Label nm = new Label();
        nm.Text = Ui.Short(req.Name, 22);
        nm.Font = Ui.Font(11, FontStyle.Bold);
        nm.ForeColor = Ui.Txt;
        nm.BackColor = Color.Transparent;
        nm.AutoSize = false;
        nm.Location = new Point(48, 12);
        nm.Size = new Size(Math.Max(120, p.Width - 60 - rightPad), 22);
        p.Controls.Add(nm);

        Label st = new Label();
        st.Text = incoming
            ? "хочет добавить в друзья • " + Ago(req.Time)
            : "заявка отправлена • ждём ответа";
        st.Font = Ui.Font(8.5f);
        st.ForeColor = incoming ? Ui.Gray : Ui.GrayDim;
        st.BackColor = Color.Transparent;
        st.AutoSize = false;
        st.Location = new Point(48, 34);
        st.Size = new Size(Math.Max(120, p.Width - 60 - rightPad), 18);
        p.Controls.Add(st);

        if (incoming)
        {
            RoundedButton no = RoundedButton.Dark("Отклонить", 10);
            no.Font = Ui.Font(8.5f, FontStyle.Bold);
            no.Size = new Size(112, 34);
            no.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            no.Click += delegate { FriendDecline(req); };
            p.Controls.Add(no);

            RoundedButton yes = new RoundedButton();
            yes.Text = "Принять";
            yes.Icon = Ico.Check;
            yes.IconSize = 14;
            yes.Radius = 10;
            yes.Font = Ui.Font(8.5f, FontStyle.Bold);
            yes.Size = new Size(104, 34);
            yes.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            yes.Click += delegate { FriendAccept(req); };
            p.Controls.Add(yes);

            p.Resize += delegate
            {
                try
                {
                    yes.Left = p.Width - 12 - yes.Width;
                    no.Left = yes.Left - 8 - no.Width;
                    nm.Width = Math.Max(120, p.Width - 60 - 240);
                    st.Width = nm.Width;
                }
                catch { }
            };
            yes.Location = new Point(p.Width - 12 - yes.Width, 14);
            no.Location = new Point(yes.Left - 8 - no.Width, 14);
        }
        else
        {
            RoundedButton cancel = RoundedButton.Dark("Отменить", 10);
            cancel.Font = Ui.Font(8.5f, FontStyle.Bold);
            cancel.Size = new Size(112, 34);
            cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            cancel.Click += delegate { FriendCancel(req); };
            p.Controls.Add(cancel);
            p.Resize += delegate
            {
                try
                {
                    cancel.Left = p.Width - 12 - cancel.Width;
                    nm.Width = Math.Max(120, p.Width - 60 - 132);
                    st.Width = nm.Width;
                }
                catch { }
            };
            cancel.Location = new Point(p.Width - 12 - cancel.Width, 14);
        }
        return p;
    }

    void FriendAccept(SocialReq r)
    {
        if (!Luxe.Ready) { FriendState("Сервис недоступен, подождите", false); return; }
        FriendState("Принимаю " + r.Name + "...", true);
        string me = SignalMe();
        Thread t = new Thread((ThreadStart)delegate
        {
            bool ok = false;
            try { ok = Luxe.AddFriend(r.Name); } catch { }
            string err = Luxe.LastError;
            if (ok && me.Length > 0)
            {
                string e2 = "";
                try { LuxeSignal.Publish(me, r.Name, "friend_ok", "", r.Id, "", out e2); } catch { }
            }
            SocialPost(delegate
            {
                if (!ok) { FriendState("Не получилось: " + err, false); return; }
                SocialStore.RemoveInById(r.Id);
                FriendlyAdded(r.Name);
            });
        });
        t.IsBackground = true;
        t.Start();
    }

    void FriendDecline(SocialReq r)
    {
        SocialStore.RemoveInById(r.Id);
        // страховка для старых писем без id: убираем по нику, иначе
        // карточка оставалась бы висеть после «Отклонить»
        SocialStore.RemoveInByName(r.Name);
        FriendsRender();
        FriendState("Заявка отклонена: " + r.Name, true);
        string me = SignalMe();
        if (me.Length == 0) return;
        string id = Guid.NewGuid().ToString("N");
        Thread t = new Thread((ThreadStart)delegate
        {
            string e = "";
            try { LuxeSignal.Publish(me, r.Name, "friend_no", "", r.Id, id, out e); } catch { }
        });
        t.IsBackground = true;
        t.Start();
    }

    void FriendCancel(SocialReq r)
    {
        SocialStore.RemoveOutById(r.Id);
        FriendsRender();
        FriendState("Заявка отменена: " + r.Name, true);
    }

    // ============================================================
    //  ВМЕСТЕ
    // ============================================================

    void TogetherRender()
    {
        friendsList.Controls.Add(TogetherHostCard());

        List<SocialInvite> inv = SocialStore.InvitesCopy();
        if (inv.Count == 0)
        {
            InviteHint("Приглашений пока нет.\r\n\r\n"
                + "Друг открывает мир и туннель, потом в списке друзей «…» → «Позвать в свой мир». "
                + "Приглашение появится здесь вместе с адресом — останется нажать «Подключиться».");
            return;
        }
        friendsList.Controls.Add(SectionCaption("ПРИГЛАШЕНИЯ"));
        foreach (SocialInvite i in inv) friendsList.Controls.Add(InviteRow(i));
    }

    void InviteHint(string text)
    {
        Label l = new Label();
        l.Text = text;
        l.Font = Ui.Font(10);
        l.ForeColor = Ui.Gray;
        l.BackColor = Ui.Bg;
        l.AutoSize = false;
        l.Location = new Point(4, 8);
        l.Size = new Size(Math.Max(280, RowWidth(friendsList) - 8), 100);
        l.Margin = new Padding(0, 8, 0, 0);
        friendsList.Controls.Add(l);
    }

    RoundedPanel TogetherHostCard()
    {
        RoundedPanel p = new RoundedPanel();
        p.Radius = 14;
        p.BackColor = Ui.Card;
        p.Border = Ui.Line;
        p.HoverBack = Ui.CardHover;
        p.HoverBorder = Ui.Line;
        p.Animated = true;
        p.Size = new Size(RowWidth(friendsList), 188);
        p.Margin = new Padding(0, 0, 0, 12);

        PixelLabel title = new PixelLabel();
        title.Text = "Мой мир";
        title.Font = Ui.Font(12, FontStyle.Bold);
        title.ForeColor = Ui.Txt;
        title.BackColor = Color.Transparent;
        title.AutoSize = false;
        title.Location = new Point(18, 14);
        title.Size = new Size(220, 24);
        p.Controls.Add(title);

        bool on = LuxeTunnel.Active;
        Label st = new Label();
        st.AutoSize = false;
        st.BackColor = Color.Transparent;
        st.Font = Ui.Font(9);
        st.Location = new Point(18, 42);
        st.Size = new Size(p.Width - 36, 18);
        if (on)
        {
            st.Text = "Туннель работает — друг может подключиться";
            st.ForeColor = Ui.Green;
        }
        else if (lanPort > 0)
        {
            st.Text = "Мир открыт на порту " + lanPort + " — осталось включить туннель";
            st.ForeColor = Ui.Gold;
        }
        else
        {
            st.Text = "Мир не открыт. В игре: Esc → «Открыть для сети»";
            st.ForeColor = Ui.Gray;
        }
        p.Controls.Add(st);

        Label pub = new Label();
        pub.Text = on ? LuxeTunnel.PublicAddress : "—";
        pub.Font = Ui.Font(15, FontStyle.Bold);
        pub.ForeColor = on ? Ui.Accent1 : Ui.GrayDim;
        pub.BackColor = Color.Transparent;
        pub.AutoSize = false;
        pub.Location = new Point(18, 66);
        pub.Size = new Size(360, 30);
        p.Controls.Add(pub);

        Label lan = new Label();
        string lanAddr = LanAddress(lanPort);
        lan.Text = lanPort > 0
            ? (lanAddr.Length > 0 ? "по сети: " + lanAddr : "по сети: адрес не найден")
            : "тут появится адрес, когда мир откроется";
        lan.Font = Ui.Font(8.5f);
        lan.ForeColor = Ui.GrayDim;
        lan.BackColor = Color.Transparent;
        lan.AutoSize = false;
        lan.Location = new Point(18, 98);
        lan.Size = new Size(p.Width - 36, 18);
        p.Controls.Add(lan);

        // Без иконки: у Ico.Stop рисунок занимает половину квадрата, и
        // рядом с текстом он читался как точка-буллит с большим зазором.
        // У соседней «Скопировать адрес» иконки тоже нет — пара ровнее.
        RoundedButton tun = new RoundedButton();
        tun.Text = on ? "Остановить туннель" : "Открыть туннель";
        tun.Radius = 10;
        tun.Font = Ui.Font(9, FontStyle.Bold);
        tun.Enabled = on || lanPort > 0;
        tun.Location = new Point(18, 134);
        tun.Size = new Size(204, 36);
        tun.Click += delegate { TogetherTunnelToggle(); };
        p.Controls.Add(tun);

        RoundedButton copy = RoundedButton.Dark("Скопировать адрес", 10);
        copy.Font = Ui.Font(9, FontStyle.Bold);
        copy.Enabled = on;
        copy.Location = new Point(232, 134);
        copy.Size = new Size(176, 36);
        copy.Click += delegate { CopyText(LuxeTunnel.PublicAddress, "Адрес скопирован"); };
        p.Controls.Add(copy);

        RoundedButton inviteAll = RoundedButton.Dark("Пригласить всех", 10);
        inviteAll.Font = Ui.Font(9, FontStyle.Bold);
        inviteAll.Enabled = on;
        inviteAll.Location = new Point(418, 134);
        inviteAll.Size = new Size(176, 36);
        inviteAll.Click += delegate { AutoInviteFriends(); };
        p.Controls.Add(inviteAll);

        p.Resize += delegate
        {
            try
            {
                st.Width = p.Width - 36;
                lan.Width = p.Width - 36;
            }
            catch { }
        };
        return p;
    }

    RoundedPanel InviteRow(SocialInvite i)
    {
        SocialInvite inv = i;
        RoundedPanel p = new RoundedPanel();
        p.Radius = 14;
        p.BackColor = Ui.Card;
        p.Border = Ui.Line;
        p.HoverBack = Ui.CardHover;
        p.HoverBorder = Ui.Accent1;
        p.Animated = true;
        p.Size = new Size(RowWidth(friendsList), 76);
        p.Margin = new Padding(0, 0, 0, 10);

        Label dot = new Label();
        dot.Text = "●";
        dot.Font = Ui.Font(13);
        dot.ForeColor = Ui.Green;
        dot.BackColor = Color.Transparent;
        dot.AutoSize = false;
        dot.TextAlign = ContentAlignment.MiddleCenter;
        dot.Location = new Point(10, 0);
        dot.Size = new Size(34, 76);
        p.Controls.Add(dot);

        Label nm = new Label();
        nm.Text = Ui.Short(inv.Name, 22);
        nm.Font = Ui.Font(11, FontStyle.Bold);
        nm.ForeColor = Ui.Txt;
        nm.BackColor = Color.Transparent;
        nm.AutoSize = false;
        nm.Location = new Point(48, 12);
        nm.Size = new Size(300, 22);
        p.Controls.Add(nm);

        Label st = new Label();
        string verSuffix = (inv.Ver != null && inv.Ver.Length > 0) ? " • " + inv.Ver : "";
        st.Text = "приглашает в мир • " + Ago(inv.Time) + verSuffix;
        st.Font = Ui.Font(8.5f);
        st.ForeColor = Ui.Gray;
        st.BackColor = Color.Transparent;
        st.AutoSize = false;
        st.Location = new Point(48, 33);
        st.Size = new Size(360, 17);
        p.Controls.Add(st);

        Label addr = new Label();
        addr.Text = inv.Addr;
        addr.Font = Ui.Font(9, FontStyle.Bold);
        addr.ForeColor = Ui.Accent1;
        addr.BackColor = Color.Transparent;
        addr.AutoSize = false;
        addr.Location = new Point(48, 51);
        addr.Size = new Size(300, 17);
        p.Controls.Add(addr);

        RoundedButton go = new RoundedButton();
        go.Text = "Подключиться";
        go.Icon = Ico.Play;
        go.IconSize = 13;
        go.Radius = 10;
        go.Font = Ui.Font(8.5f, FontStyle.Bold);
        go.FitText = true;
        go.Size = new Size(136, 34);
        go.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        go.Click += delegate { TogetherJoinInvite(inv); };
        p.Controls.Add(go);

        RoundedButton no = RoundedButton.IcoBtn(Ico.Close, 32, 10);
        no.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        no.Click += delegate
        {
            SocialStore.RemoveInvite(inv.Name);
            FriendsRender();
        };
        p.Controls.Add(no);

        p.Resize += delegate
        {
            try
            {
                go.Left = p.Width - 12 - go.Width;
                no.Left = go.Left - 6 - no.Width;
                int wd = Math.Max(120, no.Left - 60);
                nm.Width = wd;
                st.Width = wd;
                addr.Width = wd;
            }
            catch { }
        };
        go.Location = new Point(p.Width - 12 - go.Width, 21);
        no.Location = new Point(go.Left - 6 - no.Width, 22);
        return p;
    }

    // Пытаемся выбрать нужную версию автоматически (без ошибки, если её нет)
    void SelectVersionIfAvailable(string v)
    {
        if (string.IsNullOrEmpty(v)) return;
        try
        {
            // ищем в выпадающем списке
            for (int i = 0; i < cmbVer.Items.Count; i++)
            {
                string item = cmbVer.Items[i] == null ? "" : cmbVer.Items[i].ToString();
                if (item.StartsWith(v, StringComparison.OrdinalIgnoreCase))
                {
                    cmbVer.SelectedIndex = i;
                    return;
                }
            }
        }
        catch { }
    }

    void TogetherJoinInvite(SocialInvite i)
    {
        if (friendBox != null)
        {
            friendBox.Text = i.Addr;
            friendBox.ForeColor = Ui.Txt;
        }
        // Автоподбор версии хоста, чтобы гостю не выбирать вручную
        if (i.Ver != null && i.Ver.Length > 0)
        {
            SelectVersionIfAvailable(i.Ver);
        }
        // Если версия установлена — запускаем сразу, без лишнего клика
        string v = curVer();
        if (!string.IsNullOrEmpty(v) && Mc.IsInstalled(v))
        {
            TogetherConnect();
        }
        else
        {
            // Версия не установлена — просим выбрать/установить
            SetStatus("Установи версию " + (i.Ver ?? "") + " и нажми «Подключиться»", false);
            FriendsSelectTab(2);
        }
    }

    void TogetherConnect()
    {
        string addr = friendBox == null ? "" : NormalizeAddress(friendBox.Text);
        if (addr.Length == 0)
        {
            ShowError("Адрес мира: bore.pub:25565 или 192.168.0.5:25565");
            return;
        }
        string ver = curVer();
        if (string.IsNullOrEmpty(ver))
        {
            ShowError("Сначала выбери версию на главной");
            return;
        }
        // StartGame сам ник не проверяет — это делает только кнопка Play,
        // поэтому гость без ника иначе запустил бы игру с пустым именем
        if (!System.Text.RegularExpressions.Regex.IsMatch(nick, "^[a-zA-Z0-9_]{3,16}$"))
        {
            ShowError("Сначала укажи ник в Аккаунтах: 3-16 символов a-z, 0-9, _");
            return;
        }
        FriendState("Подключаюсь к " + addr + "...", true);
        StartGame(ver, addr);
    }

    void TogetherTunnelToggle()
    {
        if (tunnelBusy) return;
        if (LuxeTunnel.Active)
        {
            LuxeTunnel.Stop();
            SetStatus("Туннель остановлен", true);
            FriendsRender();
            return;
        }
        if (lanPort <= 0)
        {
            ShowError("Сначала открой мир в игре: Esc → «Открыть для сети»");
            return;
        }
        tunnelBusy = true;
        FriendState("Подключаюсь к bore.pub...", true);
        int port = lanPort;
        Thread t = new Thread((ThreadStart)delegate
        {
            string err = "";
            bool ok = false;
            try { ok = LuxeTunnel.Start(port, out err); } catch (Exception ex) { err = ex.Message; }
            SocialPost(delegate
            {
                tunnelBusy = false;
                if (!ok)
                {
                    ShowError("Туннель: " + err);
                    FriendsRender();
                    return;
                }
                WireTunnelClosed();
                SetStatus("Туннель работает: " + LuxeTunnel.PublicAddress, true);
                // Автоматизация: приглашаем всех друзей, чтобы им не
                // нужно было ждать ручного «Позвать в свой мир»
                AutoInviteFriends();
                FriendsRender();
            });
        });
        t.IsBackground = true;
        t.Start();
    }

    void WireTunnelClosed()
    {
        LuxeTunnel.Closed = delegate
        {
            SocialPost(delegate
            {
                SetStatus("Туннель закрыт", false);
                if (view == ViewFriends) FriendsRender();
            });
        };
    }

    void TogetherEnsureWatch()
    {
        if (DbgSocialDemo) return;          // на снимках порт не подменяем из старых логов
        try
        {
            string ver = curVer();
            string dir = string.IsNullOrEmpty(ver) ? Cfg.McDir : Mc.GameDirFor(ver);
            string log = Path.Combine(Path.Combine(dir, "logs"), "latest.log");
            if (lanWatch != null && lanWatch.LogPath == log) return;
            if (lanWatch != null) lanWatch.Stop();
            lanWatch = new LanWatcher();
            lanWatch.LogPath = log;
            lanWatch.Found = delegate(int p)
            {
                SocialPost(delegate { LanPortFound(p); });
            };
            lanWatch.Start();
        }
        catch { }
    }

    void LanPortFound(int p)
    {
        lanPort = p;
        if (p > 0) SetStatus("Мир открыт на порту " + p + " — можно включать туннель", true);
        else
        {
            if (LuxeTunnel.Active)
            {
                LuxeTunnel.Stop();
                SetStatus("Мир закрыт — туннель остановлен", false);
            }
        }
        if (view == ViewFriends && friendsTab == 2) FriendsRender();
        FriendsNote();
        // Автоматизация: если мир открыт и туннель ещё не поднят —
        // поднимаем его сами, чтобы хозяину не нажимать лишнюю кнопку
        if (p > 0 && !LuxeTunnel.Active && !tunnelBusy && view == ViewFriends && friendsTab == 2)
        {
            TogetherTunnelToggle();
        }
    }

    // приглашение другу: «…» → «Позвать в свой мир»
    void TogetherInvite(string name)
    {
        string addr = LuxeTunnel.PublicAddress;
        if (addr.Length == 0)
        {
            ShowError("Сначала открой мир в игре и включи туннель на вкладке «Вместе»");
            FriendsSelectTab(2);
            return;
        }
        SendSignal(name, "join", addr, "", curVer());
        SetStatus("Приглашение в мир отправлено: " + name, true);
        FriendState("Приглашение отправлено: " + name, true);
    }

    // Автоматически приглашаем всех друзей, когда туннель поднят
    void AutoInviteFriends()
    {
        try
        {
            string addr = LuxeTunnel.PublicAddress;
            if (addr.Length == 0) return;
            List<string> names = new List<string>();
            if (friendsCache != null)
            {
                foreach (LuxeFriend f in friendsCache)
                    names.Add(f.Name);
            }
            if (names.Count == 0) return;
            int sent = 0;
            foreach (string n in names)
            {
                SendSignal(n, "join", addr, "", curVer());
                sent++;
            }
            SetStatus("Приглашения отправлены: " + sent + " друзей — " + addr, true);
            FriendState("Приглашения отправлены " + sent + " друзьям", true);
        }
        catch { }
    }

    // «…» → «Играть вместе»
    void TogetherConnectTo(string name)
    {
        FriendsSelectTab(2);
        string addr = SocialStore.InviteFor(name);
        if (addr.Length > 0)
        {
            if (friendBox != null)
            {
                friendBox.Text = addr;
                friendBox.ForeColor = Ui.Txt;
                friendBox.Focus();
            }
            SetStatus(name + ": адрес подставлен — нажми «Подключиться»", true);
        }
        else
        {
            SendSignal(name, "join_req", "", "");
            SetStatus(name + " сейчас не присылал адрес — запросили у него", true);
        }
    }

    // ============================================================
    //  СИГНАЛЫ
    // ============================================================

    void StartSignalTimer()
    {
        try
        {
            SocialStore.Init();
            signalTimer = new System.Windows.Forms.Timer();
            signalTimer.Interval = 12000;
            signalTimer.Tick += delegate { SignalTick(); };
            signalTimer.Start();
        }
        catch { }
    }

    string SignalMe()
    {
        try
        {
            if (Luxe.Ready && Luxe.MyName.Length > 0) return Luxe.MyName.Trim();
            if (Auth.LoggedIn && Auth.Name.Length > 0) return Auth.Name.Trim();
        }
        catch { }
        return nick == null ? "" : nick.Trim();
    }

    void SignalTick()
    {
        if (signalBusy) return;
        string me = SignalMe();
        if (me.Length == 0) return;
        signalBusy = true;
        double since = SocialStore.LastPollTime();
        if (since > 0) since -= 120;          // перекрытие на случай расхождения часов
        Thread t = new Thread((ThreadStart)delegate
        {
            List<SignalMsg> list = null;
            try { list = LuxeSignal.Poll(me, since, 12); } catch { }
            SocialPost(delegate
            {
                signalBusy = false;
                if (list == null)
                {
                    // молчащий опрос выглядел как «заявки не приходят»;
                    // показываем причину в строке над списком
                    signalErr = LuxeSignal.LastError;
                    if (view == ViewFriends) FriendsNote();
                    return;
                }
                signalErr = "";
                SocialStore.SetLastPoll(LauncherForm.NowUnix());
                bool any = false;
                foreach (SignalMsg m in list)
                {
                    string key = m.NtfyId.Length > 0 ? m.NtfyId : (m.Pid + "|" + m.Kind);
                    if (!SocialStore.MarkSeen(key)) continue;
                    any = true;
                    try { SignalApply(m); } catch { }
                }
                if (any) SocialStore.Save();
                if (view == ViewFriends) FriendsRender();
                else FriendsNote();
            });
        });
        t.IsBackground = true;
        t.Start();
    }

    void SendSignal(string to, string kind, string addr, string rid)
    {
        SendSignal(to, kind, addr, rid, "");
    }

    void SendSignal(string to, string kind, string addr, string rid, string ver)
    {
        string me = SignalMe();
        if (me.Length == 0) { ShowError("Не определён ник — письмо отправить некому"); return; }
        string id = Guid.NewGuid().ToString("N");
        Thread t = new Thread((ThreadStart)delegate
        {
            string err = "";
            bool ok = false;
            try { ok = LuxeSignal.Publish(me, to, kind, addr, rid, id, ver, out err); } catch { }
            if (!ok) SocialPost(delegate { SetStatus("Не отправилось: " + err, false); });
        });
        t.IsBackground = true;
        t.Start();
    }

    void SignalApply(SignalMsg m)
    {
        string from = m.From;
        if (from.Length == 0) return;

        if (m.Kind == "friend_req")
        {
            if (IsFriend(from))
            {
                SendSignal(from, "friend_ok", "", m.Pid);
                return;
            }
            if (SocialStore.AddIn(m.Pid, from))
                SetStatus("Заявка в друзья: " + from + " — вкладка «Заявки»", true);
        }
        else if (m.Kind == "friend_ok")
        {
            bool had = SocialStore.RemoveOutById(m.Rid);
            if (!had) had = SocialStore.RemoveOutByName(from);
            if (!IsFriend(from)) FriendAddRemote(from);
            SetStatus(from + " принял(а) заявку — теперь вы друзья", true);
        }
        else if (m.Kind == "friend_no")
        {
            bool had = SocialStore.RemoveOutById(m.Rid);
            if (!had) had = SocialStore.RemoveOutByName(from);
            if (had) SetStatus(from + " отклонил(а) заявку", false);
        }
        else if (m.Kind == "friend_del")
        {
            if (IsFriend(from)) FriendRemoveRemote(from);
            SetStatus(from + " убрал(а) тебя из друзей", false);
        }
        else if (m.Kind == "join")
        {
            if (m.Addr.Length == 0) return;
            SocialStore.SetInvite(from, m.Addr, m.Ver);
            SetStatus(from + " приглашает в мир: " + m.Addr, true);
            // Автоматически переключаемся на вкладку «Вместе» и показываем
            // приглашение, чтобы гостю не нужно было искать его вручную
            if (view == ViewFriends)
            {
                FriendsSelectTab(2);
            }
            else
            {
                // Если раздел не открыт — хотя бы мигнём статусом
            }
        }
        else if (m.Kind == "join_req")
        {
            string addr = LuxeTunnel.PublicAddress;
            if (addr.Length > 0) SendSignal(from, "join", addr, m.Pid);
            else SendSignal(from, "join_no", "", m.Pid);
        }
        else if (m.Kind == "join_no")
        {
            SetStatus(from + " сейчас не открывает мир", false);
        }
    }

    bool IsFriend(string n)
    {
        if (friendsCache == null) return false;
        foreach (LuxeFriend f in friendsCache)
            if (string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    void FriendAddRemote(string name)
    {
        Thread t = new Thread((ThreadStart)delegate
        {
            try { Luxe.AddFriend(name); } catch { }
            SocialPost(delegate { FriendsLoad(); });
        });
        t.IsBackground = true;
        t.Start();
    }

    void FriendRemoveRemote(string name)
    {
        Thread t = new Thread((ThreadStart)delegate
        {
            try { Luxe.DelFriend(name); } catch { }
            SocialPost(delegate { FriendsLoad(); });
        });
        t.IsBackground = true;
        t.Start();
    }

    // после принятия: убрать из входящих, перерисовать, показать во «Друзьях»
    void FriendlyAdded(string name)
    {
        SocialStore.RemoveInByName(name);
        SocialStore.RemoveOutByName(name);
        SocialStore.Save();
        FriendState("Теперь вы друзья: " + name, true);
        FriendsLoad();
        FriendsChipsSooner();
    }

    void FriendsChipsSooner()
    {
        try { FriendsRender(); } catch { }
    }

    // ============================================================
    //  МЕНЮ ТРЁХ ТОЧЕК У ДРУГА
    // ============================================================

    void FriendMenu(LuxeFriend f, Control anchor)
    {
        LuxeFriend fr = f;
        ContextMenuStrip m = new ContextMenuStrip();
        m.BackColor = Ui.Surface;
        m.ForeColor = Ui.Txt;
        m.Font = Ui.Font(9);
        m.Renderer = new ToolStripProfessionalRenderer();
        m.Items.Add("Играть вместе", null, delegate { TogetherConnectTo(fr.Name); });
        m.Items.Add("Позвать в свой мир", null, delegate { TogetherInvite(fr.Name); });
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Удалить из друзей", null, delegate { FriendRemove(fr.Name); });
        m.Show(anchor, new Point(0, anchor.Height + 4));
    }

    // ============================================================
    //  МЕЛОЧИ
    // ============================================================

    static string NormalizeAddress(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Trim();
        s = s.Replace("https://", "").Replace("http://", "");
        while (s.EndsWith("/")) s = s.Substring(0, s.Length - 1);
        int c = s.LastIndexOf(':');
        if (c <= 0 || c == s.Length - 1) return "";
        string host = s.Substring(0, c).Trim();
        string port = s.Substring(c + 1).Trim();
        int p;
        if (host.Length < 1) return "";
        if (!int.TryParse(port, out p)) return "";
        if (p < 1 || p > 65535) return "";
        return host + ":" + p;
    }

    static string AddressHost(string a)
    {
        if (string.IsNullOrEmpty(a)) return "";
        int c = a.LastIndexOf(':');
        return c > 0 ? a.Substring(0, c) : a;
    }

    static string AddressPort(string a)
    {
        if (string.IsNullOrEmpty(a)) return "25565";
        int c = a.LastIndexOf(':');
        return c > 0 ? a.Substring(c + 1) : "25565";
    }

    static string LanAddress(int port)
    {
        if (port <= 0) return "";
        try
        {
            string best = "";
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (UnicastIPAddressInformation ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    string ip = ua.Address.ToString();
                    if (ip.StartsWith("127.") || ip.StartsWith("169.254.")) continue;
                    if (ip.StartsWith("192.168.") || ip.StartsWith("10."))
                    {
                        best = ip;
                        break;
                    }
                    if (best.Length == 0) best = ip;
                }
                if (best.StartsWith("192.168.") || best.StartsWith("10.")) break;
            }
            return best.Length == 0 ? "" : best + ":" + port;
        }
        catch { return ""; }
    }

    void CopyText(string s, string ok)
    {
        if (string.IsNullOrEmpty(s)) return;
        try
        {
            Clipboard.SetText(s);
            SetStatus(ok + ": " + s, true);
        }
        catch { ShowError("Не получилось скопировать адрес"); }
    }

    // ============================================================
    //  ОТЛАДОЧНЫЕ СНИМКИ
    // ============================================================

    public void DebugFriendsTab(int t)
    {
        if (InvokeRequired) { Invoke(new Action<int>(DebugFriendsTab), t); return; }
        if (DbgSocialDemo) SocialDemoSeed();
        FriendsSelectTab(t);
    }

    void SocialDemoSeed()
    {
        if (friendsCache == null || friendsCache.Count == 0)
        {
            List<LuxeFriend> l = new List<LuxeFriend>();
            LuxeFriend a = new LuxeFriend();
            a.Name = "Steve"; a.Online = true; a.Last = NowUnix();
            l.Add(a);
            LuxeFriend b = new LuxeFriend();
            b.Name = "Alex"; b.Online = false; b.Last = NowUnix() - 4200;
            l.Add(b);
            LuxeFriend c = new LuxeFriend();
            c.Name = "Herobrine"; c.Online = true; c.Last = NowUnix() - 60;
            l.Add(c);
            friendsCache = l;
        }
        SocialStore.AddIn("demo_req_1", "Notch");
        SocialStore.AddIn("demo_req_2", "Dinnerbone");
        SocialStore.AddOut("Jeb_");
        SocialStore.SetInvite("Steve", "bore.pub:25475");
        lanPort = 54321;
        LuxeTunnel.DebugOn(25475);
        SocialStore.Save();
    }
}
