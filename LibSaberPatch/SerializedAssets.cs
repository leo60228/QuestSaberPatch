﻿using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LibSaberPatch
{
    public class SerializedAssets
    {
        /// padding between metadata and data
        int paddingLen;

        string version;
        int targetPlatform;
        bool enableTypeTree;

        public List<SerializedAssets.TypeRef> types;
        public List<SerializedAssets.AssetObject> objects;
        public List<SerializedAssets.Script> scripts;
        public List<SerializedAssets.External> externals;

        private ulong resetPoint;
        private bool _beginRemoval = false;
        private bool beginRemoval
        {
            get { return _beginRemoval; }
            set { _beginRemoval = value; if (value) resetPoint = objects[objects.Count - 1].pathID; }
        }

        public class TypeRef {
            public int classID;
            bool isStripped;
            ushort scriptTypeIndex;
            byte[] scriptID;
            byte[] typeHash;

            public TypeRef(BinaryReader reader) {
                classID = reader.ReadInt32();
                isStripped = reader.ReadBoolean();
                scriptTypeIndex = reader.ReadUInt16();
                if(classID == 114) {
                    scriptID = reader.ReadBytes(16);
                }
                typeHash = reader.ReadBytes(16);
            }

            public void WriteTo(BinaryWriter w) {
                w.Write(classID);
                w.Write(isStripped);
                w.Write(scriptTypeIndex);
                if(classID == 114) {
                    w.Write(scriptID);
                }
                w.Write(typeHash);
            }
        }

        public class AssetObject {
            public ulong pathID;
            public int typeID;
            public AssetData data;
            public int paddingLen;

            // These are only used during read in and write out. Between those
            // times modifications can make them invalid but they'll be fixed
            // up during the Write process.
            public int offset;
            public int size;

            public AssetObject() {}

            public AssetObject(BinaryReader reader) {
                reader.AlignStream();
                pathID = reader.ReadUInt64();
                offset = reader.ReadInt32();
                size = reader.ReadInt32();
                typeID = reader.ReadInt32();
                // Console.WriteLine((pathID, offset, size, typeID));
            }

            // returns the location to patch the offsets
            public long WriteTo(BinaryWriter w) {
                w.AlignStream();
                w.Write(pathID);
                long patchPos = w.BaseStream.Position;
                w.Write(offset);
                w.Write(size);
                w.Write(typeID);
                return patchPos;
            }
        }

        public class Script {
            int fileIndex;
            ulong inFileID;

            public Script(BinaryReader reader) {
                fileIndex = reader.ReadInt32();
                reader.AlignStream();
                inFileID = reader.ReadUInt64();
            }

            public void WriteTo(BinaryWriter w) {
                w.Write(fileIndex);
                w.AlignStream();
                w.Write(inFileID);
            }
        }

        public class External {
            string tempEmpty;
            byte[] guid;
            int type;
            string pathName;

            public External(BinaryReader reader) {
                tempEmpty = reader.ReadStringToNull();
                guid = reader.ReadBytes(16);
                type = reader.ReadInt32();
                pathName = reader.ReadStringToNull();
            }

            public void WriteTo(BinaryWriter w) {
                w.WriteCString(tempEmpty);
                w.Write(guid);
                w.Write(type);
                w.WriteCString(pathName);
            }
        }

        public class ParseException : ApplicationException {
            public ParseException(string msg) : base(msg) {}
        }

        private const int headerLen = 5*4;
        private const int parsedGeneration = 17;

        public static SerializedAssets FromBytes(byte[] data) {
            using (Stream stream = new MemoryStream(data)) {
                return new SerializedAssets(stream);
            }
        }

        public byte[] ToBytes() {
            using (MemoryStream stream = new MemoryStream()) {
                WriteTo(stream);
                stream.Close();
                return stream.ToArray();
            }
        }

        public SerializedAssets(Stream stream) {
            BinaryReader reader = new BinaryReader(stream);

            // ===== Parse Header
            int metadataSize = reader.ReadInt32BE();
            int fileSize = reader.ReadInt32BE();
            int generation = reader.ReadInt32BE();
            if(generation != parsedGeneration) throw new ParseException("Unsupported format version");
            int dataOffset = reader.ReadInt32BE();
            int isBigEndian = reader.ReadInt32BE();
            if(isBigEndian != 0) throw new ParseException("Must be little endian");
            paddingLen = dataOffset - (metadataSize + headerLen);

            // ===== Parse Metadata
            version = reader.ReadStringToNull();
            if(version != "2018.3.10f1") throw new ParseException("Unsupported Unity version");
            targetPlatform = reader.ReadInt32();
            enableTypeTree = reader.ReadBoolean();
            if(enableTypeTree) throw new ParseException("Type trees aren't supported");

            types = reader.ReadPrefixedList(r => new TypeRef(r));
            objects = reader.ReadPrefixedList(r => new AssetObject(r));
            scripts = reader.ReadPrefixedList(r => new Script(r));
            externals = reader.ReadPrefixedList(r => new External(r));
            // this is necessary to get headerLen+metadataSize to match up with offset
            if(reader.ReadByte() != 0) throw new ParseException("Expected metadata to end with 0");

            if(!reader.ReadAllZeros(paddingLen)) throw new ParseException("Expected zeros for padding");
            Debug.Assert(reader.BaseStream.Position == dataOffset, "Parsed metadata wrong");

            // ===== Parse Data
            for(int i = 0; i < objects.Count-1; i++) {
                objects[i].paddingLen = objects[i+1].offset-(objects[i].offset+objects[i].size);
            }
            // I've never seen any padding after the last object but handle it just in case
            AssetObject last = objects[objects.Count-1];
            int dataSize = fileSize - dataOffset;
            last.paddingLen = dataSize - (last.offset+last.size);

            foreach(AssetObject obj in objects) {
                // Console.WriteLine((reader.BaseStream.Position-dataOffset, obj.offset, obj.size));
                if(reader.BaseStream.Position-dataOffset != obj.offset) {
                    throw new ParseException("Objects aren't in order");
                }
                long startOffset = reader.BaseStream.Position;
                switch(types[obj.typeID].classID) {
                    case MonoBehaviorAssetData.ClassID:
                        obj.data = new MonoBehaviorAssetData(reader, obj.size);
                        break;
                    case AudioClipAssetData.ClassID:
                        obj.data = new AudioClipAssetData(reader, obj.size);
                        break;
                    case Texture2DAssetData.ClassID:
                        obj.data = new Texture2DAssetData(reader, obj.size);
                        break;
                    default:
                        obj.data = new UnknownAssetData(reader, obj.size);
                        break;
                }
                long bytesParsed = reader.BaseStream.Position - startOffset;
                if(bytesParsed != obj.size)
                    throw new ParseException($"Parsed {bytesParsed} but expected {obj.size} for {obj.pathID}");
                if(!reader.ReadAllZeros(obj.paddingLen)) throw new ParseException("Expected zeros for padding");
            }
        }

        private static void PatchInt(byte[] arr, long index, int val, bool bigEndian) {
            byte[] buff = BitConverter.GetBytes(val);
            if(bigEndian) Array.Reverse(buff);
            for(int i = 0; i < 4; i++) arr[i+index] = buff[i];
        }

        public void WriteTo(Stream outStream) {
            List<long> patchLocs = new List<long>(objects.Count);
            byte[] buf;
            int length;
            int dataOffset;
            int metadataSize;
            using (MemoryStream stream = new MemoryStream()) {
                BinaryWriter w = new BinaryWriter(stream);

                // ===== Header
                w.Write((int)0); // patch
                w.Write((int)0); // patch
                w.WriteInt32BE(parsedGeneration);
                w.Write((int)0); // patch
                w.WriteInt32BE(0); // not big endian

                // ===== Metadata
                w.WriteCString(version);
                w.Write(targetPlatform);
                w.Write(enableTypeTree);

                w.WritePrefixedList(types, x => x.WriteTo(w));
                w.WritePrefixedList(objects, x => patchLocs.Add(x.WriteTo(w)));
                w.WritePrefixedList(scripts, x => x.WriteTo(w));
                w.WritePrefixedList(externals, x => x.WriteTo(w));
                w.Write((byte)0);
                metadataSize = (int)w.BaseStream.Position - headerLen;

                w.WriteZeros(paddingLen);
                w.AlignStream();

                // ===== Data
                dataOffset = (int)w.BaseStream.Position;
                foreach(AssetObject obj in objects) {
                    obj.offset = (int)w.BaseStream.Position - dataOffset;
                    obj.data.WriteTo(w);
                    obj.size = ((int)w.BaseStream.Position - dataOffset) - obj.offset;
                    w.WriteZeros(obj.paddingLen);

                    // TODO do objects need to be aligned?
                    // All objects I can find are. If nothing is modified this shouldn't do anything.
                    // But if we change the size of an object it's probably more important to preserve
                    // alignment than the exact amount of padding.
                    w.AlignStream();
                }

                length = (int)stream.Length;
                stream.Close();
                buf = stream.GetBuffer();
            }

            // Patch header
            PatchInt(buf, 0*4, metadataSize, true);
            PatchInt(buf, 1*4, length, true);
            PatchInt(buf, 3*4, dataOffset, true);

            // Patch objects
            for(int i = 0; i < patchLocs.Count; i++) {
                PatchInt(buf, patchLocs[i] + 0*4, objects[i].offset, false);
                PatchInt(buf, patchLocs[i] + 1*4, objects[i].size, false);
            }

            outStream.Write(buf, 0, length);
        }

        public AssetPtr AppendAsset(AssetData data) {
            ulong pathID = (ulong)(objects.Count + 1);
            AssetObject obj = new AssetObject() {
                pathID = pathID,
                typeID = data.SharedAssetsTypeIndex(),
                data = data,
                paddingLen = 0,
            };
            objects.Add(obj);
            return new AssetPtr(0, pathID);
        }

        private void ShiftPathIDs(int startIndex, ulong startPathID)
        {
            // Shift all remaining PathIDs by -1
            for (int i = startIndex; i < objects.Count; i++)
            {
                if (objects[i].pathID > startPathID)
                {
                    objects[i].pathID--;
                }
            }
            // HOWEVER! THIS STILL DOESN'T SHIFT THE PATHIDS OF POINTERS THAT WERE POINTING TO THESE THINGS!
            // THIS IS POSSIBLE THE LARGEST PROBLEM THAT REMAINS WITH DELETING SMOOTHLY AND QUICKLY!
        }

        public AssetObject GetAsset(Predicate<AssetObject> p)
        {
            return objects.Find(p);
        }

        /// <summary>
        /// Indicates that you are done removing objects and to update PathIDs of all remaining objects.
        /// </summary>
        public void EndRemoval()
        {
            ShiftPathIDs(260, resetPoint);
            beginRemoval = false;
        }

        public AssetObject RemoveAsset(Predicate<AssetObject> p)
        {
            if (!beginRemoval) beginRemoval = true;
            // First, find matching AssetObj
            int objI = objects.FindIndex(p);
            AssetObject obj = objects[objI];
            objects.RemoveAt(objI);
            // Now we need to find all assets that reference this asset's path ID.
            // Alternatively, we can just let it crash, as I don't know how we would 
            // know what to change those pointers to in order to avoid a crash
            return obj;
        }

        public AssetObject RemoveAsset(AssetData data)
        {
            if (!beginRemoval) beginRemoval = true;
            return RemoveAsset(d => d.data.GetType().Equals(data) && d.data.Equals(data));
        }

        public AssetObject RemoveAssetAt(ulong pathID)
        {
            if (!beginRemoval) beginRemoval = true;
            return RemoveAsset(d => d.pathID == pathID);
        }

        public AssetObject GetAssetAt(ulong pathID)
        {
            return objects.Find(d => d.pathID == pathID);
        }

        public LevelBehaviorData GetLevelMatching(string levelID)
        {
            AssetObject obj = objects.Find(d => d.data.GetType().Equals(typeof(MonoBehaviorAssetData))
            && (d.data as MonoBehaviorAssetData).data.GetType().Equals(typeof(LevelBehaviorData))
            && ((d.data as MonoBehaviorAssetData).data as LevelBehaviorData).levelID == levelID);
            return ((obj.data as MonoBehaviorAssetData).data as LevelBehaviorData);
        }

        public HashSet<string> ExistingLevelIDs() {
            var set = new HashSet<string>();
            foreach(AssetObject obj in objects) {
                if(!(obj.data is MonoBehaviorAssetData))
                    continue;
                MonoBehaviorAssetData monob = (MonoBehaviorAssetData)obj.data;
                if(!(monob.data is LevelBehaviorData))
                    continue;
                LevelBehaviorData levelData = (LevelBehaviorData)monob.data;
                set.Add(levelData.levelID);
            }
            return set;
        }

        public LevelCollectionBehaviorData FindExtrasLevelCollection() {
            AssetObject obj = objects[235]; // the index of the extras collection in sharedassets17
            if(!(obj.data is MonoBehaviorAssetData))
                throw new ParseException("Extras level collection not at normal spot");
            MonoBehaviorAssetData monob = (MonoBehaviorAssetData)obj.data;
            if(monob.name != "ExtrasLevelCollection")
                throw new ParseException("Extras level collection not at normal spot");
            return (LevelCollectionBehaviorData)monob.data;
        }

        public class Transaction {
            ulong lastPathID;
            List<AssetData> toAdd;

            public Transaction(SerializedAssets assets) {
                lastPathID = (ulong)assets.objects.Count;
                toAdd = new List<AssetData>();
            }

            public AssetPtr AppendAsset(AssetData data) {
                toAdd.Add(data);
                lastPathID += 1;
                return new AssetPtr(0, lastPathID);
            }

            public void ApplyTo(SerializedAssets assets) {
                Debug.Assert((ulong)(assets.objects.Count + toAdd.Count) == lastPathID, "Can't add anything while transaction is live");
                foreach(AssetData obj in toAdd) {
                    assets.AppendAsset(obj);
                }
            }
        }
    }
}
