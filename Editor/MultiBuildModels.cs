using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Coah.MultiBuild
{
    [Serializable]
    internal sealed class MultiBuildTargetEntry
    {
        [SerializeField] private string id;
        [SerializeField] private string label;
        [SerializeField] private BuildTarget target;
        [SerializeField] private BuildTargetGroup targetGroup;
        [SerializeField] private StandaloneBuildSubtarget standaloneSubtarget;
        [SerializeField] private bool enabled;
        [SerializeField] private bool installed;
        [SerializeField] private string folderName;

        internal string Id => id;
        internal string Label
        {
            get => label;
            set => label = value;
        }
        internal BuildTarget Target
        {
            get => target;
            set => target = value;
        }
        internal BuildTargetGroup TargetGroup
        {
            get => targetGroup;
            set => targetGroup = value;
        }
        internal StandaloneBuildSubtarget StandaloneSubtarget
        {
            get => standaloneSubtarget;
            set => standaloneSubtarget = value;
        }
        internal bool Enabled
        {
            get => enabled;
            set => enabled = value;
        }
        internal bool Installed
        {
            get => installed;
            set => installed = value;
        }
        internal string FolderName
        {
            get => folderName;
            set => folderName = value;
        }
        internal bool IsStandalone => targetGroup == BuildTargetGroup.Standalone;
        internal bool IsServer => standaloneSubtarget == StandaloneBuildSubtarget.Server;

        internal MultiBuildTargetEntry Clone()
        {
            return new MultiBuildTargetEntry
            {
                id = id,
                label = label,
                target = target,
                targetGroup = targetGroup,
                standaloneSubtarget = standaloneSubtarget,
                enabled = enabled,
                installed = installed,
                folderName = folderName
            };
        }

        internal static MultiBuildTargetEntry Create(
            string newId,
            string newLabel,
            BuildTarget newTarget,
            BuildTargetGroup newTargetGroup,
            StandaloneBuildSubtarget newStandaloneSubtarget,
            bool newEnabled,
            bool newInstalled,
            string newFolderName)
        {
            return new MultiBuildTargetEntry
            {
                id = newId,
                label = newLabel,
                target = newTarget,
                targetGroup = newTargetGroup,
                standaloneSubtarget = newStandaloneSubtarget,
                enabled = newEnabled,
                installed = newInstalled,
                folderName = newFolderName
            };
        }
    }

    [Serializable]
    internal sealed class MultiBuildQueueItem
    {
        public string id;
        public string label;
        public BuildTarget target;
        public BuildTargetGroup targetGroup;
        public StandaloneBuildSubtarget standaloneSubtarget;
        public string folderName;
    }

    [Serializable]
    internal sealed class MultiBuildResultRecord
    {
        public string label;
        public string location;
        public bool succeeded;
        public string result;
    }

    [Serializable]
    internal sealed class MultiBuildQueueState
    {
        public string outputRoot;
        public string productName;
        public bool developmentBuild;
        public bool cleanBuild;
        public bool stopOnFailure;
        public int currentIndex;
        public List<MultiBuildQueueItem> items = new List<MultiBuildQueueItem>();
        public List<MultiBuildResultRecord> results = new List<MultiBuildResultRecord>();
    }

    internal static class MultiBuildTargetCatalog
    {
        internal static List<MultiBuildTargetEntry> CreateDefaults()
        {
            return new List<MultiBuildTargetEntry>
            {
                Create("windows64", "windows 64-bit", BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, StandaloneBuildSubtarget.Player, "windows"),
                Create("macos", "macos", BuildTarget.StandaloneOSX, BuildTargetGroup.Standalone, StandaloneBuildSubtarget.Player, "macos"),
                Create("linux64", "linux 64-bit", BuildTarget.StandaloneLinux64, BuildTargetGroup.Standalone, StandaloneBuildSubtarget.Player, "linux"),
                Create("linux-server", "linux server", BuildTarget.StandaloneLinux64, BuildTargetGroup.Standalone, StandaloneBuildSubtarget.Server, "linux-server"),
                Create("webgl", "webgl", BuildTarget.WebGL, BuildTargetGroup.WebGL, StandaloneBuildSubtarget.Player, "webgl"),
                Create("android", "android", BuildTarget.Android, BuildTargetGroup.Android, StandaloneBuildSubtarget.Player, "android"),
                Create("ios", "ios", BuildTarget.iOS, BuildTargetGroup.iOS, StandaloneBuildSubtarget.Player, "ios")
            };
        }

        private static MultiBuildTargetEntry Create(
            string id,
            string label,
            BuildTarget target,
            BuildTargetGroup targetGroup,
            StandaloneBuildSubtarget standaloneSubtarget,
            string folderName)
        {
            return MultiBuildTargetEntry.Create(
                id,
                label,
                target,
                targetGroup,
                standaloneSubtarget,
                false,
                BuildPipeline.IsBuildTargetSupported(targetGroup, target),
                folderName);
        }
    }

    internal static class MultiBuildPaths
    {
        internal static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        internal static string ResolveOutputRoot(string configuredPath)
        {
            var value = string.IsNullOrWhiteSpace(configuredPath) ? "Builds" : configuredPath.Trim();
            if (Path.IsPathRooted(value))
            {
                return Path.GetFullPath(value);
            }

            return Path.GetFullPath(Path.Combine(ProjectRoot, value));
        }

        internal static string GetLocation(MultiBuildQueueItem item, string outputRoot, string configuredProductName)
        {
            var productName = SanitizeFileName(string.IsNullOrWhiteSpace(configuredProductName) ? PlayerSettings.productName : configuredProductName);
            var folderName = SanitizeFileName(string.IsNullOrWhiteSpace(item.folderName) ? item.id : item.folderName);
            var targetRoot = Path.Combine(outputRoot, folderName);

            switch (item.target)
            {
                case BuildTarget.StandaloneWindows64:
                    return Path.Combine(targetRoot, productName + ".exe");
                case BuildTarget.StandaloneOSX:
                    return Path.Combine(targetRoot, productName + ".app");
                case BuildTarget.StandaloneLinux64:
                    return Path.Combine(targetRoot, item.standaloneSubtarget == StandaloneBuildSubtarget.Server ? productName + "-server" : productName);
                case BuildTarget.Android:
                    return Path.Combine(targetRoot, productName + ".apk");
                case BuildTarget.WebGL:
                case BuildTarget.iOS:
                    return targetRoot;
                default:
                    return targetRoot;
            }
        }

        internal static bool OutputIsDirectory(BuildTarget target)
        {
            return target == BuildTarget.WebGL || target == BuildTarget.iOS || target == BuildTarget.StandaloneOSX;
        }

        internal static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "build";
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var chars = value.Trim().ToCharArray();

            for (var i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalidChars, chars[i]) >= 0)
                {
                    chars[i] = '-';
                }
            }

            return new string(chars);
        }
    }
}
