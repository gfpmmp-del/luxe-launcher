using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// ================================================================
//  ДРУЗЬЯ И ПЛАЩИ
//
//  Оба раздела работают через один сервис на Render (Luxe Service):
//    • кто из друзей сейчас в лаунчере
//    • каталог плащей и выбор своего
//
//  ПЛАЩИ ВАЖНО ПОНИМАТЬ ПРАВИЛЬНО. Скин виден всем, потому что он
//  живёт на аккаунте Microsoft. С плащами так нельзя: у Mojang нет
//  API для своего плаща. Поэтому плащ показывает отдельный мод,
//  который ставится в игру, — и видят его только те, у кого этот мод
//  есть, то есть пользователи лаунчера. Здесь, в лаунчере, игрок
//  только выбирает плащ; показывает его мод.
//
//  Адрес сервиса берётся из service_url.txt рядом с лаунчером,
//  чтобы поменять его без пересборки.
// ================================================================

// ---------- ответы сервиса ----------

class LuxeFriend
{
    public string Name = "";
    public string Uuid = "";
    public bool Online = false;
    public double Last = 0;
}

class LuxeCape
{
    public string Id = "";
    public string Name = "";
    public string Author = "";
    public string Owner = "";
    public string Url = "";
    public bool Builtin = false;
    public double Created = 0;      // когда плащ появился — по нему «Новые»
    public int Installs = 0;        // сколько раз надевали
    public int Wears = 0;           // сколько носят прямо сейчас — по нему «Популярные»
}

// ================================================================
//  КЛИЕНТ СЕРВИСА
// ================================================================

static class Luxe
{
    public const string DefaultBase = "https://luxe-service-w0kr.onrender.com";

    public static string Base = DefaultBase;
    public static string Session = "";
    public static string Me = "";
    public static string MyName = "";
    public static bool Offline = true;
    public static string LastError = "";

    static readonly object Sync = new object();
    static Dictionary<string, Image> imgCache = new Dictionary<string, Image>();

    public static bool Ready
    {
        get { lock (Sync) { return Session != ""; } }
    }

    public static string UrlPath()
    {
        return Path.Combine(Cfg.BaseDir, "service_url.txt");
    }

    public static string Host()
    {
        string h = Base == null ? "" : Base.Trim();
        while (h.EndsWith("/")) h = h.Substring(0, h.Length - 1);
        return h;
    }

    // Вызывается один раз при запуске лаунчера
    public static void Init()
    {
        try
        {
            // та же настройка, что и в Auth.cs: без TLS 1.2 сервис не ответит
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
        }
        catch { }
        try
        {
            string f = UrlPath();
            if (File.Exists(f))
            {
                string u = File.ReadAllText(f, Encoding.UTF8).Trim();
                if (u.StartsWith("http")) Base = u;
            }
        }
        catch { }
    }

    // ---------- сеть ----------

    static string Http(string method, string path, string body, bool auth, out int code)
    {
        code = 0;
        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Host() + path);
        req.Method = method;
        req.Timeout = 65000;                 // Render на бесплатном тарифе просыпается не сразу
        req.ReadWriteTimeout = 65000;
        req.UserAgent = "LuxeLauncher/" + Cfg.AppVersion;
        req.ContentType = "application/json";
        req.Accept = "application/json";
        string sess = Session;
        if (auth && sess != "") req.Headers["X-Session"] = sess;

        try
        {
            if (body != null)
            {
                byte[] b = Encoding.UTF8.GetBytes(body);
                req.ContentLength = b.Length;
                using (Stream s = req.GetRequestStream()) s.Write(b, 0, b.Length);
            }
            using (HttpWebResponse r = (HttpWebResponse)req.GetResponse())
            {
                code = (int)r.StatusCode;
                using (StreamReader sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
        }
        catch (WebException we)
        {
            HttpWebResponse r2 = we.Response as HttpWebResponse;
            if (r2 != null)
            {
                try
                {
                    code = (int)r2.StatusCode;
                    using (StreamReader sr = new StreamReader(r2.GetResponseStream(), Encoding.UTF8))
                        return sr.ReadToEnd();
                }
                catch { }
                finally { try { r2.Close(); } catch { } }
            }
            code = -1;
            throw;
        }
    }

    static Dictionary<string, object> Call(string method, string path,
        Dictionary<string, object> body, bool auth)
    {
        string json = body == null ? null : Auth.Json.Serialize(body);
        int code = 0;
        try
        {
            string resp = Http(method, path, json, auth, out code);
            Dictionary<string, object> d = Auth.Json.DeserializeObject(resp) as Dictionary<string, object>;
            if (d == null)
            {
                LastError = "сервис ответил непонятно";
                return null;
            }
            if (!Flag(d, "ok"))
            {
                LastError = Text(d, "error");
                if (LastError == "") LastError = "отказ сервиса";
                return null;
            }
            LastError = "";
            return d;
        }
        catch (Exception ex)
        {
            LastError = code == -1
                ? "сервис не отвечает"
                : ("сервис: " + Cut(ex.Message));
            return null;
        }
    }

    static string Cut(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length > 90 ? s.Substring(0, 90) + "..." : s;
    }

    public static bool Flag(Dictionary<string, object> d, string k)
    {
        object o;
        if (d == null || !d.TryGetValue(k, out o) || o == null) return false;
        if (o is bool) return (bool)o;
        return o.ToString().ToLower() == "true";
    }

    public static string Text(Dictionary<string, object> d, string k)
    {
        object o;
        if (d == null || !d.TryGetValue(k, out o) || o == null) return "";
        return o.ToString();
    }

    public static double Num(Dictionary<string, object> d, string k)
    {
        object o;
        if (d == null || !d.TryGetValue(k, out o) || o == null) return 0;
        try { return Convert.ToDouble(o); }
        catch { return 0; }
    }

    static object[] Arr(Dictionary<string, object> d, string k)
    {
        object o;
        if (d == null || !d.TryGetValue(k, out o) || o == null) return new object[0];
        object[] a = o as object[];
        return a == null ? new object[0] : a;
    }

    static Dictionary<string, object> Obj(object o)
    {
        return o as Dictionary<string, object>;
    }

    static Dictionary<string, object> Body(string k, object v)
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d[k] = v;
        return d;
    }

    // ---------- вход ----------

    public static bool Login(string token, string offlineNick)
    {
        Dictionary<string, object> body;
        if (!string.IsNullOrEmpty(token))
        {
            body = Body("token", token);
        }
        else
        {
            if (string.IsNullOrEmpty(offlineNick)) { LastError = "нет ника игрока"; return false; }
            body = Body("offline", offlineNick);
        }
        Dictionary<string, object> d = Call("POST", "/api/login", body, false);
        if (d == null) return false;
        lock (Sync)
        {
            Session = Text(d, "session");
            Me = Text(d, "uuid");
            MyName = Text(d, "name");
            Offline = Flag(d, "offline");
            return Session != "";
        }
    }

    public static void Logout()
    {
        lock (Sync) { Session = ""; Me = ""; MyName = ""; }
    }

    public static bool Heartbeat()
    {
        return Call("POST", "/api/heartbeat", Body("name", MyName), true) != null;
    }

    // ---------- друзья ----------

