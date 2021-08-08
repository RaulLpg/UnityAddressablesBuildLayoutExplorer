﻿//
// Addressables Build Layout Explorer for Unity. Copyright (c) 2021 Peter Schraut (www.console-dev.de). See LICENSE.md
// https://github.com/pschraut/UnityAddressablesBuildLayoutExplorer
//
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Oddworm.EditorFramework
{
    [System.Serializable]
    public class BuildLayout
    {
        public string unityVersion;
        public string addressablesVersion;
        public List<Group> groups = new List<Group>();

        [System.Serializable]
        public class Group
        {
            public string name;
            public long size;
            public List<Archive> bundles = new List<Archive>();
        }

        [System.Serializable]
        public class Archive
        {
            public string name;
            public long size;
            public string compression;
            public long assetBundleObjectSize;
            public List<string> bundleDependencies = new List<string>();
            public List<string> expandedBundleDependencies = new List<string>();
            public List<ExplicitAsset> explicitAssets = new List<ExplicitAsset>();
        }

        [System.Serializable]
        public class ExplicitAsset
        {
            public string name;
            public long size;
            public string address;
            public List<string> externalReferences = new List<string>();
            public List<string> internalReferences = new List<string>();
        }

        public static BuildLayout Load(string path)
        {
            var text = System.IO.File.ReadAllText(path);
            return Parse(text);
        }

        public static BuildLayout Parse(string text)
        {
            var layout = new BuildLayout();

            // Convert text to lines for easier processing
            var lines = new List<string>();
            foreach (var line in text.Split(new[] { '\n' }))
            {
                if (!string.IsNullOrWhiteSpace(line))
                    lines.Add(line);
            }

            for (var n = 0; n < lines.Count; ++n)
            {
                var line = lines[n];

                if (line.StartsWith("Unity Version:", System.StringComparison.Ordinal))
                {
                    layout.unityVersion = line.Substring("Unity Version:".Length).Trim();
                    continue;
                }

                if (line.StartsWith("com.unity.addressables:", System.StringComparison.Ordinal))
                {
                    layout.addressablesVersion = line.Substring("com.unity.addressables:".Length).Trim();
                    continue;
                }

                if (line.StartsWith("Group ", System.StringComparison.Ordinal))
                {
                    var group = ReadGroup(ref n);
                    layout.groups.Add(group);
                    continue;
                }
            }

            // Sort groups alphabetically
            layout.groups.Sort(delegate (Group a, Group b)
            {
                return a.name.CompareTo(b.name);
            });

            return layout;

            Group ReadGroup(ref int index)
            {
                // Group LR-globals (Bundles: 16, Total Size: 128.43KB, Explicit Asset Count: 280)
                var group = new Group();
                var groupLine = lines[index];
                var groupIndend = GetIndend(groupLine);

                // Read the group name
                var groupName = groupLine;
                groupName = groupName.Substring(groupName.IndexOf("Group ") + "Group ".Length); // Remove everything before and including "Group"
                groupName = groupName.Substring(0, groupName.IndexOf(" (Bundles:")); // Remove everything after and including " (Bundles:"
                groupName = groupName.Trim();
                group.name = groupName;

                foreach (var attribute in ReadAttributes(groupLine))
                {
                    if (attribute.Key == "Total Size")
                        group.size = ParseSize(attribute.Value);
                }

                // Iterate over each group line
                var loopguard = 0;
                index++;
                for (; index < lines.Count; ++index)
                {
                loop:
                    if (++loopguard > 30000)
                    {
                        Debug.LogError($"loopguard");
                        break;
                    }

                    var line = lines[index];
                    var lineIndend = GetIndend(line);
                    if (lineIndend <= groupIndend)
                    {
                        index--;
                        return group;
                    }

                    if (line.StartsWith("\tSchemas"))
                    {
                        SkipSchemas(ref index);
                        goto loop;
                    }

                    if (line.StartsWith("\tArchive"))
                    {
                        var archive = ReadArchive(ref index);
                        group.bundles.Add(archive);
                        goto loop;
                    }
                }

                return group;
            }

            void SkipSchemas(ref int index)
            {
                var schemasLevel = GetIndend(lines[index]);

                for (index++; index < lines.Count; ++index)
                {
                    if (GetIndend(lines[index]) <= schemasLevel)
                        break;
                }
            }

            Archive ReadArchive(ref int index)
            {
                var archive = new Archive();

                // Archive lr-globals_assets_assets/configs/balancing_eea0e08713731a51c6aac94795f26fc8.bundle (Size: 10.07KB, Compression: Lz4HC, Asset Bundle Object Size: 1.76KB)
                var archiveLine = lines[index];
                var archiveIndend = GetIndend(archiveLine);

                // Read the archive name
                var archiveName = archiveLine;
                archiveName = archiveName.Substring(archiveName.IndexOf("Archive") + "Archive".Length); // Remove everything before and including "Archive"
                archiveName = archiveName.Substring(0, archiveName.IndexOf("(Size:")); // Remove everything after and including "(Size:"
                archiveName = archiveName.Trim();
                archive.name = archiveName;

                foreach (var attribute in ReadAttributes(archiveLine))
                {
                    if (attribute.Key == "Size")
                        archive.size = ParseSize(attribute.Value);

                    if (attribute.Key == "Compression")
                        archive.compression = attribute.Value;

                    if (attribute.Key == "Asset Bundle Object Size")
                        archive.assetBundleObjectSize = ParseSize(attribute.Value);
                }

                var loopguard = 0;

                for (index++; index < lines.Count - 1; ++index)
                {
                    if (++loopguard > 30000)
                    {
                        Debug.LogError($"loopguard");
                        break;
                    }

                    if (GetIndend(lines[index + 1]) <= archiveIndend)
                        break;

                    var trimmedLine = lines[index].Trim();
                    if (trimmedLine.StartsWith("Bundle Dependencies:", System.StringComparison.OrdinalIgnoreCase))
                    {
                        archive.bundleDependencies.AddRange(ReadCommaSeparatedStrings(ref index));
                        continue;
                    }

                    if (trimmedLine.StartsWith("Expanded Bundle Dependencies:", System.StringComparison.OrdinalIgnoreCase))
                    {
                        archive.expandedBundleDependencies.AddRange(ReadCommaSeparatedStrings(ref index));
                        continue;
                    }

                    if (trimmedLine.StartsWith("Explicit Assets", System.StringComparison.OrdinalIgnoreCase))
                    {
                        archive.explicitAssets.AddRange(ReadExplicitAssets(ref index));
                        continue;
                    }

                    if (trimmedLine.StartsWith("Files", System.StringComparison.OrdinalIgnoreCase))
                    {
                        //SkipBlock(ref index);
                        continue;
                    }
                }

                return archive;
            }

            List<ExplicitAsset> ReadExplicitAssets(ref int index)
            {
                //Explicit Assets
                //	Assets/configs/shake/camera_shake_empty.prefab (Total Size: 319B, Size from Objects: 319B, Size from Streamed Data: 0B, File Index: 0, Addressable Name: Assets/configs/shake/camera_shake_empty.prefab)
                //		Internal References: Packages/com.unity.cinemachine/Presets/Noise/Handheld_tele_mild.asset
                //	Assets/configs/bundles/bundle_01.prefab (Total Size: 411B, Size from Objects: 411B, Size from Streamed Data: 0B, File Index: 0, Addressable Name: Assets/configs/bundles/bundle_01.prefab)
                //		External References: Assets/configs/item/item_02.prefab, Assets/configs/item/item_01.prefab, Assets/configs/equipment/saddlecloth/saddlecloth_config_01_01_01.prefab, Assets/configs/equipment/saddle/saddle_config_01_01_01.prefab
                //	Assets/art/debug/debug_checkerboard.png (Total Size: 170.88KB, Size from Objects: 204B, Size from Streamed Data: 170.68KB, File Index: 0, Addressable Name: Assets/art/debug/debug_checkerboard.png)

                var assets = new List<ExplicitAsset>();
                var explicitAssetsIndend = GetIndend(lines[index]);
                index++; // skip "Explicit Assets" line

                for (; index < lines.Count-1; ++index)
                {
                    var line = lines[index];
                    var lineIndend = GetIndend(line);
                    if (lineIndend <= explicitAssetsIndend)
                    {
                        index--;
                        break;
                    }

                    var asset = ReadExplicitAsset(ref index);
                    if (asset != null)
                        assets.Add(asset);
                }

                return assets;
            }

            Dictionary<string, string> ReadAttributes(string line)
            {
                // Read everything between the brackets
                // Assets/art/debug/debug_checkerboard.png (Total Size: 170.88KB, Size from Objects: 204B, Size from Streamed Data: 170.68KB, File Index: 0, Addressable Name: Assets/art/debug/debug_checkerboard.png)

                var last = line.LastIndexOf(')');
                var first = last;
                var result = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

                var count = 1;
                for (var n = last - 1; n >= 0; --n)
                {
                    if (line[n] == ')')
                        count++;
                    if (line[n] == '(')
                        count--;
                    if (count == 0)
                    {
                        first = n+1;
                        break;
                    }
                }

                if (first != last)
                {
                    // Now line contains:
                    // Total Size: 170.88KB, Size from Objects: 204B, Size from Streamed Data: 170.68KB, File Index: 0, Addressable Name: Assets/art/debug/debug_checkerboard.png
                    line = line.Substring(first, last - first);

                    // Now split it to:
                    // Total Size: 170.88KB
                    // Size from Objects: 204B
                    // Size from Streamed Data: 170.68KB
                    // File Index: 0
                    // Addressable Name: Assets/art/debug/debug_checkerboard.png
                    foreach (var entry in line.Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries))
                    {
                        // entry contains:
                        // Total Size: 170.88KB
                        if (string.IsNullOrEmpty(entry.Trim()))
                            continue;

                        // split entry to:
                        // Total Size
                        // 170.88KB
                        var pair = entry.Split(new[] { ':' }, System.StringSplitOptions.RemoveEmptyEntries);
                        if (pair.Length == 2)
                            result[pair[0].Trim()] = pair[1].Trim();
                        else if (pair.Length >= 1)
                            result[pair[0].Trim()] = pair[0].Trim();
                    }
                }

                return result;
            }

            ExplicitAsset ReadExplicitAsset(ref int index)
            {
                var result = new ExplicitAsset();
                var assetLine = lines[index++];
                var assetIndend = GetIndend(assetLine);

                // Read the asset name
                var assetName = assetLine;
                assetName = assetName.Substring(0, assetName.IndexOf(" (Total Size:")); // Remove everything after and including " (Total Size:"
                assetName = assetName.Trim();
                result.name = assetName;

                foreach (var attribute in ReadAttributes(assetLine))
                {
                    if (attribute.Key == "Total Size")
                        result.size = ParseSize(attribute.Value);

                    if (attribute.Key == "Addressable Name")
                        result.address = attribute.Value;
                }

                for (; index < lines.Count - 1; ++index)
                {
                    var line = lines[index];
                    var lineIndend = GetIndend(line);
                    if (lineIndend <= assetIndend)
                    {
                        index--;
                        return result;
                    }

                    var trimmedLine = line.Trim();
                    if (trimmedLine.StartsWith("External References:", System.StringComparison.OrdinalIgnoreCase))
                        result.externalReferences.AddRange(ReadCommaSeparatedStrings(ref index));

                    if (trimmedLine.StartsWith("Internal References:", System.StringComparison.OrdinalIgnoreCase))
                        result.internalReferences.AddRange(ReadCommaSeparatedStrings(ref index));
                }

                index--;
                return result;
            }

            List<string> ReadCommaSeparatedStrings(ref int index)
            {
                // Expanded Bundle Dependencies: lr-globals_assets_assets/configs/horse_0aae3772e478c44f7a52a3c70dd19634.bundle, lr-globals_assets_assets/configs/horsehair_0ce9fe7b12df74c0252ac5b88850c01a.bundle
                var bundlesLine = lines[index];
                if (bundlesLine.IndexOf(":") == -1)
                    return new List<string>();

                bundlesLine = bundlesLine.Substring(bundlesLine.IndexOf(":") + ":".Length); // Remove everything before and including "Bundle Dependencies:"
                bundlesLine = bundlesLine.Trim();

                var bundles = new List<string>();
                foreach (var b in bundlesLine.Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries))
                    bundles.Add(b.Trim());

                bundles.Sort();
                return bundles;
            }

            long ParseSize(string size)
            {
                if (size.EndsWith("GB", System.StringComparison.OrdinalIgnoreCase))
                {
                    var s = size.Substring(0, size.Length - 2);
                    return (long)(float.Parse(s) * 1024 * 1024 * 1024);
                }

                if (size.EndsWith("MB", System.StringComparison.OrdinalIgnoreCase))
                {
                    var s = size.Substring(0, size.Length - 2);
                    return (long)(float.Parse(s) * 1024 * 1024);
                }

                if (size.EndsWith("KB", System.StringComparison.OrdinalIgnoreCase))
                {
                    var s = size.Substring(0, size.Length - 2);
                    return (long)(float.Parse(s) * 1024);
                }

                if (size.EndsWith("B", System.StringComparison.OrdinalIgnoreCase))
                {
                    var s = size.Substring(0, size.Length - 1);
                    return long.Parse(s);
                }

                return -1;
            }

            int GetIndend(string s)
            {
                int count = 0;
                for (var n = 0; n < s.Length; ++n)
                {
                    if (s[n] == '\t')
                        count++;
                    else
                        break;
                }
                return count;
            }
        }
    }

}
