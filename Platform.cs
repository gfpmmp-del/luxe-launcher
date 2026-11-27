// ============================================================
//  Платформа: Windows или Linux/Mono
//
//  Раньше различия были вшиты по месту: «;» в classpath, «\» в
//  путях, «java.exe», «windows» в правилах манифеста. Здесь они
//  собраны в одном месте, а код просто спрашивает Os.IsWin.
//
//  Одно важно: по умолчанию на Windows всё ведёт себя как раньше,
//  байт в байт. Unix-ветки включаются только когда платформа не
//  Windows, либо принудительно переменной LUXE_FAKE_OS=linux —
//  это отладочный режим, чтобы прогнать linux-путь на Windows.
// ============================================================
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

static class Os
{
    public static readonly bool IsWin;
    public static readonly bool IsMac;
    public static readonly bool IsLinux;

    // разделитель пути: "\" или "/"
    public static readonly string Sep;
    // разделитель classpath: ";" или ":"
    public static readonly string CpSep;
    // имя исполняемого файла Java: java.exe или java
    public static readonly string JavaName;
    // имя ОС так, как его ждут правила в манифесте Minecraft
    public static readonly string McOsName;
    // архитектура для тех же правил: x64 / x86 / arm64
    public static readonly string McArch;

    static Os()
    {
        bool forceLinux = false;
        try { forceLinux = Environment.GetEnvironmentVariable("LUXE_FAKE_OS") == "linux"; }
        catch { }

        PlatformID id = PlatformID.Win32NT;
        try { id = Environment.OSVersion.Platform; }
        catch { }

        bool mac = (id == PlatformID.MacOSX);
        // надёжнее всего разделитель: на Windows он «\», на Unix «/»
        bool win = !forceLinux && !mac && Path.DirectorySeparatorChar == '\\';

        IsWin = win;
        IsMac = mac;
        IsLinux = !win && !mac;

        Sep = win ? "\\" : "/";
        CpSep = win ? ";" : ":";
        JavaName = win ? "java.exe" : "java";
        McOsName = win ? "windows" : (mac ? "osx" : "linux");
        McArch = DetectArch();
    }

    static string DetectArch()
    {
        try
        {
            string over = Environment.GetEnvironmentVariable("LUXE_ARCH");
            if (!string.IsNullOrEmpty(over)) return over;
        }
        catch { }
        try
        {
            string pa = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");
            if (!string.IsNullOrEmpty(pa))
            {
                pa = pa.ToLowerInvariant();
                if (pa.IndexOf("arm64") >= 0) return "arm64";
                if (pa.IndexOf("x86") >= 0 && pa.IndexOf("64") < 0) return "x86";
                return "x64";
            }
        }
        catch { }
        // на Unix этой переменной нет; считаем x64 (можно переопределить LUXE_ARCH)
        return "x64";
    }

    // путь из манифеста ("a/b/c.jar") -> путь текущей ОС
    public static string NativePath(string manifestPath)
    {
        if (manifestPath == null) return null;
        return manifestPath.Replace("/", Sep);
    }

    // совпадает ли имя ОС из правила манифеста с нашей
    public static bool OsMatches(string name)
    {
        if (string.IsNullOrEmpty(name)) return true;
        return name == McOsName;
    }

    // совпадает ли архитектура из правила манифеста с нашей
    public static bool ArchMatches(string arch)
    {
        if (string.IsNullOrEmpty(arch)) return true;
        string a = arch.ToLowerInvariant();
        // старые манифесты пишут просто "x86" для 32-бит
        if (a == "x86") return McArch == "x86";
        return a == McArch;
    }

