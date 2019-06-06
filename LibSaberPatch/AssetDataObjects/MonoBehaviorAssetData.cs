﻿using LibSaberPatch.BehaviorDataObjects;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LibSaberPatch.AssetDataObjects
{
    public sealed class MonoBehaviorAssetData : AssetData
    {
        public const int ClassID = 114;

        public AssetPtr gameObject; // always zero AFAIK
        public int enabled;
        public AssetPtr script;
        public string name;
        public BehaviorData data;

        public MonoBehaviorAssetData()
        {
            gameObject = new AssetPtr(0, 0);
            enabled = 1;
        }

        public MonoBehaviorAssetData(BinaryReader reader, int length, byte[] scriptID)
        {
            int startOffset = (int)reader.BaseStream.Position;
            gameObject = new AssetPtr(reader);
            enabled = reader.ReadInt32();
            script = new AssetPtr(reader);
            name = reader.ReadAlignedString();
            int headerLen = (int)reader.BaseStream.Position - startOffset;

            if (scriptID.SequenceEqual(LevelBehaviorData.ScriptID))
            {
                data = new LevelBehaviorData(reader, length - headerLen);
                return;
            }
            if (scriptID.SequenceEqual(LevelCollectionBehaviorData.ScriptID))
            {
                data = new LevelCollectionBehaviorData(reader, length - headerLen);
                return;
            }
            if (scriptID.SequenceEqual(BeatmapDataBehaviorData.ScriptID))
            {
                data = new BeatmapDataBehaviorData(reader, length - headerLen);
                return;
            }
            if (scriptID.SequenceEqual(LevelPackBehaviorData.ScriptID))
            {
                data = new LevelPackBehaviorData(reader, length - headerLen);
                return;
            }
            if (scriptID.SequenceEqual(ColorManager.ScriptID))
            {
                data = new ColorManager(reader, length - headerLen);
                return;
            }
            if (scriptID.SequenceEqual(SimpleColor.ScriptID))
            {
                data = new SimpleColor(reader, length - headerLen);
                return;
            }
            if (scriptID.SequenceEqual(BeatmapLevelPackCollection.ScriptID))
            {
                data = new BeatmapLevelPackCollection(reader, length - headerLen);
                return;
            }

            switch (script.pathID)
            {
                case TextMeshPro.PathID:
                    data = new TextMeshPro(reader, length - headerLen);
                    break;
                case Saber.PathID:
                    data = new Saber(reader, length - headerLen);
                    break;
                case LocalizedTextMeshProUGUI.PathID:
                    data = new LocalizedTextMeshProUGUI(reader, length - headerLen);
                    break;
                case Localization.PathID:
                    data = new Localization(reader, length - headerLen);
                    break;
                default:
                    data = new UnknownBehaviorData(reader, length - headerLen);
                    break;
            }
            if (!(data is UnknownBehaviorData))
            {
                Console.WriteLine($"Type: {data.GetType()} ScriptHash: {BitConverter.ToString(scriptID).Replace("-", "")}");
            }
        }

        public sealed override void WriteTo(BinaryWriter w)
        {
            gameObject.WriteTo(w);
            w.Write(enabled);
            script.WriteTo(w);
            w.WriteAlignedString(name);
            data.WriteTo(w);
        }

        public sealed override int SharedAssetsTypeIndex()
        {
            return data.SharedAssetsTypeIndex();
        }

        public sealed override void Trace(Action<AssetPtr> action)
        {
            // So, we have AssetPtrs, however, we don't want to delete any of the pointers for Gameobject/Script
            // We DO want to delete/call trace on all pointers that are in data.
            data.Trace(action);
        }

        public sealed override List<string> OwnedFiles(SerializedAssets assets)
        {
            return data.OwnedFiles(assets);
        }
    }
}
