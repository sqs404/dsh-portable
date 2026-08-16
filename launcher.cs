// DeepSeek Harness 便携版启动器 v2
// 编译: C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /r:System.Windows.Forms.dll /out:"启动 DeepSeek Harness.exe" launcher.cs
// 行为:
//   1) 默认端口 3081；同目录 port.txt（纯数字）可覆盖
//   2) 若端口已被本便携版实例占用 → 直接打开浏览器并退出（不重复启动）
//   3) 设置 DSH_HOME 指向自身 data\（数据全部隔离在便携目录内）
//   4) 启动内置 node.exe 运行 apps\cli\lib\bin.js web --port <端口>
//   5) 轮询端口就绪后再打开浏览器（首次启动自动初始化 profile，等待更久）
//   6) 若 node 在就绪前就退出 → 弹窗显示退出码与手动排查命令
//   7) 等待 node 退出（关闭黑色命令行窗口即停止服务）
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;

static class DshLauncher
{
    const int DEFAULT_PORT = 3081;
    const int READY_TIMEOUT_SECONDS = 90; // 首次启动要初始化 profile，放宽等待

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
            try { Process.Start("http://127.0.0.1:" + port.ToString()); }
            catch { }
            return 0;
        }

        try { Directory.CreateDirectory(dataDir); }
        catch (Exception ex) { Fail("无法创建数据目录: " + ex.Message); return 1; }

        // 关键：把 DSH 用户数据根指向自身 data\，不读写系统用户目录
        Environment.SetEnvironmentVariable("DSH_HOME", dataDir);

        ProcessStartInfo psi = new ProcessStartInfo();
        psi.FileName = nodeExe;
        psi.Arguments = "\"" + entry + "\" web --port " + port.ToString();
        psi.WorkingDirectory = baseDir;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = false; // 双击时给 node 子进程一个可关闭的控制台窗口

        Process p;
        try { p = Process.Start(psi); }
        catch (Exception ex) { Fail("启动 DeepSeek Harness 失败: " + ex.Message); return 1; }

        // 轮询端口就绪（首次启动可能较慢），就绪后打开浏览器
        int waited = 0;
        while (waited < READY_TIMEOUT_SECONDS)
        {
            if (PortOpen("127.0.0.1", port))
            {
                try { Process.Start("http://127.0.0.1:" + port.ToString()); }
                catch { }
                break;
            }
            if (p.HasExited)
            {
                Fail("DeepSeek Harness 启动失败（进程提前退出，退出码 " + p.ExitCode + "）。\n\n"
                    + "请关闭黑色命令行窗口后，在命令行手动执行查看错误详情：\n"
                    + "  node.exe apps\\cli\\lib\\bin.js web --port " + port.ToString());
                return p.ExitCode;
            }
            Thread.Sleep(1000);
            waited++;
        }

        p.WaitForExit();
        return p.ExitCode;
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
