using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Lz4;

namespace AssetStudio
{
    public class BundleFile
    {
        public class Header
        {
            public string signature;
            public uint version;
            public string unityVersion;
            public string unityRevision;
            public long size;
            public uint compressedBlocksInfoSize;
            public uint uncompressedBlocksInfoSize;
            public uint flags;
        }

        public class StorageBlock
        {
            public uint compressedSize;
            public uint uncompressedSize;
            public ushort flags;
        }

        public class Node
        {
            public long offset;
            public long size;
            public uint flags;
            public string path;
        }

        public Header m_Header;
        private StorageBlock[] m_BlocksInfo;
        private Node[] m_DirectoryInfo;

        public StreamFile[] fileList;
        public List<SerializedFile> assetFiles = new List<SerializedFile>();
        private byte[] uncompressedDataHashBytes;
        private byte[] headerSkipBytes;
        private byte[] writeBlockInfoUncompressedBytes;
        private byte[] writeBlockInfoCompressedBytes;

        public BundleFile(EndianBinaryReader reader, string path)
        {
            m_Header = new Header();
            m_Header.signature = reader.ReadStringToNull();
            switch (m_Header.signature)
            {
                case "UnityArchive":
                    break; //TODO
                case "UnityWeb":
                case "UnityRaw":
                    ReadHeaderAndBlocksInfo(reader);
                    using (var blocksStream = CreateBlocksStream(path))
                    {
                        ReadBlocksAndDirectory(reader, blocksStream);
                        ReadFiles(blocksStream, path);
                    }
                    break;
                case "UnityFS":
                    ReadHeader(reader);
                    ReadBlocksInfoAndDirectory(reader);
                    using (var blocksStream = CreateBlocksStream(path))
                    {
                        ReadBlocks(reader, blocksStream);
                        ReadFiles(blocksStream, path);
                    }
                    break;
            }
        }

        public void Write(EndianBinaryWriter writer)
        {
            writer.WriteStringToNull(m_Header.signature);
            switch (m_Header.signature)
            {
                case "UnityArchive":
                case "UnityWeb":
                case "UnityRaw":
                    break; //TODO
                case "UnityFS":
                    PrepareWriteBytes();
                    WriteHeader(writer);
                    WriteBlocksInfoAndDirectory(writer);
                    break;
            }
        }

