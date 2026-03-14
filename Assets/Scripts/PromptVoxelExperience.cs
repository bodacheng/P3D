using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class PromptVoxelExperience : MonoBehaviour
{
    private const float CubeSize = 0.35f;
    private const int PrimaryColor = 0;
    private const int SecondaryColor = 1;
    private const int AccentColor = 2;
    private const int DetailColor = 3;
    private const int GlowColor = 4;
    private const int GroundColor = 5;

    private static readonly string[] SamplePrompts =
    {
        "浮空城堡",
        "樱花树屋",
        "赛博飞船",
        "机械机器人",
        "蘑菇小屋",
        "森林小岛",
        "猫咪雕像",
        "冰雪高塔",
        "沙漠要塞"
    };

    private static readonly string[] TreeKeywords = { "tree", "forest", "sakura", "blossom", "树", "樹", "森林", "樱", "樱花" };
    private static readonly string[] HouseKeywords = { "house", "home", "cabin", "hut", "屋", "房", "小屋", "木屋" };
    private static readonly string[] CastleKeywords = { "castle", "fort", "keep", "tower", "城堡", "要塞", "高塔", "塔" };
    private static readonly string[] RobotKeywords = { "robot", "mecha", "android", "机器人", "机甲", "机械" };
    private static readonly string[] SpaceshipKeywords = { "ship", "spaceship", "rocket", "jet", "飞船", "战舰", "火箭", "太空" };
    private static readonly string[] MushroomKeywords = { "mushroom", "fungi", "蘑菇" };
    private static readonly string[] IslandKeywords = { "island", "floating", "sky", "岛", "浮空", "空岛", "小岛" };
    private static readonly string[] CatKeywords = { "cat", "kitty", "猫", "猫咪" };
    private const int MaxRuntimeLogs = 48;

    private readonly List<Mesh> generatedMeshes = new List<Mesh>();
    private readonly List<Material> generatedMaterials = new List<Material>();
    private readonly List<Texture2D> generatedTextures = new List<Texture2D>();
    private readonly List<string> runtimeLogs = new List<string>();
    private readonly System.Random promptPicker = new System.Random(unchecked((int)DateTime.Now.Ticks));

    private Camera sceneCamera;
    private Light sceneLight;
    private Transform modelPivot;
    private GameObject currentModel;
    private GameObject stageDisk;
    private Material stageMaterial;
    private Mesh cubeMesh;
    private Shader litShader;

    private GUIStyle panelStyle;
    private GUIStyle titleStyle;
    private GUIStyle bodyStyle;
    private GUIStyle inputStyle;
    private GUIStyle buttonStyle;
    private GUIStyle chipStyle;
    private GUIStyle subTitleStyle;
    private GUIStyle sectionToggleStyle;
    private GUIStyle headerButtonStyle;
    private GUIStyle logPanelStyle;
    private GUIStyle logStyle;

    private Vector2 controlScrollPosition;
    private string promptInput = string.Empty;
    private string lastGeneratedPrompt = string.Empty;
    private string generationSummary = string.Empty;
    private PromptProfile lastProfile;
    private int generationIndex;
    private bool useOnlinePlanner;
    private bool isOnlineRequestRunning;
    private int onlineRequestSerial;
    private OpenAIVoxelProjectSettings projectSettings;
    private string openAiApiKey = string.Empty;
    private string openAiModel = OpenAIVoxelBlueprintClient.DefaultModel;
    private string plannerStatus = "本地关键词生成已启用。";
    private string lastPlannerSource = "本地";
    private Vector2 logScrollPosition;
    private bool shouldScrollLogToBottom;
    private float lastGuiScale = -1f;
    private bool lastGuiWasPortrait;
    private bool layoutStateInitialized;
    private bool controlsExpanded = true;
    private bool logPanelExpanded = true;
    private bool onlineStatusExpanded = true;
    private bool suggestionsExpanded = true;
    private bool resultExpanded = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<PromptVoxelExperience>() != null)
        {
            return;
        }

        GameObject controller = new GameObject("Prompt Voxel Experience");
        controller.AddComponent<PromptVoxelExperience>();
    }

    private void Awake()
    {
        litShader = Shader.Find("Universal Render Pipeline/Lit");
        if (litShader == null)
        {
            litShader = Shader.Find("Standard");
        }

        LoadOnlineSettings();
        CacheCubeMesh();
        SetupScene();
        AppendLog("组件已启动。等待生成模型。");
    }

    private void Start()
    {
        if (string.IsNullOrWhiteSpace(promptInput))
        {
            promptInput = PickRandomPrompt();
        }

        GenerateModel();
    }

    private void Update()
    {
        if (modelPivot != null)
        {
            modelPivot.Rotate(Vector3.up, 18f * Time.deltaTime, Space.World);
        }
    }

    private void OnDestroy()
    {
        CleanupGeneratedModel();
        if (stageMaterial != null)
        {
            Destroy(stageMaterial);
        }
        CleanupGuiTextures();
        if (stageDisk != null)
        {
            Destroy(stageDisk);
        }
    }

    private void OnGUI()
    {
        EnsureLayoutState();
        EnsureGuiStyles();

        bool isPortrait = IsPortraitLayout();
        bool stackPanels = isPortrait || Screen.width < 1180;
        float uiScale = GetUiScale(isPortrait);
        Rect panelRect = CalculateControlPanelRect(stackPanels, isPortrait, uiScale);

        GUILayout.BeginArea(panelRect, GUIContent.none, panelStyle);
        DrawPanelHeader(
            "Voxel Prompt Lab",
            "输入提示词后，场景会随机生成一个由大量小立方体拼出的 3D 模型。",
            ref controlsExpanded,
            titleStyle,
            uiScale);

        if (controlsExpanded)
        {
            GUILayout.Space(8f);
            controlScrollPosition = GUILayout.BeginScrollView(controlScrollPosition, false, true, GUILayout.ExpandHeight(true));
            DrawControlPanelContent(panelRect.width, isPortrait, uiScale);
            GUILayout.EndScrollView();
        }

        GUILayout.EndArea();

        DrawRuntimeLogPanel(panelRect, stackPanels, isPortrait, uiScale);
    }

    private void DrawRuntimeLogPanel(Rect mainPanelRect, bool stackPanels, bool isPortrait, float uiScale)
    {
        EnsureGuiStyles();

        Rect logRect = CalculateLogPanelRect(mainPanelRect, stackPanels, isPortrait, uiScale);
        GUILayout.BeginArea(logRect, GUIContent.none, logPanelStyle);
        DrawPanelHeader("运行日志 (" + runtimeLogs.Count + ")", null, ref logPanelExpanded, subTitleStyle, uiScale);

        if (logPanelExpanded)
        {
            GUILayout.Space(6f);
            if (runtimeLogs.Count == 0)
            {
                GUILayout.Label("还没有运行日志。", bodyStyle);
            }
            else
            {
                logScrollPosition = GUILayout.BeginScrollView(logScrollPosition, false, true, GUILayout.ExpandHeight(true));
                for (int index = 0; index < runtimeLogs.Count; index++)
                {
                    GUILayout.Label(runtimeLogs[index], logStyle);
                }
                GUILayout.EndScrollView();
            }
        }

        GUILayout.EndArea();

        if (shouldScrollLogToBottom)
        {
            logScrollPosition.y = 100000f;
            shouldScrollLogToBottom = false;
        }
    }

    private void DrawControlPanelContent(float panelWidth, bool isPortrait, float uiScale)
    {
        float inputHeight = Mathf.RoundToInt(56f * uiScale);
        float buttonHeight = Mathf.RoundToInt(44f * uiScale);

        GUILayout.Label("提示词", subTitleStyle);
        GUI.SetNextControlName("PromptField");
        promptInput = GUILayout.TextArea(
            promptInput,
            64,
            inputStyle,
            GUILayout.MinHeight(inputHeight),
            GUILayout.MaxHeight(inputHeight * 1.6f));

        GUILayout.Space(10f);
        DrawActionButtons(panelWidth, buttonHeight);

        GUILayout.Space(14f);
        onlineStatusExpanded = DrawSectionToggle("在线理解", onlineStatusExpanded, uiScale);
        if (onlineStatusExpanded)
        {
            GUILayout.Space(6f);
            GUILayout.Label(useOnlinePlanner ? "项目预设: 已启用" : "项目预设: 未启用", bodyStyle);
            GUILayout.Label("配置文件: Assets/Resources/OpenAIVoxelProjectSettings.asset", bodyStyle);
            GUILayout.Label("当前模型: " + openAiModel, bodyStyle);
            GUILayout.Label(plannerStatus, bodyStyle);
        }

        GUILayout.Space(12f);
        suggestionsExpanded = DrawSectionToggle("推荐提示词", suggestionsExpanded, uiScale);
        if (suggestionsExpanded)
        {
            GUILayout.Space(6f);
            DrawPromptChips(panelWidth, isPortrait, uiScale);
        }

        GUILayout.Space(12f);
        resultExpanded = DrawSectionToggle("当前结果", resultExpanded, uiScale);
        if (resultExpanded)
        {
            GUILayout.Space(6f);
            if (!string.IsNullOrEmpty(lastGeneratedPrompt))
            {
                GUILayout.Label("Prompt: " + lastGeneratedPrompt, bodyStyle);
                GUILayout.Label("来源: " + lastPlannerSource, bodyStyle);
                GUILayout.Label(generationSummary, bodyStyle);
            }
            else
            {
                GUILayout.Label("还没有生成模型。", bodyStyle);
            }
        }

        GUILayout.Space(10f);
        GUILayout.Label("每次生成都会保留提示词语义，但会随机变化体块比例、细节和配色层次。", bodyStyle);
    }

    private void DrawActionButtons(float panelWidth, float buttonHeight)
    {
        bool stackButtons = panelWidth < 680f;
        bool previousEnabled = GUI.enabled;
        GUI.enabled = !isOnlineRequestRunning;

        if (stackButtons)
        {
            if (GUILayout.Button(isOnlineRequestRunning ? "理解中..." : "随机提示词", buttonStyle, GUILayout.Height(buttonHeight), GUILayout.ExpandWidth(true)))
            {
                TriggerGenerationFromUi(true);
            }

            GUILayout.Space(6f);
            if (GUILayout.Button(isOnlineRequestRunning ? "请稍候" : "生成模型", buttonStyle, GUILayout.Height(buttonHeight), GUILayout.ExpandWidth(true)))
            {
                TriggerGenerationFromUi(false);
            }

            GUILayout.Space(6f);
            if (GUILayout.Button(isOnlineRequestRunning ? "请求中" : "同词换造型", buttonStyle, GUILayout.Height(buttonHeight), GUILayout.ExpandWidth(true)))
            {
                TriggerGenerationFromUi(false);
            }
        }
        else
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(isOnlineRequestRunning ? "理解中..." : "随机提示词", buttonStyle, GUILayout.Height(buttonHeight), GUILayout.ExpandWidth(true)))
            {
                TriggerGenerationFromUi(true);
            }

            GUILayout.Space(8f);
            if (GUILayout.Button(isOnlineRequestRunning ? "请稍候" : "生成模型", buttonStyle, GUILayout.Height(buttonHeight), GUILayout.ExpandWidth(true)))
            {
                TriggerGenerationFromUi(false);
            }

            GUILayout.Space(8f);
            if (GUILayout.Button(isOnlineRequestRunning ? "请求中" : "同词换造型", buttonStyle, GUILayout.Height(buttonHeight), GUILayout.ExpandWidth(true)))
            {
                TriggerGenerationFromUi(false);
            }
            GUILayout.EndHorizontal();
        }

        GUI.enabled = previousEnabled;
    }

    private void TriggerGenerationFromUi(bool pickRandomPrompt)
    {
        if (isOnlineRequestRunning)
        {
            return;
        }

        if (pickRandomPrompt)
        {
            promptInput = PickRandomPrompt();
        }

        GUI.FocusControl(string.Empty);
        GenerateModel();
    }

    private void DrawPromptChips(float panelWidth, bool isPortrait, float uiScale)
    {
        int columns = isPortrait ? 2 : (panelWidth > 720f ? 3 : 2);
        float spacing = 8f;
        float chipHeight = Mathf.RoundToInt(38f * uiScale);
        float availableWidth = Mathf.Max(200f, panelWidth - 64f);
        float width = (availableWidth - spacing * (columns - 1)) / columns;
        int index = 0;
        bool previousEnabled = GUI.enabled;
        GUI.enabled = !isOnlineRequestRunning;

        while (index < SamplePrompts.Length)
        {
            GUILayout.BeginHorizontal();
            for (int column = 0; column < columns && index < SamplePrompts.Length; column++)
            {
                string prompt = SamplePrompts[index];
                if (GUILayout.Button(prompt, chipStyle, GUILayout.Width(width), GUILayout.Height(chipHeight)))
                {
                    promptInput = prompt;
                    GUI.FocusControl(string.Empty);
                    GenerateModel();
                }

                index++;
                if (column < columns - 1)
                {
                    GUILayout.Space(spacing);
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);
        }

        GUI.enabled = previousEnabled;
    }

    private void GenerateModel()
    {
        LoadOnlineSettings();
        string cleanedPrompt = string.IsNullOrWhiteSpace(promptInput) ? PickRandomPrompt() : promptInput.Trim();
        promptInput = cleanedPrompt;
        generationIndex++;
        AppendLog("收到生成请求。Prompt: " + cleanedPrompt);

        if (useOnlinePlanner && !string.IsNullOrWhiteSpace(openAiApiKey))
        {
            int requestId = ++onlineRequestSerial;
            AppendLog("已切换到在线理解。模型: " + openAiModel + "。请求编号: " + requestId);
            StartCoroutine(GenerateModelOnline(cleanedPrompt, generationIndex, requestId));
            return;
        }

        if (useOnlinePlanner && string.IsNullOrWhiteSpace(openAiApiKey))
        {
            plannerStatus = "联网理解已开启，但没有 API Key，已自动回退到本地生成。";
            AppendLog("项目配置启用了在线理解，但 API Key 为空，自动回退到本地。");
        }

        GenerateLocalModel(cleanedPrompt, generationIndex, "本地关键词");
    }

    private IEnumerator GenerateModelOnline(string prompt, int generation, int requestId)
    {
        isOnlineRequestRunning = true;
        plannerStatus = "正在联网理解提示词，并生成 voxel 蓝图...";
        AppendLog("开始在线生成，请求编号 " + requestId + "。");

        OpenAIVoxelBlueprint blueprint = null;
        string requestError = null;

        yield return OpenAIVoxelBlueprintClient.RequestBlueprint(
            openAiApiKey,
            openAiModel,
            prompt,
            result => blueprint = result,
            error => requestError = error,
            AppendLog);

        isOnlineRequestRunning = false;

        if (requestId != onlineRequestSerial)
        {
            AppendLog("旧请求已被新的生成请求覆盖，忽略结果。请求编号: " + requestId);
            yield break;
        }

        if (blueprint == null)
        {
            plannerStatus = "在线理解失败，已回退到本地生成。原因: " + requestError;
            AppendLog("在线理解失败，准备回退本地。原因: " + requestError);
            GenerateLocalModel(prompt, generation, "本地回退");
            yield break;
        }

        if (!GenerateModelFromBlueprint(prompt, generation, blueprint))
        {
            plannerStatus = "在线蓝图不完整，已回退到本地生成。";
            AppendLog("在线蓝图验证失败，已回退本地。");
            GenerateLocalModel(prompt, generation, "本地回退");
            yield break;
        }

        plannerStatus = "在线理解完成，已按蓝图生成模型。";
        AppendLog("在线蓝图已成功落地为模型。");
    }

    private void GenerateLocalModel(string prompt, int generation, string sourceLabel)
    {
        lastProfile = InterpretPrompt(prompt, generation);
        lastGeneratedPrompt = prompt;
        lastPlannerSource = sourceLabel;
        generationSummary = BuildSummary(lastProfile);
        AppendLog("使用本地生成器。主题: " + ThemeToChinese(lastProfile.PrimaryTheme) + "。来源: " + sourceLabel);

        VoxelCanvas canvas = new VoxelCanvas();
        VoxelShapeFactory.Build(canvas, lastProfile);
        BuildCombinedModel(canvas, lastProfile.Palette);
    }

    private bool GenerateModelFromBlueprint(string prompt, int generation, OpenAIVoxelBlueprint blueprint)
    {
        PromptProfile fallbackProfile = InterpretPrompt(prompt, generation);
        VoxelCanvas canvas = new VoxelCanvas();
        if (!PopulateCanvasFromBlueprint(canvas, blueprint))
        {
            return false;
        }

        if (blueprint.floating_base)
        {
            VoxelShapeFactory.AddFloatingBaseFromExternal(canvas, fallbackProfile.Seed);
        }

        EnhanceBlueprintCanvas(canvas, blueprint);

        lastProfile = fallbackProfile;
        lastGeneratedPrompt = prompt;
        lastPlannerSource = "OpenAI 蓝图";
        generationSummary = BuildBlueprintSummary(blueprint);
        AppendLog("OpenAI 蓝图概念: " + blueprint.concept_name + "。类型: " + blueprint.archetype + "。摘要: " + blueprint.summary);

        BuildCombinedModel(canvas, BuildPaletteFromBlueprint(blueprint, fallbackProfile.Palette));
        return true;
    }

    private bool PopulateCanvasFromBlueprint(VoxelCanvas canvas, OpenAIVoxelBlueprint blueprint)
    {
        if (!OpenAIVoxelBlueprintClient.IsValidBlueprint(blueprint))
        {
            return false;
        }

        for (int y = 0; y < blueprint.layers.Length; y++)
        {
            string[] rows = blueprint.layers[y].rows;
            for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
            {
                string row = rows[rowIndex];
                for (int x = 0; x < row.Length; x++)
                {
                    int colorIndex = row[x] - '1';
                    if (colorIndex < 0)
                    {
                        continue;
                    }

                    int centeredX = x - row.Length / 2;
                    int centeredZ = rowIndex - rows.Length / 2;
                    canvas.Set(centeredX, y, centeredZ, Mathf.Clamp(colorIndex, PrimaryColor, GroundColor));
                }
            }
        }

        return true;
    }

    private void EnhanceBlueprintCanvas(VoxelCanvas canvas, OpenAIVoxelBlueprint blueprint)
    {
        ApplyBlueprintSymmetry(canvas, blueprint);
        BridgeBlueprintGaps(canvas);
        RemoveIsolatedBlueprintVoxels(canvas);
        AppendLog("已对在线蓝图执行本地修形。对称: " + blueprint.symmetry_mode + "。");
    }

    private void ApplyBlueprintSymmetry(VoxelCanvas canvas, OpenAIVoxelBlueprint blueprint)
    {
        if (blueprint == null || string.IsNullOrWhiteSpace(blueprint.symmetry_mode) || blueprint.symmetry_mode == "none")
        {
            return;
        }

        VoxelBounds bounds = canvas.GetBounds();
        float centerX = (bounds.Min.x + bounds.Max.x) * 0.5f;
        float centerZ = (bounds.Min.z + bounds.Max.z) * 0.5f;
        List<KeyValuePair<Vector3Int, int>> snapshot = new List<KeyValuePair<Vector3Int, int>>(canvas.Voxels);

        for (int index = 0; index < snapshot.Count; index++)
        {
            Vector3Int position = snapshot[index].Key;
            int color = snapshot[index].Value;

            if (blueprint.symmetry_mode == "mirror_x" || blueprint.symmetry_mode == "mirror_xz")
            {
                int mirroredX = Mathf.RoundToInt(centerX + (centerX - position.x));
                canvas.Set(mirroredX, position.y, position.z, color);
            }

            if (blueprint.symmetry_mode == "mirror_z" || blueprint.symmetry_mode == "mirror_xz")
            {
                int mirroredZ = Mathf.RoundToInt(centerZ + (centerZ - position.z));
                canvas.Set(position.x, position.y, mirroredZ, color);
            }

            if (blueprint.symmetry_mode == "mirror_xz")
            {
                int mirroredX = Mathf.RoundToInt(centerX + (centerX - position.x));
                int mirroredZ = Mathf.RoundToInt(centerZ + (centerZ - position.z));
                canvas.Set(mirroredX, position.y, mirroredZ, color);
            }
        }
    }

    private void BridgeBlueprintGaps(VoxelCanvas canvas)
    {
        List<KeyValuePair<Vector3Int, int>> snapshot = new List<KeyValuePair<Vector3Int, int>>(canvas.Voxels);
        List<KeyValuePair<Vector3Int, int>> additions = new List<KeyValuePair<Vector3Int, int>>();
        Vector3Int[] directions =
        {
            new Vector3Int(1, 0, 0),
            new Vector3Int(0, 1, 0),
            new Vector3Int(0, 0, 1)
        };

        for (int index = 0; index < snapshot.Count; index++)
        {
            Vector3Int start = snapshot[index].Key;
            int startColor = snapshot[index].Value;

            for (int directionIndex = 0; directionIndex < directions.Length; directionIndex++)
            {
                Vector3Int step = directions[directionIndex];
                Vector3Int middle = start + step;
                Vector3Int end = start + step * 2;

                if (canvas.HasVoxel(middle.x, middle.y, middle.z))
                {
                    continue;
                }

                int endColor;
                if (!canvas.TryGetColor(end.x, end.y, end.z, out endColor))
                {
                    continue;
                }

                if (!(startColor == endColor || (startColor <= SecondaryColor && endColor <= SecondaryColor)))
                {
                    continue;
                }

                int bridgeColor = Mathf.Min(startColor, endColor);
                additions.Add(new KeyValuePair<Vector3Int, int>(middle, bridgeColor));
            }
        }

        for (int index = 0; index < additions.Count; index++)
        {
            Vector3Int position = additions[index].Key;
            canvas.Set(position.x, position.y, position.z, additions[index].Value);
        }
    }

    private void RemoveIsolatedBlueprintVoxels(VoxelCanvas canvas)
    {
        List<Vector3Int> removals = new List<Vector3Int>();
        List<KeyValuePair<Vector3Int, int>> snapshot = new List<KeyValuePair<Vector3Int, int>>(canvas.Voxels);
        Vector3Int[] neighbors =
        {
            new Vector3Int(1, 0, 0),
            new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 1, 0),
            new Vector3Int(0, -1, 0),
            new Vector3Int(0, 0, 1),
            new Vector3Int(0, 0, -1)
        };

        for (int index = 0; index < snapshot.Count; index++)
        {
            Vector3Int position = snapshot[index].Key;
            int neighborCount = 0;
            for (int neighborIndex = 0; neighborIndex < neighbors.Length; neighborIndex++)
            {
                Vector3Int neighbor = position + neighbors[neighborIndex];
                if (canvas.HasVoxel(neighbor.x, neighbor.y, neighbor.z))
                {
                    neighborCount++;
                }
            }

            if (neighborCount == 0)
            {
                removals.Add(position);
            }
        }

        for (int index = 0; index < removals.Count; index++)
        {
            Vector3Int position = removals[index];
            canvas.Remove(position.x, position.y, position.z);
        }
    }

    private PromptPalette BuildPaletteFromBlueprint(OpenAIVoxelBlueprint blueprint, PromptPalette fallback)
    {
        if (blueprint == null || blueprint.palette == null)
        {
            return fallback;
        }

        return new PromptPalette(
            ParseHtmlColor(blueprint.palette.primary, fallback.Primary),
            ParseHtmlColor(blueprint.palette.secondary, fallback.Secondary),
            ParseHtmlColor(blueprint.palette.accent, fallback.Accent),
            ParseHtmlColor(blueprint.palette.detail, fallback.Detail),
            ParseHtmlColor(blueprint.palette.glow, fallback.Glow),
            ParseHtmlColor(blueprint.palette.ground, fallback.Ground));
    }

    private static Color ParseHtmlColor(string rawColor, Color fallback)
    {
        Color parsed;
        if (!string.IsNullOrWhiteSpace(rawColor) && ColorUtility.TryParseHtmlString(rawColor, out parsed))
        {
            return parsed;
        }

        return fallback;
    }

    private string BuildBlueprintSummary(OpenAIVoxelBlueprint blueprint)
    {
        if (blueprint == null)
        {
            return "在线蓝图为空，已使用本地生成。";
        }

        string tags = blueprint.style_tags != null && blueprint.style_tags.Length > 0
            ? string.Join(" · ", blueprint.style_tags)
            : "在线概念生成";
        string iconicParts = blueprint.iconic_parts != null && blueprint.iconic_parts.Length > 0
            ? string.Join("/", blueprint.iconic_parts)
            : "无";

        return blueprint.concept_name + " · " + blueprint.archetype + " · " + blueprint.summary + " · 部件: " + iconicParts + " · " + tags;
    }

    private void LoadOnlineSettings()
    {
        projectSettings = Resources.Load<OpenAIVoxelProjectSettings>("OpenAIVoxelProjectSettings");
        if (projectSettings == null)
        {
            openAiApiKey = string.Empty;
            openAiModel = OpenAIVoxelBlueprintClient.DefaultModel;
            useOnlinePlanner = false;
            plannerStatus = "未找到项目配置资源，当前只使用本地生成。";
            AppendLog("未找到项目资源配置 OpenAIVoxelProjectSettings，在线理解已禁用。");
            return;
        }

        openAiApiKey = projectSettings.ApiKey;
        openAiModel = projectSettings.Model;
        useOnlinePlanner = projectSettings.EnableOnlinePlanner;
        plannerStatus = useOnlinePlanner
            ? (projectSettings.HasApiKey ? "在线配置已从项目资源加载。" : "项目资源已启用在线理解，但还没有填写 API Key。")
            : "项目资源当前关闭了在线理解。";
        AppendLog("已读取项目配置。在线: " + (useOnlinePlanner ? "开" : "关") + "，模型: " + openAiModel + "，Key: " + MaskApiKey(openAiApiKey));
    }

    private void BuildCombinedModel(VoxelCanvas canvas, PromptPalette palette)
    {
        CleanupGeneratedModel();

        currentModel = new GameObject("Generated Model");
        currentModel.transform.SetParent(modelPivot, false);

        VoxelBounds bounds = canvas.GetBounds();
        Vector3 pivotOffset = new Vector3(
            (bounds.Min.x + bounds.Max.x) * 0.5f,
            bounds.Min.y,
            (bounds.Min.z + bounds.Max.z) * 0.5f);

        Dictionary<int, List<CombineInstance>> groups = new Dictionary<int, List<CombineInstance>>();
        foreach (KeyValuePair<Vector3Int, int> pair in canvas.Voxels)
        {
            List<CombineInstance> bucket;
            if (!groups.TryGetValue(pair.Value, out bucket))
            {
                bucket = new List<CombineInstance>();
                groups.Add(pair.Value, bucket);
            }

            Vector3 localPosition = (new Vector3(pair.Key.x, pair.Key.y, pair.Key.z) - pivotOffset) * CubeSize;
            CombineInstance instance = new CombineInstance
            {
                mesh = cubeMesh,
                transform = Matrix4x4.TRS(localPosition, Quaternion.identity, Vector3.one * CubeSize * 0.94f)
            };

            bucket.Add(instance);
        }

        foreach (KeyValuePair<int, List<CombineInstance>> group in groups)
        {
            Mesh mesh = new Mesh();
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.CombineMeshes(group.Value.ToArray(), true, true, false);
            mesh.RecalculateBounds();
            generatedMeshes.Add(mesh);

            GameObject chunk = new GameObject("Voxel Chunk " + group.Key);
            chunk.transform.SetParent(currentModel.transform, false);

            MeshFilter filter = chunk.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            MeshRenderer renderer = chunk.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;

            Material material = CreateMaterial(palette.Get(group.Key));
            generatedMaterials.Add(material);
            renderer.sharedMaterial = material;
        }

        ResizeStage(bounds);
        PositionCamera(bounds);
        AppendLog("模型已构建完成。体素数: " + CountCanvasVoxels(canvas) + "，材质组: " + groups.Count + "，尺寸: " + bounds.Size.x + "x" + bounds.Size.y + "x" + bounds.Size.z + "。");
    }

    private void SetupScene()
    {
        sceneCamera = Camera.main;
        if (sceneCamera == null)
        {
            sceneCamera = FindFirstObjectByType<Camera>();
        }

        if (sceneCamera != null)
        {
            sceneCamera.transform.position = new Vector3(0f, 7.5f, -16f);
            sceneCamera.transform.rotation = Quaternion.Euler(22f, 0f, 0f);
            sceneCamera.fieldOfView = 38f;
        }

        sceneLight = FindFirstObjectByType<Light>();
        if (sceneLight != null)
        {
            sceneLight.intensity = 2.2f;
            sceneLight.transform.rotation = Quaternion.Euler(50f, -32f, 0f);
        }

        if (modelPivot == null)
        {
            GameObject pivot = new GameObject("Voxel Model Pivot");
            pivot.transform.SetParent(transform, false);
            modelPivot = pivot.transform;
        }

        if (stageDisk == null)
        {
            stageDisk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stageDisk.name = "Voxel Stage";
            stageDisk.transform.SetParent(transform, false);
            stageDisk.transform.position = new Vector3(0f, -0.48f, 0f);
            stageDisk.transform.localScale = new Vector3(3.8f, 0.12f, 3.8f);
            Collider colliderComponent = stageDisk.GetComponent<Collider>();
            if (colliderComponent != null)
            {
                Destroy(colliderComponent);
            }

            MeshRenderer renderer = stageDisk.GetComponent<MeshRenderer>();
            stageMaterial = CreateMaterial(new Color(0.11f, 0.14f, 0.18f, 1f));
            renderer.sharedMaterial = stageMaterial;
            renderer.receiveShadows = true;
        }

        Camera sceneCam = sceneCamera;
        if (sceneCam != null)
        {
            sceneCam.backgroundColor = new Color(0.08f, 0.11f, 0.14f, 1f);
        }
    }

    private void PositionCamera(VoxelBounds bounds)
    {
        if (sceneCamera == null)
        {
            return;
        }

        float maxSize = Mathf.Max(bounds.Size.x, Mathf.Max(bounds.Size.y, bounds.Size.z)) * CubeSize;
        maxSize = Mathf.Max(4.5f, maxSize);

        Vector3 focus = new Vector3(0f, bounds.Size.y * CubeSize * 0.42f, 0f);
        sceneCamera.transform.position = focus + new Vector3(maxSize * 0.95f, maxSize * 0.7f, -maxSize * 2.1f);
        sceneCamera.transform.LookAt(focus + Vector3.up * maxSize * 0.08f);
        sceneCamera.fieldOfView = Mathf.Clamp(40f + maxSize * 0.6f, 34f, 52f);
    }

    private void ResizeStage(VoxelBounds bounds)
    {
        if (stageDisk == null)
        {
            return;
        }

        float stageScale = Mathf.Clamp(Mathf.Max(bounds.Size.x, bounds.Size.z) * CubeSize * 0.16f, 3.6f, 8f);
        stageDisk.transform.localScale = new Vector3(stageScale, 0.12f, stageScale);
    }

    private void CacheCubeMesh()
    {
        if (cubeMesh != null)
        {
            return;
        }

        GameObject tempCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cubeMesh = tempCube.GetComponent<MeshFilter>().sharedMesh;
        Destroy(tempCube);
    }

    private void CleanupGeneratedModel()
    {
        if (currentModel != null)
        {
            Destroy(currentModel);
            currentModel = null;
        }

        for (int i = 0; i < generatedMeshes.Count; i++)
        {
            if (generatedMeshes[i] != null)
            {
                Destroy(generatedMeshes[i]);
            }
        }

        generatedMeshes.Clear();

        for (int i = 0; i < generatedMaterials.Count; i++)
        {
            if (generatedMaterials[i] != null)
            {
                Destroy(generatedMaterials[i]);
            }
        }

        generatedMaterials.Clear();

    }

    private void CleanupGuiTextures()
    {
        for (int i = 0; i < generatedTextures.Count; i++)
        {
            if (generatedTextures[i] != null)
            {
                Destroy(generatedTextures[i]);
            }
        }

        generatedTextures.Clear();
    }

    private Material CreateMaterial(Color color)
    {
        Material material = litShader != null ? new Material(litShader) : new Material(Shader.Find("Standard"));
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", 0.15f);
        }

        if (material.HasProperty("_Glossiness"))
        {
            material.SetFloat("_Glossiness", 0.15f);
        }

        return material;
    }

    private void EnsureGuiStyles()
    {
        bool isPortrait = IsPortraitLayout();
        float uiScale = GetUiScale(isPortrait);
        if (panelStyle != null && Mathf.Abs(lastGuiScale - uiScale) < 0.01f && lastGuiWasPortrait == isPortrait)
        {
            return;
        }

        CleanupGuiTextures();
        lastGuiScale = uiScale;
        lastGuiWasPortrait = isPortrait;

        int panelPadding = Mathf.RoundToInt(20f * uiScale);
        int titleFontSize = Mathf.RoundToInt(24f * uiScale);
        int subTitleFontSize = Mathf.RoundToInt(15f * uiScale);
        int bodyFontSize = Mathf.RoundToInt(13f * uiScale);
        int inputFontSize = Mathf.RoundToInt(16f * uiScale);
        int buttonFontSize = Mathf.RoundToInt(15f * uiScale);
        int chipFontSize = Mathf.RoundToInt(14f * uiScale);
        int logFontSize = Mathf.RoundToInt(12f * uiScale);

        panelStyle = new GUIStyle(GUI.skin.box);
        panelStyle.padding = new RectOffset(panelPadding, panelPadding, panelPadding, panelPadding);
        panelStyle.normal.background = CreateGuiTexture(new Color(0.05f, 0.08f, 0.11f, 0.94f));

        titleStyle = new GUIStyle(GUI.skin.label);
        titleStyle.fontSize = titleFontSize;
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.wordWrap = true;
        titleStyle.normal.textColor = Color.white;

        subTitleStyle = new GUIStyle(GUI.skin.label);
        subTitleStyle.fontSize = subTitleFontSize;
        subTitleStyle.fontStyle = FontStyle.Bold;
        subTitleStyle.wordWrap = true;
        subTitleStyle.normal.textColor = new Color(0.76f, 0.88f, 1f, 1f);

        bodyStyle = new GUIStyle(GUI.skin.label);
        bodyStyle.fontSize = bodyFontSize;
        bodyStyle.wordWrap = true;
        bodyStyle.normal.textColor = new Color(0.9f, 0.94f, 0.98f, 1f);

        inputStyle = new GUIStyle(GUI.skin.textField);
        inputStyle.fontSize = inputFontSize;
        inputStyle.wordWrap = true;
        inputStyle.padding = new RectOffset(14, 14, 12, 12);
        inputStyle.normal.background = CreateGuiTexture(new Color(0.08f, 0.11f, 0.15f, 1f));
        inputStyle.focused.background = CreateGuiTexture(new Color(0.11f, 0.16f, 0.22f, 1f));
        inputStyle.normal.textColor = Color.white;
        inputStyle.focused.textColor = Color.white;

        buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.fontSize = buttonFontSize;
        buttonStyle.fontStyle = FontStyle.Bold;
        buttonStyle.padding = new RectOffset(14, 14, 10, 10);
        buttonStyle.normal.background = CreateGuiTexture(new Color(0.16f, 0.31f, 0.46f, 1f));
        buttonStyle.hover.background = CreateGuiTexture(new Color(0.19f, 0.39f, 0.58f, 1f));
        buttonStyle.active.background = CreateGuiTexture(new Color(0.14f, 0.27f, 0.40f, 1f));
        buttonStyle.normal.textColor = Color.white;
        buttonStyle.hover.textColor = Color.white;
        buttonStyle.active.textColor = Color.white;

        chipStyle = new GUIStyle(GUI.skin.button);
        chipStyle.fontSize = chipFontSize;
        chipStyle.wordWrap = true;
        chipStyle.padding = new RectOffset(10, 10, 8, 8);
        chipStyle.normal.background = CreateGuiTexture(new Color(0.12f, 0.18f, 0.24f, 1f));
        chipStyle.hover.background = CreateGuiTexture(new Color(0.18f, 0.26f, 0.34f, 1f));
        chipStyle.active.background = CreateGuiTexture(new Color(0.15f, 0.22f, 0.29f, 1f));
        chipStyle.normal.textColor = new Color(0.92f, 0.97f, 1f, 1f);
        chipStyle.hover.textColor = Color.white;
        chipStyle.active.textColor = Color.white;

        sectionToggleStyle = new GUIStyle(GUI.skin.button);
        sectionToggleStyle.fontSize = subTitleFontSize;
        sectionToggleStyle.fontStyle = FontStyle.Bold;
        sectionToggleStyle.alignment = TextAnchor.MiddleLeft;
        sectionToggleStyle.padding = new RectOffset(14, 14, 9, 9);
        sectionToggleStyle.normal.background = CreateGuiTexture(new Color(0.09f, 0.14f, 0.19f, 1f));
        sectionToggleStyle.hover.background = CreateGuiTexture(new Color(0.13f, 0.20f, 0.27f, 1f));
        sectionToggleStyle.active.background = CreateGuiTexture(new Color(0.11f, 0.17f, 0.23f, 1f));
        sectionToggleStyle.normal.textColor = new Color(0.84f, 0.93f, 1f, 1f);
        sectionToggleStyle.hover.textColor = Color.white;
        sectionToggleStyle.active.textColor = Color.white;

        headerButtonStyle = new GUIStyle(buttonStyle);
        headerButtonStyle.fontSize = bodyFontSize;
        headerButtonStyle.fixedHeight = Mathf.RoundToInt(34f * uiScale);

        logPanelStyle = new GUIStyle(panelStyle);
        logPanelStyle.padding = new RectOffset(Mathf.RoundToInt(18f * uiScale), Mathf.RoundToInt(18f * uiScale), Mathf.RoundToInt(16f * uiScale), Mathf.RoundToInt(16f * uiScale));
        logPanelStyle.normal.background = CreateGuiTexture(new Color(0.03f, 0.05f, 0.08f, 0.92f));

        logStyle = new GUIStyle(bodyStyle);
        logStyle.fontSize = logFontSize;
        logStyle.richText = false;
        logStyle.wordWrap = true;
    }

    private void EnsureLayoutState()
    {
        if (layoutStateInitialized)
        {
            return;
        }

        bool isPortrait = IsPortraitLayout();
        controlsExpanded = true;
        logPanelExpanded = !isPortrait;
        onlineStatusExpanded = true;
        suggestionsExpanded = true;
        resultExpanded = true;
        layoutStateInitialized = true;
    }

    private bool IsPortraitLayout()
    {
        return Screen.height >= Screen.width;
    }

    private float GetUiScale(bool isPortrait)
    {
        return isPortrait
            ? Mathf.Clamp(Screen.width / 720f, 1.05f, 1.45f)
            : Mathf.Clamp(Mathf.Min(Screen.width / 1280f, Screen.height / 820f), 1f, 1.18f);
    }

    private Rect CalculateControlPanelRect(bool stackPanels, bool isPortrait, float uiScale)
    {
        float margin = 16f;
        float collapsedHeight = Mathf.RoundToInt(68f * uiScale);

        if (stackPanels)
        {
            float width = Mathf.Min(Screen.width - margin * 2f, isPortrait ? 860f : 780f);
            float x = (Screen.width - width) * 0.5f;
            float expandedHeight = Mathf.Min(Screen.height * (isPortrait ? 0.45f : 0.56f), isPortrait ? 820f : 700f);
            float height = controlsExpanded ? Mathf.Max(expandedHeight, 280f) : collapsedHeight;
            return new Rect(x, margin, width, Mathf.Min(height, Screen.height - margin * 2f));
        }

        float availableWidth = Screen.width - margin * 3f;
        float widthLandscape = Mathf.Clamp(availableWidth * 0.44f, 470f, 620f);
        float heightLandscape = controlsExpanded ? Mathf.Min(Screen.height - margin * 2f, 760f) : collapsedHeight;
        return new Rect(margin, margin, widthLandscape, heightLandscape);
    }

    private Rect CalculateLogPanelRect(Rect mainPanelRect, bool stackPanels, bool isPortrait, float uiScale)
    {
        float margin = 16f;
        float collapsedHeight = Mathf.RoundToInt(68f * uiScale);

        if (stackPanels)
        {
            float y = mainPanelRect.yMax + 12f;
            float availableHeight = Screen.height - y - margin;
            float expandedHeight = Mathf.Clamp(Screen.height * (isPortrait ? 0.24f : 0.28f), 140f, 360f);
            float height = logPanelExpanded ? Mathf.Min(expandedHeight, availableHeight) : Mathf.Min(collapsedHeight, availableHeight);
            return new Rect(mainPanelRect.x, y, mainPanelRect.width, Mathf.Max(height, 52f));
        }

        float x = mainPanelRect.xMax + 16f;
        float width = Mathf.Clamp(Screen.width - x - margin, 360f, 560f);
        float heightLandscape = logPanelExpanded ? Mathf.Min(Screen.height - margin * 2f, 420f) : collapsedHeight;
        return new Rect(x, margin, width, heightLandscape);
    }

    private void DrawPanelHeader(string title, string subtitle, ref bool expanded, GUIStyle labelStyle, float uiScale)
    {
        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        GUILayout.Label(title, labelStyle);
        if (expanded && !string.IsNullOrEmpty(subtitle))
        {
            GUILayout.Space(2f);
            GUILayout.Label(subtitle, bodyStyle);
        }
        GUILayout.EndVertical();

        if (GUILayout.Button(expanded ? "收起" : "展开", headerButtonStyle, GUILayout.Width(Mathf.RoundToInt(92f * uiScale))))
        {
            expanded = !expanded;
        }
        GUILayout.EndHorizontal();
    }

    private bool DrawSectionToggle(string title, bool expanded, float uiScale)
    {
        string label = (expanded ? "收起 " : "展开 ") + title;
        if (GUILayout.Button(label, sectionToggleStyle, GUILayout.Height(Mathf.RoundToInt(38f * uiScale))))
        {
            return !expanded;
        }

        return expanded;
    }

    private Texture2D CreateGuiTexture(Color color)
    {
        Texture2D texture = new Texture2D(1, 1);
        texture.hideFlags = HideFlags.HideAndDontSave;
        texture.SetPixel(0, 0, color);
        texture.Apply();
        generatedTextures.Add(texture);
        return texture;
    }

    private void AppendLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message;
        runtimeLogs.Add(line);
        if (runtimeLogs.Count > MaxRuntimeLogs)
        {
            runtimeLogs.RemoveAt(0);
        }

        shouldScrollLogToBottom = true;
        Debug.Log("[Voxel Prompt] " + message);
    }

    private static string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return "(empty)";
        }

        string trimmed = apiKey.Trim();
        if (trimmed.Length <= 8)
        {
            return "****";
        }

        return trimmed.Substring(0, 4) + "..." + trimmed.Substring(trimmed.Length - 4, 4);
    }

    private static int CountCanvasVoxels(VoxelCanvas canvas)
    {
        int count = 0;
        foreach (KeyValuePair<Vector3Int, int> _ in canvas.Voxels)
        {
            count++;
        }

        return count;
    }

    private string PickRandomPrompt()
    {
        return SamplePrompts[promptPicker.Next(SamplePrompts.Length)];
    }

    private static PromptProfile InterpretPrompt(string prompt, int generation)
    {
        string normalized = string.IsNullOrWhiteSpace(prompt) ? "浮空城堡" : prompt.Trim().ToLowerInvariant();

        int treeScore = CountMatches(normalized, TreeKeywords);
        int houseScore = CountMatches(normalized, HouseKeywords);
        int castleScore = CountMatches(normalized, CastleKeywords);
        int robotScore = CountMatches(normalized, RobotKeywords);
        int spaceshipScore = CountMatches(normalized, SpaceshipKeywords);
        int mushroomScore = CountMatches(normalized, MushroomKeywords);
        int islandScore = CountMatches(normalized, IslandKeywords);
        int catScore = CountMatches(normalized, CatKeywords);

        VoxelTheme theme = VoxelTheme.Castle;
        int bestScore = castleScore;

        if (treeScore > bestScore)
        {
            theme = VoxelTheme.Tree;
            bestScore = treeScore;
        }

        if (houseScore > bestScore)
        {
            theme = VoxelTheme.House;
            bestScore = houseScore;
        }

        if (robotScore > bestScore)
        {
            theme = VoxelTheme.Robot;
            bestScore = robotScore;
        }

        if (spaceshipScore > bestScore)
        {
            theme = VoxelTheme.Spaceship;
            bestScore = spaceshipScore;
        }

        if (mushroomScore > bestScore)
        {
            theme = VoxelTheme.Mushroom;
            bestScore = mushroomScore;
        }

        if (islandScore > bestScore)
        {
            theme = VoxelTheme.Island;
            bestScore = islandScore;
        }

        if (catScore > bestScore)
        {
            theme = VoxelTheme.Cat;
            bestScore = catScore;
        }

        if (bestScore <= 0)
        {
            int fallback = (StableHash(normalized) & int.MaxValue) % 8;
            theme = (VoxelTheme)fallback;
        }

        bool wantsFloatingBase = ContainsAny(normalized, "浮空", "floating", "sky", "cloud", "云");
        bool wantsHouse = ContainsAny(normalized, HouseKeywords);
        bool wantsCastle = ContainsAny(normalized, CastleKeywords);
        bool wantsTree = ContainsAny(normalized, TreeKeywords);
        bool wantsCute = ContainsAny(normalized, "cute", "mini", "tiny", "可爱", "萌", "小");
        bool wantsTall = ContainsAny(normalized, "tower", "tall", "高", "高耸", "修长");
        bool wantsWide = ContainsAny(normalized, "wide", "large", "huge", "宽", "巨型", "巨大");
        bool wantsNeon = ContainsAny(normalized, "cyber", "neon", "赛博", "霓虹", "夜");
        bool wantsBlossom = ContainsAny(normalized, "sakura", "blossom", "樱", "樱花");
        bool wantsSnow = ContainsAny(normalized, "ice", "snow", "frost", "雪", "冰");
        bool wantsDesert = ContainsAny(normalized, "desert", "sand", "沙", "荒漠");
        bool wantsOcean = ContainsAny(normalized, "ocean", "sea", "water", "海", "海洋");
        bool wantsRocket = ContainsAny(normalized, "rocket", "火箭");

        PromptPalette palette = BuildPalette(theme, wantsNeon, wantsBlossom, wantsSnow, wantsDesert, wantsOcean);
        int seed = unchecked(StableHash(normalized) * 1103515245 + generation * 12345 + 97);

        PromptProfile profile = new PromptProfile();
        profile.Prompt = prompt.Trim();
        profile.PrimaryTheme = theme;
        profile.WantsFloatingBase = wantsFloatingBase || theme == VoxelTheme.Island;
        profile.WantsHouse = wantsHouse;
        profile.WantsCastle = wantsCastle;
        profile.WantsTree = wantsTree;
        profile.WantsCute = wantsCute;
        profile.WantsTall = wantsTall;
        profile.WantsWide = wantsWide;
        profile.WantsNeon = wantsNeon;
        profile.WantsBlossom = wantsBlossom;
        profile.WantsSnow = wantsSnow;
        profile.WantsDesert = wantsDesert;
        profile.WantsOcean = wantsOcean;
        profile.WantsRocket = wantsRocket;
        profile.Seed = seed;
        profile.Palette = palette;
        return profile;
    }

    private static PromptPalette BuildPalette(VoxelTheme theme, bool neon, bool blossom, bool snow, bool desert, bool ocean)
    {
        PromptPalette palette;
        switch (theme)
        {
            case VoxelTheme.Tree:
                palette = new PromptPalette(
                    new Color32(88, 160, 92, 255),
                    new Color32(44, 104, 55, 255),
                    new Color32(233, 187, 92, 255),
                    new Color32(230, 221, 201, 255),
                    new Color32(255, 214, 119, 255),
                    new Color32(118, 80, 52, 255));
                break;
            case VoxelTheme.House:
                palette = new PromptPalette(
                    new Color32(196, 156, 103, 255),
                    new Color32(138, 82, 59, 255),
                    new Color32(194, 73, 68, 255),
                    new Color32(240, 229, 202, 255),
                    new Color32(115, 204, 233, 255),
                    new Color32(80, 129, 74, 255));
                break;
            case VoxelTheme.Castle:
                palette = new PromptPalette(
                    new Color32(140, 145, 157, 255),
                    new Color32(93, 98, 113, 255),
                    new Color32(185, 71, 83, 255),
                    new Color32(222, 209, 164, 255),
                    new Color32(90, 181, 214, 255),
                    new Color32(88, 129, 75, 255));
                break;
            case VoxelTheme.Robot:
                palette = new PromptPalette(
                    new Color32(124, 142, 168, 255),
                    new Color32(58, 67, 88, 255),
                    new Color32(252, 107, 126, 255),
                    new Color32(222, 232, 240, 255),
                    new Color32(75, 221, 255, 255),
                    new Color32(88, 98, 121, 255));
                break;
            case VoxelTheme.Spaceship:
                palette = new PromptPalette(
                    new Color32(205, 208, 219, 255),
                    new Color32(80, 85, 101, 255),
                    new Color32(255, 93, 131, 255),
                    new Color32(150, 234, 255, 255),
                    new Color32(255, 188, 81, 255),
                    new Color32(78, 90, 120, 255));
                break;
            case VoxelTheme.Mushroom:
                palette = new PromptPalette(
                    new Color32(220, 86, 86, 255),
                    new Color32(142, 46, 46, 255),
                    new Color32(255, 242, 226, 255),
                    new Color32(243, 223, 191, 255),
                    new Color32(255, 197, 104, 255),
                    new Color32(96, 143, 76, 255));
                break;
            case VoxelTheme.Island:
                palette = new PromptPalette(
                    new Color32(86, 150, 88, 255),
                    new Color32(96, 101, 110, 255),
                    new Color32(102, 194, 217, 255),
                    new Color32(228, 215, 172, 255),
                    new Color32(255, 213, 112, 255),
                    new Color32(126, 90, 60, 255));
                break;
            default:
                palette = new PromptPalette(
                    new Color32(196, 160, 110, 255),
                    new Color32(120, 84, 60, 255),
                    new Color32(247, 177, 85, 255),
                    new Color32(244, 244, 248, 255),
                    new Color32(78, 192, 232, 255),
                    new Color32(100, 84, 67, 255));
                break;
        }

        if (blossom)
        {
            palette = palette.WithPrimary(Color.Lerp(palette.Primary, new Color32(242, 156, 195, 255), 0.65f))
                .WithAccent(new Color32(255, 233, 244, 255));
        }

        if (snow)
        {
            palette = palette.WithPrimary(Color.Lerp(palette.Primary, new Color32(181, 222, 240, 255), 0.55f))
                .WithSecondary(Color.Lerp(palette.Secondary, new Color32(116, 151, 182, 255), 0.5f))
                .WithDetail(new Color32(244, 249, 255, 255));
        }

        if (desert)
        {
            palette = palette.WithPrimary(Color.Lerp(palette.Primary, new Color32(205, 165, 104, 255), 0.55f))
                .WithGround(new Color32(163, 122, 72, 255))
                .WithAccent(new Color32(209, 100, 52, 255));
        }

        if (ocean)
        {
            palette = palette.WithAccent(new Color32(88, 195, 225, 255))
                .WithGlow(new Color32(153, 244, 255, 255))
                .WithPrimary(Color.Lerp(palette.Primary, new Color32(88, 154, 172, 255), 0.25f));
        }

        if (neon)
        {
            palette = palette.WithAccent(new Color32(255, 88, 160, 255))
                .WithGlow(new Color32(73, 255, 245, 255))
                .WithSecondary(Color.Lerp(palette.Secondary, new Color32(36, 39, 60, 255), 0.4f));
        }

        return palette;
    }

    private static string BuildSummary(PromptProfile profile)
    {
        List<string> tags = new List<string>();
        tags.Add("主题: " + ThemeToChinese(profile.PrimaryTheme));

        if (profile.WantsFloatingBase)
        {
            tags.Add("浮空底座");
        }

        if (profile.WantsNeon)
        {
            tags.Add("霓虹");
        }
        else if (profile.WantsBlossom)
        {
            tags.Add("樱花");
        }
        else if (profile.WantsSnow)
        {
            tags.Add("冰雪");
        }
        else if (profile.WantsDesert)
        {
            tags.Add("沙漠");
        }
        else if (profile.WantsOcean)
        {
            tags.Add("海风");
        }
        else
        {
            tags.Add("默认色板");
        }

        if (profile.WantsTall)
        {
            tags.Add("高挑体型");
        }

        if (profile.WantsWide)
        {
            tags.Add("宽体造型");
        }

        if (profile.WantsCute)
        {
            tags.Add("可爱比例");
        }

        return string.Join(" · ", tags.ToArray());
    }

    private static string ThemeToChinese(VoxelTheme theme)
    {
        switch (theme)
        {
            case VoxelTheme.Tree:
                return "树木";
            case VoxelTheme.House:
                return "小屋";
            case VoxelTheme.Castle:
                return "城堡";
            case VoxelTheme.Robot:
                return "机器人";
            case VoxelTheme.Spaceship:
                return "飞船";
            case VoxelTheme.Mushroom:
                return "蘑菇";
            case VoxelTheme.Island:
                return "浮空岛";
            case VoxelTheme.Cat:
                return "猫咪";
            default:
                return "体块";
        }
    }

    private static bool ContainsAny(string source, params string[] keywords)
    {
        for (int i = 0; i < keywords.Length; i++)
        {
            if (source.Contains(keywords[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static int CountMatches(string source, string[] keywords)
    {
        int score = 0;
        for (int i = 0; i < keywords.Length; i++)
        {
            if (source.Contains(keywords[i]))
            {
                score += keywords[i].Length > 1 ? 2 : 1;
            }
        }

        return score;
    }

    private static int StableHash(string text)
    {
        unchecked
        {
            int hash = 17;
            for (int i = 0; i < text.Length; i++)
            {
                hash = hash * 31 + text[i];
            }

            return hash;
        }
    }

    private enum VoxelTheme
    {
        Tree,
        House,
        Castle,
        Robot,
        Spaceship,
        Mushroom,
        Island,
        Cat
    }

    private struct PromptProfile
    {
        public string Prompt;
        public VoxelTheme PrimaryTheme;
        public PromptPalette Palette;
        public bool WantsFloatingBase;
        public bool WantsHouse;
        public bool WantsCastle;
        public bool WantsTree;
        public bool WantsCute;
        public bool WantsTall;
        public bool WantsWide;
        public bool WantsNeon;
        public bool WantsBlossom;
        public bool WantsSnow;
        public bool WantsDesert;
        public bool WantsOcean;
        public bool WantsRocket;
        public int Seed;
    }

    private struct PromptPalette
    {
        public PromptPalette(Color primary, Color secondary, Color accent, Color detail, Color glow, Color ground)
        {
            Primary = primary;
            Secondary = secondary;
            Accent = accent;
            Detail = detail;
            Glow = glow;
            Ground = ground;
        }

        public Color Primary;
        public Color Secondary;
        public Color Accent;
        public Color Detail;
        public Color Glow;
        public Color Ground;

        public Color Get(int index)
        {
            switch (index)
            {
                case PrimaryColor:
                    return Primary;
                case SecondaryColor:
                    return Secondary;
                case AccentColor:
                    return Accent;
                case DetailColor:
                    return Detail;
                case GlowColor:
                    return Glow;
                default:
                    return Ground;
            }
        }

        public PromptPalette WithPrimary(Color value)
        {
            Primary = value;
            return this;
        }

        public PromptPalette WithSecondary(Color value)
        {
            Secondary = value;
            return this;
        }

        public PromptPalette WithAccent(Color value)
        {
            Accent = value;
            return this;
        }

        public PromptPalette WithDetail(Color value)
        {
            Detail = value;
            return this;
        }

        public PromptPalette WithGlow(Color value)
        {
            Glow = value;
            return this;
        }

        public PromptPalette WithGround(Color value)
        {
            Ground = value;
            return this;
        }
    }

    private struct VoxelBounds
    {
        public VoxelBounds(Vector3Int min, Vector3Int max)
        {
            Min = min;
            Max = max;
        }

        public Vector3Int Min;
        public Vector3Int Max;

        public Vector3Int Size
        {
            get { return Max - Min + Vector3Int.one; }
        }
    }

    private sealed class VoxelCanvas
    {
        private readonly Dictionary<Vector3Int, int> voxels = new Dictionary<Vector3Int, int>();
        private bool hasBounds;
        private bool boundsDirty;
        private Vector3Int minBounds;
        private Vector3Int maxBounds;

        public IEnumerable<KeyValuePair<Vector3Int, int>> Voxels
        {
            get { return voxels; }
        }

        public void Set(int x, int y, int z, int color)
        {
            Vector3Int position = new Vector3Int(x, y, z);
            voxels[position] = color;

            if (!hasBounds)
            {
                minBounds = position;
                maxBounds = position;
                hasBounds = true;
                boundsDirty = false;
                return;
            }

            if (boundsDirty)
            {
                return;
            }

            minBounds = new Vector3Int(
                Mathf.Min(minBounds.x, x),
                Mathf.Min(minBounds.y, y),
                Mathf.Min(minBounds.z, z));

            maxBounds = new Vector3Int(
                Mathf.Max(maxBounds.x, x),
                Mathf.Max(maxBounds.y, y),
                Mathf.Max(maxBounds.z, z));
        }

        public bool HasVoxel(int x, int y, int z)
        {
            return voxels.ContainsKey(new Vector3Int(x, y, z));
        }

        public bool TryGetColor(int x, int y, int z, out int color)
        {
            return voxels.TryGetValue(new Vector3Int(x, y, z), out color);
        }

        public void Remove(int x, int y, int z)
        {
            if (voxels.Remove(new Vector3Int(x, y, z)))
            {
                boundsDirty = true;
                if (voxels.Count == 0)
                {
                    hasBounds = false;
                }
            }
        }

        public void FillBox(int minX, int maxX, int minY, int maxY, int minZ, int maxZ, int color)
        {
            if (minX > maxX)
            {
                Swap(ref minX, ref maxX);
            }

            if (minY > maxY)
            {
                Swap(ref minY, ref maxY);
            }

            if (minZ > maxZ)
            {
                Swap(ref minZ, ref maxZ);
            }

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        Set(x, y, z, color);
                    }
                }
            }
        }

        public void FillCylinderY(int centerX, int centerZ, int minY, int maxY, float radius, int color)
        {
            int maxRadius = Mathf.CeilToInt(radius);
            float limit = radius * radius + 0.05f;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = -maxRadius; x <= maxRadius; x++)
                {
                    for (int z = -maxRadius; z <= maxRadius; z++)
                    {
                        if (x * x + z * z <= limit)
                        {
                            Set(centerX + x, y, centerZ + z, color);
                        }
                    }
                }
            }
        }

        public void FillSphere(float centerX, float centerY, float centerZ, float radiusX, float radiusY, float radiusZ, int color, float irregularity, System.Random rng)
        {
            int minX = Mathf.FloorToInt(centerX - radiusX);
            int maxX = Mathf.CeilToInt(centerX + radiusX);
            int minY = Mathf.FloorToInt(centerY - radiusY);
            int maxY = Mathf.CeilToInt(centerY + radiusY);
            int minZ = Mathf.FloorToInt(centerZ - radiusZ);
            int maxZ = Mathf.CeilToInt(centerZ + radiusZ);
            float offset = rng.Next(0, 10000) * 0.017f;

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        float nx = (x - centerX) / Mathf.Max(0.01f, radiusX);
                        float ny = (y - centerY) / Mathf.Max(0.01f, radiusY);
                        float nz = (z - centerZ) / Mathf.Max(0.01f, radiusZ);
                        float value = nx * nx + ny * ny + nz * nz;
                        float threshold = 1f;

                        if (irregularity > 0f)
                        {
                            float noise = Mathf.PerlinNoise((x + offset) * 0.31f, (z - offset) * 0.31f);
                            threshold += (noise - 0.5f) * irregularity;
                        }

                        if (value <= threshold)
                        {
                            Set(x, y, z, color);
                        }
                    }
                }
            }
        }

        public void FillEllipseSliceZ(float centerX, float centerY, int z, float radiusX, float radiusY, int color)
        {
            int minX = Mathf.FloorToInt(centerX - radiusX);
            int maxX = Mathf.CeilToInt(centerX + radiusX);
            int minY = Mathf.FloorToInt(centerY - radiusY);
            int maxY = Mathf.CeilToInt(centerY + radiusY);

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    float nx = (x - centerX) / Mathf.Max(0.01f, radiusX);
                    float ny = (y - centerY) / Mathf.Max(0.01f, radiusY);
                    if (nx * nx + ny * ny <= 1f)
                    {
                        Set(x, y, z, color);
                    }
                }
            }
        }

        public void ReplaceRandom(int fromColor, int toColor, float probability, System.Random rng)
        {
            List<Vector3Int> replacements = new List<Vector3Int>();
            foreach (KeyValuePair<Vector3Int, int> entry in voxels)
            {
                if (entry.Value == fromColor && rng.NextDouble() <= probability)
                {
                    replacements.Add(entry.Key);
                }
            }

            for (int i = 0; i < replacements.Count; i++)
            {
                voxels[replacements[i]] = toColor;
            }
        }

        public VoxelBounds GetBounds()
        {
            if (boundsDirty)
            {
                RecalculateBounds();
            }

            if (!hasBounds)
            {
                return new VoxelBounds(Vector3Int.zero, Vector3Int.zero);
            }

            return new VoxelBounds(minBounds, maxBounds);
        }

        private void RecalculateBounds()
        {
            if (voxels.Count == 0)
            {
                hasBounds = false;
                boundsDirty = false;
                return;
            }

            bool initialized = false;
            Vector3Int min = Vector3Int.zero;
            Vector3Int max = Vector3Int.zero;

            foreach (KeyValuePair<Vector3Int, int> entry in voxels)
            {
                if (!initialized)
                {
                    min = entry.Key;
                    max = entry.Key;
                    initialized = true;
                    continue;
                }

                min = new Vector3Int(
                    Mathf.Min(min.x, entry.Key.x),
                    Mathf.Min(min.y, entry.Key.y),
                    Mathf.Min(min.z, entry.Key.z));

                max = new Vector3Int(
                    Mathf.Max(max.x, entry.Key.x),
                    Mathf.Max(max.y, entry.Key.y),
                    Mathf.Max(max.z, entry.Key.z));
            }

            minBounds = min;
            maxBounds = max;
            hasBounds = true;
            boundsDirty = false;
        }

        private static void Swap(ref int left, ref int right)
        {
            int temp = left;
            left = right;
            right = temp;
        }
    }

    private static class VoxelShapeFactory
    {
        public static void Build(VoxelCanvas canvas, PromptProfile profile)
        {
            System.Random rng = new System.Random(profile.Seed);

            switch (profile.PrimaryTheme)
            {
                case VoxelTheme.Tree:
                    BuildTree(canvas, profile, rng);
                    break;
                case VoxelTheme.House:
                    BuildHouse(canvas, profile, rng);
                    break;
                case VoxelTheme.Castle:
                    BuildCastle(canvas, profile, rng);
                    break;
                case VoxelTheme.Robot:
                    BuildRobot(canvas, profile, rng);
                    break;
                case VoxelTheme.Spaceship:
                    BuildSpaceship(canvas, profile, rng);
                    break;
                case VoxelTheme.Mushroom:
                    BuildMushroom(canvas, profile, rng);
                    break;
                case VoxelTheme.Island:
                    BuildIsland(canvas, profile, rng);
                    break;
                default:
                    BuildCat(canvas, profile, rng);
                    break;
            }

            if (profile.WantsFloatingBase && profile.PrimaryTheme != VoxelTheme.Island)
            {
                AddFloatingBase(canvas, rng);
            }
        }

        public static void AddFloatingBaseFromExternal(VoxelCanvas canvas, int seed)
        {
            AddFloatingBase(canvas, new System.Random(seed));
        }

        private static void BuildTree(VoxelCanvas canvas, PromptProfile profile, System.Random rng)
        {
            int trunkHalf = profile.WantsWide ? rng.Next(2, 4) : rng.Next(1, 3);
            int trunkHeight = profile.WantsTall ? rng.Next(11, 15) : rng.Next(8, 12);
            canvas.FillBox(-trunkHalf, trunkHalf, 0, trunkHeight, -trunkHalf, trunkHalf, GroundColor);

            canvas.FillBox(-trunkHalf - 1, trunkHalf + 1, 0, 1, -trunkHalf, trunkHalf, SecondaryColor);
            canvas.FillBox(-trunkHalf, trunkHalf, 0, 1, -trunkHalf - 1, trunkHalf + 1, SecondaryColor);

            int canopyRadius = profile.WantsWide ? rng.Next(7, 10) : rng.Next(5, 8);
            canvas.FillSphere(0f, trunkHeight + canopyRadius * 0.5f, 0f, canopyRadius, canopyRadius * 0.85f, canopyRadius, PrimaryColor, 0.25f, rng);
            canvas.FillSphere(-canopyRadius * 0.6f, trunkHeight + canopyRadius * 0.25f, 0f, canopyRadius * 0.72f, canopyRadius * 0.55f, canopyRadius * 0.72f, PrimaryColor, 0.18f, rng);
            canvas.FillSphere(canopyRadius * 0.6f, trunkHeight + canopyRadius * 0.3f, 0f, canopyRadius * 0.72f, canopyRadius * 0.58f, canopyRadius * 0.72f, PrimaryColor, 0.18f, rng);
            canvas.ReplaceRandom(PrimaryColor, SecondaryColor, 0.18f, rng);

            if (profile.WantsBlossom)
            {
                canvas.ReplaceRandom(PrimaryColor, AccentColor, 0.22f, rng);
            }

            if (profile.WantsHouse)
            {
                AddMiniHouse(canvas, trunkHalf + 4, trunkHeight / 2, 0, rng);
            }
        }

        private static void BuildHouse(VoxelCanvas canvas, PromptProfile profile, System.Random rng)
        {
            int halfWidth = profile.WantsWide ? rng.Next(7, 10) : rng.Next(5, 8);
            int halfDepth = profile.WantsWide ? rng.Next(5, 7) : rng.Next(4, 6);
            int wallHeight = profile.WantsTall ? rng.Next(8, 11) : rng.Next(6, 9);
            canvas.FillBox(-halfWidth, halfWidth, 0, wallHeight, -halfDepth, halfDepth, PrimaryColor);

            AddGableRoof(canvas, -halfWidth - 1, halfWidth + 1, wallHeight + 1, -halfDepth - 1, halfDepth + 1, SecondaryColor);
            canvas.FillBox(-1, 1, 1, 4, halfDepth, halfDepth, AccentColor);
            AddWindow(canvas, -halfWidth + 2, wallHeight - 2, halfDepth, DetailColor);
            AddWindow(canvas, halfWidth - 2, wallHeight - 2, halfDepth, DetailColor);
            AddWindow(canvas, -halfWidth + 2, wallHeight - 2, -halfDepth, GlowColor);
            AddWindow(canvas, halfWidth - 2, wallHeight - 2, -halfDepth, GlowColor);

            if (profile.WantsTall)
            {
                int towerHalf = 2;
                int towerHeight = wallHeight + 6;
                int towerX = halfWidth + towerHalf + 1;
                canvas.FillBox(towerX - towerHalf, towerX + towerHalf, 0, towerHeight, -towerHalf, towerHalf, SecondaryColor);
                AddGableRoof(canvas, towerX - towerHalf - 1, towerX + towerHalf + 1, towerHeight + 1, -towerHalf - 1, towerHalf + 1, AccentColor);
            }

            if (profile.WantsTree)
            {
                AddMiniTree(canvas, -halfWidth - 5, 0, -halfDepth + 1, rng);
                AddMiniTree(canvas, halfWidth + 5, 0, halfDepth - 1, rng);
            }
        }

        private static void BuildCastle(VoxelCanvas canvas, PromptProfile profile, System.Random rng)
        {
            int coreHalf = profile.WantsWide ? rng.Next(8, 11) : rng.Next(6, 9);
            int wallHeight = profile.WantsTall ? rng.Next(9, 12) : rng.Next(7, 10);
            int towerRadius = profile.WantsWide ? 3 : 2;
            int towerHeight = wallHeight + rng.Next(4, 7) + (profile.WantsTall ? 2 : 0);

            canvas.FillBox(-coreHalf, coreHalf, 0, wallHeight, -coreHalf, coreHalf, PrimaryColor);
            AddBattlements(canvas, -coreHalf, coreHalf, wallHeight + 1, -coreHalf, coreHalf, SecondaryColor);

            AddCornerTower(-coreHalf - towerRadius + 1, -coreHalf - towerRadius + 1, towerRadius, towerHeight);
            AddCornerTower(coreHalf + towerRadius - 1, -coreHalf - towerRadius + 1, towerRadius, towerHeight);
            AddCornerTower(-coreHalf - towerRadius + 1, coreHalf + towerRadius - 1, towerRadius, towerHeight);
            AddCornerTower(coreHalf + towerRadius - 1, coreHalf + towerRadius - 1, towerRadius, towerHeight);

            canvas.FillBox(-2, 2, 1, 5, coreHalf, coreHalf, AccentColor);
            canvas.FillBox(-1, 1, wallHeight - 1, wallHeight, coreHalf, coreHalf, GlowColor);
            canvas.FillBox(-coreHalf, -coreHalf, wallHeight - 2, wallHeight - 1, -1, 1, GlowColor);
            canvas.FillBox(coreHalf, coreHalf, wallHeight - 2, wallHeight - 1, -1, 1, GlowColor);

            if (profile.WantsTall)
            {
                canvas.FillBox(-2, 2, wallHeight + 1, wallHeight + 8, -2, 2, SecondaryColor);
                AddBattlements(canvas, -2, 2, wallHeight + 9, -2, 2, AccentColor);
            }

            void AddCornerTower(int centerX, int centerZ, int radius, int height)
            {
                canvas.FillCylinderY(centerX, centerZ, 0, height, radius + 0.35f, SecondaryColor);
                AddBattlementRing(canvas, centerX, centerZ, height + 1, radius + 1, AccentColor);
            }
        }

        private static void BuildRobot(VoxelCanvas canvas, PromptProfile profile, System.Random rng)
        {
            int legHeight = profile.WantsTall ? rng.Next(6, 8) : rng.Next(4, 6);
            int legOffset = profile.WantsWide ? 4 : 3;
            int torsoHalf = profile.WantsWide ? 6 : 5;
            int torsoHeight = profile.WantsTall ? rng.Next(8, 11) : rng.Next(6, 9);
            int headHalf = profile.WantsCute ? 5 : 4;
            int headHeight = profile.WantsCute ? 6 : 5;

            canvas.FillBox(-legOffset - 1, -legOffset + 1, 0, legHeight, -1, 1, SecondaryColor);
            canvas.FillBox(legOffset - 1, legOffset + 1, 0, legHeight, -1, 1, SecondaryColor);
            canvas.FillBox(-legOffset - 2, -legOffset + 2, 0, 1, -2, 2, GroundColor);
            canvas.FillBox(legOffset - 2, legOffset + 2, 0, 1, -2, 2, GroundColor);

            canvas.FillBox(-torsoHalf, torsoHalf, legHeight, legHeight + torsoHeight, -3, 3, PrimaryColor);
            canvas.FillBox(-torsoHalf + 1, torsoHalf - 1, legHeight + 2, legHeight + torsoHeight - 1, -2, 2, SecondaryColor);
            canvas.FillBox(-2, 2, legHeight + 2, legHeight + 4, 3, 3, AccentColor);
            canvas.FillBox(-1, 1, legHeight + torsoHeight - 2, legHeight + torsoHeight, 3, 3, GlowColor);

            int armYMin = legHeight + 1;
            int armYMax = legHeight + torsoHeight - 1;
            canvas.FillBox(-torsoHalf - 3, -torsoHalf - 1, armYMin, armYMax, -1, 1, SecondaryColor);
            canvas.FillBox(torsoHalf + 1, torsoHalf + 3, armYMin, armYMax, -1, 1, SecondaryColor);
            canvas.FillBox(-torsoHalf - 4, -torsoHalf, armYMin - 1, armYMin, -2, 2, GroundColor);
            canvas.FillBox(torsoHalf, torsoHalf + 4, armYMin - 1, armYMin, -2, 2, GroundColor);

            int headYMin = legHeight + torsoHeight + 1;
            canvas.FillBox(-headHalf, headHalf, headYMin, headYMin + headHeight, -headHalf + 1, headHalf + 1, PrimaryColor);
            canvas.FillBox(-headHalf + 1, -1, headYMin + headHeight - 2, headYMin + headHeight - 1, headHalf + 1, headHalf + 1, GlowColor);
            canvas.FillBox(1, headHalf - 1, headYMin + headHeight - 2, headYMin + headHeight - 1, headHalf + 1, headHalf + 1, GlowColor);
            canvas.FillBox(-1, 1, headYMin + headHeight + 1, headYMin + headHeight + 3, 0, 0, AccentColor);

            if (profile.WantsRocket || profile.WantsNeon)
            {
                canvas.FillBox(-2, 2, legHeight + 1, legHeight + torsoHeight - 1, -4, -4, AccentColor);
                canvas.FillBox(-1, 1, legHeight + 2, legHeight + torsoHeight - 2, -5, -5, GlowColor);
            }
        }

        private static void BuildSpaceship(VoxelCanvas canvas, PromptProfile profile, System.Random rng)
        {
            int halfLength = profile.WantsTall ? rng.Next(12, 15) : rng.Next(9, 13);
            int maxWidth = profile.WantsWide ? rng.Next(7, 9) : rng.Next(5, 7);
            int maxHeight = profile.WantsWide ? 5 : 4;
            int bodyCenterY = profile.WantsRocket ? 8 : 6;

            for (int z = -halfLength; z <= halfLength; z++)
            {
                float t = 1f - Mathf.Abs(z) / (float)halfLength;
                float forwardBias = z > 0 ? Mathf.Lerp(0.55f, 1f, t) : 1f;
                float radiusX = Mathf.Max(1.3f, maxWidth * (0.25f + 0.75f * t) * forwardBias);
                float radiusY = Mathf.Max(1f, maxHeight * (0.4f + 0.6f * t));
                canvas.FillEllipseSliceZ(0f, bodyCenterY, z, radiusX, radiusY, PrimaryColor);
            }

            canvas.FillBox(-maxWidth - 4, maxWidth + 4, bodyCenterY - 1, bodyCenterY + 1, -2, 1, SecondaryColor);
            canvas.FillBox(-maxWidth + 1, maxWidth - 1, bodyCenterY + 1, bodyCenterY + 2, 1, 5, AccentColor);
            canvas.FillSphere(0f, bodyCenterY + 2f, halfLength * 0.25f, 2.5f, 2f, 3f, DetailColor, 0.05f, rng);
            canvas.FillBox(-2, 2, bodyCenterY - 1, bodyCenterY + 1, -halfLength - 1, -halfLength - 1, GlowColor);
            canvas.FillBox(-5, -2, bodyCenterY - 2, bodyCenterY + 1, -5, -2, SecondaryColor);
            canvas.FillBox(2, 5, bodyCenterY - 2, bodyCenterY + 1, -5, -2, SecondaryColor);

            if (profile.WantsRocket)
            {
                canvas.FillBox(-2, 2, bodyCenterY + 3, bodyCenterY + 11, -2, 2, PrimaryColor);
                canvas.FillBox(-1, 1, bodyCenterY + 12, bodyCenterY + 14, -1, 1, AccentColor);
            }
        }

        private static void BuildMushroom(VoxelCanvas canvas, PromptProfile profile, System.Random rng)
        {
            int stemRadius = profile.WantsWide ? 3 : 2;
            int stemHeight = profile.WantsTall ? rng.Next(10, 13) : rng.Next(7, 10);
            int capRadius = profile.WantsWide ? rng.Next(9, 12) : rng.Next(7, 10);

            canvas.FillCylinderY(0, 0, 0, stemHeight, stemRadius + 0.2f, DetailColor);
            canvas.FillSphere(0f, stemHeight + 3f, 0f, capRadius, 4f, capRadius, PrimaryColor, 0.08f, rng);
            canvas.FillSphere(0f, stemHeight + 1.8f, 0f, capRadius * 0.9f, 2.2f, capRadius * 0.9f, PrimaryColor, 0.04f, rng);
            canvas.ReplaceRandom(PrimaryColor, SecondaryColor, 0.12f, rng);
            canvas.ReplaceRandom(PrimaryColor, AccentColor, 0.08f, rng);

            if (profile.WantsHouse || profile.WantsCute)
            {
                canvas.FillBox(-1, 1, 1, 4, stemRadius, stemRadius, SecondaryColor);
                canvas.FillBox(-2, -1, 5, 6, stemRadius, stemRadius, GlowColor);
                canvas.FillBox(1, 2, 5, 6, stemRadius, stemRadius, GlowColor);
            }
        }

        private static void BuildIsland(VoxelCanvas canvas, PromptProfile profile, System.Random rng)
        {
            int topHalfX = profile.WantsWide ? rng.Next(10, 13) : rng.Next(7, 10);
            int topHalfZ = profile.WantsWide ? rng.Next(9, 12) : rng.Next(6, 9);
            int topY = 8;

            for (int y = topY; y >= 0; y--)
            {
                float falloff = topY - y;
                float radiusX = topHalfX - falloff * 0.75f;
                float radiusZ = topHalfZ - falloff * 0.75f;
                if (radiusX < 2f || radiusZ < 2f)
                {
                    break;
                }

                int color = y == topY ? PrimaryColor : (y >= topY - 2 ? GroundColor : SecondaryColor);
                FillIrregularEllipse(canvas, 0, y, 0, radiusX, radiusZ, color, rng, 0.28f);
            }

            canvas.FillCylinderY(0, 0, -3, 0, 1.5f, SecondaryColor);

            if (profile.WantsCastle)
            {
                AddMiniCastle(canvas, 0, topY + 1, 0);
            }
            else if (profile.WantsHouse)
            {
                AddMiniHouse(canvas, 0, topY + 1, 0, rng);
            }
            else
            {
                AddMiniTree(canvas, 0, topY + 1, 0, rng);
            }
        }

        private static void BuildCat(VoxelCanvas canvas, PromptProfile profile, System.Random rng)
        {
            int legHeight = 3;
            int bodyStartY = legHeight;
            int bodyEndY = bodyStartY + (profile.WantsCute ? 4 : 5);
            int bodyHalfX = profile.WantsWide ? 6 : 5;
            int bodyMinZ = -3;
            int bodyMaxZ = 3;

            canvas.FillBox(-bodyHalfX + 1, -bodyHalfX + 3, 0, legHeight, -2, 0, SecondaryColor);
            canvas.FillBox(bodyHalfX - 3, bodyHalfX - 1, 0, legHeight, -2, 0, SecondaryColor);
            canvas.FillBox(-bodyHalfX + 1, -bodyHalfX + 3, 0, legHeight, 1, 3, SecondaryColor);
            canvas.FillBox(bodyHalfX - 3, bodyHalfX - 1, 0, legHeight, 1, 3, SecondaryColor);

            canvas.FillBox(-bodyHalfX, bodyHalfX, bodyStartY, bodyEndY, bodyMinZ, bodyMaxZ, PrimaryColor);
            canvas.FillBox(-3, 3, bodyEndY - 1, bodyEndY + 6, 3, 10, PrimaryColor);
            canvas.FillBox(-4, -2, bodyEndY + 6, bodyEndY + 9, 8, 10, SecondaryColor);
            canvas.FillBox(2, 4, bodyEndY + 6, bodyEndY + 9, 8, 10, SecondaryColor);
            canvas.FillBox(-2, -1, bodyEndY + 2, bodyEndY + 3, 10, 10, GlowColor);
            canvas.FillBox(1, 2, bodyEndY + 2, bodyEndY + 3, 10, 10, GlowColor);
            canvas.FillBox(0, 0, bodyEndY + 1, bodyEndY + 1, 10, 10, AccentColor);

            for (int step = 0; step < 6; step++)
            {
                int tailMinX = bodyHalfX - 1 + step;
                int tailMaxX = tailMinX + 1;
                int tailMinY = bodyEndY - 1 + step / 2;
                int tailMaxY = bodyEndY + step / 2;
                int tailZ = -3 - step;
                canvas.FillBox(tailMinX, tailMaxX, tailMinY, tailMaxY, tailZ, tailZ + 1, SecondaryColor);
            }
        }

        private static void AddMiniTree(VoxelCanvas canvas, int baseX, int baseY, int baseZ, System.Random rng)
        {
            canvas.FillBox(baseX, baseX + 1, baseY, baseY + 4, baseZ, baseZ + 1, GroundColor);
            canvas.FillSphere(baseX + 0.5f, baseY + 6f, baseZ + 0.5f, 3f, 3f, 3f, PrimaryColor, 0.2f, rng);
        }

        private static void AddMiniHouse(VoxelCanvas canvas, int centerX, int baseY, int centerZ, System.Random rng)
        {
            canvas.FillBox(centerX - 3, centerX + 3, baseY, baseY + 4, centerZ - 3, centerZ + 3, PrimaryColor);
            AddGableRoof(canvas, centerX - 4, centerX + 4, baseY + 5, centerZ - 4, centerZ + 4, SecondaryColor);
            canvas.FillBox(centerX - 1, centerX + 1, baseY + 1, baseY + 3, centerZ + 3, centerZ + 3, AccentColor);
            canvas.FillBox(centerX - 3, centerX - 2, baseY + 2, baseY + 3, centerZ + 3, centerZ + 3, GlowColor);
            canvas.FillBox(centerX + 2, centerX + 3, baseY + 2, baseY + 3, centerZ + 3, centerZ + 3, GlowColor);
        }

        private static void AddMiniCastle(VoxelCanvas canvas, int centerX, int baseY, int centerZ)
        {
            canvas.FillBox(centerX - 4, centerX + 4, baseY, baseY + 5, centerZ - 4, centerZ + 4, PrimaryColor);
            canvas.FillCylinderY(centerX - 5, centerZ - 5, baseY, baseY + 7, 1.7f, SecondaryColor);
            canvas.FillCylinderY(centerX + 5, centerZ - 5, baseY, baseY + 7, 1.7f, SecondaryColor);
            canvas.FillCylinderY(centerX - 5, centerZ + 5, baseY, baseY + 7, 1.7f, SecondaryColor);
            canvas.FillCylinderY(centerX + 5, centerZ + 5, baseY, baseY + 7, 1.7f, SecondaryColor);
            AddBattlements(canvas, centerX - 4, centerX + 4, baseY + 6, centerZ - 4, centerZ + 4, AccentColor);
        }

        private static void AddFloatingBase(VoxelCanvas canvas, System.Random rng)
        {
            VoxelBounds bounds = canvas.GetBounds();
            int centerX = Mathf.RoundToInt((bounds.Min.x + bounds.Max.x) * 0.5f);
            int centerZ = Mathf.RoundToInt((bounds.Min.z + bounds.Max.z) * 0.5f);
            int topY = bounds.Min.y - 1;
            int halfX = Mathf.Max(5, Mathf.CeilToInt(bounds.Size.x * 0.45f) + 2);
            int halfZ = Mathf.Max(5, Mathf.CeilToInt(bounds.Size.z * 0.45f) + 2);

            for (int y = topY; y >= topY - 9; y--)
            {
                float falloff = topY - y;
                float radiusX = halfX - falloff * 0.72f;
                float radiusZ = halfZ - falloff * 0.72f;
                if (radiusX < 1.4f || radiusZ < 1.4f)
                {
                    break;
                }

                int color = y >= topY - 1 ? GroundColor : SecondaryColor;
                FillIrregularEllipse(canvas, centerX, y, centerZ, radiusX, radiusZ, color, rng, 0.24f);
            }

            canvas.FillCylinderY(centerX, centerZ, topY - 11, topY - 5, 1.5f, SecondaryColor);
            canvas.FillSphere(centerX - 3f, topY - 7f, centerZ + 2f, 2.2f, 1.8f, 2.2f, SecondaryColor, 0.15f, rng);
            canvas.FillSphere(centerX + 4f, topY - 6f, centerZ - 2f, 1.8f, 1.6f, 1.8f, SecondaryColor, 0.15f, rng);
        }

        private static void FillIrregularEllipse(VoxelCanvas canvas, int centerX, int y, int centerZ, float radiusX, float radiusZ, int color, System.Random rng, float wobble)
        {
            int minX = Mathf.FloorToInt(centerX - radiusX);
            int maxX = Mathf.CeilToInt(centerX + radiusX);
            int minZ = Mathf.FloorToInt(centerZ - radiusZ);
            int maxZ = Mathf.CeilToInt(centerZ + radiusZ);
            float offset = rng.Next(0, 10000) * 0.023f;

            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    float nx = (x - centerX) / Mathf.Max(0.01f, radiusX);
                    float nz = (z - centerZ) / Mathf.Max(0.01f, radiusZ);
                    float noise = Mathf.PerlinNoise((x + offset) * 0.27f, (z - offset) * 0.27f);
                    float threshold = 1f + (noise - 0.5f) * wobble;
                    if (nx * nx + nz * nz <= threshold)
                    {
                        canvas.Set(x, y, z, color);
                    }
                }
            }
        }

        private static void AddGableRoof(VoxelCanvas canvas, int minX, int maxX, int startY, int minZ, int maxZ, int color)
        {
            int layer = 0;
            while (minX + layer <= maxX - layer)
            {
                canvas.FillBox(minX + layer, maxX - layer, startY + layer, startY + layer, minZ, maxZ, color);
                layer++;
            }
        }

        private static void AddWindow(VoxelCanvas canvas, int centerX, int centerY, int z, int color)
        {
            canvas.FillBox(centerX - 1, centerX + 1, centerY - 1, centerY + 1, z, z, color);
        }

        private static void AddBattlements(VoxelCanvas canvas, int minX, int maxX, int y, int minZ, int maxZ, int color)
        {
            for (int x = minX; x <= maxX; x += 2)
            {
                canvas.FillBox(x, x, y, y, minZ, minZ, color);
                canvas.FillBox(x, x, y, y, maxZ, maxZ, color);
            }

            for (int z = minZ; z <= maxZ; z += 2)
            {
                canvas.FillBox(minX, minX, y, y, z, z, color);
                canvas.FillBox(maxX, maxX, y, y, z, z, color);
            }
        }

        private static void AddBattlementRing(VoxelCanvas canvas, int centerX, int centerZ, int y, int radius, int color)
        {
            for (int x = -radius; x <= radius; x++)
            {
                for (int z = -radius; z <= radius; z++)
                {
                    if (Mathf.Abs(x) == radius || Mathf.Abs(z) == radius)
                    {
                        if ((Mathf.Abs(x) + Mathf.Abs(z)) % 2 == 0)
                        {
                            canvas.Set(centerX + x, y, centerZ + z, color);
                        }
                    }
                }
            }
        }
    }
}
