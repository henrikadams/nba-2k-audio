namespace NBA2KAudio
{
    #region Using Directives

    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Reflection;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Controls;

    using LeftosCommonLibrary;

    using Microsoft.Win32;

    using NAudio.Wave;

    #endregion

    /// <summary>Interaction logic for MainWindow.xaml</summary>
    public partial class MainWindow : Window
    {
        private const int ChunkBufferSize = 25000;
        private static readonly byte[] ChunkHeader = new byte[] { 105, 161, 190, 210 };
        private static readonly byte[] StereoHeader = new byte[] { 42, 49, 115, 98 };
        private static readonly string UpdateFileLocalPath = App.AppDocsPath + @"audversion.txt";

        private readonly List<long> _curChunkOffsets;
        private readonly List<long> _curSongOffsets;
        private DirectSoundOut _audioOutput;
        private int _curChannels;
        private long _curFileLength;
        private int _curPlayingID;
        private string _playbackFn = "";
        private long _userSongLength;
        private WaveChannel32 _wc;
        private WaveFileReader _wfr;

        public MainWindow()
        {
            InitializeComponent();

            if (!Directory.Exists(App.AppDocsPath))
            {
                Directory.CreateDirectory(App.AppDocsPath);
            }

            _curChunkOffsets = new List<long>();
            _curSongOffsets = new List<long>();
            _allSongs = new List<Song>();
            _matchingSongs = new ObservableCollection<Song>();

            Tools.AppName = App.AppName;
            Tools.AppRegistryKey = App.AppRegistryKey;
            Tools.OpenRegistryKey(true);
        }

        private List<Song> _allSongs { get; set; }
        private ObservableCollection<Song> _matchingSongs { get; set; }

        private void btnReplaceSong_Click(object sender, RoutedEventArgs e)
        {
            if (String.IsNullOrWhiteSpace(txtSongFile.Text) || String.IsNullOrWhiteSpace(txtJukeboxFile.Text)
                || dgSongs.SelectedItems.Count != 1)
            {
                MessageBox.Show(
                    "You need to do all the following before replacing a song:\n\n" + "- Select the audio file you want to import.\n"
                    + "- Select the NBA 2K BIN audio file you want to import into.\n" + "- Select a song from the grid to replace.");
                return;
            }

            var ms = new MemoryStream(File.ReadAllBytes(txtSongFile.Text));
            BinaryWriter bw;
            try
            {
                bw = new BinaryWriter(File.Open(txtJukeboxFile.Text, FileMode.Open, FileAccess.Write, FileShare.ReadWrite));
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "An error happened while trying to open the NBA 2K audio file in order to replace a segment.\n\nDescription: "
                    + ex.Message,
                    App.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            var songToReplace = (Song) dgSongs.SelectedItem;
            bw.BaseStream.Position = _curChunkOffsets[songToReplace.FirstChunkID];

            var bytesWritten = 0;
            for (var i = songToReplace.FirstChunkID; i <= songToReplace.LastChunkID; i++)
            {
                var buf = new byte[ChunkBufferSize];
                for (var j = 0; j < ChunkBufferSize; j++)
                {
                    buf[j] = 0;
                }

                bw.BaseStream.Position += 58L;
                for (var j = 0; j < 10; j++)
                {
                    bw.Write((byte) (j + 1));
                    bw.BaseStream.Position += 3;
                }
                bw.BaseStream.Position -= 2;
                var chunkLength = (int) getChunkLength(i);

                if (bytesWritten == ms.Length)
                {
                    bw.Write(buf, 0, chunkLength);
                    bw.Flush();
                    continue;
                }

                if (bytesWritten + chunkLength > ms.Length)
                {
                    var newChunkLength = (int) (ms.Length - bytesWritten);
                    ms.Read(buf, 0, newChunkLength);
                    bytesWritten += newChunkLength;
                }
                else
                {
                    ms.Read(buf, 0, chunkLength);
                    bytesWritten += chunkLength;
                }
                bw.Write(buf, 0, chunkLength);
                bw.Flush();
            }
            bw.Close();
            ms.Close();

            MessageBox.Show("Song/audio segment replaced!");
        }

        private void parseSongs()
        {
            var songs = new List<Song>();
            for (var i = 0; i < _curSongOffsets.Count; i++)
            {
                var song = new Song();
                song.FirstChunkID = _curChunkOffsets.IndexOf(_curSongOffsets[i]);
                song.Offset = _curSongOffsets[i];
                if (i < _curSongOffsets.Count - 1)
                {
                    song.LastChunkID = _curChunkOffsets.IndexOf(_curSongOffsets[i + 1]) - 1;
                    song.ChunkCount = song.LastChunkID - song.FirstChunkID + 1;
                    song.Length = _curChunkOffsets[song.LastChunkID + 1] - _curChunkOffsets[song.FirstChunkID] - (96 * song.ChunkCount);
                }
                else
                {
                    song.LastChunkID = _curChunkOffsets.Count - 1;
                    song.ChunkCount = song.LastChunkID - song.FirstChunkID + 1;
                    song.Length = _curFileLength - _curChunkOffsets[song.FirstChunkID] - (96 * song.ChunkCount);
                }
                song.ID = i;
                song.Duration = (double) song.Length / 4000;
                songs.Add(song);
            }
            _allSongs = new List<Song>(songs);
            _matchingSongs = new ObservableCollection<Song>(_allSongs);
        }

        private long getChunkLength(int id)
        {
            if (id != _curChunkOffsets.Count - 1)
            {
                return _curChunkOffsets[id + 1] - _curChunkOffsets[id] - 96;
            }
            else
            {
                return _curFileLength - _curChunkOffsets[id] - 96;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            stiStatus.Content = "Ready";

            var w = new BackgroundWorker();
            w.DoWork += (o, args) => CheckForUpdates();
            w.RunWorkerAsync();
        }

        private void btnSelectSong_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog();
            ofd.Filter = "MP3 Files (*.mp3)|*.mp3|WAV Files (*.wav)|*.wav|xWMA Stripped DAT Files (*.dat)|*.dat|All Files (*.*)|*.*";
            ofd.InitialDirectory = Tools.GetRegistrySetting("LastSongPath", "");

            if (ofd.ShowDialog() == false)
            {
                return;
            }

            var fn = ofd.FileName;
            Tools.SetRegistrySetting("LastSongPath", Path.GetDirectoryName(ofd.FileName));

            MemoryStream userSongData = null;
            if (Path.GetExtension(fn) == ".mp3")
            {
                var datFilePath = App.AppDocsPath + Path.GetFileNameWithoutExtension(fn) + ".dat";
                var done = false;
                if (File.Exists(datFilePath))
                {
                    var mbr = MessageBox.Show(
                        "File " + datFilePath + " already exists. Do you want to convert and overwrite?",
                        App.AppName,
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    if (mbr == MessageBoxResult.No)
                    {
                        userSongData = new MemoryStream(File.ReadAllBytes(datFilePath));
                        txtSongFile.Text = datFilePath;
                        done = true;
                    }
                }

                if (!done)
                {
                    var tempWavePath = Path.GetTempFileName();
                    /* var command = Directory.GetCurrentDirectory() + "\\Sox\\sox.exe";
                    var args = "\"" + ofd.FileName + "\" \"" + tempWavePath + "\" remix 2 rate 44100"; */
                    var command = "\"" + Directory.GetCurrentDirectory() + "\\Sox\\sox.exe\"";
                    var args = "\"" + ofd.FileName + "\" -t wav \"" + tempWavePath + "\" rate 44100";
                    try
                    {
                        var p = Process.Start(new ProcessStartInfo(command, args));
                        p.WaitForExit();

                        WavToxWMADat(tempWavePath, datFilePath);
                        File.Delete(tempWavePath);
                    }
                    catch (Exception ex)
                    {
                        errorMessageBox("An error occured while trying to convert the user audio file.", ex);
                        return;
                    }
                    userSongData = new MemoryStream(File.ReadAllBytes(datFilePath));
                    txtSongFile.Text = datFilePath;
                }
            }
            else if (Path.GetExtension(fn) == ".wav")
            {
                var datFilePath = App.AppDocsPath + Path.GetFileNameWithoutExtension(fn) + ".dat";
                var done = false;
                if (File.Exists(datFilePath))
                {
                    var mbr = MessageBox.Show(
                        "File " + datFilePath + " already exists. Do you want to convert and overwrite?",
                        App.AppName,
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    if (mbr == MessageBoxResult.No)
                    {
                        userSongData = new MemoryStream(File.ReadAllBytes(datFilePath));
                        txtSongFile.Text = datFilePath;
                        done = true;
                    }
                }

                if (!done)
                {
                    try
                    {
                        WavToxWMADat(fn, datFilePath);
                    }
                    catch (Exception ex)
                    {
                        errorMessageBox("An error occured while trying to convert the user audio file.", ex);
                        return;
                    }
                    userSongData = new MemoryStream(File.ReadAllBytes(datFilePath));
                    txtSongFile.Text = datFilePath;
                }
            }
            else
            {
                userSongData = new MemoryStream(File.ReadAllBytes(fn));
                txtSongFile.Text = fn;
            }
            _userSongLength = userSongData.Length;
            var userSongDuration = (double) _userSongLength / 4000;
            var text = String.Format("Length: {0} bytes\nDuration: {1:F2}", _userSongLength, userSongDuration);
            txbSongInfo.Text = text;

            userSongData.Close();

            refreshMatchingSongs();
        }

        private void refreshMatchingSongs()
        {
            if (_allSongs.Count == 0)
            {
                return;
            }
            _matchingSongs = new ObservableCollection<Song>(_allSongs.Where(song => song.Length >= _userSongLength).ToList());
            dgSongs.ItemsSource = _matchingSongs;
        }

        private static void WavToxWMADat(string tempWavePath, string datFilePath)
        {
            var xmaPath = Path.GetTempFileName();
            var command = Directory.GetCurrentDirectory() + "\\xWMAEncode.exe";
            var args = "-b 32000 \"" + tempWavePath + "\" \"" + xmaPath + "\"";
            var px = Process.Start(new ProcessStartInfo(command, args));
            px.WaitForExit();

            var ms = new MemoryStream(File.ReadAllBytes(xmaPath));
            var tempbuf = new byte[4];
            byte[] header = { 100, 97, 116, 97 };
            while (true)
            {
                ms.Read(tempbuf, 0, 4);
                if (!tempbuf.SequenceEqual(header))
                {
                    ms.Position -= 3;
                    continue;
                }

                ms.Position += 4;
                var tempbuf2 = new byte[4096];
                var remaining = ms.Length - ms.Position;
                var dataBytes = new List<byte>();
                while (remaining > 0)
                {
                    for (var i = 0; i < 4096; i++)
                    {
                        tempbuf2[i] = 0;
                    }
                    var toRead = remaining > 4096 ? 4096 : remaining;
                    ms.Read(tempbuf2, 0, (int) toRead);

                    for (var i = 0; i < toRead; i++)
                    {
                        dataBytes.Add(tempbuf2[i]);
                    }
                    remaining = ms.Length - ms.Position;
                }
                File.WriteAllBytes(datFilePath, dataBytes.ToArray());
                break;
            }
            ms.Close();
            File.Delete(xmaPath);
        }

        private async void btnSelectJukebox_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog();
            ofd.Filter = "NBA 2K Jukebox Music Files|jukeboxmusic.bin|NBA 2K Audio Files (*.bin)|*.bin|All Files (*.*)|*.*";
            ofd.InitialDirectory = Tools.GetRegistrySetting("LastJukeboxPath", "");

            if (ofd.ShowDialog() == false)
            {
                return;
            }

            onPlaybackStopped(null, null);

            var fn = ofd.FileName;

            txtJukeboxFile.Text = fn;
            Tools.SetRegistrySetting("LastJukeboxPath", Path.GetDirectoryName(fn));

            IsEnabled = false;
            try
            {
                await TaskEx.Run(() => parseBinFile(fn));
            }
            catch (Exception ex)
            {
                errorMessageBox("An error happened while trying to parse the NBA 2K Audio file.", ex);
                return;
            }
            await TaskEx.Run(() => parseSongs());

            refreshMatchingSongs();
            dgSongs.ItemsSource = _matchingSongs;
            IsEnabled = true;
        }

        private static void errorMessageBox(string msg, Exception ex)
        {
            MessageBox.Show(msg + "\n\n" + ex.Message, App.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void txtSongFile_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (String.IsNullOrWhiteSpace(txtSongFile.Text))
            {
                return;
            }

            txtSongFile.ScrollToHorizontalOffset(txtSongFile.GetRectFromCharacterIndex(txtSongFile.Text.Length).Right);
        }

        private void txtJukeboxFile_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (String.IsNullOrWhiteSpace(txtJukeboxFile.Text))
            {
                return;
            }

            txtJukeboxFile.ScrollToHorizontalOffset(txtJukeboxFile.GetRectFromCharacterIndex(txtJukeboxFile.Text.Length).Right);
        }

        private void parseBinFile(string fn)
        {
            _curChunkOffsets.Clear();
            _curSongOffsets.Clear();

            Stream ms;

            if (new FileInfo(fn).Length > 262144000)
            {
                ms = new FileStream(fn, FileMode.Open, FileAccess.Read);
            }
            else
            {
                ms = new MemoryStream(File.ReadAllBytes(fn));
            }

            var headBuf = new byte[4];
            ms.Position = 102;
            var monoHeader = new byte[] { 8, 220, 66, 64 };
            ms.Read(headBuf, 0, 4);
            _curChannels = !headBuf.SequenceEqual(StereoHeader) ? 1 : 2;

            Tools.AppInvoke(() => stiStatus.Content = "Please wait (0% completed)...");
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            var oldPerc = 0;
            _curFileLength = ms.Length;
            var bigBuf = new byte[1048576];
            var idBuf = new byte[2];
            while (ms.Length - ms.Position > 106)
            {
                var curPerc = (int) (ms.Position * 100.0 / ms.Length);
                if (curPerc != oldPerc)
                {
                    var timeElapsed = stopwatch.ElapsedMilliseconds;
                    stopwatch.Reset();
                    stopwatch.Start();
                    oldPerc = curPerc;
                    var perc = curPerc;
                    Tools.AppInvoke(
                        () =>
                        stiStatus.Content =
                        String.Format(
                            "Please wait ({0}% completed - ETA: {1:0} minutes, {2:0} seconds)...",
                            perc,
                            Math.Floor(((timeElapsed / 1000.0) * (100 - perc)) / 60),
                            Math.Floor(((timeElapsed / 1000.0) * (100 - perc)) % 60)));
                }
                var bytesReadCount = ms.Read(bigBuf, 0, 1048576);
                ms.Position -= bytesReadCount;
                var found = false;
                for (var i = 0; i < bytesReadCount - 3; i++)
                {
                    if (bigBuf[i] == ChunkHeader[0] && bigBuf[i + 1] == ChunkHeader[1] && bigBuf[i + 2] == ChunkHeader[2]
                        && bigBuf[i + 3] == ChunkHeader[3])
                    {
                        found = true;
                        ms.Position += i;
                        break;
                    }
                }
                if (!found)
                {
                    ms.Position += bytesReadCount >= 4 ? bytesReadCount - 3 : bytesReadCount;
                    continue;
                }
                /*
                ms.Read(headBuf, 0, 4);
                if (!headBuf.SequenceEqual(ChunkHeader))
                {
                    ms.Position -= 3;
                    continue;
                }
                */
                _curChunkOffsets.Add(ms.Position);

                ms.Position += 36;
                ms.Read(idBuf, 0, 2);
                var chunkID = idBuf[1] * 256 + idBuf[0];
                if (chunkID != 0)
                {
                    continue;
                }
                _curSongOffsets.Add(ms.Position - 38);

                // The method below for detecting files works for most files, but not all, so it's replaced with the 
                // code above
                /*
                    ms.Position += 98;
                    ms.Read(headBuf, 0, 4);
                    if (!headBuf.SequenceEqual(SongHeader))
                    {
                        continue;
                    }
                    _curSongOffsets.Add(ms.Position - 106);
                    */
            }
            stopwatch.Stop();

            Tools.AppInvoke(() => stiStatus.Content = "Ready");
            Console.WriteLine(String.Format("{0} songs, {1} chunks detected.", _curSongOffsets.Count, _curChunkOffsets.Count));
            //MessageBox.Show(String.Format("{0} songs, {1} chunks detected.", _curSongOffsets.Count, _curChunkOffsets.Count));
        }

        private void btnSaveNames_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog();
            sfd.InitialDirectory = App.AppDocsPath;
            sfd.Filter = "Audio File Description Files (*.afd)|*.afd";

            if (sfd.ShowDialog() == false)
            {
                return;
            }

            var fn = sfd.FileName;

            var list = _allSongs.Select(song => String.Format("{0}:{1}", song.ID, song.Description)).ToList();
            File.WriteAllLines(fn, list);
        }

        private void btnLoadNames_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog();
            ofd.InitialDirectory = App.AppDocsPath;
            ofd.Filter = "Audio File Description Files (*.afd)|*.afd";

            if (ofd.ShowDialog() == false)
            {
                return;
            }

            var fn = ofd.FileName;

            var list = File.ReadAllLines(fn).ToList();
            foreach (var item in list)
            {
                var parts = item.Split(new[] { ':' }, 2);
                try
                {
                    _allSongs.Single(s => s.ID == parts[0].ToInt32()).Description = parts[1];
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Exception " + ex.Message + " while trying to parse the names file.");
                }
            }

            refreshMatchingSongs();
        }

        private void btnClearCache_Click(object sender, RoutedEventArgs e)
        {
            var mbr =
                MessageBox.Show(
                    "Any of your own audio files that you select gets converted to an NBA 2K-compatible DAT file in " + App.AppDocsPath
                    + " in order to allow you to reuse it without the need to wait for it to be converted again.\n\n"
                    + "Would you like to delete all converted files?\nThis will not affect your original MP3/WAV files.",
                    App.AppName,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
            if (mbr == MessageBoxResult.No)
            {
                return;
            }

            var files = Directory.GetFiles(App.AppDocsPath, "*.dat");
            foreach (var file in files)
            {
                if (txtSongFile.Text == file)
                {
                    txtSongFile.Text = "";
                    txbSongInfo.Text = "";
                }

                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Exception thrown while trying to delete a file from the song cache: {0}", ex.Message);
                }
            }
        }

        private void btnExport_Click(object sender, RoutedEventArgs e)
        {
            if (String.IsNullOrWhiteSpace(txtJukeboxFile.Text) || dgSongs.SelectedItems.Count != 1)
            {
                return;
            }

            var sfd = new SaveFileDialog();
            sfd.InitialDirectory = App.AppDocsPath;
            sfd.Filter = "WAV files (*.wav)|*.wav|xWMA Stripped DAT Files (*.dat)|*.dat|All Files (*.*)|*.*";
            sfd.AddExtension = true;

            if (sfd.ShowDialog() == false)
            {
                return;
            }

            var fn = sfd.FileName;
            var ext = Path.GetExtension(sfd.FileName);

            var song = (Song) dgSongs.SelectedItem;

            try
            {
                using (var br = new BinaryReader(File.OpenRead(txtJukeboxFile.Text)))
                {
                    if (ext == ".dat")
                    {
                        exportSong(song, fn, br);
                    }
                    else
                    {
                        exportSongToWav(song, br, fn);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "An error happened while trying to export the segment.\n\n" + ex.Message,
                    App.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            MessageBox.Show("Audio segment exported to " + fn + ".");
        }

        private void exportSong(Song song, string destFn, BinaryReader br)
        {
            var data = extractSongData(song, br);

            File.WriteAllBytes(destFn, data);
        }

        private byte[] extractSongData(Song song, BinaryReader br)
        {
            var buf = new byte[ChunkBufferSize];
            var data = new List<byte>();
            br.BaseStream.Position = song.Offset;
            for (var i = song.FirstChunkID; i <= song.LastChunkID; i++)
            {
                br.BaseStream.Position += 96;
                var chunkLen = getChunkLength(i);
                br.Read(buf, 0, (int) chunkLen);
                data.AddRange(buf.Take((int) chunkLen));
            }
            return data.ToArray();
        }

        /// <summary>Checks for software updates asynchronously.</summary>
        /// <param name="showMessage">
        ///     if set to <c>true</c>, a message will be shown even if no update is found.
        /// </param>
        public static void CheckForUpdates(bool showMessage = false)
        {
            //showUpdateMessage = showMessage;
            try
            {
                var webClient = new WebClient();
                var updateUri = "http://www.nba-live.com/leftos/audversion.txt";
                if (!showMessage)
                {
                    webClient.DownloadFileCompleted += CheckForUpdatesCompleted;
                    webClient.DownloadFileAsync(new Uri(updateUri), UpdateFileLocalPath);
                }
                else
                {
                    webClient.DownloadFile(new Uri(updateUri), UpdateFileLocalPath);
                    CheckForUpdatesCompleted(null, null);
                }
            }
            catch
            {
            }
        }

        /// <summary>Checks the downloaded version file to see if there's a newer version, and displays a message if needed.</summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">
        ///     The <see cref="AsyncCompletedEventArgs" /> instance containing the event data.
        /// </param>
        private static void CheckForUpdatesCompleted(object sender, AsyncCompletedEventArgs e)
        {
            string[] updateInfo;
            string[] versionParts;
            try
            {
                updateInfo = File.ReadAllLines(UpdateFileLocalPath);
                versionParts = updateInfo[0].Split('.');
            }
            catch
            {
                return;
            }
            var curVersionParts = Assembly.GetExecutingAssembly().GetName().Version.ToString().Split('.');
            var iVP = new int[versionParts.Length];
            var iCVP = new int[versionParts.Length];
            for (var i = 0; i < versionParts.Length; i++)
            {
                iVP[i] = Convert.ToInt32(versionParts[i]);
                iCVP[i] = Convert.ToInt32(curVersionParts[i]);
                if (iCVP[i] > iVP[i])
                {
                    break;
                }
                if (iVP[i] > iCVP[i])
                {
                    var changelog = "\n\nVersion " + String.Join(".", versionParts);
                    try
                    {
                        for (var j = 2; j < updateInfo.Length; j++)
                        {
                            changelog += "\n" + updateInfo[j].Replace('\t', ' ');
                        }
                    }
                    catch
                    {
                    }
                    var mbr = MessageBox.Show(
                        "A new version is available! Would you like to download it?" + changelog,
                        App.AppName,
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);
                    if (mbr == MessageBoxResult.Yes)
                    {
                        Process.Start(updateInfo[1]);
                        break;
                    }
                    return;
                }
            }
            /*
            if (showUpdateMessage)
                MessageBox.Show("No updates found!");
            */
        }

        private async void btnExportAll_Click(object sender, RoutedEventArgs e)
        {
            if (String.IsNullOrWhiteSpace(txtJukeboxFile.Text))
            {
                return;
            }

            var sfd = new SaveFileDialog();
            sfd.InitialDirectory = App.AppDocsPath;
            sfd.Filter = "WAV files (*.wav)|*.wav|xWMA Stripped DAT Files (*.dat)|*.dat|All Files (*.*)|*.*";
            sfd.Title = "Select a folder and base filename";
            sfd.AddExtension = true;

            if (sfd.ShowDialog() == false)
            {
                return;
            }

            var baseFn = sfd.FileName;
            var ext = Path.GetExtension(baseFn);

            IsEnabled = false;
            stiStatus.Content = "Please wait (0% completed)...";
            var oldPerc = 0;
            try
            {
                using (var br = new BinaryReader(File.OpenRead(txtJukeboxFile.Text)))
                {
                    for (var i = 0; i < _allSongs.Count; i++)
                    {
                        var perc = (int) (i * 100.0 / _allSongs.Count);
                        if (perc != oldPerc)
                        {
                            oldPerc = perc;
                            stiStatus.Content = String.Format("Please wait ({0}% completed)...", perc);
                        }
                        var song = _allSongs[i];
                        var fn = String.Format(
                            "{0}_{1:000000}{2}",
                            Path.GetDirectoryName(baseFn) + "\\" + Path.GetFileNameWithoutExtension(baseFn),
                            song.ID,
                            ext);
                        if (ext == ".dat")
                        {
                            await TaskEx.Run(() => exportSong(song, fn, br));
                        }
                        else
                        {
                            await TaskEx.Run(() => exportSongToWav(song, br, fn));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "An error happened while trying to export the segments.\n\n" + ex.Message,
                    App.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
            IsEnabled = true;
            stiStatus.Content = "Ready";
            MessageBox.Show("Audio segments exported to " + Path.GetDirectoryName(baseFn) + ".");
        }

        private void btnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (btnPlay.Content.ToString() == "Play")
            {
                if (_audioOutput != null && _audioOutput.PlaybackState == PlaybackState.Paused)
                {
                    _audioOutput.Play();
                    btnPlay.Content = "Pause";
                    stiStatus.Content = "Currently playing: File " + _curPlayingID;
                    return;
                }

                try
                {
                    if (!String.IsNullOrWhiteSpace(_playbackFn))
                    {
                        File.Delete(_playbackFn);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Could not delete file {0}: {1}", _playbackFn, ex.Message);
                }

                var jukeboxFile = txtJukeboxFile.Text;
                if (String.IsNullOrWhiteSpace(jukeboxFile) || dgSongs.SelectedItems.Count != 1)
                {
                    return;
                }

                var song = (Song) dgSongs.SelectedItem;
                _curPlayingID = song.ID;

                try
                {
                    var tempFn = Path.GetTempFileName();
                    using (var br = new BinaryReader(File.OpenRead(jukeboxFile)))
                    {
                        _playbackFn = exportSongToWav(song, br, tempFn);
                    }

                    btnPlay.Content = "Pause";
                    stiStatus.Content = "Currently playing: File " + _curPlayingID;

                    _wfr = new WaveFileReader(_playbackFn);

                    _wc = new WaveChannel32(_wfr);

                    _audioOutput = new DirectSoundOut();

                    _audioOutput.PlaybackStopped += onPlaybackStopped;

                    _audioOutput.Init(_wc);

                    _audioOutput.Play();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "An error happened while trying to export the segment.\n\n" + ex.Message,
                        App.AppName,
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
            }
            else if (btnPlay.Content.ToString() == "Pause")
            {
                if (_audioOutput.PlaybackState == PlaybackState.Playing)
                {
                    _audioOutput.Pause();
                    stiStatus.Content = "Currently paused: File " + _curPlayingID;
                    btnPlay.Content = "Play";
                }
            }
        }

        private string exportSongToWav(Song song, BinaryReader br, string wavFn)
        {
            var data = extractSongData(song, br);
            var tempFn = Path.GetDirectoryName(wavFn) + "\\" + Path.GetFileNameWithoutExtension(wavFn) + ".xma";
            File.Copy(_curChannels == 2 ? "audio.xma" : "audiom.xma", tempFn, true);
            using (var bw = new BinaryWriter(File.OpenWrite(tempFn)))
            {
                bw.BaseStream.Position = 6518;
                bw.Write(data);
                bw.Flush();
            }

            var command = Directory.GetCurrentDirectory() + "\\xWMAEncode.exe";
            var args = "\"" + tempFn + "\" \"" + wavFn + "\"";
            var px = Process.Start(new ProcessStartInfo(command, args) { WindowStyle = ProcessWindowStyle.Hidden });
            px.WaitForExit();
            File.Delete(tempFn);
            return wavFn;
        }

        private void onPlaybackStopped(object o, StoppedEventArgs eventArgs)
        {
            if (_audioOutput != null)
            {
                _audioOutput.Dispose();
            }
            if (_wc != null)
            {
                _wc.Dispose();
            }
            if (_wfr != null)
            {
                _wfr.Dispose();
            }
            try
            {
                File.Delete(_playbackFn);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not delete file " + _playbackFn + ": " + ex.Message);
            }
            btnPlay.Content = "Play";
            stiStatus.Content = "Ready";
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            onPlaybackStopped(null, null);
        }

        private void window_Closing(object sender, CancelEventArgs e)
        {
            onPlaybackStopped(null, null);
        }
    }
}