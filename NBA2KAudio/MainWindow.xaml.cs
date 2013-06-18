﻿namespace NBA2KAudio
{
    #region Using Directives

    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Controls;

    using LeftosCommonLibrary;

    using Microsoft.Win32;

    #endregion

    /// <summary>Interaction logic for MainWindow.xaml</summary>
    public partial class MainWindow : Window
    {
        private const int ChunkSize = 14946;
        private static readonly byte[] ChunkHeader = new byte[] { 105, 161, 190, 210 };
        private static readonly byte[] SongHeader = new byte[] { 42, 49, 115, 98 };

        private readonly List<long> _curChunkOffsets;
        private readonly List<long> _curSongOffsets;
        private long _curFileLength;
        private long _userSongLength;

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
                    "You need to do all the following before replacing a song:\n\n" + "- Select your own song.\n"
                    + "- Select the jukeboxmusic.bin.\n" + "- Select a song from the grid to replace.");
                return;
            }

            var ms = new MemoryStream(File.ReadAllBytes(txtSongFile.Text));
            var bw = new BinaryWriter(File.Open(txtJukeboxFile.Text, FileMode.Open, FileAccess.Write, FileShare.ReadWrite));

            var songToReplace = (Song) dgSongs.SelectedItem;
            bw.BaseStream.Position = _curChunkOffsets[songToReplace.FirstChunkID];

            var bytesWritten = 0;
            for (var i = songToReplace.FirstChunkID; i <= songToReplace.LastChunkID; i++)
            {
                var buf = new byte[ChunkSize];
                for (var j = 0; j < ChunkSize; j++)
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

            MessageBox.Show("Song replaced!");
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
                    var p = Process.Start(new ProcessStartInfo(command, args));
                    p.WaitForExit();

                    WavToxWMADat(tempWavePath, datFilePath);
                    File.Delete(tempWavePath);
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
                    WavToxWMADat(fn, datFilePath);
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
            ofd.Filter =
                "NBA 2K Jukebox Music Files (jukeboxmusic.bin)|jukeboxmusic.bin|NBA 2K Audio Files (*.bin)|*.bin|All Files (*.*)|*.*";
            ofd.InitialDirectory = Tools.GetRegistrySetting("LastJukeboxPath", "");

            if (ofd.ShowDialog() == false)
            {
                return;
            }

            var fn = ofd.FileName;

            txtJukeboxFile.Text = fn;
            Tools.SetRegistrySetting("LastJukeboxPath", Path.GetDirectoryName(fn));

            IsEnabled = false;
            await Task.Run(() => parseBinFile(fn));
            await Task.Run(() => parseSongs());

            refreshMatchingSongs();
            dgSongs.ItemsSource = _matchingSongs;
            IsEnabled = true;
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

            using (var ms = new MemoryStream(File.ReadAllBytes(fn)))
            {
                var chunkID = 0;
                var curPerc = 0;
                var oldPerc = 0;
                _curFileLength = ms.Length;
                var headBuf = new byte[4];
                var idBuf = new byte[2];
                while (ms.Length - ms.Position > 106)
                {
                    curPerc = (int) (ms.Position * 100.0 / ms.Length);
                    if (curPerc != oldPerc)
                    {
                        oldPerc = curPerc;
                        var perc = curPerc;
                        Tools.AppInvoke(() => stiStatus.Content = String.Format("Please wait ({0}% completed)...", perc));
                    }
                    ms.Read(headBuf, 0, 4);
                    if (!headBuf.SequenceEqual(ChunkHeader))
                    {
                        ms.Position -= 3;
                        continue;
                    }
                    _curChunkOffsets.Add(ms.Position - 4);

                    ms.Position += 32;
                    ms.Read(idBuf, 0, 2);
                    chunkID = idBuf[1] * 256 + idBuf[0];
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
            }

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
                    "Any song that you select gets converted to an NBA 2K-compatible DAT file in " + App.AppDocsPath + " "
                    + "in order to allow you to reuse it without the need to wait for it to be converted again.\n\n"
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
                    Console.WriteLine("Exception " + ex.Message + " thrown while trying to delete a file from the song cache.");
                }
            }
        }
    }
}