using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;
using System.Text.RegularExpressions;

namespace SceneAssetExtractor
{
    [System.Serializable]
    public class AssetInfo
    {
        public UnityEngine.Object Object;
        public string Path;
        public System.Type Type;
        public long FileSize;
    }

    public class SceneAssetExtractor : EditorWindow
    {
        private Vector2 mainScrollPosition;
        private Vector2 assetListScrollPosition;
        private List<AssetInfo> foundAssets = new List<AssetInfo>();
        private bool includeSubAssets = true;
        private bool includeBuiltInAssets = false;
        private string searchFilter = "";
        private Dictionary<string, bool> assetTypeFilters = new Dictionary<string, bool>();
        private string exportPath = "Assets/SceneAssetsExport";
        
        // 导出选项
        private bool exportByType = true;
        private bool preserveFolderStructure = false;
        private string customExportPath = "";
        private ExportFormat exportFormat = ExportFormat.PreserveOriginal;
        
        // 按特定类型分类的选项
        private bool exportBySpecificType = true;
        private SpecificTypeSettings specificTypeSettings = new SpecificTypeSettings();
        
        // UI 状态
        private bool showSettingsPanel = true;
        private bool showExportOptions = true;
        private bool showTypeSettings = false;
        private bool showAssetList = true;

        // HLSL 处理相关
        private HashSet<string> processedHLSLFiles = new HashSet<string>();
        private Dictionary<string, List<HLSLReference>> shaderToHLSLMap = new Dictionary<string, List<HLSLReference>>();
        private Dictionary<string, List<string>> hlslUsageMap = new Dictionary<string, List<string>>();

        [System.Serializable]
        public class SpecificTypeSettings
        {
            public bool texture2D = true;
            public bool material = true;
            public bool mesh = true;
            public bool gameObject = true;
            public bool audioClip = true;
            public bool animationClip = true;
            public bool animatorController = true;
            public bool shader = true;
            public bool script = true;
            public bool font = true;
            public bool textAsset = true;
            public bool prefab = true;
            public bool other = true;
            
            // 新增地形设置
            public bool terrain = true;
            public bool terrainData = true;
            public string terrainFolder = "Terrains";
            public string terrainDataFolder = "TerrainData";
            
            // HLSL相关设置
            public bool hlslFile = true;
            public bool extractShaderIncludes = true;
            public bool preserveHLSLFolderStructure = true;
            public bool createHLSLSymlinks = false;
            public bool excludeURPAssets = true;
            
            public string texture2DFolder = "Textures";
            public string materialFolder = "Materials";
            public string meshFolder = "Meshes";
            public string gameObjectFolder = "GameObjects";
            public string audioClipFolder = "Audio";
            public string animationClipFolder = "Animations";
            public string animatorControllerFolder = "Animators";
            public string shaderFolder = "Shaders";
            public string scriptFolder = "Scripts";
            public string fontFolder = "Fonts";
            public string textAssetFolder = "TextAssets";
            public string prefabFolder = "Prefabs";
            public string otherFolder = "Other";
        }

        [System.Serializable]
        public class HLSLReference
        {
            public string hlslPath;
            public string includePath;
            public string shaderPath;
        }

        private enum ExportFormat
        {
            PreserveOriginal,
            UnityPackage
        }

        [MenuItem("Tools/场景资产提取器")]
        public static void ShowWindow()
        {
            var window = GetWindow<SceneAssetExtractor>("场景资产提取器");
            window.minSize = new Vector2(650, 550);
        }

        private void OnEnable()
        {
            InitializeTypeFilters();
            LoadSettings();
            RefreshAssets();
        }

        private void OnDisable()
        {
            SaveSettings();
        }

        private void InitializeTypeFilters()
        {
            string[] defaultTypes = {
                "Texture2D", "Material", "Mesh", "GameObject", "AudioClip",
                "AnimationClip", "AnimatorController", "Shader", "Script",
                "Font", "TextAsset", "Prefab", "HLSL", "ComputeShader",
                "Terrain", "TerrainData" // 新增地形类型
            };

            foreach (string type in defaultTypes)
            {
                if (!assetTypeFilters.ContainsKey(type))
                    assetTypeFilters[type] = true;
            }
        }

