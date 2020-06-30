using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AssetStudio
{
    public class AssetInfo
    {
        public int preloadIndex;
        public int preloadSize;
        public PPtr<Object> asset;

        public AssetInfo(ObjectReader reader)
        {
            preloadIndex = reader.ReadInt32();
            preloadSize = reader.ReadInt32();
            asset = new PPtr<Object>(reader);
        }

        public void Write(EndianBinaryWriter writer, uint version)
        {
            writer.Write(preloadIndex);
            writer.Write(preloadSize);
            asset.Write(writer, version);
        }
    }

    public sealed class AssetBundle : NamedObject
    {
        public PPtr<Object>[] m_PreloadTable;
        public KeyValuePair<string, AssetInfo>[] m_Container;
        public AssetInfo m_MainAsset;
        public uint m_RuntimeCompatibility;
        public string m_AssetBundleName;
        public string[] m_Dependencies;
        public bool m_IsStreamedSceneAssetBunlde;
        public int m_ExplicitDataLayout;
        public int m_PathFlags;
        public KeyValuePair<string, string>[] m_SceneHashes;

        public ObjectReader m_Reader;
        public long originByteSize;

        public AssetBundle(ObjectReader reader) : base(reader)
        {
            m_Reader = reader;

            var m_PreloadTableSize = reader.ReadInt32();
            m_PreloadTable = new PPtr<Object>[m_PreloadTableSize];
            for (int i = 0; i < m_PreloadTableSize; i++)
            {
                m_PreloadTable[i] = new PPtr<Object>(reader);
            }

            var m_ContainerSize = reader.ReadInt32();
            m_Container = new KeyValuePair<string, AssetInfo>[m_ContainerSize];
            for (int i = 0; i < m_ContainerSize; i++)
            {
                m_Container[i] = new KeyValuePair<string, AssetInfo>(reader.ReadAlignedString(), new AssetInfo(reader));
            }

            m_MainAsset = new AssetInfo(reader);

            m_RuntimeCompatibility = reader.ReadUInt32();

            m_AssetBundleName = reader.ReadAlignedString();

            var m_DependenciesSize = reader.ReadInt32();
            m_Dependencies = new string[m_DependenciesSize];
            for (int i = 0; i < m_DependenciesSize; i++)
            {
                m_Dependencies[i] = reader.ReadAlignedString();
            }

            m_IsStreamedSceneAssetBunlde = reader.ReadBoolean();

            var endian = EndianType.BigEndian;

            m_ExplicitDataLayout = reader.ReadInt32(endian);

            m_PathFlags = reader.ReadInt32(endian);

            var m_SceneHashesSize = reader.ReadInt32(endian);
            m_SceneHashes = new KeyValuePair<string, string>[m_SceneHashesSize];
            for (int i = 0; i < m_SceneHashesSize; i++)
            {
                m_SceneHashes[i] = new KeyValuePair<string, string>(reader.ReadAlignedString(endian), reader.ReadAlignedString(endian));
            }

            originByteSize = m_Reader.Position - readerStartPosition;

            // Console.WriteLine($"m_Dependencies:{m_Dependencies.Length} {string.Join(" ,", m_Dependencies)}");
            // Console.WriteLine($"m_SceneHashes:{m_SceneHashes.Length} {string.Join(" ,", m_SceneHashes)}");
        }

        public void Recover(AssetBundle target)
        {
            m_Name = target.m_Name;
            m_AssetBundleName = target.m_AssetBundleName;
            m_Dependencies = target.m_Dependencies;
            m_SceneHashes = target.m_SceneHashes;

            m_Name = m_Name.Replace("a1b390950cc1a460cac5b46c06982e92.bundle", "c9a9412b8784cb7f130e25e8832f4a08.bundle");
            m_AssetBundleName = m_Name;
            for (int i = 0; i < m_Dependencies.Length; i++)
            {
                m_Dependencies[i] = m_Dependencies[i].Replace("cab-2355ff173a24de1298fad22c6a5a2679", "cab-8f3a5e097b6c0b34cef62da3f48a16f9");
                m_Dependencies[i] = m_Dependencies[i].Replace("cab-50b4441357112d41c70093b0e72473b3", "cab-50b4441357112d41c70093b0e72473b3");
            }

            using (var stream = new MemoryStream((int)originByteSize))
            {
                using (var writer = new EndianBinaryWriter(stream, EndianType.LittleEndian))
                {
                    Write(writer);
                    var size = writer.Position;
                    Console.WriteLine($"new size:{size}  origin size:{originByteSize}");
                    if (size == originByteSize)
                    {
                        Console.WriteLine("Same size, copyto origin stream");
                        var bytes = new byte[writer.Position];
                        writer.Position = 0;
                        writer.BaseStream.Read(bytes, 0, bytes.Length);

                        var originBytes = new byte[bytes.Length];
                        reader.Position = readerStartPosition;
                        reader.BaseStream.Read(originBytes, 0, originBytes.Length);

                        Console.WriteLine("Difference:");
                        for (int i = 0; i < bytes.Length; i++)
                        {
                            if (bytes[i] != originBytes[i])
                            {
                                Console.WriteLine($"[{i}] {originBytes[i]} - {bytes[i]}");
                            }
                        }

                        reader.Position = readerStartPosition;
                        reader.BaseStream.Write(bytes, 0, bytes.Length);
                        //writer.BaseStream.CopyTo(reader.BaseStream);
                    }
                    else
                    {
                        Console.WriteLine("Need expand stream size");
                    }
                }
            }
        }

        public override void Write(EndianBinaryWriter writer)
        {
            base.Write(writer);

            var version = m_Reader.m_Version;

            writer.Write(m_PreloadTable.Length);
            for (int i = 0; i < m_PreloadTable.Length; i++)
            {
                m_PreloadTable[i].Write(writer, version);
            }

            writer.Write(m_Container.Length);
            for (int i = 0; i < m_Container.Length; i++)
            {
                writer.WriteAlignedString(m_Container[i].Key);
                m_Container[i].Value.Write(writer, version);
            }

            m_MainAsset.Write(writer, version);

            writer.Write(m_RuntimeCompatibility);

            writer.WriteAlignedString(m_AssetBundleName);

            writer.Write(m_Dependencies.Length);
            for (int i = 0; i < m_Dependencies.Length; i++)
            {
                writer.WriteAlignedString(m_Dependencies[i]);
            }

            writer.Write(m_IsStreamedSceneAssetBunlde);

            var endian = EndianType.BigEndian;

            writer.Write(m_ExplicitDataLayout, endian);

            writer.Write(m_PathFlags, endian);

            writer.Write(m_SceneHashes.Length, endian);
            for (int i = 0; i < m_SceneHashes.Length; i++)
            {
                writer.WriteAlignedString(m_SceneHashes[i].Key, endian);
                writer.WriteAlignedString(m_SceneHashes[i].Value, endian);
            }
        }
    }
}