    public static List<LuxeFriend> Friends()
    {
        Dictionary<string, object> d = Call("GET", "/api/friends", null, true);
        if (d == null) return null;
        List<LuxeFriend> list = new List<LuxeFriend>();
        foreach (object o in Arr(d, "friends"))
        {
            Dictionary<string, object> f = Obj(o);
            if (f == null) continue;
            LuxeFriend fr = new LuxeFriend();
            fr.Name = Text(f, "name");
            fr.Uuid = Text(f, "uuid");
            fr.Online = Flag(f, "online");
            fr.Last = Num(f, "last");
            list.Add(fr);
        }
        return list;
    }

    public static bool AddFriend(string n)
    {
        return Call("POST", "/api/friends/add", Body("name", n), true) != null;
    }

    public static bool DelFriend(string n)
    {
        return Call("POST", "/api/friends/del", Body("name", n), true) != null;
    }

    // ---------- плащи ----------

    // Плащи живут на сервисе: там же картинки и счётчики. sort задаёт
    // порядок каталога: popular (по умолчанию), new, name.
    public static List<LuxeCape> Capes(string sort)
    {
        string path = "/api/capes";
        if (!string.IsNullOrEmpty(sort)) path += "?sort=" + sort;
        Dictionary<string, object> d = Call("GET", path, null, false);
        if (d == null) return null;
        List<LuxeCape> list = new List<LuxeCape>();
        foreach (object o in Arr(d, "capes"))
        {
            Dictionary<string, object> c = Obj(o);
            if (c == null) continue;
            LuxeCape it = new LuxeCape();
            it.Id = Text(c, "id");
            it.Name = Text(c, "name");
            it.Author = Text(c, "author");
            it.Owner = Text(c, "owner");
            it.Builtin = Flag(c, "builtin");
            it.Created = Num(c, "created");
            it.Installs = (int)Num(c, "installs");
            it.Wears = (int)Num(c, "wears");
            it.Url = "/capes/" + it.Id + ".png";
            list.Add(it);
        }
        return list;
    }

    public static bool Current(out string id)
    {
        id = "";
        Dictionary<string, object> d = Call("GET", "/api/capes/mine", null, true);
        if (d == null) return false;
        id = Text(d, "current");
        return true;
    }

    public static bool SetCape(string id)
    {
        return Call("POST", "/api/cape/set", Body("cape", id), true) != null;
    }

    public static bool UploadCape(byte[] png, string name)
    {
        Dictionary<string, object> body = new Dictionary<string, object>();
        body["name"] = name;
        body["png"] = Convert.ToBase64String(png);
        return Call("POST", "/api/cape/upload", body, true) != null;
    }

    // ---------- скины без лицензии ----------

    // Скины устроены так же, как плащи: у офлайн-игрока нет места на
    // серверах Mojang, поэтому картинка лежит на сервисе Luxe, а мод
    // подставляет её по uuid игрока. Видят её те, у кого стоит мод.

    // Сессия сервиса привязана к нику. Если ник в лаунчере поменяли,
    // входим заново, чтобы скин не ушёл старому игроку.
    public static bool LoginFor(string offlineNick)
    {
        if (string.IsNullOrEmpty(offlineNick)) { LastError = "нет ника игрока"; return false; }
        lock (Sync)
        {
            if (Session != "" && Offline &&
                string.Equals(MyName, offlineNick, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        Logout();
        return Login(null, offlineNick);
    }

    public static bool SetSkin(byte[] png, string model)
    {
        Dictionary<string, object> body = new Dictionary<string, object>();
        body["png"] = Convert.ToBase64String(png);
        body["model"] = model == "SLIM" ? "slim" : "classic";
        return Call("POST", "/api/skin/set", body, true) != null;
    }

    public static bool ClearSkin()
    {
        return Call("POST", "/api/skin/clear", null, true) != null;
    }

    // Мой скин на сервисе: url пустой, если его ещё нет.
    public static bool MySkin(out string url, out string model)
    {
        url = "";
        model = "classic";
        Dictionary<string, object> d = Call("GET", "/api/skin/mine", null, true);
        if (d == null) return false;
        url = Text(d, "url");
        model = Text(d, "model");
        return url.Length > 0;
    }

    // ---------- картинки плащей ----------

    // Картинки кладём в папку данных (Luxe Visuals\capes), а не рядом с exe:
    // при скачивании лаунчера рядом должен появляться только мир Minecraft.
    public static string CapesCacheDir()
    {
        string root = string.IsNullOrEmpty(Cfg.DataDir) ? Cfg.BaseDir : Cfg.DataDir;
        string dir = Path.Combine(root, "capes");
        try { Directory.CreateDirectory(dir); } catch { }
        return dir;
    }

    // Картинка плаща: память -> диск -> интернет.
    // allowNet=false — только то, что уже есть локально (список не ждёт сеть).
    public static Image CapeImage(string id, string url, bool force, bool allowNet)
    {
        if (string.IsNullOrEmpty(id)) return null;
        lock (imgCache)
        {
            if (!force && imgCache.ContainsKey(id)) return imgCache[id];
        }
        try
        {
            string file = Path.Combine(CapesCacheDir(), id + ".png");
            if (!force && File.Exists(file))
            {
                Image local = LoadCapeFile(file, id);
                if (local != null) return local;
            }
            if (!allowNet) return null;
            if (File.Exists(file))
            {
                try { File.Delete(file); } catch { }
            }
            string full = (url != null && url.StartsWith("http")) ? url : (Host() + "/capes/" + id + ".png");
            using (WebClient wc = new WebClient())
            {
                wc.Headers["User-Agent"] = "LuxeLauncher/" + Cfg.AppVersion;
                byte[] data = wc.DownloadData(full);
                if (data.Length > 0) File.WriteAllBytes(file, data);
            }
            return LoadCapeFile(file, id);
        }
        catch { return null; }
    }

    static Image LoadCapeFile(string file, string id)
    {
        try
        {
            using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read))
            using (Image im = Image.FromStream(fs))
            {
                Bitmap copy = new Bitmap(im);
                lock (imgCache) imgCache[id] = copy;
                return copy;
            }
        }
        catch { return null; }
    }

    public static void DropImageCache()
    {
        List<Image> trash = new List<Image>();
        lock (imgCache)
        {
            foreach (KeyValuePair<string, Image> kv in imgCache) trash.Add(kv.Value);
            imgCache.Clear();
        }
        foreach (Image im in trash) { try { im.Dispose(); } catch { } }
    }
}

// ================================================================
//  РАЗДЕЛЫ ЛАУНЧЕРА
// ================================================================

partial class LauncherForm
{
    public const int ViewFriends = 4;
    public const int ViewCapes = 5;

    // отладка снимков: --shot-run ... capetab:1 — открыть нужный фильтр «Плащей»
    public static int DbgCapeTab = -1;

    List<Control> friendsUi = new List<Control>();
    List<Control> capesUi = new List<Control>();
    NavButton navFriends;
    NavButton navCapes;
    List<NavButton> navList = new List<NavButton>();

    DarkFlowPanel friendsList;
    DarkFlowPanel capesList;
    TextBox friendBox;
    Label friendsState;
    Label friendsMe;
    Label capesState;
    RoundedButton friendAddBtn;
    RoundedButton friendsRefreshBtn;
    RoundedButton capesRefreshBtn;
    // ссылки на рамку поля ввода и подсказку: нужны LayoutFriends,
    // чтобы раскладка раздела считалась от фактической ширины
    RoundedPanel friendsAddBox;
    Label friendsAddHint;
    Label capesSub;

