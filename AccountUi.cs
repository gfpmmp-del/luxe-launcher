// ============================================================
//  UI АККАУНТОВ ЛАУНЧЕРА
//
//  Карточка «Аккаунты»: вход, регистрация и состояние «вы вошли».
//  Аккаунт лаунчера — отдельный от Minecraft: ник используется для
//  друзей и плащей, пароль от 4 до 16 символов защищает профиль.
// ============================================================
using System;
using System.Drawing;
using System.Windows.Forms;

partial class LauncherForm
{
    TextBox accPass;
    TextBox accPass2;
    Label accGuestTitle;
    Label accGuestSub;
    Label accNickLbl;
    Label accPassLbl;
    Label accPass2Lbl;
    Label accErr;
    Label accWho;
    Label accWhoSub;
    Label accInfo;
    Label accSwitch;
    RoundedButton accTabLogin;
    RoundedButton accTabReg;
    RoundedButton accGo;
    RoundedButton accLogout;
    Panel accLicLine;
    Label accLicHead;
    MascotView accMascot;
    RoundedPanel accNickBox;
    RoundedPanel accPassBox;
    RoundedPanel accPass2Box;
    int accMode = 0;

    // отладка снимков: acc:0 — вход, acc:1 — регистрация, acc:2 — «вы вошли»
    public static int DbgAccount = -1;

    public void DebugOpenAccount(int mode)
    {
        if (mode == 2 && !LuxeAccounts.LoggedIn) mode = 0;
        ShowAccountCard(mode == 1 ? 1 : 0);
    }

