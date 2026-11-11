using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SikawanLagustaReport
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ReportForm());
        }
    }

    // =========================================================================
    // REPORT FORM - Aplikasi Laporan Antrian Harian/Bulanan
    // =========================================================================
    public class ReportForm : Form
    {
        private static readonly string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kiosk.db");
        private static readonly string connectionString = string.Format("Data Source={0};Version=3;", dbPath);

        private DateTimePicker dtpStartDate;
        private DateTimePicker dtpEndDate;
        private ComboBox cbReportType;
        private ComboBox cbCategory;
        private DataGridView dgvReport;
        private Label lblTotalTickets;
        private Label lblTotalWaiting;
        private Label lblTotalCalled;
        private Label lblTotalCompleted;
        private Label lblAvgServiceTime;
        private Panel pnlSummary;
        private DataTable currentData;

        public ReportForm()
        {
            InitializeForm();
            BuildUI();
        }

        private void InitializeForm()
        {
            this.Text = "Laporan Antrian - SIKAWAN LAGUSTA";
            this.Size = new Size(1000, 720);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(240, 244, 248);
            this.MinimumSize = new Size(800, 600);

            // Set Icon dari favicon.ico
            string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "favicon.ico");
            if (File.Exists(icoPath))
            {
                try { this.Icon = new Icon(icoPath); } catch { }
            }
        }

        private void BuildUI()
        {
            // =================================================================
            // HEADER PANEL
            // =================================================================
            Panel headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 70,
                BackColor = Color.FromArgb(19, 24, 38)
            };
            this.Controls.Add(headerPanel);

            Label lblTitle = new Label
            {
                Text = "📊 LAPORAN ANTRIAN - SIKAWAN LAGUSTA",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = Color.FromArgb(245, 185, 40),
                TextAlign = ContentAlignment.MiddleLeft,
                Location = new Point(20, 0),
                Size = new Size(600, 70)
            };
            headerPanel.Controls.Add(lblTitle);

            Label lblSubtitle = new Label
            {
                Text = "Lapas Kelas I Medan",
                Font = new Font("Segoe UI", 10, FontStyle.Regular),
                ForeColor = Color.FromArgb(180, 190, 210),
                TextAlign = ContentAlignment.MiddleRight,
                Dock = DockStyle.Right,
                AutoSize = false,
                Width = 250
            };
            headerPanel.Controls.Add(lblSubtitle);

            // =================================================================
            // FILTER PANEL
            // =================================================================
            Panel filterPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 80,
                BackColor = Color.White,
                Padding = new Padding(15, 10, 15, 10)
            };
            this.Controls.Add(filterPanel);
            filterPanel.BringToFront();

            Label lblStart = new Label { Text = "Dari:", Font = new Font("Segoe UI", 9, FontStyle.Bold), Location = new Point(15, 18), AutoSize = true };
            filterPanel.Controls.Add(lblStart);

            dtpStartDate = new DateTimePicker
            {
                Format = DateTimePickerFormat.Short,
                Location = new Point(55, 15),
                Width = 130,
                Value = DateTime.Today
            };
            filterPanel.Controls.Add(dtpStartDate);

            Label lblEnd = new Label { Text = "Sampai:", Font = new Font("Segoe UI", 9, FontStyle.Bold), Location = new Point(200, 18), AutoSize = true };
            filterPanel.Controls.Add(lblEnd);

            dtpEndDate = new DateTimePicker
            {
                Format = DateTimePickerFormat.Short,
                Location = new Point(260, 15),
                Width = 130,
                Value = DateTime.Today
            };
            filterPanel.Controls.Add(dtpEndDate);

            Label lblType = new Label { Text = "Jenis:", Font = new Font("Segoe UI", 9, FontStyle.Bold), Location = new Point(410, 18), AutoSize = true };
            filterPanel.Controls.Add(lblType);

            cbReportType = new ComboBox
            {
                Location = new Point(455, 15),
                Width = 130,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cbReportType.Items.AddRange(new object[] { "Harian", "Bulanan", "Per Loket" });
            cbReportType.SelectedIndex = 0;
            filterPanel.Controls.Add(cbReportType);

            Label lblCat = new Label { Text = "Layanan:", Font = new Font("Segoe UI", 9, FontStyle.Bold), Location = new Point(600, 18), AutoSize = true };
            filterPanel.Controls.Add(lblCat);

            cbCategory = new ComboBox
            {
                Location = new Point(660, 15),
                Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cbCategory.Items.AddRange(new object[] { "Semua", "kunjungan", "penitipan" });
            cbCategory.SelectedIndex = 0;
            filterPanel.Controls.Add(cbCategory);

            Button btnGenerate = new Button
            {
                Text = "Tampilkan",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 123, 255),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(15, 48),
                Size = new Size(120, 28),
                Cursor = Cursors.Hand
            };
            btnGenerate.FlatAppearance.BorderSize = 0;
            btnGenerate.Click += BtnGenerate_Click;
            filterPanel.Controls.Add(btnGenerate);

            Button btnExportCsv = new Button
            {
                Text = "Export CSV",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                BackColor = Color.FromArgb(40, 167, 69),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(145, 48),
                Size = new Size(120, 28),
                Cursor = Cursors.Hand
            };
            btnExportCsv.FlatAppearance.BorderSize = 0;
            btnExportCsv.Click += BtnExportCsv_Click;
            filterPanel.Controls.Add(btnExportCsv);

            Button btnPrint = new Button
            {
                Text = "Cetak Laporan",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                BackColor = Color.FromArgb(108, 117, 125),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(275, 48),
                Size = new Size(120, 28),
                Cursor = Cursors.Hand
            };
            btnPrint.FlatAppearance.BorderSize = 0;
            btnPrint.Click += BtnPrint_Click;
            filterPanel.Controls.Add(btnPrint);

            // =================================================================
            // SUMMARY CARDS
            // =================================================================
            pnlSummary = new Panel
            {
                Dock = DockStyle.Top,
                Height = 90,
                BackColor = Color.FromArgb(240, 244, 248),
                Padding = new Padding(10, 10, 10, 5)
            };
            this.Controls.Add(pnlSummary);
            pnlSummary.BringToFront();

            lblTotalTickets = CreateSummaryCard(pnlSummary, 15, "Total Tiket", "0", Color.FromArgb(0, 123, 255));
            lblTotalWaiting = CreateSummaryCard(pnlSummary, 205, "Menunggu", "0", Color.FromArgb(255, 193, 7));
            lblTotalCalled = CreateSummaryCard(pnlSummary, 395, "Dipanggil", "0", Color.FromArgb(255, 133, 27));
            lblTotalCompleted = CreateSummaryCard(pnlSummary, 585, "Selesai", "0", Color.FromArgb(40, 167, 69));
            lblAvgServiceTime = CreateSummaryCard(pnlSummary, 775, "Rata-rata", "- menit", Color.FromArgb(111, 66, 193));

            // =================================================================
            // DATA GRID VIEW
            // =================================================================
            dgvReport = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(245, 248, 252) },
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Font = new Font("Segoe UI", 9),
                    Padding = new Padding(5, 3, 5, 3)
                },
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(19, 24, 38),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9, FontStyle.Bold),
                    Alignment = DataGridViewContentAlignment.MiddleCenter
                },
                EnableHeadersVisualStyles = false
            };
            this.Controls.Add(dgvReport);
            dgvReport.BringToFront();

            // =================================================================
            // FOOTER STATUS
            // =================================================================
            Panel footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 30,
                BackColor = Color.FromArgb(19, 24, 38)
            };
            this.Controls.Add(footerPanel);

            Label lblFooter = new Label
            {
                Text = "© " + DateTime.Now.Year + " - SIKAWAN LAGUSTA | Laporan Sistem Antrian",
                Font = new Font("Segoe UI", 8, FontStyle.Regular),
                ForeColor = Color.FromArgb(160, 175, 200),
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            footerPanel.Controls.Add(lblFooter);

            // Auto-load hari ini
            this.Load += (s, e) => BtnGenerate_Click(null, null);
        }

        private Label CreateSummaryCard(Panel parent, int x, string title, string value, Color accentColor)
        {
            Panel card = new Panel
            {
                Location = new Point(x, 5),
                Size = new Size(175, 75),
                BackColor = Color.White
            };
            card.Paint += (s, e) =>
            {
                using (Pen pen = new Pen(accentColor, 3))
                {
                    e.Graphics.DrawLine(pen, 0, 0, 0, card.Height);
                }
            };
            parent.Controls.Add(card);

            Label lblTitle = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 8, FontStyle.Regular),
                ForeColor = Color.FromArgb(120, 130, 145),
                Location = new Point(12, 8),
                AutoSize = true
            };
            card.Controls.Add(lblTitle);

            Label lblValue = new Label
            {
                Text = value,
                Font = new Font("Segoe UI", 20, FontStyle.Bold),
                ForeColor = accentColor,
                Location = new Point(10, 28),
                AutoSize = true
            };
            card.Controls.Add(lblValue);

            return lblValue;
        }

        // =====================================================================
        // GENERATE LAPORAN
        // =====================================================================
        private void BtnGenerate_Click(object sender, EventArgs e)
        {
            if (!File.Exists(dbPath))
            {
                MessageBox.Show("Database kiosk.db tidak ditemukan!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string reportType = cbReportType.SelectedItem.ToString();
            string category = cbCategory.SelectedItem.ToString();
            string startDate = dtpStartDate.Value.ToString("yyyy-MM-dd");
            string endDate = dtpEndDate.Value.ToString("yyyy-MM-dd");

            try
            {
                using (SQLiteConnection conn = new SQLiteConnection(connectionString))
                {
                    conn.Open();

                    if (reportType == "Harian")
                    {
                        GenerateDailyReport(conn, startDate, endDate, category);
                    }
                    else if (reportType == "Bulanan")
                    {
                        GenerateMonthlyReport(conn, startDate, endDate, category);
                    }
                    else
                    {
                        GenerateCounterReport(conn, startDate, endDate, category);
                    }

                    // Update summary cards
                    UpdateSummaryCards(conn, startDate, endDate, category);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message, "Gagal", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void GenerateDailyReport(SQLiteConnection conn, string startDate, string endDate, string category)
        {
            string sql = @"
                SELECT 
                    t.prefix || printf('%03d', t.number) AS 'No Antrian',
                    CASE t.category WHEN 'kunjungan' THEN 'Kunjungan' ELSE 'Penitipan' END AS 'Layanan',
                    CASE t.status 
                        WHEN 'waiting' THEN 'Menunggu' 
                        WHEN 'called' THEN 'Dipanggil' 
                        WHEN 'completed' THEN 'Selesai' 
                        WHEN 'archived' THEN 'Arsip' 
                    END AS 'Status',
                    COALESCE('Loket ' || t.counter_number, '-') AS 'Loket',
                    t.created_at AS 'Waktu Daftar',
                    COALESCE(t.completed_at, '-') AS 'Waktu Selesai',
                    CASE 
                        WHEN t.completed_at IS NOT NULL AND t.created_at IS NOT NULL 
                        THEN CAST((julianday(t.completed_at) - julianday(t.created_at)) * 24 * 60 AS INTEGER) || ' menit'
                        ELSE '-'
                    END AS 'Durasi'
                FROM tickets t
                WHERE date(t.created_at) BETWEEN @start AND @end";

            if (category != "Semua")
            {
                sql += " AND t.category = @cat";
            }
            sql += " ORDER BY t.created_at DESC";

            using (SQLiteCommand cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@start", startDate);
                cmd.Parameters.AddWithValue("@end", endDate);
                if (category != "Semua")
                    cmd.Parameters.AddWithValue("@cat", category);

                SQLiteDataAdapter adapter = new SQLiteDataAdapter(cmd);
                currentData = new DataTable();
                adapter.Fill(currentData);
                dgvReport.DataSource = currentData;
            }
        }

        private void GenerateMonthlyReport(SQLiteConnection conn, string startDate, string endDate, string category)
        {
            string sql = @"
                SELECT 
                    strftime('%Y-%m', created_at) AS 'Bulan',
                    CASE category WHEN 'kunjungan' THEN 'Kunjungan' ELSE 'Penitipan' END AS 'Layanan',
                    COUNT(*) AS 'Total Tiket',
                    SUM(CASE WHEN status = 'completed' THEN 1 ELSE 0 END) AS 'Selesai',
                    SUM(CASE WHEN status = 'waiting' THEN 1 ELSE 0 END) AS 'Menunggu',
                    SUM(CASE WHEN status = 'called' THEN 1 ELSE 0 END) AS 'Dipanggil',
                    COALESCE(
                        CAST(AVG(
                            CASE WHEN completed_at IS NOT NULL 
                            THEN (julianday(completed_at) - julianday(created_at)) * 24 * 60 
                            END
                        ) AS INTEGER), 0
                    ) || ' menit' AS 'Rata-rata Layanan'
                FROM tickets
                WHERE date(created_at) BETWEEN @start AND @end";

            if (category != "Semua")
            {
                sql += " AND category = @cat";
            }
            sql += " GROUP BY strftime('%Y-%m', created_at), category ORDER BY 1 DESC";

            using (SQLiteCommand cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@start", startDate);
                cmd.Parameters.AddWithValue("@end", endDate);
                if (category != "Semua")
                    cmd.Parameters.AddWithValue("@cat", category);

                SQLiteDataAdapter adapter = new SQLiteDataAdapter(cmd);
                currentData = new DataTable();
                adapter.Fill(currentData);
                dgvReport.DataSource = currentData;
            }
        }

        private void GenerateCounterReport(SQLiteConnection conn, string startDate, string endDate, string category)
        {
            string sql = @"
                SELECT 
                    COALESCE('Loket ' || counter_number, 'Belum Dilayani') AS 'Loket',
                    CASE category WHEN 'kunjungan' THEN 'Kunjungan' ELSE 'Penitipan' END AS 'Layanan',
                    COUNT(*) AS 'Total Tiket',
                    SUM(CASE WHEN status = 'completed' THEN 1 ELSE 0 END) AS 'Selesai',
                    COALESCE(
                        CAST(AVG(
                            CASE WHEN completed_at IS NOT NULL 
                            THEN (julianday(completed_at) - julianday(created_at)) * 24 * 60 
                            END
                        ) AS INTEGER), 0
                    ) || ' menit' AS 'Rata-rata Layanan'
                FROM tickets
                WHERE date(created_at) BETWEEN @start AND @end";

            if (category != "Semua")
            {
                sql += " AND category = @cat";
            }
            sql += " GROUP BY counter_number, category ORDER BY counter_number";

            using (SQLiteCommand cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@start", startDate);
                cmd.Parameters.AddWithValue("@end", endDate);
                if (category != "Semua")
                    cmd.Parameters.AddWithValue("@cat", category);

                SQLiteDataAdapter adapter = new SQLiteDataAdapter(cmd);
                currentData = new DataTable();
                adapter.Fill(currentData);
                dgvReport.DataSource = currentData;
            }
        }

        private void UpdateSummaryCards(SQLiteConnection conn, string startDate, string endDate, string category)
        {
            string whereClause = "WHERE date(created_at) BETWEEN @start AND @end";
            if (category != "Semua")
                whereClause += " AND category = @cat";

            // Total tiket
            string sqlTotal = "SELECT COUNT(*) FROM tickets " + whereClause;
            string sqlWaiting = "SELECT COUNT(*) FROM tickets " + whereClause + " AND status = 'waiting'";
            string sqlCalled = "SELECT COUNT(*) FROM tickets " + whereClause + " AND status = 'called'";
            string sqlCompleted = "SELECT COUNT(*) FROM tickets " + whereClause + " AND status = 'completed'";
            string sqlAvg = @"
                SELECT COALESCE(
                    CAST(AVG(
                        CASE WHEN completed_at IS NOT NULL 
                        THEN (julianday(completed_at) - julianday(created_at)) * 24 * 60 
                        END
                    ) AS INTEGER), 0
                ) FROM tickets " + whereClause;

            lblTotalTickets.Text = ExecuteScalarStr(conn, sqlTotal, startDate, endDate, category);
            lblTotalWaiting.Text = ExecuteScalarStr(conn, sqlWaiting, startDate, endDate, category);
            lblTotalCalled.Text = ExecuteScalarStr(conn, sqlCalled, startDate, endDate, category);
            lblTotalCompleted.Text = ExecuteScalarStr(conn, sqlCompleted, startDate, endDate, category);

            string avg = ExecuteScalarStr(conn, sqlAvg, startDate, endDate, category);
            lblAvgServiceTime.Text = avg + " mnt";
        }

        private string ExecuteScalarStr(SQLiteConnection conn, string sql, string startDate, string endDate, string category)
        {
            using (SQLiteCommand cmd = new SQLiteCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@start", startDate);
                cmd.Parameters.AddWithValue("@end", endDate);
                if (category != "Semua")
                    cmd.Parameters.AddWithValue("@cat", category);

                object result = cmd.ExecuteScalar();
                return result != null && result != DBNull.Value ? result.ToString() : "0";
            }
        }

        // =====================================================================
        // EXPORT CSV
        // =====================================================================
        private void BtnExportCsv_Click(object sender, EventArgs e)
        {
            if (currentData == null || currentData.Rows.Count == 0)
            {
                MessageBox.Show("Tidak ada data untuk di-export. Silakan tampilkan laporan terlebih dahulu.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SaveFileDialog sfd = new SaveFileDialog
            {
                Filter = "CSV File (*.csv)|*.csv",
                FileName = string.Format("Laporan_Antrian_{0}_{1}.csv", dtpStartDate.Value.ToString("yyyyMMdd"), dtpEndDate.Value.ToString("yyyyMMdd")),
                Title = "Export Laporan ke CSV"
            };

            if (sfd.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    StringBuilder sb = new StringBuilder();

                    // Header
                    List<string> headers = new List<string>();
                    foreach (DataColumn col in currentData.Columns)
                    {
                        headers.Add("\"" + col.ColumnName + "\"");
                    }
                    sb.AppendLine(string.Join(",", headers.ToArray()));

                    // Data
                    foreach (DataRow row in currentData.Rows)
                    {
                        List<string> values = new List<string>();
                        foreach (DataColumn col in currentData.Columns)
                        {
                            values.Add("\"" + row[col].ToString().Replace("\"", "\"\"") + "\"");
                        }
                        sb.AppendLine(string.Join(",", values.ToArray()));
                    }

                    File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                    MessageBox.Show("Laporan berhasil di-export ke:\n" + sfd.FileName, "Sukses", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Gagal export: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // =====================================================================
        // CETAK LAPORAN
        // =====================================================================
        private void BtnPrint_Click(object sender, EventArgs e)
        {
            if (currentData == null || currentData.Rows.Count == 0)
            {
                MessageBox.Show("Tidak ada data untuk dicetak. Silakan tampilkan laporan terlebih dahulu.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            PrintDocument pd = new PrintDocument();
            pd.DefaultPageSettings.Landscape = true;
            int currentRow = 0;

            pd.PrintPage += (s, pe) =>
            {
                Graphics g = pe.Graphics;
                float yPos = pe.MarginBounds.Top;
                float leftMargin = pe.MarginBounds.Left;
                float pageWidth = pe.MarginBounds.Width;
                float rowHeight = 22;

                Font fontTitle = new Font("Segoe UI", 14, FontStyle.Bold);
                Font fontSubTitle = new Font("Segoe UI", 9, FontStyle.Regular);
                Font fontHeader = new Font("Segoe UI", 8, FontStyle.Bold);
                Font fontData = new Font("Segoe UI", 8, FontStyle.Regular);

                // Header Laporan (hanya halaman pertama)
                if (currentRow == 0)
                {
                    g.DrawString("LAPORAN ANTRIAN - SIKAWAN LAGUSTA", fontTitle, Brushes.Black, leftMargin, yPos);
                    yPos += 25;
                    g.DrawString("Lapas Kelas I Medan", fontSubTitle, Brushes.Black, leftMargin, yPos);
                    yPos += 18;
                    string period = string.Format("Periode: {0} s/d {1} | Jenis: {2} | Layanan: {3}",
                        dtpStartDate.Value.ToString("dd/MM/yyyy"),
                        dtpEndDate.Value.ToString("dd/MM/yyyy"),
                        cbReportType.SelectedItem,
                        cbCategory.SelectedItem);
                    g.DrawString(period, fontSubTitle, Brushes.Gray, leftMargin, yPos);
                    yPos += 20;
                    g.DrawLine(Pens.Black, leftMargin, yPos, leftMargin + pageWidth, yPos);
                    yPos += 10;

                    // Print Column Headers
                    float colWidth = pageWidth / currentData.Columns.Count;
                    for (int c = 0; c < currentData.Columns.Count; c++)
                    {
                        RectangleF headerRect = new RectangleF(leftMargin + (c * colWidth), yPos, colWidth, rowHeight);
                        g.FillRectangle(new SolidBrush(Color.FromArgb(19, 24, 38)), headerRect);
                        g.DrawString(currentData.Columns[c].ColumnName, fontHeader, Brushes.White, headerRect,
                            new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                    }
                    yPos += rowHeight;
                }

                // Print Data Rows
                float colW = pageWidth / currentData.Columns.Count;
                while (currentRow < currentData.Rows.Count)
                {
                    if (yPos + rowHeight > pe.MarginBounds.Bottom)
                    {
                        pe.HasMorePages = true;
                        return;
                    }

                    // Alternating row color
                    if (currentRow % 2 == 1)
                    {
                        g.FillRectangle(new SolidBrush(Color.FromArgb(245, 248, 252)),
                            leftMargin, yPos, pageWidth, rowHeight);
                    }

                    for (int c = 0; c < currentData.Columns.Count; c++)
                    {
                        RectangleF cellRect = new RectangleF(leftMargin + (c * colW), yPos, colW, rowHeight);
                        g.DrawString(currentData.Rows[currentRow][c].ToString(), fontData, Brushes.Black, cellRect,
                            new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                    }
                    yPos += rowHeight;
                    currentRow++;
                }

                // Footer
                yPos += 10;
                g.DrawLine(Pens.Black, leftMargin, yPos, leftMargin + pageWidth, yPos);
                yPos += 5;
                g.DrawString("Dicetak: " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss") + " | SIKAWAN LAGUSTA",
                    fontSubTitle, Brushes.Gray, leftMargin, yPos);

                pe.HasMorePages = false;
            };

            PrintPreviewDialog ppd = new PrintPreviewDialog
            {
                Document = pd,
                Width = 1000,
                Height = 700
            };

            currentRow = 0;
            ppd.ShowDialog();
        }
    }
}

