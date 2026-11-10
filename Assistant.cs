// ============================================================
//  Luxe Launcher - встроенный помощник
//
//  Это настоящая языковая модель: запрос уходит по OpenAI-
//  совместимому протоколу (AiClient.cs), поэтому помощник отвечает
//  почти на любой вопрос, а не только на заготовленные.
//
//  Действия лаунчера (скин, плащ, сборка, настройки) выполняются
//  локально: модель объясняет словами, кнопки делают дело. Если
//  сети нет — остаётся офлайн-режим на правилах.
// ============================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

partial class LauncherForm
{
    RoundedPanel aiCard;
    DarkFlowPanel aiChat;
    TextBox aiInput;
    Label aiHint;
    bool aiGreeted = false;
    // история диалога для модели: роль -> текст
    List<KeyValuePair<string, string>> aiHistory = new List<KeyValuePair<string, string>>();
    RoundedPanel aiTyping;
    int aiGen = 0;
    // Только для отладки: подождать ответа модели перед снимком экрана
    public static int DbgShotWait = 0;
    // Только для отладки: нажать кнопку-действие в чате по её подписи
    public static string DbgAiClick = null;
    public static bool DbgAiHide = false;
    public static bool DbgAiShowSetup = false;
    // Только для отладки: сразу нажать «Проверить обновления» на вкладке
    public static bool DbgUpdCheck = false;

    // Только для отладки: убрать оверлеи перед снимком
    public void DebugHideOverlays() { HideOverlays(); }

    // Только для отладки: открыть настройки модели помощника
    public void DebugAiSetup() { ShowAi(); ShowAiSetup(); }

    const int AiW = 470;
    const int AiH = 610;

    // ---------- каркас ----------

    void ShowAi()
    {
        if (aiCard == null) BuildAssistant();
        if (aiCard == null) return;
        PlaceAiCard();
        if (!aiGreeted)
        {
            aiGreeted = true;
            AiGreeting();
        }
        dimPanel.Visible = true;
        dimPanel.BringToFront();
        aiCard.Visible = true;
        aiCard.BringToFront();
        try { aiInput.Focus(); } catch { }
    }

    void PlaceAiCard()
    {
        int x = ClientSize.Width - AiW - 24;
        int y = (ClientSize.Height - AiH) / 2;
        if (x < 8) x = 8;
        if (y < 8) y = 8;
        aiCard.Location = new Point(x, y);
    }

    void BuildAssistant()
    {
        aiCard = new RoundedPanel();
        aiCard.Radius = 16;
        aiCard.BackColor = Ui.Surface;
        aiCard.Border = Ui.Line;
        aiCard.Size = new Size(AiW, AiH);
        aiCard.Visible = false;
        Controls.Add(aiCard);
        CardHead(aiCard, "Помощник", AiW, delegate { HideOverlays(); });

        // шестерёнка: какой моделью отвечаем (свой ключ или локальная)
        RoundedButton gear = RoundedButton.IcoBtn(Ico.Gear, 30, 8);
        gear.Location = new Point(AiW - 24 - 30 - 34, 20);
        gear.Click += delegate { ShowAiSetup(); };
        aiCard.Controls.Add(gear);

        Label sub = new Label();
        sub.Text = "скины · плащи · настройки · сборки";
        sub.Font = Ui.Font(8);
        sub.ForeColor = Ui.GrayDim;
        sub.BackColor = Color.Transparent;
        sub.AutoSize = false;
        sub.Location = new Point(24, 46);
        sub.Size = new Size(AiW - 90, 14);
        aiCard.Controls.Add(sub);

        // быстрые запросы
        string[] chips = new string[] { "Скин", "Плащ", "Сборка", "Настройки" };
        int cw = (AiW - 32 - 3 * 6) / 4;
        for (int i = 0; i < chips.Length; i++)
        {
            string q = chips[i];
            RoundedButton b = RoundedButton.Dark(chips[i], 8);
            b.Font = Ui.Font(8.5f, FontStyle.Bold);
            b.BorderColor = Ui.Line;
            b.Size = new Size(cw, 30);
            b.Location = new Point(16 + i * (cw + 6), 70);
            b.Click += delegate { AiSend(q); };
            aiCard.Controls.Add(b);
        }

        aiChat = new DarkFlowPanel();
        aiChat.BackColor = Ui.Bg;
        aiChat.Location = new Point(16, 110);
        aiChat.Size = new Size(AiW - 32, 378);
        aiChat.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        aiCard.Controls.Add(aiChat);

        RoundedPanel inputBox = new RoundedPanel();
        inputBox.Radius = 10;
        inputBox.BackColor = Ui.SurfaceHi;
        inputBox.Border = Ui.Line;
        inputBox.HoverBorder = Ui.Accent1;
        inputBox.Animated = true;
        inputBox.Glass = false;
        inputBox.Location = new Point(16, AiH - 112);
        inputBox.Size = new Size(AiW - 32 - 96, 42);
        inputBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        aiCard.Controls.Add(inputBox);

        aiInput = new TextBox();
        aiInput.Font = Ui.Font(9.5f);
        aiInput.BackColor = Ui.SurfaceHi;
        aiInput.ForeColor = Ui.Txt;
        aiInput.BorderStyle = BorderStyle.None;
        aiInput.Location = new Point(12, 11);
        aiInput.Size = new Size(inputBox.Width - 24, 20);
        aiInput.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        aiInput.KeyDown += delegate(object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                AiSend(aiInput.Text);
            }
        };
        inputBox.Controls.Add(aiInput);

        RoundedButton send = new RoundedButton();
        send.Text = "Отправить";
        send.Radius = 10;
        send.Font = Ui.Font(8.5f, FontStyle.Bold);
        send.Location = new Point(AiW - 16 - 90, AiH - 112);
        send.Size = new Size(90, 42);
        send.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        send.Click += delegate { AiSend(aiInput.Text); };
        aiCard.Controls.Add(send);

        aiHint = new Label();
        aiHint.Text = "Модель: " + AiConfig.Model + " — отвечаю на любые вопросы";
        aiHint.Font = Ui.Font(7.5f);
        aiHint.ForeColor = Ui.GrayDim;
        aiHint.BackColor = Color.Transparent;
        aiHint.AutoSize = false;
        aiHint.Location = new Point(16, AiH - 62);
        aiHint.Size = new Size(AiW - 32, 16);
        aiHint.TextAlign = ContentAlignment.MiddleLeft;
        aiHint.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        aiCard.Controls.Add(aiHint);

