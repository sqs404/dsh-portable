// DeepSeek Harness 便携版启动器 v3
// 编译: C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /r:System.Windows.Forms.dll /out:"启动 DeepSeek Harness.exe" launcher.cs
// 行为:
//   1) 默认端口 3081；同目录 port.txt（纯数字）可覆盖
//   2) 若端口已被本便携版实例占用 → 直接打开浏览器并退出（不重复启动）
//   3) 设置 DSH_HOME 指向自身 data\（数据全部隔离在便携目录内）
//   4) 启动内置 node.exe 运行 node_modules\@deepseek-ai\dsh\lib\bin.js web --port <端口> --no-open
//      （--no-open 关闭新版 dsh 自带的浏览器打开，由本启动器统一负责打开一次）
//   5) 关键：0.1.2-alpha 起 Web 界面启用一次性 token 鉴权，直接访问 http://127.0.0.1:<端口>
//      会返回 401。启动器捕获 dsh 打印的 "dsh web: http://.../?token=xxx" 并打开该地址，
//      由浏览器换取 30 天有效的会话 cookie；老版本没有 token 时回退为打开裸地址。
//   6) node 的 stdout/stderr 同时写入同目录 dsh-console.log，便于排查
//   7) 若 node 在就绪前就退出 → 弹窗显示退出码与手动排查命令
//   8) 等待 node 退出（关闭黑色命令行窗口即停止服务）
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

static class DshLauncher
{
    const int DEFAULT_PORT = 3081;
    const int READY_TIMEOUT_SECONDS = 90;      // 首次启动要初始化 profile，放宽等待
    const int TOKEN_GRACE_SECONDS = 12;        // 端口就绪后再等一会儿 token 行（老版本直接超时回退）

    static readonly object Sync = new object();
    static string tokenUrl = null;             // 捕获到的带 token 的首页地址
    static StreamWriter logWriter = null;