        private void OnGUI()
        {
            mainScrollPosition = EditorGUILayout.BeginScrollView(mainScrollPosition);
            {
                DrawHeader();
                DrawSettingsPanel();
                DrawExportOptions();
                DrawSpecificTypeSettings();
                DrawAssetList();
                DrawActionButtons();
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            GUILayout.BeginVertical("Box");
            {
                GUILayout.BeginHorizontal();
                {
                    GUILayout.Label("场景资产提取器", EditorStyles.largeLabel, GUILayout.ExpandWidth(true));
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"场景: {SceneManager.GetActiveScene().name}", EditorStyles.boldLabel);
                }
                GUILayout.EndHorizontal();
                
                EditorGUILayout.Space();
                
                GUILayout.BeginHorizontal();
                {
                    if (GUILayout.Button("刷新资产列表", GUILayout.Height(30)))
                    {
                        RefreshAssets();
                    }
                    
                    if (GUILayout.Button("快速导出", GUILayout.Height(30)))
                    {
                        QuickExport();
                    }
                    
                    GUI.enabled = foundAssets.Count > 0;
                    if (GUILayout.Button("生成报告", GUILayout.Height(30)))
                    {
                        GenerateReport();
                    }
                    
                    if (GUILayout.Button("调试预制体", GUILayout.Height(30)))
                    {
                        DebugPrefabRecognition();
                    }
                    
                    // 新增：脚本调试按钮
                    if (GUILayout.Button("调试脚本", GUILayout.Height(30)))
                    {
                        DebugScriptRecognition();
                    }
                    GUI.enabled = true;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
            
            EditorGUILayout.Space();
        }

        private void DrawSettingsPanel()
        {
            GUILayout.BeginVertical("Box");
            {
                GUILayout.BeginHorizontal();
                {
                    showSettingsPanel = EditorGUILayout.Foldout(showSettingsPanel, "提取设置", true, EditorStyles.foldoutHeader);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label($"{foundAssets.Count} 个资产", EditorStyles.miniLabel);
                }
                GUILayout.EndHorizontal();
                
                if (showSettingsPanel)
                {
                    EditorGUILayout.BeginVertical(GUI.skin.box);
                    {
                        EditorGUILayout.Space();
                        
                        GUILayout.BeginHorizontal();
                        {
                            GUILayout.BeginVertical(GUILayout.Width(200));
                            {
                                includeSubAssets = EditorGUILayout.Toggle("包含子资产", includeSubAssets);
                                includeBuiltInAssets = EditorGUILayout.Toggle("包含内置资产", includeBuiltInAssets);
                            }
                            GUILayout.EndVertical();
                            
                            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
                            {
                                GUILayout.BeginHorizontal();
                                {
                                    EditorGUILayout.LabelField("搜索过滤:", GUILayout.Width(70));
                                    searchFilter = EditorGUILayout.TextField(searchFilter);
                                }
                                GUILayout.EndHorizontal();
                            }
                            GUILayout.EndVertical();
                        }
                        GUILayout.EndHorizontal();
                        
                        EditorGUILayout.Space();
                        
                        EditorGUILayout.LabelField("类型过滤:", EditorStyles.boldLabel);
                        GUILayout.BeginVertical();
                        {
                            GUILayout.BeginHorizontal();
                            {
                                int count = 0;
                                foreach (var type in assetTypeFilters.Keys.ToList())
                                {
                                    assetTypeFilters[type] = EditorGUILayout.ToggleLeft(type, assetTypeFilters[type], GUILayout.Width(100));
                                    count++;
                                    if (count % 3 == 0)
                                    {
                                        EditorGUILayout.EndHorizontal();
                                        GUILayout.BeginHorizontal();
                                    }
                                }
                            }
                            GUILayout.EndHorizontal();
                        }
                        GUILayout.EndVertical();
                    }
                    EditorGUILayout.EndVertical();
                }
            }
            GUILayout.EndVertical();
            
            EditorGUILayout.Space();
        }

        private void DrawExportOptions()
        {
            GUILayout.BeginVertical("Box");
            {
                GUILayout.BeginHorizontal();
                {
                    showExportOptions = EditorGUILayout.Foldout(showExportOptions, "导出选项", true, EditorStyles.foldoutHeader);
                }
                GUILayout.EndHorizontal();
                
                if (showExportOptions)
                {
                    EditorGUILayout.BeginVertical(GUI.skin.box);
                    {
                        EditorGUILayout.Space();
                        
                        GUILayout.BeginHorizontal();
                        {
                            EditorGUILayout.LabelField("导出格式:", GUILayout.Width(80));
                            exportFormat = (ExportFormat)EditorGUILayout.EnumPopup(exportFormat, GUILayout.Width(150));
                        }
                        GUILayout.EndHorizontal();
                        
                        EditorGUILayout.Space();
                        
                        EditorGUILayout.LabelField("分类方式:", EditorStyles.boldLabel);
                        GUILayout.BeginVertical();
                        {
                            exportByType = EditorGUILayout.ToggleLeft(" 按类型名称分类", exportByType);
                            exportBySpecificType = EditorGUILayout.ToggleLeft(" 按特定类型分类", exportBySpecificType);
                            preserveFolderStructure = EditorGUILayout.ToggleLeft(" 保持原始文件夹结构", preserveFolderStructure);
                        }
                        GUILayout.EndVertical();
                        
                        if (exportBySpecificType)
                            exportByType = false;
                        
                        EditorGUILayout.Space();
                        
                        EditorGUILayout.LabelField("导出路径:", EditorStyles.boldLabel);
                        GUILayout.BeginHorizontal();
                        {
                            customExportPath = EditorGUILayout.TextField(customExportPath);
                            if (GUILayout.Button("浏览", GUILayout.Width(60)))
                            {
                                string selectedPath = EditorUtility.SaveFolderPanel("选择导出路径", customExportPath, "");
                                if (!string.IsNullOrEmpty(selectedPath))
                                {
                                    customExportPath = selectedPath;
                                }
                            }
                        }
                        GUILayout.EndHorizontal();
                        
                        if (string.IsNullOrEmpty(customExportPath))
                        {
                            EditorGUILayout.HelpBox("未指定自定义路径，导出时将提示选择位置", MessageType.Info);
                        }
                        else
                        {
                            EditorGUILayout.HelpBox($"将导出到: {customExportPath}", MessageType.Info);
                        }
                    }
                    EditorGUILayout.EndVertical();
                }
            }
            GUILayout.EndVertical();
            
            EditorGUILayout.Space();
        }

        private void DrawSpecificTypeSettings()
        {
            if (!exportBySpecificType) return;
            
            GUILayout.BeginVertical("Box");
            {
                GUILayout.BeginHorizontal();
                {
                    showTypeSettings = EditorGUILayout.Foldout(showTypeSettings, "特定类型分类设置", true, EditorStyles.foldoutHeader);
                }
                GUILayout.EndHorizontal();
                
                if (showTypeSettings)
                {
                    EditorGUILayout.BeginVertical(GUI.skin.box);
                    {
                        EditorGUILayout.Space();
                        
                        EditorGUILayout.LabelField("选择要导出的类型:", EditorStyles.boldLabel);
                        GUILayout.BeginVertical();
                        {
                            GUILayout.BeginHorizontal();
                            {
                                GUILayout.BeginVertical();
                                {
                                    specificTypeSettings.texture2D = EditorGUILayout.ToggleLeft(" Texture2D", specificTypeSettings.texture2D);
                                    specificTypeSettings.material = EditorGUILayout.ToggleLeft(" Material", specificTypeSettings.material);
                                    specificTypeSettings.mesh = EditorGUILayout.ToggleLeft(" Mesh", specificTypeSettings.mesh);
                                    specificTypeSettings.prefab = EditorGUILayout.ToggleLeft(" Prefab", specificTypeSettings.prefab);
                                    // 新增地形选项
                                    specificTypeSettings.terrain = EditorGUILayout.ToggleLeft(" Terrain", specificTypeSettings.terrain);
                                }
                                GUILayout.EndVertical();
                                
                                GUILayout.BeginVertical();
                                {
                                    specificTypeSettings.gameObject = EditorGUILayout.ToggleLeft(" GameObject", specificTypeSettings.gameObject);
                                    specificTypeSettings.audioClip = EditorGUILayout.ToggleLeft(" AudioClip", specificTypeSettings.audioClip);
                                    specificTypeSettings.animationClip = EditorGUILayout.ToggleLeft(" AnimationClip", specificTypeSettings.animationClip);
                                    specificTypeSettings.animatorController = EditorGUILayout.ToggleLeft(" AnimatorController", specificTypeSettings.animatorController);
                                    // 新增地形数据选项
                                    specificTypeSettings.terrainData = EditorGUILayout.ToggleLeft(" TerrainData", specificTypeSettings.terrainData);
                                }
                                GUILayout.EndVertical();
                                
                                GUILayout.BeginVertical();
                                {
                                    specificTypeSettings.shader = EditorGUILayout.ToggleLeft(" Shader", specificTypeSettings.shader);
                                    specificTypeSettings.script = EditorGUILayout.ToggleLeft(" Script", specificTypeSettings.script);
                                    specificTypeSettings.font = EditorGUILayout.ToggleLeft(" Font", specificTypeSettings.font);
                                    specificTypeSettings.textAsset = EditorGUILayout.ToggleLeft(" TextAsset", specificTypeSettings.textAsset);
                                }
                                GUILayout.EndVertical();
                            }
                            GUILayout.EndHorizontal();
                            
                            GUILayout.BeginHorizontal();
                            {
                                GUILayout.BeginVertical();
                                {
                                    specificTypeSettings.other = EditorGUILayout.ToggleLeft(" 其他类型", specificTypeSettings.other);
                                }
                                GUILayout.EndVertical();
                                
                                GUILayout.BeginVertical();
                                {
                                    specificTypeSettings.hlslFile = EditorGUILayout.ToggleLeft(" HLSL文件", specificTypeSettings.hlslFile);
                                }
                                GUILayout.EndVertical();
                            }
                            GUILayout.EndHorizontal();

                            // HLSL提取选项
                            GUILayout.BeginVertical("Box");
                            {
                                EditorGUILayout.LabelField("HLSL提取选项:", EditorStyles.boldLabel);
                                specificTypeSettings.extractShaderIncludes = EditorGUILayout.ToggleLeft(" 提取Shader引用的HLSL文件", specificTypeSettings.extractShaderIncludes);
                                specificTypeSettings.preserveHLSLFolderStructure = EditorGUILayout.ToggleLeft(" 根据#include路径存放HLSL文件", specificTypeSettings.preserveHLSLFolderStructure);
                                specificTypeSettings.createHLSLSymlinks = EditorGUILayout.ToggleLeft(" 为HLSL文件创建多位置副本", specificTypeSettings.createHLSLSymlinks);
                                specificTypeSettings.excludeURPAssets = EditorGUILayout.ToggleLeft(" 排除URP自带HLSL和Shader", specificTypeSettings.excludeURPAssets);
                                
                                GUILayout.BeginHorizontal();
                                {
                                    if (GUILayout.Button("测试HLSL提取", GUILayout.Width(120)))
                                    {
                                        TestHLSLExtraction();
                                    }
                                    
                                    if (GUILayout.Button("查看HLSL映射", GUILayout.Width(120)))
                                    {
                                        ShowHLSLMapping();
                                    }
                                    
                                    if (GUILayout.Button("分析路径问题", GUILayout.Width(120)))
                                    {
                                        AnalyzeHLSLPathIssue();
                                    }
                                    
                                    if (GUILayout.Button("修复HLSL路径", GUILayout.Width(120)))
                                    {
                                        FixHLSLPaths();
                                    }
                                }
                                GUILayout.EndHorizontal();
                                
                                EditorGUILayout.HelpBox("HLSL文件将根据Shader中的#include指令路径进行存放。如果Shader期望的路径与实际路径不匹配，可以启用'多位置副本'选项。", MessageType.Info);
                            }
                            GUILayout.EndVertical();
                        }
                        GUILayout.EndVertical();
                        
                        EditorGUILayout.Space();
                        
                        EditorGUILayout.LabelField("文件夹名称设置:", EditorStyles.boldLabel);
                        GUILayout.BeginVertical();
                        {
                            GUILayout.BeginHorizontal();
                            {
                                GUILayout.BeginVertical();
                                {
                                    specificTypeSettings.texture2DFolder = EditorGUILayout.TextField("贴图文件夹:", specificTypeSettings.texture2DFolder);
                                    specificTypeSettings.materialFolder = EditorGUILayout.TextField("材质文件夹:", specificTypeSettings.materialFolder);
                                    specificTypeSettings.meshFolder = EditorGUILayout.TextField("模型文件夹:", specificTypeSettings.meshFolder);
                                    specificTypeSettings.prefabFolder = EditorGUILayout.TextField("预制体文件夹:", specificTypeSettings.prefabFolder);
                                    // 新增地形文件夹设置
                                    specificTypeSettings.terrainFolder = EditorGUILayout.TextField("地形文件夹:", specificTypeSettings.terrainFolder);
                                }
                                GUILayout.EndVertical();
                                
                                GUILayout.BeginVertical();
                                {
                                    specificTypeSettings.audioClipFolder = EditorGUILayout.TextField("音频文件夹:", specificTypeSettings.audioClipFolder);
                                    specificTypeSettings.animationClipFolder = EditorGUILayout.TextField("动画文件夹:", specificTypeSettings.animationClipFolder);
                                    specificTypeSettings.animatorControllerFolder = EditorGUILayout.TextField("动画控制器文件夹:", specificTypeSettings.animatorControllerFolder);
                                    specificTypeSettings.shaderFolder = EditorGUILayout.TextField("着色器文件夹:", specificTypeSettings.shaderFolder);
                                    // 新增地形数据文件夹设置
                                    specificTypeSettings.terrainDataFolder = EditorGUILayout.TextField("地形数据文件夹:", specificTypeSettings.terrainDataFolder);
                                }
                                GUILayout.EndVertical();
                            }
                            GUILayout.EndHorizontal();
                            
                            GUILayout.BeginHorizontal();
                            {
                                GUILayout.BeginVertical();
                                {
                                    specificTypeSettings.scriptFolder = EditorGUILayout.TextField("脚本文件夹:", specificTypeSettings.scriptFolder);
                                    specificTypeSettings.fontFolder = EditorGUILayout.TextField("字体文件夹:", specificTypeSettings.fontFolder);
                                    specificTypeSettings.textAssetFolder = EditorGUILayout.TextField("文本文件夹:", specificTypeSettings.textAssetFolder);
                                    specificTypeSettings.otherFolder = EditorGUILayout.TextField("其他文件夹:", specificTypeSettings.otherFolder);
                                }
                                GUILayout.EndVertical();
                            }
                            GUILayout.EndHorizontal();
                        }
                        GUILayout.EndVertical();
                    }
                    EditorGUILayout.EndVertical();
                }
            }
            GUILayout.EndVertical();
            
            EditorGUILayout.Space();
        }

        private void DrawAssetList()
        {
            GUILayout.BeginVertical("Box");
            {
                GUILayout.BeginHorizontal();
                {
                    showAssetList = EditorGUILayout.Foldout(showAssetList, $"资产列表 ({GetFilteredAssets().Count})", true, EditorStyles.foldoutHeader);
                    GUILayout.FlexibleSpace();
                    
                    var filteredAssets = GetFilteredAssets();
                    if (filteredAssets.Count > 0)
                    {
                        long totalSize = filteredAssets.Sum(a => a.FileSize);
                        GUILayout.Label($"总大小: {FormatFileSize(totalSize)}", EditorStyles.miniLabel);
                    }
                }
                GUILayout.EndHorizontal();
                
                if (showAssetList)
                {
                    EditorGUILayout.BeginVertical(GUI.skin.box);
                    {
                        var filteredAssets = GetFilteredAssets();
                        
                        if (filteredAssets.Count == 0)
                        {
                            EditorGUILayout.HelpBox("没有找到匹配的资产", MessageType.Info);
                        }
                        else
                        {
                            assetListScrollPosition = EditorGUILayout.BeginScrollView(assetListScrollPosition, GUILayout.Height(350));
                            {
                                GUILayout.BeginHorizontal(EditorStyles.toolbar);
                                {
                                    GUILayout.Label("资产", EditorStyles.miniLabel, GUILayout.MinWidth(300), GUILayout.ExpandWidth(true));
                                    GUILayout.Label("类型", EditorStyles.miniLabel, GUILayout.Width(100));
                                    GUILayout.Label("大小", EditorStyles.miniLabel, GUILayout.Width(80));
                                    GUILayout.Label("操作", EditorStyles.miniLabel, GUILayout.Width(100));
                                }
                                GUILayout.EndHorizontal();
                                
                                foreach (var asset in filteredAssets)
                                {
                                    DrawAssetItem(asset);
                                }
                            }
                            EditorGUILayout.EndScrollView();
                        }
                    }
                    EditorGUILayout.EndVertical();
                }
            }
            GUILayout.EndVertical();
        }

        private void DrawAssetItem(AssetInfo asset)
        {
            GUILayout.BeginHorizontal();
            {
                GUILayout.BeginHorizontal(GUILayout.MinWidth(300), GUILayout.ExpandWidth(true));
                {
                    var icon = AssetDatabase.GetCachedIcon(asset.Path);
                    if (icon != null)
                    {
                        GUILayout.Box(icon, GUILayout.Width(20), GUILayout.Height(20));
                    }
                    else
                    {
                        GUILayout.Box("", GUILayout.Width(20), GUILayout.Height(20));
                    }
                    
                    var content = new GUIContent(asset.Path);
                    if (GUILayout.Button(content, EditorStyles.label, GUILayout.ExpandWidth(true)))
                    {
                        FocusAssetInProject(asset);
                    }
                }
                GUILayout.EndHorizontal();
                
                string displayType = GetDisplayType(asset);
                GUILayout.Label(displayType, GUILayout.Width(100));
                
                GUILayout.Label(FormatFileSize(asset.FileSize), GUILayout.Width(80));
                
                GUILayout.BeginHorizontal(GUILayout.Width(100));
                {
                    if (GUILayout.Button("定位", GUILayout.Width(45)))
                    {
                        FocusAssetInProject(asset);
                    }
                    
                    if (GUILayout.Button("场景", GUILayout.Width(45)))
                    {
                        LocateAssetInScene(asset);
                    }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndHorizontal();
        }

        private void DrawActionButtons()
        {
            GUILayout.BeginHorizontal();
            {
                GUI.enabled = GetFilteredAssets().Count > 0;
                if (GUILayout.Button("导出选中资产", GUILayout.Height(35)))
                {
                    ExportSelectedAssets();
                }
                
                if (GUILayout.Button("导出Unity包", GUILayout.Height(35)))
                {
                    ExportAsUnityPackage();
                }
                
                if (GUILayout.Button("复制到项目", GUILayout.Height(35)))
                {
                    CopyAssetsToFolder();
                }
                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();
        }

        private List<AssetInfo> GetFilteredAssets()
        {
            return foundAssets.Where(asset => 
                (string.IsNullOrEmpty(searchFilter) || asset.Path.ToLower().Contains(searchFilter.ToLower())) &&
                assetTypeFilters.ContainsKey(GetDisplayType(asset)) && assetTypeFilters[GetDisplayType(asset)] &&
                !IsURPAsset(asset.Path)
            ).ToList();
        }

        public void RefreshAssets()
        {
            foundAssets.Clear();
            processedHLSLFiles.Clear();
            shaderToHLSLMap.Clear();
            hlslUsageMap.Clear();
            ExtractSceneAssets();
            EditorUtility.ClearProgressBar();
        }

        private void QuickExport()
        {
            var filteredAssets = GetFilteredAssets();
            if (filteredAssets.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "没有资产可导出", "确定");
                return;
            }

            string exportFolder = GetExportFolder();
            if (string.IsNullOrEmpty(exportFolder)) return;

            ExportAssetsToFolder(filteredAssets, exportFolder);
        }

        private void ExtractSceneAssets()
        {
            Scene currentScene = SceneManager.GetActiveScene();
            if (!currentScene.IsValid())
            {
                Debug.LogWarning("没有打开的场景");
                return;
            }

            GameObject[] rootObjects = currentScene.GetRootGameObjects();
            HashSet<UnityEngine.Object> collectedObjects = new HashSet<UnityEngine.Object>();

            foreach (GameObject root in rootObjects)
            {
                CollectAssetsFromGameObject(root, collectedObjects);
            }

            // 特殊处理：收集场景中所有的地形组件
            CollectTerrainAssets(collectedObjects);

            if (includeSubAssets)
            {
                List<UnityEngine.Object> objectsToProcess = new List<UnityEngine.Object>(collectedObjects);
                
                foreach (UnityEngine.Object obj in objectsToProcess)
                {
                    if (obj == null) continue;
                    
                    string path = AssetDatabase.GetAssetPath(obj);
                    if (string.IsNullOrEmpty(path)) continue;
                    
                    // 排除URP资产
                    if (specificTypeSettings.excludeURPAssets && IsURPAsset(path))
                    {
                        continue;
                    }
                    
                    CollectDependencies(path, collectedObjects);
                    
                    if (specificTypeSettings.extractShaderIncludes)
                    {
                        if (obj is Shader)
                        {
                            ExtractHLSLReferencesFromShader(path, collectedObjects);
                        }
                        else if (obj is ComputeShader)
                        {
                            ExtractHLSLReferencesFromComputeShader(path, collectedObjects);
                        }
                        else if (obj is Material)
                        {
                            Material material = obj as Material;
                            if (material.shader != null)
                            {
                                string shaderPath = AssetDatabase.GetAssetPath(material.shader);
                                if (!string.IsNullOrEmpty(shaderPath))
                                {
                                    ExtractHLSLReferencesFromShader(shaderPath, collectedObjects);
                                }
                            }
                        }
                    }
                }
            }

            foreach (UnityEngine.Object obj in collectedObjects)
            {
                if (obj == null) continue;

                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) continue;

                if (!includeBuiltInAssets && path.StartsWith("Library/"))
                    continue;

                // 排除URP资产
                if (specificTypeSettings.excludeURPAssets && IsURPAsset(path))
                {
                    continue;
                }

                var assetInfo = new AssetInfo
                {
                    Object = obj,
                    Path = path,
                    Type = obj.GetType(),
                    FileSize = GetFileSize(path)
                };

                foundAssets.Add(assetInfo);
            }

            foundAssets = foundAssets.GroupBy(a => a.Path).Select(g => g.First()).ToList();
            
            Debug.Log($"场景资产提取完成，共找到 {foundAssets.Count} 个资产");
        }
        
        // 新增方法：专门收集脚本资产
        private void CollectScriptAssets(HashSet<UnityEngine.Object> collectedObjects)
        {
            try
            {
                // 方法1：通过场景中的MonoBehaviour组件收集脚本
                MonoBehaviour[] monoBehaviours = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
                foreach (MonoBehaviour mb in monoBehaviours)
                {
                    if (mb == null) continue;
                    
                    System.Type scriptType = mb.GetType();
                    if (scriptType != null && !scriptType.IsAbstract)
                    {
                        // 获取MonoScript资产
                        MonoScript monoScript = MonoScript.FromMonoBehaviour(mb);
                        if (monoScript != null)
                        {
                            collectedObjects.Add(monoScript);
                            Debug.Log($"找到脚本: {monoScript.name} - {AssetDatabase.GetAssetPath(monoScript)}");
                        }
                    }
                }

                // 方法2：通过ScriptableObject收集脚本
                ScriptableObject[] scriptableObjects = Resources.FindObjectsOfTypeAll<ScriptableObject>();
                foreach (ScriptableObject so in scriptableObjects)
                {
                    if (so == null) continue;
                    
                    System.Type scriptType = so.GetType();
                    if (scriptType != null && !scriptType.IsAbstract)
                    {
                        MonoScript monoScript = MonoScript.FromScriptableObject(so);
                        if (monoScript != null)
                        {
                            collectedObjects.Add(monoScript);
                            Debug.Log($"找到ScriptableObject脚本: {monoScript.name} - {AssetDatabase.GetAssetPath(monoScript)}");
                        }
                    }
                }

                // 方法3：通过序列化属性收集所有MonoScript引用
                CollectScriptReferencesFromScene(collectedObjects);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"收集脚本资产时出错: {e.Message}");
            }
        }

        // 新增方法：通过序列化属性收集脚本引用
        private void CollectScriptReferencesFromScene(HashSet<UnityEngine.Object> collectedObjects)
        {
            Scene currentScene = SceneManager.GetActiveScene();
            GameObject[] rootObjects = currentScene.GetRootGameObjects();
            
            foreach (GameObject root in rootObjects)
            {
                CollectScriptReferencesFromGameObject(root, collectedObjects);
            }
        }

        // 新增方法：从游戏对象收集脚本引用
        private void CollectScriptReferencesFromGameObject(GameObject gameObject, HashSet<UnityEngine.Object> collectedObjects)
        {
            if (gameObject == null) return;

            Component[] components = gameObject.GetComponents<Component>();
            foreach (Component component in components)
            {
                if (component == null) continue;

                // 使用SerializedObject来获取所有序列化属性
                SerializedObject serializedObject = new SerializedObject(component);
                SerializedProperty property = serializedObject.GetIterator();

                while (property.NextVisible(true))
                {
                    if (property.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        UnityEngine.Object referencedObject = property.objectReferenceValue;
                        if (referencedObject != null)
                        {
                            // 检查是否是MonoScript
                            if (referencedObject is MonoScript)
                            {
                                collectedObjects.Add(referencedObject);
                                Debug.Log($"找到脚本引用: {referencedObject.name} - {AssetDatabase.GetAssetPath(referencedObject)}");
                            }
                            // 检查是否是ScriptableObject
                            else if (referencedObject is ScriptableObject)
                            {
                                System.Type scriptType = referencedObject.GetType();
                                if (scriptType != null)
                                {
                                    MonoScript monoScript = MonoScript.FromScriptableObject((ScriptableObject)referencedObject);
                                    if (monoScript != null)
                                    {
                                        collectedObjects.Add(monoScript);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // 递归处理子对象
            foreach (Transform child in gameObject.transform)
            {
                CollectScriptReferencesFromGameObject(child.gameObject, collectedObjects);
            }
        }

        // 新增方法：专门收集地形资产
        private void CollectTerrainAssets(HashSet<UnityEngine.Object> collectedObjects)
        {
            Terrain[] terrains = UnityEngine.Object.FindObjectsOfType<Terrain>();
            foreach (Terrain terrain in terrains)
            {
                if (terrain != null)
                {
                    collectedObjects.Add(terrain);
                    
                    // 收集地形数据
                    if (terrain.terrainData != null)
                    {
                        collectedObjects.Add(terrain.terrainData);
                        
                        // 收集地形相关的材质和纹理
                        if (terrain.materialTemplate != null)
                        {
                            collectedObjects.Add(terrain.materialTemplate);
                        }
                        
                        // 收集地形纹理
                        if (terrain.terrainData != null)
                        {
                            // 地形alphamap纹理
                            Texture2D[] alphaTextures = terrain.terrainData.alphamapTextures;
                            foreach (Texture2D tex in alphaTextures)
                            {
                                if (tex != null) collectedObjects.Add(tex);
                            }
                            
                            // 地形细节纹理
                            DetailPrototype[] details = terrain.terrainData.detailPrototypes;
                            foreach (DetailPrototype detail in details)
                            {
                                if (detail.prototypeTexture != null) 
                                    collectedObjects.Add(detail.prototypeTexture);
                                if (detail.prototype != null) 
                                    collectedObjects.Add(detail.prototype);
                            }
                            
                            // 地形树原型
                            TreePrototype[] trees = terrain.terrainData.treePrototypes;
                            foreach (TreePrototype tree in trees)
                            {
                                if (tree.prefab != null) 
                                    collectedObjects.Add(tree.prefab);
                            }
                        }
                    }
                }
            }
        }

        private bool IsURPAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;
            
            string[] urpKeywords = {
                "Packages/com.unity.render-pipelines.universal",
                "UniversalRenderPipelineAsset",
                "UniversalRenderer",
                "URP-",
                "UniversalAdditional",
                "Universal Render Pipeline",
                "ShaderLibrary/Universal",
                "ShaderGraph/Universal",
                "Shaders/Universal",
                "HLSL/Universal",
                "HLSL/URP",
                "RenderPipeline/Universal"
            };
            
            string lowerPath = assetPath.ToLower();
            
            foreach (string keyword in urpKeywords)
            {
                if (lowerPath.Contains(keyword.ToLower()))
                {
                    Debug.Log($"排除URP资产: {assetPath}");
                    return true;
                }
            }
            
            return false;
        }

        private void CollectAssetsFromGameObject(GameObject gameObject, HashSet<UnityEngine.Object> collectedObjects)
        {
            if (gameObject == null) return;

            // 检查游戏对象本身是否是预制体实例
            if (PrefabUtility.IsPartOfAnyPrefab(gameObject))
            {
                UnityEngine.Object prefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
                if (prefabAsset != null)
                {
                    collectedObjects.Add(prefabAsset);
                    Debug.Log($"找到预制体引用: {AssetDatabase.GetAssetPath(prefabAsset)}");
                }
            }

            Component[] components = gameObject.GetComponents<Component>();
            foreach (Component component in components)
            {
                if (component == null) continue;

                SerializedObject serializedObject = new SerializedObject(component);
                SerializedProperty property = serializedObject.GetIterator();

                while (property.NextVisible(true))
                {
                    if (property.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        UnityEngine.Object referencedObject = property.objectReferenceValue;
                        if (referencedObject != null)
                        {
                            collectedObjects.Add(referencedObject);
                            
                            // 特殊处理：如果引用的是预制体
                            if (referencedObject is GameObject)
                            {
                                string assetPath = AssetDatabase.GetAssetPath(referencedObject);
                                if (!string.IsNullOrEmpty(assetPath) && assetPath.EndsWith(".prefab"))
                                {
                                    collectedObjects.Add(referencedObject);
                                    Debug.Log($"找到GameObject预制体引用: {assetPath}");
                                }
                            }
                        }
                    }
                }
            }

            foreach (Transform child in gameObject.transform)
            {
                CollectAssetsFromGameObject(child.gameObject, collectedObjects);
            }
        }

        private void CollectDependencies(string assetPath, HashSet<UnityEngine.Object> collectedObjects)
        {
            try
            {
                string[] dependencies = AssetDatabase.GetDependencies(assetPath, true); // 改为true，包含间接依赖
                foreach (string dependency in dependencies)
                {
                    if (dependency != assetPath)
                    {
                        // 排除URP资产
                        if (specificTypeSettings.excludeURPAssets && IsURPAsset(dependency))
                        {
                            continue;
                        }
                
                        UnityEngine.Object depObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(dependency);
                        if (depObject != null)
                        {
                            collectedObjects.Add(depObject);
                    
                            // 特殊处理：如果是预制体，递归收集其依赖
                            if (dependency.EndsWith(".prefab"))
                            {
                                CollectDependencies(dependency, collectedObjects);
                            }
                        }
                    }
                }
        
                // 特殊处理：对于组件，收集其脚本依赖
                UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (mainAsset is GameObject)
                {
                    CollectScriptDependenciesFromPrefab((GameObject)mainAsset, collectedObjects);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"收集依赖项时出错 {assetPath}: {e.Message}");
            }
        }

        
        // 新增方法：从预制体收集脚本依赖
        private void CollectScriptDependenciesFromPrefab(GameObject prefab, HashSet<UnityEngine.Object> collectedObjects)
        {
            if (prefab == null) return;
    
            Component[] components = prefab.GetComponentsInChildren<Component>(true);
            foreach (Component component in components)
            {
                if (component == null) continue;
        
                System.Type componentType = component.GetType();
                if (componentType != null && componentType.IsSubclassOf(typeof(MonoBehaviour)))
                {
                    MonoScript monoScript = MonoScript.FromMonoBehaviour((MonoBehaviour)component);
                    if (monoScript != null)
                    {
                        collectedObjects.Add(monoScript);
                        Debug.Log($"从预制体找到脚本依赖: {monoScript.name} - {AssetDatabase.GetAssetPath(monoScript)}");
                    }
                }
            }
        }

        private void ExtractHLSLReferencesFromShader(string shaderPath, HashSet<UnityEngine.Object> collectedObjects)
        {
            if (string.IsNullOrEmpty(shaderPath) || !File.Exists(shaderPath)) return;
            
            // 排除URP资产
            if (specificTypeSettings.excludeURPAssets && IsURPAsset(shaderPath))
            {
                return;
            }

            try
            {
                string shaderCode = File.ReadAllText(shaderPath);
                ExtractHLSLReferencesFromCode(shaderCode, shaderPath, collectedObjects);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"无法读取Shader文件 {shaderPath}: {e.Message}");
            }
        }

        private void ExtractHLSLReferencesFromComputeShader(string computeShaderPath, HashSet<UnityEngine.Object> collectedObjects)
        {
            if (string.IsNullOrEmpty(computeShaderPath) || !File.Exists(computeShaderPath)) return;
            
            // 排除URP资产
            if (specificTypeSettings.excludeURPAssets && IsURPAsset(computeShaderPath))
            {
                return;
            }

            try
            {
                string shaderCode = File.ReadAllText(computeShaderPath);
                ExtractHLSLReferencesFromCode(shaderCode, computeShaderPath, collectedObjects);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"无法读取ComputeShader文件 {computeShaderPath}: {e.Message}");
            }
        }

        private void ExtractHLSLReferencesFromCode(string shaderCode, string sourceFilePath, HashSet<UnityEngine.Object> collectedObjects)
        {
            string sourceDirectory = Path.GetDirectoryName(sourceFilePath);
            
            Regex includeRegex = new Regex(@"#include\s*(?:<\s*([^>]+)\s*>|[""']\s*([^""']+)\s*[""'])", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            MatchCollection matches = includeRegex.Matches(shaderCode);
            
            foreach (Match match in matches)
            {
                string includePath = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                if (!string.IsNullOrEmpty(includePath))
                {
                    ResolveAndAddHLSLFile(includePath, sourceDirectory, sourceFilePath, collectedObjects);
                }
            }
        }

        private void ResolveAndAddHLSLFile(string includePath, string baseDirectory, string sourceShaderPath, HashSet<UnityEngine.Object> collectedObjects)
        {
            if (processedHLSLFiles.Contains(includePath + "|" + sourceShaderPath))
                return;
                
            processedHLSLFiles.Add(includePath + "|" + sourceShaderPath);
            
            includePath = includePath.Replace("\\", "/").Trim();
            
            List<string> possiblePaths = new List<string>();
            
            // 1. 直接路径
            if (File.Exists(includePath))
            {
                possiblePaths.Add(includePath);
            }
            
            // 2. 相对于基础目录
            if (!string.IsNullOrEmpty(baseDirectory))
            {
                string relativePath = Path.Combine(baseDirectory, includePath).Replace("\\", "/");
                if (File.Exists(relativePath))
                {
                    possiblePaths.Add(relativePath);
                }
                
                string[] allFilesInBaseDir = Directory.GetFiles(baseDirectory, "*.*", SearchOption.AllDirectories)
                    .Where(f => IsHLSLFile(f)).ToArray();
                
                string targetFileName = Path.GetFileName(includePath);
                var matchingFiles = allFilesInBaseDir.Where(f => 
                    Path.GetFileName(f).Equals(targetFileName, StringComparison.OrdinalIgnoreCase)).ToArray();
                
                if (matchingFiles.Length > 0)
                {
                    possiblePaths.AddRange(matchingFiles);
                }
            }
            
            // 3. 在项目中搜索
            string foundPath = FindHLSLFileInProject(includePath);
            if (!string.IsNullOrEmpty(foundPath))
            {
                possiblePaths.Add(foundPath);
            }
            
            foreach (string path in possiblePaths.Distinct())
            {
                if (IsHLSLFile(path) && File.Exists(path))
                {
                    // 排除URP资产
                    if (specificTypeSettings.excludeURPAssets && IsURPAsset(path))
                    {
                        continue;
                    }
                    
                    AddHLSLFileToCollection(path, collectedObjects);
                    
                    string assetPath = path;
                    if (assetPath.StartsWith(Application.dataPath))
                    {
                        assetPath = "Assets" + assetPath.Substring(Application.dataPath.Length);
                    }
                    
                    if (!shaderToHLSLMap.ContainsKey(sourceShaderPath))
                    {
                        shaderToHLSLMap[sourceShaderPath] = new List<HLSLReference>();
                    }
                    
                    var existingRef = shaderToHLSLMap[sourceShaderPath].FirstOrDefault(r => r.hlslPath == assetPath);
                    if (existingRef == null)
                    {
                        var hlslRef = new HLSLReference
                        {
                            hlslPath = assetPath,
                            includePath = includePath,
                            shaderPath = sourceShaderPath
                        };
                        shaderToHLSLMap[sourceShaderPath].Add(hlslRef);
                        
                        if (!hlslUsageMap.ContainsKey(assetPath))
                        {
                            hlslUsageMap[assetPath] = new List<string>();
                        }
                        hlslUsageMap[assetPath].Add(sourceShaderPath);
                        
                        Debug.Log($"映射: {sourceShaderPath} -> {assetPath} (include: {includePath})");
                    }
                    
                    try
                    {
                        string hlslCode = File.ReadAllText(path);
                        string hlslDirectory = Path.GetDirectoryName(path);
                        
                        ExtractHLSLReferencesFromCode(hlslCode, path, collectedObjects);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"无法读取HLSL文件进行递归解析 {path}: {e.Message}");
                    }
                }
            }
        }

        private void AddHLSLFileToCollection(string filePath, HashSet<UnityEngine.Object> collectedObjects)
        {
            if (!File.Exists(filePath)) return;
            
            string assetPath = filePath;
            if (assetPath.StartsWith(Application.dataPath))
            {
                assetPath = "Assets" + assetPath.Substring(Application.dataPath.Length);
            }
            
            UnityEngine.Object hlslAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (hlslAsset == null)
            {
                hlslAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            }
            
            if (hlslAsset != null && !collectedObjects.Contains(hlslAsset))
            {
                collectedObjects.Add(hlslAsset);
                Debug.Log($"找到HLSL文件: {assetPath}");
            }
        }

        private string FindHLSLFileInProject(string fileName)
        {
            string searchName = Path.GetFileName(fileName);
            
            string[] allFiles = Directory.GetFiles("Assets", "*.*", SearchOption.AllDirectories)
                .Where(f => IsHLSLFile(f)).ToArray();
            
            foreach (string file in allFiles)
            {
                if (Path.GetFileName(file).Equals(searchName, StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }
            }
            
            foreach (string file in allFiles)
            {
                if (file.ToLower().Contains(fileName.ToLower()))
                {
                    return file;
                }
            }
            
            return null;
        }

        private bool IsHLSLFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            
            string extension = Path.GetExtension(filePath).ToLower();
            string[] hlslExtensions = { 
                ".hlsl", ".cg", ".cginc", ".glslinc", ".compute", 
                ".shader", ".shaderinc", ".inc", ".h", ".hlsli" 
            };
            
            return hlslExtensions.Contains(extension);
        }

        private string GetDisplayType(AssetInfo asset)
        {
            string assetPath = asset.Path.ToLower();
            
            if (assetPath.EndsWith(".prefab"))
            {
                return "Prefab";
            }
            else if (assetPath.EndsWith(".hlsl") || assetPath.EndsWith(".cg") || 
                assetPath.EndsWith(".cginc") || assetPath.EndsWith(".glslinc") ||
                assetPath.EndsWith(".shaderinc") || assetPath.EndsWith(".inc") ||
                assetPath.EndsWith(".h") || assetPath.EndsWith(".hlsli"))
            {
                return "HLSL";
            }
            else if (assetPath.EndsWith(".compute"))
            {
                return "ComputeShader";
            }
            else if (asset.Type.Name == "TextAsset" && IsHLSLFile(assetPath))
            {
                return "HLSL";
            }
            else if (asset.Type.Name == "Terrain")
            {
                return "Terrain";
            }
            else if (asset.Type.Name == "TerrainData")
            {
                return "TerrainData";
            }
            // 新增：专门处理脚本文件
            else if (asset.Type.Name == "MonoScript" || assetPath.EndsWith(".cs"))
            {
                return "Script";
            }
            else
            {
                return asset.Type.Name;
            }
        }

        private long GetFileSize(string path)
        {
            if (File.Exists(path))
            {
                FileInfo fileInfo = new FileInfo(path);
                return fileInfo.Length;
            }
            return 0;
        }

        private string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return string.Format("{0:n1} {1}", number, suffixes[counter]);
        }

        private void ExportSelectedAssets()
        {
            var filteredAssets = GetFilteredAssets();
            if (filteredAssets.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "没有资产可导出", "确定");
                return;
            }

            string exportFolder = GetExportFolder();
            if (string.IsNullOrEmpty(exportFolder)) return;

            if (!Directory.Exists(exportFolder))
            {
                Directory.CreateDirectory(exportFolder);
            }

            int exportedCount = 0;
            int totalCount = filteredAssets.Count;
            
            Dictionary<string, List<string>> hlslExportPaths = new Dictionary<string, List<string>>();
            foreach (var asset in filteredAssets)
            {
                if (GetDisplayType(asset) == "HLSL")
                {
                    string primaryDestPath = GetDestinationPath(exportFolder, asset, asset.Path);
                    
                    if (!hlslExportPaths.ContainsKey(asset.Path))
                    {
                        hlslExportPaths[asset.Path] = new List<string>();
                    }
                    hlslExportPaths[asset.Path].Add(primaryDestPath);
                    
                    if (specificTypeSettings.createHLSLSymlinks && hlslUsageMap.ContainsKey(asset.Path))
                    {
                        foreach (string shaderPath in hlslUsageMap[asset.Path])
                        {
                            string altDestPath = GetHLSLDestinationPathForShader(exportFolder, asset, asset.Path, shaderPath);
                            if (!string.IsNullOrEmpty(altDestPath) && !hlslExportPaths[asset.Path].Contains(altDestPath))
                            {
                                hlslExportPaths[asset.Path].Add(altDestPath);
                                Debug.Log($"为HLSL文件 {asset.Path} 添加额外位置: {altDestPath} (用于Shader: {shaderPath})");
                            }
                        }
                    }
                }
            }
            
            for (int i = 0; i < totalCount; i++)
            {
                var asset = filteredAssets[i];
                float progress = (float)i / totalCount;
                EditorUtility.DisplayProgressBar("导出资产", $"正在导出 {asset.Path} ({i+1}/{totalCount})", progress);

                string sourcePath = asset.Path;
                
                if (!File.Exists(sourcePath))
                {
                    Debug.LogWarning($"源文件不存在: {sourcePath}");
                    continue;
                }

                List<string> destPaths = new List<string>();
                
                if (GetDisplayType(asset) == "HLSL" && hlslExportPaths.ContainsKey(sourcePath))
                {
                    destPaths.AddRange(hlslExportPaths[sourcePath]);
                }
                else
                {
                    string destPath = GetDestinationPath(exportFolder, asset, sourcePath);
                    destPaths.Add(destPath);
                }
                
                foreach (string destPath in destPaths.Distinct())
                {
                    Debug.Log($"导出: {sourcePath} -> {destPath}");

                    try
                    {
                        string destDirectory = Path.GetDirectoryName(destPath);
                        if (!Directory.Exists(destDirectory))
                        {
                            Directory.CreateDirectory(destDirectory);
                        }

                        File.Copy(sourcePath, destPath, true);
                        
                        if (GetDisplayType(asset) == "HLSL" && File.Exists(sourcePath + ".meta"))
                        {
                            File.Copy(sourcePath + ".meta", destPath + ".meta", true);
                        }
                        
                        exportedCount++;
                        
                        Debug.Log($"成功导出: {sourcePath} -> {destPath}");
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"导出失败 {sourcePath} -> {destPath}: {e.Message}");
                    }
                }
            }

            EditorUtility.ClearProgressBar();
            
            if (exportedCount > 0)
            {
                EditorUtility.DisplayDialog("完成", $"成功导出 {exportedCount} 个文件到 {exportFolder}", "确定");
                EditorUtility.RevealInFinder(exportFolder);
            }
            else
            {
                EditorUtility.DisplayDialog("错误", "没有成功导出任何资产，请查看控制台日志", "确定");
            }
        }

        private string GetExportFolder()
        {
            if (!string.IsNullOrEmpty(customExportPath))
            {
                return customExportPath;
            }
            
            return EditorUtility.SaveFolderPanel("选择导出位置", "", $"SceneAssets_{SceneManager.GetActiveScene().name}");
        }

        private string GetDestinationPath(string baseFolder, AssetInfo asset, string sourcePath)
        {
            string fileName = Path.GetFileName(sourcePath);
            string displayType = GetDisplayType(asset);
            
            Debug.Log($"计算目标路径 - 资产: {sourcePath}, 类型: {displayType}");
            
            if (displayType == "HLSL" && specificTypeSettings.preserveHLSLFolderStructure)
            {
                string hlslDestPath = GetHLSLDestinationPath(baseFolder, asset, sourcePath);
                if (!string.IsNullOrEmpty(hlslDestPath))
                {
                    Debug.Log($"HLSL目标路径: {hlslDestPath}");
                    return hlslDestPath;
                }
            }
            
            string destPath = Path.Combine(baseFolder, fileName);
            
            if (preserveFolderStructure)
            {
                string relativePath = GetRelativeAssetPath(sourcePath);
                destPath = Path.Combine(baseFolder, relativePath);
                Debug.Log($"保持文件夹结构的目标路径: {destPath}");
            }
            else if (exportBySpecificType)
            {
                string typeFolder = GetSpecificTypeFolder(asset, sourcePath);
                if (!string.IsNullOrEmpty(typeFolder))
                {
                    destPath = Path.Combine(baseFolder, typeFolder, fileName);
                    Debug.Log($"按特定类型分类的目标路径: {destPath}");
                }
            }
            else if (exportByType)
            {
                string typeFolder = SanitizeFolderName(asset.Type.Name);
                destPath = Path.Combine(baseFolder, typeFolder, fileName);
                Debug.Log($"按类型分类的目标路径: {destPath}");
            }
            
            Debug.Log($"最终目标路径: {destPath}");
            return destPath;
        }

        private string GetHLSLDestinationPathForShader(string baseFolder, AssetInfo asset, string sourcePath, string shaderPath)
        {
            try
            {
                if (shaderToHLSLMap.ContainsKey(shaderPath))
                {
                    List<HLSLReference> hlslRefs = shaderToHLSLMap[shaderPath];
                    var hlslRef = hlslRefs.FirstOrDefault(r => r.hlslPath == sourcePath);
                    
                    if (hlslRef != null)
                    {
                        string includePath = hlslRef.includePath;
                        includePath = includePath.Replace("\\", "/").Trim();
                        
                        Debug.Log($"为Shader {shaderPath} 处理HLSL路径: {sourcePath}, Include: {includePath}");
                        
                        string shaderExportPath = GetShaderExportPath(baseFolder, shaderPath);
                        string shaderExportDir = Path.GetDirectoryName(shaderExportPath);
                        
                        Debug.Log($"Shader导出路径: {shaderExportPath}");
                        Debug.Log($"Shader导出目录: {shaderExportDir}");
                        
                        if (includePath.StartsWith("Assets/"))
                        {
                            return Path.Combine(baseFolder, includePath).Replace("\\", "/");
                        }
                        
                        if (!string.IsNullOrEmpty(shaderExportDir))
                        {
                            string fullIncludePath = Path.Combine(shaderExportDir, includePath).Replace("\\", "/");
                            fullIncludePath = NormalizePath(fullIncludePath);
                            
                            if (fullIncludePath.StartsWith(baseFolder))
                            {
                                Debug.Log($"基于Shader导出目录的HLSL路径: {fullIncludePath}");
                                return fullIncludePath;
                            }
                            else
                            {
                                Debug.LogWarning($"HLSL路径超出导出目录，回退到原始结构: {fullIncludePath}");
                            }
                        }
                        
                        string shaderOriginalDir = Path.GetDirectoryName(shaderPath);
                        if (!string.IsNullOrEmpty(shaderOriginalDir))
                        {
                            string currentDir = shaderOriginalDir;
                            string relativePath = includePath;
                            
                            while (relativePath.StartsWith("../"))
                            {
                                currentDir = Path.GetDirectoryName(currentDir);
                                relativePath = relativePath.Substring(3);
                            }
                            
                            string resolvedPath = Path.Combine(currentDir, relativePath).Replace("\\", "/");
                            if (resolvedPath.StartsWith("Assets/"))
                            {
                                string exportPath = Path.Combine(baseFolder, resolvedPath).Replace("\\", "/");
                                Debug.Log($"处理上级目录后的HLSL路径: {exportPath}");
                                return exportPath;
                            }
                        }
                        
                        string hlslOriginalDir = Path.GetDirectoryName(sourcePath);
                        if (!string.IsNullOrEmpty(hlslOriginalDir) && hlslOriginalDir.StartsWith("Assets/"))
                        {
                            string exportPath = Path.Combine(baseFolder, sourcePath).Replace("\\", "/");
                            Debug.Log($"保持HLSL原始结构的路径: {exportPath}");
                            return exportPath;
                        }
                    }
                }
                
                string fileName = Path.GetFileName(sourcePath);
                string defaultPath = Path.Combine(baseFolder, "Shaders", "HLSL", fileName).Replace("\\", "/");
                Debug.Log($"使用默认HLSL路径: {defaultPath}");
                return defaultPath;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"获取HLSL目标路径失败: {e.Message}");
                
                string projectRootForError = Application.dataPath.Replace("Assets", "");
                string fullSourcePathForError = Path.Combine(projectRootForError, sourcePath);
                if (File.Exists(fullSourcePathForError))
                {
                    return Path.Combine(baseFolder, sourcePath).Replace("\\", "/");
                }
                
                string fileName = Path.GetFileName(sourcePath);
                return Path.Combine(baseFolder, fileName);
            }
        }

        private string GetShaderExportPath(string baseFolder, string shaderPath)
        {
            var shaderAsset = new AssetInfo
            {
                Path = shaderPath,
                Type = typeof(Shader)
            };
            
            return GetDestinationPath(baseFolder, shaderAsset, shaderPath);
        }

        private string NormalizePath(string path)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);
                return fullPath.Replace("\\", "/");
            }
            catch
            {
                return path;
            }
        }

