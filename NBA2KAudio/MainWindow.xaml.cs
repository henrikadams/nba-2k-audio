namespace NBA2KAudio
{
    #region Using Directives

    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Windows;
    using System.Windows.Controls;

    using LeftosCommonLibrary;

    using Microsoft.Win32;

    #endregion

    /// <summary>Interaction logic for MainWindow.xaml</summary>
    public partial class MainWindow : Window
    {
        private const int ChunkSize = 14946;

        private static readonly List<Song> songs = new List<Song>();

        public MainWindow()
        {
            InitializeComponent();

            if (!Directory.Exists(App.AppDocsPath))
            {
                Directory.CreateDirectory(App.AppDocsPath);
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
                    "You need to do all the following before replacing a song:\n\n" + "- Select your own song.\n"
                    + "- Select the jukeboxmusic.bin.\n" + "- Select a song from the grid to replace.");
                return;
            }

            var ms = new MemoryStream(File.ReadAllBytes(txtSongFile.Text));
            var bw = new BinaryWriter(File.Open(txtJukeboxFile.Text, FileMode.Open, FileAccess.Write, FileShare.ReadWrite));

            var songToReplace = (Song) dgSongs.SelectedItem;
            bw.BaseStream.Position = Constants.ChunkOffsets[songToReplace.FirstChunkID];

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
            for (var i = 0; i < Constants.SongOffsets.Count; i++)
            {
                var song = new Song();
                song.FirstChunkID = Constants.ChunkOffsets.IndexOf(Constants.SongOffsets[i]);
                song.Offset = Constants.SongOffsets[i];
                if (i < Constants.SongOffsets.Count - 1)
                {
                    song.LastChunkID = Constants.ChunkOffsets.IndexOf(Constants.SongOffsets[i + 1]) - 1;
                    song.ChunkCount = song.LastChunkID - song.FirstChunkID + 1;
                    song.Length = Constants.ChunkOffsets[song.LastChunkID + 1] - Constants.ChunkOffsets[song.FirstChunkID]
                                  - (96 * song.ChunkCount);
                }
                else
                {
                    song.LastChunkID = Constants.ChunkOffsets.Count - 1;
                    song.ChunkCount = song.LastChunkID - song.FirstChunkID + 1;
                    song.Length = Constants.JukeboxLength - Constants.ChunkOffsets[song.FirstChunkID] - (96 * song.ChunkCount);
                }
                song.ID = i;
                song.Duration = (double) song.Length / 4000;
                songs.Add(song);
            }
            _allSongs = new List<Song>(songs);
            _matchingSongs = new ObservableCollection<Song>(_allSongs);
            dgSongs.ItemsSource = _matchingSongs;
        }

        private long getChunkLength(int id)
        {
            if (id != Constants.ChunkOffsets.Count - 1)
            {
                return Constants.ChunkOffsets[id + 1] - Constants.ChunkOffsets[id] - 96;
            }
            else
            {
                return Constants.JukeboxLength - Constants.ChunkOffsets[id] - 96;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            parseSongs();
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
            var userSongDuration = (double) userSongData.Length / 4000;
            var text = String.Format("Length: {0} bytes\nDuration: {1:F2}", userSongData.Length, userSongDuration);
            txbSongInfo.Text = text;

            _matchingSongs = new ObservableCollection<Song>(_allSongs.Where(song => song.Length >= userSongData.Length).ToList());
            dgSongs.ItemsSource = _matchingSongs;

            userSongData.Close();
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

        private void btnSelectJukebox_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog();
            ofd.Filter = "NBA 2K Jukebox Music Files (jukeboxmusic.bin)|jukeboxmusic.bin|All Files (*.*)|*.*";
            ofd.InitialDirectory = Tools.GetRegistrySetting("LastJukeboxPath", "");

            if (ofd.ShowDialog() == false)
            {
                return;
            }

            txtJukeboxFile.Text = ofd.FileName;
            Tools.SetRegistrySetting("LastJukeboxPath", Path.GetDirectoryName(ofd.FileName));
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
    }
}