﻿using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Threading.Tasks;
using LibSaberPatch;
using Newtonsoft.Json;
using System.Linq;
using LibSaberPatch.BehaviorDataObjects;
using LibSaberPatch.AssetDataObjects;

namespace app
{
    class Program
    {
        static void Main(string[] args)
        {
            if(args.Length < 1) {
                Console.WriteLine("arguments: pathToAPKFileToModify [-r removeSongs] levelFolders...");
                return;
            }
            bool removeSongs = false;
            if (args.Contains("-r") || args.Contains("removeSongs"))
            {
                removeSongs = true;
            }
            bool replaceExtras = false;
            if (args.Contains("-e"))
            {
                replaceExtras = true;
            }
            string apkPath = args[0];
            using (Apk apk = new Apk(apkPath)) {
                apk.PatchSignatureCheck();

                byte[] data = apk.ReadEntireEntry(Apk.MainAssetsFile);
                SerializedAssets assets = SerializedAssets.FromBytes(data);

                string colorPath = "assets/bin/Data/sharedassets1.assets";
                SerializedAssets colorAssets = SerializedAssets.FromBytes(apk.ReadEntireEntry(colorPath));

                //string textAssetsPath = "assets/bin/Data/c4dc0d059266d8d47862f46460cf8f31";
                string textAssetsPath = "assets/bin/Data/231368cb9c1d5dd43988f2a85226e7d7";
                SerializedAssets textAssets = SerializedAssets.FromBytes(apk.ReadEntireEntry(textAssetsPath));
                var aotext = textAssets.GetAssetAt(1);
                TextAssetAssetData ta = aotext.data as TextAssetAssetData;
                var segments = Utils.ReadLocaleText(ta.script, new List<char>() { ',', ',', '\n' });
                Utils.ApplyWatermark(segments);
                ta.script = Utils.WriteLocaleText(segments, new List<char>() { ',', ',', '\n' });
                apk.ReplaceAssetsFile(textAssetsPath, textAssets.ToBytes());

                var newPacks = new List<ulong>();

                HashSet<string> existingLevels = assets.ExistingLevelIDs();
                LevelCollectionBehaviorData customCollection = assets.FindCustomLevelCollection();
                LevelPackBehaviorData customPack = assets.FindCustomLevelPack();
                ulong customPackPathID = assets.GetAssetObjectFromScript<LevelPackBehaviorData>(mob => mob.name == "CustomLevelPack", b => true).pathID;

                for (int i = 1; i < args.Length; i++) {
                    if (args[i] == "-r" || args[i] == "removeSongs" || args[i] == "-e")
                    {
                        continue;
                    }
                    if (args[i] == "-ac")
                    {
                        // Add custom collection, needs three parameters
                        if (i + 3 >= args.Length)
                        {
                            // There is STILL not enough data. We should exit.
                            Console.WriteLine($"[ERROR] Could not rename custom songs pack because there were not enough arguments!");
                        }
                        var collection = Utils.CreateCustomCollection(assets, args[i + 1] + "Collection");
                        var pack = Utils.CreateCustomPack(assets, collection, args[i + 1], args[i + 2]);

                        var c = collection.FollowToScript<LevelCollectionBehaviorData>(assets);
                        var texturePointer = assets.AppendAsset(Texture2DAssetData.CoverFromImageFile(args[i + 3], args[i + 1], true));
                        var spPointer = assets.AppendAsset(Utils.CreateSprite(assets, texturePointer, args[i + 1] + "CoverSprite"));
                        pack.FollowToScript<LevelPackBehaviorData>(assets).coverImage = spPointer;
                        // Add pack pointer to "mainLevelPack"
                        Console.WriteLine($"Added new {args[i + 1]} to all packs!");
                        newPacks.Add(pack.pathID);

                        Utils.FindLevels(args[i + 4], (levelFolder) =>
                        {
                            try
                            {
                                JsonLevel level = JsonLevel.LoadFromFolder(levelFolder);
                                string levelID = level.GenerateBasicLevelID();
                                var apkTxn = new Apk.Transaction();

                                // This will add duplicate levels and not really care what you think about it.
                                // It will also add a new collection every iteration - it does ZERO checking for duplicates
                                Console.WriteLine($"Adding to Collection: {args[i + 1]}:  {level._songName}");
                                var assetsTxn = new SerializedAssets.Transaction(assets);
                                var levelData = level.ToAssetData(assetsTxn.scriptIDToScriptPtr, levelID);
                                AssetPtr levelPtr = level.AddToAssets(assetsTxn, apkTxn, levelID, levelData);

                                // Danger should be over, nothing here should fail
                                assetsTxn.ApplyTo(assets);
                                c.levels.Add(levelPtr);
                                existingLevels.Add(levelID);
                                apkTxn.ApplyTo(apk);
                            }
                            catch (FileNotFoundException e)
                            {
                                Console.WriteLine("[SKIPPING] Missing file referenced by level: {0}", e.FileName);
                            }
                            catch (JsonReaderException e)
                            {
                                Console.WriteLine("[SKIPPING] Invalid level JSON: {0}", e.Message);
                            }
                        });
                        i += 4;
                        continue;
                    }
                    if (args[i] == "-i")
                    {
                        // Add custom collection naming support
                        int arguments = 2;
                        if (i + arguments >= args.Length)
                        {
                            // There is not enough data after the c, replace just the name?
                            Console.WriteLine($"Only replacing PackName!");
                            arguments--;
                        }
                        if (i + 1 >= args.Length)
                        {
                            // There is STILL not enough data. We should exit.
                            Console.WriteLine($"[ERROR] Could not rename custom songs pack because there were not enough arguments!");
                        }
                        if (arguments >= 1)
                        {
                            Console.WriteLine($"Renamed CustomPack PackName from: {customPack.packName} to: {args[i + 1]}");
                            customPack.packName = args[i + 1];
                        }
                        if (arguments >= 2)
                        {
                            Console.WriteLine($"Renamed CustomPack PackID from: {customPack.packName} to: {args[i + 2]}");
                            customPack.packID = args[i + 2];
                        }
                        i += arguments;
                        continue;
                    }
                    if (args[i] == "-t")
                    {
                        if (i + 2 >= args.Length)
                        {
                            // There is not enough data after the text
                            // Reset it.
                            //continue;
                        }
                        string key = args[i + 1].ToUpper();

                        //segments.ToList().ForEach(a => Console.Write(a.Trim() + ","));
                        List<string> value;
                        if (!segments.TryGetValue(key.Trim(), out value))
                        {
                            Console.WriteLine($"[ERROR] Could not find key: {key} in text!");
                        }
                        Console.WriteLine($"Found key at index: {key.Trim()} with value: {value[value.Count - 1]}");
                        segments[key.Trim()][value.Count - 1] = args[i + 2];
                        Console.WriteLine($"New value: {args[i + 2]}");
                        i += 2;
                        ta.script = Utils.WriteLocaleText(segments, new List<char>() { ',', ',', '\n' });
                        apk.ReplaceAssetsFile(textAssetsPath, textAssets.ToBytes());
                        //Console.WriteLine((a.data as TextAsset).script);
                        continue;
                    }
                    if (args[i] == "-c1" || args[i] == "-c2")
                    {
                        if (i + 1 >= args.Length)
                        {
                            // There is nothing after the color
                            // Reset it.
                            Utils.ResetColors(colorAssets);
                            apk.ReplaceAssetsFile(colorPath, colorAssets.ToBytes());
                            continue;
                        }
                        if (args[i + 1].StartsWith("#"))
                        {
                            string hexString = args[i + 1].Substring(1);
                            if (hexString.Length == 6)
                            {
                                hexString += "FF";
                            }
                            if (hexString.Length != 8)
                            {
                                Console.WriteLine($"[ERROR] invalid length color hexstring: {hexString} it should instead be 8 characters!");
                            }
                            float[] colors = new float[4];
                            for (int j = 0; j < hexString.Length; j += 2)
                            {
                                colors[j / 2] = Utils.HexToBytes("" + hexString[j] + hexString[j + 1])[0] / 255.0f;
                            }
                            SimpleColor color = new SimpleColor()
                            {
                                r = colors[0],
                                g = colors[1],
                                b = colors[2],
                                a = colors[3]
                            };

                            var manager = Utils.CreateColor(colorAssets, color, args[i] == "-c1");

                            i++;
                            continue;
                        }
                        if (!args[i + 1].StartsWith("("))
                        {
                            // Reset it.
                            Utils.ResetColors(colorAssets);
                            apk.ReplaceAssetsFile(colorPath, colorAssets.ToBytes());
                            continue;
                        }
                        if (i + 4 >= args.Length)
                        {
                            Console.WriteLine($"[ERROR] Cannot parse color, not enough colors! Please copy-paste a series of floats");
                            i += 4;
                            continue;
                        }

                        SimpleColor c = new SimpleColor
                        {
                            r = Convert.ToSingle(args[i + 1].Split(',')[0].Replace('(', '0')),
                            g = Convert.ToSingle(args[i + 2].Split(',')[0].Replace('(', '0')),
                            b = Convert.ToSingle(args[i + 3].Split(',')[0].Replace(')', '0')),
                            a = Convert.ToSingle(args[i + 4].Split(',')[0].Replace(')', '.'))
                        };

                        ColorManager dat = Utils.CreateColor(colorAssets, c, args[i] == "-c1");

                        i += 4;
                        continue;
                    }
                    if (args[i] == "-g")
                    {
                        string path = "assets/bin/Data/level11";
                        SerializedAssets a = SerializedAssets.FromBytes(apk.ReadEntireEntry(path));
                        var gameobject = a.FindGameObject("LeftSaber");
                        var script = gameobject.components[4].FollowToScript<Saber>(a);
                        Console.WriteLine($"GameObject: {gameobject}");
                        foreach (AssetPtr p in gameobject.components)
                        {
                            Console.WriteLine($"Component: {p.pathID} followed: {p.Follow(a)}");
                        }
                        Console.WriteLine($"Left saber script: {script}");
                        // Find all objects that have the GameObject: LeftSaber (pathID = 20, fileID = 0 (142))

                        continue;
                    }
                    if (args[i] == "-s")
                    {
                        string cusomCoverFile = args[i + 1];
                        try
                        {
                            //assets.SetAssetAt(14, dat);
                            var spPtr = customPack.coverImage.Follow<SpriteAssetData>(assets);
                            if (spPtr != null)
                            {
                                var texture = spPtr.texture.Follow<Texture2DAssetData>(assets);
                                if (texture.name == "CustomSongsCover")
                                {
                                    Console.WriteLine($"Replacing existing Sprite + Texture2D at: {customPack.coverImage.pathID}");
                                    assets.SetAssetAt(spPtr.texture.pathID, Texture2DAssetData.CoverFromImageFile(args[i + 1], "CustomSongs", true));
                                    i++;
                                    continue;
                                }
                            }
                            var ptr = assets.AppendAsset(Texture2DAssetData.CoverFromImageFile(args[i + 1], "CustomSongs", true));
                            Console.WriteLine($"Added Texture at PathID: {ptr.pathID} with new Texture2D from file: {args[i + 1]}");
                            var sPtr = assets.AppendAsset(Utils.CreateSprite(assets, ptr, "CustomSongsCoverSprite"));
                            Console.WriteLine($"Added Sprite at PathID: {sPtr.pathID}!");
                            customPack.coverImage = sPtr;

                        } catch (FileNotFoundException)
                        {
                            Console.WriteLine($"[ERROR] Custom cover file does not exist: {args[i+1]}");
                        }
                        i++;
                        continue;
                    }
                    var serializationFuncs = new List<Func<(JsonLevel, List<List<MonoBehaviorAssetData>>, string, string)>>();
                    Utils.FindLevels(args[i], levelFolder => {
                        try {
                            JsonLevel level = JsonLevel.LoadFromFolder(levelFolder);
                            string levelID = level.GenerateBasicLevelID();
                            var apkTxn = new Apk.Transaction();

                            if (existingLevels.Contains(levelID)) {
                                if (removeSongs)
                                {
                                    // Currently does not handle transactions
                                    Console.WriteLine($"Removing: {level._songName}");
                                    existingLevels.Remove(levelID);

                                    var l = assets.GetLevelMatching(levelID);
                                    var ao = assets.GetAssetObjectFromScript<LevelBehaviorData>(p => p.levelID == l.levelID);

                                    ulong lastLegitPathID = 201;

                                    // Currently, this removes all songs matching the song 
                                    // the very first time it runs
                                    customCollection.levels.RemoveAll(ptr => ptr.pathID > lastLegitPathID && ao.pathID == ptr.pathID);
                                    foreach (string s in l.OwnedFiles(assets))
                                    {
                                        if (apk != null) apk.RemoveFileAt($"assets/bin/Data/{s}");
                                    }

                                    Utils.RemoveLevel(assets, l);

                                    apkTxn.ApplyTo(apk);
                                } else
                                {
                                    Console.WriteLine($"Present: {level._songName}");
                                }
                            } else {
                                serializationFuncs.Add(() => {
                                    var levelData = level.ToAssetData(assets.scriptIDToScriptPtr, levelID);
                                    existingLevels.Add(levelID);

                                    Console.WriteLine($"Serialized:  {level._songName}");

                                    return (level, levelData, levelID, level._songName);
                                });
                            }
                        } catch (FileNotFoundException e) {
                            Console.WriteLine("[SKIPPING] Missing file referenced by level: {0}", e.FileName);
                        } catch (JsonReaderException e) {
                            Console.WriteLine("[SKIPPING] Invalid level JSON: {0}", e.Message);
                        }
                    });

                    var serializedAssets = new (JsonLevel, List<List<MonoBehaviorAssetData>>, string, string)[serializationFuncs.Count];

                    Parallel.ForEach(serializationFuncs, (func, state, idx) => {
                        serializedAssets[idx] = func();
                    });

                    var assetsBatchTxn = new SerializedAssets.Transaction(assets);
                    var apkBatchTxn = new Apk.Transaction();

                    foreach (var (level, levelData, levelID, songName) in serializedAssets) {
                        Console.WriteLine($"Adding:  {songName}");

                        var levelPtr = level.AddToAssets(assetsBatchTxn, apkBatchTxn, levelID, levelData);

                        customCollection.levels.Add(levelPtr);
                    }

                    assetsBatchTxn.ApplyTo(assets);
                    apkBatchTxn.ApplyTo(apk);
                }
                byte[] outData = assets.ToBytes();
                apk.ReplaceAssetsFile(Apk.MainAssetsFile, outData);
                apk.ReplaceAssetsFile(colorPath, colorAssets.ToBytes());

                string mainPackFile = "assets/bin/Data/sharedassets19.assets";
                SerializedAssets mainPackAssets = SerializedAssets.FromBytes(apk.ReadEntireEntry(mainPackFile));

                int fileI = mainPackAssets.externals.FindIndex(e => e.pathName == "sharedassets17.assets") + 1;
                Console.WriteLine($"Found sharedassets17.assets at FileID: {fileI}");
                var mainLevelPack = mainPackAssets.FindMainLevelPackCollection();
                var pointerPacks = mainLevelPack.beatmapLevelPacks[mainLevelPack.beatmapLevelPacks.Count - 1];
                Console.WriteLine($"Original last pack FileID: {pointerPacks.fileID} PathID: {pointerPacks.pathID}");

                if (!mainLevelPack.beatmapLevelPacks.Any(ptr => ptr.fileID == fileI && ptr.pathID == customPackPathID))
                {
                    Console.WriteLine($"Added CustomLevelPack to {mainPackFile}");
                    if (replaceExtras)
                    {
                        Console.WriteLine("Replacing ExtrasPack!");
                        mainLevelPack.beatmapLevelPacks[2] = new AssetPtr(fileI, customPackPathID);
                    }
                    else
                    {
                        Console.WriteLine("Adding as new Pack!");
                        mainLevelPack.beatmapLevelPacks.Add(new AssetPtr(fileI, customPackPathID));
                    }
                }
                foreach (var newPack in newPacks)
                {
                    Console.WriteLine($"Adding new pack at PathID: {newPack}");
                    mainLevelPack.beatmapLevelPacks.Add(new AssetPtr(fileI, newPack));
                }
                pointerPacks = mainLevelPack.beatmapLevelPacks[mainLevelPack.beatmapLevelPacks.Count - 1];
                Console.WriteLine($"New last pack FileID: {pointerPacks.fileID} PathID: {pointerPacks.pathID}");
                apk.ReplaceAssetsFile(mainPackFile, mainPackAssets.ToBytes());

                Console.WriteLine("Complete!");
            }

            Console.WriteLine("Signing APK...");
            Signer.Sign(apkPath);
        }
    }
}