        private string GetHLSLDestinationPath(string baseFolder, AssetInfo asset, string sourcePath)
        {
            if (hlslUsageMap.ContainsKey(sourcePath) && hlslUsageMap[sourcePath].Count > 0)
            {
                string firstShader = hlslUsageMap[sourcePath][0];
                string result = GetHLSLDestinationPathForShader(baseFolder, asset, sourcePath, firstShader);
                Debug.Log($"HLSL目标路径计算结果: {result}");
                return result;
            }
            
            string projectRoot = Application.dataPath.Replace("Assets", "");
            string fullSourcePath = Path.Combine(projectRoot, sourcePath);
            if (File.Exists(fullSourcePath))
            {
                string result = Path.Combine(baseFolder, sourcePath).Replace("\\", "/");
                Debug.Log($"使用原始结构的HLSL路径: {result}");
                return result;
            }
            
            string fileName = Path.GetFileName(sourcePath);
            string fallbackPath = Path.Combine(baseFolder, "Shaders", "HLSL", fileName);
            Debug.Log($"使用备用HLSL路径: {fallbackPath}");
            return fallbackPath;
        }

        private string GetSpecificTypeFolder(AssetInfo asset, string assetPath = "")
        {
            string displayType = GetDisplayType(asset);
            
            switch (displayType)
            {
                case "Prefab":
                    return specificTypeSettings.prefab ? specificTypeSettings.prefabFolder : null;
                case "HLSL":
                    return specificTypeSettings.hlslFile ? "Shaders/HLSL" : null;
                case "Texture2D":
                    return specificTypeSettings.texture2D ? specificTypeSettings.texture2DFolder : null;
                case "Material":
                    return specificTypeSettings.material ? specificTypeSettings.materialFolder : null;
                case "Mesh":
                    return specificTypeSettings.mesh ? specificTypeSettings.meshFolder : null;
                case "GameObject":
                    return specificTypeSettings.gameObject ? specificTypeSettings.gameObjectFolder : null;
                case "AudioClip":
                    return specificTypeSettings.audioClip ? specificTypeSettings.audioClipFolder : null;
                case "AnimationClip":
                    return specificTypeSettings.animationClip ? specificTypeSettings.animationClipFolder : null;
                case "AnimatorController":
                    return specificTypeSettings.animatorController ? specificTypeSettings.animatorControllerFolder : null;
                case "Shader":
                case "ComputeShader":
                    return specificTypeSettings.shader ? specificTypeSettings.shaderFolder : null;
                case "Script":  // 修改：将"MonoScript"改为"Script"
                    return specificTypeSettings.script ? specificTypeSettings.scriptFolder : null;
                case "Font":
                    return specificTypeSettings.font ? specificTypeSettings.fontFolder : null;
                case "TextAsset":
                    return specificTypeSettings.textAsset ? specificTypeSettings.textAssetFolder : null;
                // 新增地形类型处理
                case "Terrain":
                    return specificTypeSettings.terrain ? specificTypeSettings.terrainFolder : null;
                case "TerrainData":
                    return specificTypeSettings.terrainData ? specificTypeSettings.terrainDataFolder : null;
                default:
                    return specificTypeSettings.other ? specificTypeSettings.otherFolder : null;
            }
        }