    List<LuxeFriend> friendsCache = null;
    List<LuxeCape> capesCache = null;
    string capesCurrent = "";

    int capesTab = 0;                        // 0 — популярные, 1 — новые, 2 — мои
    RoundedButton capesTabPop, capesTabNew, capesTabMine;
    // кнопки строки действий «Плащей»: раскладка считается от ширины окна
    RoundedButton capesOff, capesUp;
    int capeImgSeq = 0;                      // чтобы старый догрузчик картинок не портил новый список
    Dictionary<string, CapeView> capeViews = new Dictionary<string, CapeView>();

    bool socialBusy = false;
    bool socialRelogin = false;   // ник сменили, пока шёл вход — перезайти после него
    bool friendsBusy = false;
    bool friendsAgain = false;    // пока шёл запрос, список просили обновить ещё раз
    bool capesBusy = false;
    System.Windows.Forms.Timer socialTimer;

    // ============================================================
    //  ЗАПУСК И СЛУЖЕБНОЕ
    // ============================================================

    void SocialStart()
    {
        try
        {
            Luxe.Init();
            socialTimer = new System.Windows.Forms.Timer();
            socialTimer.Interval = 30000;
            socialTimer.Tick += delegate { SocialTick(); };
            socialTimer.Start();
        }
        catch { }
        StartSignalTimer();
        SocialLogin();
    }

    // Вход в сервис: с лицензией — по токену Minecraft, без неё — по нику
    void SocialLogin()
    {
        if (socialBusy) return;
        socialBusy = true;
        socialRelogin = false;
        string off = nick;
        string token = null;
        try { if (Auth.LoggedIn) token = Auth.EnsureToken(); }
        catch { }
        Thread t = new Thread((ThreadStart)delegate
        {
            bool ok = false;
            try { ok = Luxe.Login(token, off); } catch { }
            if (ok) { try { Luxe.Heartbeat(); } catch { } }
            SocialPost(delegate
            {
                socialBusy = false;
                SocialState();
                if (view == ViewFriends) FriendsLoad();
                SignalTick();
                // вход шёл со старым ником, а его уже поменяли — заходим заново
                if (socialRelogin) SocialLogin();
            });
        });
        t.IsBackground = true;
        t.Start();
    }

    // Перезайти: ник мог поменяться, вход в лицензию — тоже
    void SocialRelogin()
    {
        Luxe.Logout();
        SocialLogin();
    }

    void SocialTick()
    {
        if (!Luxe.Ready)
        {
            if (!socialBusy) SocialLogin();
            return;
        }
        Thread t = new Thread((ThreadStart)delegate
        {
            try { Luxe.Heartbeat(); }
            catch { }
        });
        t.IsBackground = true;
        t.Start();
        if (view == ViewFriends) FriendsLoad();
        // бейдж «Друзей» на сайдбаре: список нужен и без открытого раздела,
        // поэтому первый раз забираем его сразу после входа в сервис
        else if (friendsCache == null && !friendsBusy) FriendsLoad();
    }

    void SocialPost(Action a)
    {
        try
        {
            if (IsDisposed) return;
            if (InvokeRequired) BeginInvoke(a);
            else a();
        }
        catch { }
    }

    // Строка состояния: видно, поднялся ли сервис и под каким ником
    void SocialState()
    {
        try
        {
            string s;
            bool ok = true;
            if (Luxe.Ready)
            {
                s = "Вы: " + Luxe.MyName + (Luxe.Offline ? " (без лицензии)" : "") +
                    " • сервис на связи";
            }
            else
            {
                s = "Сервис: " + (Luxe.LastError == "" ? "нет связи" : Luxe.LastError);
                ok = false;
            }
            if (friendsMe != null)
            {
                friendsMe.Text = s;
                friendsMe.ForeColor = ok ? Ui.Gray : Ui.Red;
            }
        }
        catch { }
    }

    // ============================================================
    //  РАЗДЕЛ «ДРУЗЬЯ»
    // ============================================================

    void BuildFriendsSection()
    {
        friendsUi = new List<Control>();

        Panel bg = new Panel();
        bg.BackColor = Ui.Bg;
        bg.Location = new Point(SecX, SecY);
        bg.Size = new Size(888, 518);
        Controls.Add(bg); friendsUi.Add(bg);

        PixelLabel head = new PixelLabel();
        head.Text = "Друзья";
        head.Font = Ui.Font(17, FontStyle.Bold);
        head.ForeColor = Ui.Txt;
        head.BackColor = Ui.Bg;
        head.AutoSize = false;
        head.Location = new Point(SecX, SecY);
        head.Size = new Size(400, 32);
        Controls.Add(head); friendsUi.Add(head);

        Label sub = new Label();
        sub.Text = "Видно тех, кто держит лаунчер открытым. Новые друзья появляются только после принятия заявки";
        sub.Font = Ui.Font(9.5f);
        sub.ForeColor = Ui.Gray;
        sub.BackColor = Ui.Bg;
        sub.AutoSize = false;
        sub.Location = new Point(SecX, SecY + 34);
        sub.Size = new Size(700, 20);
        Controls.Add(sub); friendsUi.Add(sub);

        friendsMe = new Label();
        friendsMe.Text = "Сервис: подключение...";
        friendsMe.Font = Ui.Font(9);
        friendsMe.ForeColor = Ui.GrayDim;
        friendsMe.BackColor = Ui.Bg;
        friendsMe.AutoSize = false;
        friendsMe.Location = new Point(SecX, SecY + 58);
        friendsMe.Size = new Size(700, 18);
        Controls.Add(friendsMe); friendsUi.Add(friendsMe);

        // ---- строка ввода ника ----
        RoundedPanel addBox = new RoundedPanel();
        addBox.Radius = 14;
        addBox.BackColor = Ui.Card;
        addBox.Border = Ui.Line;
        addBox.HoverBorder = Ui.Accent1;
        addBox.Animated = true;
        addBox.Location = new Point(SecX, SecY + 84);
        addBox.Size = new Size(620, 42);
        Controls.Add(addBox); friendsUi.Add(addBox);
        friendsAddBox = addBox;

        IconView ico = new IconView();
        ico.Icon = Ico.User;
        ico.Col = Ui.GrayDim;
        ico.Location = new Point(14, 13);
        ico.Size = new Size(16, 16);
        addBox.Controls.Add(ico);

        friendBox = new TextBox();
        friendBox.Font = Ui.Font(10);
        friendBox.BackColor = Ui.Card;
        friendBox.ForeColor = Ui.Txt;
        friendBox.BorderStyle = BorderStyle.None;
        friendBox.Location = new Point(42, 12);
        friendBox.Size = new Size(400, 18);
        addBox.Controls.Add(friendBox);

        Label addHint = new Label();
        addHint.Text = "ник друга — и Enter";
        addHint.Font = Ui.Font(8);
        addHint.ForeColor = Ui.GrayDim;
        addHint.BackColor = Color.Transparent;
        addHint.AutoSize = false;
        addHint.Location = new Point(460, 13);
        addHint.Size = new Size(148, 16);
        addHint.TextAlign = ContentAlignment.MiddleRight;
        // подсказка лежит поверх поля и перехватывала бы клики по правой
        // части ввода — возвращаем курсор в поле
        addHint.Cursor = Cursors.IBeam;
        addHint.Click += delegate { friendBox.Focus(); };
        addBox.Controls.Add(addHint);
        friendsAddHint = addHint;

        friendBox.KeyDown += delegate(object s2, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            FriendAdd();
        };

        friendAddBtn = new RoundedButton();
        friendAddBtn.Text = "Добавить";
        friendAddBtn.Icon = Ico.Plus;
        friendAddBtn.IconSize = 14;
        friendAddBtn.Radius = 12;
        friendAddBtn.Font = Ui.Font(9, FontStyle.Bold);
        // на узком окне кнопка «Подключиться» уже не влезает целиком —
        // пусть шрифт подстраивается, а не режется по краю
        friendAddBtn.FitText = true;
        friendAddBtn.Location = new Point(SecX + 632, SecY + 84);
        friendAddBtn.Size = new Size(140, 42);
        friendAddBtn.Click += delegate { FriendsPrimary(); };
        Controls.Add(friendAddBtn); friendsUi.Add(friendAddBtn);

        friendsRefreshBtn = RoundedButton.IcoBtn(Ico.Refresh, 42, 12);
        friendsRefreshBtn.Location = new Point(SecX + 784, SecY + 84);
        friendsRefreshBtn.Click += delegate { FriendsLoad(); };
        Controls.Add(friendsRefreshBtn); friendsUi.Add(friendsRefreshBtn);

        // ---- список друзей ----
        friendsList = new DarkFlowPanel();
        friendsList.WrapContents = false;
        friendsList.FlowDirection = FlowDirection.TopDown;
        friendsList.Location = new Point(SecX, SecY + 140);
        friendsList.Size = new Size(864, 366);
        friendsList.BackColor = Ui.Bg;
        Controls.Add(friendsList); friendsUi.Add(friendsList);

        friendsState = new Label();
        friendsState.Text = "";
        friendsState.Font = Ui.Font(8.5f);
        friendsState.ForeColor = Ui.GrayDim;
        friendsState.BackColor = Ui.Bg;
        friendsState.AutoSize = false;
        friendsState.Location = new Point(SecX, SecY + 510);
        friendsState.Size = new Size(700, 18);
        Controls.Add(friendsState); friendsUi.Add(friendsState);

        // чипы «Друзья / Заявки / Вместе» в шапке раздела
        BuildTogetherUi();
    }

