// ============================================================
//  АККАУНТЫ LUXE LAUNCHER
//
//  Это отдельные аккаунты лаунчера, не Minecraft. Они нужны для
//  друзей, плащей и профиля внутри лаунчера: ник аккаунта — это
//  сетевой идентификатор, а пароль защищает локальный профиль.
//
//  Хранение полностью локальное:
//    %APPDATA%\Luxe Launcher\accounts.json — список аккаунтов
//    %APPDATA%\Luxe Launcher\session.txt   — последний вход
//
//  Пароль не хранится: только соль и PBKDF2-SHA1 (60 000 итераций).
//  Формат файла переживает перезапуск и обновление лаунчера.
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

// Публичный класс: JavaScriptSerializer не умеет сериализовать
// закрытые вложенные типы на старом .NET 4.0.
public class LuxeAccount
{
    public string nick = "";
    public string salt = "";
    public string hash = "";
    public int iter = 60000;
    public string created = "";
}

static class LuxeAccounts
{
    const int MinNick = 3;
    const int MaxNick = 16;
    const int MinPass = 4;
    const int MaxPass = 16;
    const int Iterations = 60000;

    static readonly object Sync = new object();
    static List<LuxeAccount> list;
    static string nick = "";
    static string lastError = "";

    public static string LastError { get { return lastError; } }
    public static bool LoggedIn { get { return nick.Length > 0; } }
    public static string Nick { get { return nick; } }

    static string AccountsPath
    {
        get { return Path.Combine(Cfg.DataDir, "accounts.json"); }
    }

    static string SessionPath
    {
        get { return Path.Combine(Cfg.DataDir, "session.txt"); }
    }

    // ---------- запуск ----------

    public static void Init()
    {
        lock (Sync)
        {
            Load();
            // сессия хранит только ник: пароль повторно не спрашиваем,
            // пока игрок сам не выйдет из аккаунта
            try
            {
                if (File.Exists(SessionPath))
                {
                    string s = File.ReadAllText(SessionPath, Encoding.UTF8).Trim();
                    if (Find(s) != null) nick = Canonical(s);
                }
            }
            catch { }
        }
    }

    public static bool HasAccounts
    {
        get
        {
            lock (Sync) { Load(); return list.Count > 0; }
        }
    }

    public static int Count
    {
        get
        {
            lock (Sync) { Load(); return list.Count; }
        }
    }

    // ---------- регистрация / вход ----------

    public static bool Register(string n, string pass, out string err)
    {
        err = "";
        n = (n ?? "").Trim();
        pass = pass ?? "";
        if (!ValidNick(n)) { err = "Ник: 3–16 символов, латиница, цифры и _"; return false; }
        if (!ValidPass(pass)) { err = "Пароль: от 4 до 16 символов"; return false; }

        lock (Sync)
        {
            Load();
            if (Find(n) != null) { err = "Такой аккаунт уже есть — войди в него"; return false; }

            byte[] salt = new byte[16];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
                rng.GetBytes(salt);

            LuxeAccount a = new LuxeAccount();
            a.nick = n;
            a.salt = Convert.ToBase64String(salt);
            a.iter = Iterations;
            a.hash = Convert.ToBase64String(Hash(pass, salt, Iterations));
            a.created = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

            list.Add(a);
            if (!Save()) { err = "Не удалось сохранить аккаунт на диск"; return false; }
            nick = n;
            SaveSession();
            lastError = "";
            return true;
        }
    }

    public static bool Login(string n, string pass, out string err)
    {
        err = "";
        n = (n ?? "").Trim();
        pass = pass ?? "";
        if (n.Length == 0) { err = "Введи ник аккаунта"; return false; }
        if (pass.Length == 0) { err = "Введи пароль"; return false; }

        lock (Sync)
        {
            Load();
            LuxeAccount a = Find(n);
            if (a == null) { err = "Аккаунт не найден — создай новый"; return false; }
            byte[] salt;
            byte[] want;
            try
            {
                salt = Convert.FromBase64String(a.salt);
                want = Convert.FromBase64String(a.hash);
            }
            catch { err = "Файл аккаунта повреждён"; return false; }

            byte[] got = Hash(pass, salt, a.iter <= 0 ? Iterations : a.iter);
            if (!SlowEquals(got, want)) { err = "Неверный пароль"; return false; }

            nick = a.nick;
            SaveSession();
            lastError = "";
            return true;
        }
    }