    void BuildAccountCard(int cardW)
    {
        accCard = new RoundedPanel();
        accCard.Radius = 18;
        accCard.BackColor = Ui.Surface;
        accCard.Border = Ui.Border;
        accCard.Gradient = true;
        accCard.GradFrom = Color.FromArgb(28, 32, 66);
        accCard.GradTo = Color.FromArgb(8, 11, 26);
        accCard.Location = new Point((ClientSize.Width - cardW) / 2, 40);
        accCard.Size = new Size(cardW, 630);
        accCard.Visible = false;
        Controls.Add(accCard);
        CardHead(accCard, "Аккаунт лаунчера", cardW, delegate { HideOverlays(); });

        // ---- гостевая часть: маскот, заголовок и форма ----
        accMascot = new MascotView();
        accMascot.Location = new Point(24, 66);
        accMascot.Size = new Size(64, 64);
        accMascot.Walking = true;
        accCard.Controls.Add(accMascot);

        accGuestTitle = new Label();
        accGuestTitle.Text = "Заходи в Luxe";
        accGuestTitle.Font = Ui.Font(13, FontStyle.Bold);
        accGuestTitle.ForeColor = Ui.Txt;
        accGuestTitle.BackColor = Color.Transparent;
        accGuestTitle.AutoSize = false;
        accGuestTitle.Location = new Point(98, 72);
        accGuestTitle.Size = new Size(cardW - 130, 24);
        accCard.Controls.Add(accGuestTitle);

        accGuestSub = new Label();
        accGuestSub.Text = "Друзья, плащи и профиль лаунчера";
        accGuestSub.Font = Ui.Font(8.5f);
        accGuestSub.ForeColor = Ui.Gray;
        accGuestSub.BackColor = Color.Transparent;
        accGuestSub.AutoSize = false;
        accGuestSub.Location = new Point(98, 98);
        accGuestSub.Size = new Size(cardW - 130, 18);
        accCard.Controls.Add(accGuestSub);

        accTabLogin = AccountTab("Вход", 0);
        accTabLogin.Location = new Point(24, 140);
        accTabLogin.Size = new Size(cardW / 2 - 28, 36);
        accCard.Controls.Add(accTabLogin);

        accTabReg = AccountTab("Регистрация", 1);
        accTabReg.Location = new Point(cardW / 2 + 4, 140);
        accTabReg.Size = new Size(cardW / 2 - 28, 36);
        accCard.Controls.Add(accTabReg);

        accNickLbl = AccountLabel("Ник аккаунта", 188);
        accCard.Controls.Add(accNickLbl);
        accNick = new TextBox();
        accNick.Font = Ui.Font(11);
        accNick.BackColor = Ui.SurfaceHi;
        accNick.ForeColor = Ui.Txt;
        accNick.BorderStyle = BorderStyle.None;
        accNick.Location = new Point(12, 11);
        accNick.Size = new Size(cardW - 48 - 24, 22);
        RoundedPanel nickBox = FieldBox(cardW - 48, 44, 206);
        nickBox.Controls.Add(accNick);
        accCard.Controls.Add(nickBox);
        accNickBox = nickBox;

        accPassLbl = AccountLabel("Пароль", 258);
        accCard.Controls.Add(accPassLbl);
        accPass = AccountPassword();
        RoundedPanel passBox = FieldBox(cardW - 48, 44, 276);
        passBox.Controls.Add(accPass);
        accCard.Controls.Add(passBox);
        accPassBox = passBox;

        accPass2Lbl = AccountLabel("Повтори пароль", 328);
        accCard.Controls.Add(accPass2Lbl);
        accPass2 = AccountPassword();
        RoundedPanel pass2Box = FieldBox(cardW - 48, 44, 346);
        pass2Box.Controls.Add(accPass2);
        accCard.Controls.Add(pass2Box);
        accPass2Box = pass2Box;

        accErr = new Label();
        accErr.Text = "";
        accErr.Font = Ui.Font(8.5f);
        accErr.ForeColor = Ui.Red;
        accErr.BackColor = Color.Transparent;
        accErr.AutoSize = false;
        accErr.Location = new Point(24, 398);
        accErr.Size = new Size(cardW - 48, 34);
        accCard.Controls.Add(accErr);

        accGo = new RoundedButton();
        accGo.Text = "Войти";
        accGo.Radius = 12;
        accGo.Glow = true;
        accGo.Font = Ui.Font(10, FontStyle.Bold);
        accGo.Location = new Point(24, 434);
        accGo.Size = new Size(cardW - 48, 46);
        accGo.Click += delegate { AccountSubmit(); };
        accCard.Controls.Add(accGo);

        accSwitch = new Label();
        accSwitch.Text = "Нет аккаунта? Создать";
        accSwitch.Font = Ui.Font(8.5f, FontStyle.Bold);
        accSwitch.ForeColor = Ui.Accent1;
        accSwitch.BackColor = Color.Transparent;
        accSwitch.AutoSize = false;
        accSwitch.TextAlign = ContentAlignment.MiddleCenter;
        accSwitch.Cursor = Cursors.Hand;
        accSwitch.Location = new Point(24, 484);
        accSwitch.Size = new Size(cardW - 48, 24);
        accSwitch.Click += delegate { AccountSetMode(accMode == 0 ? 1 : 0); };
        accCard.Controls.Add(accSwitch);

        // ---- лицензия Microsoft: отдельный, но компактный блок ----
        accLicLine = new Panel();
        accLicLine.BackColor = Ui.Line;
        accLicLine.Location = new Point(24, 520);
        accLicLine.Size = new Size(cardW - 48, 1);
        accCard.Controls.Add(accLicLine);

        accLicHead = new Label();
        accLicHead.Text = "Minecraft-лицензия";
        accLicHead.Font = Ui.Font(8.5f);
        accLicHead.ForeColor = Ui.GrayDim;
        accLicHead.BackColor = Color.Transparent;
        accLicHead.AutoSize = false;
        accLicHead.Location = new Point(24, 530);
        accLicHead.Size = new Size(cardW - 48, 18);
        accCard.Controls.Add(accLicHead);

        accLic = new Label();
        accLic.Font = Ui.Font(9.5f, FontStyle.Bold);
        accLic.ForeColor = Ui.GrayDim;
        accLic.BackColor = Color.Transparent;
        accLic.AutoSize = false;
        accLic.Location = new Point(24, 548);
        accLic.Size = new Size(cardW - 48, 20);
        accCard.Controls.Add(accLic);

        accLicBtn = new RoundedButton();
        accLicBtn.Radius = 11;
        accLicBtn.Font = Ui.Font(9, FontStyle.Bold);
        accLicBtn.Location = new Point(24, 574);
        accLicBtn.Size = new Size(cardW - 48, 40);
        accLicBtn.Click += delegate { ShowAuth(); };
        accCard.Controls.Add(accLicBtn);

        // ---- состояние «вы вошли» ----
        accWho = new Label();
        accWho.Font = Ui.Font(17, FontStyle.Bold);
        accWho.ForeColor = Ui.Txt;
        accWho.BackColor = Color.Transparent;
        accWho.AutoSize = false;
        accWho.Location = new Point(120, 70);
        accWho.Size = new Size(cardW - 150, 28);
        accCard.Controls.Add(accWho);

        accWhoSub = new Label();
        accWhoSub.Text = "Аккаунт лаунчера • друзья и плащи";
        accWhoSub.Font = Ui.Font(8.5f);
        accWhoSub.ForeColor = Ui.Gray;
        accWhoSub.BackColor = Color.Transparent;
        accWhoSub.AutoSize = false;
        accWhoSub.Location = new Point(120, 100);
        accWhoSub.Size = new Size(cardW - 150, 18);
        accCard.Controls.Add(accWhoSub);

        accInfo = new Label();
        accInfo.Text = "Этот ник видят друзья. Пароль хранится только на этом компьютере в виде хеша.";
        accInfo.Font = Ui.Font(8.5f);
        accInfo.ForeColor = Ui.GrayDim;
        accInfo.BackColor = Color.Transparent;
        accInfo.AutoSize = false;
        accInfo.Location = new Point(24, 150);
        accInfo.Size = new Size(cardW - 48, 40);
        accCard.Controls.Add(accInfo);

        accLogout = new RoundedButton();
        accLogout.Text = "Выйти из аккаунта";
        accLogout.Radius = 12;
        accLogout.Font = Ui.Font(9.5f, FontStyle.Bold);
        accLogout.Location = new Point(24, 198);
        accLogout.Size = new Size(cardW - 48, 44);
        accLogout.Click += delegate { AccountLogout(); };
        accCard.Controls.Add(accLogout);

        AccountSetMode(0);
        AccountShowGuest(0);
    }

