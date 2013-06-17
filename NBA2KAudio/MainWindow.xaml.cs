using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace NBA2KAudio
{
    using System.Collections;
    using System.Collections.ObjectModel;
    using System.IO;

    using Microsoft.Win32;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private const int ChunkSize = 14946;

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

            var ms = userSongData;
            var bw = new BinaryWriter(File.Open(txtJukeboxFile.Text, FileMode.Open, FileAccess.Write, FileShare.ReadWrite));

            var songToReplace = (Song) dgSongs.SelectedItem;
            bw.BaseStream.Position = Constants.ChunkOffsets[songToReplace.FirstChunkID];

            var bytesWritten = 0;
            for (int i = songToReplace.FirstChunkID; i <= songToReplace.LastChunkID; i++)
            {
                var buf = new byte[ChunkSize];
                for (int j = 0; j < ChunkSize; j++)
                {
                    buf[j] = 0;
                }

                bw.BaseStream.Position += 58L;
                for (int j = 0; j < 10; j++)
                {
                    bw.Write((byte) (j + 1));
                    bw.BaseStream.Position += 3;
                }
                bw.BaseStream.Position -= 2;
                var chunkLength = (int) getChunkLength(i);

                if (bytesWritten == userSongData.Length)
                {
                    bw.Write(buf, 0, chunkLength);
                    bw.Flush();
                    continue;
                }

                if (bytesWritten + chunkLength > userSongData.Length)
                {
                    var newChunkLength = (int) (userSongData.Length - bytesWritten);
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

        private static List<Song> songs = new List<Song>();

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
            ofd.Filter = "xWMA Stripped DAT File (*.dat)|*.dat";

            if (ofd.ShowDialog() == false)
            {
                return;
            }

            userSongData = new MemoryStream(File.ReadAllBytes(ofd.FileName));
            var userSongDuration = (double) userSongData.Length / 4000;
            var text = String.Format("Length: {0} bytes\nDuration: {1:F2}", userSongData.Length, userSongDuration);
            txbSongInfo.Text = text;
            txtSongFile.Text = ofd.FileName;

            _matchingSongs = new ObservableCollection<Song>(_allSongs.Where(song => song.Length >= userSongData.Length).ToList());
            dgSongs.ItemsSource = _matchingSongs;
        }

        private MemoryStream userSongData = new MemoryStream();
        private List<Song> _allSongs { get; set; }
        private ObservableCollection<Song> _matchingSongs { get; set; }

        private void btnSelectJukebox_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog();
            ofd.Filter = "NBA 2K Jukebox Music File (jukeboxmusic.bin)|jukeboxmusic.bin";

            if (ofd.ShowDialog() == false)
            {
                return;
            }

            txtJukeboxFile.Text = ofd.FileName;
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