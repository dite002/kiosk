using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace SikawanLagustaCaller
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new CallerForm());
        }
    }

    // =========================================================================
    // CONFIG HELPER: Membaca file config.ini
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
                    string trimmed = lines[i].Trim();
                    if (trimmed == "[" + section + "]")
                    {
                        sectionFound = true;
                        insertIndex = i + 1;
                        for (int j = i + 1; j < lines.Count; j++)
                        {
                            if (lines[j].Trim().StartsWith("["))
                            {
                                insertIndex = j;
                                break;
                            }
                            insertIndex = j + 1;
                        }
                        break;
                    }
                }
                if (!sectionFound)
                {
                    lines.Add("");
                    lines.Add("[" + section + "]");
                    insertIndex = lines.Count;
                }
                lines.Insert(insertIndex, key + "=" + value);
            }

            File.WriteAllLines(configPath, lines.ToArray());
        }

        /// <summary>
        /// Scan jaringan untuk menemukan host dengan port 3000 terbuka
        /// </summary>
        public static string ScanForServer(int port = 3000, int timeoutMs = 200)
        {
            string baseIp = GetValue("Network", "BaseIP", "");

            if (string.IsNullOrEmpty(baseIp))
            {
                baseIp = DetectBaseIP();
            }

            if (string.IsNullOrEmpty(baseIp)) return "";

            for (int i = 1; i <= 255; i++)
            {
                string testIp = baseIp + "." + i;
                try
                {
                    using (TcpClient client = new TcpClient())
                    {
                        IAsyncResult result = client.BeginConnect(testIp, port, null, null);
                        bool success = result.AsyncWaitHandle.WaitOne(timeoutMs);
                        if (success && client.Connected)
                        {
                            client.EndConnect(result);
                            client.Close();
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

                    IPInterfaceProperties ipProps = ni.GetIPProperties();
                    foreach (UnicastIPAddressInformation addr in ipProps.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            string ip = addr.Address.ToString();
                            string[] parts = ip.Split('.');
                            if (parts.Length == 4)
                            {
                                return parts[0] + "." + parts[1] + "." + parts[2];
                            }
                        }
                    }
                }
            }
            catch { }
            return "";
        }
    }

    // =========================================================================
    // CALLER FORM - Aplikasi Pemanggil Antrian Petugas Loket
    // =========================================================================
    public class CallerForm : Form
    {
        private static readonly string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kiosk.db");
        private static readonly string connectionString = string.Format("Data Source={0};Version=3;", dbPath);

        private TextBox txtServerIp;
        private ComboBox cbCounter;
        private ComboBox cbCategory;
        private Label lblCurrentTicket;
        private Label lblWaitingCount;
        private Label lblConnectionStatus;
        private Button btnCallNext;
        private Button btnRecall;
        private Button btnComplete;
        private Button btnScanServer;
        private Timer timerRefresh;

        private string currentTicketId = null;
        private string currentTicketNumber = "---";

        // Persistent TCP connection
        private TcpClient persistentClient;
        private NetworkStream persistentStream;
        private bool isConnected = false;

        public CallerForm()
        {
            InitializeForm();
            BuildUI();
            StartMonitoring();
        }

        private void InitializeForm()
        {
            this.Text = "Caller App - Petugas Loket";
            this.Size = new Size(480, 620);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(240, 244, 248);

            // Set Icon dari favicon.ico
            string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "favicon.ico");
            if (File.Exists(icoPath))
            {
                try { this.Icon = new Icon(icoPath); } catch { }
            }
        }

        private void BuildUI()
        {
            // Panel IP Server & Pengaturan Loket
            GroupBox gbSetting = new GroupBox
            {
                Text = " Pengaturan Server & Loket ",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(20, 15),
                Size = new Size(425, 155)
            };
            this.Controls.Add(gbSetting);

            Label lblIp = new Label { Text = "IP Kiosk Host:", Location = new Point(15, 30), AutoSize = true, Font = new Font("Segoe UI", 9) };
            gbSetting.Controls.Add(lblIp);

            // Load IP dari config
            string savedIp = ConfigHelper.GetValue("Server", "ServerIP", "127.0.0.1");
            txtServerIp = new TextBox { Text = savedIp, Location = new Point(110, 27), Width = 200 };
            gbSetting.Controls.Add(txtServerIp);

            btnScanServer = new Button
            {
                Text = "Scan",
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                BackColor = Color.FromArgb(108, 117, 125),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(320, 25),
                Size = new Size(85, 28),
                Cursor = Cursors.Hand
            };
            btnScanServer.FlatAppearance.BorderSize = 0;
            btnScanServer.Click += BtnScanServer_Click;
            gbSetting.Controls.Add(btnScanServer);

            Label lbl1 = new Label { Text = "Loket:", Location = new Point(15, 75), AutoSize = true, Font = new Font("Segoe UI", 9) };
            gbSetting.Controls.Add(lbl1);

            cbCounter = new ComboBox { Location = new Point(60, 72), Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
            cbCounter.Items.AddRange(new object[] { "1", "2", "3", "4", "5" });
            cbCounter.SelectedIndex = 0;
            gbSetting.Controls.Add(cbCounter);

            Label lbl2 = new Label { Text = "Layanan:", Location = new Point(170, 75), AutoSize = true, Font = new Font("Segoe UI", 9) };
            gbSetting.Controls.Add(lbl2);

            cbCategory = new ComboBox { Location = new Point(235, 72), Width = 170, DropDownStyle = ComboBoxStyle.DropDownList };
            cbCategory.Items.AddRange(new object[] { "kunjungan", "penitipan" });
            cbCategory.SelectedIndex = 0;
            cbCategory.SelectedIndexChanged += (s, e) => FetchWaitingCount();
            gbSetting.Controls.Add(cbCategory);

            lblConnectionStatus = new Label
            {
                Text = "⚫ Belum terhubung",
                Font = new Font("Segoe UI", 9, FontStyle.Regular),
                ForeColor = Color.FromArgb(180, 60, 60),
                Location = new Point(15, 115),
                AutoSize = true
            };
            gbSetting.Controls.Add(lblConnectionStatus);

            Button btnConnect = new Button
            {
                Text = "Hubungkan",
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 123, 255),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(280, 112),
                Size = new Size(125, 28),
                Cursor = Cursors.Hand
            };
            btnConnect.FlatAppearance.BorderSize = 0;
            btnConnect.Click += (s, e) => ConnectToServer();
            gbSetting.Controls.Add(btnConnect);

            // Tampilan Antrean Sedang Dilayani
            GroupBox gbActive = new GroupBox
            {
                Text = " Antrean Sedang Dilayani ",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(20, 180),
                Size = new Size(425, 170)
            };
            this.Controls.Add(gbActive);

            lblCurrentTicket = new Label
            {
                Text = "---",
                Font = new Font("Segoe UI", 42, FontStyle.Bold),
                ForeColor = Color.FromArgb(20, 30, 55),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(10, 25),
                Size = new Size(405, 80)
            };
            gbActive.Controls.Add(lblCurrentTicket);

            lblWaitingCount = new Label
            {
                Text = "Sisa Antrean Menunggu: 0",
                Font = new Font("Segoe UI", 10, FontStyle.Regular),
                ForeColor = Color.FromArgb(100, 110, 125),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(10, 120),
                Size = new Size(405, 30)
            };
            gbActive.Controls.Add(lblWaitingCount);

            // Tombol Kontrol
            btnCallNext = new Button
            {
                Text = "PANGGIL ANTREAN BERIKUTNYA",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                BackColor = Color.FromArgb(40, 167, 69),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(20, 365),
                Size = new Size(425, 50),
                Cursor = Cursors.Hand
            };
            btnCallNext.FlatAppearance.BorderSize = 0;
            btnCallNext.Click += BtnCallNext_Click;
            this.Controls.Add(btnCallNext);

            btnRecall = new Button
            {
                Text = "PANGGIL ULANG",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = Color.FromArgb(255, 193, 7),
                ForeColor = Color.FromArgb(30, 30, 30),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(20, 430),
                Size = new Size(205, 45),
                Cursor = Cursors.Hand
            };
            btnRecall.FlatAppearance.BorderSize = 0;
            btnRecall.Click += BtnRecall_Click;
            this.Controls.Add(btnRecall);

            btnComplete = new Button
            {
                Text = "SELESAI DILAYANI",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = Color.FromArgb(23, 162, 184),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(240, 430),
                Size = new Size(205, 45),
                Cursor = Cursors.Hand
            };
            btnComplete.FlatAppearance.BorderSize = 0;
            btnComplete.Click += BtnComplete_Click;
            this.Controls.Add(btnComplete);

            // Tombol Laporan
            Button btnReport = new Button
            {
                Text = "📊 BUKA LAPORAN",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                BackColor = Color.FromArgb(111, 66, 193),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(20, 490),
                Size = new Size(425, 40),
                Cursor = Cursors.Hand
            };
            btnReport.FlatAppearance.BorderSize = 0;
            btnReport.Click += (s, e) =>
            {
                try
                {
                    string reportExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ReportApp.exe");
                    if (File.Exists(reportExe))
                    {
                        System.Diagnostics.Process.Start(reportExe);
                    }
                    else
                    {
                        MessageBox.Show("ReportApp.exe tidak ditemukan.\nSilakan compile ReportApp.cs terlebih dahulu.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Gagal membuka Laporan: " + ex.Message);
                }
            };
            this.Controls.Add(btnReport);
        }

        // =====================================================================
        // KONEKSI PERSISTENT KE TCP SERVER
        // =====================================================================
        private void ConnectToServer()
        {
            Thread connectThread = new Thread(() =>
            {
                try
                {
                    if (persistentClient != null && persistentClient.Connected)
                    {
                        try { persistentClient.Close(); } catch { }
                    }

                    string ip = "";
                    this.Invoke((MethodInvoker)delegate { ip = txtServerIp.Text.Trim(); });

                    persistentClient = new TcpClient();
                    persistentClient.Connect(ip, 3000);
                    persistentStream = persistentClient.GetStream();
                    isConnected = true;

                    // Simpan IP ke config
                    ConfigHelper.SetValue("Server", "ServerIP", ip);

                    this.Invoke((MethodInvoker)delegate
                    {
                        lblConnectionStatus.Text = "🟢 Terhubung ke " + ip + ":3000";
                        lblConnectionStatus.ForeColor = Color.FromArgb(40, 167, 69);
                    });
                }
                catch (Exception ex)
                {
                    isConnected = false;
                    this.Invoke((MethodInvoker)delegate
                    {
                        lblConnectionStatus.Text = "🔴 Gagal: " + ex.Message;
                        lblConnectionStatus.ForeColor = Color.FromArgb(220, 53, 69);
                    });
                }
            })
            { IsBackground = true };
            connectThread.Start();
        }

        private void SendSocketMessage(string message)
        {
            try
            {
                // Gunakan persistent connection jika tersedia
                if (isConnected && persistentClient != null && persistentClient.Connected)
                {
                    byte[] data = Encoding.UTF8.GetBytes(message + "\n");
                    persistentStream.Write(data, 0, data.Length);
                    persistentStream.Flush();
                    return;
                }

                // Fallback: koneksi baru (kompatibilitas)
                string ip = txtServerIp.Text.Trim();
                using (TcpClient client = new TcpClient())
                {
                    client.Connect(ip, 3000);
                    byte[] data = Encoding.UTF8.GetBytes(message + "\n");
                    NetworkStream stream = client.GetStream();
                    stream.Write(data, 0, data.Length);
                    stream.Flush();
                }
            }
            catch (Exception ex)
            {
                isConnected = false;
                lblConnectionStatus.Text = "🔴 Terputus";
                lblConnectionStatus.ForeColor = Color.FromArgb(220, 53, 69);
                MessageBox.Show("Gagal terhubung ke Socket Server:\n" + ex.Message, "Koneksi Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnScanServer_Click(object sender, EventArgs e)
        {
            btnScanServer.Enabled = false;
            btnScanServer.Text = "Scanning...";
            lblConnectionStatus.Text = "⏳ Scanning jaringan...";
            lblConnectionStatus.ForeColor = Color.FromArgb(255, 193, 7);

            Thread scanThread = new Thread(() =>
            {
                string foundIp = ConfigHelper.ScanForServer();

                this.Invoke((MethodInvoker)delegate
                {
                    if (!string.IsNullOrEmpty(foundIp))
                    {
                        txtServerIp.Text = foundIp;
                        ConfigHelper.SetValue("Server", "ServerIP", foundIp);
                        lblConnectionStatus.Text = "✅ Ditemukan server di " + foundIp;
                        lblConnectionStatus.ForeColor = Color.FromArgb(40, 167, 69);
                    }
                    else
                    {
                        lblConnectionStatus.Text = "❌ Tidak ditemukan server di jaringan";
                        lblConnectionStatus.ForeColor = Color.FromArgb(220, 53, 69);
                    }
                    btnScanServer.Enabled = true;
                    btnScanServer.Text = "Scan";
                });
            })
            { IsBackground = true };
            scanThread.Start();
        }

        private void StartMonitoring()
        {
            timerRefresh = new Timer { Interval = 2000 };
            timerRefresh.Tick += (s, e) => FetchWaitingCount();
            timerRefresh.Start();
            FetchWaitingCount();
        }

        private void FetchWaitingCount()
        {
            if (!File.Exists(dbPath)) return;

            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    string sql = "SELECT COUNT(*) FROM tickets WHERE category = @cat AND status = 'waiting' AND date(created_at) = date('now', 'localtime')";
                    using (SQLiteCommand cmd = new SQLiteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@cat", cbCategory.SelectedItem.ToString());
                        int count = Convert.ToInt32(cmd.ExecuteScalar());
                        lblWaitingCount.Text = "Sisa Antrean Menunggu: " + count;
                    }
                }
            }
            catch { }
        }

        private void BtnCallNext_Click(object sender, EventArgs e)
        {
            int counterNo = Convert.ToInt32(cbCounter.SelectedItem);
            string category = cbCategory.SelectedItem.ToString();

            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    string sqlGet = @"
                        SELECT id, prefix, number 
                        FROM tickets 
                        WHERE category = @cat AND status = 'waiting' AND date(created_at) = date('now', 'localtime')
                        ORDER BY number ASC LIMIT 1";

                    using (SQLiteCommand cmd = new SQLiteCommand(sqlGet, conn))
                    {
                        cmd.Parameters.AddWithValue("@cat", category);
                        using (SQLiteDataReader reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                currentTicketId = reader["id"].ToString();
                                string prefix = reader["prefix"].ToString();
                                int number = Convert.ToInt32(reader["number"]);
                                currentTicketNumber = prefix + number.ToString("D3");

                                lblCurrentTicket.Text = currentTicketNumber;

                                UpdateTicketStatus(conn, currentTicketId, "called", counterNo);

                                // Kirim perintah CALL via TCP Socket ke Host Kiosk secara instant
                                SendSocketMessage(string.Format("CALL|{0}|{1}", currentTicketId, counterNo));
                            }
                            else
                            {
                                MessageBox.Show("Tidak ada antrean yang sedang menunggu.", "Informasi", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                        }
                    }
                }
                FetchWaitingCount();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error saat memanggil antrean: " + ex.Message);
            }
        }

        private void BtnRecall_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(currentTicketId))
            {
                MessageBox.Show("Belum ada antrean yang dipanggil.", "Peringatan", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int counterNo = Convert.ToInt32(cbCounter.SelectedItem);
            SendSocketMessage(string.Format("CALL|{0}|{1}", currentTicketId, counterNo));
        }

        private void BtnComplete_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(currentTicketId)) return;

            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    // Update status dan set completed_at
                    string sql = "UPDATE tickets SET status = 'completed', counter_number = @counter, completed_at = datetime('now', 'localtime') WHERE id = @id";
                    using (SQLiteCommand cmd = new SQLiteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@counter", Convert.ToInt32(cbCounter.SelectedItem));
                        cmd.Parameters.AddWithValue("@id", currentTicketId);
                        cmd.ExecuteNonQuery();
                    }
                }

                currentTicketId = null;
                currentTicketNumber = "---";
                lblCurrentTicket.Text = "---";
                FetchWaitingCount();
            }
            catch { }
        }

        private void UpdateTicketStatus(SQLiteConnection conn, string ticketId, string status, int counterNo)
        {
            string sql = "UPDATE tickets SET status = @status, counter_number = @counter WHERE id = @id";
            using (SQLiteCommand cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@status", status);
                cmd.Parameters.AddWithValue("@counter", counterNo);
                cmd.Parameters.AddWithValue("@id", ticketId);
                cmd.ExecuteNonQuery();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                if (persistentClient != null) persistentClient.Close();
            }
            catch { }
            base.OnFormClosing(e);
        }
    }
}
