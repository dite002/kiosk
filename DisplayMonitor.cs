using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace SikawanLagustaDisplay
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new DisplayForm());
        }
    }

    // =========================================================================
    // CONFIG HELPER
    // =========================================================================
    public static class ConfigHelper
    {
        private static readonly string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");

        public static string GetValue(string section, string key, string defaultValue = "")
        {
            if (!File.Exists(configPath)) return defaultValue;
            string currentSection = "";
            foreach (string line in File.ReadAllLines(configPath))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith(";") || trimmed.StartsWith("#") || string.IsNullOrEmpty(trimmed)) continue;
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    currentSection = trimmed.Substring(1, trimmed.Length - 2);
                    continue;
                }
                if (currentSection.Equals(section, StringComparison.OrdinalIgnoreCase))
                {
                    int eqIndex = trimmed.IndexOf('=');
                    if (eqIndex > 0)
                    {
                        string k = trimmed.Substring(0, eqIndex).Trim();
                        string v = trimmed.Substring(eqIndex + 1).Trim();
                        if (k.Equals(key, StringComparison.OrdinalIgnoreCase))
                            return v;
                    }
                }
            }
            return defaultValue;
        }

        public static void SetValue(string section, string key, string value)
        {
            List<string> lines = new List<string>();
            if (File.Exists(configPath))
                lines = new List<string>(File.ReadAllLines(configPath));
            string currentSection = "";
            bool found = false;
            for (int i = 0; i < lines.Count; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    currentSection = trimmed.Substring(1, trimmed.Length - 2);
                    continue;
                }
                if (currentSection.Equals(section, StringComparison.OrdinalIgnoreCase))
                {
                    int eqIndex = trimmed.IndexOf('=');
                    if (eqIndex > 0)
                    {
                        string k = trimmed.Substring(0, eqIndex).Trim();
                        if (k.Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            lines[i] = key + "=" + value;
                            found = true;
                            break;
                        }
                    }
                }
            }
            if (!found)
            {
                bool sectionFound = false;
                int insertIndex = lines.Count;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].Trim() == "[" + section + "]")
                    {
                        sectionFound = true;
                        insertIndex = i + 1;
                        for (int j = i + 1; j < lines.Count; j++)
                        {
                            if (lines[j].Trim().StartsWith("[")) { insertIndex = j; break; }
                            insertIndex = j + 1;
                        }
                        break;
                    }
                }
                if (!sectionFound) { lines.Add(""); lines.Add("[" + section + "]"); insertIndex = lines.Count; }
                lines.Insert(insertIndex, key + "=" + value);
            }
            File.WriteAllLines(configPath, lines.ToArray());
        }

        public static string ScanForServer(int port = 3000, int timeoutMs = 200)
        {
            string baseIp = GetValue("Network", "BaseIP", "");
            if (string.IsNullOrEmpty(baseIp)) baseIp = DetectBaseIP();
            if (string.IsNullOrEmpty(baseIp)) return "";
            for (int i = 1; i <= 255; i++)
            {
                string testIp = baseIp + "." + i;
                try
                {
                    using (TcpClient client = new TcpClient())
                    {
                        IAsyncResult result = client.BeginConnect(testIp, port, null, null);
                        if (result.AsyncWaitHandle.WaitOne(timeoutMs) && client.Connected)
                        {
                            client.EndConnect(result);
                            return testIp;
                        }
                    }
                }
                catch { }
            }
            return "";
        }

        public static string DetectBaseIP()
        {
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (UnicastIPAddressInformation addr in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            string[] p = addr.Address.ToString().Split('.');
                            if (p.Length == 4) return p[0] + "." + p[1] + "." + p[2];
                        }
                    }
                }
            }
            catch { }
            return "";
        }
    }

    // =========================================================================
    // MP3 PLAYER - Menggunakan Windows Media Player COM (late-binding)
    // Dijamin work di background thread, tidak butuh message pump.
    // =========================================================================
    public static class Mp3Player
    {
        /// <summary>
        /// Play file MP3 secara blocking (tunggu sampai selesai).
        /// Menggunakan WMP COM object via late-binding reflection.
        /// </summary>
        public static void PlayFileBlocking(string filePath)
        {
            if (!File.Exists(filePath)) return;

            object wmpPlayer = null;
            try
            {
                // Buat instance Windows Media Player COM: WMPlayer.OCX
                Type wmpType = Type.GetTypeFromProgID("WMPlayer.OCX");
                if (wmpType == null) return;

                wmpPlayer = Activator.CreateInstance(wmpType);
                if (wmpPlayer == null) return;

                // settings.autoStart = false (kita kontrol sendiri)
                object settings = wmpPlayer.GetType().InvokeMember("settings",
                    System.Reflection.BindingFlags.GetProperty, null, wmpPlayer, null);
                settings.GetType().InvokeMember("autoStart",
                    System.Reflection.BindingFlags.SetProperty, null, settings, new object[] { false });

                // Set volume (0-100 untuk WMP COM)
                string volStr = ConfigHelper.GetValue("Audio", "Volume", "1000");
                int volConfig = 100;
                int parsed;
                if (int.TryParse(volStr, out parsed))
                {
                    volConfig = Math.Min(100, Math.Max(0, parsed / 10)); // 0-1000 -> 0-100
                }
                settings.GetType().InvokeMember("volume",
                    System.Reflection.BindingFlags.SetProperty, null, settings, new object[] { volConfig });

                // Set URL (load file)
                wmpPlayer.GetType().InvokeMember("URL",
                    System.Reflection.BindingFlags.SetProperty, null, wmpPlayer, new object[] { filePath });

                // Tunggu sampai media siap (status berubah dari 0/idle)
                // playState: 0=Undefined, 1=Stopped, 2=Paused, 3=Playing, 6=Buffering, 9=Connecting, 10=Ready
                Thread.Sleep(100); // Beri waktu load

                // Controls.play()
                object controls = wmpPlayer.GetType().InvokeMember("controls",
                    System.Reflection.BindingFlags.GetProperty, null, wmpPlayer, null);
                controls.GetType().InvokeMember("play",
                    System.Reflection.BindingFlags.InvokeMethod, null, controls, null);

                // Polling playState sampai selesai (1=Stopped berarti selesai)
                int maxWait = 15000; // 15 detik timeout
                int elapsed = 0;
                bool hasStartedPlaying = false;

                while (elapsed < maxWait)
                {
                    Thread.Sleep(50);
                    elapsed += 50;

                    try
                    {
                        object stateObj = wmpPlayer.GetType().InvokeMember("playState",
                            System.Reflection.BindingFlags.GetProperty, null, wmpPlayer, null);
                        int state = Convert.ToInt32(stateObj);

                        if (state == 3) // Playing
                        {
                            hasStartedPlaying = true;
                        }
                        else if (hasStartedPlaying && (state == 1 || state == 0)) // Stopped atau Undefined setelah playing
                        {
                            break;
                        }
                    }
                    catch { break; }
                }
            }
            catch { }
            finally
            {
                // Cleanup: close WMP COM object
                if (wmpPlayer != null)
                {
                    try
                    {
                        wmpPlayer.GetType().InvokeMember("close",
                            System.Reflection.BindingFlags.InvokeMethod, null, wmpPlayer, null);
                    }
                    catch { }
                    try { Marshal.ReleaseComObject(wmpPlayer); } catch { }
                }
            }
        }
    }

    // =========================================================================
    // DISPLAY FORM UTAMA
    // =========================================================================
    public class DisplayForm : Form
    {
        private static readonly string audioFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "audio");
        private static readonly string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kiosk.db");
        private static readonly string connectionString = string.Format("Data Source={0};Version=3;Read Only=True;", dbPath);

        private Label lblClock;
        private Label lblDate;
        private Label lblKunjunganNumber;
        private Label lblKunjunganCounter;
        private Label lblPenitipanNumber;
        private Label lblPenitipanCounter;
        private Label lblServerStatus;
        private Timer timerClock;
        private Panel videoPanelHost;

        // Socket Client
        private TcpClient tcpClient;
        private NetworkStream networkStream;
        private Thread listenThread;
        private string serverIp;

        // Audio queue
        private readonly Queue<string[]> audioQueue = new Queue<string[]>();
        private bool isPlayingAudio = false;
        private readonly object audioLock = new object();

        // TCP buffer
        private string tcpMessageBuffer = "";

        public DisplayForm()
        {
            serverIp = ConfigHelper.GetValue("Server", "ServerIP", "127.0.0.1");
            InitializeSettings();
            BuildUI();
            LoadLastQueueFromDatabase();
            StartClockTimer();
            ConnectToSocketServer();
        }

        private void InitializeSettings()
        {
            this.Text = "Display Monitor - SIKAWAN LAGUSTA";
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.BackColor = Color.FromArgb(19, 24, 38);
            this.DoubleBuffered = true;

            string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "favicon.ico");
            if (File.Exists(icoPath))
            {
                try { this.Icon = new Icon(icoPath); } catch { }
            }

            this.KeyPreview = true;
            this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Application.Exit(); };
        }

        private void LoadLastQueueFromDatabase()
        {
            if (!File.Exists(dbPath)) return;
            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    LoadLastCalled(conn, "kunjungan", lblKunjunganNumber, lblKunjunganCounter);
                    LoadLastCalled(conn, "penitipan", lblPenitipanNumber, lblPenitipanCounter);
                }
            }
            catch { }
        }

        private void LoadLastCalled(SQLiteConnection conn, string category, Label lblNum, Label lblCnt)
        {
            string sql = @"SELECT prefix, number, counter_number FROM tickets 
                           WHERE category = @cat AND status = 'called' AND date(created_at) = date('now', 'localtime')
                           ORDER BY number DESC LIMIT 1";
            using (SQLiteCommand cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@cat", category);
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        lblNum.Text = reader["prefix"].ToString() + Convert.ToInt32(reader["number"]).ToString("D3");
                        object cn = reader["counter_number"];
                        if (cn != null && cn != DBNull.Value) lblCnt.Text = "LOKET " + cn.ToString();
                    }
                }
            }
        }

        private void BuildUI()
        {
            // HEADER
            Panel headerPanel = new Panel { Dock = DockStyle.Top, Height = 80, BackColor = Color.FromArgb(15, 20, 32) };
            this.Controls.Add(headerPanel);

            PictureBox picLogo = new PictureBox { Size = new Size(60, 60), Location = new Point(20, 10), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
            string logoIcoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "favicon.ico");
            if (File.Exists(logoIcoPath))
            {
                try { picLogo.Image = new Icon(logoIcoPath, 60, 60).ToBitmap(); }
                catch { picLogo.Paint += DrawEmblemLogo; }
            }
            else { picLogo.Paint += DrawEmblemLogo; }
            headerPanel.Controls.Add(picLogo);

            headerPanel.Controls.Add(new Label
            {
                Text = "SISTEM INFORMASI PENDAFTARAN KUNJUNGAN WARGA BINAAN LAPAS KELAS I MEDAN",
                Font = new Font("Segoe UI", 13, FontStyle.Bold), ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleLeft, Location = new Point(90, 0),
                Size = new Size(Screen.PrimaryScreen.Bounds.Width - 300, 80)
            });

            lblClock = new Label { Font = new Font("Segoe UI", 16, FontStyle.Bold), ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleRight, Size = new Size(200, 30),
                Location = new Point(Screen.PrimaryScreen.Bounds.Width - 220, 12) };
            headerPanel.Controls.Add(lblClock);

            lblDate = new Label { Font = new Font("Segoe UI", 10), ForeColor = Color.FromArgb(180, 190, 205),
                TextAlign = ContentAlignment.MiddleRight, Size = new Size(200, 25),
                Location = new Point(Screen.PrimaryScreen.Bounds.Width - 220, 42) };
            headerPanel.Controls.Add(lblDate);

            // FOOTER
            Panel footerPanel = new Panel { Dock = DockStyle.Bottom, Height = 35, BackColor = Color.FromArgb(10, 14, 24) };
            this.Controls.Add(footerPanel);
            footerPanel.Controls.Add(new Label { Text = "© " + DateTime.Now.Year + " - SIKAWAN LAGUSTA",
                Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = Color.FromArgb(160, 175, 200),
                TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill });

            lblServerStatus = new Label { Text = "Server: Connecting...", Font = new Font("Segoe UI", 8),
                ForeColor = Color.FromArgb(120, 140, 160), TextAlign = ContentAlignment.MiddleRight,
                Dock = DockStyle.Right, AutoSize = true };
            footerPanel.Controls.Add(lblServerStatus);

            // MAIN
            Panel mainPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(235, 240, 248) };
            this.Controls.Add(mainPanel);
            mainPanel.BringToFront(); headerPanel.BringToFront(); footerPanel.BringToFront();

            int sw = Screen.PrimaryScreen.Bounds.Width;
            int sh = Screen.PrimaryScreen.Bounds.Height - 115;
            int leftW = (int)(sw * 0.60);
            int rightW = sw - leftW - 40;

            // LEFT: VIDEO + INFO
            Panel leftPanel = new Panel { Location = new Point(20, 20), Size = new Size(leftW, sh - 40), BackColor = Color.Transparent };
            mainPanel.Controls.Add(leftPanel);

            videoPanelHost = new Panel { Dock = DockStyle.Top, Height = (int)(leftPanel.Height * 0.72), BackColor = Color.FromArgb(30, 40, 60) };
            leftPanel.Controls.Add(videoPanelHost);
            InitializeVideoPlayer();

            Panel infoBox = new Panel { Dock = DockStyle.Bottom, Height = leftPanel.Height - videoPanelHost.Height - 15, BackColor = Color.FromArgb(19, 24, 38) };
            leftPanel.Controls.Add(infoBox);
            infoBox.Controls.Add(new Label { Text = "INFORMASI", Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.FromArgb(245, 185, 40), TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Top, Height = 30 });
            infoBox.Controls.Add(new Label
            {
                Text = "• Harap tetap tenang dan tertib saat menunggu antrean\n• Perhatikan nomor antrean yang tampil pada layar\n• Nomor antrean akan dipanggil melalui pengeras suara",
                Font = new Font("Segoe UI", 11), ForeColor = Color.White, Padding = new Padding(20, 10, 20, 10), Dock = DockStyle.Fill
            });

            // RIGHT: QUEUE CARDS
            Panel rightPanel = new Panel { Location = new Point(leftW + 30, 20), Size = new Size(rightW, sh - 40), BackColor = Color.Transparent };
            mainPanel.Controls.Add(rightPanel);

            int cardH = (rightPanel.Height - 20) / 2;

            Panel cardKunjungan = CreateQueueCard("KUNJUNGAN", Color.FromArgb(41, 182, 216), "A000", "LOKET 1", out lblKunjunganNumber, out lblKunjunganCounter);
            cardKunjungan.Dock = DockStyle.Top; cardKunjungan.Height = cardH;
            rightPanel.Controls.Add(cardKunjungan);

            Panel cardPenitipan = CreateQueueCard("PENITIPAN BARANG", Color.FromArgb(136, 108, 179), "B000", "LOKET 2", out lblPenitipanNumber, out lblPenitipanCounter);
            cardPenitipan.Dock = DockStyle.Bottom; cardPenitipan.Height = cardH;
            rightPanel.Controls.Add(cardPenitipan);
        }

        private void InitializeVideoPlayer()
        {
            string videoPath = ConfigHelper.GetValue("Display", "VideoPath", @"video\video.mp4");
            if (!Path.IsPathRooted(videoPath))
                videoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, videoPath);

            if (!File.Exists(videoPath))
            {
                videoPanelHost.Controls.Add(new Label
                {
                    Text = "Sapa Kasih Kalapas I Medan", Font = new Font("Segoe UI", 18, FontStyle.Bold),
                    ForeColor = Color.FromArgb(245, 185, 40), BackColor = Color.FromArgb(25, 45, 85),
                    TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill
                });
                return;
            }

            try
            {
                var wmp = new WMPVideoPanel();
                wmp.Dock = DockStyle.Fill;
                videoPanelHost.Controls.Add(wmp);
                this.Load += (s, e) => { try { wmp.SetVideo(videoPath, true); } catch { } };
            }
            catch
            {
                videoPanelHost.Controls.Add(new Label
                {
                    Text = "Sapa Kasih Kalapas I Medan", Font = new Font("Segoe UI", 18, FontStyle.Bold),
                    ForeColor = Color.FromArgb(245, 185, 40), BackColor = Color.FromArgb(25, 45, 85),
                    TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill
                });
            }
        }

        private Panel CreateQueueCard(string title, Color color, string defNum, string defCounter,
            out Label numLabel, out Label counterLabel)
        {
            Panel card = new Panel { BackColor = Color.White };
            card.Paint += (s, e) =>
            {
                ControlPaint.DrawBorder(e.Graphics, card.ClientRectangle,
                    Color.FromArgb(200, 210, 225), 2, ButtonBorderStyle.Solid,
                    Color.FromArgb(200, 210, 225), 2, ButtonBorderStyle.Solid,
                    Color.FromArgb(200, 210, 225), 2, ButtonBorderStyle.Solid,
                    Color.FromArgb(200, 210, 225), 2, ButtonBorderStyle.Solid);
            };

            // Z-order: Fill first, then Bottom, then Top (last added = processed first for docking)
            Label lblNum = new Label { Text = defNum, Font = new Font("Segoe UI", 56, FontStyle.Bold),
                ForeColor = Color.FromArgb(20, 25, 35), TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill };
            card.Controls.Add(lblNum);
            numLabel = lblNum;

            Panel pnlCounter = new Panel { Dock = DockStyle.Bottom, Height = 55, BackColor = Color.White };
            card.Controls.Add(pnlCounter);
            Label lblCnt = new Label { Text = defCounter, Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.White, BackColor = Color.FromArgb(25, 35, 55),
                TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill };
            pnlCounter.Controls.Add(lblCnt);
            counterLabel = lblCnt;

            Label lblHeader = new Label { Text = title, Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = Color.White, BackColor = color, TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Top, Height = 45 };
            card.Controls.Add(lblHeader);

            return card;
        }

        // =====================================================================
        // SOCKET CONNECTION
        // =====================================================================
        private void ConnectToSocketServer()
        {
            Thread connectThread = new Thread(() =>
            {
                bool autoScan = ConfigHelper.GetValue("Network", "AutoScan", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
                if (autoScan && (serverIp == "127.0.0.1" || string.IsNullOrEmpty(serverIp)))
                {
                    try { this.Invoke((MethodInvoker)delegate { lblServerStatus.Text = "Server: Scanning..."; }); } catch { }
                    string found = ConfigHelper.ScanForServer();
                    if (!string.IsNullOrEmpty(found)) { serverIp = found; ConfigHelper.SetValue("Server", "ServerIP", found); }
                }
                while (true)
                {
                    try
                    {
                        if (tcpClient == null || !tcpClient.Connected)
                        {
                            tcpClient = new TcpClient();
                            tcpClient.Connect(serverIp, 3000);
                            networkStream = tcpClient.GetStream();
                            try { this.Invoke((MethodInvoker)delegate { lblServerStatus.Text = "Server: " + serverIp + ":3000 (Connected)"; lblServerStatus.ForeColor = Color.FromArgb(80, 200, 120); }); } catch { }
                            tcpMessageBuffer = "";
                            listenThread = new Thread(ListenSocketMessages) { IsBackground = true };
                            listenThread.Start();
                        }
                    }
                    catch
                    {
                        try { this.Invoke((MethodInvoker)delegate { lblServerStatus.Text = "Server: " + serverIp + " (Reconnecting...)"; lblServerStatus.ForeColor = Color.FromArgb(255, 100, 100); }); } catch { }
                    }
                    Thread.Sleep(3000);
                }
            }) { IsBackground = true };
            connectThread.Start();
        }

        private void ListenSocketMessages()
        {
            byte[] buffer = new byte[4096];
            while (tcpClient != null && tcpClient.Connected)
            {
                try
                {
                    int bytesRead = networkStream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;
                    tcpMessageBuffer += Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    while (tcpMessageBuffer.Contains("\n"))
                    {
                        int nl = tcpMessageBuffer.IndexOf('\n');
                        string msg = tcpMessageBuffer.Substring(0, nl).Trim('\r');
                        tcpMessageBuffer = tcpMessageBuffer.Substring(nl + 1);
                        if (!string.IsNullOrEmpty(msg)) ProcessMessage(msg);
                    }

                    // Fallback tanpa newline
                    if (!string.IsNullOrEmpty(tcpMessageBuffer) && tcpMessageBuffer.Contains("|"))
                    {
                        string[] tp = tcpMessageBuffer.Split('|');
                        if ((tp.Length >= 6 && tp[0] == "ANNOUNCE") || (tp.Length >= 4 && tp[0] == "SYNC"))
                        {
                            ProcessMessage(tcpMessageBuffer.Trim());
                            tcpMessageBuffer = "";
                        }
                    }
                }
                catch { break; }
            }
        }

        private void ProcessMessage(string msg)
        {
            string[] parts = msg.Split('|');

            if (parts.Length >= 6 && parts[0] == "ANNOUNCE")
            {
                string prefix = parts[2].Trim();
                int number = Convert.ToInt32(parts[3].Trim());
                int counterNo = Convert.ToInt32(parts[4].Trim());
                string category = parts[5].Trim();
                string formatted = prefix + number.ToString("D3");

                try
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        if (category.Equals("kunjungan", StringComparison.OrdinalIgnoreCase))
                        { lblKunjunganNumber.Text = formatted; lblKunjunganCounter.Text = "LOKET " + counterNo; FlashCard(lblKunjunganNumber); }
                        else
                        { lblPenitipanNumber.Text = formatted; lblPenitipanCounter.Text = "LOKET " + counterNo; FlashCard(lblPenitipanNumber); }
                    });
                }
                catch { }

                EnqueueVoiceAnnouncement(prefix, number, counterNo);
            }
            else if (parts.Length >= 4 && parts[0] == "SYNC")
            {
                string category = parts[1].Trim();
                string prefix = parts[2].Trim();
                int lastNum = Convert.ToInt32(parts[3].Trim());
                string formatted = prefix + lastNum.ToString("D3");
                try
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        if (category.Equals("kunjungan", StringComparison.OrdinalIgnoreCase))
                            lblKunjunganNumber.Text = formatted;
                        else
                            lblPenitipanNumber.Text = formatted;
                    });
                }
                catch { }
            }
        }

        private void FlashCard(Label lbl)
        {
            Color orig = lbl.ForeColor;
            Timer ft = new Timer { Interval = 300 };
            int fc = 0;
            ft.Tick += (s, e) =>
            {
                fc++;
                lbl.ForeColor = (fc % 2 == 0) ? orig : Color.Red;
                if (fc >= 8) { ft.Stop(); ft.Dispose(); lbl.ForeColor = orig; }
            };
            ft.Start();
        }

        // =====================================================================
        // AUDIO - menggunakan WMP COM (Mp3Player class)
        // =====================================================================
        private void EnqueueVoiceAnnouncement(string prefix, int number, int counterNo)
        {
            List<string> files = new List<string>();
            files.Add("nomor.mp3");
            files.Add("antrian.mp3");
            files.Add(prefix.ToLower() + ".mp3");
            foreach (char d in number.ToString("D3")) files.Add(d + ".mp3");
            files.Add("silakan.mp3");
            files.Add("menuju.mp3");
            files.Add("ke.mp3");
            files.Add("loket.mp3");
            files.Add(counterNo.ToString() + ".mp3");

            lock (audioLock)
            {
                audioQueue.Enqueue(files.ToArray());
                if (!isPlayingAudio)
                {
                    isPlayingAudio = true;
                    Thread t = new Thread(ProcessAudioQueue) { IsBackground = true };
                    t.SetApartmentState(ApartmentState.STA); // COM membutuhkan STA
                    t.Start();
                }
            }
        }

        private void ProcessAudioQueue()
        {
            while (true)
            {
                string[] filesToPlay;
                lock (audioLock)
                {
                    if (audioQueue.Count == 0) { isPlayingAudio = false; return; }
                    filesToPlay = audioQueue.Dequeue();
                }

                foreach (string file in filesToPlay)
                {
                    string fullPath = Path.Combine(audioFolder, file);
                    Mp3Player.PlayFileBlocking(fullPath);
                }

                Thread.Sleep(500);
            }
        }

        private void StartClockTimer()
        {
            timerClock = new Timer { Interval = 1000 };
            timerClock.Tick += (s, e) =>
            {
                lblClock.Text = DateTime.Now.ToString("HH:mm:ss");
                lblDate.Text = DateTime.Now.ToString("dd MMMM yyyy");
            };
            timerClock.Start();
        }

        private void DrawEmblemLogo(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen p = new Pen(Color.FromArgb(245, 185, 40), 2)) { g.DrawEllipse(p, 2, 2, 55, 55); }
            using (SolidBrush b = new SolidBrush(Color.FromArgb(245, 185, 40))) { g.FillEllipse(b, 8, 8, 43, 43); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { if (tcpClient != null) tcpClient.Close(); } catch { }
            base.OnFormClosing(e);
        }
    }

    // =========================================================================
    // WMP VIDEO PANEL - AxHost wrapper (hanya untuk VIDEO, MUTED)
    // =========================================================================
    public class WMPVideoPanel : AxHost
    {
        public WMPVideoPanel() : base("6BF52A52-394A-11D3-B153-00C04F79FAA6") { }
        private object wmpObject;

        protected override void AttachInterfaces()
        {
            try { wmpObject = this.GetOcx(); } catch { }
        }

        public void SetVideo(string filePath, bool mute)
        {
            if (wmpObject == null) return;
            try
            {
                Type t = wmpObject.GetType();
                object settings = t.InvokeMember("settings", System.Reflection.BindingFlags.GetProperty, null, wmpObject, null);
                settings.GetType().InvokeMember("autoStart", System.Reflection.BindingFlags.SetProperty, null, settings, new object[] { true });
                settings.GetType().InvokeMember("setMode", System.Reflection.BindingFlags.InvokeMethod, null, settings, new object[] { "loop", true });
                if (mute)
                {
                    settings.GetType().InvokeMember("mute", System.Reflection.BindingFlags.SetProperty, null, settings, new object[] { true });
                    settings.GetType().InvokeMember("volume", System.Reflection.BindingFlags.SetProperty, null, settings, new object[] { 0 });
                }
                t.InvokeMember("uiMode", System.Reflection.BindingFlags.SetProperty, null, wmpObject, new object[] { "none" });
                t.InvokeMember("stretchToFit", System.Reflection.BindingFlags.SetProperty, null, wmpObject, new object[] { true });
                t.InvokeMember("URL", System.Reflection.BindingFlags.SetProperty, null, wmpObject, new object[] { filePath });
            }
            catch { }
        }
    }
}