    void LayoutFriends(int w, int h)
    {
        if (friendsUi == null || friendsUi.Count < 4) return;
        friendsUi[0].Size = new Size(w, h);

        // Поле ввода + «Добавить» + обновление раньше стояли на жёстких
        // координатах (620/632/784), поэтому на окне 1024px уезжали
        // за правый край. Теперь они делят фактическую ширину раздела.
        int refreshW = 42;
        int addW = Math.Min(140, Math.Max(96, w / 6));
        int gap = 10;
        // 26 — тот же запас под скроллбар, что и в RowWidth: без него
        // поле ввода и кнопка обновления заканчивались на 26px правее
        // карточек и правый край раздела выглядел рваным
        int innerW = Math.Max(320, w - 26);
        int boxW = innerW - addW - refreshW - gap * 2;
        if (boxW < 220) boxW = Math.Max(180, innerW - addW - refreshW - gap * 2);

        // текстовые подписи раздела не должны вылезать за его ширину
        if (friendsMe != null) friendsMe.Width = innerW;
        if (friendsState != null) friendsState.Width = innerW;

        if (friendsAddBox != null)
        {
            friendsAddBox.Location = new Point(SecX, SecY + 84);
            friendsAddBox.Size = new Size(boxW, 42);
            // подсказка «ник друга — Enter» прижимается к правому краю поля.
            // Visible не трогаем: после Visible=false прозрачный Label на
            // кастомной панели может больше не нарисоваться — вместо этого
            // сжимаем его до 1px. На узком окне места на подсказку нет:
            // обрезанный текст только мешал бы вводу.
            int hintW = Math.Min(148, Math.Max(0, boxW - 470));
            bool showHint = hintW >= 120;
            if (friendsAddHint != null)
            {
                friendsAddHint.Visible = true;
                friendsAddHint.Location = new Point(boxW - (showHint ? hintW : 0) - 12, 13);
                friendsAddHint.Size = new Size(showHint ? hintW : 1, 16);
            }
            if (friendBox != null)
                friendBox.Width = Math.Max(120, boxW - 42 - 12 - (showHint ? hintW + 8 : 0));
        }
        if (friendAddBtn != null)
        {
            friendAddBtn.Location = new Point(SecX + boxW + gap, SecY + 84);
            friendAddBtn.Size = new Size(addW, 42);
        }
        if (friendsRefreshBtn != null)
            friendsRefreshBtn.Location = new Point(SecX + boxW + gap + addW + gap, SecY + 84);

        if (friendsList != null)
        {
            friendsList.Width = w;
            // 172, а не 140: строка состояния внизу больше не накрывает
            // последнюю карточку списка
            friendsList.Height = Math.Max(120, h - 172);
            foreach (Control c in friendsList.Controls) c.Width = RowWidth(friendsList);
        }
        if (friendsState != null) friendsState.Top = SecY + h - 26;

        LayoutTogetherTabs(w);
    }

    static int RowWidth(DarkFlowPanel p)
    {
        int wd = p.ClientSize.Width - 26;
        return wd < 280 ? 280 : wd;
    }

    void FriendsLoad()
    {
        if (DbgSocialDemo) { FriendsRender(); return; }
        if (friendsBusy) { friendsAgain = true; return; }
        if (!Luxe.Ready) { SocialLogin(); return; }
        friendsBusy = true;
        FriendState("Обновляю...", true);
        Thread t = new Thread((ThreadStart)delegate
        {
            List<LuxeFriend> list = null;
            string err = "";
            try { list = Luxe.Friends(); }
            catch { }
            if (list == null) err = Luxe.LastError;
            SocialPost(delegate
            {
                friendsBusy = false;
                if (list == null)
                {
                    FriendState("Не получилось получить список: " + err, false);
                    SocialState();
                    if (friendsAgain) { friendsAgain = false; FriendsLoad(); }
                    return;
                }
                friendsCache = list;
                FriendsRender();
                FriendsNote();
                if (friendsAgain) { friendsAgain = false; FriendsLoad(); }
            });
        });
        t.IsBackground = true;
        t.Start();
    }

    // Бейдж на пункте «Друзья» в сайдбаре: сколько друзей в лаунчере
    void FriendsBadge(int online, int total)
    {
        try
        {
            if (navFriends == null) return;
            int v = online > 0 ? online : 0;
            if (navFriends.Badge != v)
            {
                navFriends.Badge = v;
                navFriends.Invalidate();
            }
        }
        catch { }
    }

    void FriendState(string s, bool good)
    {
        try
        {
            if (friendsState == null) return;
            friendsState.Text = s;
            friendsState.ForeColor = good ? Ui.GrayDim : Ui.Red;
        }
        catch { }
    }

    void FriendsRender()
    {
        if (friendsList == null) return;
        if (friendsList.InvokeRequired) { friendsList.Invoke(new Action(FriendsRender)); return; }
        int savedY = friendsList.ScrollY;
        bool keep = friendsList.Controls.Count > 0;
        friendsList.Controls.Clear();
        if (friendsTab == 1) RequestsRender();
        else if (friendsTab == 2) TogetherRender();
        else FriendsRenderList();
        if (keep) friendsList.RestoreScroll(savedY);
        else friendsList.ResetScroll();
        FriendsNote();
    }