        private string GetRelativeAssetPath(string fullPath)
        {
            if (fullPath.StartsWith("Assets/"))
            {
                return fullPath;
            }
            
            return Path.GetFileName(fullPath);
        }

        private string SanitizeFolderName(string folderName)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                folderName = folderName.Replace(c, '_');
            }
            return folderName;
        }

        private void ExportAsUnityPackage()
        {
            var filteredAssets = GetFilteredAssets();
            if (filteredAssets.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "没有资产可导出", "确定");
                return;
            }

            string packagePath = EditorUtility.SaveFilePanel("导出Unity包", "", 
                $"SceneAssets_{SceneManager.GetActiveScene().name}_{DateTime.Now:yyyyMMdd_HHmmss}", "unitypackage");
            
            if (string.IsNullOrEmpty(packagePath)) return;

            // 获取当前场景路径
            string currentScenePath = SceneManager.GetActiveScene().path;
            List<string> assetPaths = new List<string>(filteredAssets.Select(a => a.Path));
            
            // 添加当前场景到导出列表（如果场景已保存）
            if (!string.IsNullOrEmpty(currentScenePath) && File.Exists(currentScenePath))
            {
                if (!assetPaths.Contains(currentScenePath))
                {
                    assetPaths.Add(currentScenePath);
                    Debug.Log($"包含场景文件到Unity包: {currentScenePath}");
                }
                
                // 同时包含场景的依赖项
                string[] sceneDependencies = AssetDatabase.GetDependencies(currentScenePath, true);
                foreach (string dependency in sceneDependencies)
                {
                    if (!assetPaths.Contains(dependency) && File.Exists(dependency))
                    {
                        // 排除URP资产
                        if (specificTypeSettings.excludeURPAssets && IsURPAsset(dependency))
                        {
                            continue;
                        }
                        assetPaths.Add(dependency);
                    }
                }
            }
            else
            {
                Debug.LogWarning("当前场景未保存，无法包含在Unity包中");
            }
            
            try
            {
                EditorUtility.DisplayProgressBar("导出Unity包", "正在打包资产...", 0.5f);
                AssetDatabase.ExportPackage(assetPaths.ToArray(), packagePath, ExportPackageOptions.Recurse);
                EditorUtility.ClearProgressBar();
                
                // 显示导出统计信息
                string message = $"Unity包已导出到: {packagePath}\n" +
                                $"包含 {filteredAssets.Count} 个资产";
                
                if (!string.IsNullOrEmpty(currentScenePath) && File.Exists(currentScenePath))
                {
                    message += $"\n包含场景: {Path.GetFileNameWithoutExtension(currentScenePath)}";
                }
                
                EditorUtility.DisplayDialog("完成", message, "确定");
                EditorUtility.RevealInFinder(packagePath);
            }
            catch (System.Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("错误", $"导出Unity包失败: {e.Message}", "确定");
            }
        }

        private void GenerateReport()
        {
            var filteredAssets = GetFilteredAssets();
            string report = $"场景资产报告 - {SceneManager.GetActiveScene().name}\n";
            report += $"生成时间: {System.DateTime.Now}\n";
            report += $"资产总数: {filteredAssets.Count}\n";
            report += $"总大小: {FormatFileSize(filteredAssets.Sum(a => a.FileSize))}\n\n";

            var groupedByType = filteredAssets.GroupBy(a => GetDisplayType(a))
                                            .OrderByDescending(g => g.Count());

            foreach (var group in groupedByType)
            {
                long typeSize = group.Sum(a => a.FileSize);
                report += $"{group.Key} ({group.Count()}个, {FormatFileSize(typeSize)}):\n";
                foreach (var asset in group.OrderBy(a => a.Path))
                {
                    report += $"  {asset.Path} ({FormatFileSize(asset.FileSize)})\n";
                }
                report += "\n";
            }

            string reportPath = EditorUtility.SaveFilePanel("保存报告", "", 
                $"SceneAssetsReport_{SceneManager.GetActiveScene().name}_{DateTime.Now:yyyyMMdd_HHmmss}", "txt");
            if (!string.IsNullOrEmpty(reportPath))
            {
                File.WriteAllText(reportPath, report);
                EditorUtility.DisplayDialog("完成", "报告生成成功", "确定");
                EditorUtility.RevealInFinder(reportPath);
            }
        }

        private void CopyAssetsToFolder()
        {
            if (!Directory.Exists(exportPath))
            {
                Directory.CreateDirectory(exportPath);
            }

            var filteredAssets = GetFilteredAssets();
            int copiedCount = 0;

            foreach (var asset in filteredAssets)
            {
                string sourcePath = asset.Path;
                string fileName = Path.GetFileName(sourcePath);
                string destPath = Path.Combine(exportPath, fileName);

                if (!File.Exists(destPath))
                {
                    AssetDatabase.CopyAsset(sourcePath, destPath);
                    copiedCount++;
                }
            }

            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("完成", $"已复制 {copiedCount} 个资产到 {exportPath}", "确定");
            EditorUtility.RevealInFinder(exportPath);
        }

        private void FocusAssetInProject(AssetInfo asset)
        {
            if (asset.Object != null)
            {
                Selection.activeObject = asset.Object;
                EditorGUIUtility.PingObject(asset.Object);
            }
        }

        private void LocateAssetInScene(AssetInfo asset)
        {
            var scene = SceneManager.GetActiveScene();
            var rootObjects = scene.GetRootGameObjects();
            bool found = false;

            foreach (var root in rootObjects)
            {
                var components = root.GetComponentsInChildren<Component>(true);
                foreach (var component in components)
                {
                    if (component == null) continue;

                    var serializedObject = new SerializedObject(component);
                    var property = serializedObject.GetIterator();

                    while (property.NextVisible(true))
                    {
                        if (property.propertyType == SerializedPropertyType.ObjectReference)
                        {
                            var referencedObject = property.objectReferenceValue;
                            if (referencedObject != null && AssetDatabase.GetAssetPath(referencedObject) == asset.Path)
                            {
                                Selection.activeObject = component.gameObject;
                                EditorGUIUtility.PingObject(component.gameObject);
                                found = true;
                                
                                if (SceneView.lastActiveSceneView != null)
                                {
                                    SceneView.lastActiveSceneView.FrameSelected();
                                }
                                break;
                            }
                        }
                    }
                    if (found) break;
                }
                if (found) break;
            }

            if (!found)
            {
                EditorUtility.DisplayDialog("提示", "未在场景中找到使用此资产的对象", "确定");
            }
        }

        public void HighlightAsset(string assetPath)
        {
            searchFilter = Path.GetFileName(assetPath);
            RefreshAssets();
        }

        private void DebugPrefabRecognition()
        {
            var prefabAssets = foundAssets.Where(a => a.Path.ToLower().EndsWith(".prefab")).ToList();
            Debug.Log($"找到 {prefabAssets.Count} 个预制体文件:");
            
            foreach (var prefab in prefabAssets)
            {
                string displayType = GetDisplayType(prefab);
                Debug.Log($"预制体: {prefab.Path}, 识别类型: {displayType}, 文件存在: {File.Exists(prefab.Path)}");
            }
            
            Debug.Log("预制体类型过滤器状态: " + (assetTypeFilters.ContainsKey("Prefab") ? assetTypeFilters["Prefab"] : "未找到"));
            
            EditorUtility.DisplayDialog("预制体调试", $"找到 {prefabAssets.Count} 个预制体文件，请查看控制台日志", "确定");
        }

        private void AnalyzeHLSLPathIssue()
        {
            Debug.Log("=== HLSL路径问题分析 ===");
            
            if (shaderToHLSLMap.Count == 0)
            {
                Debug.Log("没有找到Shader到HLSL的映射");
                return;
            }
            
            foreach (var kvp in shaderToHLSLMap)
            {
                string shaderPath = kvp.Key;
                Debug.Log($"分析Shader: {shaderPath}");
                
                string shaderExportPath = GetShaderExportPath(customExportPath, shaderPath);
                Debug.Log($"  Shader导出路径: {shaderExportPath}");
                
                foreach (HLSLReference hlslRef in kvp.Value)
                {
                    Debug.Log($"  HLSL引用:");
                    Debug.Log($"    HLSL路径: {hlslRef.hlslPath}");
                    Debug.Log($"    Include路径: {hlslRef.includePath}");
                    
                    string expectedHLSLPath = GetHLSLDestinationPathForShader(customExportPath, 
                        new AssetInfo { Path = hlslRef.hlslPath, Type = typeof(TextAsset) }, 
                        hlslRef.hlslPath, shaderPath);
                    
                    Debug.Log($"    HLSL期望导出路径: {expectedHLSLPath}");
                    
                    string shaderExportDir = Path.GetDirectoryName(shaderExportPath);
                    string hlslExportDir = Path.GetDirectoryName(expectedHLSLPath);
                    
                    Debug.Log($"    Shader导出目录: {shaderExportDir}");
                    Debug.Log($"    HLSL导出目录: {hlslExportDir}");
                    
                    if (shaderExportDir != null && hlslExportDir != null && 
                        hlslExportDir.StartsWith(customExportPath) && 
                        shaderExportDir.StartsWith(customExportPath))
                    {
                        string relativePath = GetRelativePath(shaderExportDir, hlslExportDir);
                        Debug.Log($"    相对路径关系: {relativePath}");
                    }
                }
            }
        }

        private string GetRelativePath(string fromPath, string toPath)
        {
            try
            {
                Uri fromUri = new Uri(fromPath + Path.DirectorySeparatorChar);
                Uri toUri = new Uri(toPath + Path.DirectorySeparatorChar);
                
                Uri relativeUri = fromUri.MakeRelativeUri(toUri);
                string relativePath = Uri.UnescapeDataString(relativeUri.ToString());
                
                return relativePath.Replace('/', Path.DirectorySeparatorChar);
            }
            catch
            {
                return "无法计算相对路径";
            }
        }

        private void TestHLSLExtraction()
        {
            string testShaderPath = EditorUtility.OpenFilePanel("选择Shader文件测试HLSL提取", "Assets", "shader,compute");
            if (!string.IsNullOrEmpty(testShaderPath))
            {
                if (testShaderPath.StartsWith(Application.dataPath))
                {
                    testShaderPath = "Assets" + testShaderPath.Substring(Application.dataPath.Length);
                }

                var testObjects = new HashSet<UnityEngine.Object>();
                var originalMap = new Dictionary<string, List<HLSLReference>>(shaderToHLSLMap);
                var originalProcessed = new HashSet<string>(processedHLSLFiles);
                
                shaderToHLSLMap.Clear();
                processedHLSLFiles.Clear();
                
                if (testShaderPath.EndsWith(".shader"))
                {
                    ExtractHLSLReferencesFromShader(testShaderPath, testObjects);
                }
                else if (testShaderPath.EndsWith(".compute"))
                {
                    ExtractHLSLReferencesFromComputeShader(testShaderPath, testObjects);
                }

                if (testObjects.Count > 0)
                {
                    Debug.Log($"测试完成，找到 {testObjects.Count} 个HLSL文件:");
                    foreach (var obj in testObjects)
                    {
                        string path = AssetDatabase.GetAssetPath(obj);
                        Debug.Log($"  - {path} (存在: {File.Exists(path)})");
                    }
                    
                    if (shaderToHLSLMap.ContainsKey(testShaderPath))
                    {
                        Debug.Log("映射关系:");
                        foreach (var hlslRef in shaderToHLSLMap[testShaderPath])
                        {
                            Debug.Log($"  {hlslRef.includePath} -> {hlslRef.hlslPath}");
                        }
                    }
                    
                    EditorUtility.DisplayDialog("测试完成", $"找到 {testObjects.Count} 个HLSL文件，请查看控制台日志", "确定");
                }
                else
                {
                    Debug.Log("测试完成，但没有找到HLSL文件");
                    EditorUtility.DisplayDialog("测试完成", "没有找到HLSL文件", "确定");
                }
                
                shaderToHLSLMap = originalMap;
                processedHLSLFiles = originalProcessed;
            }
        }

        private void ShowHLSLMapping()
        {
            if (shaderToHLSLMap.Count > 0)
            {
                Debug.Log($"HLSL映射关系 ({shaderToHLSLMap.Count} 个Shader):");
                foreach (var kvp in shaderToHLSLMap)
                {
                    Debug.Log($"Shader: {kvp.Key}");
                    foreach (HLSLReference hlslRef in kvp.Value)
                    {
                        Debug.Log($"  -> HLSL: {hlslRef.hlslPath} (include: {hlslRef.includePath})");
                    }
                }
                EditorUtility.DisplayDialog("HLSL映射", $"找到 {shaderToHLSLMap.Count} 个Shader的HLSL引用，请查看控制台日志", "确定");
            }
            else
            {
                EditorUtility.DisplayDialog("HLSL映射", "没有找到HLSL映射关系", "确定");
            }
        }

        private void FixHLSLPaths()
        {
            Debug.Log("=== 开始修复HLSL路径 ===");
            
            foreach (var kvp in shaderToHLSLMap)
            {
                string shaderPath = kvp.Key;
                Debug.Log($"检查Shader: {shaderPath}");
                
                foreach (HLSLReference hlslRef in kvp.Value)
                {
                    Debug.Log($"  HLSL引用: {hlslRef.includePath} -> {hlslRef.hlslPath}");
                    
                    string expectedDir = Path.GetDirectoryName(shaderPath);
                    string expectedPath = Path.Combine(expectedDir, hlslRef.includePath).Replace("\\", "/");
                    
                    bool pathMatches = hlslRef.hlslPath.Equals(expectedPath, StringComparison.OrdinalIgnoreCase);
                    
                    Debug.Log($"    期望路径: {expectedPath}");
                    Debug.Log($"    实际路径: {hlslRef.hlslPath}");
                    Debug.Log($"    路径匹配: {pathMatches}");
                    
                    if (!pathMatches)
                    {
                        Debug.LogWarning($"    ⚠️ 路径不匹配！Shader期望HLSL在: {expectedPath}，但实际在: {hlslRef.hlslPath}");
                    }
                }
            }
            
            Debug.Log("=== HLSL使用情况分析 ===");
            foreach (var kvp in hlslUsageMap)
            {
                string hlslPath = kvp.Key;
                List<string> shaders = kvp.Value;
                
                Debug.Log($"HLSL文件: {hlslPath}");
                Debug.Log($"  被 {shaders.Count} 个Shader使用:");
                
                foreach (string shader in shaders)
                {
                    Debug.Log($"    - {shader}");
                    
                    if (shaderToHLSLMap.ContainsKey(shader))
                    {
                        var hlslRef = shaderToHLSLMap[shader].FirstOrDefault(r => r.hlslPath == hlslPath);
                        if (hlslRef != null)
                        {
                            string expectedDir = Path.GetDirectoryName(shader);
                            string expectedPath = Path.Combine(expectedDir, hlslRef.includePath).Replace("\\", "/");
                            
                            bool matches = hlslPath.Equals(expectedPath, StringComparison.OrdinalIgnoreCase);
                            Debug.Log($"      Include路径: {hlslRef.includePath}");
                            Debug.Log($"      期望位置: {expectedPath}");
                            Debug.Log($"      实际位置: {hlslPath}");
                            Debug.Log($"      路径匹配: {matches}");
                            
                            if (!matches)
                            {
                                Debug.LogWarning($"      💡 建议: 在导出时启用'多位置副本'选项，或手动将HLSL文件复制到: {expectedPath}");
                            }
                        }
                    }
                }
            }
            
            EditorUtility.DisplayDialog("HLSL路径分析完成", "请查看控制台日志了解详细的路径分析结果。建议启用'多位置副本'选项来解决路径不匹配问题。", "确定");
        }
        
        private void DebugScriptRecognition()
        {
            var scriptAssets = foundAssets.Where(a => GetDisplayType(a) == "Script").ToList();
            Debug.Log($"找到 {scriptAssets.Count} 个脚本文件:");
    
            foreach (var script in scriptAssets)
            {
                string displayType = GetDisplayType(script);
                Debug.Log($"脚本: {script.Path}, 识别类型: {displayType}, 文件存在: {File.Exists(script.Path)}, 实际类型: {script.Type.Name}");
        
                // 检查脚本类型过滤器状态
                bool isFiltered = assetTypeFilters.ContainsKey("Script") ? assetTypeFilters["Script"] : false;
                Debug.Log($"脚本类型过滤器状态: {isFiltered}");
            }
    
            // 检查场景中所有的MonoBehaviour组件
            MonoBehaviour[] allMonoBehaviours = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
            Debug.Log($"场景中找到 {allMonoBehaviours.Length} 个MonoBehaviour组件");
    
            foreach (MonoBehaviour mb in allMonoBehaviours)
            {
                if (mb == null) continue;
        
                System.Type scriptType = mb.GetType();
                MonoScript monoScript = MonoScript.FromMonoBehaviour(mb);
        
                if (monoScript != null)
                {
                    string scriptPath = AssetDatabase.GetAssetPath(monoScript);
                    Debug.Log($"MonoBehaviour: {mb.GetType().Name} -> 脚本: {monoScript.name} - 路径: {scriptPath}");
                }
                else
                {
                    Debug.Log($"MonoBehaviour: {mb.GetType().Name} -> 无法获取MonoScript");
                }
            }
    
            EditorUtility.DisplayDialog("脚本调试", $"找到 {scriptAssets.Count} 个脚本文件，请查看控制台日志", "确定");
        }

        private void LoadSettings()
        {
            customExportPath = EditorPrefs.GetString("SceneAssetExtractor_CustomExportPath", "");
            exportByType = EditorPrefs.GetBool("SceneAssetExtractor_ExportByType", true);
            exportBySpecificType = EditorPrefs.GetBool("SceneAssetExtractor_ExportBySpecificType", true);
            preserveFolderStructure = EditorPrefs.GetBool("SceneAssetExtractor_PreserveFolderStructure", false);
            exportFormat = (ExportFormat)EditorPrefs.GetInt("SceneAssetExtractor_ExportFormat", 0);
            
            specificTypeSettings.texture2D = EditorPrefs.GetBool("SceneAssetExtractor_Texture2D", true);
            specificTypeSettings.material = EditorPrefs.GetBool("SceneAssetExtractor_Material", true);
            specificTypeSettings.mesh = EditorPrefs.GetBool("SceneAssetExtractor_Mesh", true);
            specificTypeSettings.gameObject = EditorPrefs.GetBool("SceneAssetExtractor_GameObject", true);
            specificTypeSettings.audioClip = EditorPrefs.GetBool("SceneAssetExtractor_AudioClip", true);
            specificTypeSettings.animationClip = EditorPrefs.GetBool("SceneAssetExtractor_AnimationClip", true);
            specificTypeSettings.animatorController = EditorPrefs.GetBool("SceneAssetExtractor_AnimatorController", true);
            specificTypeSettings.shader = EditorPrefs.GetBool("SceneAssetExtractor_Shader", true);
            specificTypeSettings.script = EditorPrefs.GetBool("SceneAssetExtractor_Script", true);
            specificTypeSettings.font = EditorPrefs.GetBool("SceneAssetExtractor_Font", true);
            specificTypeSettings.textAsset = EditorPrefs.GetBool("SceneAssetExtractor_TextAsset", true);
            specificTypeSettings.prefab = EditorPrefs.GetBool("SceneAssetExtractor_Prefab", true);
            specificTypeSettings.other = EditorPrefs.GetBool("SceneAssetExtractor_Other", true);
            
            // 新增地形设置
            specificTypeSettings.terrain = EditorPrefs.GetBool("SceneAssetExtractor_Terrain", true);
            specificTypeSettings.terrainData = EditorPrefs.GetBool("SceneAssetExtractor_TerrainData", true);
            specificTypeSettings.terrainFolder = EditorPrefs.GetString("SceneAssetExtractor_TerrainFolder", "Terrains");
            specificTypeSettings.terrainDataFolder = EditorPrefs.GetString("SceneAssetExtractor_TerrainDataFolder", "TerrainData");
            
            specificTypeSettings.hlslFile = EditorPrefs.GetBool("SceneAssetExtractor_HLSLFile", true);
            specificTypeSettings.extractShaderIncludes = EditorPrefs.GetBool("SceneAssetExtractor_ExtractShaderIncludes", true);
            specificTypeSettings.preserveHLSLFolderStructure = EditorPrefs.GetBool("SceneAssetExtractor_PreserveHLSLFolderStructure", true);
            specificTypeSettings.createHLSLSymlinks = EditorPrefs.GetBool("SceneAssetExtractor_CreateHLSLSymlinks", false);
            specificTypeSettings.excludeURPAssets = EditorPrefs.GetBool("SceneAssetExtractor_ExcludeURPAssets", true);
            
            specificTypeSettings.texture2DFolder = EditorPrefs.GetString("SceneAssetExtractor_Texture2DFolder", "Textures");
            specificTypeSettings.materialFolder = EditorPrefs.GetString("SceneAssetExtractor_MaterialFolder", "Materials");
            specificTypeSettings.meshFolder = EditorPrefs.GetString("SceneAssetExtractor_MeshFolder", "Meshes");
            specificTypeSettings.gameObjectFolder = EditorPrefs.GetString("SceneAssetExtractor_GameObjectFolder", "GameObjects");
            specificTypeSettings.audioClipFolder = EditorPrefs.GetString("SceneAssetExtractor_AudioClipFolder", "Audio");
            specificTypeSettings.animationClipFolder = EditorPrefs.GetString("SceneAssetExtractor_AnimationClipFolder", "Animations");
            specificTypeSettings.animatorControllerFolder = EditorPrefs.GetString("SceneAssetExtractor_AnimatorControllerFolder", "Animators");
            specificTypeSettings.shaderFolder = EditorPrefs.GetString("SceneAssetExtractor_ShaderFolder", "Shaders");
            specificTypeSettings.scriptFolder = EditorPrefs.GetString("SceneAssetExtractor_ScriptFolder", "Scripts");
            specificTypeSettings.fontFolder = EditorPrefs.GetString("SceneAssetExtractor_FontFolder", "Fonts");
            specificTypeSettings.textAssetFolder = EditorPrefs.GetString("SceneAssetExtractor_TextAssetFolder", "TextAssets");
            specificTypeSettings.prefabFolder = EditorPrefs.GetString("SceneAssetExtractor_PrefabFolder", "Prefabs");
            specificTypeSettings.otherFolder = EditorPrefs.GetString("SceneAssetExtractor_OtherFolder", "Other");
        }

        private void SaveSettings()
        {
            EditorPrefs.SetString("SceneAssetExtractor_CustomExportPath", customExportPath);
            EditorPrefs.SetBool("SceneAssetExtractor_ExportByType", exportByType);
            EditorPrefs.SetBool("SceneAssetExtractor_ExportBySpecificType", exportBySpecificType);
            EditorPrefs.SetBool("SceneAssetExtractor_PreserveFolderStructure", preserveFolderStructure);
            EditorPrefs.SetInt("SceneAssetExtractor_ExportFormat", (int)exportFormat);
            
            EditorPrefs.SetBool("SceneAssetExtractor_Texture2D", specificTypeSettings.texture2D);
            EditorPrefs.SetBool("SceneAssetExtractor_Material", specificTypeSettings.material);
            EditorPrefs.SetBool("SceneAssetExtractor_Mesh", specificTypeSettings.mesh);
            EditorPrefs.SetBool("SceneAssetExtractor_GameObject", specificTypeSettings.gameObject);
            EditorPrefs.SetBool("SceneAssetExtractor_AudioClip", specificTypeSettings.audioClip);
            EditorPrefs.SetBool("SceneAssetExtractor_AnimationClip", specificTypeSettings.animationClip);
            EditorPrefs.SetBool("SceneAssetExtractor_AnimatorController", specificTypeSettings.animatorController);
            EditorPrefs.SetBool("SceneAssetExtractor_Shader", specificTypeSettings.shader);
            EditorPrefs.SetBool("SceneAssetExtractor_Script", specificTypeSettings.script);
            EditorPrefs.SetBool("SceneAssetExtractor_Font", specificTypeSettings.font);
            EditorPrefs.SetBool("SceneAssetExtractor_TextAsset", specificTypeSettings.textAsset);
            EditorPrefs.SetBool("SceneAssetExtractor_Prefab", specificTypeSettings.prefab);
            EditorPrefs.SetBool("SceneAssetExtractor_Other", specificTypeSettings.other);
            
            // 新增地形设置
            EditorPrefs.SetBool("SceneAssetExtractor_Terrain", specificTypeSettings.terrain);
            EditorPrefs.SetBool("SceneAssetExtractor_TerrainData", specificTypeSettings.terrainData);
            EditorPrefs.SetString("SceneAssetExtractor_TerrainFolder", specificTypeSettings.terrainFolder);
            EditorPrefs.SetString("SceneAssetExtractor_TerrainDataFolder", specificTypeSettings.terrainDataFolder);
            
            EditorPrefs.SetBool("SceneAssetExtractor_HLSLFile", specificTypeSettings.hlslFile);
            EditorPrefs.SetBool("SceneAssetExtractor_ExtractShaderIncludes", specificTypeSettings.extractShaderIncludes);
            EditorPrefs.SetBool("SceneAssetExtractor_PreserveHLSLFolderStructure", specificTypeSettings.preserveHLSLFolderStructure);
            EditorPrefs.SetBool("SceneAssetExtractor_CreateHLSLSymlinks", specificTypeSettings.createHLSLSymlinks);
            EditorPrefs.SetBool("SceneAssetExtractor_ExcludeURPAssets", specificTypeSettings.excludeURPAssets);
            
            EditorPrefs.SetString("SceneAssetExtractor_Texture2DFolder", specificTypeSettings.texture2DFolder);
            EditorPrefs.SetString("SceneAssetExtractor_MaterialFolder", specificTypeSettings.materialFolder);
            EditorPrefs.SetString("SceneAssetExtractor_MeshFolder", specificTypeSettings.meshFolder);
            EditorPrefs.SetString("SceneAssetExtractor_GameObjectFolder", specificTypeSettings.gameObjectFolder);
            EditorPrefs.SetString("SceneAssetExtractor_AudioClipFolder", specificTypeSettings.audioClipFolder);
            EditorPrefs.SetString("SceneAssetExtractor_AnimationClipFolder", specificTypeSettings.animationClipFolder);
            EditorPrefs.SetString("SceneAssetExtractor_AnimatorControllerFolder", specificTypeSettings.animatorControllerFolder);
            EditorPrefs.SetString("SceneAssetExtractor_ShaderFolder", specificTypeSettings.shaderFolder);
            EditorPrefs.SetString("SceneAssetExtractor_ScriptFolder", specificTypeSettings.scriptFolder);
            EditorPrefs.SetString("SceneAssetExtractor_FontFolder", specificTypeSettings.fontFolder);
            EditorPrefs.SetString("SceneAssetExtractor_TextAssetFolder", specificTypeSettings.textAssetFolder);
            EditorPrefs.SetString("SceneAssetExtractor_PrefabFolder", specificTypeSettings.prefabFolder);
            EditorPrefs.SetString("SceneAssetExtractor_OtherFolder", specificTypeSettings.otherFolder);
        }

        private void ExportAssetsToFolder(List<AssetInfo> assets, string exportFolder)
        {
            if (!Directory.Exists(exportFolder))
            {
                Directory.CreateDirectory(exportFolder);
            }

            int exportedCount = 0;
            int totalCount = assets.Count;
            
            for (int i = 0; i < totalCount; i++)
            {
                var asset = assets[i];
                float progress = (float)i / totalCount;
                EditorUtility.DisplayProgressBar("快速导出", $"正在导出 {asset.Path} ({i+1}/{totalCount})", progress);

                string sourcePath = asset.Path;
                
                if (!File.Exists(sourcePath))
                {
                    Debug.LogWarning($"源文件不存在: {sourcePath}");
                    continue;
                }

                string destPath = GetDestinationPath(exportFolder, asset, sourcePath);
                
                try
                {
                    string destDirectory = Path.GetDirectoryName(destPath);
                    if (!Directory.Exists(destDirectory))
                    {
                        Directory.CreateDirectory(destDirectory);
                    }

                    File.Copy(sourcePath, destPath, true);
                    
                    if (File.Exists(sourcePath + ".meta"))
                    {
                        File.Copy(sourcePath + ".meta", destPath + ".meta", true);
                    }
                    
                    exportedCount++;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"导出失败 {sourcePath}: {e.Message}");
                    Debug.LogError($"StackTrace: {e.StackTrace}");
                }
            }

            EditorUtility.ClearProgressBar();
            
            if (exportedCount > 0)
            {
                EditorUtility.DisplayDialog("完成", $"成功导出 {exportedCount} 个资产到 {exportFolder}", "确定");
                EditorUtility.RevealInFinder(exportFolder);
            }
            else
            {
                EditorUtility.DisplayDialog("错误", "没有成功导出任何资产，请查看控制台日志", "确定");
            }
        }
    }
}