    public static void Logout()
    {
        lock (Sync)
        {
            nick = "";
            try { if (File.Exists(SessionPath)) File.Delete(SessionPath); } catch { }
        }
    }

    // Удалить аккаунт вместе с сессией. Сейчас UI этого не делает,
    // но метод нужен, чтобы «выйти и забыть» можно было добавить
    // без переделки хранилища.
    public static bool Delete(string n, out string err)
    {
        err = "";
        n = (n ?? "").Trim();
        lock (Sync)
        {
            Load();
            LuxeAccount a = Find(n);
            if (a == null) { err = "Аккаунт не найден"; return false; }
            list.Remove(a);
            if (!Save()) { err = "Не удалось сохранить список аккаунтов"; return false; }
            if (string.Equals(nick, n, StringComparison.OrdinalIgnoreCase)) Logout();
            return true;
        }
    }

    // ---------- проверки ----------

    public static bool ValidNick(string n)
    {
        if (n == null) return false;
        n = n.Trim();
        if (n.Length < MinNick || n.Length > MaxNick) return false;
        foreach (char c in n)
        {
            bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                      || (c >= '0' && c <= '9') || c == '_';
            if (!ok) return false;
        }
        return true;
    }

    public static bool ValidPass(string p)
    {
        if (p == null) return false;
        return p.Length >= MinPass && p.Length <= MaxPass;
    }

    // ---------- файлы ----------

    static void Load()
    {
        if (list != null) return;
        list = new List<LuxeAccount>();
        try
        {
            if (!File.Exists(AccountsPath)) return;
            string json = File.ReadAllText(AccountsPath, Encoding.UTF8);
            if (string.IsNullOrEmpty(json)) return;
            List<LuxeAccount> read =
                new JavaScriptSerializer().Deserialize<List<LuxeAccount>>(json);
            if (read != null)
            {
                foreach (LuxeAccount a in read)
                {
                    if (a == null || string.IsNullOrEmpty(a.nick) || string.IsNullOrEmpty(a.hash))
                        continue;
                    list.Add(a);
                }
            }
        }
        catch { }
    }

    static bool Save()
    {
        try
        {
            string json = new JavaScriptSerializer().Serialize(list);
            string tmp = AccountsPath + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            try
            {
                if (File.Exists(AccountsPath)) File.Delete(AccountsPath);
                File.Move(tmp, AccountsPath);
            }
            catch
            {
                // антивирус/индексатор могли придержать файл — пробуем копию
                File.Copy(tmp, AccountsPath, true);
                try { File.Delete(tmp); } catch { }
            }
            return true;
        }
        catch { return false; }
    }

    static void SaveSession()
    {
        try
        {
            Directory.CreateDirectory(Cfg.DataDir);
            File.WriteAllText(SessionPath, nick, new UTF8Encoding(false));
        }
        catch { }
    }

    static LuxeAccount Find(string n)
    {
        if (list == null || string.IsNullOrEmpty(n)) return null;
        foreach (LuxeAccount a in list)
            if (string.Equals(a.nick, n, StringComparison.OrdinalIgnoreCase)) return a;
        return null;
    }

    static string Canonical(string n)
    {
        LuxeAccount a = Find(n);
        return a == null ? "" : a.nick;
    }

    static byte[] Hash(string pass, byte[] salt, int iter)
    {
        using (Rfc2898DeriveBytes k = new Rfc2898DeriveBytes(pass, salt, iter))
            return k.GetBytes(32);
    }

    // Сравнение без раннего выхода: не даёт угадывать пароль по времени ответа.
    static bool SlowEquals(byte[] a, byte[] b)
    {
        if (a == null || b == null) return false;
        int diff = a.Length ^ b.Length;
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