    RoundedButton AccountTab(string text, int mode)
    {
        RoundedButton b = new RoundedButton();
        b.Text = text;
        b.Radius = 9;
        b.Flat = true;
        b.Font = Ui.Font(9, FontStyle.Bold);
        b.Click += delegate { AccountSetMode(mode); };
        return b;
    }

    Label AccountLabel(string text, int y)
    {
        Label l = new Label();
        l.Text = text;
        l.Font = Ui.Font(8.5f);
        l.ForeColor = Ui.GrayDim;
        l.BackColor = Color.Transparent;
        l.AutoSize = false;
        l.Location = new Point(24, y);
        l.Size = new Size(accCard.Width - 48, 18);
        return l;
    }

    TextBox AccountPassword()
    {
        TextBox t = new TextBox();
        t.Font = Ui.Font(11);
        t.BackColor = Ui.SurfaceHi;
        t.ForeColor = Ui.Txt;
        t.BorderStyle = BorderStyle.None;
        t.UseSystemPasswordChar = true;
        t.Location = new Point(12, 11);
        t.Size = new Size(accCard.Width - 48 - 24, 22);
        return t;
    }

    void AccountTabPaint(RoundedButton b, bool on)
    {
        if (b == null) return;
        if (on)
        {
            b.Top = Ui.PrimeT;
            b.Bottom = Ui.PrimeB;
            b.HoverTop = Ui.Lerp(Ui.PrimeT, Color.White, 0.10f);
            b.HoverBottom = Ui.Lerp(Ui.PrimeB, Color.White, 0.08f);
            b.ForeColor = Color.White;
            b.BorderColor = Color.Empty;
        }
        else
        {
            b.Top = Ui.SurfaceHi;
            b.Bottom = Ui.Surface;
            b.HoverTop = Ui.CardHover;
            b.HoverBottom = Ui.SurfaceHi;
            b.ForeColor = Ui.Gray;
            b.BorderColor = Ui.Line;
            b.BorderW = 1;
        }
        b.Invalidate();
    }

