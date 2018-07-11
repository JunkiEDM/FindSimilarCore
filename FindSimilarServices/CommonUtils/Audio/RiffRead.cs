﻿using System;
using System.IO;
using System.Collections.Generic;

namespace CommonUtils.Audio
{
    /// <summary>
    /// Originally based by JavaScience Consulting's RiffRead32 Java class
    /// Converted to C# by Per Ivar Nerseth 2011
    /// </summary>

    public class RiffRead
    {
        private string selectedFile;
        private long fileLength;
        private int numChannels;
        private int nSamplesPerSec;
        private int nAvgBytesPerSec;
        private int nBlockAlign;
        private int wBitsPerSample;
        private int riffDataSize = 0;  // size of RIFF data chunk.
        private long dataSize = 0;
        private int sampleCount;
        private int wFormatTag;
        private float[][] soundData;
        private float lengthInSeconds = 0;
        private Dictionary<string, string> infoChunks = new Dictionary<string, string>();

        public string SelectedFile { get { return selectedFile; } set { selectedFile = value; } }
        public long FileLength { get { return fileLength; } set { fileLength = value; } }
        public int Channels { get { return numChannels; } set { numChannels = value; } }
        public int SampleRate { get { return nSamplesPerSec; } set { nSamplesPerSec = value; } }
        public int AvgBytesPerSec { get { return nAvgBytesPerSec; } set { nAvgBytesPerSec = value; } }
        public int BlockAlign { get { return nBlockAlign; } set { nBlockAlign = value; } }
        public int BitsPerSample { get { return wBitsPerSample; } set { wBitsPerSample = value; } }
        public int RiffDataSize { get { return riffDataSize; } set { riffDataSize = value; } }
        public long DataSize { get { return dataSize; } set { dataSize = value; } }
        public int SampleCount { get { return sampleCount; } set { sampleCount = value; } }
        public int Format { get { return wFormatTag; } set { wFormatTag = value; } }
        public float[][] SoundData { get { return soundData; } set { soundData = value; } }

        public float LengthInSeconds { get { return lengthInSeconds; } set { lengthInSeconds = value; } }
        public Dictionary<string, string> InfoChunks { get { return infoChunks; } set { infoChunks = value; } }

        public RiffRead(string value)
        {
            selectedFile = value;
        }