    void FriendsRenderList()
    {
        if (friendsCache == null || friendsCache.Count == 0)
        {
            FriendsHint(friendsCache == null
                ? "Список ещё не загрузился"
                : "Пока пусто. Впиши ник сверху и нажми «Добавить» — человек получит заявку и появится здесь после «Принять».");
            return;
        }
        foreach (LuxeFriend f in friendsCache) friendsList.Controls.Add(FriendRow(f));
    }

    void FriendsHint(string text)
    {
        Label l = new Label();
        l.Text = text;
        l.Font = Ui.Font(10);
        l.ForeColor = Ui.Gray;
        l.BackColor = Ui.Bg;
        l.AutoSize = true;
        l.Location = new Point(12, 12);
        friendsList.Controls.Add(l);
    }

    RoundedPanel FriendRow(LuxeFriend f)
    {
        LuxeFriend fr = f;
        RoundedPanel p = new RoundedPanel();
        p.Radius = 14;
        p.BackColor = Ui.Card;
        p.Border = Ui.Line;
        p.HoverBack = Ui.CardHover;
        p.HoverBorder = f.Online ? Ui.Green : Ui.Accent1;
        p.Animated = true;
        p.Size = new Size(RowWidth(friendsList), 62);
        p.Margin = new Padding(0, 0, 0, 10);

        Label dot = new Label();
        dot.Text = "●";
        dot.Font = Ui.Font(13);
        dot.ForeColor = f.Online ? Ui.Green : Ui.GrayDim;
        dot.BackColor = Color.Transparent;
        dot.AutoSize = false;
        dot.TextAlign = ContentAlignment.MiddleCenter;
        dot.Location = new Point(10, 0);
        dot.Size = new Size(34, 62);
        p.Controls.Add(dot);

        Label nm = new Label();
        nm.Text = Ui.Short(f.Name, 22);
        nm.Font = Ui.Font(11, FontStyle.Bold);
        nm.ForeColor = Ui.Txt;
        nm.BackColor = Color.Transparent;
        nm.AutoSize = false;
        nm.Location = new Point(48, 12);
        nm.Size = new Size(300, 22);
        p.Controls.Add(nm);

        Label st = new Label();
        st.Text = f.Online
            ? "в лаунчере"
            : (f.Last > 0 ? "не в лаунчере • был(а) " + Ago(f.Last) : "не в лаунчере");
        st.Font = Ui.Font(8.5f);
        st.ForeColor = f.Online ? Ui.Green : Ui.GrayDim;
        st.BackColor = Color.Transparent;
        st.AutoSize = false;
        st.Location = new Point(48, 34);
        st.Size = new Size(400, 18);
        p.Controls.Add(st);

        RoundedButton more = RoundedButton.IcoBtn(Ico.More, 34, 10);
        more.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        more.Location = new Point(p.Width - 46, 14);
        more.Click += delegate { FriendMenu(fr, more); };
        p.Controls.Add(more);

        p.Resize += delegate
        {
            try { more.Left = p.Width - 46; } catch { }
        };
        return p;
    }

    // «был(а) 5 мин назад» — время последнего heartbeat
    static string Ago(double unixSec)
    {
        double d = NowUnix() - unixSec;
        if (d < 0) d = 0;
        if (d < 90) return "только что";
        if (d < 3600) return ((int)(d / 60)) + " мин назад";
        if (d < 86400) return ((int)(d / 3600)) + " ч назад";
        return ((int)(d / 86400)) + " дн назад";
    }

    public static double NowUnix()
    {
        return DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
    }

    void FriendAdd()
    {
        string n = friendBox == null ? "" : friendBox.Text.Trim();
        if (n.Length < 3 || n.Length > 16)
        {
            FriendState("Ник должен быть от 3 до 16 символов", false);
            return;
        }
        string me = SignalMe();
        if (me.Length == 0)
        {
            FriendState("Сначала укажи ник на главной", false);
            return;
        }
        if (string.Equals(me, n, StringComparison.OrdinalIgnoreCase))
        {
            FriendState("Это твой ник", false);
            return;
        }
        if (IsFriend(n)) { FriendState("Этот игрок уже в друзьях", true); return; }
        if (SocialStore.HasOut(n)) { FriendState("Заявка уже отправлена: " + n, true); return; }

        // В сервисе заявок нет, поэтому сначала письмо, а в список друзей
        // человек попадёт только после ответа «Принять».
        string id = SocialStore.AddOut(n);
        friendBox.Text = "";
        FriendState("Отправляю заявку: " + n + "...", true);
        Thread t = new Thread((ThreadStart)delegate
        {
            string err = "";
            bool ok = false;
            try { ok = LuxeSignal.Publish(me, n, "friend_req", "", "", id, out err); } catch { }
            SocialPost(delegate
            {
                if (!ok)
                {
                    SocialStore.RemoveOutById(id);
                    FriendState("Не отправилось: " + err, false);
                }
                else FriendState("Заявка отправлена: " + n + " — ждём ответа", true);
                FriendsRender();
            });
        });
        t.IsBackground = true;
        t.Start();
    }

    void FriendRemove(string n)
    {
        FriendState("Убираю " + n + "...", true);
        string me = SignalMe();
        Thread t = new Thread((ThreadStart)delegate
        {
            bool ok = false;
            try { ok = Luxe.DelFriend(n); }
            catch { }
            string err = Luxe.LastError;
            // второй лаунчер тоже должен убрать меня из списка
            if (me.Length > 0)
            {
                string e2 = "";
                try { LuxeSignal.Publish(me, n, "friend_del", "", "", "", out e2); } catch { }
            }
            SocialPost(delegate
            {
                if (!ok) FriendState("Не убралось: " + err, false);
                else FriendState("Удалён из друзей: " + n, true);
                FriendsLoad();
            });
        });
        t.IsBackground = true;
        t.Start();
    }

    // ============================================================
    //  РАЗДЕЛ «ПЛАЩИ»
    // ============================================================

