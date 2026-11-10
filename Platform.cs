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

    public static void StartApplyScript(string newPath, string exePath, string dataDir)
    {
        if (IsWin)
        {
            StringBuilder bb = new StringBuilder();
            bb.AppendLine("@echo off");
            bb.AppendLine("timeout /t 3 /nobreak >nul");
            bb.AppendLine(":retry");
            bb.AppendLine("move /Y \"" + newPath + "\" \"" + exePath + "\" >nul 2>&1");
            bb.AppendLine("if errorlevel 1 ( timeout /t 2 /nobreak >nul & goto retry )");
            bb.AppendLine("start \"\" \"" + exePath + "\"");
            bb.AppendLine("(goto) 2>nul & del \"%~f0\"");
            string bat = Path.Combine(dataDir, "update_apply.bat");
            File.WriteAllText(bat, bb.ToString(), Encoding.ASCII);
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "cmd.exe";
            psi.Arguments = "/c \"" + bat + "\"";
            psi.WorkingDirectory = dataDir;
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            Process.Start(psi);
        }
        else
        {
            StringBuilder bb = new StringBuilder();
            bb.AppendLine("#!/bin/sh");
            bb.AppendLine("sleep 3");
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