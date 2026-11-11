using System;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

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

    public class CallerForm : Form
    {
        private TextBox txtServerIp;
        private ComboBox cbCounter;
        private ComboBox cbCategory;
        private Label lblCurrentTicket;
        private Label lblWaitingCount;
        private Label lblConnectionStatus;
        private Button btnCallNext;
        private Button btnRecall;
        private Button btnComplete;
        private System.Windows.Forms.Timer timerRefresh;

        private TcpClient tcpClient;
        private NetworkStream stream;
        private Thread listenThread;

        private string currentTicketId = null;
        private string currentTicketNumber = "---";

        public CallerForm()
        {
            InitializeForm();
            BuildUI();
            
            // Pindahkan koneksi agar dijalankan secara asinkron setelah Form tampil penuh (mencegah UI Freeze & Force Close)
            this.Load += (s, e) => 
            {
                ConnectToServer();
                StartMonitoring();
            };
        }

        // Helper thread-safety untuk mencegah Exception Cross-thread operation
        private void SafeInvoke(Action action)
        {
            try
            {
                if (this.IsHandleCreated && !this.IsDisposed)
                {
                    this.Invoke((MethodInvoker)delegate { action(); });
                }
            }
            catch { }
        }

        private void InitializeForm()
        {
            this.Text = "Caller App - Petugas Loket (Client Network)";
            this.Size = new Size(480, 580);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(240, 244, 248);
        }

        private void BuildUI()
        {
            // Panel IP Server & Pengaturan Loket
            GroupBox gbSetting = new GroupBox
            {
                Text = " Pengaturan Server & Loket ",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(20, 15),
                Size = new Size(425, 140)
            };
            this.Controls.Add(gbSetting);

            Label lblIp = new Label { Text = "IP Kiosk Host:", Location = new Point(15, 30), AutoSize = true, Font = new Font("Segoe UI", 9) };
            gbSetting.Controls.Add(lblIp);

            txtServerIp = new TextBox { Text = "192.168.3.251", Location = new Point(110, 27), Width = 190 };
            gbSetting.Controls.Add(txtServerIp);

            Button btnConnect = new Button { Text = "Hubungkan", Location = new Point(310, 25), Width = 100, Height = 26 };
            btnConnect.Click += (s, e) => ConnectToServer();
            gbSetting.Controls.Add(btnConnect);

            lblConnectionStatus = new Label
            {
                Text = "● Terputus dari Kiosk Host",
                ForeColor = Color.Red,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(110, 53),
                AutoSize = true
            };
            gbSetting.Controls.Add(lblConnectionStatus);

            Label lbl1 = new Label { Text = "Loket:", Location = new Point(15, 90), AutoSize = true, Font = new Font("Segoe UI", 9) };
            gbSetting.Controls.Add(lbl1);

            cbCounter = new ComboBox { Location = new Point(60, 87), Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
            cbCounter.Items.AddRange(new object[] { "1", "2", "3", "4", "5" });
            cbCounter.SelectedIndex = 0;
            gbSetting.Controls.Add(cbCounter);

            Label lbl2 = new Label { Text = "Layanan:", Location = new Point(170, 90), AutoSize = true, Font = new Font("Segoe UI", 9) };
            gbSetting.Controls.Add(lbl2);

            cbCategory = new ComboBox { Location = new Point(235, 87), Width = 175, DropDownStyle = ComboBoxStyle.DropDownList };
            cbCategory.Items.AddRange(new object[] { "kunjungan", "penitipan" });
            cbCategory.SelectedIndex = 0;
            cbCategory.SelectedIndexChanged += (s, e) => FetchWaitingCount();
            gbSetting.Controls.Add(cbCategory);

            // Tampilan Antrean Sedang Dilayani
            GroupBox gbActive = new GroupBox
            {
                Text = " Antrean Sedang Dilayani ",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(20, 165),
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
                Location = new Point(20, 350),
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
                Location = new Point(20, 415),
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
                Location = new Point(240, 415),
                Size = new Size(205, 45),
                Cursor = Cursors.Hand
            };
            btnComplete.FlatAppearance.BorderSize = 0;
            btnComplete.Click += BtnComplete_Click;
            this.Controls.Add(btnComplete);
        }

        private void ConnectToServer()
        {
            SafeInvoke(() =>
            {
                lblConnectionStatus.Text = "● Menghubungkan...";
                lblConnectionStatus.ForeColor = Color.Orange;
            });

            string serverIp = txtServerIp.Text.Trim();

            // Jalankan percobaan koneksi di Background Thread dengan Timeout agar tidak membekukan UI
            Thread t = new Thread(() =>
            {
                try
                {
                    if (tcpClient != null)
                    {
                        try { tcpClient.Close(); } catch { }
                    }

                    tcpClient = new TcpClient();
                    
                    IAsyncResult ar = tcpClient.BeginConnect(serverIp, 3000, null, null);
                    bool connected = ar.AsyncWaitHandle.WaitOne(2500, true);

                    if (connected && tcpClient.Connected)
                    {
                        tcpClient.EndConnect(ar);
                        stream = tcpClient.GetStream();

                        SafeInvoke(() =>
                        {
                            lblConnectionStatus.Text = "● Terhubung ke " + serverIp + ":3000";
                            lblConnectionStatus.ForeColor = Color.Green;
                        });

                        listenThread = new Thread(ListenServerMessages) { IsBackground = true };
                        listenThread.Start();
                        FetchWaitingCount();
                    }
                    else
                    {
                        tcpClient.Close();
                        throw new Exception("Connection Timeout");
                    }
                }
                catch
                {
                    SafeInvoke(() =>
                    {
                        lblConnectionStatus.Text = "● Terputus dari Kiosk Host";
                        lblConnectionStatus.ForeColor = Color.Red;
                    });
                }
            }) { IsBackground = true };
            t.Start();
        }

        private void ListenServerMessages()
        {
            byte[] buffer = new byte[4096];
            while (tcpClient != null && tcpClient.Connected)
            {
                try
                {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    string data = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    string[] lines = data.Split('\n');
                    foreach (string line in lines)
                    {
                        string msg = line.Trim();
                        if (string.IsNullOrEmpty(msg)) continue;

                        string[] parts = msg.Split('|');
                        if (parts.Length >= 3 && parts[0] == "WAITING_COUNT")
                        {
                            string cat = parts[1];
                            int count = Convert.ToInt32(parts[2]);
                            SafeInvoke(() =>
                            {
                                if (cbCategory.SelectedItem != null && cbCategory.SelectedItem.ToString().Equals(cat, StringComparison.OrdinalIgnoreCase))
                                {
                                    lblWaitingCount.Text = "Sisa Antrean Menunggu: " + count;
                                }
                            });
                        }
                        else if (parts.Length >= 6 && parts[0] == "ANNOUNCE")
                        {
                            string tId = parts[1];
                            string prefix = parts[2];
                            int num = Convert.ToInt32(parts[3]);
                            int cnt = Convert.ToInt32(parts[4]);

                            SafeInvoke(() =>
                            {
                                if (cbCounter.SelectedItem != null && cbCounter.SelectedItem.ToString() == cnt.ToString())
                                {
                                    currentTicketId = tId;
                                    currentTicketNumber = prefix + num.ToString("D3");
                                    lblCurrentTicket.Text = currentTicketNumber;
                                }
                                FetchWaitingCount();
                            });
                        }
                    }
                }
                catch
                {
                    SafeInvoke(() =>
                    {
                        lblConnectionStatus.Text = "● Terputus dari Kiosk Host";
                        lblConnectionStatus.ForeColor = Color.Red;
                    });
                    break;
                }
            }
        }

        private void SendSocketMessage(string message)
        {
            try
            {
                if (tcpClient != null && tcpClient.Connected && stream != null)
                {
                    byte[] data = Encoding.UTF8.GetBytes(message + "\n");
                    stream.Write(data, 0, data.Length);
                    stream.Flush();
                }
            }
            catch { }
        }

        private void StartMonitoring()
        {
            timerRefresh = new System.Windows.Forms.Timer { Interval = 3000 };
            timerRefresh.Tick += (s, e) => FetchWaitingCount();
            timerRefresh.Start();
        }

        private void FetchWaitingCount()
        {
            SafeInvoke(() =>
            {
                if (cbCategory.SelectedItem != null)
                {
                    SendSocketMessage("GET_WAITING|" + cbCategory.SelectedItem.ToString());
                }
            });
        }

        private void BtnCallNext_Click(object sender, EventArgs e)
        {
            if (cbCounter.SelectedItem == null || cbCategory.SelectedItem == null) return;
            int counterNo = Convert.ToInt32(cbCounter.SelectedItem);
            string category = cbCategory.SelectedItem.ToString();
            SendSocketMessage(string.Format("GET_NEXT|{0}|{1}", category, counterNo));
        }

        private void BtnRecall_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(currentTicketId))
            {
                MessageBox.Show("Belum ada antrean yang dipanggil.", "Peringatan", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (cbCounter.SelectedItem == null) return;
            int counterNo = Convert.ToInt32(cbCounter.SelectedItem);
            SendSocketMessage(string.Format("CALL|{0}|{1}", currentTicketId, counterNo));
        }

        private void BtnComplete_Click(object sender, EventArgs e)
        {
            currentTicketId = null;
            currentTicketNumber = "---";
            lblCurrentTicket.Text = "---";
            FetchWaitingCount();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                if (timerRefresh != null) timerRefresh.Stop();
                if (tcpClient != null) tcpClient.Close();
            }
            catch { }
            base.OnFormClosing(e);
        }
    }
}