    void BuildCapesSection()
    {
        capesUi = new List<Control>();

        Panel bg = new Panel();
        bg.BackColor = Ui.Bg;
        bg.Location = new Point(SecX, SecY);
        bg.Size = new Size(888, 518);
        Controls.Add(bg); capesUi.Add(bg);

        PixelLabel head = new PixelLabel();
        head.Text = "Плащи";
        head.Font = Ui.Font(17, FontStyle.Bold);
        head.ForeColor = Ui.Txt;
        head.BackColor = Ui.Bg;
        head.AutoSize = false;
        head.Location = new Point(SecX, SecY);
        head.Size = new Size(400, 32);
        Controls.Add(head); capesUi.Add(head);

        Label sub = new Label();
        sub.Text = "Каталог и картинки плащей берутся из интернета. Плащ видят те, у кого стоит лаунчер — его показывает отдельный мод";
        sub.Font = Ui.Font(9.5f);
        sub.ForeColor = Ui.Gray;
        sub.BackColor = Ui.Bg;
        sub.AutoSize = false;
        sub.Location = new Point(SecX, SecY + 34);
        sub.Size = new Size(820, 20);
        sub.AutoEllipsis = true;
        Controls.Add(sub); capesUi.Add(sub);
        capesSub = sub;

        capesState = new Label();
        capesState.Text = "";
        capesState.Font = Ui.Font(9);
        capesState.ForeColor = Ui.GrayDim;
        capesState.BackColor = Ui.Bg;
        capesState.AutoSize = false;
        capesState.Location = new Point(SecX, SecY + 58);
        capesState.Size = new Size(700, 18);
        Controls.Add(capesState); capesUi.Add(capesState);

        // Фильтры каталога: популярные / новые / мои
        capesTabPop = CapeChip("Популярные", 0, 0, 84, 140);
        capesTabNew = CapeChip("Новые", 1, 148, 84, 96);
        capesTabMine = CapeChip("Мои", 2, 252, 84, 84);
        CapesPaintTabs();

        RoundedButton off = RoundedButton.Dark("Снять плащ", 12);
        off.Icon = Ico.Close;
        off.IconSize = 14;
        off.Font = Ui.Font(9, FontStyle.Bold);
        off.ForeColor = Ui.Txt;
        off.Location = new Point(SecX + 632, SecY + 84);
        off.Size = new Size(140, 42);
        off.Click += delegate { CapeOff(); };
        Controls.Add(off); capesUi.Add(off);
        capesOff = off;

        RoundedButton up = new RoundedButton();
        up.Text = "Свой плащ";
        up.Icon = Ico.Download;
        up.IconSize = 14;
        up.Radius = 12;
        up.Font = Ui.Font(9, FontStyle.Bold);
        up.Location = new Point(SecX + 452, SecY + 84);
        up.Size = new Size(170, 42);
        up.Click += delegate { CapeUpload(); };
        Controls.Add(up); capesUi.Add(up);
        capesUp = up;

        capesRefreshBtn = RoundedButton.IcoBtn(Ico.Refresh, 42, 12);
        capesRefreshBtn.Location = new Point(SecX + 784, SecY + 84);
        capesRefreshBtn.Click += delegate { CapesLoad(true); };
        Controls.Add(capesRefreshBtn); capesUi.Add(capesRefreshBtn);

        capesList = new DarkFlowPanel();
        capesList.WrapContents = true;
        capesList.FlowDirection = FlowDirection.LeftToRight;
        capesList.Location = new Point(SecX, SecY + 140);
        capesList.Size = new Size(864, 366);
        capesList.BackColor = Ui.Bg;
        Controls.Add(capesList); capesUi.Add(capesList);
    }

    // Кнопка-фильтр каталога плащей
    RoundedButton CapeChip(string text, int tab, int x, int y, int w)
    {
        RoundedButton b = new RoundedButton();
        b.Text = text;
        b.Radius = 6;
        b.Font = Ui.Font(9, FontStyle.Bold);
        b.Location = new Point(SecX + x, SecY + y);
        b.Size = new Size(w, 42);
        b.Click += delegate { CapesSelectTab(tab); };
        Controls.Add(b); capesUi.Add(b);
        return b;
    }

    // Выбранный фильтр подсвечен акцентом, остальные — тёмные
    void CapesPaintTab(RoundedButton b, bool on)
    {
        if (b == null) return;
        Color top = on ? Ui.Accent1 : Ui.SurfaceHi;
        Color bot = on ? Ui.Accent2 : Ui.SurfaceHi;
        b.Top = top;
        b.Bottom = bot;
        b.HoverTop = ControlPaint.Light(top, 0.12f);
        b.HoverBottom = ControlPaint.Light(bot, 0.08f);
        b.PressTop = ControlPaint.Dark(bot, 0.1f);
        b.PressBottom = ControlPaint.Dark(bot, 0.2f);
        b.ForeColor = on ? Color.White : Ui.Txt;
        b.Invalidate();
    }

    void CapesPaintTabs()
    {
        CapesPaintTab(capesTabPop, capesTab == 0);
        CapesPaintTab(capesTabNew, capesTab == 1);
        CapesPaintTab(capesTabMine, capesTab == 2);
    }

    void CapesSelectTab(int t)
    {
        if (capesTab == t) { CapesPaintTabs(); return; }
        capesTab = t;
        CapesPaintTabs();
        CapesRender(false);
    }

    // --shot-run ... capetab:N — выбрать фильтр перед снимком
    public void DebugCapeTab(int t)
    {
        if (t >= 0 && t <= 2) CapesSelectTab(t);
    }

    // Что показано в текущем фильтре — и какой плащ надет
    void CapesStatus()
    {
        if (capesCache == null) return;
        string tab = capesTab == 1 ? "Новые" : (capesTab == 2 ? "Мои плащи" : "Популярные");
        CapeState(tab + " • показано: " + capeViews.Count
                  + (capesCurrent == "" ? " • плащ не надет" : " • надет: " + CapeName(capesCurrent)), true);
    }

    void LayoutCapes(int w, int h)
    {
        if (capesUi == null || capesUi.Count < 4) return;
        capesUi[0].Size = new Size(w, h);
        // подпись и статус раздела раньше были шириной 820/700 и на окне
        // 1024px вылезали за правый край; тянем их по фактической ширине
        if (capesSub != null)
        {
            capesSub.Width = w;
            capesSub.AutoEllipsis = true;
        }
        if (capesState != null) capesState.Width = w;

        // ---- строка действий (Свой плащ / Снять / Обновить) ----
        // была прибита к x = 452…826 и на окне 1024px уезжала за край.
        // Теперь правый край считается от фактической ширины раздела,
        // а кнопки пропорционально сжимаются, если места мало.
        const int chipsRight = 336;   // фильтры «Популярные / Новые / Мои»
        const int gap = 10;
        const int refreshW = 42;
        int upW = 170, offW = 140;
        int avail = w - chipsRight - 16;
        if (avail < upW + offW + refreshW + gap * 2)
        {
            int flex = Math.Max(170, avail - refreshW - gap * 2);
            upW = Math.Max(84, flex * 170 / 310);
            offW = Math.Max(76, flex - upW);
        }
        int refreshX = w - refreshW;
        int offX = refreshX - gap - offW;
        int upX = offX - gap - upW;

        if (capesRefreshBtn != null)
            capesRefreshBtn.Location = new Point(SecX + refreshX, SecY + 84);
        if (capesOff != null)
        {
            capesOff.Location = new Point(SecX + offX, SecY + 84);
            capesOff.Size = new Size(offW, 42);
        }
        if (capesUp != null)
        {
            capesUp.Location = new Point(SecX + upX, SecY + 84);
            capesUp.Size = new Size(upW, 42);
        }

        if (capesList != null)
        {
            capesList.Width = w;
            capesList.Height = Math.Max(120, h - 140);
        }
    }

    void CapesLoad(bool force)
    {
        if (capesBusy) return;
        capesBusy = true;
        CapeState(force ? "Обновляю каталог..." : "Загружаю каталог...", true);
        if (force) Luxe.DropImageCache();
        Thread t = new Thread((ThreadStart)delegate
        {
            List<LuxeCape> list = null;
            string cur = "";
            string err = "";
            try
            {
                list = Luxe.Capes("popular");
                if (list != null && Luxe.Ready) Luxe.Current(out cur);
            }
            catch { }
            if (list == null) err = Luxe.LastError;
            SocialPost(delegate
            {
                capesBusy = false;
                if (list == null)
                {
                    CapeState("Каталог не загрузился: " + err, false);
                    return;
                }
                capesCache = list;
                capesCurrent = cur;
                CapesRender(force);
            });
        });
        t.IsBackground = true;
        t.Start();
    }