    static int Main()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string nodeExe = Path.Combine(baseDir, "node.exe");
        string entry = Path.Combine(baseDir, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
        string dataDir = Path.Combine(baseDir, "data");
        int port = DEFAULT_PORT;

        if (!File.Exists(nodeExe))
        {
            Fail("未找到 node.exe（请确认它与启动器在同一目录）");
            return 1;
        }
        // 新布局入口：node_modules\@deepseek-ai\dsh\lib\bin.js；兼容旧源码布局 apps\cli\lib\bin.js
        if (!File.Exists(entry))
        {
            // 兼容旧源码布局 apps\cli\lib\bin.js
            string legacy = Path.Combine(baseDir, "apps", "cli", "lib", "bin.js");
            if (File.Exists(legacy)) entry = legacy;
            else
            {
                Fail("未找到 DeepSeek Harness 运行时（node_modules\\@deepseek-ai\\dsh\\lib\\bin.js 缺失）");
                return 1;
            }
        }

        // 可选端口：同目录 port.txt 内容为纯数字端口号
        string portFile = Path.Combine(baseDir, "port.txt");
        if (File.Exists(portFile))
        {
            try
            {
                int parsed;
                if (int.TryParse(File.ReadAllText(portFile).Trim(), out parsed) && parsed > 0 && parsed <= 65535)
                    port = parsed;
            }
            catch { /* 非法 port.txt 用默认端口 */ }
        }

        // 端口已被占用：说明实例已在运行，直接打开浏览器即可
        if (PortOpen("127.0.0.1", port))
        {
            OpenBrowser("http://127.0.0.1:" + port.ToString() + "/");
            return 0;
        }

        try { Directory.CreateDirectory(dataDir); }
        catch (Exception ex) { Fail("无法创建数据目录: " + ex.Message); return 1; }

        // 运行日志（每次启动覆盖，便于排查启动失败）
        try
        {
            logWriter = new StreamWriter(Path.Combine(baseDir, "dsh-console.log"), false, new UTF8Encoding(true));
            logWriter.AutoFlush = true;
        }
        catch { logWriter = null; }

        // 关键：把 DSH 用户数据根指向自身 data\，不读写系统用户目录
        Environment.SetEnvironmentVariable("DSH_HOME", dataDir);

        ProcessStartInfo psi = new ProcessStartInfo();
        psi.FileName = nodeExe;
        // --no-open：新版 dsh 默认会自动打开浏览器，由启动器统一负责"就绪后打开一次"，
        // 这样既避免弹出两个页面，又能拿到 token 地址。
        psi.Arguments = "\"" + entry + "\" web --port " + port.ToString() + " --no-open";
        psi.WorkingDirectory = baseDir;
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.CreateNoWindow = false; // 双击时给 node 子进程一个可关闭的控制台窗口

        Process p;
        try { p = Process.Start(psi); }
        catch (Exception ex) { Log("启动失败: " + ex.Message); Fail("启动 DeepSeek Harness 失败: " + ex.Message); return 1; }

        p.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { OnLine(e.Data); };
        p.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { OnLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        // 轮询 token 地址 / 端口就绪（首次启动可能较慢），就绪后打开浏览器
        int waitedMs = 0;
        int portReadyMs = -1;
        while (waitedMs < READY_TIMEOUT_SECONDS * 1000)
        {
            string url;
            lock (Sync) { url = tokenUrl; }
            if (!string.IsNullOrEmpty(url))
            {
                OpenBrowser(url);
                break;
            }

            if (PortOpen("127.0.0.1", port))
            {
                if (portReadyMs < 0) portReadyMs = waitedMs;
                // 端口已就绪但还没拿到 token（老版本无 token 鉴权），再宽限一会儿
                if (waitedMs - portReadyMs >= TOKEN_GRACE_SECONDS * 1000)
                {
                    OpenBrowser("http://127.0.0.1:" + port.ToString() + "/");
                    break;
                }
            }

            if (p.HasExited)
            {
                Fail("DeepSeek Harness 启动失败（进程提前退出，退出码 " + p.ExitCode + "）。\n\n"
                    + "请查看同目录 dsh-console.log，或在命令行手动执行：\n"
                    + "  node.exe node_modules\\@deepseek-ai\\dsh\\lib\\bin.js web --port " + port.ToString() + " --no-open");
                return p.ExitCode;
            }

            Thread.Sleep(500);
            waitedMs += 500;
        }

        p.WaitForExit();
        return p.ExitCode;
    }

    /// 逐行处理 node 输出：写日志，并捕获带 token 的首页地址。
    static void OnLine(string line)
    {
        if (line == null) return;
        Log(line);

        // dsh web: http://127.0.0.1:3081/?token=xxxx
        Match m = Regex.Match(line, @"https?://[^\s""']*/\?token=[A-Za-z0-9_\-\.]+");
        if (m.Success)
        {
            lock (Sync)
            {
                if (string.IsNullOrEmpty(tokenUrl)) tokenUrl = m.Value;
            }
        }
    }

    static void Log(string message)
    {
        try
        {
            if (logWriter != null) logWriter.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " " + message);
        }
        catch { }
    }

    static void OpenBrowser(string url)
    {
        Log("打开浏览器: " + url);
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo(url);
            psi.UseShellExecute = true;
            Process.Start(psi);
        }
        catch (Exception ex) { Log("打开浏览器失败: " + ex.Message); }
    }

    /// 尝试建立 TCP 连接判断端口是否已就绪。
    static bool PortOpen(string host, int port)
    {
        try
        {
            using (TcpClient client = new TcpClient())
            {
                IAsyncResult ar = client.BeginConnect(host, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(800)) return false;
                client.EndConnect(ar);
                return client.Connected;
            }
        }
        catch { return false; }
    }

    static void Fail(string message)
    {
        Log("失败: " + message);
        try
        {
            System.Windows.Forms.MessageBox.Show(message, "DeepSeek Harness 便携版", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
        }
        catch
        {
            try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "启动失败.log"), message + Environment.NewLine); }
            catch { }
        }
    }
}
