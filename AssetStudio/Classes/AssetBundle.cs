using System;
using System.Collections.Generic;
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

        public AssetBundle(ObjectReader reader) : base(reader)
        {
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

            // Console.WriteLine($"m_Dependencies:{m_Dependencies.Length} {string.Join(" ,", m_Dependencies)}");
            // Console.WriteLine($"m_SceneHashes:{m_SceneHashes.Length} {string.Join(" ,", m_SceneHashes)}");
        }
    }
}
