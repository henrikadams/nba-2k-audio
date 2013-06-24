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
        private static readonly byte[] XwmaDataHeader = { 100, 97, 116, 97 };
        private static readonly byte[] XwmaDpdsHeader = { 100, 112, 100, 115 };

        private readonly List<long> _curChunkOffsets;
        private readonly List<int> _curChunkPacketCounts;
        private readonly List<long> _curSongOffsets;
        private DirectSoundOut _audioOutput;
        private int _curChannels;
        private long _curFileLength;
        private int _curPlayingID;
        private string _playbackFn = "";
        private long _userSongLength;
        private WaveChannel32 _wc;
        private WaveFileReader _wfr;
        private List<int> _dpdsTable;

        public MainWindow()
        {
            InitializeComponent();

            _curChunkOffsets = new List<long>();
            _curChunkPacketCounts = new List<int>();
            _curSongOffsets = new List<long>();
            _allSongs = new List<Song>();
            _matchingSongs = new ObservableCollection<Song>();

            if (!Directory.Exists(App.AppDocsPath))
            {
                Directory.CreateDirectory(App.AppDocsPath);
            }

            try
            {
                Directory.Delete(App.AppTempPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Couldn't delete AppTempPath ({0}): {1}", App.AppTempPath, ex.Message);
            }
            if (!Directory.Exists(App.AppTempPath))
            {
                Directory.CreateDirectory(App.AppTempPath);
            }

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
            ms.Position = findArrayInStream(ms, XwmaDataHeader, 0) + 8;
            var lengthToWrite = ms.Length - ms.Position;
            BinaryWriter bw;
            BinaryReader br;
            try
            {
                br = new BinaryReader(File.Open(txtJukeboxFile.Text, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
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
            var lastDpdsEntry = _dpdsTable.Last();
            for (var i = _dpdsTable.Count; i < songToReplace.ChunkCount * 10; i++)
            {
                _dpdsTable.Add(lastDpdsEntry);
            }

            var bytesWritten = 0;
            for (var i = songToReplace.FirstChunkID; i <= songToReplace.LastChunkID; i++)
            {
                var buf = new byte[ChunkBufferSize];
                Array.Clear(buf, 0, buf.Length);

                bw.BaseStream.Position += 56;
                for (var j = 0; j < _curChunkPacketCounts[i]; j++)
                {
                    var curIndex = (i - songToReplace.FirstChunkID) * 10 + j;
                    var toWrite = _dpdsTable[curIndex];
                    if (curIndex > 9)
                    {
                        toWrite -= _dpdsTable[(curIndex / 10) * 10 - 1];
                    }
                    bw.Write(toWrite);
                    if (curIndex % _curChunkPacketCounts[i] == _curChunkPacketCounts[i] - 1)
                    {
                        bw.BaseStream.Position -= 36 + _curChunkPacketCounts[i] * 4;
                        bw.Write(toWrite);
                        bw.BaseStream.Position += 32 + _curChunkPacketCounts[i] * 4;
                    }
                }

                bw.BaseStream.Position = _curChunkOffsets[i] + 56 + (_curChunkPacketCounts[i] * 4);
                var chunkLength = (int) getChunkLength(i);

                if (bytesWritten == lengthToWrite)
                {
                    bw.Write(buf, 0, chunkLength);
                    bw.Flush();
                    continue;
                }

                if (bytesWritten + chunkLength > lengthToWrite)
                {
                    var newChunkLength = (int) (lengthToWrite - bytesWritten);
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
            br.Close();
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
                }
                else
                {
                    song.LastChunkID = _curChunkOffsets.Count - 1;
                    song.ChunkCount = song.LastChunkID - song.FirstChunkID + 1;
                }
                song.Length = 0;
                song.PacketCount = 0;
                for (var j = song.FirstChunkID; j <= song.LastChunkID; j++)
                {
                    song.Length += getChunkLength(j);
                    song.PacketCount += _curChunkPacketCounts[j];
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
                return _curChunkOffsets[id + 1] - _curChunkOffsets[id] - 56 - (_curChunkPacketCounts[id] * 4);
            }
            else
            {
                return _curFileLength - _curChunkOffsets[id] - 56 - (_curChunkPacketCounts[id] * 4);
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
            ofd.Filter =
                "All Compatible Audio Files (*.mp3;*.wav;*.xma)|*.mp3;*.wav;*.xma|MP3 Files (*.mp3)|*.mp3|WAV Files (*.wav)|*.wav|xWMA Files (*.xma)|*.xma|All Files (*.*)|*.*";
            ofd.InitialDirectory = Tools.GetRegistrySetting("LastSongPath", "");

            if (ofd.ShowDialog() == false)
            {
                return;
            }

            var origFn = ofd.FileName;
            Tools.SetRegistrySetting("LastSongPath", Path.GetDirectoryName(origFn));

            MemoryStream userSongData = null;
            if (Path.GetExtension(origFn) == ".mp3")
            {
                var changed = false;
                var normFn = Helper.RemoveDiacritics(origFn, true);
                var newFn = "";
                if (String.Compare(normFn, origFn, StringComparison.CurrentCulture) != 0)
                {
                    changed = true;
                    newFn = App.AppTempPath + Helper.RemoveDiacritics(Path.GetFileName(origFn), true);
                    File.Copy(origFn, newFn, true);
                    origFn = newFn;
                }

                var xmaFilePath = App.AppDocsPath + Path.GetFileNameWithoutExtension(origFn) + ".xma";
                var done = false;
                if (File.Exists(xmaFilePath))
                {
                    var mbr = MessageBox.Show(
                        "File " + xmaFilePath + " already exists. Do you want to convert and overwrite?",
                        App.AppName,
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    if (mbr == MessageBoxResult.No)
                    {
                        userSongData = new MemoryStream(File.ReadAllBytes(xmaFilePath));
                        txtSongFile.Text = xmaFilePath;
                        done = true;
                    }
                }

                if (!done)
                {
                    var tempWavePath = Path.GetTempFileName();
                    var command = "\"" + Directory.GetCurrentDirectory() + "\\Sox\\sox.exe\"";
                    var args = "\"" + origFn + "\" -t wav \"" + tempWavePath + "\" rate 44100";
                    try
                    {
                        var allOutput = "";
                        var p = new Process
                            {
                                StartInfo =
                                    {
                                        FileName = command,
                                        Arguments = args,
                                        RedirectStandardOutput = true,
                                        RedirectStandardError = true,
                                        UseShellExecute = false
                                    }
                            };
                        p.OutputDataReceived += (o, eventArgs) =>
                            {
                                if (eventArgs.Data != null)
                                {
                                    allOutput += eventArgs.Data + "\n";
                                }
                            };
                        p.ErrorDataReceived += (o, eventArgs) =>
                            {
                                if (eventArgs.Data != null)
                                {
                                    allOutput += eventArgs.Data + "\n";
                                }
                            };

                        p.Start();
                        p.BeginOutputReadLine();
                        p.BeginErrorReadLine();
                        p.WaitForExit();

                        if (!File.Exists(tempWavePath))
                        {
                            throw new Exception(
                                String.Format("No WAV file after SOX MP3->WAV conversion.\n\nSOX Output:\n{0}", allOutput));
                        }
                        if (new FileInfo(tempWavePath).Length == 0)
                        {
                            throw new Exception(
                                string.Format("Zero-length WAV file after SOX MP3->WAV conversion.\n\nSOX Output:\n{0}", allOutput));
                        }

                        if (changed)
                        {
                            try
                            {
                                File.Delete(newFn);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("Couldn't delete temporary unescaped name MP3: {0}", ex.Message);
                            }
                        }

                        WavToxWMAFull(tempWavePath, xmaFilePath);

                        if (!File.Exists(xmaFilePath))
                        {
                            throw new Exception("No xWMA file after xWMAEncode WAV-xWMA conversion.");
                        }

                        File.Delete(tempWavePath);
                    }
                    catch (Exception ex)
                    {
                        errorMessageBox("An error occurred while trying to convert the user audio file.", ex);
                        return;
                    }
                    userSongData = new MemoryStream(File.ReadAllBytes(xmaFilePath));
                    txtSongFile.Text = xmaFilePath;
                }
            }
            else if (Path.GetExtension(origFn) == ".wav")
            {
                var xmaFilePath = App.AppDocsPath + Path.GetFileNameWithoutExtension(origFn) + ".xma";
                var done = false;
                if (File.Exists(xmaFilePath))
                {
                    var mbr = MessageBox.Show(
                        "File " + xmaFilePath + " already exists. Do you want to convert and overwrite?",
                        App.AppName,
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    if (mbr == MessageBoxResult.No)
                    {
                        userSongData = new MemoryStream(File.ReadAllBytes(xmaFilePath));
                        txtSongFile.Text = xmaFilePath;
                        done = true;
                    }
                }

                if (!done)
                {
                    try
                    {
                        WavToxWMAFull(origFn, xmaFilePath);

                        if (!File.Exists(xmaFilePath))
                        {
                            throw new Exception("No xWMA file after xWMAEncode WAV-xWMA conversion.");
                        }
                    }
                    catch (Exception ex)
                    {
                        errorMessageBox("An error occured while trying to convert the user audio file.", ex);
                        return;
                    }
                    userSongData = new MemoryStream(File.ReadAllBytes(xmaFilePath));
                    txtSongFile.Text = xmaFilePath;
                }
            }
            else
            {
                userSongData = new MemoryStream(File.ReadAllBytes(origFn));
                txtSongFile.Text = origFn;
            }
            var dataPos = findArrayInStream(userSongData, XwmaDataHeader, 0) + 8;
            _userSongLength = userSongData.Length - dataPos;
            var userSongDuration = (double) _userSongLength / 4000;
            var text = String.Format("Length: {0} bytes\nDuration: {1:F2}", _userSongLength, userSongDuration);
            txbSongInfo.Text = text;

            _dpdsTable = new List<Int32>();
            var dpdsPos = findArrayInStream(userSongData, XwmaDpdsHeader, 0) + 8;
            userSongData.Position = dpdsPos;
            var buf = new byte[4];
            while (true)
            {
                userSongData.Read(buf, 0, 4);
                var dec = BitConverter.ToInt32(buf, 0);
                if (dec == 1635017060) // buf == data
                {
                    break;
                }
                _dpdsTable.Add(dec);
            }

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
            WavToxWMAFull(tempWavePath, xmaPath);

            var ms = new MemoryStream(File.ReadAllBytes(xmaPath));
            var tempbuf = new byte[4];
            while (true)
            {
                ms.Read(tempbuf, 0, 4);
                if (!tempbuf.SequenceEqual(XwmaDataHeader))
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

        private static void WavToxWMAFull(string wavPath, string xmaPath)
        {
            var command = Directory.GetCurrentDirectory() + "\\xWMAEncode.exe";
            var args = "-b 32000 \"" + wavPath + "\" \"" + xmaPath + "\"";
            var px = Process.Start(new ProcessStartInfo(command, args));
            px.WaitForExit();
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
            _curChunkPacketCounts.Clear();
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

            ms.Position = 0;
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
                var pos = findArrayInStream(ms, ChunkHeader, ms.Position);
                if (pos != -1)
                {
                    _curChunkOffsets.Add(pos);
                }
                else
                {
                    break;
                }
                ms.Position = pos + 12;
                ms.Read(idBuf, 0, 2);
                _curChunkPacketCounts.Add(BitConverter.ToInt16(idBuf, 0));

                ms.Position = pos + 36;
                ms.Read(idBuf, 0, 2);
                var chunkID = BitConverter.ToInt16(idBuf, 0);
                if (chunkID != 0)
                {
                    continue;
                }
                _curSongOffsets.Add(ms.Position - 38);
            }
            stopwatch.Stop();

            Tools.AppInvoke(() => stiStatus.Content = "Ready");
            Console.WriteLine(String.Format("{0} songs, {1} chunks detected.", _curSongOffsets.Count, _curChunkOffsets.Count));
            //MessageBox.Show(String.Format("{0} songs, {1} chunks detected.", _curSongOffsets.Count, _curChunkOffsets.Count));
        }

        private long findArrayInStream(Stream s, byte[] arr, long sStartPosition, int bufferSize = 1048576)
        {
            s.Position = sStartPosition;
            var buffer = new byte[bufferSize];
            var foundPosition = -1L;
            while (s.Position < s.Length)
            {
                var bytesReadCount = s.Read(buffer, 0, bufferSize);
                var found = true;
                var arrLen = arr.Length;
                for (var i = 0; i < bytesReadCount - (arrLen - 1); i++)
                {
                    found = true;
                    if (s.Position - bytesReadCount + i == 14946)
                    {
                    }
                    for (var j = 0; j < arrLen; j++)
                    {
                        if (buffer[i + j] != arr[j])
                        {
                            found = false;
                            break;
                        }
                    }
                    if (found)
                    {
                        foundPosition = s.Position - bytesReadCount + i;
                        break;
                    }
                }
                if (found)
                {
                    break;
                }
            }
            return foundPosition;
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
                    "Any of your own audio files that you select gets converted to an NBA 2K-compatible XMA file in " + App.AppDocsPath
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
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Exception thrown while trying to delete a file from the song cache: {0}", ex.Message);
                }
            }

            files = Directory.GetFiles(App.AppDocsPath, "*.xma");
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

            files = Directory.GetFiles(App.AppTempPath);
            foreach (var file in files)
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Exception thrown while trying to delete a file from the temp folder: {0}", ex.Message);
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
            sfd.Filter = "WAV files (*.wav)|*.wav|xWMA Files (*.xma)|*.xma|All Files (*.*)|*.*";
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
                    if (ext == ".xma")
                    {
                        exportSongToXwma(song, br, fn);
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

        private byte[] extractSongData(Song song, BinaryReader br, out List<Int32> dpdsTable)
        {
            var buf = new byte[ChunkBufferSize];
            var data = new List<byte>();
            dpdsTable = new List<Int32>();
            var curDecLen = 0;
            br.BaseStream.Position = song.Offset;
            for (var i = song.FirstChunkID; i <= song.LastChunkID; i++)
            {
                br.BaseStream.Position += 56;
                int packetDecLen = 0;
                for (var j = 0; j < _curChunkPacketCounts[i]; j++)
                {
                    packetDecLen = curDecLen + br.ReadInt32();
                    dpdsTable.Add(packetDecLen);
                }
                curDecLen = packetDecLen;
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
            sfd.Filter = "WAV files (*.wav)|*.wav|xWMA Files (*.xma)|*.xma|All Files (*.*)|*.*";
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
                        if (ext == ".xma")
                        {
                            await TaskEx.Run(() => exportSongToXwma(song, br, fn));
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
            var xmaFn = Path.GetDirectoryName(wavFn) + "\\" + Path.GetFileNameWithoutExtension(wavFn) + ".xma";

            exportSongToXwma(song, br, xmaFn);

            var command = Directory.GetCurrentDirectory() + "\\xWMAEncode.exe";
            var args = "\"" + xmaFn + "\" \"" + wavFn + "\"";
            var px = Process.Start(new ProcessStartInfo(command, args) { WindowStyle = ProcessWindowStyle.Hidden });
            px.WaitForExit();
            File.Delete(xmaFn);
            return wavFn;
        }

        private void exportSongToXwma(Song song, BinaryReader br, string xmaFn)
        {
            var dpdsTable = new List<Int32>();
            var data = extractSongData(song, br, out dpdsTable);
            File.Copy(_curChannels == 2 ? "audio.xma" : "audiom.xma", xmaFn, true);

            long packetCount, dpdsPos, dataPos;
            using (var tempBr = new MemoryStream(File.ReadAllBytes(xmaFn)))
            {
                dpdsPos = findArrayInStream(tempBr, XwmaDpdsHeader, 0) + 8;
                dataPos = findArrayInStream(tempBr, XwmaDataHeader, dpdsPos);
                packetCount = (dataPos - dpdsPos) / 4;
            }

            using (var bw = new BinaryWriter(File.OpenWrite(xmaFn)))
            {
                bw.BaseStream.Position = dpdsPos;
                for (var i = 0; i < packetCount; i++)
                {
                    if (i < dpdsTable.Count)
                    {
                        bw.Write(dpdsTable[i]);
                    }
                    else
                    {
                        bw.Write(dpdsTable.Last());
                    }
                }

                bw.BaseStream.Position = dataPos + 8;
                bw.Write(data);
                bw.Flush();
            }
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