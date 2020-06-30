using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AssetStudio
{
    public class NamedObject : EditorExtension
    {
        public string m_Name;
        public long readerStartPosition;

        protected NamedObject(ObjectReader reader) : base(reader)
        {
            readerStartPosition = reader.Position;
            m_Name = reader.ReadAlignedString();
        }

        public virtual void Write(EndianBinaryWriter writer)
        {
            writer.WriteAlignedString(m_Name);
        }
    }
}