    string CapeName(string id)
    {
        if (capesCache != null)
            foreach (LuxeCape c in capesCache)
                if (c.Id == id) return c.Name;
        return id;
    }

    void CapeState(string s, bool good)
    {
        try
        {
            if (capesState == null) return;
            capesState.Text = s;
            capesState.ForeColor = good ? Ui.Gray : Ui.Red;
        }
        catch { }
    }

    void CapesRender(bool force)
    {
        if (capesList == null) return;
        if (capesList.InvokeRequired) { capesList.Invoke(new Action<bool>(CapesRender), force); return; }
        // Пересборка списка (например, когда догрузились картинки) не
        // должна возвращать пользователя в начало: запоминаем позицию
        // и восстанавливаем после раскладки.
        bool keep = capesList.Controls.Count > 0;
        int savedY = capesList.ScrollY;
        foreach (Control old in capesList.Controls) { try { old.Dispose(); } catch { } }
        capesList.Controls.Clear();
        capeViews = new Dictionary<string, CapeView>();
        capeImgSeq++;
        int seq = capeImgSeq;

        // Какой фильтр выбран — такой список и собираем
        List<LuxeCape> view = new List<LuxeCape>();
        if (capesCache != null)
        {
            foreach (LuxeCape c in capesCache)
            {
                if (capesTab == 2)
                {
                    if (c.Owner != "" && c.Owner == Luxe.Me) view.Add(c);
                }
                else if (c.Owner == "" || c.Owner == Luxe.Me)
                {
                    view.Add(c);
                }
            }
        }

        if (capesTab == 2)
        {
            view.Sort(delegate(LuxeCape a, LuxeCape b)
            {
                int r = b.Created.CompareTo(a.Created);
                if (r == 0) r = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                return r;
            });
            if (view.Count == 0)
            {
                Label l = new Label();
                l.Text = "Своих плащей пока нет.\r\n\r\n"
                       + "Нажми «Свой плащ» и выбери картинку PNG 64x32 —\r\n"
                       + "она сразу появится здесь и её увидят все, у кого стоит лаунчер.";
                l.Font = Ui.Font(10);
                l.ForeColor = Ui.Gray;
                l.BackColor = Ui.Bg;
                l.AutoSize = false;
                l.Location = new Point(12, 12);
                l.Size = new Size(760, 100);
                capesList.Controls.Add(l);
                capesList.ResetScroll();
                CapesStatus();
                return;
            }
        }
        else if (capesTab == 1)
        {
            view.Sort(delegate(LuxeCape a, LuxeCape b)
            {
                int r = b.Created.CompareTo(a.Created);
                if (r == 0) r = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                return r;
            });
        }
        else
        {
            view.Sort(delegate(LuxeCape a, LuxeCape b)
            {
                int r = b.Wears.CompareTo(a.Wears);
                if (r == 0) r = b.Installs.CompareTo(a.Installs);
                if (r == 0) r = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                return r;
            });
        }

        List<LuxeCape> needNet = new List<LuxeCape>();
        foreach (LuxeCape c in view)
        {
            RoundedPanel card = MakeCapeCard(c);
            capesList.Controls.Add(card);
            CapeView cv;
            if (capeViews.TryGetValue(c.Id, out cv) && cv != null)
            {
                Image cached = Luxe.CapeImage(c.Id, c.Url, force, false);
                if (cached != null) cv.Cape = cached;
                else needNet.Add(c);
            }
        }
        capesList.ResetScroll();
        if (keep) capesList.RestoreScroll(savedY);
        CapesStatus();
        if (needNet.Count > 0) CapeImagesLoad(seq, force, needNet);
    }

    // Картинки качаем по одной в фоне: список появляется сразу, картинки — по мере загрузки
    void CapeImagesLoad(int seq, bool force, List<LuxeCape> items)
    {
        Thread t = new Thread((ThreadStart)delegate
        {
            foreach (LuxeCape c in items)
            {
                if (seq != capeImgSeq) return;
                Image im = null;
                try { im = Luxe.CapeImage(c.Id, c.Url, force, true); }
                catch { }
                if (im == null) continue;
                LuxeCape cc = c;
                SocialPost(delegate
                {
                    if (seq != capeImgSeq) return;
                    CapeView v;
                    if (capeViews != null && capeViews.TryGetValue(cc.Id, out v) && v != null && !v.IsDisposed)
                    {
                        v.Cape = im;
                        v.Invalidate();
                    }
                });
            }
        });
        t.IsBackground = true;
        t.Start();
    }