    // Заменить работающий exe новым и перезапустить лаунчер.
    //
    // Раньше здесь писался .bat в ASCII. В пути %APPDATA% или на
    // рабочем столе с кириллицей («Новая папка») байты превращались
    // в «?????», move не находил файл, и обновление «применялось»,
    // но лаунчер оставался старым. Теперь команда для PowerShell
    // кодируется в UTF-16LE/base64: путь доходит без искажений.
    //
    // Скрипт ждёт завершения текущего PID, затем до 45 секунд
    // пытается скопировать новый файл поверх старого (антивирус и
    // индексатор могут держать exe), после чего запускает его.
    public static void StartApplyScript(string newPath, string exePath, string dataDir)
    {
        if (IsWin)
        {
            string log = Cfg.LogPath("update_apply.log");
            int pid = 0;
            try { pid = Process.GetCurrentProcess().Id; } catch { }

            StringBuilder ps = new StringBuilder();
            ps.Append("$ErrorActionPreference='SilentlyContinue';");
            ps.Append("$log='" + PsQuote(log) + "';");
            ps.Append("function L($m){ try { Add-Content -LiteralPath $log -Value ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ' + $m) -Encoding UTF8 } catch {} };");
            ps.Append("L 'apply start pid=" + pid + "';");
            ps.Append("for($i=0;$i -lt 90;$i++){ if(-not (Get-Process -Id " + pid + " -ErrorAction SilentlyContinue)){ break }; Start-Sleep -Milliseconds 500 };");
            ps.Append("L 'launcher exited';");
            ps.Append("$src='" + PsQuote(newPath) + "';");
            ps.Append("$dst='" + PsQuote(exePath) + "';");
            ps.Append("$ok=$false;");
            ps.Append("for($i=0;$i -lt 90;$i++){ try { Copy-Item -LiteralPath $src -Destination $dst -Force -ErrorAction Stop; $ok=$true; L ('copy ok try=' + $i); break } catch { Start-Sleep -Milliseconds 500 } };");
            ps.Append("if(-not $ok){ L 'copy failed'; }");
            ps.Append("try { Start-Process -FilePath $dst -WorkingDirectory (Split-Path -Parent $dst) -ErrorAction Stop; L 'restarted'; } catch { L ('start failed: ' + $_.Exception.Message) };");

            string enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(ps.ToString()));
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "powershell.exe";
            psi.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " + enc;
            psi.WorkingDirectory = dataDir;
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            try
            {
                Process.Start(psi);
                try
                {
                    File.AppendAllText(log,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " start pid=" + pid +
                        " new=" + newPath + " exe=" + exePath + "\r\n", Encoding.UTF8);
                }
                catch { }
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(log,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " powershell failed: " + ex.Message + "\r\n",
                        Encoding.UTF8);
                }
                catch { }
            }
        }
        else
        {
            StringBuilder bb = new StringBuilder();
            bb.AppendLine("#!/bin/sh");
            bb.AppendLine("pid=" + (pidOfSelf()));
            bb.AppendLine("n=0");
            bb.AppendLine("while [ $n -lt 20 ]; do");
            bb.AppendLine("  if ! kill -0 $pid 2>/dev/null; then break; fi");
            bb.AppendLine("  sleep 1; n=$((n+1));");
            bb.AppendLine("done");
            bb.AppendLine("n=0");
            bb.AppendLine("while [ $n -lt 10 ]; do");
            bb.AppendLine("  if mv -f \"" + newPath + "\" \"" + exePath + "\" 2>/dev/null; then break; fi");
            bb.AppendLine("  sleep 2; n=$((n+1));");
            bb.AppendLine("done");
            bb.AppendLine("chmod +x \"" + exePath + "\" 2>/dev/null");
            bb.AppendLine("nohup \"" + exePath + "\" >/dev/null 2>&1 &");
            bb.AppendLine("rm -f \"$0\"");
            string sh = Path.Combine(dataDir, "update_apply.sh");
            File.WriteAllText(sh, bb.ToString(), Encoding.UTF8);
            try { Process.Start("chmod", "+x \"" + sh + "\""); } catch { }
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "/bin/sh";
            psi.Arguments = "\"" + sh + "\"";
            psi.WorkingDirectory = dataDir;
            psi.UseShellExecute = false;
            Process.Start(psi);
        }
    }

    // PID текущего процесса: в PowerShell-ветке он не нужен, но
    // shell-ветке нужен, чтобы дождаться выхода лаунчера.
    static string pidOfSelf()
    {
        try { return Process.GetCurrentProcess().Id.ToString(); }
        catch { return "0"; }
    }

    // Экранирование одинарных кавычек для PowerShell-строки.
    static string PsQuote(string s)
    {
        return (s ?? "").Replace("'", "''");
    }

    public static void OpenFolder(string path)
    {
        try
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            if (IsWin)
                Process.Start("explorer.exe", "\"" + path + "\"");
            else
                Process.Start("xdg-open", "\"" + path + "\"");
        }
        catch { }
    }

    // открыть URL в браузере по умолчанию
    public static void OpenUrl(string url)
    {
        try
        {
            if (IsWin)
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else
                Process.Start("xdg-open", url);
        }
        catch
        {
            try { Process.Start(url); } catch { }
        }
    }
}