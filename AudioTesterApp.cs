using System;
using System.Drawing;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using System.Threading;
using System.Windows.Forms;

namespace AudioTester
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TestForm());
        }
    }

    public class TestForm : Form
    {
        private TextBox txtFile;
        private TextBox txtLog;
        private Label lblStatus;

        // DLL Import untuk WinMM MCI (Metode 1)
        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        private static extern long mciSendString(string command, string buffer, int bufferSize, IntPtr hwndCallback);

        // DLL Import untuk PlaySound (Metode 4)
        [DllImport("winmm.dll", EntryPoint = "PlaySound", CharSet = CharSet.Auto)]
        private static extern bool PlaySound(string pszSound, IntPtr hmod, uint fdwSound);

        public TestForm()
        {
            this.Text = "Tester Pemutar Suara MP3/WAV - SIKAWAN LAGUSTA";
            this.Size = new Size(600, 520);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            BuildUI();
        }

        private void BuildUI()
        {
            Label lblSelect = new Label { Text = "File Audio yang Diuji (Folder 'audio'):", Location = new Point(20, 15), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            this.Controls.Add(lblSelect);

            txtFile = new TextBox { Text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "audio", "nomor.mp3"), Location = new Point(20, 35), Width = 440, Font = new Font("Segoe UI", 9) };
            this.Controls.Add(txtFile);

            Button btnBrowse = new Button { Text = "Cari...", Location = new Point(470, 33), Width = 90, Height = 27 };
            btnBrowse.Click += (s, e) =>
            {
                OpenFileDialog od = new OpenFileDialog { Filter = "Audio Files (*.mp3;*.wav)|*.mp3;*.wav|All Files (*.*)|*.*" };
                if (od.ShowDialog() == DialogResult.OK) txtFile.Text = od.FileName;
            };
            this.Controls.Add(btnBrowse);

            lblStatus = new Label { Text = "Status: Siap Menguji", Location = new Point(20, 70), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = Color.Blue };
            this.Controls.Add(lblStatus);

            // Group Tombol Pengujian
            int top = 105;

            // METODE 1
            Button btnMci = CreateTestButton("1. WinMM MCI (mp3/wav)", top, (s, e) => TestMethod1_MCI());
            this.Controls.Add(btnMci);

            // METODE 2
            Button btnWmp = CreateTestButton("2. Windows Media Player COM (mp3/wav)", top + 45, (s, e) => TestMethod2_WMP());
            this.Controls.Add(btnWmp);

            // METODE 3
            Button btnPowerShell = CreateTestButton("3. System.Media MediaPlayer via PowerShell (mp3/wav)", top + 90, (s, e) => TestMethod3_PowerShell());
            this.Controls.Add(btnPowerShell);

            // METODE 4
            Button btnSoundPlayer = CreateTestButton("4. System.Media SoundPlayer Native (.wav saja)", top + 135, (s, e) => TestMethod4_SoundPlayer());
            this.Controls.Add(btnSoundPlayer);

            // METODE 5
            Button btnSpeech = CreateTestButton("5. Speech Synthesizer TTS (Text-To-Speech)", top + 180, (s, e) => TestMethod5_Speech());
            this.Controls.Add(btnSpeech);

            // Log Console Box
            txtLog = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Location = new Point(20, top + 235), Size = new Size(540, 120), Font = new Font("Consolas", 8.5f), ReadOnly = true, BackColor = Color.FromArgb(20, 20, 20), ForeColor = Color.Lime };
            this.Controls.Add(txtLog);
        }

        private Button CreateTestButton(string text, int top, EventHandler onClick)
        {
            Button btn = new Button
            {
                Text = text,
                Location = new Point(20, top),
                Size = new Size(540, 36),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(240, 244, 248),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            btn.Click += onClick;
            return btn;
        }

        private void Log(string message)
        {
            txtLog.AppendText(string.Format("[{0}] {1}\r\n", DateTime.Now.ToString("HH:mm:ss"), message));
            lblStatus.Text = "Status: " + message;
        }

        // =========================================================================
        // METODE 1: WinMM MCI (Command-based MP3 Playback)
        // =========================================================================
        private void TestMethod1_MCI()
        {
            string path = txtFile.Text;
            Log("Uji Metode 1 (WinMM MCI)...");

            if (!File.Exists(path)) { Log("ERROR: File tidak ditemukan -> " + path); return; }

            Thread t = new Thread(() =>
            {
                try
                {
                    mciSendString("close test_mp3", null, 0, IntPtr.Zero);
                    string cmdOpen = string.Format("open \"{0}\" type mpegvideo alias test_mp3", path);
                    long r1 = mciSendString(cmdOpen, null, 0, IntPtr.Zero);
                    long r2 = mciSendString("play test_mp3 wait", null, 0, IntPtr.Zero);
                    mciSendString("close test_mp3", null, 0, IntPtr.Zero);

                    this.Invoke((MethodInvoker)delegate { Log("Metode 1 Selesai (Result Code: " + r1 + "/" + r2 + ")"); });
                }
                catch (Exception ex)
                {
                    this.Invoke((MethodInvoker)delegate { Log("Metode 1 Error: " + ex.Message); });
                }
            }) { IsBackground = true };
            t.Start();
        }

        // =========================================================================
        // METODE 2: Windows Media Player COM (late-binding)
        // =========================================================================
        private void TestMethod2_WMP()
        {
            string path = txtFile.Text;
            Log("Uji Metode 2 (Windows Media Player COM)...");

            if (!File.Exists(path)) { Log("ERROR: File tidak ditemukan -> " + path); return; }

            Thread t = new Thread(() =>
            {
                try
                {
                    Type wmpType = Type.GetTypeFromProgID("WMPlayer.OCX");
                    if (wmpType == null) { this.Invoke((MethodInvoker)delegate { Log("ERROR: WMPlayer.OCX tidak terdaftar di Windows!"); }); return; }

                    object wmp = Activator.CreateInstance(wmpType);
                    object controls = wmp.GetType().InvokeMember("controls", System.Reflection.BindingFlags.GetProperty, null, wmp, null);
                    
                    wmp.GetType().InvokeMember("URL", System.Reflection.BindingFlags.SetProperty, null, wmp, new object[] { path });
                    controls.GetType().InvokeMember("play", System.Reflection.BindingFlags.InvokeMethod, null, controls, null);

                    Thread.Sleep(3000); // Biarkan berbunyi selama 3 detik
                    this.Invoke((MethodInvoker)delegate { Log("Metode 2 Berhasil Dijalankan!"); });
                }
                catch (Exception ex)
                {
                    this.Invoke((MethodInvoker)delegate { Log("Metode 2 Error: " + ex.Message); });
                }
            }) { IsBackground = true };
            t.Start();
        }

        // =========================================================================
        // METODE 3: System.Windows.Media via PowerShell Process
        // =========================================================================
        private void TestMethod3_PowerShell()
        {
            string path = txtFile.Text;
            Log("Uji Metode 3 (PowerShell Audio)...");

            if (!File.Exists(path)) { Log("ERROR: File tidak ditemukan -> " + path); return; }

            Thread t = new Thread(() =>
            {
                try
                {
                    string psCommand = string.Format(
                        "Add-Type -AssemblyName presentationCore; $player = New-Object System.Windows.Media.MediaPlayer; $player.Open('{0}'); $player.Play(); Start-Sleep -s 3",
                        path.Replace("'", "''"));

                    System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" + psCommand + "\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };

                    using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi))
                    {
                        p.WaitForExit(5000);
                    }
                    this.Invoke((MethodInvoker)delegate { Log("Metode 3 Selesai!"); });
                }
                catch (Exception ex)
                {
                    this.Invoke((MethodInvoker)delegate { Log("Metode 3 Error: " + ex.Message); });
                }
            }) { IsBackground = true };
            t.Start();
        }

        // =========================================================================
        // METODE 4: SoundPlayer (.WAV Only)
        // =========================================================================
        private void TestMethod4_SoundPlayer()
        {
            string path = txtFile.Text;
            Log("Uji Metode 4 (SoundPlayer)...");

            if (!path.ToLower().EndsWith(".wav"))
            {
                Log("PERINGATAN: Metode 4 hanya mendukung format .wav! (Bukan .mp3)");
                return;
            }

            try
            {
                using (SoundPlayer sp = new SoundPlayer(path))
                {
                    sp.Play();
                }
                Log("Metode 4 Berhasil Dipanggil!");
            }
            catch (Exception ex)
            {
                Log("Metode 4 Error: " + ex.Message);
            }
        }

        // =========================================================================
        // METODE 5: Speech Synthesizer (Text-To-Speech Bawaan OS)
        // =========================================================================
        private void TestMethod5_Speech()
        {
            Log("Uji Metode 5 (Speech Synthesizer TTS)...");

            Thread t = new Thread(() =>
            {
                try
                {
                    using (SpeechSynthesizer synth = new SpeechSynthesizer())
                    {
                        synth.Volume = 100;
                        synth.Speak("Nomor antrean A 0 0 1 menuju loket 1");
                    }
                    this.Invoke((MethodInvoker)delegate { Log("Metode 5 (TTS) Selesai Suara!"); });
                }
                catch (Exception ex)
                {
                    this.Invoke((MethodInvoker)delegate { Log("Metode 5 Error: " + ex.Message); });
                }
            }) { IsBackground = true };
            t.Start();
        }
    }
}