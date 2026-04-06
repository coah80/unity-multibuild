using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Coah.MultiBuild
{
    [FilePath("ProjectSettings/MultiBuildSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class MultiBuildSettings : ScriptableSingleton<MultiBuildSettings>
    {
        [SerializeField] private string outputRoot = "Builds";
        [SerializeField] private string productNameOverride = string.Empty;
        [SerializeField] private bool developmentBuild;
        [SerializeField] private bool cleanBuild;
        [SerializeField] private bool stopOnFailure;
        [SerializeField] private List<MultiBuildTargetEntry> targets = new List<MultiBuildTargetEntry>();

        internal string OutputRoot
        {
            get => outputRoot;
            set => outputRoot = value;
        }

        internal string ProductNameOverride
        {
            get => productNameOverride;
            set => productNameOverride = value;
        }

        internal bool DevelopmentBuild
        {
            get => developmentBuild;
            set => developmentBuild = value;
        }

        internal bool CleanBuild
        {
            get => cleanBuild;
            set => cleanBuild = value;
        }

        internal bool StopOnFailure
        {
            get => stopOnFailure;
            set => stopOnFailure = value;
        }

        internal List<MultiBuildTargetEntry> Targets => targets;

        internal string EffectiveProductName => string.IsNullOrWhiteSpace(productNameOverride) ? PlayerSettings.productName : productNameOverride.Trim();

        internal void SyncTargets()
        {
            var defaults = MultiBuildTargetCatalog.CreateDefaults();
            var currentTargets = new Dictionary<string, MultiBuildTargetEntry>();

            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (target != null && !string.IsNullOrWhiteSpace(target.Id))
                {
                    currentTargets[target.Id] = target;
                }
            }

            var syncedTargets = new List<MultiBuildTargetEntry>();

            for (var i = 0; i < defaults.Count; i++)
            {
                var defaultTarget = defaults[i];
                if (currentTargets.TryGetValue(defaultTarget.Id, out var existingTarget))
                {
                    existingTarget.Label = defaultTarget.Label;
                    existingTarget.Target = defaultTarget.Target;
                    existingTarget.TargetGroup = defaultTarget.TargetGroup;
                    existingTarget.StandaloneSubtarget = defaultTarget.StandaloneSubtarget;
                    existingTarget.Installed = defaultTarget.Installed;
                    if (string.IsNullOrWhiteSpace(existingTarget.FolderName))
                    {
                        existingTarget.FolderName = defaultTarget.FolderName;
                    }

                    syncedTargets.Add(existingTarget);
                    continue;
                }

                syncedTargets.Add(defaultTarget.Clone());
            }

            targets = syncedTargets;
        }

        internal void SaveNow()
        {
            Save(true);
        }
    }
}