    void AccountSetMode(int mode)
    {
        accMode = mode < 0 ? 0 : (mode > 1 ? 1 : mode);
        AccountTabPaint(accTabLogin, accMode == 0);
        AccountTabPaint(accTabReg, accMode == 1);
        bool reg = accMode == 1;
        accPass2.Visible = reg;
        accPass2Lbl.Visible = reg;
        if (accPass2Box != null) accPass2Box.Visible = reg;
        accGo.Text = reg ? "Создать аккаунт" : "Войти";
        accSwitch.Text = reg ? "Уже есть аккаунт? Войти" : "Нет аккаунта? Создать";
        accErr.Text = "";

        int errY = reg ? 398 : 330;
        int goY = reg ? 434 : 374;
        int swY = reg ? 484 : 424;
        int lineY = reg ? 520 : 460;
        accErr.Location = new Point(24, errY);
        accGo.Location = new Point(24, goY);
        accSwitch.Location = new Point(24, swY);
        accLicLine.Location = new Point(24, lineY);
        accLicHead.Location = new Point(24, lineY + 10);
        accLic.Location = new Point(24, lineY + 28);
        accLicBtn.Location = new Point(24, lineY + 54);
        accCard.Height = reg ? 630 : 570;
        PlaceAccountCard();
    }

    void PlaceAccountCard()
    {
        try
        {
            if (accCard == null) return;
            int x = (ClientSize.Width - accCard.Width) / 2;
            int y = (ClientSize.Height - accCard.Height) / 2;
            if (x < 12) x = 12;
            if (y < 12) y = 12;
            accCard.Location = new Point(x, y);
        }
        catch { }
    }

    void AccountShowGuest(int mode)
    {
        accGuestTitle.Visible = true;
        accGuestSub.Visible = true;
        accMascot.Visible = true;
        accMascot.Location = new Point(24, 66);
        accMascot.Size = new Size(64, 64);
        accMascot.Walking = true;
        accMascot.Happy = false;
        accTabLogin.Visible = true;
        accTabReg.Visible = true;
        accNickLbl.Visible = true;
        accPassLbl.Visible = true;
        accNick.Visible = true;
        accPass.Visible = true;
        if (accNickBox != null) accNickBox.Visible = true;
        if (accPassBox != null) accPassBox.Visible = true;
        accGo.Visible = true;
        accSwitch.Visible = true;
        accErr.Visible = true;
        accLicLine.Visible = true;
        accLicHead.Visible = true;
        accLic.Visible = true;
        accLicBtn.Visible = true;
        accWho.Visible = false;
        accWhoSub.Visible = false;
        accInfo.Visible = false;
        accLogout.Visible = false;
        accNick.Text = mode == 1 ? nick : "";
        accPass.Text = "";
        accPass2.Text = "";
        AccountSetMode(mode);
        UpdateLicUi();
    }

