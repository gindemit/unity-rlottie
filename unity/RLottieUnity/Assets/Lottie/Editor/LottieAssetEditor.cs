#if UNITY_EDITOR
using System.IO;
using LSCore;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using Path = System.IO.Path;

[CustomEditor(typeof(LottieScriptedImporter), true)]
public sealed class LottieScriptedImporterEditor : ScriptedImporterEditor
{
    private float speed = 1;
    private bool loop = true;

    private Lottie anim;
    private bool playing;
    private double lastUpdateTime;

    private BaseLottieAsset asset;
    private string assetPath;

    private SerializedProperty _rotationProp;
    private SerializedProperty _flipProp;

    public LSImage.RotationMode rotation
    {
        get => (LSImage.RotationMode)_rotationProp.enumValueIndex;
        set => _rotationProp.enumValueIndex = (int)value;
    }

    public Vector2Int flip
    {
        get => (Vector2Int)_flipProp.vector2IntValue;
        set => _flipProp.vector2IntValue = (Vector2Int)value;
    }

    public override void OnEnable()
    {
        base.OnEnable();
        assetPath   = ((AssetImporter)target).assetPath;
        asset = AssetDatabase.LoadAssetAtPath<BaseLottieAsset>(assetPath);
        playing = true;
        lastUpdateTime = EditorApplication.timeSinceStartup;

        _rotationProp = serializedObject.FindProperty("rotation");
        _flipProp = serializedObject.FindProperty("flip");
    }

    public override void OnDisable()
    {
        DisposeAnim();
        base.OnDisable();
    }

    protected override bool needsApplyRevert => false;
    protected override bool useAssetDrawPreview => false;
    public override bool showImportedObject  => false;
    public override bool HasPreviewGUI() => true;
    public override GUIContent GetPreviewTitle() => GUIContent.none;
    public override bool RequiresConstantRepaint() => playing;
    
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawFlip();
        DrawRotateButton();

        if (serializedObject.ApplyModifiedProperties())
        {
            ((AssetImporter)target).SaveAndReimport();
            asset = AssetDatabase.LoadAssetAtPath<BaseLottieAsset>(assetPath);
        }

