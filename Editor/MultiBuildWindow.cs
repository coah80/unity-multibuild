using UnityEditor;
using UnityEngine;

namespace Coah.MultiBuild
{
    public sealed class MultiBuildWindow : EditorWindow
    {
        private Vector2 scrollPosition;

        [MenuItem("Tools/Multi Build")]
        private static void Open()
        {
            var window = GetWindow<MultiBuildWindow>();
            window.titleContent = new GUIContent("Multi Build");
            window.minSize = new Vector2(520f, 420f);
            window.Show();
        }

        [MenuItem("Window/Multi Build")]
        private static void OpenFromWindow()
        {
            Open();
        }

        private void OnEnable()
        {
            MultiBuildSettings.instance.SyncTargets();
            MultiBuildSettings.instance.SaveNow();
        }

        private void OnGUI()
        {
            var settings = MultiBuildSettings.instance;

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("multi build", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("active target", EditorUserBuildSettings.activeBuildTarget.ToString());

            if (MultiBuildRunner.IsRunning)
            {
                EditorGUILayout.HelpBox(MultiBuildRunner.StatusText, MessageType.Info);
            }
            else if (!string.IsNullOrEmpty(MultiBuildRunner.LastSummary))
            {
                EditorGUILayout.HelpBox(MultiBuildRunner.LastSummary, MessageType.None);
            }

            DrawPaths(settings);
            EditorGUILayout.Space();
            DrawOptions(settings);
            EditorGUILayout.Space();
            DrawToolbar(settings);
            EditorGUILayout.Space();
            DrawTargets(settings);
            EditorGUILayout.Space();
            DrawActions(settings);

            if (EditorGUI.EndChangeCheck())
            {
                settings.SaveNow();
            }
        }

        private void DrawPaths(MultiBuildSettings settings)
        {
            EditorGUILayout.LabelField("output", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                settings.OutputRoot = EditorGUILayout.TextField("output root", settings.OutputRoot);
                using (new EditorGUI.DisabledScope(MultiBuildRunner.IsRunning))
                {
                    if (GUILayout.Button("browse", GUILayout.Width(80f)))
                    {
                        var selectedPath = EditorUtility.OpenFolderPanel("Multi Build Output Root", MultiBuildPaths.ResolveOutputRoot(settings.OutputRoot), string.Empty);
                        if (!string.IsNullOrEmpty(selectedPath))
                        {
                            settings.OutputRoot = selectedPath;
                            GUI.FocusControl(null);
                        }
                    }
                }
            }

            EditorGUILayout.LabelField("resolved path", MultiBuildPaths.ResolveOutputRoot(settings.OutputRoot));
            settings.ProductNameOverride = EditorGUILayout.TextField("product name", settings.ProductNameOverride);
            EditorGUILayout.LabelField("effective name", settings.EffectiveProductName);
        }

        private void DrawOptions(MultiBuildSettings settings)
        {
            EditorGUILayout.LabelField("options", EditorStyles.boldLabel);
            settings.DevelopmentBuild = EditorGUILayout.ToggleLeft("development build", settings.DevelopmentBuild);
            settings.CleanBuild = EditorGUILayout.ToggleLeft("clean build output", settings.CleanBuild);
            settings.StopOnFailure = EditorGUILayout.ToggleLeft("stop on first failure", settings.StopOnFailure);
        }

        private void DrawToolbar(MultiBuildSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(MultiBuildRunner.IsRunning))
                {
                    if (GUILayout.Button("enable all installed"))
                    {
                        for (var i = 0; i < settings.Targets.Count; i++)
                        {
                            if (settings.Targets[i].Installed)
                            {
                                settings.Targets[i].Enabled = true;
                            }
                        }
                    }

                    if (GUILayout.Button("disable all"))
                    {
                        for (var i = 0; i < settings.Targets.Count; i++)
                        {
                            settings.Targets[i].Enabled = false;
                        }
                    }

                    if (GUILayout.Button("refresh targets"))
                    {
                        settings.SyncTargets();
                    }
                }
            }
        }

        private void DrawTargets(MultiBuildSettings settings)
        {
            EditorGUILayout.LabelField("targets", EditorStyles.boldLabel);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            for (var i = 0; i < settings.Targets.Count; i++)
            {
                var entry = settings.Targets[i];
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    using (new EditorGUI.DisabledScope(MultiBuildRunner.IsRunning || !entry.Installed))
                    {
                        entry.Enabled = EditorGUILayout.ToggleLeft(entry.Label, entry.Enabled);
                        entry.FolderName = EditorGUILayout.TextField("folder", entry.FolderName);
                    }

                    if (!entry.Installed)
                    {
                        EditorGUILayout.LabelField("status", "not installed in this editor");
                    }

                    var previewItem = new MultiBuildQueueItem
                    {
                        id = entry.Id,
                        label = entry.Label,
                        target = entry.Target,
                        targetGroup = entry.TargetGroup,
                        standaloneSubtarget = entry.StandaloneSubtarget,
                        folderName = entry.FolderName
                    };
                    EditorGUILayout.LabelField("output", MultiBuildPaths.GetLocation(previewItem, MultiBuildPaths.ResolveOutputRoot(settings.OutputRoot), settings.EffectiveProductName));
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawActions(MultiBuildSettings settings)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(MultiBuildRunner.IsRunning))
                {
                    if (GUILayout.Button("build selected", GUILayout.Height(36f)))
                    {
                        MultiBuildRunner.Start(settings);
                    }
                }

                using (new EditorGUI.DisabledScope(!MultiBuildRunner.IsRunning))
                {
                    if (GUILayout.Button("cancel queue", GUILayout.Height(36f)))
                    {
                        MultiBuildRunner.Cancel();
                    }
                }
            }
        }
    }
}