        private void PrepareWriteBytes()
        {
            writeBlockInfoUncompressedBytes = null;
            writeBlockInfoCompressedBytes = null;

            var uncompressedBytes = new byte[(int)Math.Floor(m_Header.uncompressedBlocksInfoSize * 1.2f)]; // TODO calc length
            var uncompressedStream = new MemoryStream(uncompressedBytes);
            var blockInfoWriter = new EndianBinaryWriter(uncompressedStream, EndianType.BigEndian);
            blockInfoWriter.Write(new byte[16]);

            blockInfoWriter.Write(m_BlocksInfo.Length);
            for (int i = 0; i < m_BlocksInfo.Length; i++)
            {
                var blockInfo = m_BlocksInfo[i];
                blockInfoWriter.Write(blockInfo.uncompressedSize);
                blockInfoWriter.Write(blockInfo.compressedSize);
                blockInfoWriter.Write(blockInfo.flags);
            }

            blockInfoWriter.Write(m_DirectoryInfo.Length);
            for (int i = 0; i < m_DirectoryInfo.Length; i++)
            {
                var directoryInfo = m_DirectoryInfo[i];
                blockInfoWriter.Write(directoryInfo.offset);
                blockInfoWriter.Write(directoryInfo.size);
                blockInfoWriter.Write(directoryInfo.flags);
                blockInfoWriter.WriteStringToNull(directoryInfo.path);
            }

            var isCalcHash = false;
            for (int i = 0; i < uncompressedDataHashBytes.Length; i++)
            {
                if (uncompressedDataHashBytes[i] != 0)
                {
                    isCalcHash = true;
                    break;
                }
            }
            if (isCalcHash)
            {
                var position = blockInfoWriter.Position;
                blockInfoWriter.Position = 0;
                using (SHA256 sha256Hash = SHA256.Create())
                {
                    uncompressedDataHashBytes = sha256Hash.ComputeHash(uncompressedBytes, 0, (int)position);
                }
                blockInfoWriter.Write(uncompressedDataHashBytes);
                blockInfoWriter.Position = position;
            }

            MemoryStream compressedStream;
            switch (m_Header.flags & 0x3F) //kArchiveCompressionTypeMask
            {
                default: //None
                    {
                        compressedStream = uncompressedStream;
                        break;
                    }
                case 1: //LZMA
                    {
                        compressedStream = uncompressedStream;
                        //blocksInfoUncompresseddStream = SevenZipHelper.StreamDecompress(blocksInfoCompressedStream);
                        //blocksInfoCompressedStream.Close();
                        // TODO
                        break;
                    }
                case 2: //LZ4
                case 3: //LZ4HC
                    {
                        compressedStream = null;
                        writeBlockInfoCompressedBytes = LZ4.LZ4Codec.EncodeHC(uncompressedBytes, 0, (int)blockInfoWriter.Position);
                        break;
                    }
            }

            writeBlockInfoUncompressedBytes = new byte[blockInfoWriter.Position];
            uncompressedStream.Position = 0;
            uncompressedStream.Read(writeBlockInfoUncompressedBytes, 0, writeBlockInfoUncompressedBytes.Length);

            if (writeBlockInfoCompressedBytes == null)
            {
                writeBlockInfoCompressedBytes = writeBlockInfoUncompressedBytes;
            }

            if (compressedStream != null) compressedStream.Close();
            if (uncompressedStream != null && uncompressedStream != compressedStream) uncompressedStream.Close();

            //m_Header.uncompressedBlocksInfoSize = (uint)writeBlockInfoUncompressedBytes.Length;
            //m_Header.compressedBlocksInfoSize = (uint)writeBlockInfoCompressedBytes.Length;
        }

        private void ReadHeaderAndBlocksInfo(EndianBinaryReader reader)
        {
            var isCompressed = m_Header.signature == "UnityWeb";
            m_Header.version = reader.ReadUInt32();
            m_Header.unityVersion = reader.ReadStringToNull();
            m_Header.unityRevision = reader.ReadStringToNull();
            if (m_Header.version >= 4)
            {
                var hash = reader.ReadBytes(16);
                var crc = reader.ReadUInt32();
            }
            var minimumStreamedBytes = reader.ReadUInt32();
            var headerSize = reader.ReadUInt32();
            var numberOfLevelsToDownloadBeforeStreaming = reader.ReadUInt32();
            var levelCount = reader.ReadInt32();
            m_BlocksInfo = new StorageBlock[1];
            for (int i = 0; i < levelCount; i++)
            {
                var storageBlock = new StorageBlock()
                {
                    compressedSize = reader.ReadUInt32(),
                    uncompressedSize = reader.ReadUInt32(),
                    flags = (ushort)(isCompressed ? 1 : 0)
                };
                if (i == levelCount - 1)
                {
                    m_BlocksInfo[0] = storageBlock;
                }
            }
            if (m_Header.version >= 2)
            {
                var completeFileSize = reader.ReadUInt32();
            }
            if (m_Header.version >= 3)
            {
                var fileInfoHeaderSize = reader.ReadUInt32();
            }
            reader.Position = headerSize;
        }

