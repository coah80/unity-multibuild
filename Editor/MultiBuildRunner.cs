using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Coah.MultiBuild
{
    [InitializeOnLoad]
    internal static class MultiBuildRunner
    {
        private const string StateKey = "Coah.MultiBuild.State";
        private const string LastSummaryKey = "Coah.MultiBuild.LastSummary";
        private static bool scheduled;

        static MultiBuildRunner()
        {
            ScheduleResume();
        }

        internal static bool IsRunning => !string.IsNullOrEmpty(SessionState.GetString(StateKey, string.Empty));

        internal static string LastSummary => SessionState.GetString(LastSummaryKey, string.Empty);

        internal static string StatusText
        {
            get
            {
                var state = LoadState();
                if (state == null)
                {
                    return string.Empty;
                }

                if (state.items.Count == 0 || state.currentIndex >= state.items.Count)
                {
                    return "finishing";
                }

                return "building " + (state.currentIndex + 1) + "/" + state.items.Count + ": " + state.items[state.currentIndex].label;
            }
        }

        internal static void Start(MultiBuildSettings settings)
        {
            settings.SyncTargets();

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var enabledScenes = GetEnabledScenes();
            if (enabledScenes.Length == 0)
            {
                EditorUtility.DisplayDialog("Multi Build", "No enabled scenes were found in Build Settings.", "OK");
                return;
            }

            var state = new MultiBuildQueueState
            {
                outputRoot = MultiBuildPaths.ResolveOutputRoot(settings.OutputRoot),
                productName = settings.EffectiveProductName,
                developmentBuild = settings.DevelopmentBuild,
                cleanBuild = settings.CleanBuild,
                stopOnFailure = settings.StopOnFailure
            };

            for (var i = 0; i < settings.Targets.Count; i++)
            {
                var target = settings.Targets[i];
                if (target == null || !target.Installed || !target.Enabled)
                {
                    continue;
                }

                state.items.Add(new MultiBuildQueueItem
                {
                    id = target.Id,
                    label = target.Label,
                    target = target.Target,
                    targetGroup = target.TargetGroup,
                    standaloneSubtarget = target.StandaloneSubtarget,
                    folderName = target.FolderName
                });
            }

            if (state.items.Count == 0)
            {
                EditorUtility.DisplayDialog("Multi Build", "No enabled installed targets were selected.", "OK");
                return;
            }

            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(state.outputRoot);
            SaveState(state);
            SessionState.SetString(LastSummaryKey, string.Empty);
            ScheduleResume();
        }

        internal static void Cancel()
        {
            SessionState.EraseString(StateKey);
            SessionState.SetString(LastSummaryKey, "build queue cancelled");
        }

        private static void ScheduleResume()
        {
            if (scheduled)
            {
                return;
            }

            scheduled = true;
            EditorApplication.delayCall += ResumeIfNeeded;
        }

        private static void ResumeIfNeeded()
        {
            scheduled = false;

            var state = LoadState();
            if (state == null)
            {
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer)
            {
                ScheduleResume();
                return;
            }

            if (state.currentIndex >= state.items.Count)
            {
                Finish(state);
                return;
            }

            var item = state.items[state.currentIndex];

            if (EditorUserBuildSettings.activeBuildTarget != item.target)
            {
                if (!EditorUserBuildSettings.SwitchActiveBuildTargetAsync(item.targetGroup, item.target))
                {
                    RecordFailure(state, item, "could not switch to " + item.label);
                    return;
                }

                ScheduleResume();
                return;
            }

            if (item.targetGroup == BuildTargetGroup.Standalone && EditorUserBuildSettings.standaloneBuildSubtarget != item.standaloneSubtarget)
            {
                EditorUserBuildSettings.standaloneBuildSubtarget = item.standaloneSubtarget;
                ScheduleResume();
                return;
            }

            BuildCurrent(state, item);
        }

        private static void BuildCurrent(MultiBuildQueueState state, MultiBuildQueueItem item)
        {
            var location = MultiBuildPaths.GetLocation(item, state.outputRoot, state.productName);
            PrepareLocation(location, item.target, state.cleanBuild);
            var report = BuildPlayer(state, item, location);
            var succeeded = report.summary.result == BuildResult.Succeeded;

            state.results.Add(new MultiBuildResultRecord
            {
                label = item.label,
                location = location,
                succeeded = succeeded,
                result = report.summary.result.ToString()
            });

            if (!succeeded && state.stopOnFailure)
            {
                SaveState(state);
                Finish(state);
                return;
            }

            state.currentIndex++;
            SaveState(state);
            ScheduleResume();
        }

        private static BuildReport BuildPlayer(MultiBuildQueueState state, MultiBuildQueueItem item, string location)
        {
            var options = BuildOptions.None;

            if (state.developmentBuild)
            {
                options |= BuildOptions.Development;
            }

            if (state.cleanBuild)
            {
                options |= BuildOptions.CleanBuildCache;
            }

            var buildProfile = GetActiveBuildProfile(item);
            if (buildProfile != null)
            {
                var profileOptions = new BuildPlayerWithProfileOptions
                {
                    buildProfile = buildProfile,
                    locationPathName = location,
                    options = options
                };

                return BuildPipeline.BuildPlayer(profileOptions);
            }

            var playerOptions = new BuildPlayerOptions
            {
                scenes = GetEnabledScenes(),
                locationPathName = location,
                target = item.target,
                targetGroup = item.targetGroup,
                options = options
            };

            if (item.targetGroup == BuildTargetGroup.Standalone)
            {
                playerOptions.subtarget = (int)item.standaloneSubtarget;
            }

            return BuildPipeline.BuildPlayer(playerOptions);
        }

        private static BuildProfile GetActiveBuildProfile(MultiBuildQueueItem item)
        {
            var activeBuildProfile = BuildProfile.GetActiveBuildProfile();
            if (activeBuildProfile == null)
            {
                return null;
            }

            if (activeBuildProfile.buildTarget != item.target)
            {
                return null;
            }

            if (activeBuildProfile.subtarget != item.standaloneSubtarget)
            {
                return null;
            }

            if (!activeBuildProfile.CanBuildLocally())
            {
                return null;
            }

            return activeBuildProfile;
        }

        private static void PrepareLocation(string location, BuildTarget target, bool cleanBuild)
        {
            if (cleanBuild)
            {
                if (Directory.Exists(location))
                {
                    Directory.Delete(location, true);
                }
                else if (File.Exists(location))
                {
                    File.Delete(location);
                }
            }

            if (MultiBuildPaths.OutputIsDirectory(target))
            {
                Directory.CreateDirectory(location);
                return;
            }

            var parent = Path.GetDirectoryName(location);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }
        }

        private static void RecordFailure(MultiBuildQueueState state, MultiBuildQueueItem item, string result)
        {
            state.results.Add(new MultiBuildResultRecord
            {
                label = item.label,
                location = string.Empty,
                succeeded = false,
                result = result
            });

            SaveState(state);
            Finish(state);
        }

        private static void Finish(MultiBuildQueueState state)
        {
            var builder = new StringBuilder();
            var succeededCount = 0;

            for (var i = 0; i < state.results.Count; i++)
            {
                if (state.results[i].succeeded)
                {
                    succeededCount++;
                }
            }

            builder.Append("completed ");
            builder.Append(succeededCount);
            builder.Append("/");
            builder.Append(state.items.Count);
            builder.Append(" builds");

            for (var i = 0; i < state.results.Count; i++)
            {
                builder.AppendLine();
                builder.Append(state.results[i].label);
                builder.Append(": ");
                builder.Append(state.results[i].result);
                if (!string.IsNullOrEmpty(state.results[i].location))
                {
                    builder.Append(" -> ");
                    builder.Append(state.results[i].location);
                }
            }

            var summary = builder.ToString();
            SessionState.SetString(LastSummaryKey, summary);
            SessionState.EraseString(StateKey);
            Debug.Log(summary);
            EditorUtility.DisplayDialog("Multi Build", summary, "OK");
        }

        private static MultiBuildQueueState LoadState()
        {
            var json = SessionState.GetString(StateKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            return JsonUtility.FromJson<MultiBuildQueueState>(json);
        }

        private static void SaveState(MultiBuildQueueState state)
        {
            SessionState.SetString(StateKey, JsonUtility.ToJson(state));
        }

        private static string[] GetEnabledScenes()
        {
            var scenes = EditorBuildSettings.scenes;
            var count = 0;

            for (var i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].enabled)
                {
                    count++;
                }
            }

            var paths = new string[count];
            var index = 0;

            for (var i = 0; i < scenes.Length; i++)
            {
                if (!scenes[i].enabled)
                {
                    continue;
                }

                paths[index] = scenes[i].path;
                index++;
            }

            return paths;
        }
    }
}
