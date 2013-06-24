#region Copyright Notice

//     Copyright 2011-2013 Eleftherios Aslanoglou
//  
//     Licensed under the Apache License, Version 2.0 (the "License");
//     you may not use this file except in compliance with the License.
//     You may obtain a copy of the License at
//  
//         http:www.apache.org/licenses/LICENSE-2.0
//  
//     Unless required by applicable law or agreed to in writing, software
//     distributed under the License is distributed on an "AS IS" BASIS,
//     WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//     See the License for the specific language governing permissions and
//     limitations under the License.

#endregion

namespace NBA2KAudio
{
    #region Using Directives

    using System;
    using System.ComponentModel;
    using System.Runtime.CompilerServices;

    using NBA2KAudio.Annotations;

    #endregion

    public class Song : INotifyPropertyChanged
    {
        private string _description;
        public int ID { get; set; }
        public long Offset { get; set; }
        public int ChunkCount { get; set; }
        public int PacketCount { get; set; }
        public long Length { get; set; }
        public int FirstChunkID { get; set; }
        public int LastChunkID { get; set; }
        public double Duration { get; set; }

        public string DurationS
        {
            get {
                return FormatDurationString(Duration);
            }
        }

        public static string FormatDurationString(double duration)
        {
            var mins = Convert.ToInt32(Math.Floor(duration / 60));
            string minsS = "";
            if (mins > 0)
            {
                minsS = mins + ":";
            }

            var secs = Convert.ToInt32(Math.Floor(duration % 60));
            string secsS = secs.ToString().PadLeft(mins > 0 ? 2 : 1, '0');

            var decPart = Convert.ToInt32((duration - Math.Floor(duration)) * 100);
            var decS = decPart.ToString().PadLeft(2, '0');

            return String.Format("{0}{1}.{2}", minsS, secsS, decS);
        }

        public string Description
        {
            get { return _description; }
            set
            {
                _description = value;
                OnPropertyChanged("Description");
            }
        }

        #region INotifyPropertyChanged Members

        public event PropertyChangedEventHandler PropertyChanged;

        #endregion

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}