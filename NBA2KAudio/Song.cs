﻿#region Copyright Notice

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
    public class Song
    {
        public int ID { get; set; }
        public long Offset { get; set; }
        public int ChunkCount { get; set; }
        public long Length { get; set; }
        public int FirstChunkID { get; set; }
        public int LastChunkID { get; set; }
        public double Duration { get; set; }
    }
}