    RoundedPanel MakeCapeCard(LuxeCape c)
    {
        LuxeCape cc = c;
        bool worn = (capesCurrent != "" && c.Id == capesCurrent);
        double nowSec = (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        bool fresh = c.Created > 0 && nowSec - c.Created < 14 * 86400;

        RoundedPanel p = new RoundedPanel();
        p.Radius = 16;
        p.BackColor = Ui.Card;
        p.Border = worn ? Ui.Accent1 : Ui.Line;
        p.HoverBack = Ui.CardHover;
        p.HoverBorder = Ui.Accent1;
        p.Animated = true;
        p.Size = new Size(268, 254);
        p.Margin = new Padding(0, 0, 12, 12);

        CapeView view = new CapeView();
        view.Location = new Point(14, 14);
        view.Size = new Size(240, 128);
        view.Worn = worn;
        view.New = fresh;
        p.Controls.Add(view);
        capeViews[c.Id] = view;

        Label ti = new Label();
        ti.Text = Ui.Short(c.Name, 22);
        ti.Font = Ui.Font(10.5f, FontStyle.Bold);
        ti.ForeColor = Ui.Txt;
        ti.BackColor = Color.Transparent;
        ti.AutoSize = false;
        ti.Location = new Point(16, 150);
        ti.Size = new Size(236, 22);
        p.Controls.Add(ti);

        Label au = new Label();
        au.Text = Ui.Short(c.Owner != "" ? "свой плащ"
                     : (string.IsNullOrEmpty(c.Author) ? "Luxe Visuals" : c.Author), 18);
        au.Font = Ui.Font(8);
        au.ForeColor = Ui.GrayDim;
        au.BackColor = Color.Transparent;
        au.AutoSize = false;
        au.Location = new Point(16, 172);
        au.Size = new Size(236, 16);
        p.Controls.Add(au);

        // Метаданные каталога: сколько человек уже носят этот плащ
        string stat = c.Owner != "" ? ""
                    : (c.Wears > 0 ? ("носят: " + c.Wears)
                    : (c.Installs > 0 ? ("надевали: " + c.Installs) : ""));
        if (stat != "")
        {
            Label st = new Label();
            st.Text = stat;
            st.Font = Ui.Font(8, FontStyle.Bold);
            st.ForeColor = c.Wears > 0 ? Ui.Accent1 : Ui.GrayDim;
            st.BackColor = Color.Transparent;
            st.AutoSize = false;
            st.TextAlign = ContentAlignment.MiddleRight;
            st.Location = new Point(126, 172);
            st.Size = new Size(126, 16);
            p.Controls.Add(st);
        }

        RoundedButton wear = new RoundedButton();
        wear.Text = worn ? "Снять" : "Надеть";
        wear.Icon = worn ? Ico.Close : Ico.Check;
        wear.IconSize = 15;
        wear.Radius = 11;
        wear.Font = Ui.Font(9.5f, FontStyle.Bold);
        wear.Location = new Point(16, 200);
        wear.Size = new Size(236, 38);
        wear.Click += delegate
        {
            if (worn) CapeOff();
            else CapeWear(cc);
        };
        p.Controls.Add(wear);

        p.Cursor = Cursors.Default;
        return p;
    }

    void CapeWear(LuxeCape c)
    {
        CapeState("Надеваю «" + c.Name + "»...", true);
        Thread t = new Thread((ThreadStart)delegate
        {
            bool ok = false;
            try { ok = Luxe.SetCape(c.Id); }
            catch { }
            string err = Luxe.LastError;
            SocialPost(delegate
            {
                if (!ok) CapeState("Не получилось: " + err, false);
                else CapeState("Надет: " + c.Name + ". В игре его покажет мод.", true);
                CapesLoad(false);
            });
        });
        t.IsBackground = true;
        t.Start();
    }

    void CapeOff()
    {
        CapeState("Снимаю плащ...", true);
        Thread t = new Thread((ThreadStart)delegate
        {
            bool ok = false;
            try { ok = Luxe.SetCape(""); }
            catch { }
            string err = Luxe.LastError;
            SocialPost(delegate
            {
                if (!ok) CapeState("Не получилось: " + err, false);
                else CapeState("Плащ снят", true);
                CapesLoad(false);
            });
        });
        t.IsBackground = true;
        t.Start();
    }

    // Свой плащ: нужен PNG 64x32 — такой же, как у официальных плащей
    void CapeUpload()
    {
        try
        {
            if (!Luxe.Ready) { CapeState("Сервис недоступен, подождите", false); return; }
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Свой плащ";
                d.Filter = "Плащ Minecraft (*.png)|*.png";
                if (d.ShowDialog(this) != DialogResult.OK) return;

                byte[] png = null;
                using (Image im = Image.FromFile(d.FileName))
                {
                    if (im.Width != 64 || im.Height != 32)
                    {
                        CapeState("Плащ должен быть ровно 64x32, а этот " + im.Width + "x" + im.Height, false);
                        return;
                    }
                    using (Bitmap b = new Bitmap(im))
                    using (MemoryStream ms = new MemoryStream())
                    {
                        b.Save(ms, ImageFormat.Png);
                        png = ms.ToArray();
                    }
                }
                string name = Path.GetFileNameWithoutExtension(d.FileName);
                CapeState("Заливаю «" + name + "»...", true);
                byte[] data = png;
                Thread t = new Thread((ThreadStart)delegate
                {
                    bool ok = false;
                    try { ok = Luxe.UploadCape(data, name); }
                    catch { }
                    string err = Luxe.LastError;
                    SocialPost(delegate
                    {
                        if (!ok) CapeState("Не залилось: " + err, false);
                        else CapeState("Плащ добавлен и сразу надет", true);
                        CapesLoad(true);
                    });
                });
                t.IsBackground = true;
                t.Start();
            }
        }
        catch (Exception ex)
        {
            CapeState("Не получилось: " + ex.Message, false);
        }
    }

    // Раскладка левого меню: пунктов стало больше, и на низком окне они
    // наезжали на «Аккаунты»/«Настройки». Ужимаем шаг под высоту окна.
    void LayoutNav()
    {
        try
        {
            if (navList.Count == 0) return;
            int top = 150;
            int bottom = ClientSize.Height - 168;      // выше кнопки «Аккаунты»
            int step = 46;
            // Шаг обязан укладывать ВЕСЬ список выше bottom. Раньше условие
            // сравнивало свободное место с count*38, а высота пункта была
            // step-4=42: при 680 последний пункт уходил на Y=518 и наезжал
            // на кнопки «Настройки»/«Аккаунты» (Y=528).
            int avail = bottom - top;
            int maxStep = avail > 0 ? avail / navList.Count : 0;
            if (maxStep < step) step = Math.Max(28, maxStep);
            for (int i = 0; i < navList.Count; i++)
            {
                navList[i].Height = Math.Max(24, step - 4);
                navList[i].Top = top + i * step;
            }
        }
        catch { }
    }
}

// ================================================================
//  ПРЕВЬЮ ПЛАЩА
// ================================================================

// Показывает не всю картинку 64x32, а ту её часть, которую игра
// натягивает на спину игрока: прямоугольник 10x16 в точке (1,1).
class CapeView : Control
{
    public Image Cape = null;
    public bool Worn = false;       // бейдж «НАДЕТ»
    public bool New = false;        // бейдж «НОВЫЙ»

    public CapeView()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Ui.Surface;
    }

    void Badge(Graphics g, string text, Rectangle r, Color fill, Color border, Color fore)
    {
        using (GraphicsPath bp = Ui.RoundRect(new RectangleF(r.X, r.Y, r.Width, r.Height), 4))
        {
            using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, bp);
            using (Pen p = new Pen(border, 2f))
            {
                p.Alignment = PenAlignment.Inset;
                g.DrawPath(p, bp);
            }
        }
        TextRenderer.DrawText(g, text, Ui.Font(7.5f, FontStyle.Bold), r, fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle box = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
        if (box.Width < 4 || box.Height < 4) return;

        GraphicsPath path = new GraphicsPath();
        int r = 12;
        path.AddArc(box.X, box.Y, r, r, 180, 90);
        path.AddArc(box.Right - r, box.Y, r, r, 270, 90);
        path.AddArc(box.Right - r, box.Bottom - r, r, r, 0, 90);
        path.AddArc(box.X, box.Bottom - r, r, r, 90, 90);
        path.CloseFigure();
        using (SolidBrush b = new SolidBrush(Ui.Surface))
            g.FillPath(b, path);

        if (Cape == null)
        {
            using (SolidBrush b = new SolidBrush(Ui.GrayDim))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString("нет картинки", Ui.Font(8), b, box, sf);
            }
        }
        else
        {
            try
            {
                int sw = Math.Min(10, Cape.Width);
                int sh = Math.Min(16, Cape.Height);
                Rectangle src = new Rectangle(1, 1, Math.Max(1, sw), Math.Max(1, sh));
                if (src.Right > Cape.Width || src.Bottom > Cape.Height)
                    src = new Rectangle(0, 0, Cape.Width, Cape.Height);

                float sc = Math.Min((box.Width - 16) / (float)src.Width, (box.Height - 16) / (float)src.Height);
                if (sc < 1) sc = 1;
                int dw = (int)(src.Width * sc);
                int dh = (int)(src.Height * sc);
                Rectangle dr = new Rectangle(box.X + (box.Width - dw) / 2, box.Y + (box.Height - dh) / 2, dw, dh);

                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(Cape, dr, src, GraphicsUnit.Pixel);

                using (Pen pen = new Pen(Ui.Line))
                    g.DrawRectangle(pen, dr.X - 1, dr.Y - 1, dr.Width + 1, dr.Height + 1);
            }
            catch { }
        }

        if (Worn) Badge(g, "НАДЕТ", new Rectangle(box.Right - 72, 8, 64, 20),
                        Ui.Accent1, Color.FromArgb(235, 0, 0, 0), Color.White);
        if (New) Badge(g, "НОВЫЙ", new Rectangle(8, 8, 64, 20),
                       Color.FromArgb(205, 22, 22, 28), Ui.Accent1, Ui.Accent1);
    }
}
