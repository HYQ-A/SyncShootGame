using UnityEngine;
using Mirror;

/// <summary>
/// ============================================
/// 演示用调试开关面板（OnGUI 实现）
/// ============================================
/// 在屏幕左上角显示两个开关：
///   1. 客户端预测（Client Prediction）
///   2. 插值平滑（Interpolation）
///
/// 仅在客户端显示（服务端逻辑不受影响）。
/// 使用方式：挂载到场景中任意 GameObject 上即可。
/// </summary>
public class DebugToggleUI : MonoBehaviour
{
    // 面板样式缓存（OnGUI 中初始化一次）
    private GUIStyle toggleStyle;
    private GUIStyle labelStyle;
    private GUIStyle boxStyle;
    private bool stylesInitialized = false;

    void OnGUI()
    {
        // 仅客户端显示（纯服务端无需 UI）
        if (!NetworkClient.active) return;

        // 初始化样式（只执行一次，放大字体方便答辩演示）
        if (!stylesInitialized)
        {
            InitStyles();
            stylesInitialized = true;
        }

        // 面板区域：屏幕左上角
        float panelWidth = 280;
        float panelHeight = 120;
        float panelX = Screen.width - panelWidth - 10;
        float panelY = 10;
        Rect panelRect = new Rect(panelX, panelY, panelWidth, panelHeight);

        // 半透明背景
        GUI.Box(panelRect, "", boxStyle);

        GUILayout.BeginArea(new Rect(panelX + 10, panelY + 10, panelWidth - 20, panelHeight - 20));

        // 标题
        GUILayout.Label("--- 网络同步演示开关 ---", labelStyle);
        GUILayout.Space(5);

        // 开关1：客户端预测
        SyncPlayerController.EnableClientPrediction = GUILayout.Toggle(
            SyncPlayerController.EnableClientPrediction,
            SyncPlayerController.EnableClientPrediction ? " 客户端预测：已开启" : " 客户端预测：已关闭",
            toggleStyle
        );

        GUILayout.Space(3);

        // 开关2：插值平滑
        SyncPlayerController.EnableInterpolation = GUILayout.Toggle(
            SyncPlayerController.EnableInterpolation,
            SyncPlayerController.EnableInterpolation ? " 插值平滑：已开启" : " 插值平滑：已关闭",
            toggleStyle
        );

        GUILayout.EndArea();
    }

    /// <summary>
    /// 初始化 GUI 样式：放大字体 + 半透明背景
    /// </summary>
    void InitStyles()
    {
        // Toggle 样式
        toggleStyle = new GUIStyle(GUI.skin.toggle);
        toggleStyle.fontSize = 18;
        toggleStyle.normal.textColor = Color.white;
        toggleStyle.onNormal.textColor = Color.green;
        toggleStyle.padding = new RectOffset(25, 0, 0, 0);

        // 标题样式
        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 16;
        labelStyle.fontStyle = FontStyle.Bold;
        labelStyle.normal.textColor = Color.yellow;
        labelStyle.alignment = TextAnchor.MiddleCenter;

        // 背景框样式
        boxStyle = new GUIStyle(GUI.skin.box);
        Texture2D bgTex = new Texture2D(1, 1);
        bgTex.SetPixel(0, 0, new Color(0, 0, 0, 0.7f));
        bgTex.Apply();
        boxStyle.normal.background = bgTex;
    }
}