        private Stream CreateBlocksStream(string path)
        {
            Stream blocksStream;
            var uncompressedSizeSum = m_BlocksInfo.Sum(x => x.uncompressedSize);
            if (uncompressedSizeSum >= int.MaxValue)
            {
                /*var memoryMappedFile = MemoryMappedFile.CreateNew(Path.GetFileName(path), uncompressedSizeSum);
                assetsDataStream = memoryMappedFile.CreateViewStream();*/
                blocksStream = new FileStream(path + ".temp", FileMode.Create, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
            }
            else
            {
                blocksStream = new MemoryStream((int)uncompressedSizeSum);
            }
            return blocksStream;
        }

        private void ReadBlocksAndDirectory(EndianBinaryReader reader, Stream blocksStream)
        {
            foreach (var blockInfo in m_BlocksInfo)
            {
                var uncompressedBytes = reader.ReadBytes((int)blockInfo.compressedSize);
                if (blockInfo.flags == 1)
                {
                    using (var memoryStream = new MemoryStream(uncompressedBytes))
                    {
                        using (var decompressStream = SevenZipHelper.StreamDecompress(memoryStream))
                        {
                            uncompressedBytes = decompressStream.ToArray();
                        }
                    }
                }
                blocksStream.Write(uncompressedBytes, 0, uncompressedBytes.Length);
            }
            blocksStream.Position = 0;
            var blocksReader = new EndianBinaryReader(blocksStream);
            var nodesCount = blocksReader.ReadInt32();
            m_DirectoryInfo = new Node[nodesCount];
            for (int i = 0; i < nodesCount; i++)
            {
                m_DirectoryInfo[i] = new Node
                {
                    path = blocksReader.ReadStringToNull(),
                    offset = blocksReader.ReadUInt32(),
                    size = blocksReader.ReadUInt32()
                };
            }
        }

        public void ReadFiles(Stream blocksStream, string path)
        {
            fileList = new StreamFile[m_DirectoryInfo.Length];
            for (int i = 0; i < m_DirectoryInfo.Length; i++)
            {
                var node = m_DirectoryInfo[i];
                var file = new StreamFile();
                fileList[i] = file;
                file.fileName = Path.GetFileName(node.path);
                if (node.size >= int.MaxValue)
                {
                    /*var memoryMappedFile = MemoryMappedFile.CreateNew(file.fileName, entryinfo_size);
                    file.stream = memoryMappedFile.CreateViewStream();*/
                    var extractPath = path + "_unpacked" + Path.DirectorySeparatorChar;
                    Directory.CreateDirectory(extractPath);
                    file.stream = File.Create(extractPath + file.fileName);
                }
                else
                {
                    file.stream = new MemoryStream((int)node.size);
                }
                blocksStream.Position = node.offset;
                blocksStream.CopyTo(file.stream, node.size);
                file.stream.Position = 0;
            }
        }

        private void ReadHeader(EndianBinaryReader reader)
        {
            m_Header.version = reader.ReadUInt32();
            m_Header.unityVersion = reader.ReadStringToNull();
            m_Header.unityRevision = reader.ReadStringToNull();
            m_Header.size = reader.ReadInt64();
            m_Header.compressedBlocksInfoSize = reader.ReadUInt32();
            m_Header.uncompressedBlocksInfoSize = reader.ReadUInt32();
            m_Header.flags = reader.ReadUInt32();
        }

        private void WriteHeader(EndianBinaryWriter writer)
        {
            writer.Write(m_Header.version);
            writer.WriteStringToNull(m_Header.unityVersion);
            writer.WriteStringToNull(m_Header.unityRevision);
            writer.Write(m_Header.size);
            writer.Write((uint)writeBlockInfoCompressedBytes.Length);
            writer.Write((uint)writeBlockInfoUncompressedBytes.Length);
            writer.Write(m_Header.flags);
        }

        private void ReadBlocksInfoAndDirectory(EndianBinaryReader reader)
        {
            byte[] blocksInfoBytes;
            if ((m_Header.flags & 0x80) != 0) //kArchiveBlocksInfoAtTheEnd
            {
                var position = reader.Position;
                reader.Position = reader.BaseStream.Length - m_Header.compressedBlocksInfoSize;
                blocksInfoBytes = reader.ReadBytes((int)m_Header.compressedBlocksInfoSize);
                reader.Position = position;
            }
            else //0x40 kArchiveBlocksAndDirectoryInfoCombined
            {
                if (m_Header.version >= 7)
                {
                    //reader.AlignStream(16);
                    headerSkipBytes = reader.ReadAlignBytes(16);
                }
                blocksInfoBytes = reader.ReadBytes((int)m_Header.compressedBlocksInfoSize);
            }
            var blocksInfoCompressedStream = new MemoryStream(blocksInfoBytes);
            MemoryStream blocksInfoUncompresseddStream;
            switch (m_Header.flags & 0x3F) //kArchiveCompressionTypeMask
            {
                default: //None
                    {
                        blocksInfoUncompresseddStream = blocksInfoCompressedStream;
                        break;
                    }
                case 1: //LZMA
                    {
                        blocksInfoUncompresseddStream = SevenZipHelper.StreamDecompress(blocksInfoCompressedStream);
                        blocksInfoCompressedStream.Close();
                        break;
                    }
                case 2: //LZ4
                case 3: //LZ4HC
                    {
                        var uncompressedBytes = new byte[m_Header.uncompressedBlocksInfoSize];
                        using (var decoder = new Lz4DecoderStream(blocksInfoCompressedStream))
                        {
                            decoder.Read(uncompressedBytes, 0, uncompressedBytes.Length);
                        }
                        blocksInfoUncompresseddStream = new MemoryStream(uncompressedBytes);
                        break;
                    }
            }
            using (var blocksInfoReader = new EndianBinaryReader(blocksInfoUncompresseddStream))
            {
                uncompressedDataHashBytes = blocksInfoReader.ReadBytes(16);
                var blocksInfoCount = blocksInfoReader.ReadInt32();
                m_BlocksInfo = new StorageBlock[blocksInfoCount];
                for (int i = 0; i < blocksInfoCount; i++)
                {
                    m_BlocksInfo[i] = new StorageBlock
                    {
                        uncompressedSize = blocksInfoReader.ReadUInt32(),
                        compressedSize = blocksInfoReader.ReadUInt32(),
                        flags = blocksInfoReader.ReadUInt16()
                    };
                }

                var nodesCount = blocksInfoReader.ReadInt32();
                m_DirectoryInfo = new Node[nodesCount];
                for (int i = 0; i < nodesCount; i++)
                {
                    m_DirectoryInfo[i] = new Node
                    {
                        offset = blocksInfoReader.ReadInt64(),
                        size = blocksInfoReader.ReadInt64(),
                        flags = blocksInfoReader.ReadUInt32(),
                        path = blocksInfoReader.ReadStringToNull(),
                    };
                }
            }
        }

        private void WriteBlocksInfoAndDirectory(EndianBinaryWriter writer)
        {
            if (headerSkipBytes != null && headerSkipBytes.Length > 0) writer.Write(headerSkipBytes, 0, headerSkipBytes.Length);
            writer.Write(writeBlockInfoCompressedBytes);
        }

        private void ReadBlocks(EndianBinaryReader reader, Stream blocksStream)
        {
            foreach (var blockInfo in m_BlocksInfo)
            {
                switch (blockInfo.flags & 0x3F) //kStorageBlockCompressionTypeMask
                {
                    default: //None
                        {
                            reader.BaseStream.CopyTo(blocksStream, blockInfo.compressedSize);
                            break;
                        }
                    case 1: //LZMA
                        {
                            SevenZipHelper.StreamDecompress(reader.BaseStream, blocksStream, blockInfo.compressedSize, blockInfo.uncompressedSize);
                            break;
                        }
                    case 2: //LZ4
                    case 3: //LZ4HC
                        {
                            var compressedStream = new MemoryStream(reader.ReadBytes((int)blockInfo.compressedSize));
                            using (var lz4Stream = new Lz4DecoderStream(compressedStream))
                            {
                                lz4Stream.CopyTo(blocksStream, blockInfo.uncompressedSize);
                            }
                            break;
                        }
                }
            }
            blocksStream.Position = 0;
        }

        public void Write(string path)
        {
            var fileStream = new FileStream(path, FileMode.OpenOrCreate);
        }
    }
}