    void AccountShowLoggedIn()
    {
        accGuestTitle.Visible = false;
        accGuestSub.Visible = false;
        accTabLogin.Visible = false;
        accTabReg.Visible = false;
        accNickLbl.Visible = false;
        accPassLbl.Visible = false;
        accPass2Lbl.Visible = false;
        accNick.Visible = false;
        accPass.Visible = false;
        accPass2.Visible = false;
        if (accNickBox != null) accNickBox.Visible = false;
        if (accPassBox != null) accPassBox.Visible = false;
        if (accPass2Box != null) accPass2Box.Visible = false;
        accGo.Visible = false;
        accSwitch.Visible = false;
        accErr.Visible = false;
        accWho.Visible = true;
        accWhoSub.Visible = true;
        accInfo.Visible = true;
        accLogout.Visible = true;
        accLicLine.Visible = true;
        accLicHead.Visible = true;
        accLic.Visible = true;
        accLicBtn.Visible = true;

        accMascot.Visible = true;
        accMascot.Location = new Point(28, 66);
        accMascot.Size = new Size(78, 78);
        accMascot.Walking = false;
        accMascot.Happy = true;
        accWho.Text = LuxeAccounts.Nick;
        accLicLine.Location = new Point(24, 260);
        accLicHead.Location = new Point(24, 270);
        accLic.Location = new Point(24, 288);
        accLicBtn.Location = new Point(24, 314);
        accCard.Height = 370;
        PlaceAccountCard();
        UpdateLicUi();
    }

    void ShowAccountCard(int mode)
    {
        if (accCard == null) return;
        if (LuxeAccounts.LoggedIn) AccountShowLoggedIn();
        else AccountShowGuest(mode);
        dimPanel.Visible = true;
        dimPanel.BringToFront();
        accCard.Visible = true;
        accCard.BringToFront();
        if (!LuxeAccounts.LoggedIn)
        {
            if (accNick.Visible) accNick.Focus();
        }
    }

    // Старый вызов из сайдбара: открыть карточку на вкладке «Вход».
    void ShowAccount()
    {
        ShowAccountCard(0);
    }

    void AccountSubmit()
    {
        string n = accNick.Text.Trim();
        string p = accPass.Text;
        if (accMode == 1 && p != accPass2.Text)
        {
            accErr.Text = "Пароли не совпадают";
            return;
        }
        accErr.Text = "";
        MascotLoading.Begin(accMode == 1 ? "Создаю аккаунт" : "Проверяю аккаунт");
        string err = "";
        bool ok;
        if (accMode == 1) ok = LuxeAccounts.Register(n, p, out err);
        else ok = LuxeAccounts.Login(n, p, out err);
        if (!ok)
        {
            accErr.Text = err;
            MascotLoading.Fail(err);
            return;
        }

        ApplyAccountIdentity(LuxeAccounts.Nick);
        MascotLoading.End(accMode == 1 ? "Аккаунт создан" : "С возвращением, " + LuxeAccounts.Nick);
        HideOverlays();
        RefreshProfile();
        UpdateLicUi();
        SetStatus((accMode == 1 ? "Аккаунт создан: " : "Вход выполнен: ") + LuxeAccounts.Nick, true);
        // друзья и плащи привязаны к нику аккаунта — перезаходим в сервис
        try { FriendsCacheClear(); } catch { }
        try { SocialRelogin(); } catch { }
        if (view == ViewFriends)
        {
            try { FriendsRender(); } catch { }
        }
    }

    void AccountLogout()
    {
        LuxeAccounts.Logout();
        try { Luxe.Logout(); } catch { }
        try { FriendsCacheClear(); } catch { }
        RefreshProfile();
        UpdateLicUi();
        SetStatus("Вы вышли из аккаунта лаунчера", true);
        try { SocialState(); } catch { }
        if (view == ViewFriends)
        {
            try { FriendsRender(); } catch { }
        }
        AccountShowGuest(0);
        PlaceAccountCard();
        accCard.BringToFront();
    }

    void FriendsCacheClear()
    {
        try { friendsCache = null; } catch { }
        try { FriendsBadge(0, 0); } catch { }
    }

    void ApplyAccountIdentity(string n)
    {
        nick = n;
        try { Mc.SaveConfig(nick, curVer(), ram, closeOnLaunch); } catch { }
        try
        {
            PlayerSkin.Init((Auth.LoggedIn && Auth.Name.Length > 0) ? Auth.Name : nick);
        }
        catch { }
        try
        {
            if (banner != null) { banner.Tag = nick; banner.Invalidate(); }
        }
        catch { }
        RefreshProfile();
    }
}