        Label foot = new Label();
        foot.Text = "Enter — отправить · Esc — закрыть";
        foot.Font = Ui.Font(7.5f);
        foot.ForeColor = Ui.GrayDim;
        foot.BackColor = Color.Transparent;
        foot.AutoSize = false;
        foot.Location = new Point(16, AiH - 42);
        foot.Size = new Size(AiW - 32, 16);
        foot.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        aiCard.Controls.Add(foot);
    }

    // ---------- сообщения ----------

    void AiStatus(string s)
    {
        try { if (aiHint != null) aiHint.Text = s; } catch { }
    }

    void AiSend(string text)
    {
        string q = (text ?? "").Trim();
        if (q.Length == 0) return;
        AiBubble(q, true, null, null);
        try { aiInput.Text = ""; } catch { }
        AiStatus("Думаю...");
        if (aiHistory.Count > 24) aiHistory.RemoveRange(0, aiHistory.Count - 24);
        aiHistory.Add(new KeyValuePair<string, string>("user", q));
        AiAnswer(q);
    }

    // Пузырь сообщения: текст + кнопки-действия под ним
    RoundedPanel AiBubble(string text, bool mine, string[] labels, Action[] calls)
    {
        if (aiChat == null) return null;
        int chatW = aiChat.ClientSize.Width;
        if (chatW < 120) chatW = AiW - 32;
        Font f = Ui.Font(9.5f);
        int maxW = chatW - 96;
        if (maxW < 160) maxW = 160;
        TextFormatFlags fl = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
        Size sz = TextRenderer.MeasureText(text, f, new Size(maxW, int.MaxValue), fl);
        int tw = Math.Min(maxW, Math.Max(60, sz.Width + 2));
        int th = Math.Max(18, sz.Height);

        const int padX = 12, padY = 9, btnH = 28, btnGap = 6;
        int acts = (labels == null) ? 0 : labels.Length;
        int bubbleW = tw + padX * 2;
        int bubbleH = padY * 2 + th;
        if (acts > 0) bubbleH += acts * btnH + (acts - 1) * btnGap + 10;

        RoundedPanel b = new RoundedPanel();
        b.Radius = 12;
        b.BorderW = 0;
        b.Glass = false;
        b.BackColor = mine ? Color.FromArgb(46, 40, 86) : Ui.SurfaceHi;
        b.Size = new Size(bubbleW, bubbleH);
        int left = mine ? Math.Max(0, chatW - bubbleW - 24) : 0;
        b.Margin = new Padding(left, 4, 4, 4);

        Label t = new Label();
        t.Text = text;
        t.Font = f;
        t.ForeColor = mine ? Color.White : Ui.Txt;
        t.BackColor = Color.Transparent;
        t.AutoSize = false;
        t.Location = new Point(padX, padY);
        t.Size = new Size(tw, th);
        b.Controls.Add(t);

        for (int i = 0; i < acts; i++)
        {
            string lbl = labels[i];
            Action act = (calls != null && i < calls.Length) ? calls[i] : null;
            RoundedButton btn = new RoundedButton();
            btn.Text = lbl;
            btn.Radius = 8;
            btn.Font = Ui.Font(8.5f, FontStyle.Bold);
            btn.FitText = true;
            btn.Location = new Point(padX, padY + th + 8 + i * (btnH + btnGap));
            btn.Size = new Size(bubbleW - padX * 2, btnH);
            if (act != null)
                btn.Click += delegate { AiBubble(lbl, true, null, null); act(); };
            b.Controls.Add(btn);
        }

        aiChat.Controls.Add(b);
        AiToBottom();
        return b;
    }

    // «Печатает...» — живёт, пока модель думает
    void AiShowTyping()
    {
        AiHideTyping();
        aiTyping = AiBubble("Печатает...", false, null, null);
    }

    void AiHideTyping()
    {
        if (aiTyping == null) return;
        try
        {
            aiChat.Controls.Remove(aiTyping);
            aiTyping.Dispose();
        }
        catch { }
        aiTyping = null;
    }

    // ---------- настоящая модель ----------

    string AiSystemPrompt()
    {
        string vers = "";
        try
        {
            List<string> l = Mc.GetInstalledVersions();
            vers = l.Count == 0 ? "не установлены" : string.Join(", ", l.ToArray());
        }
        catch { }
        string java = string.IsNullOrEmpty(javaExe) ? "не найдена" : javaExe;
        return
            "Ты — встроенный помощник лаунчера Luxe Launcher для Minecraft (Windows, C# WinForms). " +
            "Отвечай по-русски, коротко и по делу: 2–6 предложений, без markdown-разметки, " +
            "без длинных списков. Если вопрос не про лаунчер и не про Minecraft — всё равно ответь " +
            "по существу, ты универсальный помощник.\n" +
            "Ты умеешь искать в интернете: скин по нику игрока или популярный (mc-heads), плащи в " +
            "каталоге Luxe по названию, сборки на Modrinth по названию — и сразу применяешь найденное. " +
            "Так и говори: «скажи название — найду и поставлю».\n" +
            "Контекст прямо сейчас: игрок «" + nick + "», версия Minecraft «" + curVer() + "», " +
            "память " + ram + " ГБ, вход Microsoft: " + (Auth.LoggedIn ? "выполнен" : "нет") + ", " +
            "Java: " + java + ", установленные версии: " + vers + ".\n" +
            "В лаунчере есть разделы: Главная, Новости, Моды, Скины, Друзья, Плащи; настройки — " +
            "шесть вкладок (Игра, Java, Лаунчер, Аккаунт, Обновления, О программе). " +
            "Не выдумывай кнопки и пути, которых нет. Если советуешь действие — назови раздел, " +
            "где это сделать.";
    }

    void AiAskLlm(string q)
    {
        AiConfig.Ensure();
        int gen = ++aiGen;
        AiShowTyping();
        AiStatus("Модель думает...");

        List<KeyValuePair<string, string>> hist = new List<KeyValuePair<string, string>>(aiHistory);
        string sys = AiSystemPrompt();
        Thread t = new Thread((ThreadStart)delegate
        {
            string answer = null;
            string err = null;
            try
            {
                answer = AiClient.Ask(hist, sys);
                if (answer == null) err = AiClient.LastError;
            }
            catch (Exception ex) { err = ex.Message; }

            SocialPost(delegate
            {
                if (gen != aiGen) return;      // ответ устарел (задали новый вопрос)
                AiHideTyping();
                if (answer != null && answer.Length > 0)
                {
                    aiHistory.Add(new KeyValuePair<string, string>("assistant", answer));
                    AiBubble(answer, false, null, null);
                    AiStatus("Модель: " + AiConfig.Model);
                }
                else
                {
                    AiStatus("Модель не ответила");
                    AiBubble("Не смог получить ответ от модели" +
                             (string.IsNullOrEmpty(err) ? "." : ": " + err) +
                             "\n\nПроверь интернет или модель в настройках (шестерёнка вверху). " +
                             "Пока работаю по правилам лаунчера — спроси про скин, плащ, память, Java или сборку.",
                        false,
                        new string[] { "Настройки модели", "Что умеешь?" },
                        new Action[] {
                            delegate { ShowAiSetup(); },
                            delegate { AiSend("что ты умеешь"); }
                        });
                }
            });
        });
        t.IsBackground = true;
        t.Start();
    }

    void AiToBottom()
    {
        // FlowLayoutPanel раскладывает детей на следующем проходе цикла
        // сообщений, поэтому одного RestoreScroll мало: первый вызов
        // видит старую высоту, и новый пузырь остаётся под полем ввода.
        try { aiChat.PerformLayout(); } catch { }
        try { aiChat.RestoreScroll(999999); } catch { }
        if (aiScroll == null)
        {
            aiScroll = new System.Windows.Forms.Timer();
            aiScroll.Interval = 60;
            aiScroll.Tick += delegate
            {
                aiScroll.Stop();
                try { aiChat.PerformLayout(); } catch { }
                try { aiChat.RestoreScroll(999999); } catch { }
            };
        }
        aiScroll.Stop();
        aiScroll.Start();
    }

    System.Windows.Forms.Timer aiScroll;

    // ---------- приветствие ----------

    // --shot-run ... ai[:запрос] — открыть чат (и сразу задать вопрос)
    public void DebugOpenAi(string query)
    {
        ShowAi();
        if (!string.IsNullOrEmpty(query)) AiSend(query);
    }

    // --shot-run ... aiclick:Подпись — нажать кнопку под ответом помощника
    public void DebugAiClick(string label)
    {
        if (aiChat == null || string.IsNullOrEmpty(label)) return;
        RoundedButton b = FindBtn(aiChat, label.Trim());
        if (b != null) { try { b.PerformClick(); } catch { } }
    }

    RoundedButton FindBtn(Control root, string text)
    {
        foreach (Control c in root.Controls)
        {
            RoundedButton b = c as RoundedButton;
            if (b != null && b.Text != null && b.Text.Trim() == text) return b;
            RoundedButton inChild = FindBtn(c, text);
            if (inChild != null) return inChild;
        }
        return null;
    }

    void AiGreeting()
    {
        AiBubble("Привет! Я помощник Luxe: работаю на языковой модели и умею искать в интернете.\n\n" +
                 "• «популярный скин» или «скин как у Dream» — найду и сразу надену\n" +
                 "• «плащ дракона» — подберу в каталоге и надену\n" +
                 "• «поставь better mc» — скачаю сборку с Modrinth и установлю\n" +
                 "• любой другой вопрос — просто спроси словами",
            false,
            new string[] { "Популярный скин", "Плащ дракона", "Better MC", "Что умеешь?" },
            new Action[] {
                delegate { AiSkinWeb(null, true); },
                delegate { AiSend("найди плащ дракона"); },
                delegate { AiSend("поставь better mc"); },
                delegate { AiSend("что ты умеешь"); }
            });
        AiStatus("Модель: " + AiConfig.Model + " — спрашивай что угодно");
    }

    // ---------- разбор запроса ----------

    void AiAnswer(string q)
    {
        string low = q.ToLower();
        try
        {
            // Сначала — всё, что умеет делать сам лаунчер: скин, плащ,
            // сборка, память, Java, аккаунт, настройки. Это быстро и
            // реально меняет состояние, а не просто болтает.
            if (AiSmallTalk(low)) return;
            if (AiSkin(low, q)) return;
            if (AiCape(low)) return;
            if (AiPacks(low)) return;
            if (AiRam(low)) return;
            if (AiJava(low)) return;
            if (AiAccount(low)) return;
            if (AiSettingsHelp(low)) return;

            // Всё остальное — на настоящую модель: она ответит почти на
            // любой вопрос, а не только на заготовленный.
            AiAskLlm(q);
        }
        catch (Exception ex)
        {
            AiStatus("Ошибка разбора запроса");
            AiBubble("Что-то пошло не так: " + ex.Message + "\nПопробуй сформулировать иначе.", false, null, null);
        }
    }

    bool AiSmallTalk(string low)
    {
        if (low.Contains("привет") || low.Contains("здравств") || low.Contains("хай") || low == "ку")
        {
            AiGreeting();
            return true;
        }
        if (low.Contains("спасибо") || low.Contains("благодар"))
        {
            AiBubble("Всегда пожалуйста. Если что — я в шапке, кнопка с искрой.", false, null, null);
            AiStatus("Рад помочь");
            return true;
        }
        if (low.Contains("что ты умеешь") || low.Contains("помощь") || low.Contains("help")
            || low.Contains("команды"))
        {
            AiFallback();
            return true;
        }
        return false;
    }

    // ---------- скины ----------

    bool AiSkin(string low, string orig)
    {
        if (!(low.Contains("скин") || low.Contains("skin") || low.Contains("кож"))) return false;

        // Ник в запросе («скин как у Dream») важнее палитры: ищем в сети
        string who = AiSkinNick(orig);
        if (who != null) { AiSkinWeb(who, false); return true; }

        // «популярный скин» без ника — берём известный и сразу надеваем
        bool popular = low.Contains("популярн") || low.Contains("известн")
                    || low.Contains("топов") || low.Contains("легендар")
                    || low.Contains("знаменит");
        if (popular) { AiSkinWeb(null, true); return true; }

        bool dark = low.Contains("тём") || low.Contains("тем") || low.Contains("чёрн")
                 || low.Contains("черн") || low.Contains("dark");
        bool neon = low.Contains("неон") || low.Contains("ярк") || low.Contains("кибер")
                 || low.Contains("neon");
        bool light = low.Contains("светл") || low.Contains("бел") || low.Contains("light");

        string hair = "#1A1A1A", cloth = "#4A4A4A", accent = "#A55EEA";
        string tone = "тёмную";
        if (neon) { hair = "#2A1B4A"; cloth = "#5B4FE0"; accent = "#E056FD"; tone = "неоновую"; }
        else if (light) { hair = "#C19A6B"; cloth = "#FFFFFF"; accent = "#4FA3F7"; tone = "светлую"; }
        else if (dark) { hair = "#1A1A1A"; cloth = "#2E2E38"; accent = "#A55EEA"; }

        AiBubble("Могу найти готовый скин в интернете — назови ник (например «скин как у Dream») " +
                 "или скажи «популярный скин», и я надену первый подходящий.\n\n" +
                 "А можно дорисовать самому: раздел «Скины», кисть, заливка и 3D-превью. " +
                 "Правки сразу видны на главной.\n\n" +
                 "Под " + tone + " тему: волосы " + hair + ", одежда " + cloth +
                 ", акцент " + accent + ".",
            false,
            new string[] { "Популярный скин", "Тёмные волосы", "Одежда в цвет", "Открыть редактор" },
            new Action[] {
                delegate { AiSkinWeb(null, true); },
                delegate { SkinQuickPaint("hair", SkinColor(hair)); AiBubble("Волосы перекрашены. Открой «Скины», чтобы дорисовать детали — витрина уже обновилась.", false, null, null); },
                delegate { SkinQuickPaint("shirt", SkinColor(cloth)); AiBubble("Одежда перекрашена. Так же можно менять штаны и отдельные пиксели в редакторе.", false, null, null); },
                delegate { HideOverlays(); ShowView(ViewSkin); }
            });
        AiStatus("Подобрал палитру — можно красить");
        return true;
    }

    // ---------- скин из интернета ----------

    int aiSkinSeed = 0;

    // Ник из фразы: «скин как у Dream», «поставь скин Technoblade».
    // Кириллические имена понимаем по небольшому словарю.
    string AiSkinNick(string q)
    {
        string s = (q ?? "").Trim();
        string low = s.ToLower().Replace("ё", "е");
        int at = low.IndexOf("как у ");
        if (at >= 0) return CleanNick(s.Substring(at + 6));
        int k = low.IndexOf("скин");
        if (k < 0) k = low.IndexOf("skin");
        if (k < 0) return null;
        string tail = s.Substring(k + 4);
        string[] parts = tail.Split(new char[] { ' ', ',', '.', '!', '?', ':', ';', '«', '»', '"', '(', ')' },
            StringSplitOptions.RemoveEmptyEntries);
        foreach (string p in parts)
        {
            string c = CleanNick(p);
            if (c != null) return c;
        }
        return null;
    }

    string CleanNick(string p)
    {
        string s = (p ?? "").Trim().Trim('"', '«', '»', '.', ',', '!', '?');
        if (s.Length < 3 || s.Length > 16) return null;
        bool latin = false;
        bool cyr = false;
        foreach (char c in s)
        {
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) { latin = true; continue; }
            if ((c >= '0' && c <= '9') || c == '_') continue;
            if ((c >= 'А' && c <= 'я') || c == 'Ё' || c == 'ё') { cyr = true; continue; }
            return null;
        }
        if (cyr && !latin) return NickFromRu(s);
        if (!latin) return null;
        string l = s.ToLower();
        if (l == "skin" || l == "скин" || l == "популярный" || l == "какой" || l == "ника") return null;
        return s;
    }

    // Русские названия известных скинов
    string NickFromRu(string s)
    {
        string l = (s ?? "").ToLower().Replace("ё", "е");
        if (l.StartsWith("дрим")) return "Dream";
        if (l.StartsWith("течно") || l.StartsWith("техно")) return "Technoblade";
        if (l.StartsWith("нотч")) return "Notch";
        if (l.StartsWith("херобрин") || l.StartsWith("геробрин")) return "Herobrine";
        if (l.StartsWith("джеб")) return "jeb_";
        if (l.StartsWith("диннербон")) return "Dinnerbone";
        if (l.StartsWith("скеппи")) return "Skeppy";
        if (l.StartsWith("бэдбой") || l.StartsWith("бедбой")) return "BadBoyHalo";
        if (l.StartsWith("джордж")) return "GeorgeNotFound";
        if (l.StartsWith("сапнап")) return "Sapnap";
        if (l.StartsWith("томми")) return "TommyInnit";
        if (l.StartsWith("вилбур")) return "WilburSoot";
        if (l.StartsWith("капитан") || l.StartsWith("спаркл")) return "CaptainSparklez";
        if (l.StartsWith("дэнтдм") || l.StartsWith("дантдм")) return "DanTDM";
        return null;
    }

    // Поиск скина в сети и мгновенная примерка.
    void AiSkinWeb(string nick, bool random)
    {
        AiBubble(nick == null
            ? "Ищу популярный скин в интернете..."
            : "Ищу в интернете скин «" + nick + "»...", false, null, null);
        AiStatus("Ищу скин в сети...");
        int seed = random ? aiSkinSeed++ : 0;
        Thread t = new Thread((ThreadStart)delegate
        {
            string used = nick;
            string err = "";
            Bitmap tex = null;
            try
            {
                if (string.IsNullOrEmpty(used))
                {
                    for (int i = 0; i < WebSkins.Popular.Length && tex == null; i++)
                    {
                        used = WebSkins.Popular[(seed + i) % WebSkins.Popular.Length];
                        tex = WebSkins.Fetch(used);
                    }
                    err = WebSkins.LastError;
                }
                else
                {
                    tex = WebSkins.Fetch(used);
                    err = WebSkins.LastError;
                }
            }
            catch (Exception ex) { err = ex.Message; }

            string name = used;
            SocialPost(delegate
            {
                if (tex == null)
                {
                    AiStatus("Скин не найден");
                    AiBubble("Не нашёл в интернете скин «" + name + "»" +
                             (string.IsNullOrEmpty(err) ? "." : ": " + err) + "\n\n" +
                             "Напиши ник так, как он в игре, — например «скин как у Technoblade». " +
                             "Или нарисуй свой в редакторе.",
                        false,
                        new string[] { "Другой популярный", "Открыть редактор" },
                        new Action[] {
                            delegate { AiSkinWeb(null, true); },
                            delegate { HideOverlays(); ShowView(ViewSkin); }
                        });
                    return;
                }
                ApplyWebSkin(tex, name);
            });
        });
        t.IsBackground = true;
        t.Start();
    }

    // Надеть найденный скин: витрина, редактор, аватар, кэш и Luxe.
    void ApplyWebSkin(Bitmap tex, string who)
    {
        if (tex == null) return;
        try { PlayerSkin.SetLocal(tex); } catch { }
        try { if (skinCanvas != null) skinCanvas.Load(tex); } catch { }
        try { if (skin3d != null) skin3d.Invalidate(); } catch { }
        AiStatus("Скин «" + who + "» надет");
        AiBubble("Нашёл в интернете скин «" + who + "» и сразу надел: фигура на главной и аватар уже обновились.\n\n" +
                 "Модель осталась " + (skinModel == "SLIM" ? "SLIM" : "CLASSIC") +
                 ". Нужен другой — назови ник, поищу.",
            false,
            new string[] { "Другой популярный", "Открыть редактор" },
            new Action[] {
                delegate { AiSkinWeb(null, true); },
                delegate { HideOverlays(); ShowView(ViewSkin); }
            });

        byte[] png = null;
        try
        {
            using (MemoryStream ms = new MemoryStream())
            {
                tex.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                png = ms.ToArray();
            }
        }
        catch { }
        if (png != null) PushSkinOnline(png);
    }

    // Тихо отправляем скин на сервис: локально он уже надет, а так его
    // увидят другие. Ошибку не выносим в чат — только в строку статуса.
    void PushSkinOnline(byte[] png)
    {
        string model = skinModel;
        string who = nick;
        Thread t = new Thread((ThreadStart)delegate
        {
            bool ok = false;
            string err = "";
            try
            {
                if (Auth.LoggedIn) ok = Auth.UploadSkin(png, model, out err);
                else if (Luxe.LoginFor(who)) { ok = Luxe.SetSkin(png, model); err = Luxe.LastError; }
                else err = Luxe.LastError;
            }
            catch (Exception ex) { err = ex.Message; }
            SocialPost(delegate
            {
                AiStatus(ok ? "Скин сохранён — его видят в игре"
                            : "Скин надет локально" + (string.IsNullOrEmpty(err) ? "" : " (" + err + ")"));
            });
        });
        t.IsBackground = true;
        t.Start();
    }

    // ---------- плащи ----------

    bool AiCape(string low)
    {
        if (!(low.Contains("плащ") || low.Contains("cape") || low.Contains("накидк"))) return false;

        // «плащ дракона», «найди огненный плащ» — ищем по названию каталога
        List<string> want = AiTokens(low, new string[] {
            "плащ","плаща","плащи","плащей","плаще","cape","накидк","накидку","найди","подбери",
            "поставь","надеть","надевай","одень","хочу","мне","пожалуйста","какой","какие","есть",
            "для","под","стиль","популярный","популярные","лучший","красивый","крутой","сейчас",
            "find","wear","please","the","get","show" });

        List<LuxeCape> cached = capesCache;
        if (cached != null && cached.Count > 0)
        {
            AiCapePick(cached, want);
            return true;
        }

        AiBubble("Смотрю каталог плащей Luxe — это пара секунд.", false, null, null);
        AiStatus("Загружаю каталог плащей...");
        Thread t = new Thread((ThreadStart)delegate
        {
            List<LuxeCape> list = null;
            try { list = Luxe.Capes("popular"); } catch { }
            SocialPost(delegate
            {
                if (list == null || list.Count == 0)
                {
                    AiStatus("Каталог недоступен");
                    AiBubble("Каталог плащей сейчас не отвечает (нужен вход в аккаунт Luxe).\n\n" +
                             "Открой раздел «Плащи» — там есть встроенные варианты, они работают и офлайн.",
                        false,
                        new string[] { "Открыть плащи" },
                        new Action[] { delegate { HideOverlays(); ShowView(ViewCapes); } });
                    return;
                }
                AiCapePick(list, want);
            });
        });
        t.IsBackground = true;
        t.Start();
        return true;
    }

    // Подбор плаща: точное совпадение по названию/автору, иначе — по теме
    // («дракон» -> Энд/Огонь), иначе просто популярные. Лучший надеваем сразу.
    void AiCapePick(List<LuxeCape> list, List<string> want)
    {
        List<LuxeCape> found = new List<LuxeCape>();
        if (want != null && want.Count > 0)
        {
            List<string> keys = new List<string>();
            foreach (string w in want)
            {
                string k = AiCapeTheme(w);
                if (k != null) keys.Add(k);
                keys.Add(w.Replace("ё", "е"));
            }
            foreach (LuxeCape c in list)
            {
                if (c == null || string.IsNullOrEmpty(c.Id)) continue;
                string hay = (c.Id + " " + c.Name + " " + (c.Author ?? "")).ToLower().Replace("ё", "е");
                foreach (string k in keys) if (hay.Contains(k)) { found.Add(c); break; }
            }
        }

        List<LuxeCape> show = found.Count > 0 ? found : new List<LuxeCape>();
        if (show.Count == 0)
        {
            for (int i = 0; i < list.Count && show.Count < 3; i++)
                if (list[i] != null && !string.IsNullOrEmpty(list[i].Id)) show.Add(list[i]);
        }
        if (show.Count == 0)
        {
            AiBubble("В каталоге пока нет плащей.", false, null, null);
            return;
        }

        string text;
        if (found.Count > 0)
        {
            text = "По запросу нашёл плащ «" + show[0].Name + "» — надеваю.\n";
        }
        else if (want != null && want.Count > 0)
        {
            text = "Точного совпадения по запросу нет. Вот что есть в каталоге:\n";
        }
        else
        {
            text = "Популярные плащи сейчас:\n";
        }

        for (int i = 0; i < show.Count; i++)
        {
            LuxeCape c = show[i];
            text += "\n" + (i + 1) + ". " + c.Name
                 + (string.IsNullOrEmpty(c.Author) ? "" : " — " + c.Author)
                 + (c.Wears > 0 ? "  (" + c.Wears + " носят)" : "");
        }
        if (string.IsNullOrEmpty(nick))
            text += "\n\nСначала задай ник — плащ привязывается к игроку.";

        List<string> labels = new List<string>();
        List<Action> calls = new List<Action>();
        for (int i = 0; i < show.Count && i < 3; i++)
        {
            LuxeCape cap = show[i];
            labels.Add("Надеть «" + Ui.Short(cap.Name, 18) + "»");
            calls.Add(delegate { AiCapeWear(cap); });
        }
        labels.Add("Открыть каталог");
        calls.Add(delegate { HideOverlays(); ShowView(ViewCapes); });

        AiBubble(text, false, labels.ToArray(), calls.ToArray());
        AiStatus(found.Count > 0 ? "Нашёл плащ — надеваю" : "Нашёл " + show.Count + " плаща");

        // Автодействие: раз просили конкретику, сразу надеваем лучший
        if (found.Count > 0) AiCapeWear(show[0]);
    }

    // Тема запроса -> id плаща в каталоге Luxe
    string AiCapeTheme(string w)
    {
        string s = (w ?? "").ToLower().Replace("ё", "е");
        if (s.Length < 3) return null;
        if (s.Contains("дракон") || s.Contains("огнедыш") || s.Contains("dragon")) return "ender";
        if (s.Contains("огон") || s.Contains("пламя") || s.Contains("пламен") || s.Contains("адск")
            || s.Contains("fire") || s.Contains("flame")) return "flame";
        if (s.Contains("лед") || s.Contains("лёд") || s.Contains("зим") || s.Contains("снег") || s.Contains("мороз")
            || s.Contains("ice") || s.Contains("winter") || s.Contains("snow")) return "ice";
        if (s.Contains("космос") || s.Contains("звезд") || s.Contains("галакт") || s.Contains("планет")
            || s.Contains("space") || s.Contains("star") || s.Contains("cosmic")) return "cosmos";
        if (s.Contains("неон") || s.Contains("светящ") || s.Contains("neon")) return "neon";
        if (s.Contains("золот") || s.Contains("gold")) return "gold";
        if (s.Contains("сердц") || s.Contains("любов") || s.Contains("heart") || s.Contains("love")) return "heart";
        if (s.Contains("череп") || s.Contains("смерт") || s.Contains("скел") || s.Contains("skull")) return "skull";
        if (s.Contains("панда") || s.Contains("panda")) return "panda";
        if (s.Contains("сакур") || s.Contains("цвет") || s.Contains("sakura")) return "sakura";
        if (s.Contains("радуг") || s.Contains("rainbow")) return "rainbow";
        if (s.Contains("облак") || s.Contains("небо") || s.Contains("небес") || s.Contains("cloud")) return "clouds";
        if (s.Contains("лун") || s.Contains("ночн") || s.Contains("moon")) return "moon";
        if (s.Contains("солн") || s.Contains("рассвет") || s.Contains("sun")) return "sun";
        if (s.Contains("крипер") || s.Contains("creeper")) return "creeper";
        if (s.Contains("матриц") || s.Contains("хакер") || s.Contains("цифр") || s.Contains("код")
            || s.Contains("matrix") || s.Contains("hacker")) return "matrix";
        if (s.Contains("изумруд") || s.Contains("зелен") || s.Contains("emerald")) return "emerald";
        if (s.Contains("камуфляж") || s.Contains("воен") || s.Contains("милитар") || s.Contains("армейск")
            || s.Contains("camo") || s.Contains("military")) return "camo";
        if (s.Contains("корон") || s.Contains("цар") || s.Contains("королев") || s.Contains("crown")) return "crown";
        if (s.Contains("аметист") || s.Contains("фиолет") || s.Contains("amethyst") || s.Contains("purple")) return "amethyst";
        if (s.Contains("волн") || s.Contains("море") || s.Contains("океан") || s.Contains("водн")
            || s.Contains("wave") || s.Contains("ocean")) return "waves";
        if (s.Contains("закат") || s.Contains("sunset")) return "sunset";
        if (s.Contains("обсидиан") || s.Contains("черн") || s.Contains("тёмн")
            || s.Contains("obsidian") || s.Contains("black")) return "obsidian";
        if (s.Contains("пиксел") || s.Contains("pixel")) return "pixel";
        if (s.Contains("слиз") || s.Contains("slime")) return "slime";
        if (s.Contains("редстоун") || s.Contains("красн") || s.Contains("redstone") || s.Contains("red")) return "redstone";
        if (s.Contains("турбо") || s.Contains("спорт") || s.Contains("turbo")) return "turbox";
        if (s.Contains("шахмат") || s.Contains("клетк") || s.Contains("checker")) return "checker";
        if (s.Contains("край") || s.Contains("энд") || s.Contains("ender")) return "ender";
        return null;
    }

    // Слова запроса без служебных
    List<string> AiTokens(string text, string[] stop)
    {
        List<string> res = new List<string>();
        foreach (string w in (text ?? "").ToLower().Replace("ё", "е")
                     .Split(new char[] { ' ', ',', '.', '!', '?', ':', ';', '"', '«', '»', '(', ')' },
                            StringSplitOptions.RemoveEmptyEntries))
        {
            // «mc» и «fps» — короткие, но значимые слова запроса
            if (w.Length < 3 && !(w.Length == 2 && w[0] >= 'a' && w[0] <= 'z')) continue;
            bool skip = false;
            if (stop != null)
                foreach (string s in stop) if (w == s) { skip = true; break; }
            if (!skip && !res.Contains(w)) res.Add(w);
        }
        return res;
    }

    // Надеть плащ из чата: свой поток и честный ответ в чат, а не
    // молчаливая попытка (без входа сервис отвечает 401).
    void AiCapeWear(LuxeCape c)
    {
        if (c == null) return;
        if (string.IsNullOrEmpty(nick))
        {
            AiBubble("Сначала задай ник — плащ привязывается к игроку.", false,
                new string[] { "Открыть настройки" },
                new Action[] { delegate { HideOverlays(); setTabIdx = 3; ShowSettings(); } });
            return;
        }
        AiStatus("Надеваю «" + c.Name + "»...");
        Thread t = new Thread((ThreadStart)delegate
        {
            bool ok = false;
            string err = "";
            try
            {
                // Сессия сервиса живёт по нику: офлайн-игроку тоже можно
                // надеть плащ, вход в Microsoft для этого не нужен.
                if (!Luxe.Ready && !Luxe.LoginFor(nick)) err = Luxe.LastError;
                if (err == "") { ok = Luxe.SetCape(c.Id); err = Luxe.LastError; }
            }
            catch (Exception ex) { err = ex.Message; }
            SocialPost(delegate
            {
                if (ok)
                {
                    AiBubble("Плащ «" + c.Name + "» надет — он появится в игре и в разделе «Плащи».",
                        false, new string[] { "Открыть плащи" },
                        new Action[] { delegate { HideOverlays(); ShowView(ViewCapes); } });
                    AiStatus("Плащ надет");
                    try { CapesLoad(false); } catch { }
                }
                else
                {
                    AiBubble("Не получилось надеть: " + (string.IsNullOrEmpty(err) ? "сервис не ответил" : err),
                        false, new string[] { "Открыть плащи" },
                        new Action[] { delegate { HideOverlays(); ShowView(ViewCapes); } });
                    AiStatus("Ошибка плаща");
                }
            });
        });
        t.IsBackground = true;
        t.Start();
    }

    // ---------- сборки ----------

    bool AiPacks(string low)
    {
        bool packWord = low.Contains("сборк") || low.Contains("модпак") || low.Contains("сборок")
                     || low.Contains("моды") || low.Contains("модов")
                     || low.Contains("modpack") || low.Contains("mods") || low.Contains("pack");
        bool installWord = low.Contains("поставь") || low.Contains("установ") || low.Contains("скачай")
                        || low.Contains("загрузи") || low.Contains("хочу") || low.Contains("подбер")
                        || low.Contains("посовет") || low.Contains("какую") || low.Contains("найди")
                        || low.Contains("install") || low.Contains("download") || low.Contains("find");
        bool gameWord = low.Contains("майн") || low.Contains("minecraft") || low.Contains("игр")
                     || low.Contains("мод") || low.Contains(" mc") || low.EndsWith("mc");
        // «скачай фильм» — это не к нам: нужно либо слово про сборку,
        // либо глагол установки вместе с упоминанием игры/модов
        if (!(packWord || (installWord && gameWord))) return false;
        // «что такое моды» — это вопрос к модели, а не команда установки
        bool question = low.Contains("что такое") || low.Contains("объясни")
                     || low.Contains("чем отлич") || low.Contains("зачем");
        if (question && !low.Contains("поставь") && !low.Contains("установ")
            && !low.Contains("скачай") && !low.Contains("хочу"))
            return false;

        // Название сборки из запроса: «поставь better mc», «сборка rlcraft»
        List<string> want = AiTokens(low, new string[] {
            "сборку","сборка","сборки","сборок","сборке","модпак","модпаки","моды","модов",
            "поставь","установи","установить","установи","скачай","загрузи","хочу","мне",
            "пожалуйста","подбери","посоветуй","какую","найди","майн","майнкрафт","minecraft",
            "игру","игра","для","на","в","с","и","мод",
            "install","download","pack","modpack","mods","please","the","me","give","find" });
        string q = string.Join(" ", want.ToArray());

        AiBubble(q.Length > 0
            ? "Ищу сборку «" + q + "» в интернете (Modrinth)..."
            : "Смотрю популярные сборки в интернете (Modrinth)...", false, null, null);
        AiStatus("Ищу сборки в сети...");

        string mcVer = curVer();
        bool verOk = !string.IsNullOrEmpty(mcVer);
        Thread t = new Thread((ThreadStart)delegate
        {
            List<ModInfo> found = null;
            string err = "";
            try { found = WebPacks.Search(q, verOk ? mcVer : "", 6); err = WebPacks.LastError; }
            catch (Exception ex) { err = ex.Message; }
            SocialPost(delegate { AiPacksShow(q, found, err); });
        });
        t.IsBackground = true;
        t.Start();
        return true;
    }

    void AiPacksShow(string q, List<ModInfo> found, string err)
    {
        if (found == null || found.Count == 0)
        {
            AiStatus("Сборки не нашлись");
            AiBubble("В интернете ничего не нашлось" + (string.IsNullOrEmpty(err) ? "." : ": " + err) + "\n\n" +
                     "Скажи название иначе — например «better mc», «cobblemon», «rlcraft», «dawncraft». " +
                     "Или попроси просто «поставь сборку» — покажу популярные.",
                false,
                new string[] { "Популярные сборки" },
                new Action[] { delegate { AiSend("поставь сборку"); } });
            return;
        }

        // Название названо — ставим самое близкое совпадение сразу
        List<ModInfo> list = Relevant(q, found);
        if (list.Count == 0) list = found;

        ModInfo best = BestPack(q, list);
        bool force = q.Contains("переустанов") || q.Contains("обнови") || q.Contains("заново");
        StringBuilder text = new StringBuilder();
        List<string> labels = new List<string>();
        List<Action> calls = new List<Action>();

        if (best != null)
        {
            text.Append("Нашёл в интернете «").Append(best.title).Append("»");
            if (best.downloads > 0)
                text.Append(" (").Append(Ui.Short(FormatCount(best.downloads), 12)).Append(" скачиваний)");
            text.Append(". Ставлю — это займёт пару минут.\n");
            if (!string.IsNullOrEmpty(best.desc))
                text.Append(Ui.Short(best.desc, 160)).Append("\n");
            text.Append("\nДругие варианты по запросу:\n");
            labels.Add("Открыть главную");
            calls.Add(delegate { HideOverlays(); ShowView(ViewHome); });
        }
        else
        {
            text.Append(q.Length > 0
                ? "Нашёл сборки в интернете:\n"
                : "Популярные сборки в интернете:\n");
        }

        int shown = 0;
        foreach (ModInfo m in list)
        {
            if (best != null && m.id == best.id) continue;
            if (shown >= 4) break;
            shown++;
            text.Append("\n• ").Append(m.title);
            if (m.downloads > 0) text.Append("  (").Append(FormatCount(m.downloads)).Append(")");
            ModInfo mm = m;
            labels.Add("Поставить: " + Ui.Short(m.title, 18));
            calls.Add(delegate { AiPackInstall(mm, force); });
        }
        labels.Add("Открыть моды");
        calls.Add(delegate { HideOverlays(); ShowView(ViewMods); });

        AiBubble(text.ToString(), false, labels.ToArray(), calls.ToArray());
        AiStatus(best != null ? "Ставлю сборку..." : "Выбери сборку — поставлю");

        if (best != null) AiPackInstall(best, force);
    }

    // Самое близкое к запросу название (по словам в title/slug)
    ModInfo BestPack(string q, List<ModInfo> found)
    {
        if (string.IsNullOrEmpty(q)) return null;
        List<string> toks = AiTokens(q, null);
        if (toks.Count == 0) return null;
        ModInfo best = null;
        int bestScore = 0;
        long bestDl = -1;
        foreach (ModInfo m in found)
        {
            string hay = (m.title + " " + m.slug).ToLower().Replace("-", " ").Replace("_", " ");
            int sc = 0;
            foreach (string t in toks) if (hay.Contains(t)) sc++;
            // Автоустановка — только когда совпали ВСЕ слова запроса:
            // «better mc» не должен превращаться в «Act. Better».
            if (sc < toks.Count) continue;
            if (sc > bestScore || (sc == bestScore && m.downloads > bestDl))
            {
                bestScore = sc;
                bestDl = m.downloads;
                best = m;
            }
        }
        return best;
    }

    string FormatCount(long n)
    {
        if (n >= 1000000) return (n / 1000000) + " млн";
        if (n >= 1000) return (n / 1000) + " тыс";
        return n.ToString();
    }

    // Отбрасываем мусор из выдачи: «Better MC» не должен тянуть
    // «better mc optimize» с двумя тысячами скачиваний, если рядом
    // есть настоящая сборка с сотнями тысяч.
    List<ModInfo> Relevant(string q, List<ModInfo> found)
    {
        List<ModInfo> res = new List<ModInfo>();
        if (found == null) return res;
        List<string> toks = AiTokens(q, null);
        if (toks.Count == 0) return new List<ModInfo>(found);
        long top = 0;
        foreach (ModInfo m in found) if (m.downloads > top) top = m.downloads;
        foreach (ModInfo m in found)
        {
            string hay = (m.title + " " + m.slug).ToLower().Replace("-", " ").Replace("_", " ");
            int sc = 0;
            foreach (string t in toks) if (hay.Contains(t)) sc++;
            if (sc == 0) continue;
            // мелочь без совпадения по всем словам — в топку
            if (top > 20000 && m.downloads * 40 < top && sc < toks.Count) continue;
            res.Add(m);
        }
        return res;
    }

    // Скачивание и распаковка сборки в фоне: UI не должен ждать.
    void AiPackInstall(ModInfo m, bool force)
    {
        if (m == null) return;
        string slug = m.slug.Length > 0 ? m.slug : m.id;
        string title = m.title;

        // Уже стоит — не качаем 500 МБ заново
        string have = force ? null : WebPacks.InstalledId(slug);
        if (have != null)
        {
            AiStatus("Уже установлена");
            AiBubble("«" + title + "» уже установлена — версия «" + have + "».\n\n" +
                     "На главной открой вкладку «Моды»: карточка сборки помечена «Установлена», жми «Играть». " +
                     "Если нужна свежая версия, скажи «переустанови " + Ui.Short(title, 20) + "».",
                false,
                new string[] { "Показать сборку", "Открыть моды" },
                new Action[] {
                    delegate { HideOverlays(); SetHomeTab(2); ShowView(ViewHome); },
                    delegate { HideOverlays(); ShowView(ViewMods); }
                });
            return;
        }

        AiStatus("Готовлю «" + title + "»...");
        string mcVer = curVer();
        bool verOk = !string.IsNullOrEmpty(mcVer);

        Thread t = new Thread((ThreadStart)delegate
        {
            string kind = "";
            Dictionary<string, object> ver = null;
            string err = "";
            try
            {
                ver = WebPacks.BestVersion(m.id, verOk ? mcVer : "", out kind);
                // под текущую версию могло не быть — берём любую свежую
                if (ver == null) ver = WebPacks.BestVersion(m.id, "", out kind);
            }
            catch (Exception ex) { err = ex.Message; }

            if (ver == null)
            {
                SocialPost(delegate
                {
                    AiStatus("Нет версии сборки");
                    AiBubble("У «" + title + "» нет сборки под " +
                             (verOk ? "Minecraft " + mcVer : "fabric/quilt") + ". " +
                             "Modrinth отдаёт только fabric/quilt — forge-сборки этот лаунчер не ставит.",
                        false,
                        new string[] { "Другие сборки" },
                        new Action[] { delegate { AiSend("поставь сборку"); } });
                });
                return;
            }

            string id = "";
            bool ok = false;
            try
            {
                ok = WebPacks.Install(slug, title, ver, kind, out id,
                    delegate(string s) { AiStatusAsync(s); }, null);
                if (!ok) err = WebPacks.LastError;
            }
            catch (Exception ex) { err = ex.Message; }

            string installedId = id;
            SocialPost(delegate
            {
                if (ok)
                {
                    AiStatus("Сборка установлена: " + title);
                    AiBubble("Готово: «" + title + "» установлена" +
                             (installedId.Length > 0 ? " (версия «" + installedId + "»)" : "") + ".\n\n" +
                             "Она уже в списке: главная → вкладка «Моды», карточка с пометкой «Установлена». " +
                             "Открой её и жми «Играть». Первый запуск дольше обычного: докачивается игра и собираются моды.",
                        false,
                        new string[] { "Показать сборку", "Открыть моды" },
                        new Action[] {
                            delegate { HideOverlays(); SetHomeTab(2); ShowView(ViewHome); },
                            delegate { HideOverlays(); ShowView(ViewMods); }
                        });
                }
                else
                {
                    AiStatus("Сборка не установилась");
                    AiBubble("Не получилось поставить «" + title + "»" +
                             (string.IsNullOrEmpty(err) ? "." : ": " + Ui.Short(err, 160)) + "\n\n" +
                             "Проверь интернет и свободное место — сборки весят 300–800 МБ.",
                        false,
                        new string[] { "Повторить", "Другие сборки" },
                        new Action[] {
                            delegate { AiPackInstall(m, true); },
                            delegate { AiSend("поставь сборку"); }
                        });
                }
            });
        });
        t.IsBackground = true;
        t.Start();
    }

    // Статус из фонового потока: Label трогаем только на UI-потоке
    void AiStatusAsync(string s)
    {
        try { SocialPost(delegate { AiStatus(s); }); } catch { }
    }

    // ---------- память ----------

    bool AiRam(string low)
    {
        bool direct = low.Contains("озу") || low.Contains("памят") || low.Contains("ram")
                   || low.Contains("оператив");
        bool symptom = (low.Contains("лаг") || low.Contains("тормоз") || low.Contains("просад"))
                   && (low.Contains("майн") || low.Contains("minecraft") || low.Contains("игр")
                       || low.Contains("сборк") || low.Contains("лаунчер"));
        if (!direct && !symptom) return false;

        string advice;
        try { advice = Convert.ToInt32(ram) >= 4 ? "у тебя уже " + ram + " ГБ — для модов это норм"
                                                 : "сейчас " + ram + " ГБ — для ванилы хватает, для сборок мало"; }
        catch { advice = "сейчас " + ram + " ГБ"; }

        AiBubble("Память для игры: " + advice + ".\n\n" +
                 "Ориентир: ванила — 2 ГБ, лёгкие сборки — 4 ГБ, тяжёлые (300+ модов) — 6–8 ГБ. " +
                 "Больше 12 ГБ не нужно: Java начинает дольше собирать мусор, и FPS падает.\n\n" +
                 "Выбери значение — применю сразу.",
            false,
            new string[] { "4 ГБ", "6 ГБ", "8 ГБ", "Открыть настройки" },
            new Action[] {
                delegate { AiSetRam("4"); },
                delegate { AiSetRam("6"); },
                delegate { AiSetRam("8"); },
                delegate { HideOverlays(); setTabIdx = 0; ShowSettings(); }
            });
        AiStatus("Память: " + ram + " ГБ");
        return true;
    }

    void AiSetRam(string gb)
    {
        ram = gb;
        Mc.SaveConfig(nick, curVer(), ram, closeOnLaunch);
        SetStatus("Память: " + ram + " ГБ", true);
        AiStatus("Поставил " + gb + " ГБ");
        AiBubble("Готово: теперь игре выделено " + gb + " ГБ. Применится при следующем запуске.",
            false, null, null);
    }

    // ---------- Java ----------

    bool AiJava(string low)
    {
        if (!(low.Contains("java") || low.Contains("джав") || low.Contains("jdk")))
            return false;

        string state;
        if (string.IsNullOrEmpty(javaExe))
            state = "Java пока не найдена — лаунчер скачает её при первом запуске.";
        else
        {
            int major = 0;
            try { major = Mc.GetJavaMajor(javaExe); } catch { }
            state = "Сейчас используется Java " + (major > 0 ? major.ToString() : "?") + "\n" + javaExe;
        }

        AiBubble("Под каждую версию Minecraft нужна своя Java:\n" +
                 "• 1.16 и ниже — Java 8\n• 1.17–1.21.3 — Java 17\n" +
                 "• 1.21.4 и новее — Java 21\n• 26.x — Java 25\n\n" + state,
            false,
            new string[] { "Скачать Java 21", "Найти Java", "Открыть настройки" },
            new Action[] {
                delegate
                {
                    AiStatus("Скачиваю Java 21...");
                    string mc = Mc.BaseMcOf(curVer());
                    Thread t = new Thread((ThreadStart)delegate
                    {
                        try
                        {
                            javaExe = Mc.EnsureJavaFor(mc, AiStatus);
                            SocialPost(delegate
                            {
                                AiStatus("Java готова");
                                AiBubble("Java установлена:\n" + javaExe, false, null, null);
                            });
                        }
                        catch (Exception ex)
                        {
                            SocialPost(delegate { AiBubble("Не вышло скачать Java: " + ex.Message, false, null, null); });
                        }
                    });
                    t.IsBackground = true;
                    t.Start();
                },
                delegate
                {
                    javaExe = Mc.FindLocalJava();
                    AiStatus(javaExe == null ? "Java не найдена" : "Java найдена");
                    AiBubble(javaExe == null
                        ? "Системная Java не найдена. Нажми «Скачать Java 21» — она встанет в папку лаунчера."
                        : "Нашёл: " + javaExe, false, null, null);
                },
                delegate { HideOverlays(); setTabIdx = 1; ShowSettings(); }
            });
        return true;
    }

    // ---------- аккаунт ----------

    bool AiAccount(string low)
    {
        if (!(low.Contains("ник") || low.Contains("аккаунт") || low.Contains("лиценз")
              || low.Contains("вход") || low.Contains("войти") || low.Contains("microso")))
            return false;

        string state = "Ник: " + nick + "\n";
        state += Auth.LoggedIn
            ? "Microsoft: вход выполнен (" + Auth.Name + ") — работает лицензия, скины и плащи Luxe."
            : "Microsoft: вход не выполнен — игра идёт офлайн, плащи Luxe не сохраняются.";

        AiBubble(state + "\n\nНик — это имя в игре, 3–16 символов: латиница, цифры, «_». " +
                 "Аккаунт Microsoft нужен только для лицензии и общего каталога.",
            false,
            new string[] { "Сменить ник", Auth.LoggedIn ? "Выйти" : "Войти через Microsoft" },
            new Action[] {
                delegate { HideOverlays(); setTabIdx = 3; ShowSettings(); },
                delegate
                {
                    if (Auth.LoggedIn)
                    {
                        try { Auth.Logout(); } catch { }
                        try { UpdateLicUi(); } catch { }
                        AiStatus("Вы вышли из аккаунта");
                        AiBubble("Вышел из аккаунта. Офлайн-игра и ник остались.", false, null, null);
                    }
                    else
                    {
                        ShowAuth();
                    }
                }
            });
        AiStatus("Аккаунт: " + nick);
        return true;
    }

    // ---------- настройки ----------

    bool AiSettingsHelp(string low)
    {
        bool launcherWord = low.Contains("настрой") || low.Contains("вкладк") || low.Contains("лаунчер")
                         || low.Contains("шестер") || low.Contains("папк") || low.Contains("конфиг");
        if (!launcherWord) return false;
        // «как настроить телевизор» пусть разбирает модель, а не правила
        bool ctx = low.Contains("лаунчер") || low.Contains("майн") || low.Contains("minecraft")
                || low.Contains("скин") || low.Contains("плащ") || low.Contains("озу")
                || low.Contains("java") || low.Contains("мод") || low.Contains("вкладк")
                || low.Contains("шестер") || low.Contains("папк") || low.Contains("верси")
                || low.Contains("памят") || low.Contains("обнов");
        if (!ctx) return false;

        AiBubble("Все настройки — под шестерёнкой в сайдбаре, шесть вкладок:\n\n" +
                 "• Игра — память, закрытие лаунчера, папка игры, версия\n" +
                 "• Java — какая Java найдена и кнопка загрузки\n" +
                 "• Лаунчер — папка данных, очистка кэша, сброс\n" +
                 "• Аккаунт — ник и вход через Microsoft\n" +
                 "• Обновления — проверка новой версии\n" +
                 "• О программе — версия и путь к exe\n\n" +
                 "Изменения применяются сразу, «Готово» просто закрывает окно.",
            false,
            new string[] { "Открыть настройки", "Про память", "Про Java" },
            new Action[] {
                delegate { HideOverlays(); ShowSettings(); },
                delegate { AiSend("сколько озу"); },
                delegate { AiSend("java"); }
            });
        AiStatus("Настройки — шесть вкладок");
        return true;
    }

    // ---------- настройки модели ----------

    void ShowAiSetup()
    {
        if (aiSetup == null) BuildAiSetup();
        if (aiSetup == null) return;
        aiSetup.Location = new Point(
            Math.Max(8, (ClientSize.Width - aiSetup.Width) / 2),
            Math.Max(8, (ClientSize.Height - aiSetup.Height) / 2));
        aiSetupUrl.Text = AiConfig.Endpoint;
        aiSetupModel.Text = AiConfig.Model;
        aiSetupKey.Text = AiConfig.Key;
        aiSetup.Visible = true;
        aiSetup.BringToFront();
    }

    RoundedPanel aiSetup;
    TextBox aiSetupUrl, aiSetupModel, aiSetupKey;
    Label aiSetupState;

    void BuildAiSetup()
    {
        int w = 520, h = 400;
        aiSetup = new RoundedPanel();
        aiSetup.Radius = 16;
        aiSetup.BackColor = Ui.Surface;
        aiSetup.Border = Ui.Line;
        aiSetup.Size = new Size(w, h);
        aiSetup.Visible = false;
        Controls.Add(aiSetup);
        CardHead(aiSetup, "Модель помощника", w, delegate { aiSetup.Visible = false; });

        Label l1 = AiSetupLabel("Адрес API (OpenAI-совместимый)", 66);
        aiSetup.Controls.Add(l1);
        aiSetupUrl = AiSetupBox(w - 48, 88);
        aiSetup.Controls.Add(AiSetupBoxHost(w - 48, 88, aiSetupUrl));

        Label l2 = AiSetupLabel("Модель", 140);
        aiSetup.Controls.Add(l2);
        aiSetupModel = AiSetupBox(230, 162);
        aiSetup.Controls.Add(AiSetupBoxHost(230, 162, aiSetupModel));

        Label l3 = AiSetupLabel("Ключ (если нужен)", 214);
        aiSetup.Controls.Add(l3);
        aiSetupKey = AiSetupBox(w - 48, 236);
        aiSetupKey.UseSystemPasswordChar = true;
        aiSetup.Controls.Add(AiSetupBoxHost(w - 48, 236, aiSetupKey));

        aiSetupState = new Label();
        aiSetupState.Font = Ui.Font(8);
        aiSetupState.ForeColor = Ui.GrayDim;
        aiSetupState.BackColor = Color.Transparent;
        aiSetupState.AutoSize = false;
        aiSetupState.Location = new Point(24, 282);
        aiSetupState.Size = new Size(w - 48, 44);
        aiSetupState.Text = "Без ключа — бесплатная модель. Можно вставить свой ключ OpenAI/OpenRouter " +
                            "или адрес локальной Ollama: http://127.0.0.1:11434/v1/chat/completions";
        aiSetup.Controls.Add(aiSetupState);

        RoundedButton save = new RoundedButton();
        save.Text = "Сохранить";
        save.Radius = 9;
        save.Font = Ui.Font(9, FontStyle.Bold);
        save.Location = new Point(24, h - 56);
        save.Size = new Size(150, 38);
        save.Click += delegate
        {
            AiConfig.Endpoint = aiSetupUrl.Text.Trim();
            AiConfig.Model = aiSetupModel.Text.Trim();
            AiConfig.Key = aiSetupKey.Text.Trim();
            if (AiConfig.Endpoint.Length == 0) AiConfig.Endpoint = AiConfig.DefaultEndpoint;
            if (AiConfig.Model.Length == 0) AiConfig.Model = AiConfig.DefaultModel;
            AiConfig.Save();
            aiSetup.Visible = false;
            AiHideTyping();
            aiTyping = null;
            AiBubble("Модель обновлена: " + AiConfig.Model + "\n" + AiConfig.Endpoint, false, null, null);
            AiStatus("Модель: " + AiConfig.Model);
        };
        aiSetup.Controls.Add(save);

        RoundedButton def = RoundedButton.Dark("По умолчанию", 9);
        def.Font = Ui.Font(9, FontStyle.Bold);
        def.Location = new Point(184, h - 56);
        def.Size = new Size(160, 38);
        def.Click += delegate
        {
            AiConfig.Reset();
            aiSetupUrl.Text = AiConfig.Endpoint;
            aiSetupModel.Text = AiConfig.Model;
            aiSetupKey.Text = "";
            AiStatus("Модель: " + AiConfig.Model);
        };
        aiSetup.Controls.Add(def);
    }

    Label AiSetupLabel(string text, int y)
    {
        Label l = new Label();
        l.Text = text;
        l.Font = Ui.Font(8.5f);
        l.ForeColor = Ui.GrayDim;
        l.BackColor = Color.Transparent;
        l.AutoSize = false;
        l.Location = new Point(24, y);
        l.Size = new Size(472, 16);
        return l;
    }

    TextBox AiSetupBox(int hostW, int y)
    {
        TextBox tb = new TextBox();
        tb.Font = Ui.Font(9.5f);
        tb.BackColor = Ui.SurfaceHi;
        tb.ForeColor = Ui.Txt;
        tb.BorderStyle = BorderStyle.None;
        tb.Location = new Point(12, 9);
        tb.Size = new Size(hostW - 24, 20);
        return tb;
    }

    RoundedPanel AiSetupBoxHost(int w, int y, TextBox tb)
    {
        RoundedPanel p = new RoundedPanel();
        p.Radius = 10;
        p.BackColor = Ui.SurfaceHi;
        p.Border = Ui.Line;
        p.Glass = false;
        p.Location = new Point(24, y);
        p.Size = new Size(w, 38);
        p.Controls.Add(tb);
        return p;
    }

    void AiFallback()
    {
        AiBubble("Могу ответить на любой вопрос — просто напиши его. Плюс вот что делаю руками:\n\n" +
                 "• скин — найду в интернете по нику («как у Dream») или популярный и надену\n" +
                 "• плащ — подберу по названию («дракон», «огонь») и надену\n" +
                 "• сборка — найду на Modrinth по названию и установлю\n" +
                 "• память, Java, аккаунт, настройки — открыть и поменять\n\n" +
                 "Спроси словами, я разберу.",
            false,
            new string[] { "Популярный скин", "Плащ дракона", "Better MC", "Настройки" },
            new Action[] {
                delegate { AiSkinWeb(null, true); },
                delegate { AiSend("найди плащ дракона"); },
                delegate { AiSend("поставь better mc"); },
                delegate { HideOverlays(); ShowSettings(); }
            });
        AiStatus("Подсказал, что умею");
    }
}