        public bool Process()
        {
            var listInfo = new Dictionary<string, string>();
            for (int i = 0; i < SoundIO.INFO_TYPE.Length; i++)
            {
                listInfo.Add(SoundIO.INFO_TYPE[i], SoundIO.INFO_DESC[i]);
            }

            var bf = new BinaryFile(selectedFile);
            try
            {
                var fileInfo = new FileInfo(selectedFile);
                fileLength = fileInfo.Length;

                int chunkSize = 0, infoChunkSize = 0, byteCount = 0, listByteCount = 0;
                string field = "", infoField = "", infoDescription = "", infoData = "";
                int bytesPerSec = 0;
                int byteRead = 0;
                bool isPCM = false;

                // Get RIFF chunk header 
                field = bf.ReadString(4);
                if (field != "RIFF")
                {
                    Console.Error.WriteLine(" ****  Not a valid RIFF file  ****");
                    return false;
                }

                // read RIFF data size
                chunkSize = bf.ReadInt32();

                // read form-type (WAVE etc)
                field = bf.ReadString(4);

                riffDataSize = chunkSize;

                byteCount = 4;  // initialize bytecount to include RIFF form-type bytes.
                while (byteCount < riffDataSize)
                {    // check for chunks inside RIFF data area.
                    field = "";
                    int firstbyte = bf.ReadByte();
                    if (firstbyte == 0)
                    {
                        // if previous data had odd bytecount, was padded by null so skip
                        byteCount++;
                        continue;
                    }

                    field += (char)firstbyte;  // if we have a new chunk
                    for (int i = 1; i <= 3; i++)
                    {
                        field += (char)bf.ReadByte();
                    }

                    chunkSize = 0;
                    chunkSize = bf.ReadInt32();
                    byteCount += (8 + chunkSize);

                    if (field == "data")
                    {
                        // get data size to compute duration later.
                        dataSize = chunkSize;
                    }

                    if (field == "fmt ")
                    {
                        /*
                        Offset   Size  Description                  Value
                        0x00     4     Chunk ID                     "fmt " (0x666D7420)
                        0x04     4     Chunk Data Size              16 + extra format bytes
                        0x08     2     Compression code             1 - 65,535
                        0x0a     2     Number of channels           1 - 65,535
                        0x0c     4     Sample rate                  1 - 0xFFFFFFFF
                        0x10     4     Average bytes per second     1 - 0xFFFFFFFF
                        0x14     2     Block align                  1 - 65,535
                        0x16     2     Significant bits per sample  2 - 65,535
                        0x18     2     Extra format bytes           0 - 65,535
                        0x1a
                        Extra format bytes *
						 */

                        // extract info from "format" chunk.
                        if (chunkSize < 16)
                        {
                            Console.Error.WriteLine(" ****  Not a valid fmt chunk  ****");
                            return false;
                        }

                        // Read compression code, 2 bytes
                        wFormatTag = bf.ReadInt16();
                        switch (wFormatTag)
                        {
                            case SoundIO.WAVE_FORMAT_PCM:
                            case SoundIO.WAVE_FORMAT_EXTENSIBLE:
                            case SoundIO.WAVE_FORMAT_IEEE_FLOAT:
                                isPCM = true;
                                break;
                        }
                        // Read number of channels, 2 bytes
                        numChannels = bf.ReadInt16();

                        // Read sample rate, 4 bytes
                        nSamplesPerSec = bf.ReadInt32();

                        // Read average bytes per second, 4 bytes
                        nAvgBytesPerSec = bf.ReadInt32();
                        bytesPerSec = nAvgBytesPerSec;

                        // Read block align, 2 bytes
                        nBlockAlign = bf.ReadInt16();

                        // Read significant bits per sample, 2 bytes
                        if (isPCM)
                        {
                            // if PCM or EXTENSIBLE format
                            wBitsPerSample = bf.ReadInt16();
                        }
                        else
                        {
                            bf.ReadBytes(2);
                            wBitsPerSample = 0;
                        }

                        // skip over any extra bytes in format specific field.
                        bf.ReadBytes(chunkSize - 16);

                    }
                    else if (field == "LIST")
                    {
                        String listtype = bf.ReadString(4);

                        // skip over LIST chunks which don't contain INFO subchunks
                        if (listtype != "INFO")
                        {
                            bf.ReadBytes(chunkSize - 4);
                            continue;
                        }

                        try
                        {
                            listByteCount = 4;

                            // iterate over all entries in LIST chunk
                            while (listByteCount < chunkSize)
                            {
                                infoField = "";
                                infoDescription = "";
                                infoData = "";

                                firstbyte = bf.ReadByte();
                                // if previous data had odd bytecount, was padded by null so skip
                                if (firstbyte == 0)
                                {
                                    listByteCount++;
                                    continue;
                                }

                                // if firstbyte is not an alpha char, read one more byte
                                if (!Char.IsLetterOrDigit((char)firstbyte))
                                {
                                    firstbyte = bf.ReadByte();
                                    listByteCount++;
                                }

                                // if we have a new chunk
                                infoField += (char)firstbyte;
                                for (int i = 1; i <= 3; i++)
                                {
                                    // get the remaining part of info chunk name ID
                                    infoField += (char)bf.ReadByte();
                                }

                                // get the info chunk data byte size
                                infoChunkSize = bf.ReadInt32();
                                listByteCount += (8 + infoChunkSize);

                                if (listByteCount > chunkSize)
                                {
                                    bf.SetPosition(bf.GetPosition() - 8);
                                    break;
                                }

                                // get the info chunk data
                                for (int i = 0; i < infoChunkSize; i++)
                                {
                                    byteRead = bf.ReadByte();
                                    if (byteRead == 0)
                                    {
                                        // if null byte in string, ignore it
                                        continue;
                                    }
                                    infoData += (char)byteRead;
                                }

                                int unknownCount = 1;
                                try
                                {
                                    infoDescription = (string)listInfo[infoField];
                                }
                                catch (KeyNotFoundException) { }

                                if (infoDescription != null)
                                {
                                    InfoChunks.Add(infoDescription, infoData);
                                }
                                else
                                {
                                    InfoChunks.Add(String.Format("unknown{0}", unknownCount), infoData);
                                    unknownCount++;
                                }
                            } // end iteration over LIST chunk
                        }
                        catch (Exception)
                        {

                            // don't care about these?
                        }
                    }
                    else
                    {
                        if (field.Equals("data"))
                        {
                            sampleCount = (int)dataSize / (wBitsPerSample / 8) / numChannels;

                            soundData = new float[numChannels][];
                            for (int ic = 0; ic < numChannels; ic++)
                            {
                                soundData[ic] = new float[sampleCount];
                            }

                            // Data loading
                            if (BitsPerSample == 8)
                            {
                                SoundIO.Read8Bit(bf, soundData, sampleCount, numChannels);
                            }
                            if (BitsPerSample == 16)
                            {
                                SoundIO.Read16Bit(bf, soundData, sampleCount, numChannels);
                            }
                            if (BitsPerSample == 32)
                            {
                                if (wFormatTag == SoundIO.WAVE_FORMAT_PCM)
                                {
                                    SoundIO.Read32Bit(bf, soundData, sampleCount, numChannels);
                                }
                                else if (wFormatTag == SoundIO.WAVE_FORMAT_IEEE_FLOAT)
                                {
                                    SoundIO.Read32BitFloat(bf, soundData, sampleCount, numChannels);
                                }
                            }
                        }
                        else
                        {
                            // if NOT the fmt or LIST chunks skip data
                            bf.ReadBytes(chunkSize);
                        }
                    }
                }  // end while.

                // End of chunk iteration
                if (isPCM && dataSize > 0)
                {   // compute duration of PCM wave file
                    lengthInSeconds = ((float)dataSize / (float)bytesPerSec);
                    long waveduration = 1000L * dataSize / bytesPerSec; // in msec units
                    long mins = waveduration / 60000;    // integer minutes
                    double secs = 0.001 * (waveduration % 60000);    // double secs.
                }

                if ((8 + byteCount) != (int)fileLength)
                {
                    throw new FormatException("Problem with file structure!");
                }

                return true;
            }
            catch (Exception)
            {
                // ignore
                // throw new FormatException(e.Message);
            }
            finally
            {
                // close all streams.
                bf.Close();
            }
            return true;
        }
    }
}