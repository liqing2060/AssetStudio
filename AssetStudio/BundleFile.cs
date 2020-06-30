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

        //private byte[] readUncompressedBlockBytes;
        //private byte[] readCompressedBlockBytes;

        private byte[] uncompressedDataHashBytes;
        private byte[] headerSkipBytes;
        private byte[] writeBlockInfoUncompressedBytes;
        private byte[] writeBlockInfoCompressedBytes;
        private byte[] writeBlockUncompressedBytes;
        private byte[] writeBlockCompressedBytes;

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

        public void Write(EndianBinaryWriter writer, bool useCustomCompressFlags = false, ushort compressFlags = 0)
        {
            writer.WriteStringToNull(m_Header.signature);
            switch (m_Header.signature)
            {
                case "UnityArchive":
                case "UnityWeb":
                case "UnityRaw":
                    break;
                case "UnityFS":
                    PrepareWriteBytes(useCustomCompressFlags, compressFlags);
                    WriteHeader(writer, useCustomCompressFlags, compressFlags);
                    WriteBlocksInfoAndDirectory(writer);
                    WriteBlocks(writer);
                    break;
            }
        }

        private void PrepareWriteBytes(bool useCustomCompressFlags = false, ushort compressFlags = 0)
        {
            writeBlockCompressedBytes = null;

            var scaleFactor = 1.1;
            var uncompressedBlocksStream = CreateBlocksStream("", scaleFactor);
            for (int i = 0; i < m_DirectoryInfo.Length; i++)
            {
                var node = m_DirectoryInfo[i];
                var file = fileList[i];
                node.offset = uncompressedBlocksStream.Position;
                var bytes = new byte[file.stream.Length];
                file.stream.Position = 0;
                file.stream.Read(bytes, 0, bytes.Length);
                uncompressedBlocksStream.Write(bytes, 0, bytes.Length);
            }
            //writeBlockUncompressedBytes = new byte[uncompressedBlocksStream.Position];
            //uncompressedBlocksStream.Position = 0;
            //uncompressedBlocksStream.Read(writeBlockUncompressedBytes, 0, writeBlockUncompressedBytes.Length);
            uncompressedBlocksStream.Position = 0;

            // TODO 重新计算块大小
            var compressedBlockStream = new MemoryStream((int)(m_BlocksInfo.Sum(x => x.compressedSize) * scaleFactor));
            foreach (var blockInfo in m_BlocksInfo)
            {
                byte[] uncompressedBlockBytes = new byte[blockInfo.uncompressedSize];
                uncompressedBlocksStream.Read(uncompressedBlockBytes, 0, uncompressedBlockBytes.Length);
                byte[] compressedBlockBytes;
                var blockInfoFlags = useCustomCompressFlags ? compressFlags : blockInfo.flags;
                switch (blockInfoFlags & 0x3F) //kStorageBlockCompressionTypeMask
                {
                    default: //None
                        {
                            compressedBlockBytes = uncompressedBlockBytes;
                            break;
                        }
                    case 1: //LZMA
                        {
                            //SevenZipHelper.StreamDecompress(reader.BaseStream, blocksStream, blockInfo.compressedSize, blockInfo.uncompressedSize);
                            // TODO
                            compressedBlockBytes = uncompressedBlockBytes;
                            break;
                        }
                    case 2: //LZ4
                    case 3: //LZ4HC
                        {
                            compressedBlockBytes = LZ4.LZ4Codec.EncodeHC(uncompressedBlockBytes, 0, uncompressedBlockBytes.Length);
                            break;
                        }
                }
                compressedBlockStream.Write(compressedBlockBytes, 0, compressedBlockBytes.Length);
            }
            writeBlockCompressedBytes = new byte[compressedBlockStream.Position];
            compressedBlockStream.Position = 0;
            compressedBlockStream.Read(writeBlockCompressedBytes, 0, writeBlockCompressedBytes.Length);

            //int diffIndex = -1;
            //if (readUncompressedBlockBytes.Length != writeBlockUncompressedBytes.Length) diffIndex = 0;
            //if (diffIndex == -1)
            //{
            //    for (int i = 0; i < readUncompressedBlockBytes.Length; i++)
            //    {
            //        if (readUncompressedBlockBytes[i] != writeBlockUncompressedBytes[i])
            //        {
            //            diffIndex = i;
            //            break;
            //        }
            //    }
            //}
            //Console.WriteLine($"Diff index of uncompressed bytes:{diffIndex}");

            //diffIndex = -1;
            //if (readCompressedBlockBytes.Length != writeBlockCompressedBytes.Length) diffIndex = 0;
            //if (diffIndex == -1)
            //{
            //    for (int i = 0; i < readCompressedBlockBytes.Length; i++)
            //    {
            //        if (readCompressedBlockBytes[i] != writeBlockCompressedBytes[i])
            //        {
            //            diffIndex = i;
            //            break;
            //        }
            //    }
            //}
            //Console.WriteLine($"Diff index of compressed bytes:{diffIndex}");


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
                blockInfoWriter.Write((ushort)(useCustomCompressFlags ? ((blockInfo.flags & 0xfc) | compressFlags) : blockInfo.flags));
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
            var headerFlags = useCustomCompressFlags ? compressFlags : m_Header.flags;
            switch (headerFlags & 0x3F) //kArchiveCompressionTypeMask
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

        private Stream CreateBlocksStream(string path, double scaleFactor = 1.0)
        {
            Stream blocksStream;
            var uncompressedSizeSum = m_BlocksInfo.Sum(x => x.uncompressedSize) * scaleFactor;
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

        private void WriteHeader(EndianBinaryWriter writer, bool useCustomCompressFlags = false, ushort compressFlags = 0)
        {
            writer.Write(m_Header.version);
            writer.WriteStringToNull(m_Header.unityVersion);
            writer.WriteStringToNull(m_Header.unityRevision);
            writer.Write(m_Header.size);
            writer.Write((uint)writeBlockInfoCompressedBytes.Length);
            writer.Write((uint)writeBlockInfoUncompressedBytes.Length);
            writer.Write(useCustomCompressFlags ? ((m_Header.flags & 0xfffc) | compressFlags) : m_Header.flags);
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
            //readCompressedBlockBytes = new byte[reader.BaseStream.Length - reader.BaseStream.Position];
            //var compressedBlockStream = new MemoryStream(readCompressedBlockBytes);
            //var position = reader.BaseStream.Position;
            //reader.BaseStream.CopyTo(compressedBlockStream, readCompressedBlockBytes.Length);
            //reader.BaseStream.Position = position;

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

            //readUncompressedBlockBytes = new byte[blocksStream.Position];
            //blocksStream.Position = 0;
            //blocksStream.Read(readUncompressedBlockBytes, 0, readUncompressedBlockBytes.Length);

            blocksStream.Position = 0;
        }

        private void WriteBlocks(EndianBinaryWriter writer)
        {
            writer.Write(writeBlockCompressedBytes, 0, writeBlockCompressedBytes.Length);
        }

        public void WriteFile(string bundlePath)
        {
            using (var fileStream = new FileStream(bundlePath, FileMode.Create))
            {
                using (var writer = new EndianBinaryWriter(fileStream, EndianType.BigEndian))
                {
                    Write(writer);
                }
            }
        }

        public void WriteFileUncompress(string bundlePath)
        {
            using (var fileStream = new FileStream(bundlePath, FileMode.Create))
            {
                using (var writer = new EndianBinaryWriter(fileStream, EndianType.BigEndian))
                {
                    Write(writer, true, 0);
                }
            }
        }

        public void Recover(BundleFile target)
        {
            assetFiles = target.assetFiles;
            m_DirectoryInfo = target.m_DirectoryInfo;

            // TEST CODE
            for (int i = 0; i < assetFiles.Count; i++)
            {
                var assetFile = assetFiles[i];
                var externals = assetFile.m_Externals;
                for (int j = 0; j < externals.Count; j++)
                {
                    var external = externals[j];
                    external.pathName = external.pathName.Replace("cab-2355ff173a24de1298fad22c6a5a2679", "cab-8f3a5e097b6c0b34cef62da3f48a16f9");
                    external.fileName = Path.GetFileName(external.pathName);

                    var bytes = System.Text.Encoding.UTF8.GetBytes(external.pathName);
                    assetFile.reader.Position = assetFile.externalsPathStartPositions[j];
                    assetFile.reader.BaseStream.Write(bytes, 0, bytes.Length);
                    assetFile.reader.BaseStream.Write(new byte[] { 0 }, 0, 1);
                }
            }

            for (int i = 0; i < m_DirectoryInfo.Length; i++)
            {
                m_DirectoryInfo[i].path = m_DirectoryInfo[i].path.Replace("CAB-18a2b7ce64e494182bc9efa718ec9edc", "CAB-f2d448cee29506788da9402db6443504");
            }

            for (int i = 0; i < assetFiles.Count; i++)
            {
                var assetFile = assetFiles[i];
                var targetAssetFile = target.assetFiles[i];
                for (int j = 0; j < assetFile.Objects.Count; j++)
                {
                    var obj = assetFile.Objects[j];
                    if (obj is AssetBundle)
                    {
                        var assetBundle = (AssetBundle)obj;
                        var targetAssetBundle = (AssetBundle)targetAssetFile.Objects[j];
                        assetBundle.Recover(targetAssetBundle);
                    }
                }
            }
        }
    }
}
