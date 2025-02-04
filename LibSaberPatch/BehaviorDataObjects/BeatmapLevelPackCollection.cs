﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LibSaberPatch.BehaviorDataObjects
{
    public class BeatmapLevelPackCollection : BehaviorData
    {
        public const int PathID = 1530;

        public List<AssetPtr> beatmapLevelPacks;
        public List<AssetPtr> previewBeatmapLevelPack;

        public BeatmapLevelPackCollection(BinaryReader reader, int _length)
        {
            beatmapLevelPacks = reader.ReadPrefixedList(r => new AssetPtr(r));
            previewBeatmapLevelPack = reader.ReadPrefixedList(r => new AssetPtr(r));
        }

        public override bool Equals(BehaviorData data)
        {
            //TODO Implement
            return false;
        }

        public override int SharedAssetsTypeIndex()
        {
            return 0x01;
        }

        public override void WriteTo(BinaryWriter w)
        {
            w.WritePrefixedList(beatmapLevelPacks, a => a.WriteTo(w));
            w.WritePrefixedList(previewBeatmapLevelPack, a => a.WriteTo(w));
        }

        public override void Trace(Action<AssetPtr> action)
        {
            foreach (AssetPtr p in beatmapLevelPacks)
            {
                action(p);
            }
            foreach (AssetPtr p in previewBeatmapLevelPack)
            {
                action(p);
            }
        }
    }
}
