using System;
using System.Collections;
using System.IO;
using System.Reflection;
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
        private static readonly MethodInfo GetAllBuildProfilesMethod = GetBuildProfileModuleUtilMethod("GetAllBuildProfiles");
        private static readonly PropertyInfo BuildProfileBuildTargetProperty = typeof(BuildProfile).GetProperty("buildTarget", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo BuildProfileSubtargetProperty = typeof(BuildProfile).GetProperty("subtarget", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo BuildProfileCanBuildLocallyMethod = typeof(BuildProfile).GetMethod("CanBuildLocally", BindingFlags.Instance | BindingFlags.NonPublic);
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

            if (EnsureBuildProfile(item))
            {
                ScheduleResume();
                return;
            }

            BuildCurrent(state, item);
        }

        private static void BuildCurrent(MultiBuildQueueState state, MultiBuildQueueItem item)
        {
            var location = MultiBuildPaths.GetLocation(item, state.outputRoot, state.productName);
            PrepareLocation(location, item.target, state.cleanBuild);
            BuildReport report;

            try
            {
                report = BuildPlayer(state, item, location);
            }
            catch (Exception exception)
            {
                state.results.Add(new MultiBuildResultRecord
                {
                    label = item.label,
                    location = location,
                    succeeded = false,
                    result = exception.Message
                });

                if (!state.stopOnFailure)
                {
                    state.currentIndex++;
                    SaveState(state);
                    ScheduleResume();
                    return;
                }

                SaveState(state);
                Finish(state);
                return;
            }

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
            if (MatchesBuildProfile(activeBuildProfile, item))
            {
                return activeBuildProfile;
            }

            var buildProfiles = GetAllBuildProfiles();
            if (buildProfiles == null)
            {
                return null;
            }

            foreach (var buildProfile in buildProfiles)
            {
                if (buildProfile is BuildProfile matchingBuildProfile && MatchesBuildProfile(matchingBuildProfile, item))
                {
                    return matchingBuildProfile;
                }
            }

            return null;
        }

        private static bool EnsureBuildProfile(MultiBuildQueueItem item)
        {
            var buildProfile = GetActiveBuildProfile(item);
            if (buildProfile == null)
            {
                return false;
            }

            if (ReferenceEquals(BuildProfile.GetActiveBuildProfile(), buildProfile))
            {
                return false;
            }

            BuildProfile.SetActiveBuildProfile(buildProfile);
            return true;
        }

        private static IList GetAllBuildProfiles()
        {
            if (GetAllBuildProfilesMethod == null)
            {
                return null;
            }

            return GetAllBuildProfilesMethod.Invoke(null, null) as IList;
        }

        private static MethodInfo GetBuildProfileModuleUtilMethod(string name)
        {
            var type = Type.GetType("UnityEditor.Build.Profile.BuildProfileModuleUtil, UnityEditor");
            if (type == null)
            {
                return null;
            }

            return type.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private static bool MatchesBuildProfile(BuildProfile buildProfile, MultiBuildQueueItem item)
        {
            if (buildProfile == null || BuildProfileBuildTargetProperty == null || BuildProfileSubtargetProperty == null || BuildProfileCanBuildLocallyMethod == null)
            {
                return false;
            }

            object canBuildLocallyValue;

            try
            {
                canBuildLocallyValue = BuildProfileCanBuildLocallyMethod.Invoke(buildProfile, null);
            }
            catch
            {
                return false;
            }

            if (canBuildLocallyValue is not bool canBuildLocally || !canBuildLocally)
            {
                return false;
            }

            object buildTargetValue;
            object subtargetValue;

            try
            {
                buildTargetValue = BuildProfileBuildTargetProperty.GetValue(buildProfile);
                subtargetValue = BuildProfileSubtargetProperty.GetValue(buildProfile);
            }
            catch
            {
                return false;
            }

            if (buildTargetValue is not BuildTarget buildTarget || buildTarget != item.target)
            {
                return false;
            }

            if (subtargetValue is not StandaloneBuildSubtarget subtarget)
            {
                return false;
            }

            return subtarget == item.standaloneSubtarget;
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