        if (asset.IsCompressed)
        {
            if (GUILayout.Button("Decompress"))
            {
                var json = LottieCompressor.Decompress(File.ReadAllBytes(assetPath));
                var newPath = Path.ChangeExtension(assetPath, asset.DecompressedExtension);
                File.WriteAllText(newPath, json);
                AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.Refresh();
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<BaseLottieAsset>(newPath);
            }
        }
        else
        {
            if (GUILayout.Button("Compress"))
            {
                var bytes = LottieCompressor.Compress(File.ReadAllText(assetPath));
                var newPath = Path.ChangeExtension(assetPath, asset.CompressedExtension);
                File.WriteAllBytes(newPath, bytes);
                AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.Refresh();
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<BaseLottieAsset>(newPath);
            }
        }
    }
    
    private void DrawFlip()
    {
        GUILayout.Space(10);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Flip", GUILayout.ExpandWidth(true));

        var v = _flipProp.vector2IntValue;

        GUI.color = new Color(1, 1, 1, v.x == 1 ? 1f : 0.5f);
        if (GUILayout.Button(v.x == 1 ? "X ❤️" : "X", GUILayout.Height(30)))
            v.x = v.x * -1 + 1;

        GUI.color = new Color(1, 1, 1, v.y == 1 ? 1f : 0.5f);
        if (GUILayout.Button(v.y == 1 ? "Y ❤️" : "Y", GUILayout.Height(30)))
            v.y = v.y * -1 + 1;

        GUI.color = new Color(1, 1, 1, 1);
        _flipProp.vector2IntValue = v;

        GUILayout.EndHorizontal();
    }
    
    private void DrawRotateButton()
    {
        GUILayout.Space(10);
        GUILayout.BeginHorizontal();

        var cur = _rotationProp.enumValueIndex;
        for (var i = 0; i < 4; i++)
        {
            var targetAngle = i * 90;
            var text = cur == i ? $"{targetAngle}° ❤️" : $"{targetAngle}°";
            if (GUILayout.Button(text, GUILayout.Height(30)) && cur != i)
                _rotationProp.enumValueIndex = i;
        }

        GUILayout.EndHorizontal();
    }

    public override void OnPreviewGUI(Rect r, GUIStyle background)
    {
        if(Event.current.type != EventType.Repaint) return;
        
        EnsureAnimationForPreviewRect();
        EditorUpdate();
        if (toDraw == null)
        {
            EditorGUI.DropShadowLabel(r, "No texture from LottieAnimation.");
            return;
        }

        var angleDeg = (int)rotation * 90;
        var swap = angleDeg is 90 or 270;
        var sprite = anim.Spritee;
        
        var drawRect = r;
        if (swap)
        {
            drawRect = new Rect(0, 0, r.height, r.width);
            drawRect.center = r.center;
        }
        
        
        var aspect = sprite.Aspect;
        drawRect = GetScaleToFitRect(drawRect, aspect);
        var uvMin = sprite.UvMin;
        var uvMax = sprite.UvMax;
        
        var uv = Rect.MinMaxRect(uvMin.x, uvMin.y, uvMax.x, uvMax.y);
        var scale = new Vector2(flip.x == 1 ? -1 : 1, flip.y == 1 ? -1 : 1);
        var prev = GUI.matrix;
        GUIUtility.RotateAroundPivot(angleDeg, r.center);
        GUIUtility.ScaleAroundPivot(scale, r.center);
        GUI.DrawTextureWithTexCoords(drawRect, toDraw, uv, true);
        GUI.matrix = prev;
    }
    
    
    private static Rect GetScaleToFitRect(Rect container, float contentAspect)
    {
        if (contentAspect <= 0f) return container;

        float containerAspect = container.width / container.height;
        if (containerAspect > contentAspect)
        {
            float h = container.height;
            float w = h * contentAspect;
            float x = container.x + (container.width - w) * 0.5f;
            return new Rect(x, container.y, w, h);
        }
        else
        {
            float w = container.width;
            float h = w / contentAspect;
            float y = container.y + (container.height - h) * 0.5f;
            return new Rect(container.x, y, w, h);
        }
    }

    public override void OnPreviewSettings()
    {
        if (GUILayout.Button(playing ? "Pause" : "Play", EditorStyles.miniButton))
        {
            if (anim == null) EnsureAnimationForPreviewRect();
            playing = !playing;
            lastUpdateTime = EditorApplication.timeSinceStartup;
        }

        if (GUILayout.Button("Stop", EditorStyles.miniButton))
        {
            playing = false;
            anim?.DrawOneFrame(0);
            LottieUpdater.ForceApplyTexture();
        }
        
        loop = GUILayout.Toggle(loop, "Loop", EditorStyles.miniButton);
        
        GUILayout.Label("Speed", GUILayout.Width(40));
        speed = GUILayout.HorizontalSlider(speed, 0f, 4f, GUILayout.Width(100));
        
        if (anim != null)
        {
            anim.loop = loop;
            var total = Mathf.Max(1, (int)anim.TotalFramesCount);
            var cur = Mathf.Clamp((int)anim.currentFrame, 0, total - 1);

            GUILayout.Space(10);
            GUILayout.Label($"Frame {cur+1}/{total}", GUILayout.Width(110));
            var newFrame = Mathf.RoundToInt(GUILayout.HorizontalSlider(cur, 0, total - 1, GUILayout.Width(150)));
            if (newFrame != cur)
            {
                playing = false;
                anim.DrawOneFrame(newFrame);
                LottieUpdater.ForceApplyTexture();
            }
        }
    }

    private void EditorUpdate()
    {
        if (!playing || anim == null) return;

        var t = EditorApplication.timeSinceStartup;
        var delta = (float)(t - lastUpdateTime);
        lastUpdateTime = t;
        
        anim.UpdateDelta(delta * speed);
        LottieUpdater.ForceApplyTexture();
        if (!loop && anim.currentFrame >= anim.TotalFramesCount - 1)
            playing = false;

        Repaint();
    }
    
    private Texture2D toDraw;

    private void EnsureAnimationForPreviewRect()
    {
        if(anim != null) return;

        anim = new Lottie(
            asset,
            string.Empty,
            1024, false);
        
        var t = EditorApplication.timeSinceStartup;
        lastUpdateTime = t;
        anim.DrawOneFrame(0);
        anim.Spritee.TextureChanged += () =>
        {
            toDraw = anim.Spritee.Texture;
        };
        LottieUpdater.ForceApplyTexture();
        lastUpdateTime = EditorApplication.timeSinceStartup;
    }

    private void DisposeAnim()
    {
        if (anim != null) { anim.Destroy(); anim = null; }
    }
}
#endif
