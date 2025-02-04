﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LibSaberPatch.BehaviorDataObjects
{
    public class SimpleColor : BehaviorData
    {
        public const int PathID = 423;

        public float r;
        public float g;
        public float b;
        public float a;

        public SimpleColor() { }

        public SimpleColor(BinaryReader reader, int _length)
        {
            r = reader.ReadSingle();
            g = reader.ReadSingle();
            b = reader.ReadSingle();
            a = reader.ReadSingle();
        }
        public override bool Equals(BehaviorData data)
        {
            if (GetType().Equals(data))
            {
                SimpleColor c = data as SimpleColor;
                return r == c.r && g == c.g && b == c.b && a == c.a;
            }
            return false;
        }

        public override int SharedAssetsTypeIndex()
        {
            return 13;
        }

        public override void WriteTo(BinaryWriter w)
        {
            w.Write(r);
            w.Write(g);
            w.Write(b);
            w.Write(a);
        }
    }
}
