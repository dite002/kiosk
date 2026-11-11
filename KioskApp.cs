using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SikawanLagustaKiosk
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            // Auto Extract & Load SQLite Interop DLL (Native C++) & Managed Assembly
            EnsureSQLiteLibraries();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new KioskForm());
        }

        private static void EnsureSQLiteLibraries()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string x64Dir = Path.Combine(baseDir, "x64");
                string x86Dir = Path.Combine(baseDir, "x86");

                Directory.CreateDirectory(x64Dir);
                Directory.CreateDirectory(x86Dir);

                // Ekstrak SQLite.Interop.dll ke folder x64 dan x86 jika belum ada
                ExtractResourceToFile("SQLite.Interop.dll", Path.Combine(x64Dir, "SQLite.Interop.dll"));
                ExtractResourceToFile("SQLite.Interop.dll", Path.Combine(x86Dir, "SQLite.Interop.dll"));
                ExtractResourceToFile("SQLite.Interop.dll", Path.Combine(baseDir, "SQLite.Interop.dll"));
                ExtractResourceToFile("System.Data.SQLite.dll", Path.Combine(baseDir, "System.Data.SQLite.dll"));

                // Dynamic Assembly Resolve untuk System.Data.SQLite
                AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
                {
                    string asmName = new System.Reflection.AssemblyName(args.Name).Name;
                    if (asmName.Equals("System.Data.SQLite", StringComparison.OrdinalIgnoreCase))
                    {
                        string localDll = Path.Combine(baseDir, "System.Data.SQLite.dll");
                        if (File.Exists(localDll))
                        {
                            return System.Reflection.Assembly.LoadFrom(localDll);
                        }
                    }
                    return null;
                };
            }
            catch { }
        }

        private static void ExtractResourceToFile(string resourceName, string targetPath)
        {
            if (File.Exists(targetPath)) return;

            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        using (FileStream fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write))
                        {
                            stream.CopyTo(fs);
                        }
                    }
                }
            }
            catch { }
        }
    }

    public class KioskForm : Form
    {
        // Import Win32 API untuk menyembunyikan/menampilkan Taskbar Windows
        [DllImport("user32.dll")]
        private static extern int FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern int ShowWindow(int hwnd, int nCmdShow);

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        // Path Database SQLite
        private static readonly string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kiosk.db");
        private static readonly string connectionString = string.Format("Data Source={0};Version=3;", dbPath);

        // TCP Socket Server
        private TcpListener tcpListener;
        private Thread serverThread;
        private static readonly System.Collections.Generic.List<TcpClient> connectedClients = new System.Collections.Generic.List<TcpClient>();

        private Panel mainGradientPanel;
        private Label lblHeaderQueueA;
        private Label lblHeaderQueueB;

        public KioskForm()
        {
            InitializeDatabase();
            StartTcpServer();
            InitializeKioskSettings();
            BuildUI();
            LoadLatestTicketNumbers();
        }

        // -------------------------------------------------------------
        // TCP SOCKET SERVER (PORT 3000)
        // -------------------------------------------------------------
        private void StartTcpServer()
        {
            try
            {
                tcpListener = new TcpListener(IPAddress.Any, 3000);
                tcpListener.Start();
                serverThread = new Thread(ListenForClients) { IsBackground = true };
                serverThread.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Gagal menjalankan TCP Server di Port 3000: " + ex.Message);
            }
        }

        private void ListenForClients()
        {
            while (true)
            {
                try
                {
                    TcpClient client = tcpListener.AcceptTcpClient();
                    lock (connectedClients)
                    {
                        connectedClients.Add(client);
                    }

                    // Kirim state terakhir antrian ke client baru agar DisplayMonitor sinkron
                    SendInitialSync(client);

                    Thread clientThread = new Thread(HandleClientComm) { IsBackground = true };
                    clientThread.Start(client);
                }
                catch { break; }
            }
        }

        /// <summary>
        /// Kirim nomor antrian terakhir ke client yang baru connect (DisplayMonitor)
        /// </summary>
        private void SendInitialSync(TcpClient client)
        {
            try
            {
                // Kirim SYNC untuk kunjungan
                int lastKunjungan = GetLastTicketNumber("kunjungan");
                string syncA = string.Format("SYNC|kunjungan|A|{0}\n", lastKunjungan);

                // Kirim SYNC untuk penitipan
                int lastPenitipan = GetLastTicketNumber("penitipan");
                string syncB = string.Format("SYNC|penitipan|B|{0}\n", lastPenitipan);

                byte[] dataA = Encoding.UTF8.GetBytes(syncA);
                byte[] dataB = Encoding.UTF8.GetBytes(syncB);

                NetworkStream stream = client.GetStream();
                stream.Write(dataA, 0, dataA.Length);
                stream.Write(dataB, 0, dataB.Length);
                stream.Flush();
            }
            catch { }
        }

        private void HandleClientComm(object clientObj)
        {
            TcpClient tcpClient = (TcpClient)clientObj;
            NetworkStream stream = tcpClient.GetStream();
            byte[] buffer = new byte[4096];
            string msgBuffer = "";

            while (true)
            {
                int bytesRead = 0;
                try
                {
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                }
                catch { break; }

                if (bytesRead == 0) break;

                msgBuffer += Encoding.UTF8.GetString(buffer, 0, bytesRead);

                // Proses setiap pesan yang diakhiri dengan newline
                while (msgBuffer.Contains("\n"))
                {
                    int nlIndex = msgBuffer.IndexOf('\n');
                    string msg = msgBuffer.Substring(0, nlIndex).Trim('\r');
                    msgBuffer = msgBuffer.Substring(nlIndex + 1);

                    if (!string.IsNullOrEmpty(msg))
                    {
                        ProcessIncomingSocketMessage(msg);
                    }
                }

                // Fallback: pesan tanpa newline (kompatibilitas format lama)
                if (!string.IsNullOrEmpty(msgBuffer) && msgBuffer.Contains("|"))
                {
                    string[] testParts = msgBuffer.Split('|');
                    if (testParts.Length >= 3 && testParts[0] == "CALL")
                    {
                        ProcessIncomingSocketMessage(msgBuffer.Trim());
                        msgBuffer = "";
                    }
                }
            }

            lock (connectedClients)
            {
                connectedClients.Remove(tcpClient);
            }
            tcpClient.Close();
        }

        private void ProcessIncomingSocketMessage(string message)
        {
            // Format perintah dari CallerApp: CALL|ticketId|counterNo
            message = message.Trim();
            string[] parts = message.Split('|');
            if (parts.Length >= 3 && parts[0] == "CALL")
            {
                string ticketId = parts[1].Trim();
                int counterNo = Convert.ToInt32(parts[2].Trim());

                ProcessCallTicket(ticketId, counterNo);
            }
        }

        private void ProcessCallTicket(string ticketId, int counterNo)
        {
            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();

                    // Update status tiket menjadi 'called'
                    string sqlUpdate = "UPDATE tickets SET status = 'called', counter_number = @counter WHERE id = @id AND status IN ('waiting', 'called')";
                    using (SQLiteCommand cmdUpdate = new SQLiteCommand(sqlUpdate, conn))
                    {
                        cmdUpdate.Parameters.AddWithValue("@counter", counterNo);
                        cmdUpdate.Parameters.AddWithValue("@id", ticketId);
                        cmdUpdate.ExecuteNonQuery();
                    }

                    string sqlGet = "SELECT prefix, number, category FROM tickets WHERE id = @id";
                    using (SQLiteCommand cmd = new SQLiteCommand(sqlGet, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", ticketId);
                        using (SQLiteDataReader reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                string prefix = reader["prefix"].ToString();
                                int number = Convert.ToInt32(reader["number"]);
                                string category = reader["category"].ToString();

                                // Broadcast Event ke Display Monitor
                                string broadcastMsg = string.Format("ANNOUNCE|{0}|{1}|{2}|{3}|{4}", ticketId, prefix, number, counterNo, category);
                                BroadcastMessage(broadcastMsg);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        public static void BroadcastMessage(string msg)
        {
            byte[] data = Encoding.UTF8.GetBytes(msg + "\n");
            lock (connectedClients)
            {
                foreach (TcpClient client in connectedClients.ToArray())
                {
                    try
                    {
                        if (client.Connected)
                        {
                            NetworkStream stream = client.GetStream();
                            stream.Write(data, 0, data.Length);
                        }
                    }
                    catch { }
                }
            }
        }

        // -------------------------------------------------------------
        // INISIALISASI DATABASE & TABEL SQLITE
        // -------------------------------------------------------------
        private void InitializeDatabase()
        {
            try
            {
                if (!File.Exists(dbPath))
                {
                    SQLiteConnection.CreateFile(dbPath);
                }

                using (SQLiteConnection conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();

                    string sqlTickets = @"
                        CREATE TABLE IF NOT EXISTS tickets (
                            id TEXT PRIMARY KEY,
                            number INTEGER NOT NULL,
                            category TEXT CHECK(category IN ('kunjungan', 'penitipan', 'tamu')) NOT NULL,
                            prefix TEXT NOT NULL,
                            status TEXT CHECK(status IN ('waiting', 'called', 'completed', 'archived')) NOT NULL DEFAULT 'waiting',
                            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                            completed_at DATETIME NULL,
                            counter_number INTEGER NULL
                        );";

                    string sqlMonthlyReports = @"
                        CREATE TABLE IF NOT EXISTS monthly_reports (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            month INTEGER NOT NULL,
                            year INTEGER NOT NULL,
                            category TEXT NOT NULL,
                            total_tickets INTEGER NOT NULL,
                            avg_wait_time REAL NULL,
                            report_path TEXT NULL,
                            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                            UNIQUE (month, year, category)
                        );";

                    string sqlAnnouncements = @"
                        CREATE TABLE IF NOT EXISTS announcements (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            ticket_id TEXT,
                            prefix TEXT NOT NULL,
                            number INTEGER NOT NULL,
                            counter_number INTEGER NULL,
                            announced INTEGER DEFAULT 0,
                            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                            FOREIGN KEY (ticket_id) REFERENCES tickets(id) ON DELETE CASCADE
                        );";

                    using (SQLiteCommand cmd = new SQLiteCommand(sqlTickets, conn))
                    {
                        cmd.ExecuteNonQuery();
                    }
                    using (SQLiteCommand cmd = new SQLiteCommand(sqlMonthlyReports, conn))
                    {
                        cmd.ExecuteNonQuery();
                    }
                    using (SQLiteCommand cmd = new SQLiteCommand(sqlAnnouncements, conn))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Gagal inisialisasi database SQLite:\n" + ex.Message, "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Load Nomor Antrean Terakhir dari Database
        private void LoadLatestTicketNumbers()
        {
            if (lblHeaderQueueA != null)
                lblHeaderQueueA.Text = "A" + GetLastTicketNumber("kunjungan").ToString("D3");

            if (lblHeaderQueueB != null)
                lblHeaderQueueB.Text = "B" + GetLastTicketNumber("penitipan").ToString("D3");
        }

        private int GetLastTicketNumber(string category)
        {
            int lastNum = 0;
            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    // Menggunakan substr/date yang kompatibel dengan format timestamp SQLite
                    string sql = @"
                        SELECT COALESCE(MAX(number), 0) 
                        FROM tickets 
                        WHERE category = @cat 
                          AND (
                            date(created_at) = date('now', 'localtime') 
                            OR date(created_at) = date('now')
                          )";

                    using (SQLiteCommand cmd = new SQLiteCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@cat", category);
                        object result = cmd.ExecuteScalar();
                        if (result != null && result != DBNull.Value)
                        {
                            lastNum = Convert.ToInt32(result);
                        }
                    }
                }
            }
            catch { }
            return lastNum;
        }

        private void InitializeKioskSettings()
        {
            // =========================================================================
            // MODE KIOSK PRODUKSI AKTIF
            // =========================================================================
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.TopMost = true;
            ToggleTaskbar(false);

            /* --- MODE TESTING DITUTUP ---
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.WindowState = FormWindowState.Normal;
            this.Size = new Size(1280, 720);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.TopMost = false;
            --------------------------------*/

            this.DoubleBuffered = true;

            // Set Icon dari favicon.ico
            string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "favicon.ico");
            if (File.Exists(icoPath))
            {
                try { this.Icon = new Icon(icoPath); } catch { }
            }

            this.KeyPreview = true;
            this.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    ExitKiosk();
                }
            };
        }

        private void BuildUI()
        {
            // -------------------------------------------------------------
            // 1. TOP HEADER PANEL
            // -------------------------------------------------------------
            Panel headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 260,
                BackColor = Color.FromArgb(19, 24, 38)
            };
            this.Controls.Add(headerPanel);

            PictureBox picLogo = new PictureBox
            {
                Size = new Size(90, 90),
                Location = new Point((Screen.PrimaryScreen.Bounds.Width - 90) / 2, 10),
                BackColor = Color.Transparent,
                SizeMode = PictureBoxSizeMode.Zoom
            };
            picLogo.Paint += DrawEmblemLogo;

            Label lblTitle = new Label
            {
                Text = "SIKAWAN LAGUSTA",
                Font = new Font("Segoe UI", 24, FontStyle.Bold),
                ForeColor = Color.FromArgb(245, 185, 40),
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(Screen.PrimaryScreen.Bounds.Width, 40),
                Location = new Point(0, 110),
                BackColor = Color.Transparent
            };

            Label lblSubTitle = new Label
            {
                Text = "SISTEM INFORMASI PENDAFTARAN KUNJUNGAN WARGA BINAAN LAPAS KELAS I MEDAN",
                Font = new Font("Segoe UI", 13, FontStyle.Bold),
                ForeColor = Color.FromArgb(245, 185, 40),
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(Screen.PrimaryScreen.Bounds.Width, 32),
                Location = new Point(0, 160),
                BackColor = Color.Transparent
            };

            Label lblInstruction = new Label
            {
                Text = "Silakan pilih kategori layanan",
                Font = new Font("Segoe UI", 12, FontStyle.Regular),
                ForeColor = Color.FromArgb(210, 215, 225),
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(Screen.PrimaryScreen.Bounds.Width, 30),
                Location = new Point(0, 205),
                BackColor = Color.Transparent
            };

            headerPanel.Controls.Add(picLogo);
            headerPanel.Controls.Add(lblTitle);
            headerPanel.Controls.Add(lblSubTitle);
            headerPanel.Controls.Add(lblInstruction);

            picLogo.BringToFront();

            // -------------------------------------------------------------
            // 2. FOOTER PANEL
            // -------------------------------------------------------------
            Panel footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                BackColor = Color.FromArgb(15, 20, 32)
            };
            this.Controls.Add(footerPanel);

            Label lblFooter = new Label
            {
                Text = "© " + DateTime.Now.Year + " - SIKAWAN LAGUSTA",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.FromArgb(160, 175, 200),
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            footerPanel.Controls.Add(lblFooter);

            // -------------------------------------------------------------
            // 3. MAIN CONTENT
            // -------------------------------------------------------------
            mainGradientPanel = new Panel
            {
                Dock = DockStyle.Fill
            };
            mainGradientPanel.Paint += MainGradientPanel_Paint;
            this.Controls.Add(mainGradientPanel);
            mainGradientPanel.BringToFront();
            headerPanel.BringToFront();
            footerPanel.BringToFront();

            int cardWidth = 280;
            int cardHeight = 360;
            int spacing = 30;
            int totalWidth = (cardWidth * 2) + spacing;
            int startX = (Screen.PrimaryScreen.Bounds.Width - totalWidth) / 2;
            int startY = (Screen.PrimaryScreen.Bounds.Height - 300 - cardHeight) / 2 + 230;

            // --- KARTU 1: KUNJUNGAN ---
            Panel cardKunjungan = CreateServiceCard(
                startX, startY, cardWidth, cardHeight,
                "A000",
                Color.FromArgb(41, 182, 216),
                "KUNJUNGAN",
                "Kunjungan keluarga/teman",
                DrawKunjunganIcon,
                out lblHeaderQueueA,
                () => HandlePendaftaranDatabase("kunjungan", "A", lblHeaderQueueA)
            );
            mainGradientPanel.Controls.Add(cardKunjungan);

            // --- KARTU 2: PENITIPAN ---
            Panel cardPenitipan = CreateServiceCard(
                startX + cardWidth + spacing, startY, cardWidth, cardHeight,
                "B000",
                Color.FromArgb(136, 108, 179),
                "PENITIPAN",
                "Penitipan barang",
                DrawPenitipanIcon,
                out lblHeaderQueueB,
                () => HandlePendaftaranDatabase("penitipan", "B", lblHeaderQueueB)
            );
            mainGradientPanel.Controls.Add(cardPenitipan);

            Label lblThankYou = new Label
            {
                Text = "Terima Kasih Atas Kunjungan Anda",
                Font = new Font("Segoe UI", 20, FontStyle.Bold),
                ForeColor = Color.FromArgb(245, 185, 40),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false,
                Size = new Size(Screen.PrimaryScreen.Bounds.Width, 50),
                Location = new Point(0, startY + cardHeight + 40),
                BackColor = Color.Transparent
            };
            mainGradientPanel.Controls.Add(lblThankYou);
        }

        private Panel CreateServiceCard(
            int x, int y, int width, int height, 
            string queueNo, Color headerColor, 
            string title, string subtitle, 
            PaintEventHandler iconPainter,
            out Label queueLabelHolder,
            Action onClickAction)
        {
            Panel card = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(width, height),
                BackColor = Color.White,
                Cursor = Cursors.Hand
            };

            Panel headerCard = new Panel
            {
                Dock = DockStyle.Top,
                Height = 90,
                BackColor = headerColor
            };
            card.Controls.Add(headerCard);

            Label lblQueue = new Label
            {
                Text = queueNo,
                Font = new Font("Segoe UI", 32, FontStyle.Bold),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            headerCard.Controls.Add(lblQueue);
            queueLabelHolder = lblQueue;

            PictureBox picIcon = new PictureBox
            {
                Size = new Size(110, 110),
                Location = new Point((width - 110) / 2, 110),
                BackColor = Color.Transparent
            };
            picIcon.Paint += iconPainter;
            card.Controls.Add(picIcon);

            Label lblCardTitle = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = Color.FromArgb(20, 25, 35),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 235),
                Size = new Size(width, 35)
            };
            card.Controls.Add(lblCardTitle);

            Label lblCardSub = new Label
            {
                Text = subtitle,
                Font = new Font("Segoe UI", 10, FontStyle.Regular),
                ForeColor = Color.FromArgb(120, 130, 145),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 275),
                Size = new Size(width, 30)
            };
            card.Controls.Add(lblCardSub);

            BindClickRecursive(card, onClickAction);

            return card;
        }

        private void BindClickRecursive(Control ctrl, Action action)
        {
            ctrl.Click += (s, e) => action();
            foreach (Control child in ctrl.Controls)
            {
                BindClickRecursive(child, action);
            }
        }

        // -------------------------------------------------------------
        // LOGIKA PENDAFTARAN KE SQLITE DATABASE
        // -------------------------------------------------------------
        private void HandlePendaftaranDatabase(string category, string prefix, Label lblDisplay)
        {
            try
            {
                string ticketId = Guid.NewGuid().ToString();
                int nextNumber = GetLastTicketNumber(category) + 1;
                string noAntrian = prefix + nextNumber.ToString("D3");

                using (SQLiteConnection conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();
                    using (SQLiteTransaction trans = conn.BeginTransaction())
                    {
                        // 1. Insert ke tabel tickets (simpan waktu lokal secara eksplisit)
                        string sqlTicket = @"
                            INSERT INTO tickets (id, number, category, prefix, status, created_at)
                            VALUES (@id, @number, @category, @prefix, 'waiting', datetime('now', 'localtime'))";

                        using (SQLiteCommand cmd = new SQLiteCommand(sqlTicket, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@id", ticketId);
                            cmd.Parameters.AddWithValue("@number", nextNumber);
                            cmd.Parameters.AddWithValue("@category", category);
                            cmd.Parameters.AddWithValue("@prefix", prefix);
                            cmd.ExecuteNonQuery();
                        }

                        // 2. Insert ke tabel announcements
                        string sqlAnnounce = @"
                            INSERT INTO announcements (ticket_id, prefix, number, announced, created_at)
                            VALUES (@ticket_id, @prefix, @number, 0, datetime('now', 'localtime'))";

                        using (SQLiteCommand cmd = new SQLiteCommand(sqlAnnounce, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@ticket_id", ticketId);
                            cmd.Parameters.AddWithValue("@prefix", prefix);
                            cmd.Parameters.AddWithValue("@number", nextNumber);
                            cmd.ExecuteNonQuery();
                        }

                        trans.Commit();
                    }
                }

                // Synchronize & Update UI
                if (lblDisplay != null)
                {
                    lblDisplay.Text = noAntrian;
                    lblDisplay.Refresh();
                }

                // Broadcast SYNC ke semua Display Monitor agar nomor selalu sinkron
                string syncMsg = string.Format("SYNC|{0}|{1}|{2}", category, prefix, nextNumber);
                BroadcastMessage(syncMsg);

                // CETAK LANGSUNG KE PRINTER THERMAL / DEFAULT
                CetakStrukThermal(category.ToUpper(), noAntrian);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Gagal menyimpan pendaftaran ke database:\n" + ex.Message, "Error Database", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // -------------------------------------------------------------
        // FITUR CETAK DIRECT KE PRINTER THERMAL (80mm / 58mm)
        // -------------------------------------------------------------
        private void CetakStrukThermal(string kategori, string noAntrian)
        {
            try
            {
                PrintDocument pd = new PrintDocument();
                // Menggunakan Printer Default Windows
                pd.PrintPage += (sender, e) =>
                {
                    Graphics g = e.Graphics;
                    float yPos = 10;
                    float leftMargin = 0;
                    float printWidth = e.PageBounds.Width > 0 ? e.PageBounds.Width : 280; // Lebar standar kertas thermal

                    StringFormat centerFormat = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };

                    Font fontHeader = new Font("Segoe UI", 10, FontStyle.Bold);
                    Font fontSubHeader = new Font("Segoe UI", 8, FontStyle.Regular);
                    Font fontKategori = new Font("Segoe UI", 14, FontStyle.Bold);
                    Font fontNumber = new Font("Segoe UI", 36, FontStyle.Bold);
                    Font fontFooter = new Font("Segoe UI", 8, FontStyle.Regular);
                    Font fontDivider = new Font("Segoe UI", 8, FontStyle.Bold);

                    // 1. Header Struk
                    g.DrawString("LAPAS KELAS I MEDAN", fontHeader, Brushes.Black, new RectangleF(leftMargin, yPos, printWidth, 20), centerFormat);
                    yPos += 20;
                    g.DrawString("SIKAWAN LAGUSTA", fontSubHeader, Brushes.Black, new RectangleF(leftMargin, yPos, printWidth, 15), centerFormat);
                    yPos += 20;

                    // Garis Pembatas
                    g.DrawString("------------------------------------------", fontDivider, Brushes.Black, new RectangleF(leftMargin, yPos, printWidth, 15), centerFormat);
                    yPos += 15;

                    // 2. Kategori Layanan
                    g.DrawString("LAYANAN: " + kategori, fontKategori, Brushes.Black, new RectangleF(leftMargin, yPos, printWidth, 25), centerFormat);
                    yPos += 30;

                    // 3. Nomor Antrean (Ukuran Besar)
                    g.DrawString(noAntrian, fontNumber, Brushes.Black, new RectangleF(leftMargin, yPos, printWidth, 60), centerFormat);
                    yPos += 65;

                    // Garis Pembatas
                    g.DrawString("------------------------------------------", fontDivider, Brushes.Black, new RectangleF(leftMargin, yPos, printWidth, 15), centerFormat);
                    yPos += 15;

                    // 4. Informasi Waktu & Catatan kaki
                    string wibTime = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss") + " WIB";
                    g.DrawString("Waktu: " + wibTime, fontFooter, Brushes.Black, new RectangleF(leftMargin, yPos, printWidth, 15), centerFormat);
                    yPos += 18;
                    g.DrawString("Harap menunggu nomor Anda dipanggil.", fontFooter, Brushes.Black, new RectangleF(leftMargin, yPos, printWidth, 15), centerFormat);
                    yPos += 18;
                    g.DrawString("Terima Kasih", fontFooter, Brushes.Black, new RectangleF(leftMargin, yPos, printWidth, 15), centerFormat);
                };

                // Eksekusi pencetakan tanpa menampilkan Print Dialog (Direct Print)
                pd.PrintController = new StandardPrintController();
                pd.Print();
            }
            catch (Exception ex)
            {
                // Jika printer tidak terhubung / bermasalah, tampilkan pesan warning namun transaksi tetap tersimpan
                MessageBox.Show("Tiket berhasil disimpan, namun printer tidak merespon:\n" + ex.Message, 
                    "Peringatan Printer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void MainGradientPanel_Paint(object sender, PaintEventArgs e)
        {
            Rectangle rect = mainGradientPanel.ClientRectangle;
            if (rect.Width <= 0 || rect.Height <= 0) return;

            using (LinearGradientBrush brush = new LinearGradientBrush(
                rect,
                Color.FromArgb(5, 18, 115),
                Color.FromArgb(85, 125, 155),
                LinearGradientMode.Horizontal))
            {
                e.Graphics.FillRectangle(brush, rect);
            }
        }

        private void DrawEmblemLogo(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 1. Coba muat gambar dari Embedded Resource (.exe internal)
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using (Stream stream = assembly.GetManifestResourceStream("logo.png"))
                {
                    if (stream != null)
                    {
                        using (Image img = Image.FromStream(stream))
                        {
                            g.DrawImage(img, new Rectangle(0, 0, 90, 90));
                            return;
                        }
                    }
                }
            }
            catch { }

            // 2. Fallback jika file external img/logo.png ada
            string logoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "img", "logo.png");

            if (System.IO.File.Exists(logoPath))
            {
                try
                {
                    using (Image img = Image.FromFile(logoPath))
                    {
                        g.DrawImage(img, new Rectangle(0, 0, 90, 90));
                        return;
                    }
                }
                catch { }
            }

            // 3. Fallback jika gambar tidak ditemukan sama sekali: Gambar Logo Vektor GDI+
            using (Pen penGold = new Pen(Color.FromArgb(245, 185, 40), 3))
            {
                g.DrawEllipse(penGold, 4, 4, 82, 82);
                g.DrawEllipse(penGold, 10, 10, 70, 70);
            }

            // Background Perisai Biru Tua
            using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(19, 24, 38)))
            {
                g.FillEllipse(bgBrush, 12, 12, 66, 66);
            }

            // Lambang Bintang & Perisai Emas
            using (SolidBrush brushGold = new SolidBrush(Color.FromArgb(245, 185, 40)))
            {
                PointF[] shield = {
                    new PointF(45, 22),
                    new PointF(62, 32),
                    new PointF(62, 54),
                    new PointF(45, 68),
                    new PointF(28, 54),
                    new PointF(28, 32)
                };
                g.FillPolygon(brushGold, shield);
            }

            // Garuda / Detail Dalam (Putih & Merah)
            using (SolidBrush brushRed = new SolidBrush(Color.FromArgb(210, 40, 40)))
            {
                g.FillRectangle(brushRed, 37, 36, 16, 16);
            }
            using (SolidBrush brushWhite = new SolidBrush(Color.White))
            {
                g.FillRectangle(brushWhite, 41, 40, 8, 8);
            }
        }

        private void DrawKunjunganIcon(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color cyanColor = Color.FromArgb(41, 182, 216);

            using (Pen pen = new Pen(cyanColor, 4))
            {
                g.DrawEllipse(pen, 20, 15, 24, 24);
                g.DrawArc(pen, 10, 45, 44, 40, 190, 160);

                g.DrawEllipse(pen, 60, 20, 22, 22);
                g.DrawArc(pen, 50, 48, 40, 36, 190, 150);

                g.DrawLines(pen, new Point[] {
                    new Point(72, 60),
                    new Point(78, 66),
                    new Point(90, 52)
                });
            }
        }

        private void DrawPenitipanIcon(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color purpleColor = Color.FromArgb(136, 108, 179);

            using (Pen pen = new Pen(purpleColor, 4))
            {
                g.DrawArc(pen, 35, 12, 40, 30, 180, 180);

                Rectangle body = new Rectangle(20, 30, 70, 65);
                g.DrawRectangle(pen, body);

                g.DrawArc(pen, 40, 35, 30, 20, 0, 180);
            }
        }

        private void ExitKiosk()
        {
            DialogResult dr = MessageBox.Show("Apakah Anda yakin ingin keluar dari Kiosk?", 
                "Konfirmasi Keluar", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            
            if (dr == DialogResult.Yes)
            {
                ToggleTaskbar(true); // Tampilkan kembali taskbar saat keluar
                Application.Exit();
            }
        }

        private static void ToggleTaskbar(bool show)
        {
            int hwnd = FindWindow("Shell_TrayWnd", null);
            if (hwnd != 0)
            {
                ShowWindow(hwnd, show ? SW_SHOW : SW_HIDE);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                if (tcpListener != null)
                {
                    tcpListener.Stop();
                }
            }
            catch { }
            base.OnFormClosing(e);
        }
    }
}